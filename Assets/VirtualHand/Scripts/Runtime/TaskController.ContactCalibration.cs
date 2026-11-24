#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    public partial class TaskController
    {
        /// <summary>
        /// Editor-time fit from the actual weighted skin and box collider. It stores
        /// only a task pose blend and a bounded visual wrist correction in the scene.
        /// </summary>
        public void CalibrateTaskHoldFromGeometry()
        {
            _taskAssistChecked = false;
            if (!EnsureTaskAssistReady())
                throw new InvalidOperationException("Task hand calibration needs both native-profile hand drivers.");
            if (boxVisual == null || leftAnchor == null || rightAnchor == null)
                throw new InvalidOperationException("Task hand calibration needs the box renderer and both grip anchors.");
            var box = boxVisual.GetComponent<BoxCollider>();
            if (box == null) throw new InvalidOperationException("Task hand calibration needs the CarryBox BoxCollider.");

            leftTaskHoldCurl = CalibrateHand(leftHand, leftAnchor, box, isLeftHand: true,
                                             out leftTaskWristOffset, out leftTaskWristRotation, out leftOpenWristOffset);
            rightTaskHoldCurl = CalibrateHand(rightHand, rightAnchor, box, isLeftHand: false,
                                              out rightTaskWristOffset, out rightTaskWristRotation, out rightOpenWristOffset);
            BuildHoldControls(leftHand, leftTaskHoldCurl, _leftHoldControls);
            BuildHoldControls(rightHand, rightTaskHoldCurl, _rightHoldControls);
            leftHandoffFitValid = CalibrateOneHandHandoff(leftHand, leftAnchor, box, true, _leftHoldControls,
                leftTaskWristOffset, leftTaskWristRotation, out leftHandoffWristOffset, out leftHandoffWristRotation);
            rightHandoffFitValid = CalibrateOneHandHandoff(rightHand, rightAnchor, box, false, _rightHoldControls,
                rightTaskWristOffset, rightTaskWristRotation, out rightHandoffWristOffset, out rightHandoffWristRotation);
            Debug.Log("[TaskController] weighted-skin hold fit: left curl " + leftTaskHoldCurl.ToString("0.00") +
                      ", right curl " + rightTaskHoldCurl.ToString("0.00") + ".");
        }

        // Top-face grasp fitted to Elena's skinned fingers on the training cube. The
        // correction is expressed in box axes and mirrored about its center plane.
        // Validate the full posed mesh before storing the wrist in anchor space.
        bool CalibrateOneHandHandoff(HandRigDriver hand, Transform anchor, BoxCollider box, bool left,
            float[] controls, Vector3 bilateralOffset, Quaternion bilateralRotation,
            out Vector3 wristOffset, out Quaternion wristRotation)
        {
            wristOffset = bilateralOffset;
            wristRotation = bilateralRotation;
            Vector3 start = hand.transform.position;
            Quaternion rest = hand.transform.rotation;
            float side = left ? 1f : -1f;
            Quaternion inBox = new Quaternion(-0.311740f, -0.522428f * side, -0.681608f * side, 0.406567f).normalized;
            Quaternion correction = box.transform.rotation * inBox * Quaternion.Inverse(box.transform.rotation);
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 shift = box.transform.rotation * new Vector3(side * 0.150492f, 0.038031f, -0.044143f);
            Vector3 fromCenter = start + anchor.TransformVector(bilateralOffset) - center;
            try
            {
                hand.transform.SetPositionAndRotation(center + correction * fromCenter + shift, correction * rest * bilateralRotation);
                hand.SetControls(controls);
                var skin = CreateSkinCalibration(hand);
                if (skin == null) return false;
                var fingers = MeasureSkin(skin, box, left);
                if (!TryMeasureThumbPads(skin, box, out var pad)) return false;
                float tableClearance = fingers.lowestWorldY - box.bounds.min.y;
                bool fits = fingers.meanDistalFaceDistanceMm <= 15f && fingers.insideVertexFraction <= 0.04f &&
                    fingers.maxPenetrationMm <= 8f && tableClearance >= -0.002f && pad.topContactVertices >= 8;
                Debug.Log($"[HandoffFit] {hand.name} accepted={fits} topPatch={pad.topContactVertices} topGap={pad.closestTopGapMm:F2}mm fingerMean={fingers.meanDistalFaceDistanceMm:F2}mm depth={fingers.maxPenetrationMm:F2}mm table={tableClearance*1000f:F2}mm");
                if (!fits)
                {
                    Debug.LogWarning("Using the bilateral wrist pose for handoff on " + hand.name);
                    return false;
                }
                wristOffset = anchor.InverseTransformVector(hand.transform.position - start);
                wristRotation = Quaternion.Inverse(rest) * hand.transform.rotation;
                return true;
            }
            finally
            {
                hand.transform.SetPositionAndRotation(start, rest);
                hand.SetControls(hand.ProfileOpen);
            }
        }

        float CalibrateHand(HandRigDriver hand, Transform anchor, BoxCollider box, bool isLeftHand,
                            out Vector3 localOffset, out Quaternion localRotation, out Vector3 openOffset)
        {
            if (!hand.CacheBones()) throw new InvalidOperationException("Could not bind task hand: " + hand.name);
            var skin = CreateSkinCalibration(hand);
            if (skin == null) throw new InvalidOperationException("Task hand has no weighted skinned mesh: " + hand.name);
            Vector3 basePosition = hand.transform.position;
            Quaternion baseRotation = hand.transform.rotation;
            Vector3 inward = Vector3.ProjectOnPlane(box.transform.position - anchor.position, box.transform.up).normalized;
            if (inward.sqrMagnitude < 1e-8f) throw new InvalidOperationException("Task hand anchor lies on the box centerline.");

            var controls = new float[20];
            var open = hand.ProfileOpen;
            hand.SetControls(open);
            var atRest = MeasureSkin(skin, box, isLeftHand);
            openOffset = anchor.InverseTransformVector(-inward * (atRest.beyondSidePlaneMm + 4f) * 0.001f);
            var best = new HoldFit { score = float.PositiveInfinity };
            for (int a = 1; a <= 10; a++)
                for (int d = 0; d <= 8; d++)
                    for (int r = -1; r <= 1; r++)
                        ConsiderHoldFit(hand, skin, box, isLeftHand, basePosition, baseRotation, inward,
                                        a * 0.1f, d * 0.01f, r * 8f, controls, ref best);

            HoldFit coarse = best;
            for (int a = -2; a <= 2; a++)
                for (int d = -4; d <= 4; d++)
                    for (int r = -2; r <= 2; r++)
                        ConsiderHoldFit(hand, skin, box, isLeftHand, basePosition, baseRotation, inward,
                                        Mathf.Clamp(coarse.curl + a * 0.05f, 0.1f, 1f),
                                        Mathf.Clamp(coarse.offset + d * 0.0025f, 0f, 0.08f),
                                        Mathf.Clamp(coarse.angle + r * 2f, -8f, 8f), controls, ref best);

            hand.transform.SetPositionAndRotation(basePosition, baseRotation);
            hand.SetControls(open);
            localOffset = anchor.InverseTransformVector(inward * best.offset);
            Quaternion worldCorrection = Quaternion.AngleAxis(best.angle, box.transform.up);
            localRotation = Quaternion.Inverse(baseRotation) * worldCorrection * baseRotation;
            Debug.Log("[TaskController] " + hand.name + " hold fit: " +
                      best.geometry.meanDistalFaceDistanceMm.ToString("0.0") + " mm mean distal-face gap, " +
                      (best.geometry.insideVertexFraction * 100f).ToString("0.0") + "% vertices inside, " +
                      best.geometry.maxPenetrationMm.ToString("0.0") + " mm max penetration.");
            if (!best.feasible)
                throw new InvalidOperationException("No feasible task grip for " + hand.name);
            return best.curl;
        }

        struct HoldFit
        {
            public float curl, offset, angle, score;
            public bool feasible;
            public TaskHoldGeometry geometry;
        }

        void ConsiderHoldFit(HandRigDriver hand, SkinCalibration skin, BoxCollider box, bool isLeftHand,
                             Vector3 basePosition, Quaternion baseRotation, Vector3 inward,
                             float curl, float offset, float angle, float[] controls, ref HoldFit best)
        {
            for (int i = 0; i < 20; i++)
                controls[i] = Mathf.Lerp(hand.ProfileOpen[i], hand.ProfileFist[i], curl);
            hand.transform.SetPositionAndRotation(basePosition + inward * offset,
                Quaternion.AngleAxis(angle, box.transform.up) * baseRotation);
            hand.SetControls(controls);
            var geometry = MeasureSkin(skin, box, isLeftHand);
            float contactMeters = geometry.meanDistalFaceDistanceMm * 0.001f;
            float inside = geometry.insideVertexFraction;
            float penetrationMeters = geometry.maxPenetrationMm * 0.001f;
            var candidate = new HoldFit
            {
                curl = curl,
                offset = offset,
                angle = angle,
                geometry = geometry,
                feasible = inside <= 0.04f && penetrationMeters <= 0.008f && contactMeters <= 0.015f,
                score = contactMeters + inside * 0.5f + penetrationMeters * 0.5f,
            };
            if (candidate.feasible != best.feasible ? candidate.feasible : candidate.score < best.score)
                best = candidate;
        }

        sealed class SkinCalibration
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public BoneWeight[] weights;
            public Matrix4x4[] bindposes;
            public Transform[] bones;
            public Matrix4x4[] skinMatrices;
            public Matrix4x4[] normalMatrices;
            public int[] distalGroup;
            public Vector3[] thumbPadPoints, indexPadPoints;
            public Vector3[] thumbPadNormals, indexPadNormals;
            public int thumbPadCount, indexPadCount;
            public float[] indexNearestSquared;
            public bool[] indexContacts;
            public Vector3 thumbFlexSide, indexFlexSide;
            public Transform thumbDistal, thumbEnd, indexDistal, indexEnd;
        }

        SkinCalibration CreateSkinCalibration(HandRigDriver hand)
        {
            var renderer = hand.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null || renderer.sharedMesh == null) return null;
            var mesh = renderer.sharedMesh;
            var data = new SkinCalibration
            {
                vertices = mesh.vertices,
                normals = mesh.normals,
                weights = mesh.boneWeights,
                bindposes = mesh.bindposes,
                bones = renderer.bones,
            };
            if (data.vertices.Length == 0 || data.weights.Length != data.vertices.Length ||
                data.bones == null || data.bones.Length == 0 || data.bindposes.Length != data.bones.Length)
                return null;
            data.skinMatrices = new Matrix4x4[data.bones.Length];
            data.normalMatrices = new Matrix4x4[data.bones.Length];
            data.distalGroup = new int[data.vertices.Length];
            data.thumbPadPoints = new Vector3[data.vertices.Length];
            data.indexPadPoints = new Vector3[data.vertices.Length];
            data.thumbPadNormals = new Vector3[data.vertices.Length];
            data.indexPadNormals = new Vector3[data.vertices.Length];
            data.indexNearestSquared = new float[data.vertices.Length];
            data.indexContacts = new bool[data.vertices.Length];
            data.thumbDistal = FindSkinBone(data.bones, "thumb_03.");
            data.thumbEnd = FindSkinBone(data.bones, "thumb_03.R_end");
            data.indexDistal = FindSkinBone(data.bones, "index_03.");
            data.indexEnd = FindSkinBone(data.bones, "index_03.R_end");
            data.thumbFlexSide = NativeFlexSide(hand, data.thumbDistal, data.thumbEnd);
            data.indexFlexSide = NativeFlexSide(hand, data.indexDistal, data.indexEnd);
            for (int i = 0; i < data.distalGroup.Length; i++)
            {
                data.distalGroup[i] = -1;
                var weight = data.weights[i];
                if (weight.weight0 >= 0.5f && weight.boneIndex0 >= 0 && weight.boneIndex0 < data.bones.Length)
                    data.distalGroup[i] = DistalFingerGroup(data.bones[weight.boneIndex0].name);
            }
            return data;
        }

        static Transform FindSkinBone(Transform[] bones, string namePrefix)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && bones[i].name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                    return bones[i];
            return null;
        }

        // The side toward which the native distal joint bends is the pad side.
        // Preserve that anatomical reference when the hand moves or mirrors.
        static Vector3 NativeFlexSide(HandRigDriver hand, Transform distal, Transform endpoint)
        {
            if (distal == null || endpoint == null || hand.nativeProfile == null) return Vector3.zero;
            var bone = Array.Find(hand.nativeProfile.bones, b => b.name == distal.name);
            if (bone == null) return Vector3.zero;
            Vector3 axis = distal.InverseTransformPoint(endpoint.position).normalized;
            Quaternion flex = Quaternion.Inverse(bone.openLocal) * bone.closedLocal;
            return Vector3.ProjectOnPlane(flex * axis, axis).normalized;
        }

        static int DistalFingerGroup(string boneName)
        {
            if (boneName.StartsWith("thumb_03.", StringComparison.OrdinalIgnoreCase)) return 4;
            if (boneName.StartsWith("index_03.", StringComparison.OrdinalIgnoreCase)) return 0;
            if (boneName.StartsWith("middle_03.", StringComparison.OrdinalIgnoreCase)) return 1;
            if (boneName.StartsWith("ring_03.", StringComparison.OrdinalIgnoreCase)) return 2;
            if (boneName.StartsWith("pinky_03.", StringComparison.OrdinalIgnoreCase)) return 3;
            return -1;
        }

        static TaskHoldGeometry MeasureSkin(SkinCalibration skin, BoxCollider box, bool isLeftHand)
        {
            for (int b = 0; b < skin.bones.Length; b++)
                skin.skinMatrices[b] = skin.bones[b].localToWorldMatrix * skin.bindposes[b];
            Matrix4x4 worldToBox = box.transform.worldToLocalMatrix;
            Vector3 half = box.size * 0.5f;
            Vector3 scale = box.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float faceX = isLeftHand ? half.x : -half.x;
            float min0 = float.PositiveInfinity, min1 = float.PositiveInfinity;
            float min2 = float.PositiveInfinity, min3 = float.PositiveInfinity;
            int insideCount = 0;
            float lowestY = float.PositiveInfinity;
            float maxDepth = 0f, beyondSidePlane = 0f;
            for (int i = 0; i < skin.vertices.Length; i++)
            {
                BoneWeight w = skin.weights[i];
                Vector3 v = skin.vertices[i];
                Vector3 world = skin.skinMatrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                                skin.skinMatrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                                skin.skinMatrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                                skin.skinMatrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                lowestY = Mathf.Min(lowestY, world.y);
                Vector3 p = worldToBox.MultiplyPoint3x4(world) - box.center;
                float px = half.x - Mathf.Abs(p.x);
                float py = half.y - Mathf.Abs(p.y);
                float pz = half.z - Mathf.Abs(p.z);
                if (px >= 0f && py >= 0f && pz >= 0f)
                {
                    insideCount++;
                    maxDepth = Mathf.Max(maxDepth, Mathf.Min(px * scale.x, Mathf.Min(py * scale.y, pz * scale.z)));
                }
                float dx = p.x - faceX;
                beyondSidePlane = Mathf.Max(beyondSidePlane, (isLeftHand ? -dx : dx) * scale.x);
                float dy = p.y - Mathf.Clamp(p.y, -half.y, half.y);
                float dz = p.z - Mathf.Clamp(p.z, -half.z, half.z);
                float distance = new Vector3(dx * scale.x, dy * scale.y, dz * scale.z).magnitude;
                switch (skin.distalGroup[i])
                {
                    case 0: min0 = Mathf.Min(min0, distance); break;
                    case 1: min1 = Mathf.Min(min1, distance); break;
                    case 2: min2 = Mathf.Min(min2, distance); break;
                    case 3: min3 = Mathf.Min(min3, distance); break;
                }
            }
            const float missingGroupDistance = 0.12f;
            if (float.IsPositiveInfinity(min0)) min0 = missingGroupDistance;
            if (float.IsPositiveInfinity(min1)) min1 = missingGroupDistance;
            if (float.IsPositiveInfinity(min2)) min2 = missingGroupDistance;
            if (float.IsPositiveInfinity(min3)) min3 = missingGroupDistance;
            return new TaskHoldGeometry
            {
                meanDistalFaceDistanceMm = (min0 + min1 + min2 + min3) * 250f,
                insideVertexFraction = insideCount / (float)skin.vertices.Length,
                maxPenetrationMm = maxDepth * 1000f,
                lowestWorldY = lowestY,
                beyondSidePlaneMm = beyondSidePlane * 1000f,
                indexFaceDistanceMm = min0 * 1000f,
                middleFaceDistanceMm = min1 * 1000f,
            };
        }

        bool TryMeasureThumbPads(SkinCalibration skin, BoxCollider box, out ThumbPadGeometry geometry)
        {
            geometry = default;
            if (skin.normals == null || skin.normals.Length != skin.vertices.Length ||
                skin.thumbFlexSide.sqrMagnitude < 0.5f || skin.indexFlexSide.sqrMagnitude < 0.5f)
                return false;

            Vector3 thumbVolar = skin.thumbDistal.TransformVector(skin.thumbFlexSide).normalized;
            Vector3 indexVolar = skin.indexDistal.TransformVector(skin.indexFlexSide).normalized;
            Vector3 thumbAxis = skin.thumbEnd.position - skin.thumbDistal.position;
            Vector3 indexAxis = skin.indexEnd.position - skin.indexDistal.position;

            for (int b = 0; b < skin.bones.Length; b++)
            {
                skin.skinMatrices[b] = skin.bones[b].localToWorldMatrix * skin.bindposes[b];
                skin.normalMatrices[b] = skin.skinMatrices[b].inverse.transpose;
            }
            skin.thumbPadCount = skin.indexPadCount = 0;
            Matrix4x4 worldToBox = box.transform.worldToLocalMatrix;
            Vector3 half = box.size * 0.5f;
            int topContacts = 0;
            float nearestTop = float.PositiveInfinity;
            Vector3 scale = box.transform.lossyScale;
            Vector3 topNormal = box.transform.up;
            int adjacentFace = -1;
            float adjacentScore = float.PositiveInfinity;
            float adjacentGap = float.PositiveInfinity;
            float adjacentFacing = -1f;

            for (int i = 0; i < skin.vertices.Length; i++)
            {
                int group = skin.distalGroup[i];
                if (group != 4 && group != 0) continue;
                BoneWeight w = skin.weights[i];
                if (w.weight0 < 0.5f) continue;
                Vector3 v = skin.vertices[i];
                Vector3 point = skin.skinMatrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                                skin.skinMatrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                                skin.skinMatrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                                skin.skinMatrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                Vector3 normal = skin.normalMatrices[w.boneIndex0].MultiplyVector(skin.normals[i]) * w.weight0 +
                                 skin.normalMatrices[w.boneIndex1].MultiplyVector(skin.normals[i]) * w.weight1 +
                                 skin.normalMatrices[w.boneIndex2].MultiplyVector(skin.normals[i]) * w.weight2 +
                                 skin.normalMatrices[w.boneIndex3].MultiplyVector(skin.normals[i]) * w.weight3;
                if (normal.sqrMagnitude < 1e-8f) continue;
                normal.Normalize();
                if (group == 4 && Vector3.Dot(normal, thumbVolar) >= 0.55f &&
                    Vector3.Dot(point - skin.thumbDistal.position, thumbAxis) >= 0.40f * thumbAxis.sqrMagnitude)
                {
                    skin.thumbPadPoints[skin.thumbPadCount] = point;
                    skin.thumbPadNormals[skin.thumbPadCount++] = normal;
                    Vector3 local = worldToBox.MultiplyPoint3x4(point) - box.center;
                    // Only the interior of the top face supports the thumb pad.
                    if ((half.x - Mathf.Abs(local.x)) * Mathf.Abs(scale.x) >= 0.006f &&
                        (half.z - Mathf.Abs(local.z)) * Mathf.Abs(scale.z) >= 0.004f &&
                        Vector3.Dot(normal, -topNormal) >= 0.35f)
                    {
                        float gap = Mathf.Abs((local.y - half.y) * scale.y);
                        nearestTop = Mathf.Min(nearestTop, gap);
                        if (gap <= 0.003f) topContacts++;
                    }
                    MeasureAdjacentFaces(point, normal, box, worldToBox, half,
                                         ref adjacentScore, ref adjacentGap, ref adjacentFacing, ref adjacentFace);
                }
                else if (group == 0 && Vector3.Dot(normal, indexVolar) >= 0.55f &&
                         Vector3.Dot(point - skin.indexDistal.position, indexAxis) >= 0.40f * indexAxis.sqrMagnitude)
                {
                    skin.indexPadPoints[skin.indexPadCount] = point;
                    skin.indexPadNormals[skin.indexPadCount++] = normal;
                }
            }
            if (skin.thumbPadCount < 6 || skin.indexPadCount < 6 || adjacentFace < 0) return false;

            float closestSquared = float.PositiveInfinity;
            float closestOpposition = -1f;
            float thumbNearestSum = 0f;
            float maxOverlap = 0f;
            int thumbContacts = 0, indexContacts = 0;
            for (int i = 0; i < skin.indexPadCount; i++)
            {
                skin.indexNearestSquared[i] = float.PositiveInfinity;
                skin.indexContacts[i] = false;
            }
            for (int t = 0; t < skin.thumbPadCount; t++)
            {
                float nearestSquared = float.PositiveInfinity;
                int nearest = 0;
                bool touches = false;
                for (int i = 0; i < skin.indexPadCount; i++)
                {
                    Vector3 offset = skin.indexPadPoints[i] - skin.thumbPadPoints[t];
                    float sq = offset.sqrMagnitude;
                    if (sq < nearestSquared) { nearestSquared = sq; nearest = i; }
                    skin.indexNearestSquared[i] = Mathf.Min(skin.indexNearestSquared[i], sq);
                    if (sq > 0.003f * 0.003f && sq >= closestSquared) continue;
                    float opposition = -Vector3.Dot(skin.thumbPadNormals[t], skin.indexPadNormals[i]);
                    if (sq < closestSquared) { closestSquared = sq; closestOpposition = opposition; }
                    if (sq > 0.003f * 0.003f || opposition < 0.55f) continue;
                    if (Vector3.Dot(offset, skin.thumbPadNormals[t]) < -0.0003f ||
                        Vector3.Dot(offset, skin.indexPadNormals[i]) > 0.0003f) continue;
                    touches = true;
                    skin.indexContacts[i] = true;
                }
                if (touches) thumbContacts++;
                thumbNearestSum += Mathf.Sqrt(nearestSquared);
                Vector3 difference = skin.thumbPadPoints[t] - skin.indexPadPoints[nearest];
                if (nearestSquared < 0.012f * 0.012f &&
                    Vector3.Dot(skin.thumbPadNormals[t], skin.indexPadNormals[nearest]) < -0.55f)
                    maxOverlap = Mathf.Max(maxOverlap, -Vector3.Dot(difference, skin.indexPadNormals[nearest]));
            }
            float indexNearestSum = 0f;
            for (int i = 0; i < skin.indexPadCount; i++)
            {
                indexNearestSum += Mathf.Sqrt(skin.indexNearestSquared[i]);
                if (skin.indexContacts[i]) indexContacts++;
            }
            geometry = new ThumbPadGeometry
            {
                thumbPadVertexCount = skin.thumbPadCount,
                indexPadVertexCount = skin.indexPadCount,
                closestPadGapMm = Mathf.Sqrt(closestSquared) * 1000f,
                meanNearestPadGapMm = (thumbNearestSum / skin.thumbPadCount +
                                       indexNearestSum / skin.indexPadCount) * 500f,
                opposingPadNormals = closestOpposition,
                thumbContactVertices = thumbContacts,
                indexContactVertices = indexContacts,
                maxPadOverlapMm = maxOverlap * 1000f,
                adjacentFaceGapMm = adjacentGap * 1000f,
                adjacentFaceFacing = adjacentFacing,
                adjacentFace = adjacentFace,
                topContactVertices = topContacts,
                closestTopGapMm = nearestTop * 1000f,
            };
            return true;
        }

        static void MeasureAdjacentFaces(Vector3 worldPoint, Vector3 worldNormal, BoxCollider box,
                                         Matrix4x4 worldToBox, Vector3 half, ref float bestScore,
                                         ref float bestGap, ref float bestFacing, ref int bestFace)
        {
            Vector3 p = worldToBox.MultiplyPoint3x4(worldPoint) - box.center;
            for (int face = 0; face < 4; face++)
            {
                Vector3 localNormal, closest;
                if (face == 0)
                {
                    localNormal = Vector3.up;
                    closest = new Vector3(Mathf.Clamp(p.x, -half.x, half.x), half.y, Mathf.Clamp(p.z, -half.z, half.z));
                }
                else if (face == 1)
                {
                    localNormal = Vector3.down;
                    closest = new Vector3(Mathf.Clamp(p.x, -half.x, half.x), -half.y, Mathf.Clamp(p.z, -half.z, half.z));
                }
                else if (face == 2)
                {
                    localNormal = Vector3.forward;
                    closest = new Vector3(Mathf.Clamp(p.x, -half.x, half.x), Mathf.Clamp(p.y, -half.y, half.y), half.z);
                }
                else
                {
                    localNormal = Vector3.back;
                    closest = new Vector3(Mathf.Clamp(p.x, -half.x, half.x), Mathf.Clamp(p.y, -half.y, half.y), -half.z);
                }
                Vector3 faceWorld = box.transform.TransformPoint(box.center + closest);
                float gap = Vector3.Distance(worldPoint, faceWorld);
                Vector3 towardHand = -box.transform.TransformDirection(localNormal).normalized;
                float facing = Vector3.Dot(worldNormal, towardHand);
                float score = gap + Mathf.Max(0f, 0.5f - facing) * 0.01f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestGap = gap;
                bestFacing = facing;
                bestFace = face;
            }
        }

        public bool TryMeasureThumbPadContact(bool isLeftHand, out ThumbPadGeometry geometry)
        {
            geometry = default;
            var hand = isLeftHand ? leftHand : rightHand;
            var box = boxVisual != null ? boxVisual.GetComponent<BoxCollider>() : null;
            if (hand == null || box == null) return false;
            var skin = CreateSkinCalibration(hand);
            return skin != null && TryMeasureThumbPads(skin, box, out geometry);
        }

        public bool TryMeasureTaskHold(bool isLeftHand, out TaskHoldGeometry geometry)
        {
            geometry = default;
            var hand = isLeftHand ? leftHand : rightHand;
            var box = boxVisual != null ? boxVisual.GetComponent<BoxCollider>() : null;
            if (hand == null || box == null) return false;
            var skin = CreateSkinCalibration(hand);
            if (skin == null) return false;
            geometry = MeasureSkin(skin, box, isLeftHand);
            return true;
        }

    }
}
#endif
