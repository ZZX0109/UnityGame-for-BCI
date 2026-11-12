using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace StreetCrossing.Save
{
    public sealed class StreetEventLog : IDisposable
    {
        [Serializable] private sealed class Row
        {
            public string utc, scenarioId, sessionId, eventName, phase, detail;
            public int attempt;
            public float simulationSeconds, x, z;
        }
        private StreamWriter writer;
        private readonly string scenarioId;
        private readonly string sessionId;
        public string Path { get; private set; }
        public string Error { get; private set; }
        public StreetEventLog(string scenario)
        {
            scenarioId = scenario; sessionId = Guid.NewGuid().ToString("N");
            try
            {
                string directory = System.IO.Path.Combine(Application.persistentDataPath, "StreetCrossing", "Logs");
                Directory.CreateDirectory(directory);
                Path = System.IO.Path.Combine(directory, scenario + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + sessionId.Substring(0, 8) + ".jsonl");
                writer = new StreamWriter(Path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Error = e.Message; Debug.LogWarning("Street event log unavailable: " + Error); }
        }
        public void Write(string name, string detail, float time, int attempt, string phase, Vector3 position)
        {
            if (writer == null) return;
            try { writer.WriteLine(JsonUtility.ToJson(new Row { utc = DateTime.UtcNow.ToString("O"), scenarioId = scenarioId, sessionId = sessionId, eventName = name, phase = phase, detail = detail, attempt = attempt, simulationSeconds = time, x = position.x, z = position.z })); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Error = e.Message; writer.Dispose(); writer = null; Debug.LogWarning("Street event log stopped: " + Error); }
        }
        public void Dispose() { writer?.Dispose(); writer = null; }
    }
}
