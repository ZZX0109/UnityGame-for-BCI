using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Solves the grip-local wrist offset consumed by FirstPersonWrist.
    /// Poses must share a tracking origin and timestamp, with positions in meters.
    /// </summary>
    public static class SeatedWristCalibration
    {
        /// <summary>
        /// Computes the offset that maps a tracked grip pose onto an explicit desired wrist pose.
        /// The caller supplies the selected hand's grip; this method never substitutes another pose.
        /// </summary>
        public static bool TryCalculateOffset(
            Pose measuredGripPose,
            bool measuredGripTracked,
            Pose desiredWristPose,
            out SeatedHandSettings.WristOffset offset)
        {
            offset = default(SeatedHandSettings.WristOffset);

            if (!measuredGripTracked ||
                !IsFinite(measuredGripPose.position) ||
                !IsFinite(desiredWristPose.position) ||
                !TryNormalizeQuaternion(measuredGripPose.rotation, out Quaternion gripRotation) ||
                !TryNormalizeQuaternion(desiredWristPose.rotation, out Quaternion desiredRotation))
            {
                return false;
            }

            Quaternion inverseGripRotation = Quaternion.Inverse(gripRotation);
            Vector3 translation = inverseGripRotation * (desiredWristPose.position - measuredGripPose.position);
            Quaternion relativeRotation = inverseGripRotation * desiredRotation;

            if (!IsFinite(translation) || !TryNormalizeQuaternion(relativeRotation, out relativeRotation))
                return false;

            Vector3 euler = relativeRotation.eulerAngles;
            if (!IsFinite(euler))
                return false;

            euler = new Vector3(WrapEuler(euler.x), WrapEuler(euler.y), WrapEuler(euler.z));
            if (!IsFinite(euler))
                return false;

            offset = new SeatedHandSettings.WristOffset(translation, euler);
            return true;
        }

        // Keep Euler components in the shortest signed range, including across the 0/360 boundary.
        private static float WrapEuler(float angle)
        {
            float wrapped = angle % 360f;
            if (wrapped >= 180f)
                wrapped -= 360f;
            else if (wrapped < -180f)
                wrapped += 360f;
            return wrapped;
        }

        private static bool TryNormalizeQuaternion(Quaternion value, out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z) || !IsFinite(value.w))
                return false;

            float maxComponent = Mathf.Max(
                Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y)),
                Mathf.Max(Mathf.Abs(value.z), Mathf.Abs(value.w)));
            if (maxComponent == 0f)
                return false;

            float x = value.x / maxComponent;
            float y = value.y / maxComponent;
            float z = value.z / maxComponent;
            float w = value.w / maxComponent;
            float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (!IsFinite(length) || length == 0f)
                return false;

            normalized = new Quaternion(x / length, y / length, z / length, w / length);
            return IsFinite(normalized.x) && IsFinite(normalized.y) &&
                   IsFinite(normalized.z) && IsFinite(normalized.w);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
