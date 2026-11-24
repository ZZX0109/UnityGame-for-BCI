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
        /// Flexion of one finger from the angle between consecutive joint segments.
        /// <paramref name="joints"/> is indexed by <see cref="TrackedJoint"/>;
        /// missing or invalid joints make the result fall back to
        /// <paramref name="fallback"/> rather than reporting a false straight finger.
        /// </summary>
        public static float FingerCurl(Pose[] joints, bool[] validity, int finger, float maxAngleDeg, float fallback)
        {
            if (joints == null || finger < 0 || finger > 4)
                return fallback;

            var chain = ChainOf(finger);
            float sum = 0f;
            int used = 0;

            for (int i = 0; i < chain.Length - 1; i++)
            {
                int a = (int)chain[i];
                int b = (int)chain[i + 1];
                if (!IsValid(validity, a) || !IsValid(validity, b))
                    continue;

                Vector3 dirA = joints[b].position - joints[a].position;
                if (dirA.sqrMagnitude < 1e-10f)
                    continue;

                // Reference direction: the palm's forward axis, so a straight finger
                // reports ~0 regardless of how the whole hand is oriented.
                float angle = Vector3.Angle(joints[a].rotation * Vector3.up, dirA.normalized);
                sum += Mathf.Clamp01(angle / Mathf.Max(1f, maxAngleDeg));
                used++;
            }

            if (used == 0)
                return fallback;

            return Mathf.Clamp01(sum / used);
        }

        /// <summary>
        /// Writes a full 20-control array from tracked joints. Each finger gets its
        /// own curl; spread comes from the lateral splay between adjacent proximal
        /// joints. Unmeasured fingers keep <paramref name="fallbackPose"/> values.
        /// </summary>
        public static void FillControls(float[] destination, Pose[] joints, bool[] validity,
                                        float[] fallbackPose, float maxAngleDeg = 90f)
        {
            if (destination == null || destination.Length != 20)
                return;

            if (fallbackPose != null && fallbackPose.Length == 20)
                Array.Copy(fallbackPose, destination, 20);
            else
                Array.Clear(destination, 0, 20);

            for (int finger = 0; finger < 5; finger++)
            {
                float curl = FingerCurl(joints, validity, finger, maxAngleDeg,
                                        fallbackPose != null && fallbackPose.Length == 20
                                            ? fallbackPose[Mathf.Min(19, finger * 4)]
                                            : 0f);
                if (finger == 0)
                {
                    destination[0] = curl;
                    destination[1] = curl;
                    destination[2] = curl;
                    // opposition is not recoverable from the chain alone
                    if (fallbackPose != null) destination[3] = fallbackPose[3];
                    continue;
                }

                int o = finger * 4;
                destination[o] = curl;
                destination[o + 1] = curl;
                destination[o + 2] = curl * 0.9f;
            }

            ApplySpread(destination, joints, validity);
        }

        /// <summary>Lateral splay of index/ring/little relative to middle.</summary>
        static void ApplySpread(float[] c, Pose[] joints, bool[] validity)
        {
            var reference = SegmentDirection(joints, validity, TrackedJoint.MiddleProximal, TrackedJoint.MiddleIntermediate);
            if (reference == Vector3.zero)
                return;

            SetSpread(c, 1, joints, validity, reference, TrackedJoint.IndexProximal, TrackedJoint.IndexIntermediate, +1f);
            SetSpread(c, 3, joints, validity, reference, TrackedJoint.RingProximal, TrackedJoint.RingIntermediate, -1f);
            SetSpread(c, 4, joints, validity, reference, TrackedJoint.LittleProximal, TrackedJoint.LittleIntermediate, -1f);
        }

        static void SetSpread(float[] c, int finger, Pose[] joints, bool[] validity,
                              Vector3 reference, TrackedJoint a, TrackedJoint b, float sign)
        {
            Vector3 dir = SegmentDirection(joints, validity, a, b);
            if (dir == Vector3.zero)
                return;

            float angle = Vector3.Angle(reference, dir) * sign;
            c[finger * 4 + 3] = Mathf.Clamp01(Mathf.Abs(angle) / 25f);
        }

        static Vector3 SegmentDirection(Pose[] joints, bool[] validity, TrackedJoint a, TrackedJoint b)
        {
            int ia = (int)a, ib = (int)b;
            if (!IsValid(validity, ia) || !IsValid(validity, ib))
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