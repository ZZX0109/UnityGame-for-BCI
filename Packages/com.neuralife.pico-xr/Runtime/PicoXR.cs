using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace Neuralife.XR
{
    [DefaultExecutionOrder(-10000)]
    public sealed class PicoXR : MonoBehaviour
    {
        private static PicoXR instance;
        private InputActionMap actions;
        private InputAction headPosition, headRotation, headTracked, leftTracked, rightTracked;
        private InputAction leftAxis, rightAxis, trigger, a, b, x, y, stick;
        private InputAction rightPosition, rightRotation;
        private readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        private bool focused = true, paused;
        private string failure;
        public static readonly PicoControls Controls = new PicoControls();
#if UNITY_EDITOR
        public static string PreviewKey => "Neuralife.PicoXR.Preview." + Application.dataPath;
#endif
        public static bool Requested
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetBool(PreviewKey, false);
#elif NEURALIFE_PICO_XR || UNITY_ANDROID
                return true;
#else
                return commandRequestsVR;
#endif
            }
        }
#if !UNITY_EDITOR && !NEURALIFE_PICO_XR && !UNITY_ANDROID
        private static readonly bool commandRequestsVR = Array.IndexOf(Environment.GetCommandLineArgs(), "--pico-vr") >= 0;
#endif
        public static bool Ready { get; private set; }
        public static bool HeadTracked { get; private set; }
        public static bool RightTracked { get; private set; }
        public static bool FloorTracking { get; private set; }
        public static Vector3 HeadPosition { get; private set; }
        public static Quaternion HeadRotation { get; private set; } = Quaternion.identity;
        public static Vector3 RightPosition { get; private set; }
        public static Quaternion RightRotation { get; private set; } = Quaternion.identity;
        public static bool UIHasTarget => PicoRayInputModule.Current && PicoRayInputModule.Current.HasTarget;
        public static bool PrimaryPressed => Controls.Select.Pressed && !UIHasTarget;
        public static bool MovementAllowed => Ready && HeadTracked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null; Ready = HeadTracked = RightTracked = FloorTracking = false;
            Controls.Sample(false, false, false, Vector2.zero, Vector2.zero, false, false, false, false, false, false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Launch()
        {
            if (!Requested) return;
            var owner = new GameObject("Neuralife PICO XR");
            DontDestroyOnLoad(owner); owner.AddComponent<PicoXR>();
        }

        private void Awake()
        {
            if (instance) { Destroy(gameObject); return; }
            instance = this;
            actions = new InputActionMap("Neuralife XR");
            headPosition = Action("Head position", "<XRHMD>/devicePosition", "Vector3");
            headRotation = Action("Head rotation", "<XRHMD>/deviceRotation", "Quaternion");
            headTracked = Action("Head tracked", "<XRHMD>/isTracked", "Button");
            leftTracked = Action("Left tracked", "<XRController>{LeftHand}/isTracked", "Button");
            rightTracked = Action("Right tracked", "<XRController>{RightHand}/isTracked", "Button");
            leftAxis = Action("Move", "<XRController>{LeftHand}/{Primary2DAxis}", "Vector2");
            rightAxis = Action("Turn and scroll", "<XRController>{RightHand}/{Primary2DAxis}", "Vector2");
            trigger = Action("Select", "<XRController>{RightHand}/{TriggerButton}", "Button");
            a = Action("Undo", "<XRController>{RightHand}/{PrimaryButton}", "Button");
            b = Action("Reset", "<XRController>{RightHand}/{SecondaryButton}", "Button");
            x = Action("Pause", "<XRController>{LeftHand}/{PrimaryButton}", "Button");
            y = Action("Auxiliary", "<XRController>{LeftHand}/{SecondaryButton}", "Button");
            stick = Action("Recenter", "<XRController>{RightHand}/{Primary2DAxisClick}", "Button");
            rightPosition = Action("Pointer position", "<XRController>{RightHand}/pointerPosition", "Vector3");
            rightRotation = Action("Pointer rotation", "<XRController>{RightHand}/pointerRotation", "Quaternion");
            actions.Enable(); // All bindings exist before OpenXR creates its action sets.

        }

        // Manual XR initialization must wait for graphics initialization.
        private IEnumerator Start() { yield return null; yield return StartXR(); }

        private InputAction Action(string label, string binding, string control)
            => actions.AddAction(label, InputActionType.Value, binding, expectedControlLayout: control);

        private IEnumerator StartXR()
        {
            XRManagerSettings manager = XRGeneralSettings.Instance ? XRGeneralSettings.Instance.Manager : null;
            if (!manager) { Fail("XR settings are missing. Run Neuralife > PICO XR > Configure project."); yield break; }
            yield return manager.InitializeLoader();
            if (!manager.activeLoader) { Fail("No OpenXR headset. Connect PICO streaming / select an OpenXR runtime, then restart."); yield break; }
            manager.StartSubsystems();
            SubsystemManager.GetInstances(inputs);
            foreach (XRInputSubsystem input in inputs)
                if (input.running) { input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor); FloorTracking = input.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor; break; }
            Ready = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1; // XR runtime owns the headset refresh rate.
            Debug.Log("Neuralife PICO XR started; device acceptance NOT_TESTED until headset evidence is recorded.");
        }

        private void Fail(string message) { failure = message; Debug.LogWarning("PICO XR: " + message); }
        private void Update()
        {
            bool tracked = Ready && focused && !paused && headTracked.ReadValue<float>() > 0.5f;
            UnityEngine.XR.InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (hmd.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool present) && !present) tracked = false;
            HeadTracked = tracked;
            RightTracked = tracked && rightTracked.ReadValue<float>() > 0.5f;
            ReadPoses();
            // Buttons and sticks remain usable when the controller is connected but its pose is outside tracking view.
            Controls.Sample(tracked, leftAxis.controls.Count > 0, a.controls.Count > 0,
                leftAxis.ReadValue<Vector2>(), rightAxis.ReadValue<Vector2>(), trigger.ReadValue<float>() > 0.5f,
                a.ReadValue<float>() > 0.5f, b.ReadValue<float>() > 0.5f, x.ReadValue<float>() > 0.5f,
                y.ReadValue<float>() > 0.5f, stick.ReadValue<float>() > 0.5f);
        }

        internal static void ReadPoses()
        {
            if (!instance || !Ready) return;
            HeadPosition = instance.headPosition.ReadValue<Vector3>();
            HeadRotation = ValidRotation(instance.headRotation.ReadValue<Quaternion>());
            RightPosition = instance.rightPosition.ReadValue<Vector3>();
            RightRotation = ValidRotation(instance.rightRotation.ReadValue<Quaternion>());
        }
        private static Quaternion ValidRotation(Quaternion q)
        {
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return float.IsNaN(norm) || float.IsInfinity(norm) || norm < 0.01f ? Quaternion.identity : q.normalized;
        }
        private void OnApplicationFocus(bool value) { focused = value; }
        private void OnApplicationPause(bool value) { paused = value; }
        private void OnGUI()
        {
            if (!Requested || Ready) return;
            GUI.Box(new Rect(20, 20, Mathf.Min(Screen.width - 40, 780), 100),
                failure ?? "PICO VR: connecting to the OpenXR headset...");
        }
        private void OnDestroy()
        {
            if (instance != this) return;
            actions?.Dispose();
            XRManagerSettings manager = XRGeneralSettings.Instance ? XRGeneralSettings.Instance.Manager : null;
            if (manager && manager.activeLoader) { manager.StopSubsystems(); manager.DeinitializeLoader(); }
            Ready = HeadTracked = RightTracked = false; instance = null;
        }
    }
}
