using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Pure geometry for deriving a contralateral wrist pose from an explicitly tracked
    /// operating wrist in a shared, metre-based tracking origin.
    /// </summary>
    public static class ContralateralWristPoseMath
    {
        /// <summary>
        /// Reflects a pose across a plane and derives the same grip value for the opposite hand.
        /// A successful result is labelled as mirror-derived, never as measured.
        /// </summary>
        public static bool TryDeriveFromTrackedOperatingWrist(
            Pose operatingWristPose,
            bool operatingWristIsTracked,
            float operatingGrip,
            SeatedHandSide operatingSide,
            SeatedHandSide targetSide,
            Vector3 sagittalPlanePointMeters,
            Vector3 sagittalPlaneNormal,
            out Pose targetWristPose,
            out float targetGrip,
            out string provenanceLabel)
        {
            targetWristPose = Pose.identity;
            targetGrip = 0f;
            provenanceLabel = string.Empty;

            if (!operatingWristIsTracked ||
                !IsHandSide(operatingSide) ||
                !IsHandSide(targetSide) ||
                operatingSide == targetSide ||
                !IsFinite(operatingGrip) ||
                operatingGrip < 0f || operatingGrip > 1f)
            {
                return false;
            }

            if (!TryReflectPoseAcrossPlane(
                    operatingWristPose,
                    sagittalPlanePointMeters,
                    sagittalPlaneNormal,
                    out Pose reflectedPose))
            {
                return false;
            }

            targetWristPose = reflectedPose;
            targetGrip = operatingGrip;
            provenanceLabel = operatingSide == SeatedHandSide.Left ? "Mirror-derived from tracked left wrist" : "Mirror-derived from tracked right wrist";
            return true;
        }

        /// <summary>
        /// Reflects pose geometry across a plane in the pose's coordinate frame.
        /// This low-level operation carries no tracking or measurement provenance.
        /// The plane point and pose position are in metres; the normal may have any
        /// finite, non-zero magnitude.
        /// </summary>
        public static bool TryReflectPoseAcrossPlane(
            Pose sourcePose,
            Vector3 planePointMeters,
            Vector3 planeNormal,
            out Pose reflectedPose)
        {
            reflectedPose = Pose.identity;

            if (!IsFinite(sourcePose.position) ||
                !IsFinite(planePointMeters) ||
                !TryNormalizeVector(planeNormal, out Vector3 normal) ||
                !TryNormalizeQuaternion(sourcePose.rotation, out Quaternion sourceRotation))
            {
                return false;
            }

            Vector3 positionDelta = sourcePose.position - planePointMeters;
            if (!IsFinite(positionDelta))
                return false;

            float signedDistance = Vector3.Dot(positionDelta, normal);
            if (!IsFinite(signedDistance))
                return false;

            Vector3 reflectedPosition = sourcePose.position - (2f * signedDistance) * normal;
            if (!IsFinite(reflectedPosition))
                return false;

            // A reflection reverses handedness. The hand rigs use the same wrist basis
            // on both sides, so reflect anatomical forward and up, then rebuild a proper
            // rotation. This necessarily negates the reflected local right axis.
            Vector3 reflectedForward = ReflectDirection(sourceRotation * Vector3.forward, normal);
            Vector3 reflectedUp = ReflectDirection(sourceRotation * Vector3.up, normal);
            if (!TryNormalizeVector(reflectedForward, out reflectedForward))
                return false;

            // Remove round-off error before LookRotation; the two reflected axes are
            // mathematically orthogonal, but may differ slightly in single precision.
            reflectedUp -= Vector3.Dot(reflectedUp, reflectedForward) * reflectedForward;
            if (!TryNormalizeVector(reflectedUp, out reflectedUp))
                return false;

            Quaternion rawReflectedRotation = Quaternion.LookRotation(reflectedForward, reflectedUp);
            if (!TryNormalizeQuaternion(rawReflectedRotation, out Quaternion reflectedRotation))
                return false;

            reflectedPose = new Pose(reflectedPosition, reflectedRotation);
            return true;
        }

        private static Vector3 ReflectDirection(Vector3 direction, Vector3 unitNormal)
        {
            return direction - (2f * Vector3.Dot(direction, unitNormal)) * unitNormal;
        }

        private static bool IsHandSide(SeatedHandSide side)
        {
            return side == SeatedHandSide.Left || side == SeatedHandSide.Right;
        }

        private static bool TryNormalizeVector(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value))
                return false;

            float maxComponent = Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
            if (maxComponent == 0f)
                return false;

            float x = value.x / maxComponent;
            float y = value.y / maxComponent;
            float z = value.z / maxComponent;
            float length = Mathf.Sqrt(x * x + y * y + z * z);
            if (!IsFinite(length) || length == 0f)
                return false;

            normalized = new Vector3(x / length, y / length, z / length);
            return IsFinite(normalized);
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
