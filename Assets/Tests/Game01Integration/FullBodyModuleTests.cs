#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
// EditMode regression tests for the Game01 integration of the VirtualHand
// full-body prefab (Assets/VirtualHand/Prefabs/FirstPersonFullBody.prefab).
// Each test class is independently runnable from the Unity Test Runner and
// touches no rendering pixels, no PlayMode loop and no graphics device state.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;

namespace Game01Integration.Tests
{
    /// <summary>
    /// Shared plumbing for prefab based EditMode tests. Instantiates the
    /// FirstPersonFullBody prefab and guarantees deterministic cleanup.
    /// </summary>
    public abstract class FirstPersonFullBodyTestBase
    {
        protected const string PrefabPath = "Assets/VirtualHand/Prefabs/FirstPersonFullBody.prefab";
        protected const int ExpectedBoneCount = 53; // 52 named bones + 1 rig root (see Logs/fullbody-import-report.txt: SMR bones=53)

        /// <summary>22 core skeleton bones, named exactly as in the FBX rig.</summary>
        protected static readonly string[] CoreBones =
        {
            "pelvis",
            "spine_01", "spine_02", "spine_03",
            "neck_01", "head",
            "clavicle_l", "clavicle_r",
            "upperarm_l", "upperarm_r",
            "lowerarm_l", "lowerarm_r",
            "hand_l", "hand_r",
            "thigh_l", "thigh_r",
            "calf_l", "calf_r",
            "foot_l", "foot_r",
            "ball_l", "ball_r"
        };

        /// <summary>15 per-side finger bones; the FBX appends _l/_r to these base names.</summary>
        protected static readonly string[] FingerBones =
        {
            "index_01", "index_02", "index_03",
            "middle_01", "middle_02", "middle_03",
            "ring_01", "ring_02", "ring_03",
            "pinky_01", "pinky_02", "pinky_03",
            "thumb_01", "thumb_02", "thumb_03"
        };

        protected GameObject LoadPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"Prefab missing at '{PrefabPath}'.");
            return prefab;
        }

        protected GameObject InstantiateRoot()
        {
            GameObject root = Object.Instantiate(LoadPrefab());
            Assert.IsNotNull(root, "Prefab instantiation failed.");
            root.name = "FirstPersonFullBody_TestInstance";
            return root;
        }

        protected void DestroyRoot(GameObject root)
        {
            if (root != null)
                Object.DestroyImmediate(root);
        }

        /// <summary>
        /// True when the renderer is bound to the first-person body skeleton.
        /// The body skeleton is the only bone set containing the pelvis root;
        /// nested first-person hand prefab instances additionally bind their
        /// own 69-bone GLB hand rig and must not pollute body assertions.
        /// </summary>
        protected static bool BindsBodySkeleton(SkinnedMeshRenderer renderer)
        {
            Transform[] bones = renderer.bones;
            if (bones == null || bones.Length == 0)
                return false;
            foreach (Transform bone in bones)
            {
                if (bone != null && bone.name == "pelvis")
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Collects the unique bone names referenced by every body-skeleton
        /// skinned renderer (nested hand-rig renderers excluded).
        /// </summary>
        protected HashSet<string> CollectBoneNames(GameObject root)
        {
            HashSet<string> names = new HashSet<string>();
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.GreaterOrEqual(renderers.Length, 1, "Prefab instantiates no SkinnedMeshRenderer.");
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (!BindsBodySkeleton(renderer))
                    continue;
                foreach (Transform bone in renderer.bones)
                {
                    if (bone != null)
                        names.Add(bone.name);
                }
            }
            return names;
        }

        protected static void AssertAllNamedBonesPresent(HashSet<string> boneNames)
        {
            foreach (string core in CoreBones)
                Assert.IsTrue(boneNames.Contains(core), $"Core bone '{core}' missing from skeleton.");

            foreach (string finger in FingerBones)
            {
                Assert.IsTrue(boneNames.Contains(finger + "_l"), $"Left finger bone '{finger}_l' missing from skeleton.");
                Assert.IsTrue(boneNames.Contains(finger + "_r"), $"Right finger bone '{finger}_r' missing from skeleton.");
            }
        }

        protected static void AssertAreEqual(Vector3 expected, Vector3 actual, float delta, string message)
        {
            Assert.IsTrue((expected - actual).magnitude <= delta,
                $"{message} expected {expected} actual {actual} (delta {delta}).");
        }
    }

    /// <summary>
    /// Asserts the full-body module of the instantiated prefab: complete
    /// skeleton, required runtime components, adapter wiring idempotency,
    /// seated/standing pose switching and first-person visibility semantics.
    /// </summary>
    public sealed class FullBodyModuleTests : FirstPersonFullBodyTestBase
    {
        [Test]
        public void PrefabLoadsAndInstantiates()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                Assert.AreEqual("FirstPersonFullBody", LoadPrefab().name,
                    "Loaded prefab asset name changed unexpectedly.");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void SkeletonContainsCompleteBoneSet()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                HashSet<string> boneNames = CollectBoneNames(root);

                Assert.AreEqual(ExpectedBoneCount, boneNames.Count,
                    "Skeleton bone count mismatch (52 named bones + 1 rig root expected).");
                AssertAllNamedBonesPresent(boneNames);
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void SkinnedMeshRenderersCarryFullSkeleton()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.GreaterOrEqual(renderers.Length, 1, "Prefab instantiates no SkinnedMeshRenderer.");

                int bodyRendererCount = 0;
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    if (!BindsBodySkeleton(renderer))
                        continue; // nested hand-rig renderers bind their own 69-bone GLB skeleton
                    bodyRendererCount++;
                    Assert.AreEqual(ExpectedBoneCount, renderer.bones.Length,
                        $"Body renderer '{renderer.name}' does not bind the complete {ExpectedBoneCount}-bone skeleton.");
                }
                Assert.GreaterOrEqual(bodyRendererCount, 1,
                    "No renderer binds the body skeleton (no pelvis bone found).");

                AssertAllNamedBonesPresent(CollectBoneNames(root));
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void RequiredComponentsPresent()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                Assert.IsNotNull(root.GetComponentInChildren<WalkCycleDriver>(true),
                    "WalkCycleDriver missing from prefab.");
                Assert.IsNotNull(root.GetComponentInChildren<LowerBodyPoseController>(true),
                    "LowerBodyPoseController missing from prefab.");
                Assert.IsNotNull(root.GetComponentInChildren<FullBodyPoseController>(true),
                    "FullBodyPoseController missing from prefab.");
                Assert.IsNotNull(root.GetComponentInChildren<FirstPersonVisibility>(true),
                    "FirstPersonVisibility missing from prefab.");
                Assert.IsNotNull(root.GetComponentInChildren<FullBodyAppearanceController>(true),
                    "FullBodyAppearanceController missing from prefab.");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void GameIntegrationAdapterWiringIsIdempotent()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                GameIntegrationAdapter adapter = root.AddComponent<GameIntegrationAdapter>();

                // First public call wires the private references (Wire()).
                adapter.SetSeated(false);
                FullBodyPoseController pose = adapter.poseController;
                LowerBodyPoseController locomotion = adapter.locomotion;
                FirstPersonVisibility visibility = adapter.visibility;
                FullBodyAppearanceController appearance = adapter.appearance;

                Assert.IsNotNull(pose, "Adapter did not resolve FullBodyPoseController.");
                Assert.IsNotNull(locomotion, "Adapter did not resolve LowerBodyPoseController.");
                Assert.IsNotNull(visibility, "Adapter did not resolve FirstPersonVisibility.");
                Assert.IsNotNull(appearance, "Adapter did not resolve FullBodyAppearanceController.");

                // Repeated calls must never re-resolve or replace the wired references.
                adapter.SetSeated(true);
                adapter.SetSeated(false);
                adapter.SetFirstPerson(true);
                adapter.SetFirstPerson(false);

                Assert.AreEqual(pose, adapter.poseController, "poseController re-wired by later calls.");
                Assert.AreEqual(locomotion, adapter.locomotion, "locomotion re-wired by later calls.");
                Assert.AreEqual(visibility, adapter.visibility, "visibility re-wired by later calls.");
                Assert.AreEqual(appearance, adapter.appearance, "appearance re-wired by later calls.");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void SetSeatedSwitchesCurrentPoseName()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                GameIntegrationAdapter adapter = root.AddComponent<GameIntegrationAdapter>();

                adapter.SetSeated(false);
                Assert.AreEqual("Standing", adapter.poseController.CurrentPoseName,
                    "SetSeated(false) must select the 'Standing' idle pose.");

                adapter.SetSeated(true);
                Assert.AreEqual("NaturalSeated", adapter.poseController.CurrentPoseName,
                    "SetSeated(true) must select the 'NaturalSeated' idle pose.");
            }
            finally
            {
                DestroyRoot(root);
            }
        }

        [Test]
        public void FirstPersonVisibilityHidesOnlyHeadRenderers()
        {
            GameObject root = null;
            try
            {
                root = InstantiateRoot();
                FirstPersonVisibility visibility = root.GetComponentInChildren<FirstPersonVisibility>(true);
                Assert.IsNotNull(visibility, "FirstPersonVisibility missing from prefab.");
                Assert.IsNotNull(visibility.HiddenRenderers, "HiddenRenderers list not wired in prefab.");
                Assert.GreaterOrEqual(visibility.HiddenRenderers.Length, 1,
                    "HiddenRenderers not wired in prefab; first person would hide nothing.");
                Assert.IsFalse(visibility.FirstPersonActive, "Prefab must start in third person.");

                Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
                Dictionary<Renderer, bool> initial = new Dictionary<Renderer, bool>();
                foreach (Renderer renderer in all)
                    initial[renderer] = renderer.enabled;

                HashSet<Renderer> hiddenSet = new HashSet<Renderer>(visibility.HiddenRenderers);

                visibility.SetFirstPerson(true);
                Assert.IsTrue(visibility.FirstPersonActive, "FirstPersonActive must be true after SetFirstPerson(true).");
                foreach (Renderer hidden in visibility.HiddenRenderers)
                    Assert.IsFalse(hidden.enabled, $"Hidden renderer '{hidden.name}' still enabled in first person.");
                foreach (Renderer renderer in all)
                {
                    if (!hiddenSet.Contains(renderer))
                        Assert.AreEqual(initial[renderer], renderer.enabled,
                            $"Non-hidden renderer '{renderer.name}' changed state in first person.");
                }

                visibility.SetFirstPerson(false);
                Assert.IsFalse(visibility.FirstPersonActive, "FirstPersonActive must be false after SetFirstPerson(false).");
                foreach (Renderer renderer in all)
                    Assert.AreEqual(initial[renderer], renderer.enabled,
                        $"Renderer '{renderer.name}' not restored after leaving first person.");
            }
            finally
            {
                DestroyRoot(root);
            }
        }
    }
}
#endif
