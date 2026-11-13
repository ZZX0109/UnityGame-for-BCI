using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClawMachine
{
    public enum ClawAudioCue { Move, Approach, Contact, Close, Lift, Transport, Release, Slip, Success, Failure }

    /// <summary>Original deterministic synthesis; no downloaded samples or external audio service.</summary>
    public sealed class ClawAudioController : MonoBehaviour
    {
        private GraspTaskController task;
        private IManipulatorDriver driver;
        private AudioSource cueSource, motorSource;
        private GameObject motorObject;
        private readonly Dictionary<ClawAudioCue, AudioClip> clips = new Dictionary<ClawAudioCue, AudioClip>();
        private float volume = 0.25f;
        private int slipSession = -1, slipAttempt = -1;
        public bool Initialized => task && cueSource && motorSource;

        public void Initialize(GraspTaskController owner, IManipulatorDriver manipulator, AudioSource source)
        {
            Release(); task = owner; driver = manipulator; cueSource = source;
            if (!task || driver == null || !cueSource) return;
            foreach (ClawAudioCue kind in Enum.GetValues(typeof(ClawAudioCue)))
            {
                float[] samples = BuildCueSamples(kind, 22050);
                var clip = AudioClip.Create("Claw original " + kind, samples.Length, 1, 22050, false);
                clip.SetData(samples, 0); clips.Add(kind, clip);
            }
            motorObject = new GameObject("Claw original motor layer");
            motorObject.transform.SetParent(transform, false);
            motorSource = motorObject.AddComponent<AudioSource>();
            motorSource.playOnAwake = false; motorSource.loop = true; motorSource.spatialBlend = 0;
            motorSource.clip = clips[ClawAudioCue.Move];
            cueSource.playOnAwake = false; cueSource.loop = false; cueSource.spatialBlend = 0;
            SetVolume(volume); task.Session.Changed += OnPhase;
        }

        public void SetVolume(float amount)
        {
            volume = float.IsNaN(amount) || float.IsInfinity(amount) ? 0 : Mathf.Clamp01(amount);
            if (cueSource) cueSource.volume = volume;
            if (motorSource) motorSource.volume = volume * 0.3f;
        }
        public void SetPaused(bool paused)
        {
            if (!Initialized) return;
            if (paused) { cueSource.Pause(); motorSource.Pause(); }
            else { cueSource.UnPause(); motorSource.UnPause(); }
        }
        private void Update()
        {
            if (!Initialized) return;
            bool aimingMotion = !task.Session.Paused && task.Session.State == GraspState.Aiming && driver.IsBusy;
            if (aimingMotion && !motorSource.isPlaying) motorSource.Play();
            else if (!aimingMotion && motorSource.isPlaying) motorSource.Stop();
            if (task.Session.Failure == GraspFailure.Slipped &&
                (slipSession != task.Session.SessionId || slipAttempt != task.Session.AttemptId))
            {
                slipSession = task.Session.SessionId; slipAttempt = task.Session.AttemptId; Play(ClawAudioCue.Slip);
            }
        }
        private void OnPhase(GraspState phase)
        {
            switch (phase.ToString())
            {
                case "Approaching": case "Lowering": Play(ClawAudioCue.Approach); break;
                case "ContactDescent": Play(ClawAudioCue.Contact); break;
                case "Closing": Play(ClawAudioCue.Close); break;
                case "Lifting": Play(ClawAudioCue.Lift); break;
                case "Transporting": Play(ClawAudioCue.Transport); break;
                case "Releasing": Play(ClawAudioCue.Release); break;
                case "Result": Play(task.Session.Awarded ? ClawAudioCue.Success : ClawAudioCue.Failure); break;
                case "Error": Play(ClawAudioCue.Failure); break;
            }
        }
        public void Play(ClawAudioCue kind)
        {
            if (cueSource && clips.TryGetValue(kind, out AudioClip clip)) cueSource.PlayOneShot(clip);
        }
        public void Release()
        {
            if (task) task.Session.Changed -= OnPhase;
            if (cueSource) cueSource.Stop();
            if (motorSource) motorSource.Stop();
            if (motorObject) Destroy(motorObject);
            foreach (AudioClip clip in clips.Values) if (clip) Destroy(clip);
            clips.Clear(); motorSource = null; motorObject = null; cueSource = null; task = null; driver = null;
            slipSession = slipAttempt = -1;
        }
        private void OnDisable() { Release(); }

        public static float[] BuildCueSamples(ClawAudioCue kind, int sampleRate)
        {
            if (sampleRate < 8000 || sampleRate > 96000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            float duration = kind == ClawAudioCue.Success ? 0.60f : kind == ClawAudioCue.Slip ? 0.38f : 0.22f;
            var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
            double phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate, progress = i / (float)(samples.Length - 1);
                float hz;
                switch (kind)
                {
                    case ClawAudioCue.Move: hz = 105; break;
                    case ClawAudioCue.Approach: hz = Mathf.Lerp(260, 145, progress); break;
                    case ClawAudioCue.Contact: hz = 185; break;
                    case ClawAudioCue.Close: hz = 480; break;
                    case ClawAudioCue.Lift: hz = Mathf.Lerp(170, 310, progress); break;
                    case ClawAudioCue.Transport: hz = 130; break;
                    case ClawAudioCue.Release: hz = 660; break;
                    case ClawAudioCue.Slip: hz = Mathf.Lerp(780, 95, progress); break;
                    case ClawAudioCue.Success: hz = progress < 0.34f ? 523.25f : progress < 0.67f ? 659.25f : 783.99f; break;
                    default: hz = progress < 0.5f ? 220 : 164.81f; break;
                }
                phase += 2 * Math.PI * hz / sampleRate;
                float signal = (float)Math.Sin(phase);
                float modulation = 1;
                if (kind == ClawAudioCue.Move) { signal += (float)Math.Sin(phase * 2) * 0.25f; modulation = 0.72f + Mathf.Sin(t * 2 * Mathf.PI * 18) * 0.2f; }
                if (kind == ClawAudioCue.Contact || kind == ClawAudioCue.Close) modulation = Mathf.Pow(1 - progress, 3);
                if (kind == ClawAudioCue.Transport) modulation = 0.65f + Mathf.Sin(t * 2 * Mathf.PI * 12) * 0.3f;
                float envelope = kind == ClawAudioCue.Move ? Mathf.Sin(progress * Mathf.PI) : Mathf.Min(1, t / 0.012f) * Mathf.Pow(1 - progress, 0.75f);
                samples[i] = Mathf.Clamp(signal * modulation * envelope * 0.28f, -0.8f, 0.8f);
            }
            return samples;
        }
    }
}
