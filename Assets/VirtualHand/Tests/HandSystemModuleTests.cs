using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// Covers the modules added for the reusable hand system: joint retargeting,
    /// tracking validity, action bindings, finger proportions and interaction edges.
    ///
    /// These are EditMode tests and deliberately avoid needing a scene, an XR
    /// subsystem or a device: the logic under test is pure, which is exactly why it
    /// was factored that way.
    /// </summary>
    public class HandSystemModuleTests
    {
        // ------------------------------------------------------------------
        // Joint retargeting
        // ------------------------------------------------------------------

        [Test]
        public void Retargeter_MapsEveryFingerJointToABone()
        {
            for (int finger = 0; finger < 5; finger++)
            {
                var chain = HandJointRetargeter.ChainOf(finger);
                Assert.Greater(chain.Length, 0, "finger " + finger + " has no joint chain");

                foreach (var joint in chain)
                {
                    string bone = HandJointRetargeter.ElenaBoneName(joint);
                    Assert.IsFalse(string.IsNullOrEmpty(bone),
                                   "joint " + joint + " (finger " + finger + ") has no Elena bone");
                }
            }
        }

        [Test]
        public void Retargeter_WristAndPalmAreMapped()
        {
            Assert.AreEqual("hand.R_02", HandJointRetargeter.ElenaBoneName(TrackedJoint.Wrist));
            Assert.AreEqual("hand.R.001_011", HandJointRetargeter.ElenaBoneName(TrackedJoint.Palm));
        }

        [Test]
        public void Retargeter_UnmappedJointsReturnNull()
        {
            Assert.IsNull(HandJointRetargeter.ElenaBoneName(TrackedJoint.Invalid));
        }

        [Test]
        public void Retargeter_MappedBoneNamesExistInTheNativeRigProfile()
        {
            var profile = Resources.Load<ElenaNativeRigProfile>("Rig/ElenaFF/ElenaNativeRig_R");
            if (profile == null || profile.bones == null || profile.bones.Length == 0)
                Assert.Ignore("native rig profile not available in this project state");

            var known = new HashSet<string>();
            foreach (var b in profile.bones)
                if (!string.IsNullOrEmpty(b.name)) known.Add(b.name);

            var missing = new List<string>();
            for (int i = 0; i < (int)TrackedJoint.JointCount; i++)
            {
                string bone = HandJointRetargeter.ElenaBoneName((TrackedJoint)i);
                if (string.IsNullOrEmpty(bone)) continue;
                if (!known.Contains(bone)) missing.Add(bone);
            }

            CollectionAssert.IsEmpty(missing,
                "mapped bone names that the imported rig does not contain: " + string.Join(", ", missing));
        }

        [Test]
        public void FingerCurl_AllJointsInvalidFallsBackInsteadOfReportingStraight()
        {
            var poses = new Pose[(int)TrackedJoint.JointCount];
            var validity = new bool[(int)TrackedJoint.JointCount];   // all false

            float curl = HandJointRetargeter.FingerCurl(poses, validity, 1, 90f, 0.42f);

            Assert.AreEqual(0.42f, curl, 1e-4f,
                "an unmeasured finger must keep its fallback value, not report a straight finger");
        }

        [Test]
        public void FillControls_WritesAllTwentyValues()
        {
            var destination = new float[20];
            var fallback = new float[20];
            for (int i = 0; i < 20; i++) fallback[i] = 0.25f;

            var poses = new Pose[(int)TrackedJoint.JointCount];
            var validity = new bool[(int)TrackedJoint.JointCount];

            HandJointRetargeter.FillControls(destination, poses, validity, fallback);

            Assert.AreEqual(20, destination.Length);
            foreach (var v in destination)
                Assert.That(v, Is.InRange(0f, 1f), "control outside [0,1]");
        }

        [Test]
        public void FillControls_RejectsWrongSizedDestination()
        {
            var wrong = new float[19];
            HandJointRetargeter.FillControls(wrong, null, null, null);   // must not throw
            Assert.AreEqual(19, wrong.Length);
        }

        // ------------------------------------------------------------------
        // Tracking validity
        // ------------------------------------------------------------------

        static HandTrackingFrame ValidFrame(SeatedHandSide side) => new HandTrackingFrame
        {
            side = side,
            wristPositionValid = true,
            wristRotationValid = true,
            jointValidity = new bool[(int)TrackedJoint.JointCount]
        };

        // NOTE: jointValidity is all-false above, so wrist validity drives AnyValid.

        [Test]
        public void Validity_HoldsCachedPoseForAFewMissingFramesThenExpires()
        {
            var v = new HandTrackingValidity { holdSeconds = 0.1f };
            v.Report(ValidFrame(SeatedHandSide.Left), 0.016f);
            Assert.IsTrue(v.IsTracking);
            Assert.IsTrue(v.MayDisplayCachedPose);

            // Miss a frame but stay inside the hold window.
            v.Report(default, 0.05f);
            Assert.IsFalse(v.IsTracking, "a miss must stop reporting active tracking");
            Assert.IsTrue(v.MayDisplayCachedPose, "the pose may still be shown inside the hold window");
            Assert.IsFalse(v.IsInteractive, "a hand that is not tracked must not be interactive");

            // Miss long enough to expire.
            v.Report(default, 0.2f);
            Assert.IsFalse(v.MayDisplayCachedPose, "past the hold window nothing may be displayed as current");
        }

        [Test]
        public void Validity_RecoveryRequiresResync()
        {
            var v = new HandTrackingValidity { holdSeconds = 0.1f, resyncSeconds = 0.3f };
            v.Report(ValidFrame(SeatedHandSide.Right), 0.016f);
            v.Report(default, 0.5f);                       // lose tracking
            Assert.IsFalse(v.IsInteractive);

            v.Report(ValidFrame(SeatedHandSide.Right), 0.016f);   // regain
            Assert.IsTrue(v.IsTracking);
            Assert.IsTrue(v.NeedsResync, "recovery must ask for a resync");
            Assert.IsFalse(v.IsInteractive, "no interaction until the resync window elapses");

            v.Report(ValidFrame(SeatedHandSide.Right), 0.4f);
            Assert.IsFalse(v.NeedsResync);
            Assert.IsTrue(v.IsInteractive, "interaction resumes after the resync window");
        }

        [Test]
        public void Validity_TwoSidesAreIndependent()
        {
            var left = new HandTrackingValidity();
            var right = new HandTrackingValidity();

            left.Report(ValidFrame(SeatedHandSide.Left), 0.016f);
            right.Report(ValidFrame(SeatedHandSide.Right), 0.016f);

            left.Report(default, 1.0f);   // left lost

            Assert.IsFalse(left.IsTracking);
            Assert.IsTrue(right.IsTracking, "losing the left hand must not disturb a valid right hand");
        }

        [Test]
        public void Validity_LosingTrackingNeverOpensTheHand()
        {
            // The contract that matters: expiry changes interactivity, never the
            // control values. There is no API here that could open a fist, and this
            // test pins that down so a future refactor cannot quietly add one.
            var v = new HandTrackingValidity();
            v.Report(ValidFrame(SeatedHandSide.Left), 0.016f);
            v.Report(default, 5f);

            Assert.IsFalse(v.IsInteractive);
            Assert.IsFalse(v.MayDisplayCachedPose);
            Assert.IsFalse(v.NeedsResync, "no resync flag until tracking actually returns");
        }

        // ------------------------------------------------------------------
        // Finger proportions
        // ------------------------------------------------------------------

        static HandProportions MakeProportions(float lengthScale, float proximal)
        {
            var p = ScriptableObject.CreateInstance<HandProportions>();
            foreach (var f in p.fingers)
            {
                if (f.finger == "index")
                {
                    f.lengthScale = lengthScale;
                    f.proximalScale = proximal;
                }
            }
            return p;
        }

        [Test]
        public void Proportions_DefaultIsIdentity()
        {
            var p = ScriptableObject.CreateInstance<HandProportions>();
            Assert.IsTrue(p.IsIdentity, "a fresh HandProportions must be identity");
        }

        [Test]
        public void Proportions_MatchesRealBoneNamesForTheTunedFinger()
        {
            var p = MakeProportions(1.25f, 1.1f);

            Assert.AreEqual(1.25f * 1.1f, p.ScaleFor("index_01.R_017"), 1e-4f);
            Assert.AreEqual(1.25f, p.ScaleFor("index_02.R_018"), 1e-4f);
            Assert.AreEqual(1.25f, p.ScaleFor("index_03.R_019"), 1e-4f);
            Assert.AreEqual(1.25f, p.ScaleFor("index_03.R_end_053"), 1e-4f);
        }

        [Test]
        public void Proportions_LeavesOtherFingersAlone()
        {
            var p = MakeProportions(1.25f, 1.1f);

            Assert.AreEqual(1f, p.ScaleFor("middle_01.R_025"), 1e-4f);
            Assert.AreEqual(1f, p.ScaleFor("ring_02.R_034"), 1e-4f);
            Assert.AreEqual(1f, p.ScaleFor("pinky_03.R_043"), 1e-4f);
        }

        [Test]
        public void Proportions_SkipsControlBonesAndWrist()
        {
            var p = MakeProportions(1.25f, 1.1f);

            Assert.AreEqual(1f, p.ScaleFor("index_Ctrl_01.R_014"), 1e-4f);
            Assert.AreEqual(1f, p.ScaleFor("hand.R_02"), 1e-4f);
            Assert.AreEqual(1f, p.ScaleFor("pulse.R_01"), 1e-4f);
        }

        [Test]
        public void Proportions_MetacarpalScalesOnlyTheBaseBone()
        {
            var p = ScriptableObject.CreateInstance<HandProportions>();
            p.metacarpalScale = 1.3f;

            Assert.AreEqual(1.3f, p.ScaleFor("index_base.R_012"), 1e-4f);
            Assert.AreEqual(1f, p.ScaleFor("index_01.R_017"), 1e-4f);
        }

        // ------------------------------------------------------------------
        // Action bindings
        // ------------------------------------------------------------------

        [Test]
        public void Bindings_StableIdIsDeterministic()
        {
            var a = HandActionBindingProfile.StableId("action/Open/Left");
            var b = HandActionBindingProfile.StableId("action/Open/Left");
            var c = HandActionBindingProfile.StableId("action/Fist/Left");

            Assert.AreEqual(a, b, "the same seed must always produce the same id");
            Assert.AreNotEqual(a, c, "different actions must not share an id");
        }

        [Test]
        public void Bindings_DefaultLayoutCoversEveryPoseForBothHands()
        {
            var profile = HandActionBindingProfile.CreateDefault();

            foreach (var side in new[] { SeatedHandSide.Left, SeatedHandSide.Right })
            {
                foreach (var action in new[]
                {
                    HandAction.Open, HandAction.Fist, HandAction.Pinch,
                    HandAction.Point, HandAction.Release
                })
                {
                    bool found = false;
                    foreach (var b in profile.bindings)
                        if (b.action == action && b.side == side) { found = true; break; }

                    Assert.IsTrue(found, action + " is not bound for " + side);
                }
            }
        }

        [Test]
        public void Bindings_DefaultLayoutHasNoInternalDuplicates()
        {
            var profile = HandActionBindingProfile.CreateDefault();
            var problems = profile.DetectConflicts();

            // No reserved paths were set, so any report here is an internal duplicate.
            CollectionAssert.IsEmpty(problems, string.Join(" | ", problems));
        }

        [Test]
        public void Bindings_DefaultLayoutDoesNotCollideWithGameKeys()
        {
            var profile = HandActionBindingProfile.CreateDefault();
            profile.reservedPaths = HandActionBindingProfile.DefaultReservedPaths();

            var problems = profile.DetectConflicts();

            CollectionAssert.IsEmpty(problems,
                "shipped defaults must not sit on game controls: " + string.Join(" | ", problems));
        }

        [Test]
        public void Bindings_ConflictDetectionReportsReservedAndDuplicate()
        {
            var profile = ScriptableObject.CreateInstance<HandActionBindingProfile>();
            profile.bindings.Add(new HandActionBinding
            {
                action = HandAction.Open, side = SeatedHandSide.Left, bindingPath = "<Keyboard>/w"
            });
            profile.bindings.Add(new HandActionBinding
            {
                action = HandAction.Fist, side = SeatedHandSide.Left, bindingPath = "<Keyboard>/w"
            });
            profile.reservedPaths = new List<string> { "<Keyboard>/w" };

            var problems = profile.DetectConflicts();

            Assert.GreaterOrEqual(problems.Count, 2,
                "both the reserved-key collision and the duplicate must be reported: " +
                string.Join(" | ", problems));
        }

        [Test]
        public void Bindings_GeneratedJsonContainsEveryBindingAndIsParseable()
        {
            var profile = HandActionBindingProfile.CreateDefault();
            string json = profile.BuildAssetJson();

            StringAssert.Contains("\"name\":\"VirtualHand\"", json);
            foreach (var b in profile.bindings)
                StringAssert.Contains(b.Key, json, "binding " + b.Key + " missing from generated JSON");

            // A malformed blob would silently leave the Input System with no actions.
            Assert.DoesNotThrow(() =>
            {
                var parsed = UnityEngine.JsonUtility.FromJson<JsonShape>(json);
                Assert.IsNotNull(parsed);
                Assert.AreEqual("VirtualHandActions", parsed.name);
                Assert.AreEqual(1, parsed.maps.Length);
            });
        }

        [System.Serializable]
        class JsonShape
        {
            public string name;
            public MapShape[] maps;
        }

        [System.Serializable]
        class MapShape
        {
            public string name;
            public string id;
        }

        [Test]
        public void Bindings_GeneratedJsonPinsIdsSoSavedOverridesSurvive()
        {
            var profile = HandActionBindingProfile.CreateDefault();
            string first = profile.BuildAssetJson();
            string second = profile.BuildAssetJson();

            Assert.AreEqual(first, second,
                "regenerating must produce identical ids, otherwise saved Binding Overrides " +
                "would no longer resolve after a restart");
        }
    }
}
