#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Neuralife.VirtualHand.PlayTests
{
    /// <summary>
    /// Spec section 10.2: finger behaviour verified on the actual skeleton.
    ///
    /// The spec is explicit that checking the 20-value control array is not enough,
    /// so every assertion here measures real bone world positions. The primary
    /// measurement is the distance from each fingertip to the wrist: a curled finger
    /// pulls its tip toward the wrist, an extended one keeps it far. That is a
    /// functional property of the posed mesh, not a restatement of the input.
    /// </summary>
    public sealed class HandPoseBonePlayModeTests
    {
        const string PrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";

        GameObject _root;
        ControllerHandPair _pair;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "FirstPersonHands.prefab is missing");

            _root = new GameObject("Pose bone test root");
            var instance = Object.Instantiate(prefab, _root.transform);
            _pair = instance.GetComponent<ControllerHandPair>();
            Assert.IsNotNull(_pair, "FirstPersonHands.prefab has no ControllerHandPair");
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
        // helpers
        // ------------------------------------------------------------------

        static Transform Bone(Transform root, string landmark)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (ElenaNativeRigProfile.MatchesBone(t.name, landmark))
                    return t;
            return null;
        }

        /// <summary>Distance from a fingertip to the wrist, in metres.</summary>
        static float TipToWrist(Transform visualRoot, string tipLandmark)
        {
            var wrist = Bone(visualRoot, "hand.R_02");
            var tip = Bone(visualRoot, tipLandmark);
            Assert.IsNotNull(wrist, "missing wrist bone");
            Assert.IsNotNull(tip, "missing tip bone " + tipLandmark);
            return Vector3.Distance(wrist.position, tip.position);
        }

        static float[] TipDistances(Transform visualRoot)
        {
            return new[]
            {
                TipToWrist(visualRoot, "thumb_03.R_end"),
                TipToWrist(visualRoot, "index_03.R_end"),
                TipToWrist(visualRoot, "middle_03.R_end"),
                TipToWrist(visualRoot, "ring_03.R_end"),
                TipToWrist(visualRoot, "pinky_03.R_end")
            };
        }

        HandRigDriver Driver(bool left) => left ? _pair.leftHand : _pair.rightHand;
        Transform VisualRoot(bool left) =>
            (left ? _pair.leftWrist : _pair.rightWrist).visualRoot;

        // ------------------------------------------------------------------
        // 10.2 poses, on bones
        // ------------------------------------------------------------------

        [TestCase(true)]
        [TestCase(false)]
        public void OpenFistAndPinchProduceDistinctBoneConfigurations(bool left)
        {
            var driver = Driver(left);
            var visual = VisualRoot(left);
            Assert.IsNotNull(driver, "hand driver missing");
            Assert.IsNotNull(visual, "visual root missing");

            driver.CacheBones();

            driver.SetControls(PoseLibrary.OpenPose(driver));
            float[] open = TipDistances(visual);

            driver.SetControls(PoseLibrary.FistPose(driver));
            float[] fist = TipDistances(visual);

            driver.SetControls(PoseLibrary.PinchPose(driver));
            float[] pinch = TipDistances(visual);

            // Open vs fist: every finger's tip must come measurably closer to the wrist.
            for (int finger = 1; finger < 5; finger++)
                Assert.Less(fist[finger], open[finger] - 0.005f,
                    "finger " + finger + " did not curl between open and fist " +
                    "(open=" + open[finger].ToString("F4") + " fist=" + fist[finger].ToString("F4") + ")");

            // The three poses must not collapse into each other.
            Assert.Greater(Mathf.Abs(fist[0] - open[0]), 0.002f,
                "thumb tip did not move between open and fist");
            Assert.Greater(Mathf.Abs(pinch[0] - fist[0]) + Mathf.Abs(pinch[1] - fist[1]), 0.004f,
                "pinch is indistinguishable from fist at the thumb/index tips");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PointKeepsOnlyTheIndexFingerExtended(bool left)
        {
            // Spec 3.1: pointing must leave only the index straight; middle, ring and
            // little must curl, and the thumb must stay natural.
            var driver = Driver(left);
            var visual = VisualRoot(left);
            driver.CacheBones();

            driver.SetControls(PoseLibrary.OpenPose(driver));
            float[] open = TipDistances(visual);

            driver.SetControls(PoseLibrary.Point());
            float[] point = TipDistances(visual);

            // Index stays extended: its tip-to-wrist distance barely changes.
            Assert.That(point[1], Is.GreaterThan(open[1] - 0.01f),
                "the index finger curled while pointing (open=" + open[1].ToString("F4") +
                " point=" + point[1].ToString("F4") + ")");

            // The other three curl toward the wrist.
            for (int finger = 2; finger < 5; finger++)
                Assert.Less(point[finger], open[finger] - 0.004f,
                    "finger " + finger + " did not curl while pointing " +
                    "(open=" + open[finger].ToString("F4") + " point=" + point[finger].ToString("F4") + ")");

            // Thumb stays natural: it must not collapse like the curled fingers.
            Assert.Greater(point[0], point[3],
                "thumb collapsed further than the ring finger while pointing");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WristRotationDoesNotDisturbFingerLocalPose(bool left)
        {
            // Spec 1.1: moving or rotating the wrist must not break finger bone pose.
            var driver = Driver(left);
            var visual = VisualRoot(left);
            driver.CacheBones();
            driver.SetControls(PoseLibrary.FistPose(driver));

            var restLocalRotations = new List<Quaternion>();
            var bones = visual.GetComponentsInChildren<Transform>(true);
            foreach (var t in bones) restLocalRotations.Add(t.localRotation);
            float[] before = TipDistances(visual);

            var mount = (left ? _pair.leftWrist : _pair.rightWrist).transform;
            Quaternion original = mount.localRotation;

            foreach (float yaw in new[] { 90f, 180f })
            {
                mount.localRotation = original * Quaternion.Euler(0f, yaw, 0f);

                // Local rotations of the finger chain must be untouched by wrist motion.
                int i = 0;
                foreach (var t in bones)
                {
                    float delta = Quaternion.Angle(t.localRotation, restLocalRotations[i]);
                    Assert.Less(delta, 0.5f,
                        "wrist rotated by " + yaw + " degrees changed the local rotation of " + t.name +
                        " by " + delta.ToString("F2") + " degrees");
                    i++;
                }

                float[] after = TipDistances(visual);
                for (int f = 0; f < 5; f++)
                    Assert.That(after[f], Is.EqualTo(before[f]).Within(0.0005f),
                        "wrist rotated by " + yaw + " degrees changed finger " + f +
                        "'s tip-to-wrist distance");
            }

            mount.localRotation = original;
        }

        // ------------------------------------------------------------------
        // 10.2 numeric validation
        // ------------------------------------------------------------------

        [Test]
        public void NaNInputIsRejectedAndTheLastTrustedPoseSurvives()
        {
            var driver = _pair.rightHand;
            var visual = _pair.rightWrist.visualRoot;
            driver.CacheBones();
            driver.SetControls(PoseLibrary.FistPose(driver));

            float[] trusted = TipDistances(visual);

            var bad = new float[20];
            for (int i = 0; i < 20; i++) bad[i] = float.NaN;
            driver.SetControls(bad);

            float[] afterNaN = TipDistances(visual);
            for (int f = 0; f < 5; f++)
                Assert.That(afterNaN[f], Is.EqualTo(trusted[f]).Within(0.0005f),
                    "a NaN frame changed finger " + f + "'s real bone position");
        }

        [Test]
        public void InfinityInputIsRejectedAndTheLastTrustedPoseSurvives()
        {
            var driver = _pair.rightHand;
            var visual = _pair.rightWrist.visualRoot;
            driver.CacheBones();
            driver.SetControls(PoseLibrary.PinchPose(driver));

            float[] trusted = TipDistances(visual);

            var bad = new float[20];
            for (int i = 0; i < 20; i++) bad[i] = float.PositiveInfinity;
            driver.SetControls(bad);

            float[] afterInf = TipDistances(visual);
            for (int f = 0; f < 5; f++)
                Assert.That(afterInf[f], Is.EqualTo(trusted[f]).Within(0.0005f),
                    "an Infinity frame changed finger " + f + "'s real bone position");
        }

        [Test]
        public void WrongLengthInputIsIgnored()
        {
            var driver = _pair.rightHand;
            var visual = _pair.rightWrist.visualRoot;
            driver.CacheBones();
            driver.SetControls(PoseLibrary.OpenPose(driver));
            float[] trusted = TipDistances(visual);

            driver.SetControls(new float[7]);       // wrong length
            driver.SetControls(null);

            float[] after = TipDistances(visual);
            for (int f = 0; f < 5; f++)
                Assert.That(after[f], Is.EqualTo(trusted[f]).Within(0.0005f),
                    "a malformed frame changed finger " + f + "'s real bone position");
        }

        [Test]
        public void OutOfRangeInputIsRejectedAndTheLastTrustedPoseSurvives()
        {
            var driver = _pair.rightHand;
            var visual = _pair.rightWrist.visualRoot;
            driver.CacheBones();
            driver.SetControls(PoseLibrary.PinchPose(driver));
            float[] trusted = TipDistances(visual);

            var huge = new float[20];
            for (int i = 0; i < 20; i++) huge[i] = 50f;

            Assert.DoesNotThrow(() => driver.SetControls(huge));

            float[] after = TipDistances(visual);
            for (int f = 0; f < 5; f++)
                Assert.That(after[f], Is.EqualTo(trusted[f]).Within(0.0005f),
                    "an out-of-range frame changed finger " + f + "'s real bone position");

            // The caller's array must not be rewritten behind its back.
            Assert.AreEqual(50f, huge[0], "SetControls mutated the caller's control array");
        }

        [Test]
        public void IsValidControlArrayRejectsEveryMalformedShape()
        {
            Assert.IsTrue(HandRigDriver.IsValidControlArray(new float[20]));

            Assert.IsFalse(HandRigDriver.IsValidControlArray(null));
            Assert.IsFalse(HandRigDriver.IsValidControlArray(new float[19]));

            var nan = new float[20];
            nan[7] = float.NaN;
            Assert.IsFalse(HandRigDriver.IsValidControlArray(nan));

            var inf = new float[20];
            inf[13] = float.NegativeInfinity;
            Assert.IsFalse(HandRigDriver.IsValidControlArray(inf));

            var over = new float[20];
            over[3] = 1.5f;
            Assert.IsFalse(HandRigDriver.IsValidControlArray(over));

            var under = new float[20];
            under[9] = -0.2f;
            Assert.IsFalse(HandRigDriver.IsValidControlArray(under));
        }
    }
}
#endif
