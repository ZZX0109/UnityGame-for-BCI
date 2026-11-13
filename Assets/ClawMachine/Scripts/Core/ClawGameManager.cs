using System.Collections.Generic;
using UnityEngine;

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
        private void Awake()
        {
            input = keyboard;
            if (!task || !keyboard || !cameras || !cameras.IsConfigured || challenges == null || challenges.Length < 3)
            { enabled = false; Debug.LogError("Claw scene references are incomplete.", this); return; }
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
            if (!task || input == null) return;
            ClawInputFrame frame = input.Read();
            if (frame.Pause)
            {
                if (SettingsOpen) CloseSettings(); else if (ResetConfirmation) DeclineReset(); else if (!InMenu) Pause(!IsPaused);
            }
            if (frame.View != 0) cameras.Select(frame.View);
            if (frame.Pip) cameras.TogglePip();
            cameras.Zoom(frame.Zoom);
            if (frame.Reset && !InMenu && !ResetConfirmation) RequestReset();
            task.SetAim(!InMenu && !IsPaused && !ResetConfirmation && !SettingsOpen ? frame.Move : Vector2.zero);
            if (frame.Grab && !InMenu && !IsPaused && !SettingsOpen && !ResetConfirmation)
            {
                if (task.Session.State == GraspState.Result) task.Retry(); else if (task.Session.State == GraspState.End) StartGame(); else task.StartGrab();
            }
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
        public void OpenSettings() { SettingsOpen = true; if (!InMenu) Pause(true); }
        public void CloseSettings() { SettingsOpen = false; }
        public void RequestReset() { ResetConfirmation = true; Pause(true); }
        public void DeclineReset() { ResetConfirmation = false; }
        public void ConfirmReset() { ResetConfirmation = false; StartGame(); }
        public void ReturnToMenu() { SettingsOpen = ResetConfirmation = false; Pause(false); task.Cancel(true); InMenu = true; }
        public void SetVolume(float value) { Volume = Mathf.Clamp01(value); if (audioController) audioController.SetVolume(Volume); }
        public void SetTextScale(float value) { TextScale = Mathf.Clamp(value, 0.9f, 1.2f); }
        private void OnDisable() { Pause(false); if (audioController) audioController.Release(); if (audioSource) audioSource.Stop(); }
        private void OnApplicationFocus(bool focused) { if (!focused && AutoPauseOnFocusLoss && !InMenu) Pause(true); }
        private void OnApplicationPause(bool paused) { if (paused && AutoPauseOnFocusLoss && !InMenu) Pause(true); }
        private void OnApplicationQuit() { Pause(false); }
    }
}
