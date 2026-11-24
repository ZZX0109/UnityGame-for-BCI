using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Per-side calibration between a tracking source and the Elena hand model.
    /// All values are recorded from an actual measurement pass; nothing here is a
    /// guessed default, and the asset is expected to be re-sampled per user.
    ///
    /// What is calibrated and why:
    ///   wrist offsets      – the tracking origin reports the grip, not the
    ///                        anatomical wrist, so the model needs a rigid offset.
    ///   hand length/width  – scales the model so a long-handed user does not get a
    ///                        child-sized hand, which also keeps contact distances
    ///                        meaningful.
    ///   palm / finger axis – the source coordinate frame rarely matches the model's
    ///                        rest basis; these rotations align them.
    ///   thumb opposition   – thumb anatomy varies most; a separate bias keeps the
    ///                        pad facing the index pad rather than the palm.
    /// </summary>
    [CreateAssetMenu(menuName = "Neuralife/VirtualHand/Hand Tracking Calibration",
                     fileName = "HandTrackingCalibration")]
    public class HandTrackingCalibration : ScriptableObject
    {
        [Serializable]
        public sealed class SideCalibration
        {
            [Tooltip("Rigid offset from the tracked grip point to the anatomical wrist.")]
            public Vector3 wristPositionOffset = Vector3.zero;

            [Tooltip("Rotation applied on top of the tracked wrist rotation.")]
            public Vector3 wristRotationOffsetEuler = Vector3.zero;

            [Tooltip("Multiplier on the model's wrist-to-middle-tip length.")]
            [Range(0.7f, 1.4f)] public float handLengthScale = 1f;

            [Tooltip("Multiplier on the model's palm width.")]
            [Range(0.7f, 1.4f)] public float palmWidthScale = 1f;

            [Tooltip("Rotation aligning the source palm basis to the model's.")]
            public Vector3 palmAxisOffsetEuler = Vector3.zero;

            [Tooltip("Bias added to thumb opposition so the pad faces the index pad.")]
            [Range(-1f, 1f)] public float thumbOppositionBias;

            [Tooltip("True once a real measurement pass has filled these values.")]
            public bool measured;

            public bool IsPlausible =>
                Mathf.Abs(handLengthScale - 1f) < 0.6f &&
                Mathf.Abs(palmWidthScale - 1f) < 0.6f &&
                IsFinite(wristPositionOffset) &&
                IsFinite(wristRotationOffsetEuler) &&
                IsFinite(palmAxisOffsetEuler);

            static bool IsFinite(Vector3 v) =>
                !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        public SideCalibration left = new SideCalibration();
        public SideCalibration right = new SideCalibration();

        [Tooltip("Identifier of the tracking origin this calibration was measured against.")]
        public string trackingOriginId = "";

        [Tooltip("Free-form note: device, runtime, date and who performed the pass.")]
        public string measuredWith = "";

        public SideCalibration For(SeatedHandSide side) =>
            side == SeatedHandSide.Left ? left : right;

        /// <summary>
        /// Turns a tracked wrist pose into the model's wrist pose.
        /// Returns false when the calibration is not usable, so callers can fall
        /// back to a clearly-labelled uncalibrated display instead of silently
        /// applying a bad transform.
        /// </summary>
        public bool TryApplyWrist(SeatedHandSide side, Vector3 trackedPosition,
                                  Quaternion trackedRotation,
                                  out Vector3 modelPosition, out Quaternion modelRotation)
        {
            var c = For(side);
            if (c == null || !c.IsPlausible)
            {
                modelPosition = trackedPosition;
                modelRotation = trackedRotation;
                return false;
            }

            modelRotation = trackedRotation * Quaternion.Euler(c.wristRotationOffsetEuler);
            modelPosition = trackedPosition + modelRotation * c.wristPositionOffset;
            return true;
        }

        /// <summary>Applies the thumb opposition bias to a 20-control array in place.</summary>
        public void ApplyThumbBias(SeatedHandSide side, float[] controls)
        {
            var c = For(side);
            if (c == null || controls == null || controls.Length != 20)
                return;

            controls[3] = Mathf.Clamp01(controls[3] + c.thumbOppositionBias);
        }
    }
}