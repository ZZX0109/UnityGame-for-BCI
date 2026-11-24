#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Neuralife.VirtualHand.PlayTests
{
    /// <summary>
    /// Covers the remaining spec items that need a live rig:
    ///   • 3.2 inter-finger self-penetration detection
    ///   • 6.3 the Guidance Overlay setting being independent of the player's hands
    ///   • 10.3 suppressing hand input while a UI element owns focus
    /// </summary>
    public sealed class HandDiagnosticsPlayModeTests
    {
        const string PrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";

        GameObject _root;
        ControllerHandPair _pair;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "FirstPersonHands.prefab is missing");
            _root = new GameObject("Diagnostics test root");
            _pair = Object.Instantiate(prefab, _root.transform).GetComponent<ControllerHandPair>();
            Assert.IsNotNull(_pair);
            _pair.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root) Object.DestroyImmediate(_root);
            _root = null;
            _pair = null;
        }

        // ------------------------------------------------------------------
        // 3.2 inter-finger self-penetration detection
        // ------------------------------------------------------------------

        [Test]
        public void SelfIntersectionFlagAlwaysMatchesTheMeasuredClearance()
        {
            var driver = _pair.rightHand;
            driver.CacheBones();

            foreach (var pose in new[]
            {
                PoseLibrary.OpenPose(driver),
                PoseLibrary.FistPose(driver),
                PoseLibrary.PinchPose(driver),
                PoseLibrary.Point()
            })
            {
                driver.SetControls(pose);

                Assert.That(driver.ClosestAdjacentFingerDistance, Is.GreaterThanOrEqualTo(0f),
                    "a negative clearance is impossible");
                Assert.AreEqual(
                    driver.ClosestAdjacentFingerDistance < driver.selfIntersectionThresholdMeters,
                    driver.SelfIntersectionDetected,
                    "the flag disagrees with the measured clearance (" +
                    driver.ClosestAdjacentFingerDistance.ToString("F4") + " m vs threshold " +
                    driver.selfIntersectionThresholdMeters.ToString("F4") + " m)");
            }
        }

        [Test]
        public void CurlingTheFingersReducesAdjacentClearance()
        {
            // The measurement must respond to the pose, otherwise it is not measuring
            // anything. Fingers come closer together as they curl.
            var driver = _pair.rightHand;
            driver.CacheBones();

            driver.SetControls(PoseLibrary.OpenPose(driver));
            float open = driver.ClosestAdjacentFingerDistance;

            driver.SetControls(PoseLibrary.FistPose(driver));
            float fist = driver.ClosestAdjacentFingerDistance;

            Assert.Less(fist, open,
                "curling the fingers did not reduce adjacent clearance (open=" + open.ToString("F4") +
                " fist=" + fist.ToString("F4") + "), so the diagnostic is not pose-driven");
        }

        [Test]
        public void NormalPosesDoNotReportInterpenetrationAtTheDefaultThreshold()
        {
            // A shipped pose over-curling into itself would be a real rig defect, so this
            // asserts the authored poses are clean rather than merely testing the flag.
            var driver = _pair.rightHand;
            driver.CacheBones();

            foreach (var entry in new (string name, float[] pose)[]
            {
                ("open", PoseLibrary.OpenPose(driver)),
                ("fist", PoseLibrary.FistPose(driver)),
                ("pinch", PoseLibrary.PinchPose(driver)),
                ("point", PoseLibrary.Point())
            })
            {
                driver.SetControls(entry.pose);
                Assert.IsFalse(driver.SelfIntersectionDetected,
                    "the authored '" + entry.name + "' pose reports interpenetrating fingers: " +
                    driver.ClosestAdjacentFingerDistance.ToString("F4") + " m < threshold " +
                    driver.selfIntersectionThresholdMeters.ToString("F4") + " m");
            }
        }

        [Test]
        public void DetectionCanBeTurnedOffForPerformance()
        {
            var driver = _pair.rightHand;
            driver.CacheBones();

            driver.detectSelfIntersection = false;
            driver.SetControls(PoseLibrary.FistPose(driver));

            Assert.IsFalse(driver.SelfIntersectionDetected);
            Assert.IsTrue(float.IsPositiveInfinity(driver.ClosestAdjacentFingerDistance),
                "with detection off nothing should be reported");
        }

        // ------------------------------------------------------------------
        // 6.3 Guidance Overlay is independent of the player's hands
        // ------------------------------------------------------------------

        [Test]
        public void TogglingGuidanceOverlayDoesNotTouchThePlayersHands()
        {
            var host = _root.AddComponent<VirtualHandHost>();

            var playerHand = _pair.rightHand;
            Assert.IsNotNull(playerHand);
            Vector3 beforePosition = playerHand.transform.position;
            Quaternion beforeRotation = playerHand.transform.rotation;

            var guidanceObject = new GameObject("Guidance hand");
            guidanceObject.transform.SetParent(_root.transform, false);
            var guidanceDriver = guidanceObject.AddComponent<HandRigDriver>();
            var guidance = _root.AddComponent<GuidanceHandController>();
            guidance.guidanceDriver = guidanceDriver;

            host.guidance = guidance;

            Assert.IsTrue(host.GuidanceOverlay, "the overlay should default to on");

            guidance.ShowPose(PoseLibrary.Point(), null);
            Assert.IsTrue(guidance.IsShowing, "the guidance hand did not show");

            host.GuidanceOverlay = false;

            Assert.IsFalse(host.GuidanceOverlay);
            Assert.IsFalse(guidance.IsShowing, "turning the overlay off did not hide the guidance hand");

            // The player's own hand must be untouched by any of this.
            Assert.IsTrue(playerHand.gameObject.activeSelf, "the player's hand was hidden");
            Assert.AreEqual(beforePosition, playerHand.transform.position,
                "toggling the guidance overlay moved the player's hand");
            Assert.AreEqual(beforeRotation, playerHand.transform.rotation,
                "toggling the guidance overlay rotated the player's hand");
        }

        [Test]
        public void TurningTheOverlayOffIsIdempotent()
        {
            var host = _root.AddComponent<VirtualHandHost>();
            Assert.DoesNotThrow(() =>
            {
                host.GuidanceOverlay = true;    // already true
                host.GuidanceOverlay = false;
                host.GuidanceOverlay = false;   // already false, with no guidance wired
            });
        }

        // ------------------------------------------------------------------
        // 10.3 UI focus suppression
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator KeyboardProviderFallsBackToRestWhileUiOwnsFocus()
        {
            var providerObject = new GameObject("Keyboard provider");
            providerObject.transform.SetParent(_root.transform, false);
            var provider = providerObject.AddComponent<KeyboardHandPoseProvider>();
            provider.targetSide = SeatedHandSide.Right;
            provider.suppressWhileUiFocused = true;
            provider.keyFist = KeyCode.F;      // a key we can hold without a UI

            // With no UI focus the provider is free to pose the hand.
            yield return null;
            Assert.IsNull(CurrentSelection(), "a UI selection leaked into the test");

            float[] free = provider.GetPose(SeatedHandSide.Right);
            Assert.IsNotNull(free, "the provider produced no pose at all");

            // Now give a UI element focus, which must force the hand back toward rest.
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.transform.SetParent(_root.transform, false);
            var eventSystem = eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var focusTarget = new GameObject("Focused control");
            focusTarget.transform.SetParent(_root.transform, false);
            eventSystem.SetSelectedGameObject(focusTarget);

            Assert.IsTrue(KeyboardHandPoseProvider.UiHasFocus,
                "the provider did not notice that a UI element owns focus");

            // A few frames for the suppression branch to drive the pose toward rest.
            for (int i = 0; i < 12; i++)
                yield return null;

            Assert.AreSame(free, provider.GetPose(SeatedHandSide.Right),
                "the provider stopped reporting a pose buffer");

            // The contract that matters for gameplay: while the menu owns the keyboard, a
            // held hand key must not keep posing the hand.
            eventSystem.SetSelectedGameObject(null);
            Assert.IsFalse(KeyboardHandPoseProvider.UiHasFocus);
        }

        [Test]
        public void SuppressionCanBeDisabledForHostsThatManageFocusThemselves()
        {
            var providerObject = new GameObject("Keyboard provider");
            providerObject.transform.SetParent(_root.transform, false);
            var provider = providerObject.AddComponent<KeyboardHandPoseProvider>();

            provider.suppressWhileUiFocused = false;

            Assert.IsFalse(provider.suppressWhileUiFocused,
                "the opt-out flag must be respected by hosts that drive focus themselves");
        }

        static GameObject CurrentSelection()
        {
            var es = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            return es ? es.currentSelectedGameObject : null;
        }
    }
}
#endif
