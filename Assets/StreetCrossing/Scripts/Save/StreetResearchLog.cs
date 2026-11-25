using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace StreetCrossing.Save
{
    /// <summary>
    /// One research telemetry row. <c>signal</c> separates the recorded channels; <c>measured</c>
    /// is the provenance contract of the whole file: <c>true</c> rows carry real sensor values in
    /// the numeric fields, <c>false</c> rows are explicit absence records (instrument not present)
    /// whose numeric fields are zero and MUST NOT be analysed. Camera orientation is what the
    /// renderer's camera actually faced; eye gaze would require an eye tracker, which desktop
    /// builds do not have - conflating the two is the classic head-as-gaze error this file exists
    /// to prevent.
    /// </summary>
    [Serializable]
    internal sealed class ResearchRow
    {
        public string utc, scenarioId, sessionId, signal, source, note, unit;
        public bool measured;
        public int attempt;
        public float simulationSeconds, yawDeg, pitchDeg, value;
    }

    /// <summary>
    /// Research metadata log (JSONL, one row per line) that keeps the camera-orientation vs eye-gaze
    /// distinction auditable. Camera orientation rows are written from the actual player camera.
    /// Eye-gaze and BCI channels are recorded <b>only</b> when a measuring source has been
    /// registered (<see cref="RegisterEyeGazeSource"/> / <see cref="RegisterBciMetric"/>); without
    /// hardware the log records a single explicit absence row and then refuses to fabricate values.
    /// Desktop BCI numbers can therefore never appear unless they were actually measured.
    /// </summary>
    public sealed class StreetResearchLog : IDisposable
    {
        private StreamWriter writer;
        private readonly string scenarioId;
        private readonly string sessionId = Guid.NewGuid().ToString("N");
        private bool gazeAvailabilityWritten, bciAvailabilityWritten;
        private int attempt = 1;
        public string Path { get; private set; }
        public string Error { get; private set; }
        public string SessionId => sessionId;
        public bool EyeGazeRegistered { get; private set; }
        public bool HasBciMetrics { get; private set; }

        public StreetResearchLog(string scenarioId) : this(scenarioId, null) { }

        /// <param name="absoluteDirectory">Test seam: null writes to persistentDataPath/StreetCrossing/Research.</param>
        public StreetResearchLog(string scenarioId, string absoluteDirectory)
        {
            this.scenarioId = scenarioId;
            try
            {
                string directory = absoluteDirectory ?? System.IO.Path.Combine(Application.persistentDataPath, "StreetCrossing", "Research");
                Directory.CreateDirectory(directory);
                Path = System.IO.Path.Combine(directory, scenarioId + "-research-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + sessionId.Substring(0, 8) + ".jsonl");
                writer = new StreamWriter(Path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Error = e.Message;
                Debug.LogWarning("Street research log unavailable: " + Error);
            }
        }

        public void SetAttempt(int value) => attempt = value;

        /// <summary>Camera orientation actually measured from the renderer's camera transform.</summary>
        public void RecordCameraOrientation(float simulationSeconds, float yawDeg, float pitchDeg)
            => Write("camera-orientation", "player-camera", true, simulationSeconds, yawDeg, pitchDeg, 0f, "deg", "renderer camera yaw/pitch; NOT eye gaze");

        /// <summary>
        /// Writes the eye-gaze channel availability exactly once. Without a registered eye tracker
        /// this is an explicit <c>measured=false</c> absence row so downstream analysis can filter
        /// the channel instead of mistaking head/camera orientation for gaze.
        /// </summary>
        public void RecordEyeGazeAvailability(float simulationSeconds)
        {
            if (gazeAvailabilityWritten) return;
            gazeAvailabilityWritten = true;
            if (EyeGazeRegistered) Write("eye-gaze", GazeSource, true, simulationSeconds, 0f, 0f, 0f, "deg", "eye tracker registered; per-sample rows follow");
            else Write("eye-gaze", "none", false, simulationSeconds, 0f, 0f, 0f, "", "desktop build has no eye tracker; camera-orientation rows are head-only evidence");
        }

        /// <summary>Per-sample eye gaze; silently ignored unless a source was registered.</summary>
        public bool RecordEyeGaze(float simulationSeconds, float yawDeg, float pitchDeg)
        {
            if (!EyeGazeRegistered) return false;
            Write("eye-gaze", GazeSource, true, simulationSeconds, yawDeg, pitchDeg, 0f, "deg", "");
            return true;
        }

        /// <summary>Registers (or clears, with null) the future eye-tracking hardware source.</summary>
        public void RegisterEyeGazeSource(string sourceId)
        {
            EyeGazeRegistered = !string.IsNullOrEmpty(sourceId);
            if (EyeGazeRegistered) GazeSource = sourceId;
        }

        /// <summary>
        /// BCI absence documentation. No numeric value is accepted here on purpose: an unmeasured
        /// channel must never carry data, only this explicit availability row.
        /// </summary>
        public void MarkBciUnavailable(float simulationSeconds, string reason)
        {
            if (bciAvailabilityWritten || HasBciMetrics) return;
            bciAvailabilityWritten = true;
            Write("bci", "none", false, simulationSeconds, 0f, 0f, 0f, "", reason);
        }

        /// <summary>Registers a live BCI metric provider; without one no bci value rows can exist.</summary>
        public void RegisterBciMetric(string metric, Func<double> provider, string unit)
        {
            if (string.IsNullOrEmpty(metric) || provider == null) return;
            bciMetrics.Add(new BciMetric { name = metric, provider = provider, unit = unit });
            HasBciMetrics = true;
        }

        /// <summary>Samples every registered BCI provider; returns the number of rows written.</summary>
        public int RecordBci(float simulationSeconds)
        {
            if (!HasBciMetrics) return 0;
            int written = 0;
            foreach (BciMetric metric in bciMetrics)
            {
                double value = metric.provider();
                if (double.IsNaN(value) || double.IsInfinity(value)) continue; // a dead stream must not fabricate a number
                Write("bci", metric.name, true, simulationSeconds, 0f, 0f, (float)value, metric.unit, "");
                written++;
            }
            return written;
        }

        /// <summary>JSONL integrity probe for callers; false after any IO failure.</summary>
        public bool Healthy => writer != null;

        private string GazeSource = "none";
        private sealed class BciMetric { public string name, unit; public Func<double> provider; }
        private readonly List<BciMetric> bciMetrics = new List<BciMetric>();

        private void Write(string signal, string source, bool measured, float simulationSeconds,
            float yawDeg, float pitchDeg, float value, string unit, string note)
        {
            if (writer == null) return;
            try
            {
                writer.WriteLine(JsonUtility.ToJson(new ResearchRow
                {
                    utc = DateTime.UtcNow.ToString("O"),
                    scenarioId = scenarioId,
                    sessionId = sessionId,
                    signal = signal,
                    source = source,
                    note = note,
                    unit = unit ?? "",
                    measured = measured,
                    attempt = attempt,
                    simulationSeconds = simulationSeconds,
                    yawDeg = measured ? yawDeg : 0f,
                    pitchDeg = measured ? pitchDeg : 0f,
                    value = measured ? value : 0f
                }));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Error = e.Message;
                writer.Dispose(); writer = null;
                Debug.LogWarning("Street research log stopped: " + Error);
            }
        }

        public void Dispose() { writer?.Dispose(); writer = null; }
    }
}
