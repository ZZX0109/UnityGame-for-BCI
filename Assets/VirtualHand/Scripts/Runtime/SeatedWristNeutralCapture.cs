using UnityEngine;
using UnityEngine.XR;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Captures one controller's seated neutral wrist offset into the presenter's open draft.
    /// The host must assign a tracking origin and an explicit desired wrist anchor for each
    /// side it wants to capture. XR device poses are read in tracking-local coordinates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatedWristNeutralCapture : MonoBehaviour
    {
        [Header("Required host references")]
        [Tooltip("The explicit XR tracking origin used to convert each desired wrist anchor into tracking-local coordinates.")]
        [SerializeField] private Transform trackingOrigin;
        [Tooltip("Explicit desired neutral wrist pose for the left side. It may be left unassigned when only capturing the right side.")]
        [SerializeField] private Transform leftDesiredWristAnchor;
        [Tooltip("Explicit desired neutral wrist pose for the right side. It may be left unassigned when only capturing the left side.")]
        [SerializeField] private Transform rightDesiredWristAnchor;
        [SerializeField] private SeatedHandSettingsPresenter presenter;

        public void BindTargets(Transform origin, Transform left, Transform right)
        {
            trackingOrigin = origin;
            leftDesiredWristAnchor = left;
            rightDesiredWristAnchor = right;
        }

        public void CaptureLeftButton() => CaptureLeft();
        public void CaptureRightButton() => CaptureRight();

        public bool CaptureLeft()
        {
            return Capture(SeatedHandSide.Left);
        }

        /// <summary>Captures from the right XR controller only.</summary>
        public bool CaptureRight()
        {
            return Capture(SeatedHandSide.Right);
        }

        /// <summary>Reads the selected controller pose using Unity XR CommonUsages.</summary>
        public bool Capture(SeatedHandSide side)
        {
            if (!IsSupportedSide(side))
                return Fail("采集失败：请选择左手或右手。");
            if (trackingOrigin == null)
                return Fail("采集失败：请在检查器中指定追踪原点。");
            if (GetDesiredWristAnchor(side) == null)
                return Fail(SideLabel(side) + "采集失败：请指定该侧的腕部中立位目标。");
            if (presenter == null)
                return Fail("采集失败：请将采集组件绑定到设置面板。");
            if (!TryReadGripPose(side, out Pose gripTrackingLocalPose))
                return Fail(SideLabel(side) + "采集失败：未检测到控制器姿态，请恢复该手的追踪后重试。");

            return CaptureFromTrackingLocalSample(side, gripTrackingLocalPose, true);
        }

        /// <summary>
        /// Deterministic sample entry point for tests and host integrations. The supplied grip
        /// pose must be in the same tracking-local space as the XR devicePosition/deviceRotation
        /// features. A false tracking flag is rejected without calculating or applying an offset.
        /// </summary>
        public bool CaptureFromTrackingLocalSample(
            SeatedHandSide side,
            Pose gripTrackingLocalPose,
            bool gripTracked)
        {
            if (!IsSupportedSide(side))
                return Fail("采集失败：请选择左手或右手。");
            if (!gripTracked)
                return Fail(SideLabel(side) + "采集失败：控制器已失去追踪，请恢复该手的追踪后重试。");
            if (trackingOrigin == null)
                return Fail("采集失败：请在检查器中指定追踪原点。");

            Transform desiredWristAnchor = GetDesiredWristAnchor(side);
            if (desiredWristAnchor == null)
                return Fail(SideLabel(side) + "采集失败：请指定该侧的腕部中立位目标。");
            if (presenter == null)
                return Fail("采集失败：请将采集组件绑定到设置面板。");

            // XR devicePosition/deviceRotation are already tracking-local. Convert the
            // host-authored world-space target into that same explicit origin's local space.
            Pose desiredWristTrackingLocalPose = new Pose(
                trackingOrigin.InverseTransformPoint(desiredWristAnchor.position),
                Quaternion.Inverse(trackingOrigin.rotation) * desiredWristAnchor.rotation);

            if (!SeatedWristCalibration.TryCalculateOffset(
                    gripTrackingLocalPose,
                    true,
                    desiredWristTrackingLocalPose,
                    out SeatedHandSettings.WristOffset offset))
            {
                return Fail("采集失败：控制器姿态或腕部中立位目标无效。");
            }

            if (!presenter.ApplyCapturedWristOffset(side, offset))
                return Fail("采集失败：请先打开设置面板，再重新采集。");

            return true;
        }

        private static bool TryReadGripPose(SeatedHandSide side, out Pose gripTrackingLocalPose)
        {
            gripTrackingLocalPose = default(Pose);
            XRNode node = side == SeatedHandSide.Left ? XRNode.LeftHand : XRNode.RightHand;
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            const InputTrackingState required = InputTrackingState.Position | InputTrackingState.Rotation;

            if (!device.isValid ||
                !device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) || !isTracked ||
                !device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState trackingState) ||
                (trackingState & required) != required ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                return false;
            }

            gripTrackingLocalPose = new Pose(position, rotation);
            return true;
        }

        private Transform GetDesiredWristAnchor(SeatedHandSide side)
        {
            if (side == SeatedHandSide.Left)
                return leftDesiredWristAnchor;
            if (side == SeatedHandSide.Right)
                return rightDesiredWristAnchor;
            return null;
        }

        private bool Fail(string message)
        {
            if (presenter != null)
                presenter.ReportWristCaptureError(message);
            else
                Debug.LogWarning(message, this);
            return false;
        }

        private static bool IsSupportedSide(SeatedHandSide side)
        {
            return side == SeatedHandSide.Left || side == SeatedHandSide.Right;
        }

        private static string SideLabel(SeatedHandSide side)
        {
            return side == SeatedHandSide.Left ? "左手" : "右手";
        }
    }
}
