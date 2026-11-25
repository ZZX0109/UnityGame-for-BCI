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
    /// Skin-weighted triangles are partitioned into visible body, hidden head, and replaced
    /// hand regions. Derived mesh and material assets persist alongside the source FBX.
    /// </summary>
    public static class FirstPersonFullBodyBuilder
    {
        const string LogPrefix = "[FullBodyBuilder] ";
        const string WarnPrefix = "[FullBodyBuilder] WARN ";
        const string SourceFbxPath = "Assets/VirtualHand/Models/FullBody/neutral_adult_avatar.fbx";
        // Keep the generated asset at the Resources path used by StreetCrossingFullBodyBinding.
        const string PrefabOutputPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";
        const string HandLeftPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_L.prefab";
        const string HandRightPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_R.prefab";

        /// <summary>Normalized skin-weight share a vertex must dedicate to the head/neck chain to count as head.</summary>
        public const float HeadWeightThreshold = 0.5f;

        /// <summary>Bone-name prefixes that define the head region (case-insensitive).</summary>
        static readonly string[] HeadBoneNamePatterns = { "head", "neck" };

        /// <summary>Material/mesh name keywords mapped to explicit Body membership (no warning).</summary>
        static readonly string[] BodyKeywords = {
            "skin", "face", "head", "body", "torso", "eye", "brow", "hair", "lash", "neck",
            "arm", "leg", "hand", "foot", "toe", "mouth", "teeth", "tongue", "nail", "gum", "lips", "nipple", "ears", "low-poly", "blunt_bob"
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

                EnsureAssetFolder(Path.GetDirectoryName(PrefabOutputPath).Replace('\\', '/'));

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
                int headVertices = 0, headTriangles = 0;
                // Capture acceptance stats from the live FBX instance NOW: the source SMR components are
                // destroyed in step 3 and the whole temp root goes away in the finally block, so the
                // report must never rely on scene queries.
                var acceptance = CaptureAcceptanceData(sourceRenderers);
                var eyes = sourceRenderers.Single(r => r.name == "Human.low-poly");
                // The FBX mesh is authored in centimetres. Evaluate its bind matrices
                // once in the editor so the runtime marker is in avatar metres.
                Mesh eyeMesh = eyes.sharedMesh;
                var vertices = eyeMesh.vertices;
                var weights = eyeMesh.boneWeights;
                var matrices = eyes.bones.Zip(eyeMesh.bindposes, (bone, bind) => bone.localToWorldMatrix * bind).ToArray();
                Vector3 eyeCenter = Vector3.zero;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var w = weights[i];
                    var v = vertices[i];
                    eyeCenter += matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                        + matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                        + matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                        + matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                }
                var eyeAnchor = new GameObject("EyeCenter").transform;
                eyeAnchor.SetParent(bodyRoot.transform, false);
                eyeAnchor.position = eyeCenter / vertices.Length;
                if (eyeAnchor.localPosition.y < 1f || eyeAnchor.localPosition.y > 2f)
                    throw new InvalidOperationException("Eye center outside the neutral adult avatar.");
                Debug.Log($"{LogPrefix}Desktop eye center in avatar metres: {eyeAnchor.localPosition:F4}");

                EnsureAssetFolder(GeneratedFolder);
                foreach (var source in sourceRenderers)
                    PartitionRenderer(source, visuals.transform, createdRenderers, ref headVertices, ref headTriangles);
                var headRenderer = createdRenderers.FirstOrDefault(r => r.name == "Head");
                if (headRenderer == null) throw new InvalidOperationException("Avatar has no head region.");
                string headSplitMode = "exclusive triangle partition";
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

        const string GeneratedFolder = "Assets/VirtualHand/Models/FullBody/Generated";

        // Every source triangle has one owner. The original hand surfaces are replaced by
        // Elena below the wrist. Boundary triangles stay on the forearm for a closed cuff.
        static void PartitionRenderer(SkinnedMeshRenderer source, Transform parent,
            List<SkinnedMeshRenderer> created, ref int headVertices, ref int headTriangles, bool previewHeadOnly = false)
        {
            Mesh mesh = source.sharedMesh;
            var weights = mesh.boneWeights;
            var materials = source.sharedMaterials;
            var head = MarkHeadVertices(weights, FindHeadBoneIndices(source.bones), mesh.vertexCount, out int count);
            var handIndices = source.bones.Select((bone, i) => new { bone, i })
                .Where(x => x.bone != null && IsHandBone(x.bone)).Select(x => x.i).ToArray();
            var hand = MarkHeadVertices(weights, handIndices, mesh.vertexCount, out _);
            var groups = new Dictionary<string, SortedDictionary<int, List<int>>>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                string part = ClassifyPart(materials[slot].name);
                bool accessory = source.name == "Human.low-poly" || source.name.Contains("eyebrows") || source.name.Contains("blunt_bob");
                var indices = mesh.GetTriangles(slot);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i+1], c = indices[i+2];
                    if (part == "Body" && hand[a] && hand[b] && hand[c]) continue;
                    string region = accessory || (part == "Body" && head[a] && head[b] && head[c]) ? "Head" : part;
                    if (!groups.TryGetValue(region, out var slots))
                        groups.Add(region, slots = new SortedDictionary<int, List<int>>());
                    if (!slots.TryGetValue(slot, out var triangles))
                        slots.Add(slot, triangles = new List<int>());
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
            }
            foreach (var group in groups)
            {
                if (previewHeadOnly && group.Key != "Head") continue;
                var renderer = new GameObject(group.Key).AddComponent<SkinnedMeshRenderer>();
                renderer.transform.SetParent(parent, false);
                int indexCount = group.Value.Values.Sum(list => list.Count);
                if (indexCount == mesh.triangles.Length)
                    renderer.sharedMesh = mesh;
                else
                {
                    Mesh subset = Object.Instantiate(mesh);
                    subset.name = mesh.name + "_" + group.Key;
                    subset.subMeshCount = group.Value.Count;
                    int i = 0;
                    foreach (var triangles in group.Value.Values) subset.SetTriangles(triangles, i++, false);
                    renderer.sharedMesh = previewHeadOnly ? subset : SaveGenerated(subset);
                }
                renderer.sharedMaterials = group.Value.Keys.Select(i => previewHeadOnly ? materials[i] : AvatarMaterial(materials[i])).ToArray();
                CopySkinning(source, renderer);
                created.Add(renderer);
                if (group.Key == "Head")
                {
                    headVertices += count;
                    headTriangles += indexCount / 3;
                }
            }
        }

        static bool IsHandBone(Transform bone)
        {
            for (; bone != null; bone = bone.parent)
                if (bone.name == "hand_l" || bone.name == "hand_r") return true;
            return false;
        }

        static T SaveGenerated<T>(T asset) where T : Object
        {
            string extension = asset is Material ? ".mat" : ".asset";
            string path = GeneratedFolder + "/" + asset.name + extension;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(asset);
            return existing;
        }

        static Material AvatarMaterial(Material original)
        {
            string map;
            switch (original.name)
            {
                case "Human.low-poly": map = "Eyes"; break;
                case "Human.toigo_blunt_bob": map = "Hair"; break;
                case "Human.cortu_cargo_pants": map = "Pants"; break;
                case "Human.toigo_fisherman_sweater": map = "Sweater"; break;
                case "Human.toigo_mj_cloth_shoes": map = "Shoes"; break;
                case "Human.mindfront_eyebrows_01": map = null; break;
                default: map = "Skin"; break;
            }
            var mat = new Material(Shader.Find("Standard")) { name = original.name };
            if (map != null)
            {
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/VirtualHand/Models/FullBody/Textures/" + map + ".png");
                if (!mat.mainTexture) throw new InvalidOperationException("Missing avatar texture: " + map);
            }
            else mat.color = new Color(0.14f, 0.09f, 0.06f);
            mat.SetFloat("_Glossiness", map == "Eyes" ? 0.45f : 0.15f);
            if (map == "Hair" || map == "Eyes" || map == "Pants")
            {
                mat.SetFloat("_Mode", 1);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
                mat.SetFloat("_Cutoff", 0.35f);
            }
            return SaveGenerated(mat);
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
            if (!sourceRenderer.sharedMesh.isReadable)
                return BuildFallbackHeadRenderer(sourceRenderer, visualsParent);
            var parts = new List<SkinnedMeshRenderer>();
            int vertices = 0, triangles = 0;
            PartitionRenderer(sourceRenderer, visualsParent, parts, ref vertices, ref triangles, true);
            return parts.FirstOrDefault() ?? BuildFallbackHeadRenderer(sourceRenderer, visualsParent);
        }

        // Preserve the Repair branch's explicit unreadable-mesh fallback for this public
        // helper. The production Build path uses the imported, readable FBX and writes a
        // disjoint persistent head mesh through PartitionRenderer.
        static SkinnedMeshRenderer BuildFallbackHeadRenderer(SkinnedMeshRenderer source, Transform visualsParent)
        {
            Mesh mesh = source.sharedMesh;
            if (mesh == null) return null;
            Material[] materials = source.sharedMaterials;
            var candidates = new List<int>();
            for (int slot = 0; slot < mesh.subMeshCount; slot++)
            {
                string materialName = slot < materials.Length && materials[slot] != null
                    ? materials[slot].name : mesh.name;
                string lowered = materialName.ToLowerInvariant();
                if (lowered.Contains("head") || lowered.Contains("face") || lowered.Contains("eye") ||
                    lowered.Contains("brow") || lowered.Contains("scalp") || lowered.Contains("hair") ||
                    lowered.Contains("skull") || lowered.Contains("neck"))
                    candidates.Add(slot);
            }
            if (candidates.Count == 0)
            {
                for (int slot = 0; slot < mesh.subMeshCount; slot++)
                {
                    string materialName = slot < materials.Length && materials[slot] != null
                        ? materials[slot].name : mesh.name;
                    if (ClassifyPart(materialName) == "Body") candidates.Add(slot);
                }
            }
            if (candidates.Count == 0) return null;

            Debug.LogWarning($"{WarnPrefix}Head split fallback for '{GetPath(source.transform)}': " +
                $"assigning submeshes {string.Join(", ", candidates)} wholesale to the Head SMR.");
            var headObject = new GameObject("Head");
            headObject.transform.SetParent(visualsParent, false);
            var renderer = headObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = BuildSlotMaterials(materials, mesh.subMeshCount, candidates);
            CopySkinning(source, renderer);
            return renderer;
        }

        static Material[] BuildSlotMaterials(Material[] originals, int subMeshCount, List<int> ownedSlots)
        {
            var result = new Material[subMeshCount];
            foreach (int slot in ownedSlots)
                if (slot < originals.Length) result[slot] = originals[slot];
            return result;
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

            var aligner = hand.AddComponent<Neuralife.VirtualHand.Body.ElenaWristAligner>();
            aligner.Configure(left, skeleton);
            if (!aligner.AlignNow())
                throw new InvalidOperationException($"Could not fit {hand.name} to {wristBone.name}.");
            PrefabUtility.RecordPrefabInstancePropertyModifications(hand.transform);
            paths.Add($"{hand.name} -> {GetPath(wristBone)} (palm fitted, wrist aligned)");

        }

        // Wires the FullBodyPoseController rig fields to manifest bones so the SeatedArmSolver output
        // actually reaches the skeleton (bodySpace = pelvis, upper arms = upperarm_*, elbows = lowerarm_*, wrists = hand_*).
        static void WirePoseControllerRig(Component poseController, Transform skeleton, List<string> missingRefs, List<string> attachLog)
        {
            var bindings = new (string field, string bone)[]
            {
                ("bodySpace", "pelvis"),
                ("leftUpperArm", "upperarm_l"),
                ("rightUpperArm", "upperarm_r"),
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
            Debug.Log($"{LogPrefix}Wired FullBodyPoseController rig fields: {wired}/{bindings.Length} (bodySpace=pelvis, upper arms=upperarm_*, elbows=lowerarm_*, wrists=hand_*).");
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
                else SetReferenceList(visibility, "HiddenRenderers", createdRenderers.Where(r => r.name == "Head").ToArray(), attachLog);
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
            Debug.Log($"{LogPrefix}ACCEPT 9/9 Head split: {headSplitMode}, {headVertices} weighted vertices, {headTriangles} triangles");

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
