using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.OpenXR;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using Neuralife.XR;

namespace StreetCrossing.Integration
{
    /// <summary>
    /// Drives Game03's Elena avatar from optical OpenXR hand joints. Each side is
    /// independent: optical tracking wins, a physically tracked controller is the
    /// second choice (with synthetic fingers from its grip), and loss of both returns
    /// only that side to its ordinary standing/seated wrist target and open hand.
    ///
    /// XR Hands joint poses are in tracking-space. This component always converts
    /// them through PicoRig.TrackingSpace before passing world positions to the body
    /// solver, so recentering and snap turns stay in the same coordinate contract.
    /// </summary>
    [AddComponentMenu("StreetCrossing/Tracked Hands")]
    [DisallowMultipleComponent]
    public sealed class StreetTrackedHands : MonoBehaviour
    {
        public enum InputSource
        {
            None,
            OpticalHandTracking,
            Controller,
            NaturalRest
        }

        [Header("Runtime binding")]
        public FullBodyPoseController poseController;
        [Tooltip("Tracking-space origin (normally PicoRig.TrackingSpace), not the player root.")]
        public Transform trackingSpace;

        [Header("Controller fallback")]
        public bool useTrackedControllerFallback = true;
        [Range(0.016f, 0.25f)]
        public float controlSmoothingSeconds = 0.06f;

        [Header("Hand orientation calibration")]
        [Tooltip("Optional per-user rotation calibration applied after the tracked wrist/palm orientation.")]
        public Vector3 leftRotationOffsetEuler;
        public Vector3 rightRotationOffsetEuler;

        public InputSource LeftSource => _left.Source;
        public InputSource RightSource => _right.Source;
        public bool RightGestureTracked => isActiveAndEnabled && _right.Source == InputSource.OpticalHandTracking && _right.GestureJointsValid;
        public bool OpticalSubsystemRunning => _subsystem != null && _subsystem.running;

        // XR Hands 1.4 uses Wrist=1, Palm=2. TrackedJoint intentionally preserves
        // the VirtualHand module's older Palm=1, Wrist=2 layout, so map every joint
        // explicitly instead of casting the enum values.
        static readonly XRHandJointID[] SourceJointIds =
        {
            XRHandJointID.Palm, XRHandJointID.Wrist,
            XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal,
            XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
            XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal,
            XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
            XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal,
            XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
            XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal,
            XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
            XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal,
            XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip
        };

        static readonly TrackedJoint[] TargetJointIds =
        {
            TrackedJoint.Palm, TrackedJoint.Wrist,
            TrackedJoint.ThumbMetacarpal, TrackedJoint.ThumbProximal,
            TrackedJoint.ThumbDistal, TrackedJoint.ThumbTip,
            TrackedJoint.IndexMetacarpal, TrackedJoint.IndexProximal,
            TrackedJoint.IndexIntermediate, TrackedJoint.IndexDistal, TrackedJoint.IndexTip,
            TrackedJoint.MiddleMetacarpal, TrackedJoint.MiddleProximal,
            TrackedJoint.MiddleIntermediate, TrackedJoint.MiddleDistal, TrackedJoint.MiddleTip,
            TrackedJoint.RingMetacarpal, TrackedJoint.RingProximal,
            TrackedJoint.RingIntermediate, TrackedJoint.RingDistal, TrackedJoint.RingTip,
            TrackedJoint.LittleMetacarpal, TrackedJoint.LittleProximal,
            TrackedJoint.LittleIntermediate, TrackedJoint.LittleDistal, TrackedJoint.LittleTip
        };

        sealed class SideState
        {
            public readonly SeatedHandSide Side;
            public readonly Pose[] JointPoses = new Pose[(int)TrackedJoint.JointCount];
            public readonly bool[] JointValidity = new bool[(int)TrackedJoint.JointCount];
            public readonly float[] Controls = new float[20];
            public HandRigDriver Driver;
            public Transform WristBone;
            public float[] OpenControls;
            public float[] FistControls;
            public Quaternion WristRestLocalRotation;
            public Quaternion PalmFrameInWrist;
            public bool HasPalmFrame;
            public bool GestureJointsValid;
            public float LastDynamicUpdate = float.NegativeInfinity;
            public InputSource Source = InputSource.None;
            public bool WarnedMissingDriver;

            public SideState(SeatedHandSide side)
            {
                Side = side;
            }
        }

        readonly SideState _left = new SideState(SeatedHandSide.Left);
        readonly SideState _right = new SideState(SeatedHandSide.Right);
        XRHandSubsystem _subsystem;
        HandGestureRecognizer _recognizer;
        float _nextReferenceRetry;

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            ConnectSubsystem();
        }

        void OnDisable()
        {
            DisconnectSubsystem();
            InvalidateGesture(_left);
            InvalidateGesture(_right);
        }

        /// <summary>
        /// Bind the body solver and tracking origin after the host creates the
        /// full-body instance. The tracking origin must be PicoRig.TrackingSpace.
        /// </summary>
        public void Bind(FullBodyPoseController controller, Transform origin)
        {
            poseController = controller;
            trackingSpace = origin;
            ResolveReferences();
            ConnectSubsystem();
        }

        void Update()
        {
            if ((!poseController || !trackingSpace || !_left.Driver || !_right.Driver) &&
                Time.unscaledTime >= _nextReferenceRetry)
            {
                ResolveReferences();
                _nextReferenceRetry = Time.unscaledTime + 0.5f;
            }
            ConnectSubsystem();

            if (_subsystem == null || !_subsystem.running)
            {
                ApplyFallback(_left);
                ApplyFallback(_right);
                return;
            }

            // A provider or loader can stop updating without destroying its subsystem.
            // Recover each stale side independently rather than freezing both hands.
            float now = Time.unscaledTime;
            if (now - _left.LastDynamicUpdate > 0.2f)
                ApplyFallback(_left);
            if (now - _right.LastDynamicUpdate > 0.2f)
                ApplyFallback(_right);
        }

        void ResolveReferences()
        {
            if (!_recognizer) _recognizer = GetComponentInChildren<HandGestureRecognizer>(true);
            if (!poseController)
                poseController = GetComponentInChildren<FullBodyPoseController>(true);

            if (!trackingSpace)
            {
                PicoRig rig = GetComponentInParent<PicoRig>();
                if (!rig)
                    rig = FindObjectOfType<PicoRig>();
                if (rig)
                    trackingSpace = rig.TrackingSpace;
            }

            // The controller, bodySpace, and Elena hand roots are not guaranteed to
            // share one branch. This component is bound to BodyInstance, so search that
            // avatar root first and then the explicitly wired rig subtrees.
            var drivers = GetComponentsInChildren<HandRigDriver>(true);
            if (drivers.Length == 0 && poseController && poseController.bodySpace)
                drivers = poseController.bodySpace.GetComponentsInChildren<HandRigDriver>(true);
            if (drivers.Length == 0 && poseController)
                drivers = poseController.GetComponentsInChildren<HandRigDriver>(true);

            HandRigDriver left = null, right = null;
            foreach (var driver in drivers)
            {
                if (!driver)
                    continue;
                if (driver.IsMirrored) left = driver;
                else right = driver;
            }

            BindDriver(_left, left, poseController ? poseController.leftWrist : null);
            BindDriver(_right, right, poseController ? poseController.rightWrist : null);
        }

        void BindDriver(SideState state, HandRigDriver driver, Transform wristBone)
        {
            bool driverChanged = state.Driver != driver;
            bool wristChanged = state.WristBone != wristBone;
            if (!driverChanged && !wristChanged && state.HasPalmFrame)
                return;

            state.Driver = driver;
            state.WristBone = wristBone;

            if (driverChanged)
            {
                state.OpenControls = PoseLibrary.OpenPose(driver);
                state.FistControls = PoseLibrary.FistPose(driver);
                Array.Copy(state.OpenControls, state.Controls, 20);
            }

            state.HasPalmFrame = false;
            if (wristChanged && wristBone)
                state.WristRestLocalRotation = wristBone.localRotation;
            if (driver && wristBone)
                state.HasPalmFrame = TryCapturePalmFrameInWrist(driver, wristBone, out state.PalmFrameInWrist);
            if (driver && wristBone && !state.HasPalmFrame)
                Debug.LogWarning($"[StreetTrackedHands] {state.Side} palm frame could not be calibrated from the Elena wrist/middle/index landmarks.");
        }

        void ConnectSubsystem()
        {
            XRHandSubsystem next = HandTracking.subsystem;
            if (_subsystem == next)
                return;

            DisconnectSubsystem();
            _subsystem = next;
            if (_subsystem != null)
                _subsystem.updatedHands += OnHandsUpdated;
        }

        void DisconnectSubsystem()
        {
            if (_subsystem != null)
                _subsystem.updatedHands -= OnHandsUpdated;
            _subsystem = null;
        }

        void OnHandsUpdated(XRHandSubsystem subsystem,
                            XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags,
                            XRHandSubsystem.UpdateType updateType)
        {
            if (subsystem != _subsystem || updateType != XRHandSubsystem.UpdateType.Dynamic)
                return;

            float now = Time.unscaledTime;
            _left.LastDynamicUpdate = now;
            _right.LastDynamicUpdate = now;
            const XRHandSubsystem.UpdateSuccessFlags leftRequired =
                XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose |
                XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints;
            const XRHandSubsystem.UpdateSuccessFlags rightRequired =
                XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose |
                XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;

            // XRHand objects hold mutable native buffers. Only consume a side when
            // this callback reports fresh root AND joint samples; otherwise an old
            // still-isTracked flag or buffer must not be mistaken for a new frame.
            if ((updateSuccessFlags & leftRequired) == leftRequired)
                ProcessHand(_left, subsystem.leftHand);
            else
                ApplyFallback(_left);

            if ((updateSuccessFlags & rightRequired) == rightRequired)
                ProcessHand(_right, subsystem.rightHand);
            else
                ApplyFallback(_right);
        }

        void ProcessHand(SideState state, XRHand hand)
        {
            if (TryApplyOpticalHand(state, hand))
            {
                SetSource(state, InputSource.OpticalHandTracking);
                return;
            }

            ApplyFallback(state);
        }

        bool TryApplyOpticalHand(SideState state, XRHand hand)
        {
            if (!hand.isTracked || !trackingSpace || !poseController)
                return false;

            Array.Clear(state.JointValidity, 0, state.JointValidity.Length);
            for (int i = 0; i < SourceJointIds.Length; i++)
            {
                XRHandJoint joint = hand.GetJoint(SourceJointIds[i]);
                if (!joint.TryGetPose(out Pose localPose))
                    continue;

                Pose worldPose = ToWorldPose(localPose);
                if (!IsFinite(worldPose.position) || !IsFinite(worldPose.rotation))
                    continue;

                int destination = (int)TargetJointIds[i];
                state.JointPoses[destination] = worldPose;
                state.JointValidity[destination] = true;
            }

            int wristIndex = (int)TrackedJoint.Wrist;
            Pose wristPose;
            if (state.JointValidity[wristIndex])
            {
                wristPose = state.JointPoses[wristIndex];
            }
            else
            {
                // XRHand.rootPose is defined at the wrist and points toward the fingers.
                wristPose = ToWorldPose(hand.rootPose);
                if (!IsFinite(wristPose.position) || !IsFinite(wristPose.rotation))
                    return false;
            }

            // OpenXR's Palm joint quaternion uses its own anatomical axes. Build the
            // target from the same three measured points as the Elena model frame so
            // finger direction and the mirrored left-hand normal stay consistent.
            int middleBaseIndex = (int)TrackedJoint.MiddleProximal;
            int indexBaseIndex = (int)TrackedJoint.IndexProximal;
            if (!state.JointValidity[middleBaseIndex] || !state.JointValidity[indexBaseIndex] ||
                !TryBuildPalmFrame(wristPose.position,
                    state.JointPoses[middleBaseIndex].position,
                    state.JointPoses[indexBaseIndex].position,
                    out Quaternion handRotation))
                return false;

            if (!poseController.ApplyTrackedWrist(state.Side, wristPose.position))
                return false;

            if (state.Driver)
            {
                ApplyTrackedPalmOrientation(state.Side, handRotation);
                float[] fallback = state.Driver.LastControls;
                HandJointRetargeter.FillControls(
                    state.Controls, state.JointPoses, state.JointValidity, fallback);
                UpdateControls(state, state.Controls);
                // Partial optical frames can still animate the wrist. Only classify
                // a hand shape when every finger has fresh joint positions.
                state.GestureJointsValid = true;
                for (int i = (int)TrackedJoint.ThumbMetacarpal; i <= (int)TrackedJoint.LittleTip; i++)
                    state.GestureJointsValid &= state.JointValidity[i];
                float palmLength = Vector3.Distance(wristPose.position, state.JointPoses[middleBaseIndex].position);
                float tipGap = Vector3.Distance(state.JointPoses[(int)TrackedJoint.ThumbTip].position,
                                                state.JointPoses[(int)TrackedJoint.IndexTip].position);
                if (_recognizer)
                    _recognizer.SetOpticalTipGap(state.Side, state.GestureJointsValid ? tipGap / palmLength : float.NaN);
            }
            else
            {
                WarnMissingDriver(state);
            }

            return true;
        }

        void InvalidateGesture(SideState state)
        {
            state.GestureJointsValid = false;
            if (_recognizer) _recognizer.SetOpticalTipGap(state.Side, float.NaN);
        }

        void ApplyFallback(SideState state)
        {
            InvalidateGesture(state);
            if (useTrackedControllerFallback && TryReadTrackedController(state.Side,
                    out Pose controllerPose, out float grip))
            {
                if (poseController && poseController.ApplyTrackedWrist(state.Side, controllerPose.position))
                {
                    if (state.Driver)
                    {
                        ApplyTrackedPalmOrientation(state.Side, controllerPose.rotation);
                        BlendGripControls(state, grip);
                        UpdateControls(state, state.Controls);
                    }
                    else
                    {
                        WarnMissingDriver(state);
                    }

                    SetSource(state, InputSource.Controller);
                    return;
                }
            }

            if (poseController)
                poseController.ApplyTrackedWrist(state.Side, NaturalWristWorldPosition(state.Side));

            if (state.WristBone)
                state.WristBone.localRotation = state.WristRestLocalRotation;

            if (state.Driver)
            {
                Array.Copy(state.OpenControls, state.Controls, 20);
                UpdateControls(state, state.Controls);
            }
            else
            {
                WarnMissingDriver(state);
            }

            SetSource(state, InputSource.NaturalRest);
        }

        bool TryReadTrackedController(SeatedHandSide side, out Pose worldPose, out float grip)
        {
            worldPose = Pose.identity;
            grip = 0f;
            if (!trackingSpace)
                return false;

            XRNode node = side == SeatedHandSide.Left ? XRNode.LeftHand : XRNode.RightHand;
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid ||
                (device.characteristics & InputDeviceCharacteristics.Controller) == 0 ||
                (device.characteristics & InputDeviceCharacteristics.HandTracking) != 0)
                return false;

            if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState tracking) ||
                (tracking & (InputTrackingState.Position | InputTrackingState.Rotation)) !=
                    (InputTrackingState.Position | InputTrackingState.Rotation) ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 localPosition) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion localRotation) ||
                !IsFinite(localPosition) || !IsFinite(localRotation))
                return false;

            worldPose = new Pose(
                trackingSpace.TransformPoint(localPosition),
                (trackingSpace.rotation * localRotation).normalized);

            if (!device.TryGetFeatureValue(CommonUsages.grip, out grip) &&
                !device.TryGetFeatureValue(CommonUsages.trigger, out grip))
                grip = 0f;

            grip = float.IsNaN(grip) || float.IsInfinity(grip) ? 0f : Mathf.Clamp01(grip);
            return IsFinite(worldPose.position) && IsFinite(worldPose.rotation);
        }

        Pose ToWorldPose(Pose trackingPose)
        {
            return new Pose(
                trackingSpace.TransformPoint(trackingPose.position),
                (trackingSpace.rotation * trackingPose.rotation).normalized);
        }

        /// <summary>
        /// Applies a world-space tracked palm frame by rotating the body's wrist bone
        /// around its own pivot. The Elena hand root stays at its calibrated wrist
        /// offset and keeps its baked local matrix.
        /// </summary>
        public bool ApplyTrackedPalmOrientation(SeatedHandSide side, Quaternion trackingPalmRotation)
        {
            SideState state = side == SeatedHandSide.Left ? _left : _right;
            if (!state.WristBone || !state.HasPalmFrame || !IsFinite(trackingPalmRotation))
                return false;

            Vector3 offset = side == SeatedHandSide.Left
                ? leftRotationOffsetEuler
                : rightRotationOffsetEuler;
            Quaternion desiredPalmFrame = trackingPalmRotation.normalized * Quaternion.Euler(offset);
            Quaternion desiredWristRotation = desiredPalmFrame * Quaternion.Inverse(state.PalmFrameInWrist);
            if (!IsFinite(desiredWristRotation))
                return false;

            state.WristBone.rotation = desiredWristRotation.normalized;
            return true;
        }

        static bool TryCapturePalmFrameInWrist(HandRigDriver driver, Transform wristBone,
                                               out Quaternion palmFrameInWrist)
        {
            palmFrameInWrist = Quaternion.identity;
            Transform modelWrist = FindLandmark(driver.transform, "hand.R_02");
            Transform middleBase = FindLandmark(driver.transform, "middle_01.R_025");
            Transform indexBase = FindLandmark(driver.transform, "index_01.R_017");
            if (!modelWrist || !middleBase || !indexBase)
                return false;

            if (!TryBuildPalmFrame(modelWrist.position, middleBase.position,
                                   indexBase.position, out Quaternion modelPalmFrame) ||
                !IsFinite(wristBone.rotation))
                return false;

            palmFrameInWrist =
                (Quaternion.Inverse(wristBone.rotation.normalized) * modelPalmFrame).normalized;
            return IsFinite(palmFrameInWrist);
        }

        static bool TryBuildPalmFrame(Vector3 wrist, Vector3 middleBase, Vector3 indexBase,
                                      out Quaternion palmFrame)
        {
            palmFrame = Quaternion.identity;
            Vector3 forward = middleBase - wrist;
            Vector3 indexAxis = indexBase - wrist;
            Vector3 normal = Vector3.Cross(forward, indexAxis);
            // Build in world space. InverseTransformPoint would also invert the
            // imported wrist scale (centimetre bones plus non-uniform normalization),
            // making this anatomical orientation frame ill-conditioned.
            if (!IsFinite(forward) || !IsFinite(indexAxis) || !IsFinite(normal) ||
                forward.sqrMagnitude < 1e-12f || normal.sqrMagnitude < 1e-12f)
                return false;

            palmFrame = Quaternion.LookRotation(forward, normal);
            return IsFinite(palmFrame);
        }

        static Transform FindLandmark(Transform root, string semanticName)
        {
            if (!root)
                return null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (ElenaNativeRigProfile.MatchesBone(candidate.name, semanticName))
                    return candidate;
            return null;
        }

        void BlendGripControls(SideState state, float grip)
        {
            for (int i = 0; i < state.Controls.Length; i++)
                state.Controls[i] = Mathf.Lerp(state.OpenControls[i], state.FistControls[i], grip);
        }

        void UpdateControls(SideState state, float[] target)
        {
            if (state.Driver)
                state.Driver.UpdateControls(target, controlSmoothingSeconds, Time.deltaTime);
        }

        Vector3 NaturalWristWorldPosition(SeatedHandSide side)
        {
            Vector3 target = FullBodyPoseController.GetBodySpaceWristTarget(
                poseController.CurrentPoseName, side);
            Vector3 pelvis = poseController.bodySpace
                ? poseController.bodySpace.position
                : poseController.transform.position;
            return pelvis + poseController.transform.rotation * target;
        }

        static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        static bool IsFinite(Quaternion value) =>
            IsFinite(value.x) && IsFinite(value.y) &&
            IsFinite(value.z) && IsFinite(value.w) &&
            value.x * value.x + value.y * value.y +
            value.z * value.z + value.w * value.w > 1e-10f;

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        void SetSource(SideState state, InputSource source)
        {
            if (state.Source == source)
                return;
            state.Source = source;
            Debug.Log($"[StreetTrackedHands] {state.Side}: {source}");
        }

        void WarnMissingDriver(SideState state)
        {
            if (state.WarnedMissingDriver)
                return;
            state.WarnedMissingDriver = true;
            Debug.LogWarning($"[StreetTrackedHands] {state.Side} HandRigDriver was not found; wrist tracking can run, but the Elena fingers cannot be driven.");
        }
    }
}

