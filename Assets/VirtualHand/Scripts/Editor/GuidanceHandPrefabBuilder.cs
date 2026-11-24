using System;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds the standalone teaching hand used by <c>GuidanceHandController</c>.
    ///
    /// It is a separate prefab on purpose: the guidance hand must be able to play a
    /// demonstration while the player's own hands keep following real tracking. If
    /// guidance reused the player's prefab, the two would fight over the same
    /// skeleton and the "watch this" overlay would overwrite the player's real hand.
    ///
    /// The prefab carries its own driver, is forced to the blue transparent style so
    /// it reads as an overlay, and has no host, no input router and no wrist tracker
    /// so nothing can drive it except GuidanceHandController.
    /// </summary>
    public static class GuidanceHandPrefabBuilder
    {
        public const string PrefabPath = "Assets/VirtualHand/Prefabs/GuidanceHand.prefab";
        const string BlueShaderPath = "Assets/VirtualHand/Shaders/AvatarBlueOutline.shader";

        [MenuItem("Neuralife/VirtualHand/Build Guidance Hand Prefab")]
        public static void Build()
        {
            BuildInternal();
            Debug.Log("[GuidanceHandPrefabBuilder] wrote " + PrefabPath);
        }

        /// <summary>Called by the showcase scene builder so both stay in step.</summary>
        internal static GameObject BuildInternal()
        {
            var root = new GameObject("GuidanceHand");
            try
            {
                var left = BuildHand(root.transform, "ElenaHand_L", "Guidance hand L");
                var right = BuildHand(root.transform, "ElenaHand_R", "Guidance hand R");

                var controller = root.AddComponent<GuidanceHandController>();
                controller.guidanceDriver = left.driver;
                controller.secondGuidanceDriver = right.driver;
                controller.guidanceVisualStyle = left.style;
                controller.loop = true;
                controller.speed = 1f;

                // Both hands share one visual style component owning both targets, so a
                // single blue toggle covers the whole guidance overlay.
                left.style.targets = new Renderer[] { left.renderer, right.renderer };
                UnityEngine.Object.DestroyImmediate(right.style);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                if (!ok || !saved)
                    throw new InvalidOperationException("Could not save guidance hand prefab.");

                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static (HandRigDriver driver, AvatarVisualStyle style, Renderer renderer) BuildHand(
            Transform parent, string sourceName, string instanceName)
        {
            string sourcePath = "Assets/VirtualHand/Prefabs/ElenaFF/" + sourceName + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source)
                throw new InvalidOperationException("Missing hand prefab: " + sourcePath);

            var mount = new GameObject(instanceName);
            mount.transform.SetParent(parent, false);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, mount.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            var renderer = visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (!renderer)
                throw new InvalidOperationException(sourceName + " has no SkinnedMeshRenderer.");

            var driver = visual.GetComponent<HandRigDriver>();
            if (!driver)
                driver = visual.AddComponent<HandRigDriver>();

            // The teaching hand is decorative: keep it out of every input path.
            var style = visual.AddComponent<AvatarVisualStyle>();
            style.targets = new Renderer[] { renderer };
            AssignBlueShader(style);
            style.interiorAlpha = 0.35f;
            style.rimAlpha = 0.9f;

            return (driver, style, renderer);
        }

        /// <summary>
        /// Assigns the outline shader on AvatarVisualStyle. The field is private
        /// serialized state, so it can only be written through SerializedObject —
        /// and assigning it keeps the shader referenced in player builds instead of
        /// relying on Shader.Find, which gets stripped.
        /// </summary>
        static void AssignBlueShader(AvatarVisualStyle style)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(BlueShaderPath);
            if (!shader)
                throw new InvalidOperationException("Missing blue outline shader: " + BlueShaderPath);

            var serialized = new SerializedObject(style);
            var property = serialized.FindProperty("blueShader");
            if (property == null)
                throw new InvalidOperationException("AvatarVisualStyle has no serialized 'blueShader' field.");

            property.objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}