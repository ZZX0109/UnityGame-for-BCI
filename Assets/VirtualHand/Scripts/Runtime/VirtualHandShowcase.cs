using System.Collections.Generic;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Standalone keyboard viewer for the virtual-hand component. It drives the
    /// existing 20-control <see cref="HandRigDriver"/> API, so the same component
    /// works on the Elena hand prefabs, on FirstPersonHands, and on the table Demo
    /// hands, and it references no scene object, HUD or task controller. Hosts can
    /// either use the built-in keys or drive the public API from their own menu.
    ///
    /// Keys (all remappable in the Inspector):
    ///   1..0       jump straight to an action
    ///   E / Q      next / previous action
    ///   B          realistic skin &lt;-&gt; translucent blue outline
    ///   Space      play / pause the animation and the automatic action cycle
    ///   [ / ]      slower / faster
    ///   H          show / hide the on-screen legend
    ///
    /// Behaviours that drive the same HandRigDriver (ControllerHandPair,
    /// ReplayController, TaskController) are suspended while this component is
    /// enabled and restored exactly as they were on disable, so a showcase never
    /// fights the host that owns the hands.
    /// </summary>
    [AddComponentMenu("Neuralife/Virtual Hand/Virtual Hand Showcase")]
    [DisallowMultipleComponent]
    public sealed class VirtualHandShowcase : MonoBehaviour
    {
        public enum ShowcaseAction
        {
            Rest = 0,
            Fist = 1,
            Pinch = 2,
            Point = 3,
            Spread = 4,
            HalfFist = 5,
            OpenCloseCycle = 6,
            FingerWave = 7,
            PinchCycle = 8,
            GripCycle = 9
        }

        /// <summary>Number of leading actions that are static poses.</summary>
        public const int StaticPoseCount = 6;

        static readonly string[] ActionLabels =
        {
            "静息 Rest",
            "抓握 Fist",
            "捏取 Pinch",
            "指向 Point",
            "张开 Spread",
            "半握 Half fist",
            "连续开合 Open/close cycle",
            "逐指波浪 Finger wave",
            "捏合循环 Pinch cycle",
            "握放循环 Grip cycle"
        };

        [Header("Wiring (auto-filled from this hierarchy when left empty)")]
        [Tooltip("Left hand driver. Mirrored side is detected automatically when empty.")]
        public HandRigDriver leftHand;

        [Tooltip("Right hand driver. Mirrored side is detected automatically when empty.")]
        public HandRigDriver rightHand;

        [Tooltip("Optional. Added to this object when empty and createVisualStyle is on.")]
        public AvatarVisualStyle visualStyle;

        [Tooltip("Extra behaviours to switch off while the showcase drives the hands.")]
        public Behaviour[] suspendWhileShowing;

        public bool autoFindHands = true;
        public bool createVisualStyle = true;

        [Tooltip("Suspend the scene's ControllerHandPair / ReplayController / TaskController so they cannot fight the showcase.")]
        public bool suspendOtherHandDrivers = true;

        [Header("Translucent blue")]
        public bool startInBlueMode;

        [ColorUsage(true, true)]
        public Color interiorColor = new Color(0.05f, 0.48f, 0.72f, 1f);

        [ColorUsage(true, true)]
        public Color rimColor = new Color(0.2f, 0.9f, 1f, 1f);

        [Range(0f, 1f)] public float interiorAlpha = 0.12f;
        [Range(0f, 1f)] public float rimAlpha = 0.95f;
        [Range(0.25f, 8f)] public float rimPower = 2f;
        public bool depthPrepass = true;

        [Header("Playback")]
        public ShowcaseAction startAction = ShowcaseAction.Rest;
        [Range(0.05f, 4f)] public float animationSpeed = 1f;
        [Tooltip("Seconds of smoothing used when approaching a pose. Lower is snappier.")]
        [Range(0.016f, 0.5f)] public float smoothing = 0.08f;
        [Tooltip("Advance to the next action automatically while playing.")]
        public bool autoCycleActions;
        [Range(0.5f, 10f)] public float autoCycleSeconds = 2.5f;
        [Tooltip("Offset the right hand by half a cycle so two-handed motion reads as alternating.")]
        public bool offsetRightHandPhase = true;

        [Header("Keys")]
        public bool keyboardEnabled = true;
        public KeyCode nextActionKey = KeyCode.E;
        public KeyCode previousActionKey = KeyCode.Q;
        public KeyCode toggleBlueKey = KeyCode.B;
        public KeyCode playPauseKey = KeyCode.Space;
        public KeyCode helpKey = KeyCode.H;
        public KeyCode slowerKey = KeyCode.LeftBracket;
        public KeyCode fasterKey = KeyCode.RightBracket;

        [Header("On-screen legend")]
        public bool showOnScreenLegend = true;

        int _action;
        float _phase;
        float _cycleTimer;
        bool _paused;
        bool _blueMode;
        bool _warnedNoLegacyInput;

        float[][] _leftPoses;
        float[][] _rightPoses;

        readonly List<Behaviour> _suspended = new List<Behaviour>();
        readonly List<bool> _suspendedWasEnabled = new List<bool>();

        // ---------------------------------------------------------------- public API

        public int ActionCount => ActionLabels.Length;
        public int CurrentActionIndex => _action;
        public string CurrentActionName => ActionLabels[Mathf.Clamp(_action, 0, ActionLabels.Length - 1)];
        public bool BlueModeEnabled => _blueMode;
        public bool IsPlaying => !_paused;
        public float AnimationSpeed
        {
            get => animationSpeed;
            set => animationSpeed = Mathf.Clamp(value, 0.05f, 4f);
        }

        public void SetAction(int index)
        {
            _action = Mathf.Clamp(index, 0, ActionLabels.Length - 1);
            _cycleTimer = 0f;
        }

        public void NextAction() => SetAction((_action + 1) % ActionLabels.Length);

        public void PreviousAction() => SetAction((_action + ActionLabels.Length - 1) % ActionLabels.Length);

        public void SetPlaying(bool playing) => _paused = !playing;

        /// <summary>Normalised animation phase (0-1 is one cycle) of the cyclic actions.</summary>
        public float Phase
        {
            get => _phase;
            set => _phase = value;
        }

        /// <summary>
        /// Applies the current action right away instead of waiting for Update(). Editor
        /// tooling needs this because Update() does not run in edit mode, and a host that
        /// owns its own update loop can call it to stay in control of stepping.
        /// </summary>
        public void ApplyImmediately(float deltaTime = 0f)
        {
            _phase += deltaTime * animationSpeed;
            ApplyCurrentAction(deltaTime <= 0f ? 1f : deltaTime);
        }

        public void ToggleBlueMode() => SetBlueMode(!_blueMode);

        public void SetBlueMode(bool enabled)
        {
            _blueMode = enabled;
            if (!visualStyle)
                return;

            if (enabled)
            {
                visualStyle.interiorColor = interiorColor;
                visualStyle.rimColor = rimColor;
                visualStyle.interiorAlpha = interiorAlpha;
                visualStyle.rimAlpha = rimAlpha;
                visualStyle.rimPower = rimPower;
                visualStyle.depthPrepass = depthPrepass;
            }

            visualStyle.SetBlueMode(enabled);
        }

        /// <summary>Recomputes the cached poses, e.g. after swapping a hand driver at runtime.</summary>
        public void RefreshPoses()
        {
            _leftPoses = BuildPoses(leftHand);
            _rightPoses = BuildPoses(rightHand);
        }

        /// <summary>Hands the drivers back to the suspended host behaviours without disabling this component.</summary>
        public void ResumeHandControl()
        {
            RestoreSuspended();
            enabled = false;
        }

        // ---------------------------------------------------------------- lifecycle

        void OnEnable()
        {
            ResolveReferences();
            if (leftHand) leftHand.CacheBones();
            if (rightHand) rightHand.CacheBones();
            SuspendHandDrivers();
            RefreshPoses();
            _action = Mathf.Clamp((int)startAction, 0, ActionLabels.Length - 1);
            SetBlueMode(startInBlueMode);
        }

        void OnDisable()
        {
            SetBlueMode(false);
            RestoreSuspended();
        }

        void Update()
        {
            HandleKeys();

            float dt = Time.unscaledDeltaTime;
            if (!_paused)
            {
                _phase += dt * animationSpeed;

                if (autoCycleActions)
                {
                    _cycleTimer += dt;
                    if (_cycleTimer >= autoCycleSeconds)
                    {
                        _cycleTimer = 0f;
                        NextAction();
                    }
                }
            }

            ApplyCurrentAction(dt);
        }

        // ---------------------------------------------------------------- wiring

        void ResolveReferences()
        {
            if (autoFindHands && (!leftHand || !rightHand))
            {
                HandRigDriver[] drivers = GetComponentsInChildren<HandRigDriver>(true);
                foreach (HandRigDriver driver in drivers)
                {
                    if (!driver)
                        continue;

                    if (driver.IsMirrored)
                    {
                        if (!leftHand) leftHand = driver;
                    }
                    else if (!rightHand)
                    {
                        rightHand = driver;
                    }
                }

                // Fall back to name matching when both drivers report the same side.
                if (!leftHand || !rightHand)
                {
                    foreach (HandRigDriver driver in drivers)
                    {
                        if (!driver)
                            continue;

                        bool looksLeft = driver.name.IndexOf('L') >= 0 || driver.name.ToLowerInvariant().Contains("left");
                        if (looksLeft && !leftHand) leftHand = driver;
                        else if (!looksLeft && !rightHand) rightHand = driver;
                    }
                }
            }

            if (createVisualStyle && !visualStyle)
            {
                visualStyle = GetComponent<AvatarVisualStyle>();
                if (!visualStyle)
                    visualStyle = gameObject.AddComponent<AvatarVisualStyle>();
            }

            if (visualStyle && (visualStyle.targets == null || visualStyle.targets.Length == 0))
            {
                var renderers = new List<Renderer>();
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                    if (renderer && renderer.enabled)
                        renderers.Add(renderer);

                if (renderers.Count > 0)
                    visualStyle.targets = renderers.ToArray();
            }
        }

        void SuspendHandDrivers()
        {
            _suspended.Clear();
            _suspendedWasEnabled.Clear();

            var seen = new HashSet<Behaviour>();

            void Suspend(Behaviour behaviour)
            {
                if (!behaviour || behaviour == this || !seen.Add(behaviour))
                    return;

                _suspended.Add(behaviour);
                _suspendedWasEnabled.Add(behaviour.enabled);
                behaviour.enabled = false;
            }

            if (suspendWhileShowing != null)
                foreach (Behaviour behaviour in suspendWhileShowing)
                    Suspend(behaviour);

            if (!suspendOtherHandDrivers)
                return;

            foreach (ControllerHandPair pair in FindObjectsOfType<ControllerHandPair>(true))
                Suspend(pair);
            foreach (ReplayController replay in FindObjectsOfType<ReplayController>(true))
                Suspend(replay);
            foreach (TaskController task in FindObjectsOfType<TaskController>(true))
                Suspend(task);
        }

        void RestoreSuspended()
        {
            for (int i = 0; i < _suspended.Count; i++)
                if (_suspended[i])
                    _suspended[i].enabled = _suspendedWasEnabled[i];

            _suspended.Clear();
            _suspendedWasEnabled.Clear();
        }

        // ---------------------------------------------------------------- poses

        static float[] Copy(float[] source)
        {
            var copy = new float[20];
            for (int i = 0; i < 20 && i < source.Length; i++)
                copy[i] = source[i];
            return copy;
        }

        /// <summary>Cached rest/fist/pinch/point/spread/half poses for one driver.</summary>
        static float[][] BuildPoses(HandRigDriver driver)
        {
            if (!driver)
                return null;

            float[] rest = PoseLibrary.OpenPose(driver);
            float[] fist = PoseLibrary.FistPose(driver);
            float[] pinch = PoseLibrary.PinchPose(driver);

            float[] point = Copy(PoseLibrary.Point());

            // Copy before mutating: OpenPose may return the driver's own profile array.
            float[] spread = Copy(rest);
            spread[7] = 1f;
            spread[11] = 1f;
            spread[15] = 1f;
            spread[19] = 1f;

            return new[]
            {
                rest,
                fist,
                pinch,
                point,
                spread,
                PoseLibrary.Lerp(rest, fist, 0.5f)
            };
        }

        void ApplyCurrentAction(float dt)
        {
            var action = (ShowcaseAction)_action;
            ApplyToDriver(leftHand, _leftPoses, action, dt, 0);
            ApplyToDriver(rightHand, _rightPoses, action, dt, 1);
        }

        void ApplyToDriver(HandRigDriver driver, float[][] poses, ShowcaseAction action, float dt, int side)
        {
            if (!driver || poses == null)
                return;

            float phase = _phase + (offsetRightHandPhase && side == 1 ? 0.5f : 0f);
            float[] target = BuildTarget(poses, action, phase);
            if (target != null)
                driver.UpdateControls(target, smoothing, dt);
        }

        static float[] BuildTarget(float[][] poses, ShowcaseAction action, float phase)
        {
            if ((int)action < StaticPoseCount)
                return poses[(int)action];

            switch (action)
            {
                case ShowcaseAction.OpenCloseCycle:
                    return PoseLibrary.Lerp(poses[0], poses[1], Pulse(phase));
                case ShowcaseAction.PinchCycle:
                    return PoseLibrary.Lerp(poses[0], poses[2], Pulse(phase));
                case ShowcaseAction.GripCycle:
                    return PoseLibrary.Lerp(poses[0], poses[1], GripProfile(phase));
                case ShowcaseAction.FingerWave:
                    return FingerWave(poses[0], phase);
                default:
                    return poses[0];
            }
        }

        /// <summary>Smooth 0-1-0 pulse over one cycle.</summary>
        static float Pulse(float phase) => 0.5f - 0.5f * Mathf.Cos(phase * 2f * Mathf.PI);

        /// <summary>Close, hold, then open again, all inside one cycle.</summary>
        static float GripProfile(float phase)
        {
            float x = phase - Mathf.Floor(phase);
            if (x < 0.4f)
                return Mathf.SmoothStep(0f, 1f, x / 0.4f);
            if (x < 0.75f)
                return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (x - 0.75f) / 0.25f);
        }

        /// <summary>A flexion wave travelling index to little, with a light thumb follow.</summary>
        static float[] FingerWave(float[] rest, float phase)
        {
            float[] pose = Copy(rest);
            for (int finger = 0; finger < 4; finger++)
            {
                float curl = Pulse(phase - finger * 0.18f);
                int o = (finger + 1) * 4;
                pose[o] = curl;
                pose[o + 1] = curl;
                pose[o + 2] = curl * 0.9f;
            }

            float thumb = Pulse(phase - 0.6f);
            pose[0] = 0.15f + 0.4f * thumb;
            pose[1] = 0.1f + 0.5f * thumb;
            pose[2] = 0.1f + 0.45f * thumb;
            pose[3] = Mathf.Lerp(rest[3], 1f, thumb);
            return pose;
        }

        // ---------------------------------------------------------------- input

        void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!keyboardEnabled)
                return;

            for (int i = 0; i < 9 && i < ActionLabels.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    SetAction(i);

            if (ActionLabels.Length > 9 && Input.GetKeyDown(KeyCode.Alpha0))
                SetAction(9);

            if (Input.GetKeyDown(nextActionKey)) NextAction();
            if (Input.GetKeyDown(previousActionKey)) PreviousAction();
            if (Input.GetKeyDown(toggleBlueKey)) ToggleBlueMode();
            if (Input.GetKeyDown(playPauseKey)) SetPlaying(_paused);
            if (Input.GetKeyDown(helpKey)) showOnScreenLegend = !showOnScreenLegend;
            if (Input.GetKeyDown(slowerKey)) AnimationSpeed = animationSpeed - 0.1f;
            if (Input.GetKeyDown(fasterKey)) AnimationSpeed = animationSpeed + 0.1f;
#else
            if (keyboardEnabled && !_warnedNoLegacyInput)
            {
                _warnedNoLegacyInput = true;
                Debug.LogWarning("[VirtualHandShowcase] This project disabled the legacy Input Manager, " +
                                 "so the built-in keys are inert. Drive SetAction / NextAction / SetBlueMode " +
                                 "from the host's own input instead.", this);
            }
#endif
        }

        void OnGUI()
        {
            if (!showOnScreenLegend || !keyboardEnabled)
                return;

            const float width = 340f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 24f * (ActionLabels.Length + 5) + 16f), GUI.skin.box);

            GUILayout.Label("<b>虚拟手组件 · 键盘预览</b>", RichLabel());
            GUILayout.Label(CurrentActionName + (_paused ? "   [已暂停]" : ""), RichLabel());
            GUILayout.Label(_blueMode ? "外观：透明蓝色" : "外观：写实肤色", RichLabel());
            GUILayout.Label("速度 " + animationSpeed.ToString("0.00") + "x", RichLabel());

            for (int i = 0; i < ActionLabels.Length; i++)
            {
                string key = i < 9 ? (i + 1).ToString() : "0";
                string prefix = i == _action ? "▶ " : "   ";
                GUILayout.Label(prefix + key + "  " + ActionLabels[i], RichLabel());
            }

            GUILayout.Label(nextActionKey + "/" + previousActionKey + " 上/下一个   " + toggleBlueKey + " 蓝色", RichLabel());
            GUILayout.Label(playPauseKey + " 播放/暂停   [ ] 速度   " + helpKey + " 隐藏", RichLabel());

            GUILayout.EndArea();
        }

        static GUIStyle _richLabel;

        static GUIStyle RichLabel()
        {
            if (_richLabel == null)
            {
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            }

            return _richLabel;
        }
    }
}
