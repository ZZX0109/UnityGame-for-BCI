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
        const string PrefabFolder = "Assets/VirtualHand/Prefabs/ElenaFF";
        const string RightPrefabPath = PrefabFolder + "/ElenaHand_R.prefab";
        const string LeftPrefabPath = PrefabFolder + "/ElenaHand_L.prefab";
        const float WristToMiddleTipMeters = 0.19f;
        const float RelativeSkinMotionThreshold = 0.001f;
        const float RelativeReopenTolerance = 0.0001f;

        [MenuItem("Neuralife/VirtualHand/Build Elena FF Hand Prefabs and Verify")]
        public static void BuildAll()
        {
            // The native profile must be sampled from the original imported GLB before
            // creating the scaled, mirrored runtime prefab variants.
            ElenaNativeProfileBuilder.Build();
            AssetDatabase.SaveAssets();

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
        /// Fixes the glTF-imported material for skin: disables back-face rendering
        /// to prevent internal-surface stacking with the blue shader, and sets skin-
        /// appropriate metallic (0) and smoothness (0.65, from the roughness texture G
        /// channel). The material is saved as a persistent asset under
        /// Materials/Generated so scene reloads and player builds keep the fix.
        /// </summary>
        public static void OptimizeSkinMaterial(GameObject instance)
        {
            var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!renderer || !renderer.sharedMaterial)
                return;

            Material mat = renderer.sharedMaterial;

            // Only patch materials that look like a glTF import (Standard shader,
            // named after the GLB or the generic "lambert1").
            if (mat.shader != Shader.Find("Standard") && mat.shader != Shader.Find("glTF/PbrMetallicRoughness"))
                return;

            const string matDir = "Assets/VirtualHand/Materials/Generated";
            EnsureAssetFolder(matDir);
            string matPath = matDir + "/Elena_Skin_" + instance.name + ".mat";

            // Create a persistent copy once; reuses the existing asset on rebuilds.
            Material persistent = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!persistent)
            {
                persistent = new Material(mat) { name = "Elena_Skin_" + instance.name };
                AssetDatabase.CreateAsset(persistent, matPath);
            }
            else
            {
                persistent.CopyPropertiesFromMaterial(mat);
            }

            persistent.SetFloat("_Metallic", 0f);
            persistent.SetFloat("_Glossiness", 0.5f);
            persistent.SetFloat("_GlossMapScale", 0.5f);
            persistent.SetFloat("_Mode", 0f);           // Opaque
            persistent.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            persistent.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            persistent.SetInt("_ZWrite", 1);
            persistent.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            persistent.renderQueue = -1;

            EditorUtility.SetDirty(persistent);
            renderer.sharedMaterial = persistent;
            Debug.Log("[ElenaHandPrefabBuilder] skin material optimized: " + matPath +
                      " (metallic=0, smoothness=0.5, opaque single-sided, specular on)");
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
