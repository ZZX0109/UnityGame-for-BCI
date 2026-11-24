#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
// EditMode integrity checks for Assets/VirtualHand/Prefabs/FirstPersonFullBody.prefab.
// Pure asset/skeleton inspection: no PlayMode, no rendering, no pixel assertions.
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game01Integration.Tests
{
    public sealed class PrefabIntegrityTests : FirstPersonFullBodyTestBase
    {
        private const float MaxChainLocalPosition = 1f;

        private static readonly Regex ScriptGuidRegex =
            new Regex(@"m_Script:\s*\{fileID:\s*11500000,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}",
                RegexOptions.Compiled);

        private static readonly Regex NestedPrefabGuidRegex =
            new Regex(@"m_SourcePrefab:\s*\{fileID:\s*100100000,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}",
                RegexOptions.Compiled);

        private static string PrefabFileSystemPath =>
            Path.Combine(Application.dataPath, "VirtualHand/Prefabs/FirstPersonFullBody.prefab");

        private static HashSet<string> ExtractGuids(string yaml, Regex regex)
        {
            HashSet<string> guids = new HashSet<string>();
            foreach (Match match in regex.Matches(yaml))
                guids.Add(match.Groups[1].Value);
            return guids;
        }

        [Test]
        public void AllScriptGuidsResolve()
        {
            string yaml = File.ReadAllText(PrefabFileSystemPath);
            HashSet<string> guids = ExtractGuids(yaml, ScriptGuidRegex);
            Assert.GreaterOrEqual(guids.Count, 1, "Prefab declares no script references.");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Assert.IsFalse(string.IsNullOrEmpty(assetPath),
                    $"Script guid '{guid}' does not resolve inside this project.");
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                Assert.IsNotNull(script, $"Guid '{guid}' resolves to '{assetPath}' which is not a MonoScript.");
                Assert.IsFalse(string.IsNullOrEmpty(script.GetClass() == null ? null : script.GetClass().Name),
                    $"Script '{assetPath}' exposes no class.");
            }
        }

        [Test]
        public void KnownBodyScriptsAreWired()
        {
            string yaml = File.ReadAllText(PrefabFileSystemPath);
            HashSet<string> guids = ExtractGuids(yaml, ScriptGuidRegex);
            HashSet<string> resolved = new HashSet<string>();
            foreach (string guid in guids)
                resolved.Add(AssetDatabase.GUIDToAssetPath(guid));

            Assert.IsTrue(resolved.Contains("Assets/VirtualHand/Scripts/Runtime/Body/WalkCycleDriver.cs"),
                "Prefab does not reference WalkCycleDriver.cs.");
            Assert.IsTrue(resolved.Contains("Assets/VirtualHand/Scripts/Runtime/Body/LowerBodyPoseController.cs"),
                "Prefab does not reference LowerBodyPoseController.cs.");
            Assert.IsTrue(resolved.Contains("Assets/VirtualHand/Scripts/Runtime/Body/FullBodyPoseController.cs"),
                "Prefab does not reference FullBodyPoseController.cs.");
            Assert.IsTrue(resolved.Contains("Assets/VirtualHand/Scripts/Runtime/Body/FirstPersonVisibility.cs"),
                "Prefab does not reference FirstPersonVisibility.cs.");
            Assert.IsTrue(resolved.Contains("Assets/VirtualHand/Scripts/Runtime/Body/FullBodyAppearanceController.cs"),
                "Prefab does not reference FullBodyAppearanceController.cs.");
        }

        [Test]
        public void NestedSkeletonPrefabGuidResolves()
        {
            string yaml = File.ReadAllText(PrefabFileSystemPath);
            HashSet<string> guids = ExtractGuids(yaml, NestedPrefabGuidRegex);
            Assert.GreaterOrEqual(guids.Count, 1, "Prefab declares no nested source prefab (skeleton FBX missing).");

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Assert.IsFalse(string.IsNullOrEmpty(assetPath),
                    $"Nested prefab guid '{guid}' does not resolve inside this project.");
            }
        }

        [Test]
        public void SpineChainLocalPositionsStayWithinOneMeter()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();

                Transform current = null;
                string[] chain = { "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head" };
                foreach (string boneName in chain)
                {
                    Transform bone = FindBoneExact(root, boneName);
                    Assert.IsNotNull(bone, $"Bone '{boneName}' missing from instantiated skeleton.");
                    Assert.AreEqual(current, bone.parent,
                        $"Bone '{boneName}' is not parented to the previous chain bone.");
                    AssertAreEqual(Vector3.zero, bone.localPosition, MaxChainLocalPosition,
                        $"Bone '{boneName}' local position exceeds one meter.");
                    current = bone;
                }
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        private static Transform FindBoneExact(GameObject root, string boneName)
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                if (candidate.name == boneName)
                    return candidate;
            }
            return null;
        }
    }
}
#endif
