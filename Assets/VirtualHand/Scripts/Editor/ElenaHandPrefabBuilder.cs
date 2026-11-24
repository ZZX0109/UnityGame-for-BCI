using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds reusable Elena FF hand prefabs from the imported GLB, assigns the
    /// sampled native profile, scales wrist-to-middle-tip length to 0.19 m, and
    /// verifies saved right/left prefab binding and skin deformation.
    /// </summary>
    public static class ElenaHandPrefabBuilder
    {
        const string ModelPath = "Assets/VirtualHand/Models/ElenaFF/ElenaHand_R.glb";
        const string ProfilePath = "Assets/VirtualHand/Resources/Rig/ElenaFF/ElenaNativeRig_R.asset";
        const string ProportionsPath = "Assets/VirtualHand/Resources/Rig/ElenaFF/HandProportions.asset";
        const string BaseColorTexturePath = "Assets/VirtualHand/Models/ElenaFF/ElenaHand_BaseColor.png";
        const string PrefabFolder = "Assets/VirtualHand/Prefabs/ElenaFF";
        const string RightPrefabPath = PrefabFolder + "/ElenaHand_R.prefab";
        const string LeftPrefabPath = PrefabFolder + "/ElenaHand_L.prefab";
        const float WristToMiddleTipMeters = 0.19f;
        const float RelativeSkinMotionThreshold = 0.001f;
        const float RelativeReopenTolerance = 0.0001f;

        [MenuItem("Neuralife/VirtualHand/Build Elena FF Hand Prefabs and Verify")]
        [MenuItem("Assets/Neuralife/Rebuild Hand Prefabs", priority = 100)]
        public static void BuildAll()
        {
            // The native profile must be sampled from the original imported GLB before
            // creating the scaled, mirrored runtime prefab variants.
            ElenaNativeProfileBuilder.Build();
            AssetDatabase.SaveAssets();

            // Force-reimport the source GLB in case the model was updated since the
            // last Unity session (e.g. stripped or re-exported).  Batchmode -quit
            // races the asynchronous asset pipeline, so a synchronous import is needed.
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            var profile = AssetDatabase.LoadAssetAtPath<ElenaNativeRigProfile>(ProfilePath);
            ValidateProfile(profile);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (source == null)
                throw new InvalidOperationException("Imported Elena FF GLB prefab missing: " + ModelPath);

            EnsureAssetFolder(PrefabFolder);
            BuildPrefab(source, profile, RightPrefabPath, mirrored: false);
            BuildPrefab(source, profile, LeftPrefabPath, mirrored: true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            VerifySavedPrefab(RightPrefabPath, profile, expectedMirrored: false);
            VerifySavedPrefab(LeftPrefabPath, profile, expectedMirrored: true);
            Debug.Log("[ElenaHandPrefabBuilder] both prefabs bind and pass open/closed/reopen skin checks. " +
                      "The existing Demo scene was not modified.");
        }

        static void ValidateProfile(ElenaNativeRigProfile profile)
        {
            if (profile == null || !profile.IsValid)
                throw new InvalidOperationException("Native Elena profile is missing or incomplete: " + ProfilePath);
            var paths = new HashSet<string>(profile.bones.Where(b => b != null).Select(b => b.path));
            var missing = Enumerable.Range(0, 20)
                                    .Where(c => !profile.bindings.Any(b => b != null && b.control == c))
                                    .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Native Elena profile does not bind all 20 controls. Missing: " +
                                                    string.Join(", ", missing));
            foreach (var binding in profile.bindings)
                if (binding == null || string.IsNullOrEmpty(binding.bonePath) || !paths.Contains(binding.bonePath))
                    throw new InvalidOperationException("Native Elena profile contains an unresolved control binding.");
        }

        static void BuildPrefab(GameObject source, ElenaNativeRigProfile profile, string path, bool mirrored)
        {
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Could not instantiate Elena FF source GLB: " + ModelPath);

            try
            {
                var wrist = FindTransform(instance.transform, "hand.R_02");
                var middleTip = FindTransform(instance.transform, "middle_tip");
                if (wrist == null || middleTip == null)
                    throw new InvalidOperationException("Elena FF GLB must contain hand.R_02 wrist and middle_tip transforms.");

                float sourceLength = Vector3.Distance(wrist.position, middleTip.position);
                if (sourceLength <= 0.000001f)
                    throw new InvalidOperationException("Elena FF wrist-to-middle-tip length is zero or invalid.");
                float scale = WristToMiddleTipMeters / sourceLength;
                Vector3 scaled = instance.transform.localScale * scale;
                instance.transform.localScale = new Vector3(
                    mirrored ? -Mathf.Abs(scaled.x) : Mathf.Abs(scaled.x),
                    Mathf.Abs(scaled.y), Mathf.Abs(scaled.z));
                float finalLength = Vector3.Distance(wrist.position, middleTip.position);
                if (Mathf.Abs(finalLength - WristToMiddleTipMeters) > 0.001f)
                    throw new InvalidOperationException("Could not scale Elena FF hand to the requested wrist-to-middle-tip length. " +
                                                        "Measured " + finalLength.ToString("0.000") + " m.");

                OptimizeSkinMaterial(instance);
                // Finger-length tuning is applied at runtime by HandRigDriver.Awake
                // (from Resources/Rig/ElenaFF/HandProportions), so it must NOT be
                // baked here as well — that would square the multipliers.

                var driver = instance.GetComponent<HandRigDriver>();
                if (driver == null) driver = instance.AddComponent<HandRigDriver>();
                driver.nativeProfile = profile;
                driver.profileJson = null;
                driver.SetMirrored(mirrored);
                DisableNativePlayback(instance);
                if (!driver.CacheBones() || !driver.HasProfile)
                    throw new InvalidOperationException("Elena FF " + SideName(mirrored) +
                                                        " prefab could not bind its native profile before saving.");

                EnsureAssetFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                bool success;
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, path, out success);
                if (!success || saved == null)
                    throw new InvalidOperationException("Unity could not save Elena FF prefab: " + path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void VerifySavedPrefab(string path, ElenaNativeRigProfile profile, bool expectedMirrored)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
                throw new InvalidOperationException("Generated Elena FF prefab could not be loaded: " + path);
            var instance = PrefabUtility.InstantiatePrefab(asset) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Generated Elena FF prefab could not be instantiated: " + path);

            try
            {
                var driver = instance.GetComponentInChildren<HandRigDriver>(true);
                if (driver == null || driver.nativeProfile != profile)
                    throw new InvalidOperationException("Generated Elena FF prefab has no reference to its native rig profile: " + path);
                if (driver.IsMirrored != expectedMirrored)
                    throw new InvalidOperationException("Generated Elena FF prefab has the wrong mirrored-side setting: " + path);
                DisableNativePlayback(instance);
                if (!driver.CacheBones() || !driver.HasProfile)
                    throw new InvalidOperationException("Generated Elena FF " + SideName(expectedMirrored) +
                                                        " prefab failed to bind its native bones: " + path);

                var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                       .FirstOrDefault(r => r != null && r.sharedMesh != null);
                if (renderer == null || !renderer.enabled || renderer.sharedMesh.vertexCount == 0 ||
                    renderer.bones == null || renderer.bones.Length == 0)
                    throw new InvalidOperationException("Generated Elena FF prefab has no enabled, skinned mesh to verify: " + path);
                VerifySkinChanges(renderer, driver, profile, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        static void VerifySkinChanges(SkinnedMeshRenderer renderer, HandRigDriver driver,
                                     ElenaNativeRigProfile profile, string path)
        {
            var baked = new Mesh { name = "ElenaHandSkinCheck" };
            try
            {
                driver.SetControls(profile.openControls);
                var open = Capture(renderer, baked);
                baked.RecalculateBounds();
                float span = baked.bounds.size.magnitude;
                if (span <= Mathf.Epsilon) throw new InvalidOperationException("Empty open-pose bounds: " + path);
                driver.SetControls(profile.fistControls);
                var closed = Capture(renderer, baked);
                driver.SetControls(profile.openControls);
                var reopened = Capture(renderer, baked);

                float closedMotion = MaxVertexDelta(open, closed) / span;
                float reopenError = MaxVertexDelta(open, reopened) / span;
                if (closedMotion < RelativeSkinMotionThreshold)
                    throw new InvalidOperationException("Elena FF skin check failed for " + path +
                                                        ": the baked skinned mesh did not change between open and closed " +
                                                        "(maximum vertex movement " + closedMotion.ToString("0.000000") + " of the open mesh diagonal).");
                if (reopenError > RelativeReopenTolerance)
                    throw new InvalidOperationException("Elena FF skin check failed for " + path +
                                                        ": the open pose did not return after closing " +
                                                        "(maximum vertex error " + reopenError.ToString("0.000000") + " of the open mesh diagonal).");
                Debug.Log("[ElenaHandPrefabBuilder] skin verified: " + path +
                          " (closed movement " + closedMotion.ToString("0.0000") + ", reopen error " +
                          reopenError.ToString("0.000000") + " of the open mesh diagonal).");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        static Vector3[] Capture(SkinnedMeshRenderer renderer, Mesh baked)
        {
            renderer.BakeMesh(baked);
            return baked.vertices;
        }

        static float MaxVertexDelta(Vector3[] a, Vector3[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
                throw new InvalidOperationException("Baked Elena FF skin vertex counts changed during pose verification.");
            float maxSquared = 0f;
            for (int i = 0; i < a.Length; i++)
                maxSquared = Mathf.Max(maxSquared, (a[i] - b[i]).sqrMagnitude);
            return Mathf.Sqrt(maxSquared);
        }

        static Transform FindTransform(Transform root, string contains)
        {
            return root.GetComponentsInChildren<Transform>(true)
                       .Where(t => t.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                       .OrderBy(t => t.name.Length)
                       .FirstOrDefault();
        }

        static void DisableNativePlayback(GameObject root)
        {
            // Keep the imported Animator/Animation components and their clip references,
            // while leaving pose ownership to the 20-control native rig driver.
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (var animation in root.GetComponentsInChildren<Animation>(true))
                animation.enabled = false;
        }

        /// <summary>
        /// Repairs the glTF-imported skin material and keeps it as a persistent asset
        /// under Materials/Generated.
        ///
        /// The glTFast material uses glTF-style property names, NOT the Built-in
        /// Standard names, so these must be set explicitly:
        ///   _CullMode      0 = Cull Off (double sided) -> 2 = Cull Back
        ///   metallicFactor 0 (skin is non-metal)
        ///   roughnessFactor 1 (metallicRoughness texture carries per-texel roughness)
        ///   baseColorTexture -> the external PNG so art can repaint skin and nails
        /// </summary>
        public static void OptimizeSkinMaterial(GameObject instance)
        {
            var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!renderer || !renderer.sharedMaterial)
                return;

            Material mat = renderer.sharedMaterial;

            if (mat.shader != Shader.Find("Standard") && mat.shader != Shader.Find("glTF/PbrMetallicRoughness"))
                return;

            const string matDir = "Assets/VirtualHand/Materials/Generated";
            EnsureAssetFolder(matDir);
            string matPath = matDir + "/Elena_Skin_" + instance.name + ".mat";

            Material persistent = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!persistent)
            {
                persistent = new Material(mat) { name = "Elena_Skin_" + instance.name };
                AssetDatabase.CreateAsset(persistent, matPath);
            }
            else
            {
                // Keep the artist's texture assignment: only copy from the source
                // material when the persistent asset still points at the GLB sub-asset.
                persistent.CopyPropertiesFromMaterial(mat);
            }

            // glTFast PbrMetallicRoughness property names.
            // roughnessFactor must stay 1: the model's metallicRoughness texture
            // already carries the per-texel roughness, and lowering the factor makes
            // the whole hand read as glossy/oily skin.
            persistent.SetFloat("_CullMode", 2f);          // Cull Back = single sided
            persistent.SetFloat("metallicFactor", 0f);
            persistent.SetFloat("roughnessFactor", 1f);
            persistent.SetColor("baseColorFactor", Color.white);
            persistent.SetFloat("_Mode", 0f);              // Opaque
            persistent.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            persistent.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            persistent.SetInt("_ZWrite", 1);

            // Point the base colour at the editable external PNG when it exists, so
            // skin and nail colours can be repainted without touching the GLB.
            var external = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorTexturePath);
            if (external != null)
                persistent.SetTexture("baseColorTexture", external);

            persistent.renderQueue = -1;
            EditorUtility.SetDirty(persistent);
            renderer.sharedMaterial = persistent;
            Debug.Log("[ElenaHandPrefabBuilder] skin material optimized: " + matPath +
                      " (cull=Back, metallic=0, roughness=1 matte, opaque" +
                      (external != null ? ", base colour = " + BaseColorTexturePath : ", base colour = GLB embedded") + ")");
        }

        static void EnsureAssetFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next) &&
                    string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, parts[i])))
                    throw new InvalidOperationException("Could not create asset folder: " + next);
                current = next;
            }
        }

        static string SideName(bool mirrored) => mirrored ? "left" : "right";
    }
}
