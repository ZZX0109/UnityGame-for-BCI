using UnityEngine;

namespace ClawMachine
{
    public sealed class ClawGameManager : MonoBehaviour
    {
        [SerializeField] private GraspTaskController task;
        [SerializeField] private ClawInputController keyboard;
        [SerializeField] private CraneCameraController cameras;
        [SerializeField] private AudioSource audioSource;
        private IClawInputSource input;
        private float savedTimeScale = 1;
        private AudioClip cue;
        public GraspTaskController Task => task;
        public CraneCameraController Cameras => cameras;
        public bool InMenu { get; private set; } = true;
        public bool SettingsOpen { get; private set; }
        public bool ResetConfirmation { get; private set; }
        public bool IsPaused { get; private set; }
        public float Volume { get; private set; } = 0.25f;
        public float TextScale { get; private set; } = 1;
        private void Awake()
        {
            input = keyboard;
            if (!task || !keyboard || !cameras || !cameras.IsConfigured) { enabled = false; Debug.LogError("Claw scene references are incomplete.", this); return; }
            task.Session.Changed += OnPhase;
            cue = AudioClip.Create("Original mechanical confirmation", 2205, 1, 22050, false);
            var data = new float[2205];
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Sin(i * 2 * Mathf.PI * 360 / 22050) * (1 - i / (float)data.Length) * 0.12f;
            cue.SetData(data, 0);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void SetInputSource(IClawInputSource source) { input = source ?? keyboard; }
        private void Update()
        {
            if (!task || input == null) return;
            ClawInputFrame frame = input.Read();
            if (frame.Pause)
            {
                if (SettingsOpen) CloseSettings();
                else if (ResetConfirmation) DeclineReset();
                else if (!InMenu) Pause(!IsPaused);
            }
            if (frame.View != 0) cameras.Select(frame.View);
            cameras.Zoom(frame.Zoom);
            if (frame.Reset && !InMenu && !ResetConfirmation) RequestReset();
            task.SetAim(!InMenu && !IsPaused && !ResetConfirmation && !SettingsOpen ? frame.Move : Vector2.zero);
            if (frame.Grab && !InMenu && !IsPaused && !SettingsOpen && !ResetConfirmation)
            {
                if (task.Session.State == GraspState.Result) task.Retry();
                else if (task.Session.State == GraspState.End) StartGame();
                else task.StartGrab();
            }
        }
        public void StartGame() { Pause(false); SettingsOpen = ResetConfirmation = false; InMenu = !task.StartChallenge(); }
        public void Grab() { if (!IsPaused && !InMenu && !SettingsOpen && !ResetConfirmation) task.StartGrab(); }
        public void Retry() { if (!IsPaused && !InMenu) task.Retry(); }
        public void Pause(bool value)
        {
            if (value == IsPaused) return;
            if (value) { savedTimeScale = Time.timeScale; Time.timeScale = 0; }
            else Time.timeScale = savedTimeScale;
            IsPaused = value; if (task) task.SetPaused(value);
        }
        public void OpenSettings() { SettingsOpen = true; if (!InMenu) Pause(true); }
        public void CloseSettings() { SettingsOpen = false; }
        public void RequestReset() { ResetConfirmation = true; Pause(true); }
        public void DeclineReset() { ResetConfirmation = false; }
        public void ConfirmReset() { ResetConfirmation = false; StartGame(); }
        public void ReturnToMenu() { SettingsOpen = ResetConfirmation = false; Pause(false); task.Cancel(true); InMenu = true; }
        public void SetVolume(float value) { Volume = Mathf.Clamp01(value); }
        public void SetTextScale(float value) { TextScale = Mathf.Clamp(value, 0.9f, 1.2f); }
        private void OnPhase(GraspState phase)
        {
            if (audioSource && cue && phase != GraspState.Resetting && phase != GraspState.Idle) audioSource.PlayOneShot(cue, Volume);
        }
        private void OnDisable()
        {
            Pause(false); if (task) task.Session.Changed -= OnPhase;
            if (audioSource) audioSource.Stop(); if (cue) Destroy(cue);
        }
        private void OnApplicationQuit() { Pause(false); }
    }
}
