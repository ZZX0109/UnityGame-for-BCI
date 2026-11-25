using NUnit.Framework;
using UnityEngine;

namespace Neuralife.VirtualHand.Tests
{
    /// <summary>
    /// EditMode coverage for the positions-based optical retargeting. Flexion is
    /// the turn angle of successive tracked segment positions (metacarpal included),
    /// measured independently for each digit's three channels; it must be invariant
    /// under rigid transforms, keep per-channel fallbacks for missing or degenerate
    /// data, and opposition must come from real thumb/index proximity normalized by
    /// palm scale. No XR subsystem, scene or device is required.
    /// </summary>
    public class HandJointRetargeterPositionTests
    {
        const int JointCount = (int)TrackedJoint.JointCount;
        const float Segment = 0.04f;

        Pose[] _joints;
        bool[] _validity;
        float[] _destination;
        float[] _fallback;

        [SetUp]
        public void SetUp()
        {
            _joints = new Pose[JointCount];
            _validity = new bool[JointCount];
            _destination = new float[20];
            _fallback = new float[20];
            for (int i = 0; i < 20; i++)
                _fallback[i] = 0.7f;
        }

        void Set(TrackedJoint joint, Vector3 position)
        {
            _joints[(int)joint] = new Pose(position, Quaternion.identity);
            _validity[(int)joint] = true;
        }

        static Vector3 Along(float x, float z) => new Vector3(x, 0f, z);

        /// <summary>Index chain straight along +Z, wrist in line, measurable palm.</summary>
        void BuildStraightIndex()
        {
            Set(TrackedJoint.Wrist, Along(0f, -Segment));
            Set(TrackedJoint.IndexMetacarpal, Along(0f, 0f));
            Set(TrackedJoint.IndexProximal, Along(0f, Segment));
            Set(TrackedJoint.IndexIntermediate, Along(0f, 2f * Segment));
            Set(TrackedJoint.IndexDistal, Along(0f, 3f * Segment));
            Set(TrackedJoint.IndexTip, Along(0f, 4f * Segment));
            Set(TrackedJoint.MiddleMetacarpal, Along(0f, 0.09f));
        }

        /// <summary>Index chain with a 90° bend at the MCP only; PIP and DIP straight.</summary>
        void BuildIndexBentAtMetacarpophalangealJoint()
        {
            Set(TrackedJoint.Wrist, Along(0f, -Segment));
            Set(TrackedJoint.IndexMetacarpal, Along(0f, 0f));
            Set(TrackedJoint.IndexProximal, Along(0f, Segment));
            Set(TrackedJoint.IndexIntermediate, Along(Segment, Segment));
            Set(TrackedJoint.IndexDistal, Along(2f * Segment, Segment));
            Set(TrackedJoint.IndexTip, Along(3f * Segment, Segment));
            Set(TrackedJoint.MiddleMetacarpal, Along(0.02f, 0.09f));
        }

        // ------------------------------------------------------------------
        // Straight vs bent poses
        // ------------------------------------------------------------------

        [Test]
        public void StraightFinger_AllThreeChannelsNearZero()
        {
            BuildStraightIndex();

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[4], Is.EqualTo(0f).Within(0.01f), "MCP channel of a straight finger");
            Assert.That(_destination[5], Is.EqualTo(0f).Within(0.01f), "PIP channel of a straight finger");
            Assert.That(_destination[6], Is.EqualTo(0f).Within(0.01f), "DIP channel of a straight finger");
        }

        [Test]
        public void BendAtMetacarpophalangealJoint_FiresOnlyItsChannel()
        {
            BuildIndexBentAtMetacarpophalangealJoint();

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback, 90f);

            Assert.That(_destination[4], Is.EqualTo(1f).Within(0.01f), "the 90° MCP bend must saturate its channel");
            Assert.That(_destination[5], Is.EqualTo(0f).Within(0.01f), "PIP stays straight");
            Assert.That(_destination[6], Is.EqualTo(0f).Within(0.01f), "DIP stays straight");
        }

        [Test]
        public void BendAtInterphalangealJoint_DoesNotSearTheBaseChannel()
        {
            // Fold the distal chain straight back on itself: only the PIP turns.
            Set(TrackedJoint.Wrist, Along(0f, -Segment));
            Set(TrackedJoint.IndexMetacarpal, Along(0f, 0f));
            Set(TrackedJoint.IndexProximal, Along(0f, Segment));
            Set(TrackedJoint.IndexIntermediate, Along(0f, 2f * Segment));
            Set(TrackedJoint.IndexDistal, Along(0f, Segment));
            Set(TrackedJoint.IndexTip, Along(0f, 0f));
            Set(TrackedJoint.MiddleMetacarpal, Along(0f, 0.09f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback, 90f);

            Assert.That(_destination[4], Is.EqualTo(0f).Within(0.01f), "MCP is straight");
            Assert.That(_destination[5], Is.EqualTo(1f).Within(0.01f), "the 180° PIP fold must saturate its channel");
            Assert.That(_destination[6], Is.EqualTo(0f).Within(0.01f), "DIP is straight");
        }

        [Test]
        public void FingerCurl_AveragesTheMeasuredChannels()
        {
            BuildIndexBentAtMetacarpophalangealJoint();

            float curl = HandJointRetargeter.FingerCurl(_joints, _validity, 1, 90f, 0.42f);

            Assert.That(curl, Is.EqualTo(1f / 3f).Within(0.01f),
                "one bent joint of three measured channels");
        }

        // ------------------------------------------------------------------
        // Rigid-transform invariance
        // ------------------------------------------------------------------

        [Test]
        public void ControlsAreInvariantUnderRotationAndTranslation()
        {
            BuildIndexBentAtMetacarpophalangealJoint();
            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);
            var untransformed = new float[20];
            System.Array.Copy(_destination, untransformed, 20);

            Quaternion rotation = Quaternion.Euler(37f, 71f, 23f);
            for (int i = 0; i < JointCount; i++)
            {
                Vector3 position = rotation * _joints[i].position + new Vector3(1.2f, -0.7f, 2.5f);
                _joints[i] = new Pose(position, rotation * _joints[i].rotation);
            }

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            for (int i = 0; i < 20; i++)
                Assert.That(_destination[i], Is.EqualTo(untransformed[i]).Within(1e-3f),
                            "control " + i + " must not depend on hand orientation or placement");
        }

        // ------------------------------------------------------------------
        // Missing / degenerate data keeps per-channel fallbacks
        // ------------------------------------------------------------------

        [Test]
        public void MissingTip_MeasuresBaseChannelsAndKeepsDistalFallback()
        {
            BuildStraightIndex();
            _validity[(int)TrackedJoint.IndexTip] = false;

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[4], Is.EqualTo(0f).Within(0.01f), "MCP still measured");
            Assert.That(_destination[5], Is.EqualTo(0f).Within(0.01f), "PIP still measured");
            Assert.AreEqual(0.7f, _destination[6], "the unmeasurable DIP channel keeps its fallback");
        }

        [Test]
        public void MissingMetacarpal_KeepsBaseChannelFallback()
        {
            BuildStraightIndex();
            _validity[(int)TrackedJoint.IndexMetacarpal] = false;

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[4], "the MCP channel needs the metacarpal anchor");
            Assert.That(_destination[5], Is.EqualTo(0f).Within(0.01f), "PIP still measured");
            Assert.That(_destination[6], Is.EqualTo(0f).Within(0.01f), "DIP still measured");
        }

        [Test]
        public void DegenerateSegment_FallsBackInsteadOfReportingStraight()
        {
            BuildStraightIndex();
            _joints[(int)TrackedJoint.IndexDistal] = _joints[(int)TrackedJoint.IndexIntermediate];

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[4], Is.EqualTo(0f).Within(0.01f), "MCP unaffected");
            Assert.AreEqual(0.7f, _destination[5], "PIP loses its outgoing segment");
            Assert.AreEqual(0.7f, _destination[6], "DIP loses its incoming segment");
        }

        [Test]
        public void NonFinitePosition_FallsBackInsteadOfReportingStraight()
        {
            BuildStraightIndex();
            _joints[(int)TrackedJoint.IndexDistal] = new Pose(
                new Vector3(float.NaN, 0f, 3f * Segment), Quaternion.identity);

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[5], "NaN must not read as a straight finger");
            Assert.AreEqual(0.7f, _destination[6], "NaN must not read as a straight finger");
            Assert.That(_destination[4], Is.EqualTo(0f).Within(0.01f), "MCP unaffected");
        }

        [Test]
        public void AllChannelsUnmeasurable_FingerCurlKeepsFallback()
        {
            BuildStraightIndex();
            for (int i = 0; i < JointCount; i++)
                _validity[i] = false;

            float curl = HandJointRetargeter.FingerCurl(_joints, _validity, 1, 90f, 0.42f);

            Assert.AreEqual(0.42f, curl, 1e-4f);
        }

        // ------------------------------------------------------------------
        // Thumb opposition from real proximity
        // ------------------------------------------------------------------

        /// <summary>Palm wrist→middle-metacarpal length 0.09 m; tips at given positions.</summary>
        void BuildOppositionPalm(Vector3 thumbTip, Vector3 indexTip)
        {
            Set(TrackedJoint.Wrist, Vector3.zero);
            Set(TrackedJoint.MiddleMetacarpal, Along(0f, 0.09f));
            Set(TrackedJoint.ThumbTip, thumbTip);
            Set(TrackedJoint.IndexTip, indexTip);
        }

        [Test]
        public void Opposition_TouchingTipsReportOne()
        {
            BuildOppositionPalm(Along(0f, 0.2f), Along(0f, 0.2f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[3], Is.EqualTo(1f).Within(0.01f), "tips touching = full opposition");
        }

        [Test]
        public void Opposition_ScalesWithGapNormalizedByPalmLength()
        {
            BuildOppositionPalm(Vector3.zero, Along(0f, 0.045f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[3], Is.EqualTo(0.5f).Within(0.02f),
                "a gap of half a palm length is half opposition");
        }

        [Test]
        public void Opposition_BeyondOnePalmLengthReportsZero()
        {
            BuildOppositionPalm(Vector3.zero, Along(0f, 0.5f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[3], Is.EqualTo(0f).Within(0.01f), "a wide gap cannot oppose");
        }

        [Test]
        public void Opposition_WithoutPalmScaleKeepsFallback()
        {
            BuildOppositionPalm(Along(0f, 0.2f), Along(0f, 0.2f));
            _validity[(int)TrackedJoint.Wrist] = false;         // no metacarpal, no palm joint either

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[3], "an unmeasurable palm scale keeps the fallback");
        }

        [Test]
        public void Opposition_MissingThumbTipKeepsFallback()
        {
            BuildOppositionPalm(Along(0f, 0.2f), Along(0f, 0.2f));
            _validity[(int)TrackedJoint.ThumbTip] = false;

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[3]);
        }

        // ------------------------------------------------------------------
        // Signed palm-plane splay
        // ------------------------------------------------------------------

        /// <summary>Unit direction rotated <paramref name="degrees"/> from palm forward
        /// (+Z); positive degrees lean toward the index/thumb side (+X).</summary>
        static Vector3 SplayDir(float degrees)
            => new Vector3(Mathf.Sin(degrees * Mathf.Deg2Rad), 0f, Mathf.Cos(degrees * Mathf.Deg2Rad));

        /// <summary>
        /// Palm axes: forward = wrist → middle proximal = +Z, knuckle line = ±X with
        /// +X on the index side (index proximal at +0.03, little proximal at −0.03).
        /// </summary>
        void BuildSpreadPalm()
        {
            Set(TrackedJoint.Wrist, Vector3.zero);
            Set(TrackedJoint.MiddleProximal, Along(0f, 0.09f));
            Set(TrackedJoint.IndexProximal, Along(0.03f, 0.06f));
            Set(TrackedJoint.LittleProximal, Along(-0.03f, 0.06f));
        }

        void SetSegment(TrackedJoint proximal, Vector3 position, Vector3 direction)
        {
            Set(proximal, position);
            Set((TrackedJoint)((int)proximal + 1), position + direction.normalized * Segment);
        }

        [Test]
        public void Spread_NeutralSegmentsReportHalf()
        {
            BuildSpreadPalm();
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f), SplayDir(0f));
            SetSegment(TrackedJoint.RingProximal, Along(-0.01f, 0.07f), SplayDir(0f));
            SetSegment(TrackedJoint.LittleProximal, Along(-0.03f, 0.06f), SplayDir(0f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[7], Is.EqualTo(0.5f).Within(0.01f), "neutral index splay");
            Assert.That(_destination[15], Is.EqualTo(0.5f).Within(0.01f), "neutral ring splay");
            Assert.That(_destination[19], Is.EqualTo(0.5f).Within(0.01f), "neutral little splay");
        }

        [Test]
        public void Spread_OutwardAndInwardMoveOppositeAroundNeutral()
        {
            BuildSpreadPalm();
            // 12.5° is half of the ±25° splay range: half a control unit from neutral.
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f), SplayDir(12.5f));    // toward thumb
            SetSegment(TrackedJoint.RingProximal, Along(-0.01f, 0.07f), SplayDir(-12.5f));   // away from middle
            SetSegment(TrackedJoint.LittleProximal, Along(-0.03f, 0.06f), SplayDir(-12.5f)); // away from middle

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[7], Is.EqualTo(0.75f).Within(0.01f), "index outward is positive");
            Assert.That(_destination[15], Is.EqualTo(0.75f).Within(0.01f), "ring outward is positive");
            Assert.That(_destination[19], Is.EqualTo(0.75f).Within(0.01f), "little outward is positive");

            // Inward: toward the middle finger on every digit.
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f), SplayDir(-12.5f));
            SetSegment(TrackedJoint.RingProximal, Along(-0.01f, 0.07f), SplayDir(12.5f));
            SetSegment(TrackedJoint.LittleProximal, Along(-0.03f, 0.06f), SplayDir(12.5f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[7], Is.EqualTo(0.25f).Within(0.01f), "index inward is negative");
            Assert.That(_destination[15], Is.EqualTo(0.25f).Within(0.01f), "ring inward is negative");
            Assert.That(_destination[19], Is.EqualTo(0.25f).Within(0.01f), "little inward is negative");
        }

        [Test]
        public void Spread_MirroredHandProducesIdenticalControls()
        {
            BuildSpreadPalm();
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f), SplayDir(12.5f));
            SetSegment(TrackedJoint.RingProximal, Along(-0.01f, 0.07f), SplayDir(-6f));
            SetSegment(TrackedJoint.LittleProximal, Along(-0.03f, 0.06f), SplayDir(-18f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);
            var upright = new float[20];
            System.Array.Copy(_destination, upright, 20);

            for (int i = 0; i < JointCount; i++)          // mirror across the YZ plane
            {
                Vector3 p = _joints[i].position;
                _joints[i] = new Pose(new Vector3(-p.x, p.y, p.z), _joints[i].rotation);
            }

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            for (int i = 0; i < 20; i++)
                Assert.That(_destination[i], Is.EqualTo(upright[i]).Within(1e-3f),
                            "control " + i + " must be mirror-symmetric");
        }

        [Test]
        public void Spread_CurlDoesNotChangeSplay()
        {
            BuildSpreadPalm();
            // The index segment rotated 45° out of the palm plane: its in-plane
            // projection still points along the palm axis, so the splay must
            // remain neutral instead of reading the flexion as lateral splay.
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f),
                new Vector3(0f, Mathf.Sin(45f * Mathf.Deg2Rad), Mathf.Cos(45f * Mathf.Deg2Rad)));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.That(_destination[7], Is.EqualTo(0.5f).Within(0.01f),
                "flexion out of the palm plane must not read as splay");
        }

        [Test]
        public void Spread_NearVerticalCurlPreservesFallback()
        {
            BuildSpreadPalm();
            // 80° out of the palm plane: the in-plane projection (cos 80° ≈ 0.17)
            // is too short to measure a direction, so the fallback is kept.
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f),
                new Vector3(0f, Mathf.Sin(80f * Mathf.Deg2Rad), Mathf.Cos(80f * Mathf.Deg2Rad)));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[7], "a segment near the palm normal keeps its fallback");
        }

        [Test]
        public void Spread_MissingKnuckleLinePreservesFallback()
        {
            BuildSpreadPalm();
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f), SplayDir(0f));
            _validity[(int)TrackedJoint.LittleProximal] = false;

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[7], "no knuckle line, no splay");
            Assert.AreEqual(0.7f, _destination[15]);
            Assert.AreEqual(0.7f, _destination[19]);
        }

        [Test]
        public void Spread_DegenerateAxesAndSegmentsPreserveFallback()
        {
            BuildSpreadPalm();
            SetSegment(TrackedJoint.IndexProximal, Along(0.03f, 0.06f), SplayDir(0f));
            SetSegment(TrackedJoint.RingProximal, Along(-0.01f, 0.07f), SplayDir(0f));
            SetSegment(TrackedJoint.LittleProximal, Along(-0.03f, 0.06f), SplayDir(0f));

            // Index segment collapsed onto its proximal joint: unmeasurable.
            _joints[(int)TrackedJoint.IndexIntermediate] = _joints[(int)TrackedJoint.IndexProximal];

            // Knuckle line collapsed: index and little proximal at the same spot.
            Set(TrackedJoint.LittleProximal, Along(0.03f, 0.06f));

            HandJointRetargeter.FillControls(_destination, _joints, _validity, _fallback);

            Assert.AreEqual(0.7f, _destination[7], "collapsed segment keeps its fallback");
            Assert.AreEqual(0.7f, _destination[15], "collapsed knuckle line keeps every fallback");
            Assert.AreEqual(0.7f, _destination[19]);
        }

        // ------------------------------------------------------------------
        // Public API surface
        // ------------------------------------------------------------------

        [Test]
        public void FillControls_WithoutAnyDataKeepsTheWholeFallback()
        {
            HandJointRetargeter.FillControls(_destination, null, null, _fallback);

            for (int i = 0; i < 20; i++)
                Assert.AreEqual(0.7f, _destination[i], "control " + i);
        }

        [Test]
        public void ChainOf_StillReturnsTheDocumentedChains()
        {
            var thumb = HandJointRetargeter.ChainOf(0);
            Assert.AreEqual(TrackedJoint.ThumbProximal, thumb[0]);
            Assert.AreEqual(TrackedJoint.ThumbTip, thumb[2]);

            var little = HandJointRetargeter.ChainOf(4);
            Assert.AreEqual(TrackedJoint.LittleProximal, little[0]);
            Assert.AreEqual(TrackedJoint.LittleTip, little[3]);
        }
    }
}
