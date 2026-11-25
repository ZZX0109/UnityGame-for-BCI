#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds the FirstPersonFullBody prefab:
    /// BodyRoot -> Skeleton (FBX instance, source renderers replaced) + Visuals (Body/Clothing/Shoes/Head SMRs)
    /// + Elena first-person hands parented to the FBX wrist bones.
    ///
    /// Design notes:
    /// - Per-part SMRs (Body/Clothing/Shoes) SHARE the original mesh asset. Each part SMR carries the full
    ///   original material array with null in every slot that does not belong to that part. Unity 2022.2+
    ///   skips submeshes whose material slot is null, so each part renders only its own submeshes without
    ///   duplicating the mesh asset (no new mesh assets are created for the parts).
    /// - The Head SMR is a true vertex-weight split (head/neck_01 chain, normalized weight >= 0.5 per vertex,
    ///   triangle accepted only when all three vertices agree) so first-person hiding can disable exactly the
    ///   head geometry. When the mesh is not readable the split degrades to a wholesale submesh assignment
    ///   and logs a [FullBodyBuilder] WARN - it never silently skips the head split.
    /// - Runtime body components (FullBodyPoseController, LowerBodyPoseController, WalkCycleDriver,
    ///   FirstPersonVisibility, FullBodyAppearanceController) are attached by name via reflection so this editor utility keeps
    ///   compiling even if the runtime side lands later; missing types are logged, never silently skipped.
    ///   SeatedArmSolver and AvatarBodyCalibration are not MonoBehaviours and are intentionally not attached.
    ///   GameIntegrationAdapter is deliberately NOT attached (it belongs to Game02/Game03).
    /// </summary>
    public static class FirstPersonFullBodyBuilder
    {
        const string LogPrefix = "[FullBodyBuilder] ";
        const string WarnPrefix = "[FullBodyBuilder] WARN ";
        const string SourceFbxPath = "Assets/VirtualHand/Models/FullBody/neutral_adult_avatar.fbx";
        const string PrefabOutputPath = "Assets/VirtualHand/Prefabs/FirstPersonFullBody.prefab";
        const string HandLeftPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_L.prefab";
        const string HandRightPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_R.prefab";

        /// <summary>Normalized skin-weight share a vertex must dedicate to the head/neck chain to count as head.</summary>
        public const float HeadWeightThreshold = 0.5f;

        /// <summary>Bone-name prefixes that define the head region (case-insensitive).</summary>
        static readonly string[] HeadBoneNamePatterns = { "head", "neck" };

        /// <summary>Material/mesh name keywords mapped to explicit Body membership (no warning).</summary>
        static readonly string[] BodyKeywords = {
            "skin", "face", "head", "body", "torso", "eye", "brow", "hair", "lash", "neck",
            "arm", "leg", "hand", "foot", "toe", "mouth", "teeth", "tongue", "nail", "gum"
        };

        static readonly string[] ClothingKeywords = {
            "sweater", "pants", "cloth", "shirt", "jacket", "trouser", "hoodie", "sock", "skirt", "dress", "sleeve"
        };

        static readonly string[] ShoesKeywords = { "shoe", "boot", "sneaker", "slipper", "footwear", "sandal" };

        static readonly HashSet<string> UnmatchedNames = new HashSet<string>();

        [MenuItem("VirtualHand/Build FirstPersonFullBody")]
        public static void Build()
        {
            UnmatchedNames.Clear();
            var missingRefs = new List<string>();
            var createdRenderers = new List<SkinnedMeshRenderer>();
            var attachLog = new List<string>();

            GameObject root = null;
            try
            {
                // ------------------------------------------------------------------ 1) Source FBX
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFbxPath);
                if (fbx == null)
                    throw new InvalidOperationException($"Source FBX not found at '{SourceFbxPath}'.");

                EnsureAssetFolder("Assets/VirtualHand/Prefabs");

                root = new GameObject("FirstPersonFullBody");
                var bodyRoot = new GameObject("BodyRoot");
                bodyRoot.transform.SetParent(root.transform, false);

                var skeleton = (GameObject)PrefabUtility.InstantiatePrefab(fbx, bodyRoot.transform);
                skeleton.name = "Skeleton";
                skeleton.transform.localPosition = Vector3.zero;
                skeleton.transform.localRotation = Quaternion.identity;
                skeleton.transform.localScale = Vector3.one;

                var visuals = new GameObject("Visuals");
                visuals.transform.SetParent(bodyRoot.transform, false);

                var sourceRenderers = skeleton.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (sourceRenderers.Length == 0)
                    throw new InvalidOperationException("No SkinnedMeshRenderer found inside the source FBX.");
                Debug.Log($"{LogPrefix}Source FBX contains {sourceRenderers.Length} SkinnedMeshRenderer(s).");

                // --------------------------------------------------- 2) Per-part split of every source SMR
                var classTally = new Dictionary<string, int> { { "Body", 0 }, { "Clothing", 0 }, { "Shoes", 0 } };
                SkinnedMeshRenderer headRenderer = null;
                string headSplitMode = "none";
                int headVertices = 0, headTriangles = 0;
                var primary = sourceRenderers.OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.subMeshCount : 0)
                    .ThenByDescending(r => r.bones != null ? r.bones.Length : 0).First();

                // Capture acceptance stats from the live FBX instance NOW: the source SMR components are
                // destroyed in step 3 and the whole temp root goes away in the finally block, so the
                // report must never rely on scene queries.
                var acceptance = CaptureAcceptanceData(sourceRenderers);

                foreach (var source in sourceRenderers)
                    SplitSourceRenderer(source, visuals.transform, classTally, createdRenderers, missingRefs);

                // Head split runs on the primary renderer (the merged body). If it yields nothing we try the
                // remaining renderers before falling back, so the head region is never silently skipped.
                headRenderer = TryBuildHeadRenderer(primary, visuals.transform, missingRefs, ref headSplitMode, ref headVertices, ref headTriangles);
                if (headRenderer == null)
                {
                    foreach (var source in sourceRenderers)
                    {
                        if (source == primary) continue;
                        headRenderer = TryBuildHeadRenderer(source, visuals.transform, missingRefs, ref headSplitMode, ref headVertices, ref headTriangles);
                        if (headRenderer != null) break;
                    }
                }
                if (headRenderer != null) createdRenderers.Add(headRenderer);
                else missingRefs.Add("Head SMR (no head/neck weights and no submesh fallback available)");

                // --------------------------------------------------- 3) Retire the source renderers
                foreach (var source in sourceRenderers)
                {
                    Debug.Log($"{LogPrefix}Destroying source renderer '{GetPath(source.transform)}' (replaced by part SMRs).");
                    Object.DestroyImmediate(source);
                }

                // --------------------------------------------------- 4) Elena first-person hands
                var handPaths = AttachHands(skeleton.transform, missingRefs);

                // --------------------------------------------------- 5) Root runtime components
                AttachRootComponents(root, skeleton.transform, createdRenderers, headRenderer, missingRefs, attachLog);
                AttachIfMonoBehaviour(root, typeof(SeatedArmSolver), attachLog);
                AttachIfMonoBehaviour(root, typeof(AvatarBodyCalibration), attachLog);

                // --------------------------------------------------- 6) Save prefab
                bool saved;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabOutputPath, out saved);
                if (!saved || prefab == null)
                    throw new InvalidOperationException($"PrefabUtility.SaveAsPrefabAsset failed for '{PrefabOutputPath}'.");

                Debug.Log($"{LogPrefix}Prefab saved to '{PrefabOutputPath}'.");

                // --------------------------------------------------- 7) Acceptance report
                // Runs while the temp root is still alive: ACCEPT 2/9 reads the created SMRs and the
                // skeleton stats were captured from the FBX instance before its source SMRs were retired.
                LogAcceptance(createdRenderers, missingRefs, attachLog, headSplitMode, headVertices, headTriangles, acceptance);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // ============================================================================================
        //  Classification (testable)
        // ============================================================================================

        /// <summary>
        /// Maps a material or mesh name to the Visuals child name it belongs to:
        /// "Body", "Clothing" or "Shoes". Unknown names still return "Body" but emit a single warning.
        /// </summary>
        public static string ClassifyPart(string materialOrMeshName)
        {
            string name = (materialOrMeshName ?? string.Empty).ToLowerInvariant();

            if (Matches(name, ShoesKeywords)) return "Shoes";
            if (Matches(name, ClothingKeywords)) return "Clothing";
            if (Matches(name, BodyKeywords)) return "Body";

            if (UnmatchedNames.Add(materialOrMeshName ?? "<null>"))
                Debug.LogWarning($"{WarnPrefix}Material/mesh name '{materialOrMeshName}' matched no part keyword; defaulting to Body.");
            return "Body";
        }

        static bool Matches(string lowered, string[] keywords)
        {
            foreach (var keyword in keywords)
                if (lowered.Contains(keyword)) return true;
            return false;
        }

        // ============================================================================================
        //  Part splitting
        // ============================================================================================

        static void SplitSourceRenderer(SkinnedMeshRenderer source, Transform visualsParent,
            Dictionary<string, int> classTally, List<SkinnedMeshRenderer> created, List<string> missingRefs)
        {
            var mesh = source.sharedMesh;
            if (mesh == null) { missingRefs.Add($"Source renderer '{source.name}' has no sharedMesh"); return; }
            var materials = source.sharedMaterials;

            if (materials.Length != mesh.subMeshCount)
                Debug.LogWarning($"{WarnPrefix}Renderer '{source.name}': material count {materials.Length} != submesh count {mesh.subMeshCount}; extra slots fall back to the mesh name for classification.");

            // Group submesh slots by part.
            var slotsByPart = new Dictionary<string, List<int>>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                string materialName = slot < materials.Length && materials[slot] != null ? materials[slot].name : mesh.name;
                string part = ClassifyPart(materialName);
                classTally[part]++;
                if (!slotsByPart.TryGetValue(part, out var list)) slotsByPart[part] = list = new List<int>();
                list.Add(slot);
            }

            foreach (var pair in slotsByPart)
            {
                string partName = pair.Key;
                var slots = pair.Value;

                var partObject = new GameObject(partName);
                partObject.transform.SetParent(visualsParent, false);
                var renderer = partObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; // original mesh asset, shared - no new mesh asset per part
                renderer.sharedMaterials = BuildSlotMaterials(materials, mesh.subMeshCount, slots);
                CopySkinning(source, renderer);

                int vertices = CountSlotVertices(mesh, slots);
                Debug.Log($"{LogPrefix}Part SMR '{partName}' <- '{GetPath(source.transform)}': submeshes {slots.Count}/{mesh.subMeshCount}, vertices {vertices}/{mesh.vertexCount}, materials {slots.Count} of {materials.Length} (null slots skip rendering, Unity {Application.unityVersion}).");
                created.Add(renderer);
            }
        }

        static Material[] BuildSlotMaterials(Material[] originals, int subMeshCount, List<int> ownedSlots)
        {
            var result = new Material[subMeshCount];
            foreach (int slot in ownedSlots)
                if (slot < originals.Length) result[slot] = originals[slot];
            return result;
        }

        static int CountSlotVertices(Mesh mesh, List<int> slots)
        {
            int count = 0;
            foreach (int slot in slots)
            {
                if (mesh.isReadable) count += mesh.GetTriangles(slot).Length / 3;
                else { count = -1; break; }
            }
            return count;
        }

        static void CopySkinning(SkinnedMeshRenderer source, SkinnedMeshRenderer target)
        {
            target.bones = source.bones;                 // preserve full bone reference array
            target.rootBone = source.rootBone;           // preserve root bone reference
            target.localBounds = source.localBounds;
            target.quality = source.quality;
            target.updateWhenOffscreen = source.updateWhenOffscreen;
            target.shadowCastingMode = source.shadowCastingMode;
            target.receiveShadows = source.receiveShadows;
        }

        // ============================================================================================
        //  Head split (first-person hiding)
        // ============================================================================================

        /// <summary>
        /// Reusable head-split entry point: builds a Head SMR under <paramref name="visualsParent"/> from
        /// <paramref name="sourceRenderer"/> using vertex skin weights over the head/neck_01 bone chain.
        /// Returns null when the source carries no head weights at all; callers must then fall back or log.
        /// </summary>
        public static SkinnedMeshRenderer BuildHeadRenderer(SkinnedMeshRenderer sourceRenderer, Transform visualsParent)
        {
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null) return null;
            return TryBuildHeadRenderer(sourceRenderer, visualsParent, new List<string>(),
                ref HeadSplitModeTrace, ref HeadVertexTrace, ref HeadTriangleTrace);
        }

        // Trace fields keep BuildHeadRenderer's public signature simple while TryBuild* reports details.
        static string HeadSplitModeTrace = "none";
        static int HeadVertexTrace;
        static int HeadTriangleTrace;

        static SkinnedMeshRenderer TryBuildHeadRenderer(SkinnedMeshRenderer source, Transform visualsParent,
            List<string> missingRefs, ref string headSplitMode, ref int headVertices, ref int headTriangles)
        {
            var mesh = source.sharedMesh;
            if (mesh == null) return null;

            int[] headIndices = FindHeadBoneIndices(source.bones);
            if (headIndices.Length == 0)
            {
                Debug.LogWarning($"{WarnPrefix}Renderer '{source.name}' owns no head/neck bones; head split cannot run on it.");
                return null;
            }

            if (!mesh.isReadable)
            {
                // Allowed fallback: assign head/neck submeshes wholesale, never silently skip.
                headSplitMode = "fallback (mesh not readable)";
                var renderer = BuildFallbackHeadRenderer(source, visualsParent, headIndices);
                if (renderer == null)
                    missingRefs.Add($"Head SMR fallback for '{source.name}' matched no submesh");
                return renderer;
            }

            headSplitMode = $"vertex-weight split (threshold {HeadWeightThreshold})";
            var weights = mesh.boneWeights;
            if (weights.Length != mesh.vertexCount)
            {
                Debug.LogWarning($"{WarnPrefix}Renderer '{source.name}': boneWeight count {weights.Length} != vertex count {mesh.vertexCount}; using fallback.");
                headSplitMode = "fallback (boneWeight count mismatch)";
                return BuildFallbackHeadRenderer(source, visualsParent, headIndices);
            }

            bool[] isHeadVertex = MarkHeadVertices(weights, headIndices, mesh.vertexCount, out int headVertexCount);
            if (headVertexCount == 0)
            {
                Debug.LogWarning($"{WarnPrefix}Renderer '{source.name}': no vertex passes the head weight threshold ({HeadWeightThreshold}); head split yields nothing on it.");
                return null;
            }

            // One output submesh per original material slot that contributed head triangles.
            var contributingSlots = new List<int>();
            var headTrisBySlot = new Dictionary<int, List<int>>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                var sourceTris = mesh.GetTriangles(slot);
                var headTris = new List<int>();
                for (int t = 0; t < sourceTris.Length; t += 3)
                {
                    if (isHeadVertex[sourceTris[t]] && isHeadVertex[sourceTris[t + 1]] && isHeadVertex[sourceTris[t + 2]])
                        headTris.AddRange(new[] { sourceTris[t], sourceTris[t + 1], sourceTris[t + 2] });
                }
                if (headTris.Count > 0)
                {
                    headTrisBySlot[slot] = headTris;
                    contributingSlots.Add(slot);
                    headTriangles += headTris.Count / 3;
                }
            }
            if (contributingSlots.Count == 0)
            {
                Debug.LogWarning($"{WarnPrefix}Renderer '{source.name}': {headVertexCount} head-weighted vertices exist but no triangle is fully head-weighted; using fallback.");
                headSplitMode = "fallback (no fully head-weighted triangle)";
                return BuildFallbackHeadRenderer(source, visualsParent, headIndices);
            }

            var headMesh = new Mesh { name = mesh.name + "_HeadRegion" };
            CopyVertexData(mesh, headMesh, isHeadVertex, headTrisBySlot, source.bones.Length);

            var headObject = new GameObject("Head");
            headObject.transform.SetParent(visualsParent, false);
            var headRenderer = headObject.AddComponent<SkinnedMeshRenderer>();
            headRenderer.sharedMesh = headMesh;
            headRenderer.sharedMaterials = contributingSlots.Select(s => s < source.sharedMaterials.Length ? source.sharedMaterials[s] : null).ToArray();
            CopySkinning(source, headRenderer); // bones array preserved verbatim -> boneWeight indices stay aligned

            Debug.Log($"{LogPrefix}Head SMR built from '{GetPath(source.transform)}': {headVertexCount} vertices, {headTriangles} triangles, {contributingSlots.Count} material slot(s), bones {source.bones.Length} (preserved).");
            headVertices = headVertexCount;
            headTriangles = headTrisBySlot.Sum(pair => pair.Value.Count) / 3;
            return headRenderer;
        }

        static int[] FindHeadBoneIndices(Transform[] bones)
        {
            var found = new List<int>();
            if (bones == null) return found.ToArray();
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                string lowered = bones[i].name.ToLowerInvariant();
                foreach (var pattern in HeadBoneNamePatterns)
                    if (lowered.StartsWith(pattern, StringComparison.Ordinal)) { found.Add(i); break; }
            }
            return found.ToArray();
        }

        static bool[] MarkHeadVertices(BoneWeight[] weights, int[] headIndices, int vertexCount, out int headVertexCount)
        {
            var isHead = new bool[vertexCount];
            var headSet = new HashSet<int>(headIndices);
            headVertexCount = 0;
            for (int v = 0; v < weights.Length; v++)
            {
                float total = weights[v].weight0 + weights[v].weight1 + weights[v].weight2 + weights[v].weight3;
                if (total <= 1e-6f) continue;
                float headSum = 0f;
                if (headSet.Contains(weights[v].boneIndex0)) headSum += weights[v].weight0;
                if (headSet.Contains(weights[v].boneIndex1)) headSum += weights[v].weight1;
                if (headSet.Contains(weights[v].boneIndex2)) headSum += weights[v].weight2;
                if (headSet.Contains(weights[v].boneIndex3)) headSum += weights[v].weight3;
                if (headSum / total >= HeadWeightThreshold) { isHead[v] = true; headVertexCount++; }
            }
            return isHead;
        }

        static void CopyVertexData(Mesh sourceMesh, Mesh targetMesh, bool[] used, Dictionary<int, List<int>> trisBySlot, int boneCount)
        {
            var remap = new Dictionary<int, int>();
            var indexList = new List<int>();
            for (int v = 0; v < used.Length; v++)
                if (used[v]) { remap[v] = indexList.Count; indexList.Add(v); }

            targetMesh.indexFormat = indexList.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            targetMesh.vertices = indexList.Select(i => sourceMesh.vertices[i]).ToArray();
            Vector3[] normals = indexList.Select(i => sourceMesh.normals[i]).ToArray();
            if (normals.Any(n => !IsFinite(n))) normals = Array.Empty<Vector3>();
            if (normals.Length > 0) targetMesh.normals = normals;
            Vector4[] tangents = indexList.Select(i => sourceMesh.tangents[i]).ToArray();
            if (tangents.Any(t => t.sqrMagnitude > 0f)) targetMesh.tangents = tangents;
            if (sourceMesh.uv.Length > 0) targetMesh.uv = indexList.Select(i => sourceMesh.uv[i]).ToArray();
            if (sourceMesh.uv2.Length > 0) targetMesh.uv2 = indexList.Select(i => sourceMesh.uv2[i]).ToArray();
            if (sourceMesh.uv3.Length > 0) targetMesh.uv3 = indexList.Select(i => sourceMesh.uv3[i]).ToArray();
            if (sourceMesh.uv4.Length > 0) targetMesh.uv4 = indexList.Select(i => sourceMesh.uv4[i]).ToArray();
            if (sourceMesh.colors.Length > 0) targetMesh.colors = indexList.Select(i => sourceMesh.colors[i]).ToArray();
            targetMesh.boneWeights = indexList.Select(i => sourceMesh.boneWeights[i]).ToArray();
            targetMesh.bindposes = sourceMesh.bindposes; // full array: preserved with the source bones

            targetMesh.subMeshCount = trisBySlot.Count;
            int sub = 0;
            foreach (var pair in trisBySlot.OrderBy(p => p.Key))
                targetMesh.SetTriangles(pair.Value.Select(t => remap[t]).ToArray(), sub++);

            Debug.Assert(targetMesh.boneWeights.Length <= boneCount, "head vertex weights must stay inside the preserved bones array");
        }

        static bool IsFinite(Vector3 v)
        {
            // NaN/Infinity checks instead of float.IsFinite: valid on every API compatibility level.
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        static SkinnedMeshRenderer BuildFallbackHeadRenderer(SkinnedMeshRenderer source, Transform visualsParent, int[] headIndices)
        {
            var mesh = source.sharedMesh;
            var materials = source.sharedMaterials;

            // Prefer submeshes whose material name describes head/neck parts; else every slot already
            // classified as Body. The rule actually used is always logged via WARN.
            var candidates = new List<int>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                string materialName = slot < materials.Length && materials[slot] != null ? materials[slot].name : mesh.name;
                string lowered = materialName.ToLowerInvariant();
                if (lowered.Contains("head") || lowered.Contains("face") || lowered.Contains("eye") ||
                    lowered.Contains("brow") || lowered.Contains("scalp") || lowered.Contains("hair") ||
                    lowered.Contains("skull") || lowered.Contains("neck"))
                    candidates.Add(slot);
            }
            if (candidates.Count == 0)
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    string materialName = slot < materials.Length && materials[slot] != null ? materials[slot].name : mesh.name;
                    if (ClassifyPart(materialName) == "Body") candidates.Add(slot);
                }
            if (candidates.Count == 0) return null;

            Debug.LogWarning($"{WarnPrefix}Head split fallback for '{GetPath(source.transform)}': mesh not readable or no head-weighted triangle; assigning submeshes {string.Join(", ", candidates)} wholesale to the Head SMR (head bones: {string.Join(", ", headIndices.Select(i => source.bones[i].name))}).");

            var headObject = new GameObject("Head");
            headObject.transform.SetParent(visualsParent, false);
            var renderer = headObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; // shared original mesh; null slots skip rendering
            renderer.sharedMaterials = BuildSlotMaterials(materials, mesh.subMeshCount, candidates);
            CopySkinning(source, renderer);
            return renderer;
        }

        // ============================================================================================
        //  Elena hands
        // ============================================================================================

        static List<string> AttachHands(Transform skeleton, List<string> missingRefs)
        {
            var paths = new List<string>();
            AttachHand(skeleton, true, paths, missingRefs);
            AttachHand(skeleton, false, paths, missingRefs);
            return paths;
        }

        static void AttachHand(Transform skeleton, bool left, List<string> paths, List<string> missingRefs)
        {
            string wristBoneName = left ? "hand_l" : "hand_r";
            var wristBone = FindWristBone(skeleton, wristBoneName);
            if (wristBone == null)
            {
                missingRefs.Add($"FBX wrist bone '{wristBoneName}'");
                Debug.LogWarning($"{WarnPrefix}No wrist bone matching '{wristBoneName}' under '{GetPath(skeleton)}'; {(left ? "left" : "right")} hand skipped (never silently).");
                return;
            }

            string prefabPath = left ? HandLeftPrefabPath : HandRightPrefabPath;
            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (handPrefab == null) { missingRefs.Add($"Elena hand prefab '{prefabPath}'"); return; }

            var hand = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, wristBone);
            hand.name = left ? "ElenaHand_L" : "ElenaHand_R";
            hand.transform.localPosition = Vector3.zero;    // spec: exact wrist alignment
            hand.transform.localRotation = Quaternion.identity;

            var elenaWrist = FindElenaWrist(hand.transform, left);
            string delta = "n/a (landmark not found)";
            if (elenaWrist != null)
            {
                var d = elenaWrist.position - wristBone.position;
                // V5-075 fix (1) (ClipAudit DEFECT (1)): bake the wrist-landmark compensation at attach
                // time. The GLB-internal node chain (0.0145x root scale x Armature 100x) leaves the Elena
                // wrist landmark ~2.8 m off the body wrist bone at localPosition zero.
                // InverseTransformPoint absorbs parent-chain rotation + per-axis lossyScale (incl. the
                // ~101.65x body-side authored bone scale, arithmetically inferred) in one step and is
                // idempotent (d ~= 0 writes ~= 0). Runtime counterpart: ElenaWristAligner.
                hand.transform.localPosition = wristBone.InverseTransformPoint(wristBone.position - d);
                var post = elenaWrist.position - wristBone.position;
                delta = $"pre {d.magnitude:F4} m -> post {post.magnitude:F4} m {post}";
            }
            else missingRefs.Add($"Elena wrist landmark on '{hand.name}'");

            string parentPath = GetPath(wristBone);
            paths.Add($"{hand.name} -> {parentPath} (localPos {hand.transform.localPosition}, localRot {hand.transform.localRotation.eulerAngles}), wrist delta {delta}");
            Debug.Log($"{LogPrefix}Hand '{hand.name}' parented to '{parentPath}'; Elena landmark '{(elenaWrist != null ? elenaWrist.name : "?")}'; wrist world-position delta: {delta}.");
        }

        // Wires the FullBodyPoseController rig fields to manifest bones so the SeatedArmSolver output
        // actually reaches the skeleton (bodySpace = pelvis origin, elbows = lowerarm_*, wrists = hand_*).
        static void WirePoseControllerRig(Component poseController, Transform skeleton, List<string> missingRefs, List<string> attachLog)
        {
            var bindings = new (string field, string bone)[]
            {
                ("bodySpace", "pelvis"),
                ("leftElbow", "lowerarm_l"),
                ("rightElbow", "lowerarm_r"),
                ("leftWrist", "hand_l"),
                ("rightWrist", "hand_r"),
            };
            var serialized = new SerializedObject(poseController);
            int wired = 0;
            foreach (var (field, bone) in bindings)
            {
                var property = serialized.FindProperty(field);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    missingRefs.Add($"FullBodyPoseController.{field} (serialized field not found)");
                    continue;
                }
                var boneTransform = FindWristBone(skeleton, bone);
                if (boneTransform == null)
                {
                    missingRefs.Add($"FullBodyPoseController.{field} <- bone '{bone}'");
                    continue;
                }
                property.objectReferenceValue = boneTransform;
                attachLog.Add($"FullBodyPoseController.{field} <- '{bone}' ({GetPath(boneTransform)})");
                wired++;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(poseController);
            Debug.Log($"{LogPrefix}Wired FullBodyPoseController rig fields: {wired}/{bindings.Length} (bodySpace=pelvis, elbows=lowerarm_*, wrists=hand_*).");
        }

        static Transform FindWristBone(Transform skeleton, string wristBoneName)
        {
            string target = wristBoneName.ToLowerInvariant();
            Transform best = null;
            foreach (var t in skeleton.GetComponentsInChildren<Transform>(true))
            {
                string lowered = t.name.ToLowerInvariant();
                if (lowered == target || lowered == target.Replace('_', '.') || lowered.StartsWith(target)) { best = t; break; }
            }
            return best;
        }

        // Wires WalkCycleDriver: locomotion source + the six manifest leg bones
        // (thigh/calf/foot × l/r). Legs are left unwired (logged) when a bone is absent.
        static void WireWalkCycleDriver(Component walkDriver, Component lowerBody, Transform skeleton, List<string> missingRefs, List<string> attachLog)
        {
            var bindings = new (string field, Object value, string label)[]
            {
                ("locomotion", lowerBody, lowerBody != null ? lowerBody.name : "LowerBodyPoseController"),
                ("leftThigh", FindWristBone(skeleton, "thigh_l") as Object, "thigh_l"),
                ("leftCalf", FindWristBone(skeleton, "calf_l") as Object, "calf_l"),
                ("leftFoot", FindWristBone(skeleton, "foot_l") as Object, "foot_l"),
                ("rightThigh", FindWristBone(skeleton, "thigh_r") as Object, "thigh_r"),
                ("rightCalf", FindWristBone(skeleton, "calf_r") as Object, "calf_r"),
                ("rightFoot", FindWristBone(skeleton, "foot_r") as Object, "foot_r"),
            };
            var serialized = new SerializedObject(walkDriver);
            int wired = 0;
            foreach (var (field, value, label) in bindings)
            {
                var property = serialized.FindProperty(field);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    missingRefs.Add($"WalkCycleDriver.{field} (serialized field not found)");
                    continue;
                }
                if (value == null)
                {
                    missingRefs.Add($"WalkCycleDriver.{field} <- '{label}'");
                    continue;
                }
                property.objectReferenceValue = value;
                attachLog.Add($"WalkCycleDriver.{field} <- '{label}'");
                wired++;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(walkDriver);
            Debug.Log($"{LogPrefix}Wired WalkCycleDriver refs: {wired}/{bindings.Length} (locomotion + thigh/calf/foot × l/r).");
        }

        static Transform FindElenaWrist(Transform handRoot, bool left)
        {
            // V5-075 门禁实测：两个 Elena 手部 prefab 的 m_SourcePrefab 均指向 ElenaHand_R.glb，
            // 其内部只有 hand.R_02 / hand.R.001_011，没有 hand.L_*；左手实例的腕部地标实际
            // 名为 hand.R_02。先按本侧精确名，再按对侧精确名，最后宽松回退（保持旧行为）。
            string primary = left ? "hand.L_02" : "hand.R_02";
            string secondary = left ? "hand.R_02" : "hand.L_02";
            var children = handRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in children)
                if (ElenaNativeRigProfile.MatchesBone(t.name, primary)) return t;
            foreach (var t in children)
                if (ElenaNativeRigProfile.MatchesBone(t.name, secondary)) return t;
            foreach (var t in children)
                if (t != handRoot && (ElenaNativeRigProfile.MatchesBone(t.name, "hand") || t.name.ToLowerInvariant().Contains("wrist")))
                    return t;
            return null;
        }

        // ============================================================================================
        //  Root runtime components
        // ============================================================================================

        static void AttachRootComponents(GameObject root, Transform skeleton, List<SkinnedMeshRenderer> createdRenderers,
            SkinnedMeshRenderer headRenderer, List<string> missingRefs, List<string> attachLog)
        {
            var poseController = AttachRuntimeComponent(root, "FullBodyPoseController", new[]
                { "Neuralife.VirtualHand.Body.FullBodyPoseController", "Neuralife.VirtualHand.FullBodyPoseController" }, attachLog, missingRefs);
            if (poseController != null)
                WirePoseControllerRig(poseController, skeleton, missingRefs, attachLog);
            var lowerBody = AttachRuntimeComponent(root, "LowerBodyPoseController", new[]
                { "Neuralife.VirtualHand.Body.LowerBodyPoseController", "Neuralife.VirtualHand.LowerBodyPoseController" }, attachLog, missingRefs);
            var walkDriver = AttachRuntimeComponent(root, "WalkCycleDriver", new[]
                { "Neuralife.VirtualHand.Body.WalkCycleDriver", "Neuralife.VirtualHand.WalkCycleDriver" }, attachLog, missingRefs);
            if (walkDriver != null)
                WireWalkCycleDriver(walkDriver, lowerBody, skeleton, missingRefs, attachLog);

            var visibility = AttachRuntimeComponent(root, "FirstPersonVisibility", new[]
                { "Neuralife.VirtualHand.Body.FirstPersonVisibility", "Neuralife.VirtualHand.FirstPersonVisibility" }, attachLog, missingRefs);
            if (visibility != null)
            {
                if (headRenderer == null) missingRefs.Add("FirstPersonVisibility.HiddenRenderers <- Head SMR (head SMR missing)");
                else SetReferenceList(visibility, "HiddenRenderers", new[] { headRenderer }, attachLog);
            }

            var appearance = AttachRuntimeComponent(root, "FullBodyAppearanceController", new[]
                { "Neuralife.VirtualHand.Body.FullBodyAppearanceController", "Neuralife.VirtualHand.FullBodyAppearanceController" }, attachLog, missingRefs);
            if (appearance != null)
            {
                if (createdRenderers.Count == 0) missingRefs.Add("FullBodyAppearanceController.BodyRenderers <- no part SMRs");
                else SetReferenceList(appearance, "BodyRenderers", createdRenderers.ToArray(), attachLog);
            }
        }

        static Component AttachRuntimeComponent(GameObject root, string simpleName, string[] candidateFullNames,
            List<string> attachLog, List<string> missingRefs)
        {
            Type type = FindRuntimeType(candidateFullNames, simpleName);
            if (type == null)
            {
                attachLog.Add($"{simpleName}: SKIPPED (type not found yet)");
                missingRefs.Add($"Runtime type '{simpleName}'");
                Debug.LogWarning($"{WarnPrefix}Runtime type '{simpleName}' not found in any loaded assembly; not attached.");
                return null;
            }
            if (!typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                attachLog.Add($"{simpleName}: SKIPPED (not a MonoBehaviour)");
                Debug.LogWarning($"{WarnPrefix}Type '{type.FullName}' is not a MonoBehaviour; not attached.");
                return null;
            }
            var component = root.AddComponent(type);
            attachLog.Add($"{type.FullName}: attached");
            EditorUtility.SetDirty(component);
            return component;
        }

        static void AttachIfMonoBehaviour(GameObject root, Type type, List<string> attachLog)
        {
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                root.AddComponent(type);
                attachLog.Add($"{type.FullName}: attached");
            }
            else
            {
                attachLog.Add($"{type.FullName}: SKIPPED ({(type.IsSealed && type.IsAbstract ? "static class" : "not a MonoBehaviour")})");
                Debug.LogWarning($"{WarnPrefix}{type.Name} is not a MonoBehaviour (spec says attach only if it is); skipping with this log.");
            }
        }

        static Type FindRuntimeType(string[] candidateFullNames, string simpleName)
        {
            foreach (var fullName in candidateFullNames)
            {
                var byAssembly = Type.GetType(fullName + ", Neuralife.VirtualHand.Runtime");
                if (byAssembly != null) return byAssembly;
            }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.FullName.StartsWith("Neuralife.VirtualHand", StringComparison.OrdinalIgnoreCase) &&
                    !assembly.FullName.StartsWith("VirtualHand", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var fullName in candidateFullNames)
                {
                    var t = assembly.GetType(fullName);
                    if (t != null) return t;
                }
                foreach (var t in assembly.GetTypes())
                    if (t.Name == simpleName) return t;
            }
            return null;
        }

        static void SetReferenceList(Component component, string fieldName, Object[] values, List<string> attachLog)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                attachLog.Add($"{component.GetType().Name}.{fieldName}: field not found for wiring");
                Debug.LogWarning($"{WarnPrefix}{component.GetType().Name} has no serialized field '{fieldName}'; wiring skipped.");
                return;
            }
            if (property.isArray)
            {
                property.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            else property.objectReferenceValue = values.Length > 0 ? values[0] : null;
            serialized.ApplyModifiedProperties();
            attachLog.Add($"{component.GetType().Name}.{fieldName} <- [{string.Join(", ", values.Select(v => v.name))}]");
        }

        // ============================================================================================
        //  Acceptance log
        // ============================================================================================

        static void LogAcceptance(List<SkinnedMeshRenderer> renderers, List<string> missingRefs, List<string> attachLog,
            string headSplitMode, int headVertices, int headTriangles, AcceptanceData acceptance)
        {
            // All skeleton-level stats come from 'acceptance', captured from the live FBX instance before
            // its source SMRs were destroyed and before the temp root was cleaned up. No scene queries
            // (FindObjectOfType) here: they returned null in an empty batch scene or picked up arbitrary
            // SMRs from whatever scene happened to be open, which made the acceptance data untrustworthy.
            Debug.Log($"{LogPrefix}ACCEPT 1/9 Skeleton bones: {acceptance.SkeletonBoneCount} (expected 53 for the MPFB rev2 neutral adult avatar).");
            foreach (var renderer in renderers)
            {
                var mesh = renderer.sharedMesh;
                Debug.Log($"{LogPrefix}ACCEPT 2/9 SMR '{renderer.name}': vertices {(mesh != null ? mesh.vertexCount : 0)}, submeshes {(mesh != null ? mesh.subMeshCount : 0)}, materials {renderer.sharedMaterials.Length}, bones {renderer.bones.Length}, null material slots {renderer.sharedMaterials.Count(m => m == null)} (skip rendering).");
            }
            Debug.Log($"{LogPrefix}ACCEPT 3/9 Head bone world position: {acceptance.HeadBonePosition} (bone '{acceptance.HeadBoneName}'; baselines y=1.5521, pelvis y=0.9009, height ~1.70 m)");
            Debug.Log($"{LogPrefix}ACCEPT 4/9 Elena hand parent paths: see per-hand LOG lines above ({(acceptance.SkeletonBoneCount > 0 ? "wrist bones live under Skeleton" : "see LOG")}).");
            var info = new FileInfo(PrefabOutputPath);
            Debug.Log($"{LogPrefix}ACCEPT 5/9 Prefab '{PrefabOutputPath}' size: {(info.Exists ? info.Length.ToString("N0") + " bytes" : "missing!")}");
            Debug.Log($"{LogPrefix}ACCEPT 6/9 Missing references: {(missingRefs.Count == 0 ? "none" : string.Join("; ", missingRefs))}");
            Debug.Log($"{LogPrefix}ACCEPT 7/9 Classification tallies recorded during split; unmatched names: {UnmatchedNames.Count} ({string.Join(", ", UnmatchedNames.Take(5))}{(UnmatchedNames.Count > 5 ? ", ..." : "")})");
            Debug.Log($"{LogPrefix}ACCEPT 8/9 Root components: {(attachLog.Count == 0 ? "none" : string.Join("; ", attachLog))}");
            Debug.Log($"{LogPrefix}ACCEPT 9/9 Head split: {headSplitMode}{(headSplitMode.StartsWith("vertex") ? $", {headVertices} head vertices, {headTriangles} head triangles" : "")}");
            Debug.Log($"{LogPrefix}Acceptance log complete; Unity {Application.unityVersion} skips submeshes with null material slots (required for the shared-mesh part split).");
        }

        /// <summary>
        /// Skeleton-level acceptance stats captured from the live FBX instance while the temp root is
        /// still alive, so the report never depends on scene queries after cleanup.
        /// </summary>
        sealed class AcceptanceData
        {
            public int SkeletonBoneCount;
            public string HeadBoneName = "(none)";
            public string HeadBonePosition = "head bone not found";
        }

        static AcceptanceData CaptureAcceptanceData(SkinnedMeshRenderer[] sourceRenderers)
        {
            var data = new AcceptanceData();

            // Bone count: union over every source SMR skeleton (dedup by instance ID), taken while the
            // SMR components are still alive. Falls back to counting distinct transforms under the first
            // renderer's root if the bones array is somehow empty.
            var boneSet = new HashSet<int>();
            Transform skeletonRoot = null;
            foreach (var renderer in sourceRenderers)
            {
                if (renderer.bones != null)
                    foreach (var bone in renderer.bones)
                        if (bone != null)
                        {
                            boneSet.Add(bone.GetInstanceID());
                            if (skeletonRoot == null) skeletonRoot = bone.root;
                        }
                if (data.SkeletonBoneCount == 0 && renderer.bones != null && renderer.bones.Length > 0)
                    data.SkeletonBoneCount = renderer.bones.Length;
            }
            if (boneSet.Count > 0) data.SkeletonBoneCount = boneSet.Count;
            else if (skeletonRoot != null)
                data.SkeletonBoneCount = skeletonRoot.GetComponentsInChildren<Transform>(true).Length - 1; // minus the root itself

            // Head bone: prefix match over the captured skeleton, same rule as the head split.
            foreach (var renderer in sourceRenderers)
            {
                if (renderer.bones == null) continue;
                var headIndices = FindHeadBoneIndices(renderer.bones);
                if (headIndices.Length == 0) continue;
                var headBone = renderer.bones[headIndices[0]];
                if (headBone == null) continue;
                data.HeadBoneName = headBone.name;
                data.HeadBonePosition = headBone.position.ToString("F4");
                break;
            }
            return data;
        }

        // ============================================================================================
        //  Small helpers
        // ============================================================================================

        static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder) != null) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, leaf);
        }

        static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
#endif
