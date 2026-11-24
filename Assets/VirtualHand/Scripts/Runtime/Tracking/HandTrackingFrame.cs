using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// One frame of per-hand tracking data, independent of <see cref="HandMotionFrame"/>.
    /// The fields describe what was actually measured by which sensor (wrist position,
    /// rotation, per-joint pose, and their validity) rather than the 20‑control
    /// visualisation values that HandMotionFrame carries.
    /// </summary>
    public struct HandTrackingFrame
    {
        /// <summary>Seconds since an arbitrary consistent epoch (e.g. Time.timeAsDouble).</summary>
        public double timestamp;

        /// <summary>Left or right hand.</summary>
        public SeatedHandSide side;

        /// <summary>Identifies the tracking origin (e.g. "PICO_TrackingSpace").</summary>
        public string trackingOriginId;

        // ---- wrist ----
        public Vector3 wristPosition;
        public Quaternion wristRotation;
        public bool wristPositionValid;
        public bool wristRotationValid;

        // ---- joints ----
        /// <summary>
        /// Joint poses in the same order as defined by the input source
        /// (e.g. XRHandJointID order).  Null when the source provides no
        /// per‑joint data.
        /// </summary>
        public Pose[] jointPoses;

        /// <summary>Per‑joint validity flags, same length as jointPoses.</summary>
        public bool[] jointValidity;

        // ---- meta ----
        /// <summary>"device", "replay", "manual", "mirror", "inferred".</summary>
        public string inputSource;

        /// <summary>True when this frame was derived from the opposite hand.</summary>
        public bool isMirrored;

        /// <summary>Seconds after timestamp during which the data is considered valid.</summary>
        public double validityWindow;

        /// <summary>Non‑empty when the source has changed since the previous frame.</summary>
        public string sourceSwitchReason;

        public bool AnyValid
        {
            get
            {
                if (wristPositionValid || wristRotationValid)
                    return true;
                if (jointValidity != null)
                    for (int i = 0; i < jointValidity.Length; i++)
                        if (jointValidity[i]) return true;
                return false;
            }
        }

        public bool AnyJointValid
        {
            get
            {
                if (jointValidity == null) return false;
                for (int i = 0; i < jointValidity.Length; i++)
                    if (jointValidity[i]) return true;
                return false;
            }
        }
    }
}