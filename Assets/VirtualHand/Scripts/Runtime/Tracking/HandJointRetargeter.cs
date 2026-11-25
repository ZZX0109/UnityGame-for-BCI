using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// The joint identifiers this retargeter understands, in the same order as
    /// Unity's <c>XRHandJointID</c> so the two can be mapped 1:1 without a
    /// compile-time dependency on the XR Hands package.
    ///
    /// IMPORTANT: this mirror must stay in the same order as XRHandJointID. When
    /// the package becomes available, convert with
    /// <c>(TrackedJoint)(int)xrJointId</c> and verify the count matches.
    /// </summary>
    public enum TrackedJoint
    {
        Invalid = 0,
        BeginMarker = 1,

        Palm = 1,

        Wrist = 2,

        ThumbMetacarpal = 3,
        ThumbProximal = 4,
        ThumbDistal = 5,
        ThumbTip = 6,

        IndexMetacarpal = 7,
        IndexProximal = 8,
        IndexIntermediate = 9,
        IndexDistal = 10,
        IndexTip = 11,

        MiddleMetacarpal = 12,
        MiddleProximal = 13,
        MiddleIntermediate = 14,
        MiddleDistal = 15,
        MiddleTip = 16,

        RingMetacarpal = 17,
        RingProximal = 18,
        RingIntermediate = 19,
        RingDistal = 20,
        RingTip = 21,

        LittleMetacarpal = 22,
        LittleProximal = 23,
        LittleIntermediate = 24,
        LittleDistal = 25,
        LittleTip = 26,

        EndMarker = 26,
        JointCount = 27
    }

    /// <summary>
    /// Maps tracked hand joints onto the Elena-native rig and derives finger
    /// flexion from real joint angles.
    ///
    /// Design rules taken from the project spec:
    ///   • The tracked joint order is NOT assumed to equal the 20-control order.
    ///     The mapping is an explicit table.
    ///   • The full joint set is NOT collapsed into a single grip value; each
    ///     finger's flexion comes from its own joint chain.
    ///   • The 20-control values stay the interface for keyboard, glove and EEG
    ///     sources. Optical input produces the same 20 controls here, plus optional
    ///     finer bone rotations for callers that want them.
    ///
    /// Pure static math with no scene dependency, so it can be unit-tested without
    /// an XR subsystem.
    /// </summary>
    public static class HandJointRetargeter
    {
        /// <summary>Elena bone name for each tracked joint, or null when unmapped.</summary>
        static readonly string[] ElenaBoneForJoint = BuildBoneTable();

        static string[] BuildBoneTable()
        {
            var t = new string[(int)TrackedJoint.JointCount];
            t[(int)TrackedJoint.Wrist] = "hand.R_02";
            t[(int)TrackedJoint.Palm] = "hand.R.001_011";

            t[(int)TrackedJoint.ThumbMetacarpal] = "thumb_base.R_03";
            t[(int)TrackedJoint.ThumbProximal] = "thumb_01.R_08";
            t[(int)TrackedJoint.ThumbDistal] = "thumb_02.R_09";
            t[(int)TrackedJoint.ThumbTip] = "thumb_03.R_010";

            t[(int)TrackedJoint.IndexMetacarpal] = "index_base.R_012";
            t[(int)TrackedJoint.IndexProximal] = "index_01.R_017";
            t[(int)TrackedJoint.IndexIntermediate] = "index_02.R_018";
            t[(int)TrackedJoint.IndexDistal] = "index_03.R_019";
            t[(int)TrackedJoint.IndexTip] = "index_03.R_end_053";

            t[(int)TrackedJoint.MiddleMetacarpal] = "middle_base.R_020";
            t[(int)TrackedJoint.MiddleProximal] = "middle_01.R_025";
            t[(int)TrackedJoint.MiddleIntermediate] = "middle_02.R_026";
            t[(int)TrackedJoint.MiddleDistal] = "middle_03.R_027";
            t[(int)TrackedJoint.MiddleTip] = "middle_03.R_end_056";

            t[(int)TrackedJoint.RingMetacarpal] = "ring_base.R_028";
            t[(int)TrackedJoint.RingProximal] = "ring_01.R_033";
            t[(int)TrackedJoint.RingIntermediate] = "ring_02.R_034";
            t[(int)TrackedJoint.RingDistal] = "ring_03.R_035";
            t[(int)TrackedJoint.RingTip] = "ring_03.R_end_059";

            t[(int)TrackedJoint.LittleMetacarpal] = "pinky_base.R_036";
            t[(int)TrackedJoint.LittleProximal] = "pinky_01.R_041";
            t[(int)TrackedJoint.LittleIntermediate] = "pinky_02.R_042";
            t[(int)TrackedJoint.LittleDistal] = "pinky_03.R_043";
            t[(int)TrackedJoint.LittleTip] = "pinky_03.R_end_062";
            return t;
        }

        public static string ElenaBoneName(TrackedJoint joint)
        {
            int i = (int)joint;
            return i >= 0 && i < ElenaBoneForJoint.Length ? ElenaBoneForJoint[i] : null;
        }

        /// <summary>Joint chain of one finger, proximal first. Finger 0 is thumb.</summary>
        public static TrackedJoint[] ChainOf(int finger)
        {
            switch (finger)
            {
                case 0: return new[] { TrackedJoint.ThumbProximal, TrackedJoint.ThumbDistal, TrackedJoint.ThumbTip };
                case 1: return new[] { TrackedJoint.IndexProximal, TrackedJoint.IndexIntermediate, TrackedJoint.IndexDistal, TrackedJoint.IndexTip };
                case 2: return new[] { TrackedJoint.MiddleProximal, TrackedJoint.MiddleIntermediate, TrackedJoint.MiddleDistal, TrackedJoint.MiddleTip };
                case 3: return new[] { TrackedJoint.RingProximal, TrackedJoint.RingIntermediate, TrackedJoint.RingDistal, TrackedJoint.RingTip };
                case 4: return new[] { TrackedJoint.LittleProximal, TrackedJoint.LittleIntermediate, TrackedJoint.LittleDistal, TrackedJoint.LittleTip };
                default: return Array.Empty<TrackedJoint>();
            }
        }

        /// <summary>
        /// Flexion of one finger from its tracked joint POSITIONS. The bend is the
        /// interior angle of each successive segment pair of the measured polyline
        /// (wrist→metacarpal→proximal→intermediate→distal→tip), so a straight finger
        /// reports ~0 and a folded one ~1 regardless of how the whole hand is
        /// oriented or how the tracking provider rotates its joint frames.
        /// <paramref name="joints"/> is indexed by <see cref="TrackedJoint"/>;
        /// missing or degenerate measurements make the result fall back to
        /// <paramref name="fallback"/> rather than reporting a false straight finger.
        /// </summary>
        public static float FingerCurl(Pose[] joints, bool[] validity, int finger, float maxAngleDeg, float fallback)
        {
            if (joints == null || finger < 0 || finger > 4)
                return fallback;

            float sum = 0f;
            int used = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                float curl = ChannelCurl(joints, validity, finger, channel, maxAngleDeg);
                if (curl < 0f)
                    continue;                       // unmeasurable channel
                sum += curl;
                used++;
            }

            return used == 0 ? fallback : Mathf.Clamp01(sum / used);
        }

        /// <summary>
        /// Writes a full 20-control array from tracked joints. Each digit's three
        /// flexion channels are measured independently from its own joint chain
        /// (base/middle/distal joint bend), so a curl at one joint no longer
        /// smears across the whole finger. Unmeasurable channels keep
        /// <paramref name="fallbackPose"/>. Thumb opposition is estimated from the
        /// actual thumb-tip/index-tip proximity normalized by palm scale; when it
        /// cannot be measured the fallback value is kept. Spread comes from the
        /// lateral splay between adjacent proximal joints.
        /// </summary>
        public static void FillControls(float[] destination, Pose[] joints, bool[] validity,
                                        float[] fallbackPose, float maxAngleDeg = 90f)
        {
            if (destination == null || destination.Length != 20)
                return;

            bool hasFallback = fallbackPose != null && fallbackPose.Length == 20;
            if (hasFallback)
                Array.Copy(fallbackPose, destination, 20);
            else
                Array.Clear(destination, 0, 20);

            for (int finger = 0; finger < 5; finger++)
            {
                int o = finger * 4;
                for (int channel = 0; channel < 3; channel++)
                {
                    float curl = ChannelCurl(joints, validity, finger, channel, maxAngleDeg);
                    if (curl < 0f)
                        continue;                   // channel keeps its fallback value
                    destination[o + channel] = curl;
                }
            }

            if (joints != null)
                destination[3] = ThumbOpposition(joints, validity, destination[3]);

            ApplySpread(destination, joints, validity);
        }

        // -- flexion measurement -------------------------------------------------

        /// <summary>
        /// For each finger (0=thumb..4=little) and flexion channel (0..2), the joint
        /// triplet whose interior angle is that channel's flexion. The first channel
        /// anchors on the metacarpal (wrist for the thumb's CMC joint), so the base
        /// joint's bend is measured from bone geometry and not from the palm plane.
        /// Built once; the per-frame path reads it without allocating.
        /// </summary>
        static readonly int[] FlexionTriplets = BuildFlexionTriplets();

        static int[] BuildFlexionTriplets()
        {
            var t = new int[5 * 3 * 3];

            // Thumb: wrist → metacarpal → proximal → distal → tip (CMC, MCP, IP joints).
            SetTriplet(t, 0, 0, TrackedJoint.Wrist, TrackedJoint.ThumbMetacarpal, TrackedJoint.ThumbProximal);
            SetTriplet(t, 0, 1, TrackedJoint.ThumbMetacarpal, TrackedJoint.ThumbProximal, TrackedJoint.ThumbDistal);
            SetTriplet(t, 0, 2, TrackedJoint.ThumbProximal, TrackedJoint.ThumbDistal, TrackedJoint.ThumbTip);

            // Fingers: metacarpal → proximal → intermediate → distal → tip (MCP, PIP, DIP).
            for (int finger = 1; finger <= 4; finger++)
            {
                int mc = (int)TrackedJoint.IndexMetacarpal + (finger - 1) * 5;
                SetTriplet(t, finger, 0, (TrackedJoint)mc, (TrackedJoint)(mc + 1), (TrackedJoint)(mc + 2));
                SetTriplet(t, finger, 1, (TrackedJoint)(mc + 1), (TrackedJoint)(mc + 2), (TrackedJoint)(mc + 3));
                SetTriplet(t, finger, 2, (TrackedJoint)(mc + 2), (TrackedJoint)(mc + 3), (TrackedJoint)(mc + 4));
            }
            return t;
        }

        static void SetTriplet(int[] t, int finger, int channel, TrackedJoint a, TrackedJoint m, TrackedJoint b)
        {
            int i = (finger * 3 + channel) * 3;
            t[i] = (int)a;
            t[i + 1] = (int)m;
            t[i + 2] = (int)b;
        }

        /// <summary>Normalized 0..1 flexion of one channel, or -1 when unmeasurable.</summary>
        static float ChannelCurl(Pose[] joints, bool[] validity, int finger, int channel, float maxAngleDeg)
        {
            int t = (finger * 3 + channel) * 3;
            float bend = InteriorBendDeg(joints, validity,
                                         FlexionTriplets[t], FlexionTriplets[t + 1], FlexionTriplets[t + 2]);
            if (bend < 0f)
                return -1f;
            return Mathf.Clamp01(bend / Mathf.Max(1f, maxAngleDeg));
        }

        /// <summary>
        /// Flexion in degrees at the middle joint of a→m→b (0 = straight, 180 = fully
        /// folded), or -1 when a joint is missing, a segment degenerates or the data
        /// is not finite. Derived purely from positions, so it is invariant under
        /// any rigid transform of the whole hand.
        /// </summary>
        static float InteriorBendDeg(Pose[] joints, bool[] validity, int a, int m, int b)
        {
            if (joints == null || joints.Length <= b ||
                !IsValid(validity, a) || !IsValid(validity, m) || !IsValid(validity, b))
                return -1f;

            Vector3 incoming = joints[m].position - joints[a].position;
            Vector3 outgoing = joints[b].position - joints[m].position;
            if (incoming.sqrMagnitude < 1e-10f || outgoing.sqrMagnitude < 1e-10f)
                return -1f;

            // Deviation from straight travel: both segment vectors point the same way
            // when the chain is straight (0°) and opposite when fully folded (180°).
            float bend = Vector3.Angle(incoming, outgoing);
            return bend >= 0f && bend <= 180f ? bend : -1f;   // rejects NaN/∞
        }

        // -- thumb opposition ----------------------------------------------------

        /// <summary>
        /// Thumb opposition estimated from the real thumb-tip to index-tip distance,
        /// normalized by palm scale: touching tips report 1, a gap of one palm
        /// length (or more) reports 0. Falls back when the required joints are
        /// missing or the palm scale cannot be measured.
        /// </summary>
        static float ThumbOpposition(Pose[] joints, bool[] validity, float fallback)
        {
            float palmScale = PalmScale(joints, validity);
            if (palmScale <= 0f || joints.Length <= (int)TrackedJoint.IndexTip ||
                !IsValid(validity, (int)TrackedJoint.ThumbTip) ||
                !IsValid(validity, (int)TrackedJoint.IndexTip))
                return fallback;

            Vector3 gap = joints[(int)TrackedJoint.IndexTip].position
                        - joints[(int)TrackedJoint.ThumbTip].position;
            float normalized = gap.magnitude / palmScale;
            if (float.IsNaN(normalized) || float.IsInfinity(normalized))
                return fallback;                          // non-finite tracked data
            return Mathf.Clamp01(1f - normalized);
        }

        /// <summary>
        /// Characteristic palm length from the wrist to a metacarpal head, used to
        /// normalize proximity measures across hand sizes and tracking volumes.
        /// </summary>
        static float PalmScale(Pose[] joints, bool[] validity)
        {
            float scale = JointDistance(joints, validity, TrackedJoint.Wrist, TrackedJoint.MiddleProximal);
            if (scale > 0f) return scale;
            scale = JointDistance(joints, validity, TrackedJoint.Wrist, TrackedJoint.MiddleMetacarpal);
            if (scale > 0f) return scale;
            scale = JointDistance(joints, validity, TrackedJoint.Wrist, TrackedJoint.IndexMetacarpal);
            if (scale > 0f) return scale;
            return JointDistance(joints, validity, TrackedJoint.Wrist, TrackedJoint.Palm);
        }

        static float JointDistance(Pose[] joints, bool[] validity, TrackedJoint a, TrackedJoint b)
        {
            if (joints == null || joints.Length <= Mathf.Max((int)a, (int)b) ||
                !IsValid(validity, (int)a) || !IsValid(validity, (int)b))
                return 0f;

            Vector3 d = joints[(int)b].position - joints[(int)a].position;
            float length = d.magnitude;
            return length > 1e-6f && length < float.PositiveInfinity ? length : 0f;
        }

        /// <summary>
        /// Signed palm-plane splay of index/ring/little. The palm plane is spanned by
        /// the measured wrist→middle-proximal axis and the little↔index knuckle line,
        /// so the measure is anatomical and mirror-symmetric between hands: each
        /// finger's proximal segment is compared with the palm's forward axis within
        /// that plane, outward (away from the middle finger) positive, inward
        /// negative. Neutral splay writes 0.5, outward up to 1, inward down to 0.
        /// Flexion rotates a finger out of the palm plane and therefore cannot fake
        /// lateral splay; missing, degenerate or near-vertical (heavily curled)
        /// segments preserve the fallback control.
        /// </summary>
        static void ApplySpread(float[] c, Pose[] joints, bool[] validity)
        {
            Vector3 forward = SegmentDirection(joints, validity, TrackedJoint.Wrist, TrackedJoint.MiddleProximal);
            Vector3 knuckles = SegmentDirection(joints, validity, TrackedJoint.LittleProximal, TrackedJoint.IndexProximal);
            if (forward == Vector3.zero || knuckles == Vector3.zero)
                return;

            // Orthogonal in-plane axes. Both co-transform under left/right mirroring,
            // so signed splay values are identical for mirrored hands.
            Vector3 lateral = knuckles - forward * Vector3.Dot(knuckles, forward);
            if (lateral.sqrMagnitude < 1e-6f)            // knuckle line parallel to the palm axis
                return;
            lateral.Normalize();

            WriteSplay(c, 1, +1f, joints, validity, forward, lateral);   // index: outward = +lateral
            WriteSplay(c, 3, -1f, joints, validity, forward, lateral);   // ring/little: outward = -lateral
            WriteSplay(c, 4, -1f, joints, validity, forward, lateral);
        }

        /// <summary>
        /// Writes one finger's splay control from its proximal segment's in-plane
        /// direction, or nothing (keeping the fallback) when unmeasurable.
        /// </summary>
        static void WriteSplay(float[] c, int finger, float outwardSign, Pose[] joints, bool[] validity,
                               Vector3 forward, Vector3 lateral)
        {
            int proximal = (int)TrackedJoint.IndexProximal + (finger - 1) * 5;
            Vector3 dir = SegmentDirection(joints, validity, (TrackedJoint)proximal, (TrackedJoint)(proximal + 1));
            if (dir == Vector3.zero)
                return;

            float inPlaneForward = Vector3.Dot(dir, forward);
            float inPlaneLateral = Vector3.Dot(dir, lateral);
            float inPlaneSq = inPlaneForward * inPlaneForward + inPlaneLateral * inPlaneLateral;
            if (float.IsNaN(inPlaneSq) || inPlaneSq < 0.25f)
                return;                      // near-vertical curl: no reliable in-plane projection

            float angleDeg = Mathf.Atan2(inPlaneLateral, inPlaneForward) * Mathf.Rad2Deg;
            float signed = angleDeg * outwardSign / 25f;   // ±25° spans the control range
            c[finger * 4 + 3] = Mathf.Clamp01(0.5f + 0.5f * Mathf.Clamp(signed, -1f, 1f));
        }

        static Vector3 SegmentDirection(Pose[] joints, bool[] validity, TrackedJoint a, TrackedJoint b)
        {
            int ia = (int)a, ib = (int)b;
            if (joints == null || joints.Length <= Mathf.Max(ia, ib) || !IsValid(validity, ia) || !IsValid(validity, ib))
                return Vector3.zero;

            Vector3 d = joints[ib].position - joints[ia].position;
            return d.sqrMagnitude < 1e-10f ? Vector3.zero : d.normalized;
        }

        static bool IsValid(bool[] validity, int joint)
        {
            if (validity == null || joint < 0 || joint >= validity.Length)
                return false;
            return validity[joint];
        }
    }
}
