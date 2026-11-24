using UnityEngine;

namespace Neuralife.VirtualHand
{
    [DisallowMultipleComponent]
    public sealed class FirstPersonWrist : MonoBehaviour
    {
        public Transform trackingSpace;
        public Transform visualRoot;
        public Vector3 wristPositionOffset;
        public Vector3 wristRotationOffsetEuler;

        private Transform lastSafeVisualRoot;

        private void OnEnable()
        {
            HideVisuals();
        }

        private void OnDisable()
        {
            HideVisuals();
        }

        public bool ApplyTrackedPose(Pose localPose, bool tracked)
            => ApplyPose(localPose, tracked, true);

        // The operating wrist has already been calibrated before it is reflected.
        internal bool ApplyMirroredWristPose(Pose localPose)
            => ApplyPose(localPose, true, false);

        private bool ApplyPose(Pose localPose, bool tracked, bool applyGripOffset)
        {
            if (!tracked || !isActiveAndEnabled)
            {
                HideVisuals();
                return false;
            }

            if (!HasValidConfiguration() ||
                !IsFinite(localPose.position) ||
                !TryNormalizeQuaternion(localPose.rotation, out Quaternion localRotation))
            {
                HideVisuals();
                return false;
            }

            Vector3 gripPosition = trackingSpace.TransformPoint(localPose.position);
            Quaternion mappedRotation = trackingSpace.rotation * localRotation;
            if (!IsFinite(gripPosition) || !TryNormalizeQuaternion(mappedRotation, out Quaternion gripRotation))
            {
                HideVisuals();
                return false;
            }

            Quaternion rawOffsetRotation = applyGripOffset ? Quaternion.Euler(wristRotationOffsetEuler) : Quaternion.identity;
            if (!TryNormalizeQuaternion(rawOffsetRotation, out Quaternion offsetRotation))
            {
                HideVisuals();
                return false;
            }

            Vector3 targetPosition = applyGripOffset ? gripPosition + gripRotation * wristPositionOffset : gripPosition;
            Quaternion rawTargetRotation = gripRotation * offsetRotation;
            if (!IsFinite(targetPosition) || !TryNormalizeQuaternion(rawTargetRotation, out Quaternion targetRotation))
            {
                HideVisuals();
                return false;
            }

            transform.SetPositionAndRotation(targetPosition, targetRotation);

            if (lastSafeVisualRoot != null && lastSafeVisualRoot != visualRoot)
                SetInactiveIfSafe(lastSafeVisualRoot);

            lastSafeVisualRoot = visualRoot;
            SetActiveIfNeeded(visualRoot, true);
            return true;
        }

        private bool HasValidConfiguration()
        {
            if (trackingSpace == null || visualRoot == null)
                return false;

            if (visualRoot == transform || !visualRoot.IsChildOf(transform))
                return false;

            if (trackingSpace == transform || trackingSpace.IsChildOf(transform))
                return false;

            return IsFinite(wristPositionOffset) && IsFinite(wristRotationOffsetEuler);
        }

        private void HideVisuals()
        {
            SetInactiveIfSafe(visualRoot);

            if (lastSafeVisualRoot != visualRoot)
                SetInactiveIfSafe(lastSafeVisualRoot);
        }

        private void SetInactiveIfSafe(Transform candidate)
        {
            if (candidate == null || candidate == transform || !candidate.IsChildOf(transform))
                return;

            SetActiveIfNeeded(candidate, false);
        }

        private static void SetActiveIfNeeded(Transform target, bool active)
        {
            GameObject targetObject = target.gameObject;
            if (targetObject.activeSelf != active)
                targetObject.SetActive(active);
        }

        private static bool TryNormalizeQuaternion(Quaternion value, out Quaternion normalized)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z) || !IsFinite(value.w))
            {
                normalized = Quaternion.identity;
                return false;
            }

            float maxComponent = Mathf.Max(
                Mathf.Max(Mathf.Abs(value.x), Mathf.Abs(value.y)),
                Mathf.Max(Mathf.Abs(value.z), Mathf.Abs(value.w)));

            if (maxComponent == 0f)
            {
                normalized = Quaternion.identity;
                return false;
            }

            float x = value.x / maxComponent;
            float y = value.y / maxComponent;
            float z = value.z / maxComponent;
            float w = value.w / maxComponent;
            float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (!IsFinite(length) || length == 0f)
            {
                normalized = Quaternion.identity;
                return false;
            }

            normalized = new Quaternion(x / length, y / length, z / length, w / length);
            return IsFinite(normalized.x) && IsFinite(normalized.y) && IsFinite(normalized.z) && IsFinite(normalized.w);
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
