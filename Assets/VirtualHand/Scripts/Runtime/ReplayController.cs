using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Owns the single replay clock. Pausing stops both pose playback and any
    /// task motion that also ticks from this component. Reset clears pose,
    /// attachment state, clock and source label.
    /// </summary>
    public class ReplayController : MonoBehaviour
    {
        public ReplaySource source;
        public HandRigDriver leftHand;
        public HandRigDriver rightHand;

        [Header("Pose smoothing (s)")]
        public float smoothTime = 0.12f;

        [Header("Gap handling")]
        [Tooltip("Seconds after the last frame before active intent expires to rest.")]
        public double gapExpiry = 1.0;

        public string CurrentIntent { get; private set; } = "rest";
        public string CurrentSource { get; private set; } = "manual";
        public float CurrentConfidence { get; private set; } = 1f;
        public string SubjectRun { get; private set; } = "";
        public string LastWarning { get; private set; } = "";

        double _lastFrameTime = -1;
        float[] _leftTarget;
        float[] _rightTarget;

        public event Action<HandMotionFrame> OnFrameApplied;

        void Awake()
        {
            if (source == null) source = GetComponent<ReplaySource>();
            if (source != null) source.FrameReady += ApplyFrame;
        }

        void OnDestroy() { if (source != null) source.FrameReady -= ApplyFrame; }

        /// <summary>Wire contract: prediction frame time must not precede its window end.</summary>
        public void ApplyFrame(HandMotionFrame f)
        {
            if (f == null) return;
            if (f.source == "prediction" && f.time < _lastFrameTime)
                Debug.LogWarning($"[Replay] prediction frame at t={f.time:0.00} precedes previous frame; applying anyway.");
            _lastFrameTime = f.time;
            CurrentIntent = f.intent;
            CurrentSource = f.source;
            CurrentConfidence = f.confidence;
            SubjectRun = $"{f.subject}|{f.run}";

            _leftTarget = f.leftJoints;
            _rightTarget = f.rightJoints;

            // Missing per-hand pose falls back to authored intent poses.
            bool gripL = IntentMapper.HandShouldGrip(f.intent, isLeftHand: true);
            bool gripR = IntentMapper.HandShouldGrip(f.intent, isLeftHand: false);
            if (_leftTarget == null)
                _leftTarget = gripL ? PoseLibrary.GripPose() : PoseLibrary.OpenPose();
            if (_rightTarget == null)
                _rightTarget = gripR ? PoseLibrary.GripPose() : PoseLibrary.OpenPose();

            OnFrameApplied?.Invoke(f);
        }

        void Update()
        {
            double dt = Time.deltaTime;
            source?.Tick(dt);
            float smooth = (float)smoothTime;
            if (leftHand != null && _leftTarget != null)
            {
                leftHand.UpdateControls(_leftTarget, smooth, (float)dt);
                if (leftHand.IsMirrored == false) leftHand.SetMirrored(true);
            }
            if (rightHand != null && _rightTarget != null)
            {
                rightHand.UpdateControls(_rightTarget, smooth, (float)dt);
                if (rightHand.IsMirrored) rightHand.SetMirrored(false);
            }
        }

        public void Pause() => source?.Pause();
        public void Play() => source?.Play();

        public void Restart()
        {
            source?.ResetPlayback();
            CurrentIntent = "rest"; CurrentSource = "manual"; CurrentConfidence = 1f;
            SubjectRun = ""; _lastFrameTime = -1;
            _leftTarget = PoseLibrary.OpenPose();
            _rightTarget = PoseLibrary.OpenPose();
            leftHand?.ResetControls();
            rightHand?.ResetControls();
        }

        public void SetManualPose(float[] leftPose, float[] rightPose)
        {
            _leftTarget = leftPose; _rightTarget = rightPose;
            CurrentSource = "manual";
        }

        public void Warn(string msg)
        {
            LastWarning = msg;
            Debug.LogWarning("[Replay] " + msg);
        }
    }
}
