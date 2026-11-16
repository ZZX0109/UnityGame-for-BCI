using System.Collections.Generic;

namespace StreetCrossing.Core
{
    public enum StreetNoticeKind { Inspection = 10, ObservationRefresh = 20, Occlusion = 30, Clearance = 40, ClearanceWatchdog = 50, Violation = 100, Safety = 110, Result = 120 }

    /// <summary>Simulation-time expiry: opening a pause menu never consumes a message's lifetime.</summary>
    public sealed class StreetNoticeBoard
    {
        private struct Entry { public string Text; public float Until; }
        private readonly Dictionary<StreetNoticeKind, Entry> entries = new Dictionary<StreetNoticeKind, Entry>();
        public void Set(StreetNoticeKind kind, string text, float until = float.PositiveInfinity)
        {
            if (string.IsNullOrEmpty(text)) { Clear(kind); return; }
            entries[kind] = new Entry { Text = text, Until = until };
        }
        public void Clear(StreetNoticeKind kind) => entries.Remove(kind);
        public void Reset() => entries.Clear();
        public string Resolve(float simulationTime)
        {
            int priority = -1;
            string text = "";
            foreach (KeyValuePair<StreetNoticeKind, Entry> pair in entries)
                if (pair.Value.Until > simulationTime && (int)pair.Key > priority)
                { priority = (int)pair.Key; text = pair.Value.Text; }
            return text;
        }
    }
}
