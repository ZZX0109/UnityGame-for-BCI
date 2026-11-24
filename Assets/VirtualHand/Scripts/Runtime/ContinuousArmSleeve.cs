using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Builds a single open cloth tube from SeatedArmPreview's shoulder, elbow, and wrist outputs.
    /// Its proximal end sits inside the torso and its distal end overlaps the existing wrist cuff.
    /// This component never moves or writes to a tracked wrist or hand transform.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class ContinuousArmSleeve : MonoBehaviour
    {
        private const int RadialSegments = 32;
        private const int ShoulderRootSegments = 6;
        private const int UpperArmSegments = 18;
        private const int LowerArmSegments = 24;
        private const int RingCount = ShoulderRootSegments + UpperArmSegments + LowerArmSegments + 1;
        private const string WovenMaterialName = "First Person Woven Cuff";

        [Header("Preview and mesh mount")]
        public SeatedArmPreview preview;
        [Tooltip("Optional mesh-space root. If unset, the preview body origin is used, then this transform.")]
        public Transform mountRoot;
        [Tooltip("Assign the generated FirstPersonWristCuff material. If empty, it is found on the pair's hand renderers.")]
        public Material weaveMaterial;
        public bool renderLeft = true;
        public bool renderRight = true;

        [Header("Overlap (metres)")]
        [Tooltip("Extends the shoulder opening back and inward beneath the torso.")]
        public float shoulderRootInsetBackMeters = 0.075f;
        public float shoulderRootInsetXMeters = 0.04f;
        [Tooltip("Ends the sleeve beneath the existing forearm cuff so the two garments overlap.")]
        public float wristCuffOverlapMeters = 0.06f;
        [Range(0.45f, 1.2f)]
        public float crossSectionDepthScale = 0.88f;

        [Header("Left cross-section X radii (metres)")]
        public float leftShoulderRadiusXMeters = 0.060f;
        public float leftElbowRadiusXMeters = 0.046f;
        public float leftWristRadiusXMeters = 0.039f;

        [Header("Right cross-section X radii (metres)")]
        public float rightShoulderRadiusXMeters = 0.060f;
        public float rightElbowRadiusXMeters = 0.046f;
        public float rightWristRadiusXMeters = 0.039f;

        private sealed class ArmMesh
        {
            public GameObject gameObject;
            public Mesh mesh;
            public MeshFilter filter;
            public MeshRenderer renderer;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uv;
            public int[] triangles;
            public Vector3[] centers;
            public Vector3[] tangents;
            public Vector3[] lateral;
            public Vector3[] depth;
            public float[] radiiX;
            public float[] radiiDepth;
            public bool reversedWinding;
        }

        private readonly ArmMesh left = new ArmMesh();
        private readonly ArmMesh right = new ArmMesh();
        private ControllerHandPair searchedPair;

        public Mesh LeftMesh { get { return left.mesh; } }
        public Mesh RightMesh { get { return right.mesh; } }
        public Renderer LeftRenderer { get { return left.renderer; } }
        public Renderer RightRenderer { get { return right.renderer; } }

        private void OnEnable()
        {
            Refresh();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        private void OnDisable()
        {
            SetVisible(left, false);
            SetVisible(right, false);
        }

        private void OnDestroy()
        {
            Dispose(left);
            Dispose(right);
        }

        /// <summary>Updates the existing mesh buffers from the preview's current arm states.</summary>
        public void Refresh()
        {
            Transform mount = ResolveMountRoot();
            if (!mount)
            {
                SetVisible(left, false);
                SetVisible(right, false);
                return;
            }

            EnsureArmMesh(left, "Continuous arm sleeve L", mount);
            EnsureArmMesh(right, "Continuous arm sleeve R", mount);
            ResolveWeaveMaterial();
            ApplyMaterial(left);
            ApplyMaterial(right);
            UpdateWinding(left, mount);
            UpdateWinding(right, mount);

            if (!preview)
            {
                SetVisible(left, false);
                SetVisible(right, false);
                return;
            }

            RefreshArm(left, preview.LeftArm, preview.leftShoulderPoint, preview.leftElbowPoint,
                preview.leftWristPoint, true, renderLeft, mount);
            RefreshArm(right, preview.RightArm, preview.rightShoulderPoint, preview.rightElbowPoint,
                preview.rightWristPoint, false, renderRight, mount);
        }

        private Transform ResolveMountRoot()
        {
            if (mountRoot) return mountRoot;
            if (preview && preview.bodyOrigin) return preview.bodyOrigin;
            return transform;
        }

        private void EnsureArmMesh(ArmMesh data, string objectName, Transform mount)
        {
            if (!data.gameObject)
            {
                data.gameObject = new GameObject(objectName);
                data.gameObject.layer = gameObject.layer;
                data.gameObject.transform.SetParent(mount, false);
                data.filter = data.gameObject.AddComponent<MeshFilter>();
                data.renderer = data.gameObject.AddComponent<MeshRenderer>();
                data.renderer.shadowCastingMode = ShadowCastingMode.On;
                data.renderer.receiveShadows = true;
                data.renderer.lightProbeUsage = LightProbeUsage.BlendProbes;

                int vertexCount = RingCount * RadialSegments;
                data.vertices = new Vector3[vertexCount];
                data.normals = new Vector3[vertexCount];
                data.uv = new Vector2[vertexCount];
                data.triangles = new int[(RingCount - 1) * RadialSegments * 6];
                data.centers = new Vector3[RingCount];
                data.tangents = new Vector3[RingCount];
                data.lateral = new Vector3[RingCount];
                data.depth = new Vector3[RingCount];
                data.radiiX = new float[RingCount];
                data.radiiDepth = new float[RingCount];
                BuildTopology(data);

                data.mesh = new Mesh { name = objectName + " mesh", indexFormat = IndexFormat.UInt16 };
                data.mesh.MarkDynamic();
                data.mesh.vertices = data.vertices;
                data.mesh.normals = data.normals;
                data.mesh.uv = data.uv;
                data.mesh.subMeshCount = 1;
                data.mesh.SetTriangles(data.triangles, 0, false);
                data.mesh.bounds = new Bounds(Vector3.zero, Vector3.zero);
                data.filter.sharedMesh = data.mesh;
                data.reversedWinding = false;
            }
            else if (data.gameObject.transform.parent != mount)
            {
                data.gameObject.transform.SetParent(mount, false);
            }
        }

        private static void BuildTopology(ArmMesh data)
        {
            for (int ring = 0; ring < RingCount; ring++)
            {
                float v = (float)ring / (RingCount - 1);
                int row = ring * RadialSegments;
                for (int segment = 0; segment < RadialSegments; segment++)
                    data.uv[row + segment] = new Vector2((float)segment / RadialSegments, v);
            }

            int index = 0;
            for (int ring = 0; ring < RingCount - 1; ring++)
            {
                int row = ring * RadialSegments;
                int nextRow = row + RadialSegments;
                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    int nextSegment = (segment + 1) % RadialSegments;
                    int a = row + segment;
                    int b = row + nextSegment;
                    int c = nextRow + nextSegment;
                    int d = nextRow + segment;
                    data.triangles[index++] = a;
                    data.triangles[index++] = b;
                    data.triangles[index++] = c;
                    data.triangles[index++] = a;
                    data.triangles[index++] = c;
                    data.triangles[index++] = d;
                }
            }
        }

        private void ResolveWeaveMaterial()
        {
            if (weaveMaterial || !preview || !preview.pair || searchedPair == preview.pair)
                return;

            searchedPair = preview.pair;
            weaveMaterial = FindWovenMaterial(preview.pair.leftWrist);
            if (!weaveMaterial)
                weaveMaterial = FindWovenMaterial(preview.pair.rightWrist);
        }

        private static Material FindWovenMaterial(FirstPersonWrist wrist)
        {
            if (!wrist) return null;
            Renderer[] renderers = wrist.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i] ? renderers[i].sharedMaterial : null;
                if (material && material.name.IndexOf(WovenMaterialName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return material;
            }
            return null;
        }

        private void ApplyMaterial(ArmMesh data)
        {
            if (data.renderer && data.renderer.sharedMaterial != weaveMaterial)
                data.renderer.sharedMaterial = weaveMaterial;
        }

        private static void UpdateWinding(ArmMesh data, Transform mount)
        {
            if (!data.mesh) return;
            bool shouldReverse = mount.localToWorldMatrix.determinant < 0f;
            if (shouldReverse == data.reversedWinding) return;

            for (int i = 0; i < data.triangles.Length; i += 3)
            {
                int swap = data.triangles[i + 1];
                data.triangles[i + 1] = data.triangles[i + 2];
                data.triangles[i + 2] = swap;
            }
            data.mesh.SetTriangles(data.triangles, 0, false);
            data.reversedWinding = shouldReverse;
        }

        private void RefreshArm(ArmMesh data, SeatedArmPreview.ArmState state,
            Transform shoulderPoint, Transform elbowPoint, Transform wristPoint,
            bool isLeft, bool enabledForSide, Transform mount)
        {
            SetVisible(data, false);
            if (!enabledForSide || !state.visible || !data.mesh || !weaveMaterial)
                return;

            Transform bodyOrigin = preview.bodyOrigin;
            if (!TryGetWorldPoint(shoulderPoint, bodyOrigin, state.shoulderPositionBodyMeters, out Vector3 shoulder) ||
                !TryGetWorldPoint(elbowPoint, bodyOrigin, state.elbowPositionBodyMeters, out Vector3 elbow) ||
                !TryGetWorldPoint(wristPoint, bodyOrigin, state.wristPositionBodyMeters, out Vector3 wrist))
                return;

            if (!UpdateGeometry(data, shoulder, elbow, wrist, isLeft, bodyOrigin, mount))
                return;
            SetVisible(data, true);
        }

        private static bool TryGetWorldPoint(Transform output, Transform bodyOrigin,
            Vector3 bodyPositionMeters, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (output)
            {
                worldPosition = output.position;
                return IsFinite(worldPosition);
            }
            if (!bodyOrigin || !IsFinite(bodyPositionMeters))
                return false;

            worldPosition = bodyOrigin.position + bodyOrigin.rotation * bodyPositionMeters;
            return IsFinite(worldPosition);
        }

        private bool UpdateGeometry(ArmMesh data, Vector3 shoulder, Vector3 elbow, Vector3 wrist,
            bool isLeft, Transform bodyOrigin, Transform mount)
        {
            Vector3 upperDelta = elbow - shoulder;
            Vector3 lowerDelta = wrist - elbow;
            float upperLength = upperDelta.magnitude;
            float lowerLength = lowerDelta.magnitude;
            if (!IsFinite(upperLength) || !IsFinite(lowerLength) || upperLength < 0.01f || lowerLength < 0.01f)
                return false;

            Vector3 upperDirection = upperDelta / upperLength;
            Vector3 lowerDirection = lowerDelta / lowerLength;
            Vector3 jointDirection;
            if (!TryNormalize(upperDirection + lowerDirection, out jointDirection))
                jointDirection = upperDirection;

            Vector3 bodyRight = bodyOrigin ? bodyOrigin.right : mount.right;
            Vector3 bodyUp = bodyOrigin ? bodyOrigin.up : mount.up;
            Vector3 bodyForward = bodyOrigin ? bodyOrigin.forward : mount.forward;
            float inwardSign = isLeft ? 1f : -1f;
            Vector3 rootPoint = shoulder
                - bodyForward * Mathf.Max(0f, shoulderRootInsetBackMeters)
                + bodyRight * (inwardSign * Mathf.Max(0f, shoulderRootInsetXMeters));

            float rootLength = Vector3.Distance(rootPoint, shoulder);
            if (rootLength < 0.005f)
            {
                rootPoint = shoulder - upperDirection * 0.04f;
                rootLength = 0.04f;
            }

            float overlap = Mathf.Clamp(wristCuffOverlapMeters, 0f, lowerLength * 0.35f);
            Vector3 cuffEnd = wrist - lowerDirection * overlap;
            float lowerPathLength = Vector3.Distance(elbow, cuffEnd);
            if (lowerPathLength < 0.01f)
                return false;

            float shoulderRadius = isLeft ? leftShoulderRadiusXMeters : rightShoulderRadiusXMeters;
            float elbowRadius = isLeft ? leftElbowRadiusXMeters : rightElbowRadiusXMeters;
            float wristRadius = isLeft ? leftWristRadiusXMeters : rightWristRadiusXMeters;
            shoulderRadius = Mathf.Max(0.005f, shoulderRadius);
            elbowRadius = Mathf.Max(0.005f, elbowRadius);
            wristRadius = Mathf.Max(0.005f, wristRadius) * 1.04f;
            float depthScale = Mathf.Clamp(crossSectionDepthScale, 0.45f, 1.2f);

            float rootHandle = Mathf.Min(rootLength, upperLength) * 0.34f;
            Vector3 rootDirection = (shoulder - rootPoint) / rootLength;
            Vector3 rootP1 = rootPoint + rootDirection * (rootLength * 0.34f);
            Vector3 rootP2 = shoulder - upperDirection * rootHandle;
            float jointHandle = Mathf.Min(upperLength, lowerPathLength) * 0.34f;

            Vector3 upperP1 = shoulder + upperDirection * (upperLength * 0.34f);
            Vector3 upperP2 = elbow - jointDirection * jointHandle;
            Vector3 lowerP1 = elbow + jointDirection * jointHandle;
            Vector3 lowerP2 = cuffEnd - lowerDirection * (lowerPathLength * 0.34f);

            Vector3 previousLateral = Vector3.zero;
            bool hasPreviousLateral = false;
            for (int step = 0; step <= ShoulderRootSegments; step++)
            {
                float t = (float)step / ShoulderRootSegments;
                EvaluateBezier(rootPoint, rootP1, rootP2, shoulder, t, out Vector3 center, out Vector3 tangent);
                float radius = SmoothRadius(shoulderRadius * 1.16f, shoulderRadius, t, 0f);
                if (!WriteRing(data, step, center, tangent, radius, depthScale,
                    bodyRight, bodyUp, ref previousLateral, ref hasPreviousLateral)) return false;
            }

            int upperStartRing = ShoulderRootSegments;
            for (int step = 1; step <= UpperArmSegments; step++)
            {
                float t = (float)step / UpperArmSegments;
                EvaluateBezier(shoulder, upperP1, upperP2, elbow, t, out Vector3 center, out Vector3 tangent);
                float radius = SmoothRadius(shoulderRadius, elbowRadius, t, 0.075f);
                if (!WriteRing(data, upperStartRing + step, center, tangent, radius, depthScale,
                    bodyRight, bodyUp, ref previousLateral, ref hasPreviousLateral)) return false;
            }

            int lowerStartRing = ShoulderRootSegments + UpperArmSegments;
            for (int step = 1; step <= LowerArmSegments; step++)
            {
                float t = (float)step / LowerArmSegments;
                EvaluateBezier(elbow, lowerP1, lowerP2, cuffEnd, t, out Vector3 center, out Vector3 tangent);
                float radius = SmoothRadius(elbowRadius, wristRadius, t, 0.09f);
                if (!WriteRing(data, lowerStartRing + step, center, tangent, radius, depthScale,
                    bodyRight, bodyUp, ref previousLateral, ref hasPreviousLateral)) return false;
            }

            return WriteMeshBuffers(data, mount);
        }

        private static float SmoothRadius(float from, float to, float t, float bulge)
        {
            float eased = t * t * (3f - 2f * t);
            return Mathf.Max(0.005f, Mathf.Lerp(from, to, eased) * (1f + bulge * Mathf.Sin(Mathf.PI * t)));
        }

        private static void EvaluateBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t,
            out Vector3 position, out Vector3 tangent)
        {
            float oneMinusT = 1f - t;
            float oneMinusTSquared = oneMinusT * oneMinusT;
            float tSquared = t * t;
            position = oneMinusTSquared * oneMinusT * p0
                + 3f * oneMinusTSquared * t * p1
                + 3f * oneMinusT * tSquared * p2
                + tSquared * t * p3;
            Vector3 derivative = 3f * oneMinusTSquared * (p1 - p0)
                + 6f * oneMinusT * t * (p2 - p1)
                + 3f * tSquared * (p3 - p2);
            if (!TryNormalize(derivative, out tangent) && !TryNormalize(p3 - p0, out tangent))
                tangent = Vector3.forward;
        }

        private static bool WriteRing(ArmMesh data, int ring, Vector3 center, Vector3 tangent,
            float radiusX, float depthScale, Vector3 bodyRight, Vector3 bodyUp,
            ref Vector3 previousLateral, ref bool hasPreviousLateral)
        {
            Vector3 lateral = Vector3.ProjectOnPlane(bodyRight, tangent);
            if (!TryNormalize(lateral, out lateral))
                lateral = Vector3.ProjectOnPlane(bodyUp, tangent);
            if (!TryNormalize(lateral, out lateral))
                return false;
            if (hasPreviousLateral && Vector3.Dot(lateral, previousLateral) < 0f)
                lateral = -lateral;

            Vector3 depth = Vector3.Cross(tangent, lateral);
            if (!TryNormalize(depth, out depth))
                return false;

            data.centers[ring] = center;
            data.tangents[ring] = tangent;
            data.lateral[ring] = lateral;
            data.depth[ring] = depth;
            data.radiiX[ring] = radiusX;
            data.radiiDepth[ring] = radiusX * depthScale;
            previousLateral = lateral;
            hasPreviousLateral = true;
            return true;
        }

        private static bool WriteMeshBuffers(ArmMesh data, Transform mount)
        {
            Matrix4x4 worldToLocal = mount.worldToLocalMatrix;
            Matrix4x4 normalToLocal = mount.localToWorldMatrix.transpose;
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

            for (int ring = 0; ring < RingCount; ring++)
            {
                float radiusX = data.radiiX[ring];
                float radiusDepth = data.radiiDepth[ring];
                Vector3 center = data.centers[ring];
                Vector3 lateral = data.lateral[ring];
                Vector3 depth = data.depth[ring];
                int row = ring * RadialSegments;
                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / RadialSegments;
                    float cosine = Mathf.Cos(angle);
                    float sine = Mathf.Sin(angle);
                    Vector3 worldVertex = center + lateral * (cosine * radiusX) + depth * (sine * radiusDepth);
                    Vector3 localVertex = worldToLocal.MultiplyPoint3x4(worldVertex);
                    if (!IsFinite(localVertex)) return false;
                    int index = row + segment;
                    data.vertices[index] = localVertex;
                    minimum = Vector3.Min(minimum, localVertex);
                    maximum = Vector3.Max(maximum, localVertex);
                }
            }

            for (int ring = 0; ring < RingCount; ring++)
            {
                int previousRing = Mathf.Max(0, ring - 1);
                int nextRing = Mathf.Min(RingCount - 1, ring + 1);
                float centerDistance = Vector3.Distance(data.centers[previousRing], data.centers[nextRing]);
                float radiusDerivative = centerDistance > 0.00001f
                    ? (data.radiiX[nextRing] - data.radiiX[previousRing]) / centerDistance
                    : 0f;
                float radiusX = data.radiiX[ring];
                float radiusDepth = data.radiiDepth[ring];
                Vector3 tangent = data.tangents[ring];
                Vector3 lateral = data.lateral[ring];
                Vector3 depth = data.depth[ring];
                int row = ring * RadialSegments;

                for (int segment = 0; segment < RadialSegments; segment++)
                {
                    float angle = 2f * Mathf.PI * segment / RadialSegments;
                    float cosine = Mathf.Cos(angle);
                    float sine = Mathf.Sin(angle);
                    Vector3 worldNormal = lateral * (cosine / radiusX) + depth * (sine / radiusDepth)
                        - tangent * (radiusDerivative / radiusX);
                    if (!TryNormalize(worldNormal, out worldNormal))
                        worldNormal = lateral;
                    Vector3 localNormal = normalToLocal.MultiplyVector(worldNormal);
                    if (!TryNormalize(localNormal, out localNormal))
                        localNormal = worldNormal;
                    data.normals[row + segment] = localNormal;
                }
            }

            data.mesh.vertices = data.vertices;
            data.mesh.normals = data.normals;
            data.mesh.bounds = new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
            return true;
        }

        private static void SetVisible(ArmMesh data, bool visible)
        {
            if (data.renderer && data.renderer.enabled != visible)
                data.renderer.enabled = visible;
        }

        private static void Dispose(ArmMesh data)
        {
            if (data.mesh)
            {
                if (Application.isPlaying) Destroy(data.mesh);
                else DestroyImmediate(data.mesh);
            }
            if (data.gameObject)
            {
                if (Application.isPlaying) Destroy(data.gameObject);
                else DestroyImmediate(data.gameObject);
            }
        }

        private static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            float magnitudeSquared = value.sqrMagnitude;
            if (!IsFinite(value) || !IsFinite(magnitudeSquared) || magnitudeSquared < 0.00000001f)
                return false;
            normalized = value / Mathf.Sqrt(magnitudeSquared);
            return IsFinite(normalized);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
