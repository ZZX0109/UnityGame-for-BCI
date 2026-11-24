using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds a standalone hand showcase scene: the two Elena FF hand prefabs under
    /// one root that carries <see cref="VirtualHandShowcase"/>, the shared studio
    /// lighting rig and an orbit camera. It contains no task objects, HUD, replay
    /// data or box, so it doubles as the minimum example of what a host game needs
    /// in order to embed the hand component.
    ///
    /// The scene is written to Scenes/Showcase/ on purpose: VirtualHandBuild builds
    /// every *.unity directly inside Scenes/, so a subfolder keeps this viewer out of
    /// the player scene list while staying one click away.
    /// </summary>
    public static class HandShowcaseSceneBuilder
    {
        const string OutputFolder = "Assets/VirtualHand/Scenes/Showcase";
        const string ScenePath = OutputFolder + "/HandShowcase.unity";
        const string ShaderPath = "Assets/VirtualHand/Shaders/AvatarBlueOutline.shader";

        // Same placement as the task Demo, so the shared OrbitCamera rest framing
        // (PreviewFocus = 0, 0.065, 0) applies unchanged.
        const float GripX = 0.25f;
        const float GripY = 0.06f;

        [MenuItem("Neuralife/VirtualHand/Create Hand Showcase Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            VirtualHandStudioLighting.Create();

            var cameraObject = new GameObject("MainCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 25f;
            camera.fieldOfView = 38f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.20f, 0.24f, 0.25f);
            cameraObject.AddComponent<OrbitCamera>().FrameHands();

            var root = new GameObject("VirtualHandComponent");
            GameObject left = InstantiateHand(root.transform, "L", new Vector3(GripX, GripY, 0f), Vector3.left);
            GameObject right = InstantiateHand(root.transform, "R", new Vector3(-GripX, GripY, 0f), Vector3.right);

            var showcase = root.AddComponent<VirtualHandShowcase>();
            showcase.leftHand = RequireDriver(left);
            showcase.rightHand = RequireDriver(right);
            showcase.startAction = VirtualHandShowcase.ShowcaseAction.Rest;
            showcase.showOnScreenLegend = true;
            showcase.suspendOtherHandDrivers = true;

            var style = root.AddComponent<AvatarVisualStyle>();
            var targets = new List<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer && renderer.enabled)
                    targets.Add(renderer);
            style.targets = targets.ToArray();
            showcase.visualStyle = style;
            AssignBlueShader(style);

            if (!Directory.Exists(OutputFolder))
                Directory.CreateDirectory(OutputFolder);
            AssetDatabase.Refresh();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[HandShowcaseSceneBuilder] scene saved: " + ScenePath +
                      " (" + targets.Count + " renderer targets, " +
                      "keys 1-0 / " + showcase.nextActionKey + "-" + showcase.previousActionKey +
                      " / " + showcase.toggleBlueKey + " blue / " + showcase.playPauseKey + " play-pause). " +
                      "It is not added to the build scene list.");
        }

        static HandRigDriver RequireDriver(GameObject hand)
        {
            var driver = hand.GetComponentInChildren<HandRigDriver>(true);
            if (!driver)
                throw new InvalidOperationException("Elena FF hand has no HandRigDriver: " + hand.name);
            return driver;
        }

        /// <summary>
        /// Stores the shader asset on the component so a player build keeps it:
        /// nothing else references the shader, and the component falls back to
        /// Shader.Find, which cannot find an unreferenced shader after stripping.
        /// Also fails loudly if the serialized field ever disappears again.
        /// </summary>
        static void AssignBlueShader(AvatarVisualStyle style)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader)
                throw new InvalidOperationException("Missing shader asset: " + ShaderPath);

            var serialized = new SerializedObject(style);
            SerializedProperty property = serialized.FindProperty("blueShader");
            if (property == null)
                throw new InvalidOperationException(
                    "AvatarVisualStyle has no serialized 'blueShader' field; the blue style would be stripped from player builds.");
            property.objectReferenceValue = shader;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>Mirrors DemoSceneBuilder.InstantiateHand so both scenes place hands identically.</summary>
        static GameObject InstantiateHand(Transform parent, string side, Vector3 gripPosition, Vector3 inwardDirection)
        {
            string prefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_" + side + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Missing generated Elena FF hand prefab: " + prefabPath);

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Could not instantiate Elena FF hand prefab: " + prefabPath);

            instance.name = "HandRig." + side;
            instance.transform.SetParent(parent, false);

            // Ensure the skin material is single-sided and non-metallic even if the
            // prefab has not been rebuilt recently (belt-and-suspenders).
            ElenaHandPrefabBuilder.OptimizeSkinMaterial(instance);

            var driver = instance.GetComponentInChildren<HandRigDriver>(true);
            if (driver == null || driver.nativeProfile == null)
                throw new InvalidOperationException("Elena FF prefab has no native HandRigDriver profile: " + prefabPath);
            driver.profileJson = null;
            driver.SetMirrored(side == "L");

            Transform wrist = FindNamedTransform(instance.transform, "hand.R_02");
            Transform middleTip = FindNamedTransform(instance.transform, "middle_tip");
            if (wrist == null || middleTip == null)
                throw new InvalidOperationException("Elena FF prefab is missing its wrist or middle-tip transform: " + prefabPath);

            instance.transform.rotation = Quaternion.identity;
            Vector3 wristLocal = instance.transform.InverseTransformPoint(wrist.position);
            Vector3 tipLocal = instance.transform.InverseTransformPoint(middleTip.position);
            Vector3 handAxis = Vector3.Scale(tipLocal - wristLocal, instance.transform.localScale).normalized;
            if (handAxis.sqrMagnitude < 1e-8f || inwardDirection.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException("Could not determine the Elena FF hand axis for placement: " + prefabPath);

            instance.transform.rotation = Quaternion.FromToRotation(handAxis, inwardDirection.normalized);
            instance.transform.position += gripPosition - wrist.position;
            return instance;
        }

        static Transform FindNamedTransform(Transform root, string contains)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            return null;
        }
    }
}
