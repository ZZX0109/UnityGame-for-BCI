#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
// EditMode wiring test for the Game01 side binding: SokobanFullBodyBinding
// (Assets/FirstPersonSokoban/Scripts/Integration/SokobanFullBodyBinding.cs)
// resolves the FirstPersonFullBody prefab and instantiates a "SokobanFullBody"
// instance driven by GameIntegrationAdapter with the standing, first-person,
// Realistic defaults. Uses the manual fullBodyPrefabOverride slot so the test
// exercises the same CreateBody() path the game uses at runtime, without any
// scene bootstrap.
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FirstPersonSokoban.Integration;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;

namespace Game01Integration.Tests
{
    public sealed class SokobanFullBodyBindingTests : FirstPersonFullBodyTestBase
    {
        [Test]
        public void CreateBody_WiresFullBodyInstance_FromOverridePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"Prefab missing at '{PrefabPath}'.");

            GameObject holder = new GameObject("SokobanFullBodyBinding_TestHolder");
            GameObject body = null;
            try
            {
                var binding = holder.AddComponent<SokobanFullBodyBinding>();
                binding.fullBodyPrefabOverride = prefab;

                MethodInfo createBody = typeof(SokobanFullBodyBinding).GetMethod(
                    "CreateBody", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(createBody, "CreateBody() not found via reflection.");
                createBody.Invoke(binding, null);

                body = GameObject.Find("SokobanFullBody");
                Assert.IsNotNull(body, "CreateBody() did not spawn a 'SokobanFullBody' instance.");

                var adapter = body.GetComponent<GameIntegrationAdapter>();
                Assert.IsNotNull(adapter, "Body instance lacks GameIntegrationAdapter.");
                Assert.IsNotNull(adapter.poseController, "poseController not wired on the body instance.");
                Assert.IsNotNull(adapter.locomotion, "locomotion not wired on the body instance.");
                Assert.IsNotNull(adapter.visibility, "visibility not wired on the body instance.");
                Assert.IsNotNull(adapter.appearance, "appearance not wired on the body instance.");

                Assert.AreEqual("Standing", adapter.poseController.CurrentPoseName,
                    "SetSeated(false) during CreateBody should apply the Standing idle pose.");
                Assert.IsTrue(adapter.visibility.FirstPersonActive,
                    "SetFirstPerson(true) during CreateBody should leave first-person visibility active.");
                Assert.AreEqual(FullBodyAppearanceMode.Realistic, adapter.appearance.Mode,
                    "CreateBody should default the appearance to Realistic.");
            }
            finally
            {
                // Awake() -> SetupGestureStack() also instantiated the gesture stack root.
                GameObject handsRoot = GameObject.Find("SokobanVirtualHandRoot");
                if (handsRoot != null) Object.DestroyImmediate(handsRoot);
                // Re-find the body even if CreateBody() threw midway, so a failure here
                // can never leak the instance into unrelated tests (e.g. render tests).
                if (body == null) body = GameObject.Find("SokobanFullBody");
                if (body != null) Object.DestroyImmediate(body);
                Object.DestroyImmediate(holder);
            }
        }
    }
}
#endif
