using UnityEngine;
using UnityEngine.XR;

namespace Neuralife.VirtualHand
{
    /// <summary>Displays grip poses from the host's active XR runtime. The prefab is a direct child of its tracking space.</summary>
    public sealed class ControllerHandPair : MonoBehaviour
    {
        public FirstPersonWrist leftWrist, rightWrist;
        public HandRigDriver leftHand, rightHand;
        [Tooltip("Optional. Defaults to the parent of this prefab, which must be the tracking origin.")]
        public Transform trackingOrigin;
        [Tooltip("Seated body midline: position is a point on the plane and right is its normal.")]
        public Transform sagittalPlane;
        public enum PoseSource { Hidden, Controller, Mirror }
        public PoseSource LeftSource { get; private set; }
        public PoseSource RightSource { get; private set; }

        public struct GripSample
        {
            public Pose pose;
            public bool tracked;
            public float grip;
        }

        [SerializeField]
        private string seatedHandSettingsPlayerPrefsKey = SeatedHandSettingsPresenter.DefaultPlayerPrefsKey;

        readonly float[] leftControls = new float[20];
        readonly float[] rightControls = new float[20];

        private SeatedHandSide operatingHand = SeatedHandSide.Unspecified;
        private SeatedHandDisplayMode displayMode = SeatedHandDisplayMode.MeasuredHands;
        private bool hasSavedSeatedSettings;
        private float leftPhysicalGrip, rightPhysicalGrip;
        private double samplesAt = double.NegativeInfinity;

        public bool TryGetPhysicalGrip(SeatedHandSide side, float maxAgeSeconds, out float grip)
        {
            grip = 0f;
            double age = Time.realtimeSinceStartupAsDouble - samplesAt;
            if (!isActiveAndEnabled || !(age >= 0 && age <= maxAgeSeconds)) return false;
            if (side == SeatedHandSide.Left && LeftSource == PoseSource.Controller) grip = leftPhysicalGrip;
            else if (side == SeatedHandSide.Right && RightSource == PoseSource.Controller) grip = rightPhysicalGrip;
            else return false;
            return !float.IsNaN(grip) && !float.IsInfinity(grip);
        }
        private SeatedHandSide pointingDisplayPresetSide = SeatedHandSide.Unspecified;

        void OnEnable()
        {
            SeatedHandSettings.PreferencesChanged += OnSeatedHandPreferencesChanged;
            ApplySavedSeatedSettings();
            Application.onBeforeRender += Refresh;
        }

        void OnDisable()
        {
            samplesAt = double.NegativeInfinity;
            LeftSource = RightSource = PoseSource.Hidden;
            Application.onBeforeRender -= Refresh;
            SeatedHandSettings.PreferencesChanged -= OnSeatedHandPreferencesChanged;
            if (leftWrist) leftWrist.ApplyTrackedPose(Pose.identity, false);
            if (rightWrist) rightWrist.ApplyTrackedPose(Pose.identity, false);
        }

        void Update() => Refresh();

        void Refresh()
        {
            ApplyControllerSamples(ReadGrip(XRNode.LeftHand), ReadGrip(XRNode.RightHand));
        }

        public void ApplyControllerSamples(GripSample left, GripSample right)
        {
            if (!isActiveAndEnabled) return;
            leftPhysicalGrip = left.grip;
            rightPhysicalGrip = right.grip;
            samplesAt = Time.realtimeSinceStartupAsDouble;
            var origin = trackingOrigin ? trackingOrigin : transform.parent;
            LeftSource = RenderMeasured(SeatedHandSide.Left, left, origin, leftWrist, leftHand, leftControls);
            RightSource = RenderMeasured(SeatedHandSide.Right, right, origin, rightWrist, rightHand, rightControls);
            if (displayMode != SeatedHandDisplayMode.Mirror || !sagittalPlane || !origin) return;
            if (LeftSource == PoseSource.Controller)
                RightSource = RenderMirror(SeatedHandSide.Right, leftWrist, left.grip, origin, rightWrist, rightHand, rightControls);
            else if (RightSource == PoseSource.Controller)
                LeftSource = RenderMirror(SeatedHandSide.Left, rightWrist, right.grip, origin, leftWrist, leftHand, leftControls);
            else
            {
                if (leftWrist) leftWrist.ApplyTrackedPose(Pose.identity, false);
                if (rightWrist) rightWrist.ApplyTrackedPose(Pose.identity, false);
            }
        }

        PoseSource RenderMeasured(SeatedHandSide side, GripSample sample, Transform origin, FirstPersonWrist wrist, HandRigDriver hand, float[] controls)
        {
            if (!wrist) return PoseSource.Hidden;
            wrist.trackingSpace = origin;
            // A pending mirror is updated directly below. Avoid deactivating then reactivating its mesh every frame.
            if (displayMode == SeatedHandDisplayMode.Mirror && sagittalPlane && origin &&
                operatingHand != SeatedHandSide.Unspecified && side != operatingHand) return PoseSource.Hidden;
            if (!wrist.ApplyTrackedPose(sample.pose, sample.tracked && ShouldDisplayHand(side))) return PoseSource.Hidden;
            ApplyGrip(side, hand, controls, sample.grip);
            return PoseSource.Controller;
        }

        PoseSource RenderMirror(SeatedHandSide side, FirstPersonWrist source, float grip, Transform origin, FirstPersonWrist target, HandRigDriver hand, float[] controls)
        {
            if (!target) return PoseSource.Hidden;
            var localWrist = new Pose(origin.InverseTransformPoint(source.transform.position),
                Quaternion.Inverse(origin.rotation) * source.transform.rotation);
            if (ContralateralWristPoseMath.TryDeriveFromTrackedOperatingWrist(localWrist, true, grip, operatingHand, side,
                origin.InverseTransformPoint(sagittalPlane.position), origin.InverseTransformDirection(sagittalPlane.right),
                out var pose, out var derivedGrip, out _) && target.ApplyMirroredWristPose(pose))
            {
                ApplyGrip(SeatedHandSide.Unspecified, hand, controls, derivedGrip);
                return PoseSource.Mirror;
            }
            target.ApplyTrackedPose(Pose.identity, false);
            return PoseSource.Hidden;
        }

        private void OnSeatedHandPreferencesChanged(string changedKey)
        {
            if (!string.Equals(changedKey, seatedHandSettingsPlayerPrefsKey, System.StringComparison.Ordinal))
                return;

            ApplySavedSeatedSettings();
        }

        private void ApplySavedSeatedSettings()
        {
            hasSavedSeatedSettings = false;
            operatingHand = SeatedHandSide.Unspecified;
            displayMode = SeatedHandDisplayMode.MeasuredHands;

            if (string.IsNullOrWhiteSpace(seatedHandSettingsPlayerPrefsKey) ||
                !PlayerPrefs.HasKey(seatedHandSettingsPlayerPrefsKey))
            {
                // With no saved profile, preserve the wrist offsets authored on the pair.
                return;
            }

            SeatedHandSettings settings = SeatedHandSettings.Load(seatedHandSettingsPlayerPrefsKey);
            if (settings.IsProtectedByNewerVersion)
            {
                // An unsupported future profile must not replace the current scene setup.
                return;
            }

            hasSavedSeatedSettings = true;
            operatingHand = settings.operatingHand;
            displayMode = settings.displayMode;

            // Keep the stored meter values and Euler degrees verbatim. FirstPersonWrist
            // retains its existing grip-relative axis convention and does not scale this offset.
            if (leftWrist)
            {
                leftWrist.wristPositionOffset = settings.leftWristOffset.translationMeters;
                leftWrist.wristRotationOffsetEuler = settings.leftWristOffset.rotationEulerDegrees;
            }

            if (rightWrist)
            {
                rightWrist.wristPositionOffset = settings.rightWristOffset.translationMeters;
                rightWrist.wristRotationOffsetEuler = settings.rightWristOffset.rotationEulerDegrees;
            }
        }

        private bool ShouldDisplayHand(SeatedHandSide side)
        {
            if (side != SeatedHandSide.Left && side != SeatedHandSide.Right)
                return false;

            if (!hasSavedSeatedSettings || displayMode == SeatedHandDisplayMode.MeasuredHands)
                return true;

            return operatingHand == side;
        }

        public SeatedHandSide OperatingHand => operatingHand;
        public bool RequiresOperatingHand => displayMode != SeatedHandDisplayMode.MeasuredHands;

        /// <summary>Sets a transient visual-only pointing pose for one measured controller hand.</summary>
        public bool SetPointingDisplayPreset(SeatedHandSide side, bool enabled)
        {
            if (side != SeatedHandSide.Left && side != SeatedHandSide.Right) return false;
            if (enabled) pointingDisplayPresetSide = side;
            else if (pointingDisplayPresetSide == side) pointingDisplayPresetSide = SeatedHandSide.Unspecified;
            return true;
        }

        static GripSample ReadGrip(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            const InputTrackingState required = InputTrackingState.Position | InputTrackingState.Rotation;
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state) || (state & required) != required ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
                !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                return default;
            if (!device.TryGetFeatureValue(CommonUsages.grip, out float grip) || float.IsNaN(grip) || float.IsInfinity(grip)) grip = 0f;
            return new GripSample { pose = new Pose(position, rotation), tracked = true, grip = Mathf.Clamp01(grip) };
        }

        void ApplyGrip(SeatedHandSide side, HandRigDriver hand, float[] controls, float grip)
        {
            if (!hand) return;
            var open = hand.ProfileOpen;
            var closed = hand.ProfileFist;
            if (open == null || closed == null) return;
            for (int i = 0; i < controls.Length; i++)
            {
                float blend = grip;
                if (side != SeatedHandSide.Unspecified && side == pointingDisplayPresetSide)
                {
                    int finger = i / 4;
                    int channel = i % 4;
                    blend = finger == 1 || channel == 3 ? 0f : (finger == 0 ? .30f : .68f);
                }
                controls[i] = Mathf.Lerp(open[i], closed[i], blend);
            }
            hand.SetControls(controls);
        }
    }
}
