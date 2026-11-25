using System;
using UnityEngine;

namespace StreetCrossing.Save
{
    /// <summary>
    /// Per-frame driver that samples the player camera into a <see cref="StreetResearchLog"/> and
    /// owns its lifetime. Attached by StreetBackgroundPopulation (the one per-scene component that
    /// already receives the manager and scenario), so no other subsystem had to change. Desktop
    /// builds have no eye tracker and no BCI hardware: the recorder states that explicitly once,
    /// then records only what was actually measured. Future hardware bootstraps call
    /// <see cref="RegisterEyeGazeSource"/> / <see cref="RegisterBciMetric"/> before the first sample.
    /// </summary>
    public sealed class StreetResearchRecorder : MonoBehaviour
    {
        private StreetResearchLog log;
        private Core.StreetGameManager manager;
        private float lastSample;
        /// <summary>Camera-orientation sampling period in simulation seconds (2 Hz default).</summary>
        public float sampleInterval = 0.5f;
        public StreetResearchLog Log => log;

        public static StreetResearchRecorder Attach(Transform host, Core.StreetGameManager manager, string scenarioId)
        {
            StreetResearchRecorder recorder = new GameObject("Research recorder").AddComponent<StreetResearchRecorder>();
            recorder.transform.SetParent(host, false); // dies with the environment on every scenario rebuild
            recorder.manager = manager;
            recorder.log = new StreetResearchLog(scenarioId);
            return recorder;
        }

        public void RegisterEyeGazeSource(string sourceId) => log?.RegisterEyeGazeSource(sourceId);
        public void RegisterBciMetric(string metric, Func<double> provider, string unit) => log?.RegisterBciMetric(metric, provider, unit);

        private void LateUpdate()
        {
            if (log == null || manager == null || !manager.IsPlaying) return;
            if (!log.HasBciMetrics) log.MarkBciUnavailable(manager.WorldTime, "desktop build without BCI hardware; metrics recorded only when actually measured");
            log.RecordEyeGazeAvailability(manager.WorldTime);
            if (manager.Player == null || manager.Player.View == null) return;
            if (manager.WorldTime >= lastSample && manager.WorldTime - lastSample < sampleInterval) return;
            lastSample = manager.WorldTime;
            log.SetAttempt(manager.CourseIndex >= 0 ? manager.CourseIndex + 1 : manager.SelectedVariant + 1);
            Vector3 forward = manager.Player.View.transform.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            // Positive X euler pitches the camera up and forward.y = sin(pitch), so Asin recovers
            // exactly the Player.Pitch convention the observation pipeline (|pitch| <= 35°) uses.
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            log.RecordCameraOrientation(manager.WorldTime, yaw, pitch);
            log.RecordBci(manager.WorldTime);
        }

        private void OnDestroy() { log?.Dispose(); log = null; }
    }
}
