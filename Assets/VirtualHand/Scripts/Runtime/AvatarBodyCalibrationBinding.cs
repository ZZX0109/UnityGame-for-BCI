using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Applies validated body dimensions to a seated arm preview and maintains a yaw-only frame from a tracked head.
    /// The body origin is placed at the tracked head plus a configurable offset, then the shoulder line is positioned
    /// below that eye-midpoint frame using the calibrated torso and eye heights.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class AvatarBodyCalibrationBinding : MonoBehaviour
    {
        [Header("Binding")]
        public SeatedArmPreview preview;
        public Transform trackedHead;
        [Tooltip("The host must update this flag from actual head tracking validity; a Transform alone does not prove tracking.")]
        public bool headTrackingValid;
        [Tooltip("Dedicated transform controlled as the upright eye-midpoint body frame. Defaults to this component's transform.")]
        public Transform bodyOrigin;
        public AvatarBodyCalibration calibration;

        [Header("Head to body origin (metres)")]
        [Tooltip("Exact offset from the tracked head pose origin to the eye-midpoint body origin, expressed in upright yaw axes.")]
        public Vector3 bodyOriginOffsetFromTrackedHeadMeters;

        private SeatedArmPreview capturedPreview;
        private Transform originalPreviewBodyOrigin;
        private Transform lastAssignedPreviewBodyOrigin;
        private bool hasAssignedPreviewBodyOrigin;
        private bool calibrationApplied;
        private bool hasYaw;
        private Quaternion uprightYaw = Quaternion.identity;
        private AvatarBodyCalibration appliedCalibration;

        /// <summary>A normalized copy of the applied calibration, including each measurement's source metadata.</summary>
        public AvatarBodyCalibration AppliedCalibration => appliedCalibration;

        /// <summary>Most recent calibration or binding error. Null when the binding refreshed successfully.</summary>
        public string LastError { get; private set; }

        private Transform ControlledBodyOrigin => bodyOrigin ? bodyOrigin : transform;

        private void OnEnable()
        {
            CapturePreviewOrigin();
            ApplyCalibration();
        }

        private void LateUpdate()
        {
            RefreshBinding();
        }

        private void OnDisable()
        {
            RestorePreviewOrigin();
        }

        /// <summary>
        /// Validates and applies the current calibration using its existing model methods. Call again after replacing
        /// the calibration at runtime. Returns false without changing preview dimensions when validation fails.
        /// A valid calibration can apply while the head is absent; outputs remain hidden until a head is available.
        /// </summary>
        public bool ApplyCalibration()
        {
            CapturePreviewOrigin();
            if (!preview)
                return FailCalibration("A seated arm preview is required.");

            string error = null;
            AvatarBodyCalibration normalized = null;
            if (calibration == null || !calibration.TryNormalize(out normalized, out error))
                return FailCalibration(error ?? "A valid avatar body calibration is required.");

            if (!normalized.TryBuildSolverMeasurements(out AvatarBodySolverMeasurements measurements, out error))
                return FailCalibration(error);

            float shoulderHeightFromEyeMidpoint = normalized.torsoHeight.meters - normalized.eyeHeightFromPelvis.meters;

            // Apply only after all validation succeeds. Provenance remains on the normalized calibration copy.
            preview.upperArmLengthMeters = measurements.upperArmLengthMeters;
            preview.lowerArmLengthMeters = measurements.forearmLengthMeters;
            preview.leftShoulderBodyPositionMeters = new Vector3(
                -measurements.halfShoulderWidthMeters, shoulderHeightFromEyeMidpoint, 0f);
            preview.rightShoulderBodyPositionMeters = new Vector3(
                measurements.halfShoulderWidthMeters, shoulderHeightFromEyeMidpoint, 0f);

            appliedCalibration = normalized;
            calibrationApplied = true;
            LastError = null;
            RefreshBinding();
            return true;
        }

        /// <summary>Updates the upright body frame and refreshes preview outputs immediately.</summary>
        public bool RefreshBinding()
        {
            CapturePreviewOrigin();
            if (!preview)
            {
                LastError = "A seated arm preview is required.";
                return false;
            }

            if (!calibrationApplied)
                return DisableOutputs("A valid avatar body calibration must be applied first.");

            if (!trackedHead || !headTrackingValid || !IsFinite(trackedHead.position))
                return DisableOutputs("A tracked head transform is required.");

            if (!IsFinite(bodyOriginOffsetFromTrackedHeadMeters))
                return DisableOutputs("The head-to-body-origin offset must be finite.");

            Transform controlledOrigin = ControlledBodyOrigin;
            if (!IsSafeBodyOrigin(controlledOrigin, out string error))
                return DisableOutputs(error);

            UpdateUprightYaw(trackedHead, controlledOrigin);
            Vector3 worldOriginPosition = trackedHead.position + uprightYaw * bodyOriginOffsetFromTrackedHeadMeters;
            controlledOrigin.SetPositionAndRotation(worldOriginPosition, uprightYaw);

            SetPreviewBodyOrigin(controlledOrigin);
            preview.Refresh();
            LastError = null;
            return true;
        }

        private bool FailCalibration(string error)
        {
            calibrationApplied = false;
            appliedCalibration = null;
            LastError = error;
            if (preview)
            {
                SetPreviewBodyOrigin(null);
                preview.Refresh();
            }
            return false;
        }

        private bool DisableOutputs(string error)
        {
            LastError = error;
            SetPreviewBodyOrigin(null);
            preview.Refresh();
            return false;
        }

        private void UpdateUprightYaw(Transform head, Transform origin)
        {
            // Head-right remains horizontal during a pure nod, including when the head looks straight up or down.
            // Holding the last yaw when its projection degenerates prevents vertical gaze from rolling the shoulder plane.
            Vector3 horizontalRight = Vector3.ProjectOnPlane(head.right, Vector3.up);
            if (horizontalRight.sqrMagnitude > 0.000001f)
            {
                Vector3 horizontalForward = Vector3.Cross(horizontalRight.normalized, Vector3.up).normalized;
                if (horizontalForward.sqrMagnitude > 0.000001f)
                {
                    uprightYaw = Quaternion.LookRotation(horizontalForward, Vector3.up);
                    hasYaw = true;
                    return;
                }
            }

            if (hasYaw)
                return;

            Vector3 fallbackRight = Vector3.ProjectOnPlane(origin.right, Vector3.up);
            if (fallbackRight.sqrMagnitude > 0.000001f)
            {
                Vector3 fallbackForward = Vector3.Cross(fallbackRight.normalized, Vector3.up).normalized;
                uprightYaw = Quaternion.LookRotation(fallbackForward, Vector3.up);
            }
            else
            {
                uprightYaw = Quaternion.identity;
            }
            hasYaw = true;
        }

        private bool IsSafeBodyOrigin(Transform origin, out string error)
        {
            if (!origin)
            {
                error = "A body-origin transform is required.";
                return false;
            }

            if (origin == trackedHead || trackedHead.IsChildOf(origin))
            {
                error = "The body origin cannot be the tracked head or one of its ancestors.";
                return false;
            }

            if (preview.pair &&
                (IsRelatedToWrist(origin, preview.pair.leftWrist) || IsRelatedToWrist(origin, preview.pair.rightWrist)))
            {
                error = "The body origin must be separate from the tracked wrist hierarchies.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool IsRelatedToWrist(Transform candidate, FirstPersonWrist wrist)
        {
            if (!candidate || !wrist)
                return false;

            Transform wristTransform = wrist.transform;
            return candidate == wristTransform || candidate.IsChildOf(wristTransform) || wristTransform.IsChildOf(candidate);
        }

        private void CapturePreviewOrigin()
        {
            if (capturedPreview == preview)
                return;

            RestorePreviewOrigin();
            capturedPreview = preview;
            originalPreviewBodyOrigin = preview ? preview.bodyOrigin : null;
            hasAssignedPreviewBodyOrigin = false;
            lastAssignedPreviewBodyOrigin = null;
        }

        private void SetPreviewBodyOrigin(Transform value)
        {
            if (!preview)
                return;

            preview.bodyOrigin = value;
            lastAssignedPreviewBodyOrigin = value;
            hasAssignedPreviewBodyOrigin = true;
        }

        private void RestorePreviewOrigin()
        {
            if (hasAssignedPreviewBodyOrigin && capturedPreview &&
                capturedPreview.bodyOrigin == lastAssignedPreviewBodyOrigin)
            {
                capturedPreview.bodyOrigin = originalPreviewBodyOrigin;
                capturedPreview.Refresh();
            }

            hasAssignedPreviewBodyOrigin = false;
            lastAssignedPreviewBodyOrigin = null;
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
