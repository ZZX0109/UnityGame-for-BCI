using StreetCrossing.Core;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using UnityEngine;

namespace StreetCrossing.Audio
{
    /// <summary>Original soft optional walk-start cue. No external recordings; visual signals remain authoritative.</summary>
    public sealed class StreetAudioController : MonoBehaviour
    {
        private StreetGameManager game;
        private AudioSource source;
        private AudioClip cue;
        public void Initialize(StreetGameManager owner)
        {
            game = owner; source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0;
            const int sampleRate = 22050; float[] samples = new float[3307];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate, envelope = Mathf.Sin(Mathf.PI * i / (samples.Length - 1));
                samples[i] = Mathf.Sin(t * 2 * Mathf.PI * 660) * envelope * 0.16f;
            }
            cue = AudioClip.Create("Original gentle walk cue", samples.Length, 1, sampleRate, false); cue.SetData(samples, 0);
            game.Intersection.PhaseChanged += OnPhase;
        }
        private void OnPhase(TrafficPhase phase) { if (phase == TrafficPhase.Walk && game.IsPlaying) source.PlayOneShot(cue, StreetPreferences.Volume); }
        private void Update() { if (game != null && !game.IsPlaying && source.isPlaying) source.Stop(); }
        private void OnDestroy() { if (game != null && game.Intersection != null) game.Intersection.PhaseChanged -= OnPhase; if (cue != null) Destroy(cue); }
    }
}
