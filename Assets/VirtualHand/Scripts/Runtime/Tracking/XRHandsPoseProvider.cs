using UnityEngine;

// Unity XR Hands 1.4.2 was NOT found in the Unity China Package Manager registry
// (packages.unity.cn) as of 2026-11-24.  The XR Management (4.4.1) and OpenXR
// (1.11.0) packages resolved correctly, so OpenXR-dependent controller tracking
// is available via ControllerGripPoseProvider.  Optical hand tracking through
// XRHandSubsystem remains blocked until the XR Hands package is available from
// a compatible registry.
//
// When the package becomes available, uncomment the using and the #if block
// below, then populate HandJointRetargeter.JointToElenaName with the calibrated
// mapping from XRHandJointID to Elena-native bone names.

// #define HAS_XR_HANDS  // uncomment when com.unity.xr.hands is installed
// using UnityEngine.XR.Hands;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Optical hand-tracking provider that reads
    /// <c>XRHandSubsystem.updatedHands</c> and maps XRHandJoint poses
    /// to the Elena-native rig bones via a calibrated retargeter.
    /// 
    /// When the XR Hands package is not installed, IsActive always returns
    /// false and the provider is inert 鈥?the hand system falls back to
    /// the next lower-priority provider (controller or keyboard).
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/XR Hands Pose Provider")]
    [DisallowMultipleComponent]
    public sealed class XRHandsPoseProvider : MonoBehaviour, IHandPoseProvider
    {
        public string ProviderName => "XR Hands";
        public bool IsActive => isActiveAndEnabled && _subsystemRunning;
        public string SourceCategory => "device";

        [Header("Tracking")]
        [Tooltip("How many consecutive missing frames before the hand is marked lost.")]
        [Range(1, 30)]
        public int maxMissingFrames = 5;

        [Range(0.016f, 0.5f)]
        public float smoothing = 0.08f;

        [Header("Calibration")]
        [Tooltip("Offset added to the tracked wrist position (local to the wrist).")]
        public Vector3 wristPositionOffset = Vector3.zero;

        [Tooltip("Euler offset applied to the tracked wrist rotation.")]
        public Vector3 wristRotationOffsetEuler = Vector3.zero;

        bool _subsystemRunning;
        int _leftMissing, _rightMissing;

        Vector3 _lastLeftWrist, _lastRightWrist;
        Quaternion _lastLeftRot = Quaternion.identity, _lastRightRot = Quaternion.identity;
        float[] _leftPose = new float[20], _rightPose = new float[20];
        float[] _leftTarget = new float[20], _rightTarget = new float[20];

        public float[] GetPose(SeatedHandSide side)
            => side == SeatedHandSide.Left ? _leftPose : _rightPose;

        public bool TryGetWrist(SeatedHandSide side,
                                out Vector3 worldPosition,
                                out Quaternion worldRotation)
        {
            worldPosition = side == SeatedHandSide.Left ? _lastLeftWrist : _lastRightWrist;
            worldRotation = side == SeatedHandSide.Left ? _lastLeftRot : _lastRightRot;
            return _subsystemRunning;
        }

        void Start()
        {
#if HAS_XR_HANDS
            var subsystem = XRHandSubsystem.GetSubsystemsInManager();
            // On first valid subsystem we latch and poll.
            // (full implementation polls updatedHands)
#endif
            // When the package is absent we stay dormant.
        }

        void Update()
        {
#if HAS_XR_HANDS
            // Poll XRHandSubsystem.updatedHands, extract XRHandJoint poses,
            // retarget to Elena bones, and produce 20-control arrays.
            // 
            // Stub: the real implementation requires the XR Hands package
            // and a concrete device.  Until then this provider is inert.
#endif
            float dt = Time.deltaTime;
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / Mathf.Max(0.016f, smoothing));
            if (float.IsNaN(k) || float.IsInfinity(k)) k = 1f;

            for (int i = 0; i < 20; i++)
            {
                _leftPose[i]  = Mathf.Lerp(_leftPose[i],  _leftTarget[i],  k);
                _rightPose[i] = Mathf.Lerp(_rightPose[i], _rightTarget[i], k);
            }
        }

        /// <summary>
        /// Updates the internal target from a calibrated joint array.
        /// Called by the retargeter once per XR update.
        /// </summary>
        public void SetCalibratedPose(SeatedHandSide side, float[] jointValues20)
        {
            if (jointValues20 == null || jointValues20.Length != 20)
                return;
            float[] target = side == SeatedHandSide.Left ? _leftTarget : _rightTarget;
            System.Array.Copy(jointValues20, target, 20);
        }

        public void SetWrist(SeatedHandSide side, Vector3 worldPos, Quaternion worldRot)
        {
            if (side == SeatedHandSide.Left)
            {
                _lastLeftWrist = worldPos;
                _lastLeftRot = worldRot * Quaternion.Euler(wristRotationOffsetEuler);
            }
            else
            {
                _lastRightWrist = worldPos;
                _lastRightRot = worldRot * Quaternion.Euler(wristRotationOffsetEuler);
            }
        }
    }
}
