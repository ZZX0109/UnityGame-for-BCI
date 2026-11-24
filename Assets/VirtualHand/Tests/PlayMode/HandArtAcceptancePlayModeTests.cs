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
    /// Automates the art-acceptance criteria listed in spec section 9, measured on the
    /// real skeleton rather than by eye:
    ///   • the thumb and index pads can approach each other naturally (pinch)
    ///   • a fist shows no obvious self-intersection
    ///   • pointing curls the other three fingers
    ///   • both hands are the same size and correctly sided
    ///   • the wrist cut stays out of the first-person view
    ///
    /// This does not replace an art review, but it turns four subjective sentences into
    /// numbers a regression can catch.
    /// </summary>
    public sealed class HandArtAcceptancePlayModeTests
    {
        const string PrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";

        GameObject _root;
        ControllerHandPair _pair;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "FirstPersonHands.prefab is missing");
            _root = new GameObject("Art acceptance test root");
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

        static Transform Bone(Transform root, string landmark)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (ElenaNativeRigProfile.MatchesBone(t.name, landmark))
                    return t;
            return null;
        }

        static float Distance(Transform root, string a, string b)
        {
            var ta = Bone(root, a);
            var tb = Bone(root, b);
            Assert.IsNotNull(ta, "missing bone " + a);
            Assert.IsNotNull(tb, "missing bone " + b);
            return Vector3.Distance(ta.position, tb.position);
        }

        Transform Visual(bool left) => (left ? _pair.leftWrist : _pair.rightWrist).visualRoot;
        HandRigDriver Driver(bool left) => left ? _pair.leftHand : _pair.rightHand;

        // ------------------------------------------------------------------
        // spec 9: thumb and index pads can approach naturally
        // ------------------------------------------------------------------

        [TestCase(true)]
        [TestCase(false)]
        public void PinchBringsTheThumbAndIndexTipsTogether(bool left)
        {
            var driver = Driver(left);
            var visual = Visual(left);
            driver.CacheBones();

            driver.SetControls(PoseLibrary.OpenPose(driver));
            float open = Distance(visual, "thumb_03.R_end", "index_03.R_end");

            driver.SetControls(PoseLibrary.PinchPose(driver));
            float pinched = Distance(visual, "thumb_03.R_end", "index_03.R_end");

            Assert.Less(pinched, open,
                "pinching did not bring the thumb and index tips closer (open=" +
                open.ToString("F4") + " pinch=" + pinched.ToString("F4") + ")");

            // "Naturally approach" means actually close, not merely closer: a pinch that
            // leaves a centimetre of air is not a pinch a user can aim with.
            Assert.Less(pinched, 0.035f,
                "the pinch pose leaves the thumb and index tips " + (pinched * 1000f).ToString("F1") +
                " mm apart, which is not a usable contact");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PinchDoesNotCollapseTheOtherFingersIntoThePalm(bool left)
        {
            var driver = Driver(left);
            var visual = Visual(left);
            driver.CacheBones();
            driver.SetControls(PoseLibrary.PinchPose(driver));

            // The middle/ring/little tips should stay clear of the wrist; a pinch that
            // curls everything reads as a fist with a gap.
            foreach (var tip in new[] { "middle_03.R_end", "ring_03.R_end", "pinky_03.R_end" })
            {
                float d = Distance(visual, "hand.R_02", tip);
                Assert.Greater(d, 0.02f,
                    tip + " collapsed to " + (d * 1000f).ToString("F1") + " mm from the wrist during a pinch");
            }
        }

        // ------------------------------------------------------------------
        // spec 9: no obvious self-intersection in a fist
        // ------------------------------------------------------------------

        [TestCase(true)]
        [TestCase(false)]
        public void FistShowsNoObviousSelfIntersection(bool left)
        {
            var driver = Driver(left);
            driver.CacheBones();
            driver.detectSelfIntersection = true;
            driver.SetControls(PoseLibrary.FistPose(driver));

            Assert.IsFalse(driver.SelfIntersectionDetected,
                "the fist pose reports interpenetrating fingers: closest adjacent clearance " +
                driver.ClosestAdjacentFingerDistance.ToString("F4") + " m vs threshold " +
                driver.selfIntersectionThresholdMeters.ToString("F4") + " m");
        }

        // ------------------------------------------------------------------
        // spec 9: shapes stay plausible
        // ------------------------------------------------------------------

        [TestCase(true)]
        [TestCase(false)]
        public void PointCurlsTheOtherThreeFingers(bool left)
        {
            var driver = Driver(left);
            var visual = Visual(left);
            driver.CacheBones();

            driver.SetControls(PoseLibrary.OpenPose(driver));
            float openMiddle = Distance(visual, "hand.R_02", "middle_03.R_end");

            driver.SetControls(PoseLibrary.Point());
            float pointMiddle = Distance(visual, "hand.R_02", "middle_03.R_end");

            Assert.Less(pointMiddle, openMiddle - 0.004f,
                "pointing did not curl the middle finger (open=" + openMiddle.ToString("F4") +
                " point=" + pointMiddle.ToString("F4") + ")");
        }

        [Test]
        public void BothHandsAreTheSameSize()
        {
            // Spec 9: left and right length and palm width must match.
            var left = Visual(true);
            var right = Visual(false);
            Assert.IsNotNull(left);
            Assert.IsNotNull(right);

            float leftSpan = Distance(left, "index_01", "pinky_01");
            float rightSpan = Distance(right, "index_01", "pinky_01");
            Assert.That(leftSpan, Is.EqualTo(rightSpan).Within(0.001f),
                "the two hands have different palm widths: " + leftSpan.ToString("F4") +
                " vs " + rightSpan.ToString("F4"));
        }

        [Test]
        public void HandsAreMirrorImagesOfEachOther()
        {
            // Spec 9: the side assignment must not be swapped.
            Assert.IsTrue(_pair.leftHand.IsMirrored, "the left hand driver is not marked as mirrored");
            Assert.IsFalse(_pair.rightHand.IsMirrored, "the right hand driver is marked as mirrored");

            var left = Visual(true);
            var right = Visual(false);
            Assert.IsFalse(ReferenceEquals(left, right),
                "both sides resolve to the same visual transform, so nothing is sided");

            // Siding lives entirely in the root rotation: both hands share one mesh with one
            // internal bone layout, so measuring anything in bone-local space (or projecting
            // onto the index-to-pinky axis, which is itself mirrored) is invariant and cannot
            // tell the sides apart. The root rotation is the only place the answer exists.
            Quaternion qr = right.rotation;
            var mirrored = new Quaternion(qr.x, -qr.y, -qr.z, qr.w);   // reflect across the YZ plane
            float error = Quaternion.Angle(mirrored, left.rotation);

            Assert.Less(error, 0.5f,
                "the left hand is not the mirror image of the right (measured " +
                error.ToString("F3") + " degrees apart). Identical rotations would mean one " +
                "hand is displaying the other side's mesh.");
        }

        [Test]
        public void WristCutSitsBelowTheFingerRoots()
        {
            // Spec 9: the wrist opening must not intrude into the palm area of the
            // first-person view. The cut plane is the mesh's lowest visible ring, so it
            // must sit clearly behind the knuckles along the hand's own forward axis.
            var right = Visual(false);
            var wrist = Bone(right, "hand.R_02");
            var middleRoot = Bone(right, "middle_01");
            var middleTip = Bone(right, "middle_03.R_end");
            Assert.IsNotNull(wrist);

            Vector3 forward = (middleTip.position - wrist.position).normalized;
            float knuckleDistance = Vector3.Dot(middleRoot.position - wrist.position, forward);

            Assert.Greater(knuckleDistance, 0.02f,
                "the knuckles sit only " + (knuckleDistance * 1000f).ToString("F1") +
                " mm in front of the wrist, so the cut would be inside the grip area");
        }
    }
}
#endif
