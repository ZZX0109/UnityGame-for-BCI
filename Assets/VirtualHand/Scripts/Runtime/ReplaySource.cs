using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

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
    public class ReplaySource : MonoBehaviour
    {
        public ReplayData Data { get; private set; } = new ReplayData();
        public event Action<HandMotionFrame> FrameReady;
        public bool IsPlaying { get; private set; }
        public double Position { get; private set; }
        public double Duration => Data?.Duration ?? 0;

        int _cursor;
        bool _loop;

        public static ReplayData LoadFile(string path, ReplayParseMode mode = ReplayParseMode.Tolerant)
        {
            var data = new ReplayData { filePath = path };
            string text;
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                var req = UnityWebRequest.Get(path);
                var op = req.SendWebRequest();
                while (!op.isDone) { }
#if UNITY_2020_1_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success) throw new IOException("web " + req.error);
#else
                if (req.isNetworkError || req.isHttpError) throw new IOException("web " + req.error);
#endif
                text = req.downloadHandler.text;
            }
            else text = File.ReadAllText(path);

            int lineNo = 0;
            using (var sr = new StringReader(text))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (TryParseLine(line, out var f, out var err, out bool partial)) data.frames.Add(f);
                    else
                    {
                        data.rejectedLines++;
                        if (data.errors.Count < 20) data.errors.Add($"line {lineNo}: {err}");
                        if (mode == ReplayParseMode.Strict) throw new IOException($"{path} line {lineNo}: {err}");
                    }
                }
            }
            data.frames.Sort((a, b) => a.time.CompareTo(b.time));
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
            Data = data; _loop = loop; _cursor = 0; Position = 0; IsPlaying = false;
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
            if (_cursor >= Data.frames.Count)
            {
                if (_loop) { _cursor = 0; Position = 0; }
                else IsPlaying = false;
            }
        }
    }
}
