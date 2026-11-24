using System;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Owns the single replay clock and the validity of the active input. Pauses freeze
    /// both pose smoothing and task motion. Expired frames become "rest" visually but are
    /// explicitly marked invalid so a task never treats an input gap as a release command.
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
        public bool HasValidInput { get; private set; } = true;
        public bool IsMirroredDisplay { get; private set; }
        public double Position => source != null ? source.Position : 0;
        public double Duration => source != null ? source.Duration : 0;

        double _lastFrameTime = -1;
        double _validUntil = double.MaxValue;
        float[] _leftTarget;
        float[] _rightTarget;
        bool _manualMode;
        public bool IsManualPreview => _manualMode;

        public event Action<HandMotionFrame> OnFrameApplied;
        public event Action OnReset;

        TaskController _tasks;

        void Awake()
        {
            _tasks = GetComponent<TaskController>();
            if (source == null) source = GetComponent<ReplaySource>();
            if (source != null)
            {
                source.FrameReady += ApplyFrame;
                source.SourceChanged += OnSourceChanged;
            }
        }

        void OnDestroy()
        {
            if (source != null)
            {
                source.FrameReady -= ApplyFrame;
                source.SourceChanged -= OnSourceChanged;
            }
        }

        /// <summary>
        /// Spec 3.3: switching source must clear the previous stream's target pose and any
        /// action events that were produced but never consumed.
        ///
        /// Without this the controller kept serving the old stream's intent and grip, so a
        /// newly opened replay would briefly drive the hand from stale data — and a task
        /// mid-grasp would be acting on an intent that no longer exists.
        /// </summary>
        void OnSourceChanged()
        {
            _lastFrameTime = 0.0;
            _validUntil = 0.0;
            HasValidInput = false;
            CurrentIntent = "rest";
            CurrentConfidence = 0f;
            CurrentSource = "manual";
            SubjectRun = "";
            _leftTarget = null;
            _rightTarget = null;
            IsMirroredDisplay = false;

            // Un-consumed task progress belongs to the old stream. Clear it, but do NOT
            // arm the tracking-resync gate: opening a different replay is not a tracking
            // loss, and freezing the task for the resync window on every load would stall
            // legitimate progress.
            if (_tasks != null)
                _tasks.ClearStaleState();

            Debug.Log("[Replay] source changed — cleared the previous stream's target pose and pending state.");
        }

        /// <summary>Apply one decoded frame; validity expires after duration+gap.</summary>
        public void ApplyFrame(HandMotionFrame f)
        {
            if (f == null) return;
            if (_manualMode)
            {
                Debug.LogWarning("[Replay] Replay frame ignored — manual mode is active.");
                return;
            }
            if (f.version < 1)
            {
                Debug.LogWarning($"[Replay] Unsupported replay version {f.version}; ignoring frame.");
                return;
            }
            if (f.source == "prediction" && f.time < _lastFrameTime)
                Debug.LogWarning($"[Replay] prediction frame at t={f.time:0.00} precedes previous frame; applying anyway.");
            _lastFrameTime = f.time;
            _validUntil = f.time + Math.Max(0, f.duration) + Math.Max(0, gapExpiry);
            HasValidInput = true;
            _manualMode = false;
            CurrentIntent = f.intent;
            CurrentSource = f.source;
            CurrentConfidence = f.confidence;
            SubjectRun = $"{f.subject}|{f.run}";

            _leftTarget = f.leftJoints;
            _rightTarget = f.rightJoints;
            // DB8 glove is left-hand only; the right-hand display is an explicit mirror.
            IsMirroredDisplay = f.source == "glove" && f.leftJoints != null && f.rightJoints == null;
            if (IsMirroredDisplay) _rightTarget = f.leftJoints;

            bool gripL = IntentMapper.HandShouldGrip(f.intent, isLeftHand: true);
            bool gripR = IntentMapper.HandShouldGrip(f.intent, isLeftHand: false);
            if (_leftTarget == null)
                _leftTarget = gripL ? PoseLibrary.FistPose(leftHand)
                                    : PoseLibrary.OpenPose(leftHand);
            if (_rightTarget == null)
                _rightTarget = gripR ? PoseLibrary.FistPose(rightHand)
                                     : PoseLibrary.OpenPose(rightHand);

            OnFrameApplied?.Invoke(f);
        }

        void Update()
        {
            double dt = Time.deltaTime;
            if (source != null && source.IsPlaying)
            {
                source.Tick(dt);
                if (HasValidInput && source.Position > _validUntil)
                {
                    HasValidInput = false;
                    CurrentIntent = "rest";       // open visually, but not a valid release command
                    _leftTarget = PoseLibrary.OpenPose(leftHand);
                    _rightTarget = PoseLibrary.OpenPose(rightHand);
                }
            }
            // Manual preview is the sole exception that may animate while playback is paused.
            if (source != null && !source.IsPlaying && !_manualMode) return;
            if (_tasks != null && _tasks.ControlsTaskHands) return;
            float smooth = smoothTime;
            if (leftHand != null && _leftTarget != null)
                leftHand.UpdateControls(_leftTarget, smooth, (float)dt);
            if (rightHand != null && _rightTarget != null)
                rightHand.UpdateControls(_rightTarget, smooth, (float)dt);
            ResolveGloveContact((float)dt);
        }

        public void Pause() => source?.Pause();
        public void Play() => source?.Play();

        /// <summary>Restore the latest unmodified replay targets after task-only visual assistance ends.</summary>
        public void RestoreCurrentTargets()
        {
            if (leftHand != null)
            {
                if (_leftTarget != null && _leftTarget.Length == 20) leftHand.SetControls(_leftTarget);
                else leftHand.ResetControls();
            }
            if (rightHand != null)
            {
                if (_rightTarget != null && _rightTarget.Length == 20) rightHand.SetControls(_rightTarget);
                else rightHand.ResetControls();
            }
            ResolveGloveContact(-1f);
        }

        void ResolveGloveContact(float deltaTime)
        {
            if (CurrentSource != "glove") return;
            if (leftHand != null) leftHand.ResolveGloveThumbContact(deltaTime);
            if (rightHand != null) rightHand.ResolveGloveThumbContact(deltaTime);
        }

        public void Restart()
        {
            leftHand?.ResetGloveThumbContact();
            rightHand?.ResetGloveThumbContact();
            source?.ResetPlayback();
            CurrentIntent = "rest";
            CurrentSource = "manual";
            CurrentConfidence = 1f;
            SubjectRun = "";
            LastWarning = "";
            HasValidInput = true;
            IsMirroredDisplay = false;
            _manualMode = false;
            _lastFrameTime = -1;
            _validUntil = double.MaxValue;
            _leftTarget = PoseLibrary.OpenPose(leftHand);
            _rightTarget = PoseLibrary.OpenPose(rightHand);
            leftHand?.ResetControls();
            rightHand?.ResetControls();
            OnReset?.Invoke();
        }

        /// <summary>Explicit manual entry: stop and clear replay instead of mixing sources.</summary>
        public void SetManualPose(float[] leftPose, float[] rightPose)
        {
            Restart();
            source?.Pause();
            _manualMode = true;
            CurrentIntent = "rest";
            CurrentSource = "manual";
            CurrentConfidence = 1f;
            SubjectRun = "";
            HasValidInput = true;
            IsMirroredDisplay = false;
            _validUntil = double.MaxValue;
            _leftTarget = leftPose;
            _rightTarget = rightPose;
        }

        public void Warn(string msg)
        {
            LastWarning = msg;
            Debug.LogWarning("[Replay] " + msg);
        }
    }
}
