using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Neuralife.VirtualHand.Body;
using StreetCrossing.Integration;
using StreetCrossing.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace StreetCrossing.Integration.Tests
{
    /// <summary>
    /// M2 PlayMode coverage: the body skin tint applier that
    /// <see cref="StreetCrossingFullBodyBinding"/> wires into the full-body instance.
    ///
    /// Silence contract: every true wiring emits exactly the 7 log lines consumed by
    /// StreetCrossing.Tests.PlayMode.FullBodyWiringLogs (replicated below — that helper
    /// is internal to another assembly). The applier's success path must stay silent,
    /// so no expectation beyond the wiring block is registered anywhere.
    ///
    /// Blue-mode hazard: AvatarVisualStyle snapshots the renderer slots once (first Blue
    /// entry) and restores them verbatim, so the tint must land before the first Blue
    /// switch; the hazard test pins the deferred-apply and reference-exact restore.
    /// </summary>
    [TestFixture]
    public class BodySkinTintPlayModeTests
    {
        const float TintTolerance = 0.02f;
        const string BodyPrefabPath = "Assets/StreetCrossing/Resources/FirstPersonFullBody.prefab";
        const string HandSkinMaterialPath = "Assets/VirtualHand/Materials/Generated/Elena_Skin_ElenaHand_R.mat";

        GameObject _player;
        GameObject _bindingHost;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("TestPedestrianPlayer");
            _player.AddComponent<PedestrianPlayerController>(); // RequireComponent adds CharacterController
        }

        [TearDown]
        public void TearDown()
        {
            if (_bindingHost != null)
                Object.Destroy(_bindingHost);
            if (_player != null)
                Object.Destroy(_player);

            // EnsureWiring spawns the Elena hands; sweep any survivors so the scene scan
            // in the next test cannot pick up a stale hand from this one.
            GameObject[] leftovers = Object.FindObjectsOfType<GameObject>();
            for (int i = 0; i < leftovers.Length; i++)
            {
                if (leftovers[i] != null && leftovers[i].name.StartsWith("ElenaHand_"))
                    Object.Destroy(leftovers[i]);
            }
        }

        StreetCrossingFullBodyBinding CreateWiredBinding()
        {
            ExpectWiringSequence(); // every true wiring emits the alignment result
            _bindingHost = new GameObject("TestFullBodyBinding");
            var binding = _bindingHost.AddComponent<StreetCrossingFullBodyBinding>();
            Assert.IsTrue(binding.EnsureWiring(), "EnsureWiring must succeed once a PedestrianPlayerController exists.");
            return binding;
        }

        [Test]
        public void Wiring_AppliesSkinTintSilently_AndIsIdempotentPerInstance()
        {
            var binding = CreateWiredBinding();

            var applier = binding.BodyInstance.GetComponent<BodySkinTintApplier>();
            Assert.IsNotNull(applier, "Wiring must attach the BodySkinTintApplier to the body instance.");
            Assert.IsTrue(applier.Applied, "Tint must land during wiring; Blue mode cannot be active yet.");

            HashSet<Material> assetMaterials = CollectAssetMaterials();
            Material cloneBefore = FindAnyClone(binding.BodyInstance, assetMaterials);
            Assert.IsNotNull(cloneBefore, "Wiring must replace at least one slot with a tinted runtime clone.");

            Assert.IsTrue(binding.EnsureWiring(), "EnsureWiring must be idempotent.");
            Material cloneAfter = FindAnyClone(binding.BodyInstance, assetMaterials);
            Assert.AreSame(cloneBefore, cloneAfter,
                "Idempotent re-wiring must keep the same clone references (per-instance guard, no re-tint).");
        }

        [UnityTest]
        public IEnumerator Rewiring_AfterBodyDestroyed_ProducesFreshClonesWithoutLeaks()
        {
            var binding = CreateWiredBinding();
            HashSet<Material> assetMaterials = CollectAssetMaterials();
            Material firstClone = FindAnyClone(binding.BodyInstance, assetMaterials);
            Assert.IsNotNull(firstClone, "Initial wiring must produce a tinted clone.");

            Object.Destroy(binding.BodyInstance);
            yield return null; // let Destroy + applier OnDestroy (clone cleanup) settle

            Assert.IsTrue(firstClone == null,
                "The destroyed applier's OnDestroy must destroy the clones it produced.");

            ExpectWiringSequence(); // a true re-wiring re-fires the alignment result
            Assert.IsTrue(binding.EnsureWiring(), "EnsureWiring must re-wire after the body instance is destroyed.");
            Assert.IsNotNull(binding.BodyInstance, "Re-wiring must instantiate a fresh body.");

            var applier = binding.BodyInstance.GetComponent<BodySkinTintApplier>();
            Assert.IsNotNull(applier, "Re-wiring must attach a fresh applier instance.");
            Assert.IsTrue(applier.Applied, "The fresh applier must tint the fresh body.");
            Material secondClone = FindAnyClone(binding.BodyInstance, assetMaterials);
            Assert.IsNotNull(secondClone, "Re-wired body must receive fresh tint clones.");
        }

        [Test]
        public void TintedClone_CombinedColor_MatchesHandSkinWithinTolerance()
        {
            var binding = CreateWiredBinding();
            var applier = binding.BodyInstance.GetComponent<BodySkinTintApplier>();
            Assert.IsTrue(applier.Applied);

            Material hand = applier.TargetSkinMaterial;
            Assert.IsNotNull(hand, "Applier must have resolved the hand skin material from the spawned hands.");

            Color handCombined;
            Assert.IsTrue(SkinColorSampling.TryGetCombinedColor(hand, out handCombined),
                "Hand skin sampling must succeed in play mode.");

            HashSet<Material> assetMaterials = CollectAssetMaterials();
            int checkedClones = 0;
            Renderer[] renderers = binding.BodyInstance.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] slots = renderers[r].sharedMaterials;
                for (int s = 0; s < slots.Length; s++)
                {
                    Material slot = slots[s];
                    if (slot == null || assetMaterials.Contains(slot))
                        continue; // not a runtime clone

                    Color cloneCombined;
                    Assert.IsTrue(SkinColorSampling.TryGetCombinedColor(slot, out cloneCombined),
                        "Clone sampling must succeed on the tinted clone.");

                    AssertAreEqual(handCombined.r, cloneCombined.r, TintTolerance,
                        "Clone combined R must match the hand skin within tolerance.");
                    AssertAreEqual(handCombined.g, cloneCombined.g, TintTolerance,
                        "Clone combined G must match the hand skin within tolerance.");
                    AssertAreEqual(handCombined.b, cloneCombined.b, TintTolerance,
                        "Clone combined B must match the hand skin within tolerance.");
                    AssertAreEqual(1f, cloneCombined.a, TintTolerance, "Clone alpha must stay opaque.");

                    checkedClones++;
                }
            }
            Assert.Greater(checkedClones, 0, "At least one tinted clone must be present after wiring.");
        }

        [UnityTest]
        public IEnumerator BlueFirst_TintDefers_AndRealisticRestoreReturnsOriginals()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);
            Assert.IsNotNull(prefab, "Body prefab must exist at " + BodyPrefabPath);

            // Expected restore targets, captured from the ASSET before instantiating —
            // the prefab asset itself is never mutated by these tests.
            Renderer[] assetRenderers = prefab.GetComponentsInChildren<Renderer>(true);
            var originals = new List<Material[]>(assetRenderers.Length);
            for (int i = 0; i < assetRenderers.Length; i++)
                originals.Add(assetRenderers[i].sharedMaterials);

            GameObject body = Object.Instantiate(prefab);
            var controller = body.GetComponent<FullBodyAppearanceController>();
            Assert.IsNotNull(controller, "Prefab root ships with the FullBodyAppearanceController.");
            Assert.IsNull(body.GetComponent<BodySkinTintApplier>(),
                "Prefab must not bake the runtime applier; wiring adds it.");
            controller.BodyRenderers = body.GetComponentsInChildren<Renderer>(true);

            // Hazard: Blue owns the slots first, so the snapshot is the untinted state.
            controller.SetMode(FullBodyAppearanceMode.Blue);

            var applier = body.AddComponent<BodySkinTintApplier>();
            applier.TargetSkinMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(HandSkinMaterialPath);
            Assert.IsNotNull(applier.TargetSkinMaterial, "Hand skin material must load for the explicit path.");

            Assert.IsFalse(applier.Apply(),
                "Apply must defer while Blue mode owns the slots (snapshot-race guard).");
            Assert.IsFalse(applier.Applied, "A deferred apply must not be marked applied.");

            controller.SetMode(FullBodyAppearanceMode.Realistic);
            yield return null;

            Renderer[] liveRenderers = body.GetComponentsInChildren<Renderer>(true);
            Assert.AreEqual(assetRenderers.Length, liveRenderers.Length, "Hierarchy must be unchanged.");
            for (int i = 0; i < liveRenderers.Length; i++)
            {
                Material[] live = liveRenderers[i].sharedMaterials;
                Material[] expected = originals[i];
                Assert.AreEqual(expected.Length, live.Length,
                    "Slot count must survive the Blue cycle on " + liveRenderers[i].name + ".");
                for (int s = 0; s < live.Length; s++)
                    Assert.AreSame(expected[s], live[s],
                        "Blue→Realistic must restore the ORIGINAL shared materials, reference-exact.");
            }

            Assert.IsTrue(applier.Apply(), "Once back in Realistic the deferred tint must land.");
            Assert.IsTrue(applier.Applied, "Successful apply must mark the applier as applied.");

            int cloneSlots = 0;
            for (int i = 0; i < liveRenderers.Length; i++)
            {
                Material[] live = liveRenderers[i].sharedMaterials;
                Material[] expected = originals[i];
                for (int s = 0; s < live.Length && s < expected.Length; s++)
                {
                    if (!ReferenceEquals(live[s], expected[s]))
                        cloneSlots++;
                }
            }
            Assert.Greater(cloneSlots, 0, "Post-defer apply must still replace slots with tinted clones.");

            Object.Destroy(body);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ApplierOnDestroy_DestroysItsClones()
        {
            var binding = CreateWiredBinding();
            var applier = binding.BodyInstance.GetComponent<BodySkinTintApplier>();
            Assert.IsTrue(applier.Applied);

            HashSet<Material> assetMaterials = CollectAssetMaterials();
            Material clone = FindAnyClone(binding.BodyInstance, assetMaterials);
            Assert.IsNotNull(clone, "A tinted clone must exist before the destroy check.");

            Object.Destroy(applier);
            yield return null;
            yield return null;

            Assert.IsTrue(clone == null,
                "Applier OnDestroy must destroy the materials it produced (no leak).");
        }

        // ---------------------------------------------------------------- helpers

        static void ExpectWiringSequence()
        {
            LogAssert.Expect(LogType.Log, "[StreetCrossingFullBodyBinding] Elena wrist landmark alignment 2/2 hands (target <2 cm residual).");
        }

        GameObject _prefabCache;

        /// <summary>All distinct shared materials referenced by the body prefab ASSET.</summary>
        HashSet<Material> CollectAssetMaterials()
        {
            if (_prefabCache == null)
                _prefabCache = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);
            Assert.IsNotNull(_prefabCache, "Body prefab must exist at " + BodyPrefabPath);

            var materials = new HashSet<Material>();
            Renderer[] renderers = _prefabCache.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] slots = renderers[i].sharedMaterials;
                for (int s = 0; s < slots.Length; s++)
                {
                    if (slots[s] != null)
                        materials.Add(slots[s]);
                }
            }
            return materials;
        }

        static Material FindAnyClone(GameObject body, HashSet<Material> assetMaterials)
        {
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] slots = renderers[i].sharedMaterials;
                for (int s = 0; s < slots.Length; s++)
                {
                    if (slots[s] != null && !assetMaterials.Contains(slots[s]))
                        return slots[s]; // runtime clone, not a project asset
                }
            }
            return null;
        }

        static void AssertAreEqual(float expected, float actual, float delta, string message)
        {
            // Bundled NUnit (com.unity.ext.nunit 1.0.6) has no float overload of AreEqual.
            Assert.That(actual, Is.EqualTo(expected).Within(delta), message);
        }
    }
}
