using System.Collections.Generic;
using StreetCrossing.Core;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using UnityEngine;

namespace StreetCrossing.Audio
{
    /// <summary>Original procedural audio: soft steps, spatial engine beds, distant ambience and differentiated safety cues.</summary>
    public sealed class StreetAudioController : MonoBehaviour
    {
        private StreetGameManager game;
        private IntersectionController intersection;
        private AudioSource cues, ambience;
        private AudioClip step, engine, ambient, wait, walk, contact, warning, accident, success;
        private readonly List<AudioClip> clips = new List<AudioClip>();
        private readonly Dictionary<VehicleController, AudioSource> engines = new Dictionary<VehicleController, AudioSource>();
        private Vector3 previousPlayer;
        private float stepDistance;
        public void Initialize(StreetGameManager owner)
        {
            game = owner; intersection = owner.Intersection;
            cues = gameObject.AddComponent<AudioSource>(); cues.playOnAwake = false;
            step = Tone("Pavement step", .11f, 110, .15f, .45f);
            wait = Tone("Wait signal low double", .22f, 330, .17f, 0);
            walk = Tone("Walk signal bright chime", .28f, 660, .16f, 0);
            contact = Tone("Stationary contact soft tap", .11f, 190, .14f, .3f);
            warning = Tone("Near miss two-note caution", .3f, 520, .15f, .08f);
            accident = Tone("Moving accident muted stop", .22f, 85, .2f, .28f);
            success = Tone("Arrival warm chord", .65f, 440, .16f, 0);
            engine = Tone("Original engine loop", 1, 63, .13f, .12f, true);
            ambient = Tone("Original distant daytime traffic", 4, 42, .04f, .8f, true);
            ambience = gameObject.AddComponent<AudioSource>(); ambience.clip = ambient; ambience.loop = true; ambience.playOnAwake = false;
            previousPlayer = game.PlayerPosition; intersection.PhaseChanged += OnPhase; game.GameEvent += OnGameEvent;
        }
        private AudioClip Tone(string name, float length, float frequency, float amplitude, float noise, bool loop = false)
        {
            const int sampleRate = 22050; float[] samples = new float[Mathf.RoundToInt(sampleRate * length)];
            int seed = 17; foreach (char c in name) seed = unchecked(seed * 31 + c);
            System.Random rng = new System.Random(seed); float filtered = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                filtered = filtered * .92f + ((float)rng.NextDouble() * 2 - 1) * .08f;
                float envelope = loop ? 1 : Mathf.Sin(Mathf.PI * i / (samples.Length - 1));
                float fundamental = Mathf.Sin(t * 2 * Mathf.PI * frequency);
                float harmonic = name.Contains("Arrival") ? .5f * Mathf.Sin(t * 2 * Mathf.PI * frequency * 1.25f) + .4f * Mathf.Sin(t * 2 * Mathf.PI * frequency * 1.5f) : .2f * Mathf.Sin(t * 4 * Mathf.PI * frequency);
                samples[i] = (fundamental * (1 - noise) + harmonic + filtered * noise) * amplitude * envelope;
            }
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false); clip.SetData(samples, 0); clips.Add(clip); return clip;
        }
        private void OnPhase(TrafficPhase phase)
        {
            if (!game.IsPlaying || !isActiveAndEnabled) return;
            if (phase == TrafficPhase.Walk) cues.PlayOneShot(walk, StreetPreferences.Volume);
            else if (phase == TrafficPhase.VehicleGreen) cues.PlayOneShot(wait, StreetPreferences.Volume * .65f);
        }
        private void OnGameEvent(string name, string detail)
        {
            if (!isActiveAndEnabled) return;
            AudioClip clip = name == "SafeArrival" ? success : name == "Collision" ? accident : name == "StationaryContact" ? contact : name == "NearMiss" ? warning : null;
            if (clip != null) cues.PlayOneShot(clip, StreetPreferences.Volume);
        }
        private void Update()
        {
            if (game == null) return;
            bool playing = game.IsPlaying;
            ambience.volume = StreetPreferences.Volume * .18f;
            if (playing) { ambience.UnPause(); cues.UnPause(); if (!ambience.isPlaying) ambience.Play(); }
            else ambience.Pause();
            foreach (VehicleController vehicle in game.Traffic.Active)
            {
                if (!engines.TryGetValue(vehicle, out AudioSource source))
                {
                    source = vehicle.gameObject.AddComponent<AudioSource>(); source.clip = engine; source.loop = true; source.playOnAwake = false;
                    source.spatialBlend = 1; source.rolloffMode = AudioRolloffMode.Linear; source.minDistance = 3; source.maxDistance = 28; source.dopplerLevel = .15f; engines[vehicle] = source;
                }
                source.volume = StreetPreferences.Volume * (.08f + vehicle.Motion.Speed * .012f);
                source.pitch = .8f + vehicle.Motion.Speed * .055f;
                if (playing) { source.UnPause(); if (!source.isPlaying) source.Play(); } else source.Pause();
            }
            foreach (var pair in engines) if (pair.Key != null && !pair.Key.gameObject.activeSelf) pair.Value.Stop();
            Vector3 position = game.PlayerPosition;
            if (playing)
            {
                stepDistance += new Vector2(position.x - previousPlayer.x, position.z - previousPlayer.z).magnitude;
                if (stepDistance > .72f) { stepDistance = 0; cues.PlayOneShot(step, StreetPreferences.Volume * .35f); }
            }
            else stepDistance = 0;
            previousPlayer = position;
            if (game.State == StreetGameState.Paused && cues.isPlaying) cues.Pause();
        }
        private void OnDestroy()
        {
            if (intersection != null) intersection.PhaseChanged -= OnPhase;
            if (game != null) game.GameEvent -= OnGameEvent;
            foreach (AudioClip clip in clips) if (clip != null) Destroy(clip);
        }
    }
}
