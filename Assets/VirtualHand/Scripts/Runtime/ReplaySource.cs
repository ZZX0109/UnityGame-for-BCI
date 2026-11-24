using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    public enum ReplayParseMode { Strict = 0, Tolerant = 1 }

    /// <summary>Parsed result for one JSONL replay file.</summary>
    public sealed class ReplayData
    {
        public List<HandMotionFrame> frames = new List<HandMotionFrame>();
        public int rejectedLines;
        public List<string> errors = new List<string>();
        public string filePath;
        public double Duration => frames.Count > 0 ? frames[frames.Count - 1].time + frames[frames.Count - 1].duration : 0;

        // ---- protocol audit (spec 3.3) ----

        /// <summary>Frames whose time went backwards in the file.</summary>
        public int outOfOrderFrames;

        /// <summary>Number of time gaps larger than <see cref="gapThresholdSeconds"/>.</summary>
        public int gapCount;

        /// <summary>Largest gap between consecutive frames, in seconds.</summary>
        public double largestGapSeconds;

        /// <summary>Gaps above this are reported; they are not silently interpolated.</summary>
        public double gapThresholdSeconds = 0.5;

        /// <summary>SHA-256 of the file bytes, so a switched source is detectable.</summary>
        public string sourceHash = "";

        /// <summary>Bit i set means version i+1 appeared. Bit 0 = v1.</summary>
        public int declaredVersions;

        /// <summary>v1 is the only supported contract; anything else is refused.</summary>
        public bool VersionCompatible => declaredVersions == 0 || (declaredVersions & ~1) == 0;

        /// <summary>True when the file yielded frames that may be played back.</summary>
        public bool IsUsable => frames.Count > 0 && VersionCompatible;

        /// <summary>Human-readable summary for the host / test report.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("frames=").Append(frames.Count);
            sb.Append(" duration=").Append(Duration.ToString("F3", CultureInfo.InvariantCulture)).Append("s");
            sb.Append(" rejected=").Append(rejectedLines);
            sb.Append(" outOfOrder=").Append(outOfOrderFrames);
            sb.Append(" gaps=").Append(gapCount);
            if (gapCount > 0)
                sb.Append(" maxGap=").Append(largestGapSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append("s");
            sb.Append(" versions=").Append(declaredVersions == 0 ? "none" : declaredVersions.ToString());
            if (!VersionCompatible) sb.Append(" UNSUPPORTED_VERSION");
            if (frames.Count == 0) sb.Append(" EMPTY");
            return sb.ToString();
        }

        public static bool TryParseFrame(string json, out HandMotionFrame f, out string error)
        {
            f = null; error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty line"; return false; }
            var req = new HashSet<string>();
            try
            {
                // Lightweight parse to avoid a JSON dependency: ordered field scan.
                f = new HandMotionFrame();
                int i = 0, n = json.Length;
                while (i < n)
                {
                    int keyStart = json.IndexOf('"', i);
                    if (keyStart < 0) break;
                    int keyEnd = json.IndexOf('"', keyStart + 1);
                    if (keyEnd < 0) { error = "unterminated key"; return false; }
                    string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
                    int colon = json.IndexOf(':', keyEnd + 1);
                    if (colon < 0) { error = "missing ':' after " + key; return false; }
                    // find value end
                    int vStart = colon + 1;
                    while (vStart < n && char.IsWhiteSpace(json[vStart])) vStart++;
                    if (vStart >= n) { error = "missing value for " + key; return false; }
                    string value;
                    if (json[vStart] == '"')
                    {
                        int vEnd = json.IndexOf('"', vStart + 1);
                        if (vEnd < 0) { error = "unterminated string for " + key; return false; }
                        value = json.Substring(vStart + 1, vEnd - vStart - 1);
                        i = vEnd + 1;
                    }
                    else if (json[vStart] == '[')
                    {
                        int vEnd = json.IndexOf(']', vStart);
                        if (vEnd < 0) { error = "unterminated array for " + key; return false; }
                        value = json.Substring(vStart, vEnd - vStart + 1);
                        i = vEnd + 1;
                    }
                    else
                    {
                        int vEnd = vStart;
                        while (vEnd < n && ",}".IndexOf(json[vEnd]) < 0) vEnd++;
                        value = json.Substring(vStart, vEnd - vStart).Trim();
                        i = vEnd;
                    }
                    switch (key)
                    {
                        case "version": f.version = ParseInt(value); break;
                        case "time": f.time = ParseFloat(value); break;
                        case "duration": f.duration = ParseFloat(value); break;
                        case "intent": f.intent = value.ToLowerInvariant(); break;
                        case "confidence": f.confidence = (float)ParseFloat(value); break;
                        case "source": f.source = value.ToLowerInvariant(); break;
                        case "subject": f.subject = value; break;
                        case "run": f.run = value; break;
                        case "leftJoints": f.leftJoints = ParseArray(value); break;
                        case "rightJoints": f.rightJoints = ParseArray(value); break;
                        default: break; // unknown keys tolerated (forward compat)
                    }
                }
                // validation
                if (double.IsNaN(f.time) || double.IsInfinity(f.time)) { error = "nonfinite time"; return false; }
                if (double.IsNaN(f.duration) || double.IsInfinity(f.duration) || f.duration <= 0) { error = "duration must be >0"; return false; }
                if (f.confidence < 0f || f.confidence > 1f || float.IsNaN(f.confidence)) { error = "confidence out of [0,1]"; return false; }
                string[] validIntent = { "rest", "left", "right", "both" };
                if (Array.IndexOf(validIntent, f.intent) < 0) { error = "unknown intent " + f.intent; return false; }
                string[] validSource = { "manual", "glove", "labels", "prediction" };
                if (Array.IndexOf(validSource, f.source) < 0) { error = "unknown source " + f.source; return false; }
                if (!CheckArray(f.leftJoints, "leftJoints", ref error)) return false;
                if (!CheckArray(f.rightJoints, "rightJoints", ref error)) return false;
                return true;
            }
            catch (Exception e) { error = "parse exception: " + e.Message; f = null; return false; }
        }

        static bool CheckArray(float[] a, string name, ref string error)
        {
            if (a == null) return true;
            if (a.Length != 20) { error = name + " must have 20 values, got " + a.Length; return false; }
            foreach (var v in a)
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > 1f)
                { error = name + " value outside [0,1]"; return false; }
            return true;
        }

        static int ParseInt(string s) => (int)ParseFloat(s);
        static double ParseFloat(string s)
        {
            if (!double.TryParse(s.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException("bad number: " + s);
            return d;
        }
        static float[] ParseArray(string s)
        {
            s = s.Trim().Trim('[', ']'); // value passed includes brackets
            s = s.Trim('[', ']');
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<float>();
            var parts = s.Split(',');
            var r = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = (float)ParseFloat(parts[i]);
            return r;
        }
    }

    /// <summary>
    /// Streams HandMotionFrames from a UTF-8 JSONL file (one JSON object per line).
    /// Clock is owned by ReplayController so pause affects both playback and tasks.
    /// </summary>
    public class ReplaySource : MonoBehaviour, IHandMotionSource
    {
        public ReplayData Data { get; private set; } = new ReplayData();
        public event Action<HandMotionFrame> FrameReady;

        /// <summary>
        /// Raised whenever <see cref="Open"/> swaps in a different data set. The host
        /// uses it to drop the previous source's target pose and any action events
        /// that were produced but never consumed.
        /// </summary>
        public event Action SourceChanged;

        public bool IsPlaying { get; private set; }
        public double Position { get; private set; }
        public double Duration => Data?.Duration ?? 0;

        /// <summary>Frames at or past the current position not yet delivered.</summary>
        public int PendingFrames => Data == null ? 0 : Mathf.Max(0, Data.frames.Count - _cursor);

        int _cursor;
        bool _loop;

        public static ReplayData LoadFile(string path, ReplayParseMode mode = ReplayParseMode.Tolerant)
        {
            // v1 contract: local Windows JSONL only. Remote URLs and the missing
            // UnityWebRequest module are out of scope; fail fast and visibly.
            if (string.IsNullOrWhiteSpace(path)) throw new IOException("回放路径为空");
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new IOException("v1 仅支持本地 JSONL 文件: " + path);
            if (!File.Exists(path)) throw new IOException("回放文件不存在: " + path);
            var data = new ReplayData { filePath = path };
            string text;
            try
            {
                text = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (IOException)
            {
                throw; // file is locked or unreadable: surface as-is
            }
            catch (Exception e)
            {
                throw new IOException("读取回放文件失败: " + e.Message);
            }

            int lineNo = 0;
            double previousTime = double.NegativeInfinity;
            using (var sr = new StringReader(text))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (TryParseLine(line, out var f, out var err, out bool partial))
                    {
                        // Time-order audit: records the problem instead of silently
                        // reordering, so a malformed file is visible in the report.
                        if (f.time < previousTime)
                            data.outOfOrderFrames++;
                        previousTime = f.time;

                        if (f.version >= 1 && f.version <= 31)
                            data.declaredVersions |= 1 << (f.version - 1);

                        data.frames.Add(f);
                    }
                    else
                    {
                        data.rejectedLines++;
                        if (data.errors.Count < 20) data.errors.Add($"line {lineNo}: {err}");
                        if (mode == ReplayParseMode.Strict) throw new IOException($"{path} line {lineNo}: {err}");
                    }
                }
            }

            data.frames.Sort((a, b) => a.time.CompareTo(b.time));

            // Gap audit: a long break means the stream stalled; playable, but the
            // host must be told so it can hold rather than interpolate across it.
            for (int i = 1; i < data.frames.Count; i++)
            {
                double gap = data.frames[i].time - (data.frames[i - 1].time + data.frames[i - 1].duration);
                if (gap > data.gapThresholdSeconds)
                {
                    data.gapCount++;
                    if (gap > data.largestGapSeconds) data.largestGapSeconds = gap;
                }
            }

            // Source fingerprint: identifies this exact file so a source switch is
            // detectable even when two files share a name.
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
                var sb = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                data.sourceHash = sb.ToString();
            }

            return data;
        }

        static bool TryParseLine(string line, out HandMotionFrame f, out string err, out bool partial)
        {
            partial = false;
            if (ReplayData.TryParseFrame(line, out f, out err)) return true;
            f = null; partial = false;
            return false;
        }

        public void Open(ReplayData data, bool loop = false)
        {
            // Switching source must not inherit anything from the previous stream:
            // the cursor, the clock and the pending frame backlog all reset, and the
            // host is told so it can clear the old target pose and un-consumed events.
            bool isSwitch = !ReferenceEquals(Data, data) || (Data != null && Data.frames.Count > 0);

            Data = data;
            _loop = loop;
            _cursor = 0;
            Position = 0;
            IsPlaying = false;

            if (isSwitch)
                SourceChanged?.Invoke();
        }

        /// <summary>
        /// Discards every buffered frame without delivering it. A host calls this
        /// after a source switch or a long stall so stale target poses and action
        /// events from the old stream can never be applied.
        /// </summary>
        public void ClearPending()
        {
            if (Data != null)
                _cursor = Data.frames.Count;
        }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;

        public void ResetPlayback()
        {
            Position = 0; _cursor = 0; IsPlaying = false;
        }

        /// <summary>Advance the clock; fires FrameReady for every frame whose time has passed.</summary>
        public void Tick(double dt)
        {
            if (!IsPlaying || Data == null) return;
            Position += dt;
            while (_cursor < Data.frames.Count && Data.frames[_cursor].time <= Position)
            {
                FrameReady?.Invoke(Data.frames[_cursor]);
                _cursor++;
            }
            double total = Duration;
            if (total <= 0 || Position >= total)
            {
                if (_loop && total > 0)
                {
                    Position -= Math.Floor(Position / total) * total;
                    _cursor = 0;
                    while (_cursor < Data.frames.Count && Data.frames[_cursor].time <= Position)
                    {
                        FrameReady?.Invoke(Data.frames[_cursor]);
                        _cursor++;
                    }
                }
                else
                {
                    Position = total;
                    IsPlaying = false;
                }
            }
        }
    }
}
