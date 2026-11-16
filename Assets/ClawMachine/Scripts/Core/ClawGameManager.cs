using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ClawMachine
{
    public sealed class ClawGameManager : MonoBehaviour
    {
        public static bool AutoPauseOnFocusLoss = true;
        [SerializeField] private GraspTaskController task;
        [SerializeField] private ClawInputController keyboard;
        [SerializeField] private CraneCameraController cameras;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private ClawAudioController audioController;
        [SerializeField] private ClawChallengeDefinition[] challenges;
        private IClawInputSource input;
        private ClawGameUI ui;
        private ClawPreferences preferences;
        private EventSystem pointerEventSystem;
        private PointerEventData pointerData;
        private readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
        private bool settingsPausedBefore, resetPausedBefore, settingsDirty;
        private float saveAt;
        private float savedTimeScale = 1;
        public GraspTaskController Task => task;
        public CraneCameraController Cameras => cameras;
        public IReadOnlyList<ClawChallengeDefinition> Challenges => challenges;
        public int SelectedChallengeIndex { get; private set; }
        public bool InMenu { get; private set; } = true;
        public bool SettingsOpen { get; private set; }
        public bool ResetConfirmation { get; private set; }
        public bool IsPaused { get; private set; }
        public float Volume { get; private set; } = 0.25f;
        public float TextScale { get; private set; } = 1;
        public bool PositionAssist => preferences == null || preferences.positionAssist;
        public bool CenterOfMassAssist => preferences == null || preferences.centerOfMassAssist;
        private void Awake()
        {
            input = keyboard;
            ui = GetComponent<ClawGameUI>();
            preferences = ClawPreferences.Load(); Volume = preferences.volume; TextScale = preferences.textScale;
            if (!task || !keyboard || !cameras || !cameras.IsConfigured || challenges == null || challenges.Length < 3)
            { enabled = false; Debug.LogError("Claw scene references are incomplete.", this); return; }
            cameras.Smooth = preferences.smooth; cameras.SetPipQuality(preferences.pipLowQuality);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        private void Start() { if (audioController) { audioController.Initialize(task, task.Driver, audioSource); audioController.SetVolume(Volume); } }
        public void SetInputSource(IClawInputSource source) { input = source ?? keyboard; }
        public bool SelectChallenge(int index)
        {
            if (!InMenu || challenges == null || index < 0 || index >= challenges.Length || !challenges[index]) return false;
            SelectedChallengeIndex = index; return task.SetChallenge(challenges[index], false);
        }
        private void Update()
        {
            if (settingsDirty && Time.unscaledTime >= saveAt) FlushSettings();
            if (!task || input == null) return;
            ClawInputFrame frame = input.Read();
            if (frame.Pause)
            {
                if (SettingsOpen) CloseSettings(); else if (ResetConfirmation) DeclineReset(); else if (!InMenu) Pause(!IsPaused);
            }
            if (frame.View != 0) cameras.Select(frame.View);
            if (frame.Pip) cameras.TogglePip();
            ZoomObservation(frame.Zoom, Input.mousePosition);
            if (frame.Reset && !InMenu && !ResetConfirmation) RequestReset();
            task.SetAim(!InMenu && !IsPaused && !ResetConfirmation && !SettingsOpen ? frame.Move : Vector2.zero);
            // A focused UI button receives Submit from StandaloneInputModule. Do
            // not also interpret that same press as a global task command.
            GameObject selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            bool uiSubmit = ReferenceEquals(input, keyboard) && selected && selected.activeInHierarchy && selected.GetComponent<UnityEngine.UI.Button>();
            if (frame.Grab && !uiSubmit && !InMenu && !IsPaused && !SettingsOpen && !ResetConfirmation)
            {
                if (task.Session.State == GraspState.Result) task.Retry(); else if (task.Session.State == GraspState.End) StartGame(); else task.StartGrab();
            }
        }
        public bool AllowsObservationPointer(Vector2 point)
        {
            if (!cameras || !cameras.CurrentCamera || !cameras.CurrentCamera.pixelRect.Contains(point)) return false;
            if (ui && !ui.AllowsObservationPointer(point)) return false;
            if (!EventSystem.current) return true;
            if (pointerEventSystem != EventSystem.current)
            { pointerEventSystem = EventSystem.current; pointerData = new PointerEventData(pointerEventSystem); }
            pointerData.Reset(); pointerData.position = point; pointerHits.Clear();
            pointerEventSystem.RaycastAll(pointerData, pointerHits);
            return pointerHits.Count == 0;
        }
        public bool ZoomObservation(float amount, Vector2 point)
        {
            if (amount == 0 || !AllowsObservationPointer(point)) return false;
            cameras.Zoom(amount); return true;
        }
        public void StartGame()
        {
            Pause(false); SettingsOpen = ResetConfirmation = false;
            InMenu = challenges == null || SelectedChallengeIndex >= challenges.Length || !task.SetChallenge(challenges[SelectedChallengeIndex], true);
            if (audioController) audioController.Initialize(task, task.Driver, audioSource);
        }
        public void Grab() { if (!IsPaused && !InMenu && !SettingsOpen && !ResetConfirmation) task.StartGrab(); }
        public void Retry() { if (!IsPaused && !InMenu) task.Retry(); }
        public void Pause(bool value)
        {
            if (value == IsPaused) return;
            if (value) { savedTimeScale = Time.timeScale; Time.timeScale = 0; } else Time.timeScale = savedTimeScale;
            IsPaused = value; if (task) task.SetPaused(value);
            if (audioController) audioController.SetPaused(value);
        }
        public void OpenSettings() { if (SettingsOpen) return; settingsPausedBefore = IsPaused; SettingsOpen = true; if (!InMenu) Pause(true); }
        public void CloseSettings() { if (!SettingsOpen) return; SettingsOpen = false; Pause(settingsPausedBefore); FlushSettings(); }
        public void RequestReset() { if (ResetConfirmation) return; resetPausedBefore = IsPaused; ResetConfirmation = true; Pause(true); }
        public void DeclineReset() { if (!ResetConfirmation) return; ResetConfirmation = false; Pause(resetPausedBefore); }
        public void ConfirmReset() { ResetConfirmation = false; StartGame(); }
        public void ReturnToMenu() { SettingsOpen = ResetConfirmation = false; Pause(false); task.Cancel(true); InMenu = true; }
        public void SetVolume(float value) { Volume = ClawPreferences.Sanitize(value, 0.25f, 0, 1); if (audioController) audioController.SetVolume(Volume); MarkSettingsDirty(); }
        public void SetTextScale(float value) { TextScale = ClawPreferences.Sanitize(value, 1, 0.9f, 1.2f); MarkSettingsDirty(); }
        public void SetPositionAssist(bool value) { EnsurePreferences(); preferences.positionAssist = value; MarkSettingsDirty(); }
        public void SetCenterOfMassAssist(bool value) { EnsurePreferences(); preferences.centerOfMassAssist = value; MarkSettingsDirty(); }
        public void SetCameraSmooth(bool value) { if (cameras) cameras.Smooth = value; MarkSettingsDirty(); }
        public void SetPipQuality(bool lowQuality) { if (cameras) cameras.SetPipQuality(lowQuality); MarkSettingsDirty(); }
        private void EnsurePreferences() { if (preferences == null) preferences = new ClawPreferences(); }
        private void MarkSettingsDirty() { EnsurePreferences(); settingsDirty = true; saveAt = Time.unscaledTime + 0.5f; }
        public void FlushSettings()
        {
            if (!settingsDirty) return;
            EnsurePreferences(); preferences.volume = Volume; preferences.textScale = TextScale;
            if (cameras) { preferences.smooth = cameras.Smooth; preferences.pipLowQuality = cameras.PipLowQuality; }
            preferences.Save(); settingsDirty = false;
        }
        private void OnDisable() { FlushSettings(); Pause(false); if (audioController) audioController.Release(); if (audioSource) audioSource.Stop(); }
        private void LostFocus()
        {
            FlushSettings();
            if (!AutoPauseOnFocusLoss || InMenu) return;
            // Closing settings must never undo a later focus-loss pause.
            if (SettingsOpen) settingsPausedBefore = true;
            if (ResetConfirmation) resetPausedBefore = true;
            Pause(true);
        }
        private void OnApplicationFocus(bool focused) { if (!focused) LostFocus(); }
        private void OnApplicationPause(bool paused) { if (paused) LostFocus(); }
        private void OnApplicationQuit() { FlushSettings(); Pause(false); }
    }
}
