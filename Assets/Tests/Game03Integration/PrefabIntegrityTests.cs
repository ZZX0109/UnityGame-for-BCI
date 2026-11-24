using System.Collections.Generic;
using NUnit.Framework;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using UnityEditor;
using UnityEngine;

namespace StreetCrossing.Integration.Tests
{
    /// <summary>
    /// Structural checks on the FirstPersonFullBody prefab, run against the loaded
    /// prefab asset (no YAML parsing). Expectations come from the Game04 builder:
    /// 53 skeleton bones, no GameIntegrationAdapter, no colliders.
    /// Bone counting is restricted to body-surface renderers: instantiating the prefab
    /// also exposes the nested ElenaHand prefab instances with GLB-bound hand renderers
    /// (69-bone GLB skeleton), which must not enter the body-skeleton union.
    /// </summary>
    [TestFixture]
    public class PrefabIntegrityTests
    {
        const string PrefabPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";

        static GameObject LoadPrefab()
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        [Test]
        public void Prefab_IsLoadable()
        {
            Assert.IsNotNull(LoadPrefab(), $"Expected the full-body prefab at '{PrefabPath}'.");
        }

        [Test]
        public void Prefab_ContainsBodyComponents_ButNoAdapter()
        {
            var prefab = LoadPrefab();
            Assert.IsNotNull(prefab, "Prefab must be loadable before checking contents.");

            Assert.IsTrue(prefab.GetComponentInChildren<FullBodyPoseController>(true) != null, "FullBodyPoseController missing.");
            Assert.IsTrue(prefab.GetComponentInChildren<LowerBodyPoseController>(true) != null, "LowerBodyPoseController missing.");
            Assert.IsTrue(prefab.GetComponentInChildren<WalkCycleDriver>(true) != null, "WalkCycleDriver missing.");
            Assert.IsTrue(prefab.GetComponentInChildren<FirstPersonVisibility>(true) != null, "FirstPersonVisibility missing.");
            Assert.IsTrue(prefab.GetComponentInChildren<FullBodyAppearanceController>(true) != null, "FullBodyAppearanceController missing.");

            Assert.IsTrue(prefab.GetComponentInChildren<GameIntegrationAdapter>(true) == null,
                "GameIntegrationAdapter must be absent from the prefab by design (hosts attach it themselves).");
        }

        /// <summary>
        /// Positive discriminator for body-surface renderers: any renderer bound to the
        /// body skeleton necessarily references the "pelvis" bone, which no hand GLB
        /// skeleton contains. Deliberately NOT bones.Length — that would be circular
        /// with the per-renderer count asserted below.
        /// </summary>
        static bool BindsBodySkeleton(SkinnedMeshRenderer renderer)
        {
            foreach (var bone in renderer.bones)
                if (bone != null && string.Equals(bone.name, "pelvis", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        [Test]
        public void Prefab_SkeletonBoneUnion_Is53()
        {
            var prefab = LoadPrefab();
            Assert.IsNotNull(prefab, "Prefab must be loadable before counting bones.");

            // GetComponentsInChildren(true) also reaches the nested ElenaHand hand prefab
            // instances (GLB-bound renderers), so filter to body-surface renderers first.
            var bodyRenderers = new List<SkinnedMeshRenderer>();
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (BindsBodySkeleton(renderer))
                    bodyRenderers.Add(renderer);

            Assert.That(bodyRenderers.Count, Is.GreaterThanOrEqualTo(1),
                "At least one body-surface renderer bound to the body skeleton must exist.");

            foreach (var renderer in bodyRenderers)
                Assert.AreEqual(53, renderer.bones.Length,
                    $"Body-surface renderer '{renderer.name}' must bind the full 53-bone skeleton.");

            var bones = new HashSet<Transform>();
            foreach (var renderer in bodyRenderers)
                foreach (var bone in renderer.bones)
                    if (bone != null)
                        bones.Add(bone);

            Assert.AreEqual(53, bones.Count,
                "Union of SkinnedMeshRenderer.bones across all body-surface renderers must be the full 53-bone skeleton.");
        }

        [Test]
        public void Prefab_HasNoColliders()
        {
            var prefab = LoadPrefab();
            Assert.IsNotNull(prefab, "Prefab must be loadable before checking colliders.");
            Assert.IsEmpty(prefab.GetComponentsInChildren<Collider>(true),
                "The body must not add colliders: the host CharacterController and interact raycast own physics.");
        }
    }
}
