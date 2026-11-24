using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    public static class FirstPersonHandPrefabBuilder
    {
        public const string PrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";

        [MenuItem("Neuralife/VirtualHand/Build First Person Hands")]
        public static void Build()
        {
            var root = new GameObject("FirstPersonHands");
            try
            {
                var pair = root.AddComponent<ControllerHandPair>();
                var pointer = root.AddComponent<ControllerIndexPointer>();
                pointer.handPair = pair;
                BuildHand(root.transform, true, out pair.leftWrist, out pair.leftHand, out pointer.leftIndexAnatomy);
                BuildHand(root.transform, false, out pair.rightWrist, out pair.rightHand, out pointer.rightIndexAnatomy);

                var appearance = root.AddComponent<HandAppearanceController>();
                appearance.blueShader = AssetDatabase.LoadAssetAtPath<Shader>(
                    "Assets/VirtualHand/Shaders/AvatarBlueOutline.shader");
                root.AddComponent<HandInputRouter>();
                root.AddComponent<VirtualHandHost>();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Could not save first-person hand prefab.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("Neuralife/VirtualHand/Export First Person Hands")]
        public static void Export()
        {
            Build();
            SeatedHandSettingsPanelBuilder.BuildPrefab();
            var files = new HashSet<string>(StringComparer.Ordinal);
            const string panel = "Assets/VirtualHand/Prefabs/SeatedHandSettingsPanel.prefab";
            foreach (var path in AssetDatabase.GetDependencies(new[] { PrefabPath, panel }, true))
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) files.Add(path);
            files.Add("Assets/VirtualHand/Scripts/Runtime/SeatedHandSettings.cs");
            files.Add("Assets/VirtualHand/Scripts/Runtime/SeatedWristCalibration.cs");
            files.Add("Assets/VirtualHand/Scripts/Runtime/ContralateralWristPoseMath.cs");
            files.Add("Assets/VirtualHand/Resources/Fonts/NotoSansCJK_Subset.license.txt");
            // The wrist renderer's compatibility profile is referenced in C#, outside serialized dependencies.
            files.Add("Assets/VirtualHand/Scripts/Runtime/HandRigProfile.cs");
            files.Add("Assets/VirtualHand/Scripts/Runtime/HandRigDriver.ThumbClearance.cs");
            files.Add("Assets/VirtualHand/Scripts/VirtualHand.Runtime.asmdef");
            files.Add("Assets/VirtualHand/Models/ElenaFF/ATTRIBUTION.md");
            const string directory = "Builds/FirstPersonHands";
            Directory.CreateDirectory(directory);
            AssetDatabase.ExportPackage(new List<string>(files).ToArray(), directory + "/FirstPersonHands.unitypackage",
                ExportPackageOptions.Default);
            File.Copy("Packages/com.unity.cloud.gltfast-6.13.1.tgz", directory + "/com.unity.cloud.gltfast-6.13.1.tgz", true);
        }

        static void BuildHand(Transform parent, bool left, out FirstPersonWrist wrist, out HandRigDriver driver,
            out ControllerIndexPointer.IndexAnatomy anatomy)
        {
            var mount = new GameObject(left ? "Left wrist" : "Right wrist");
            mount.transform.SetParent(parent, false);
            wrist = mount.AddComponent<FirstPersonWrist>();
            string file = left ? "ElenaHand_L" : "ElenaHand_R";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VirtualHand/Prefabs/ElenaFF/" + file + ".prefab");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, mount.transform);
            driver = visual.GetComponent<HandRigDriver>();
            // Align the anatomical wrist with the grip pivot. Forward runs to the middle MCP, thumb is medial.
            var bones = visual.GetComponentsInChildren<Transform>(true);
            Transform Bone(string name) => Array.Find(bones, t => ElenaNativeRigProfile.MatchesBone(t.name, name))
                ?? throw new InvalidOperationException("Missing Elena landmark " + name);
            var pivot = Bone("hand.R_02");
            Vector3 forward = Bone("middle_01").position - pivot.position;
            Vector3 right = Bone("pinky_01").position - Bone("index_01").position;
            if (left) right = -right;
            var up = Vector3.Cross(forward, right);
            if (up.sqrMagnitude < 1e-12f) throw new InvalidOperationException("Degenerate wrist basis.");
            visual.transform.rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up)) * visual.transform.rotation;
            visual.transform.position += mount.transform.position - pivot.position;
            wrist.visualRoot = visual.transform;
            anatomy = new ControllerIndexPointer.IndexAnatomy {
                baseJoint = Bone("index_03"), tip = Bone("index_03.R_end")
            };
            FirstPersonWristCuffBuilder.Build(visual.transform, pivot, Bone("middle_01"), Bone("index_01"), Bone("pinky_01"), left);
        }
    }
}
