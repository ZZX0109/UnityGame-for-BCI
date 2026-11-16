using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace BCI.RuntimeValidation
{
    public interface IGameRuntimeAuditAdapter
    {
        string GameId { get; }
        string SnapshotJson { get; }
        IEnumerator Prepare();
        void TickActive();
        IEnumerator RebuildAndRestart(int iteration);
        void Restore();
    }

    /// <summary>Explicit command-line-only Player audit. No object is created in ordinary play.</summary>
    public sealed class GameRuntimeAudit : MonoBehaviour
    {
        [Serializable] private sealed class ResolutionSample
        {
            public int requestedWidth, requestedHeight, actualWidth, actualHeight;
            public int applicationAttempts;
            public string status, file, sha256;
        }
        [Serializable] private sealed class MemorySample
        {
            public int iteration, gameObjects, behaviours, materials, meshes, textures;
            public long unityAllocatedBytes, unityReservedBytes, monoUsedBytes, managedBytes;
            public string adapterSnapshotJson;
        }
        [Serializable] private sealed class Report
        {
            public int schemaVersion = 1;
            public string game, status = "running", startedUtc, finishedUtc, unityVersion, renderer, graphicsApi, processor;
            public string requestedQuality = "High", actualQuality, adapterSnapshotJson, error;
            public string measurement = "Standalone Player, real backbuffer captures; unscaled frame deltas include all natural restarts and CSV instrumentation.";
            public string memoryConclusion = "Recorded only; compare equal scenario groups. No leak PASS is inferred from these samples.";
            public string humanGameplayAcceptance = "not_run";
            public bool editor, mainThreadRecorderAvailable, gcRecorderAvailable, frameTimingFeatureEnabled, performancePassed, allResolutionsMatched;
            public int originalVSync, originalTargetFrameRate, benchmarkVSync, benchmarkTargetFrameRate;
            public int requestedSeconds, benchmarkWidth, benchmarkHeight, frames;
            public double warmupSeconds = 10, measuredSeconds, averageFps, meanFrameMs, p95FrameMs, p99FrameMs, maximumFrameMs;
            public double averageMainThreadMs, maximumMainThreadMs;
            public double averageGpuFrameMs, maximumGpuFrameMs;
            public int gpuTimingSamples;
            public string gpuFrameTimingStatus = "not_supported_or_unavailable";
            public long recordedGcAllocatedBytes;
            public ResolutionSample[] resolutions;
            public MemorySample[] memorySamples;
        }

        private IGameRuntimeAuditAdapter adapter;
        private string output;
        private int seconds;
        private int resolutionAttempts;
        private bool ticking, completed, restored;
        private Exception fatal;
        private Report report;
        private readonly List<ResolutionSample> resolutions = new List<ResolutionSample>();
        private readonly List<MemorySample> memory = new List<MemorySample>();
        private int originalQuality, originalVSync, originalTarget, originalWidth, originalHeight;
        private FullScreenMode originalFullScreen;
        private bool originalRunInBackground;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static bool TryStart(IGameRuntimeAuditAdapter adapter)
        {
            string[] args = Environment.GetCommandLineArgs();
            int key = Array.IndexOf(args, "--bci-audit-output");
            if (key < 0) return false;
            try
            {
                if (adapter == null) throw new ArgumentNullException(nameof(adapter));
                if (key + 1 >= args.Length || !Path.IsPathRooted(args[key + 1]))
                    throw new ArgumentException("--bci-audit-output requires an absolute fresh directory.");
                string folder = Path.GetFullPath(args[key + 1]);
                if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length != 0)
                    throw new ArgumentException("Audit output must be empty; existing evidence is never overwritten.");
                int duration = 600;
                int durationKey = Array.IndexOf(args, "--bci-audit-seconds");
                if (durationKey >= 0 && (durationKey + 1 >= args.Length ||
                    !int.TryParse(args[durationKey + 1], NumberStyles.None, Invariant, out duration) || duration < 1 || duration > 3600))
                    throw new ArgumentException("Audit seconds must be an integer from 1 to 3600.");
                Directory.CreateDirectory(folder);
                var root = new GameObject("Explicit Standalone Runtime Audit");
                DontDestroyOnLoad(root);
                var runner = root.AddComponent<GameRuntimeAudit>();
                runner.adapter = adapter; runner.output = folder; runner.seconds = duration;
                return true;
            }
            catch (Exception error)
            {
                Debug.LogError("Runtime audit refused: " + error);
                Application.Quit(1);
                return false;
            }
        }

        private IEnumerator Start()
        {
            originalQuality = QualitySettings.GetQualityLevel();
            originalVSync = QualitySettings.vSyncCount; originalTarget = Application.targetFrameRate;
            originalWidth = Screen.width; originalHeight = Screen.height; originalFullScreen = Screen.fullScreenMode;
            originalRunInBackground = Application.runInBackground;
            report = new Report
            {
                game = adapter.GameId, startedUtc = DateTime.UtcNow.ToString("o"), requestedSeconds = seconds,
                unityVersion = Application.unityVersion, renderer = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(), processor = SystemInfo.processorType,
                editor = Application.isEditor, originalVSync = originalVSync, originalTargetFrameRate = originalTarget
            };
            WriteReport();
            var stack = new Stack<IEnumerator>();
            stack.Push(Workflow());
            // Unity normally stops a throwing coroutine before it can write evidence.
            // Drive nested enumerators explicitly so every exception produces a failed report.
            while (stack.Count > 0 && fatal == null)
            {
                object next = null;
                bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception error) { fatal = error; }
                if (fatal != null) break;
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            ticking = false;
            while (stack.Count > 0) { try { (stack.Pop() as IDisposable)?.Dispose(); } catch { } }
            if (fatal != null) { report.error = fatal.ToString(); report.status = "failed"; Debug.LogError(report.error); }
            else report.status = report.performancePassed && report.allResolutionsMatched ? "passed" : "completed_with_failed_checks";
            Finish();
            Application.Quit(fatal == null && report.status == "passed" ? 0 : 2);
        }

        private void Update()
        {
            if (!ticking || fatal != null) return;
            try { adapter.TickActive(); }
            catch (Exception error) { fatal = error; }
        }

        private IEnumerator Workflow()
        {
            if (Application.isEditor) throw new InvalidOperationException("Runtime audit evidence requires a standalone Player.");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("A real graphics device is required.");
            Application.runInBackground = true;
            int high = Array.FindIndex(QualitySettings.names, n => string.Equals(n, "High", StringComparison.OrdinalIgnoreCase));
            if (high < 0) throw new InvalidOperationException("This project has no High quality preset.");
            QualitySettings.SetQualityLevel(high, true);
            report.actualQuality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            yield return null;
            yield return adapter.Prepare();
            ticking = true;
            int[,] sizes = { {1280, 720}, {1920, 1080}, {2560, 1440}, {3840, 2160} };
            for (int i = 0; i < sizes.GetLength(0); i++)
            {
                int width = sizes[i, 0], height = sizes[i, 1];
                yield return SetResolution(width, height);
                yield return new WaitForEndOfFrame();
                Texture2D image = ScreenCapture.CaptureScreenshotAsTexture();
                if (image == null) throw new InvalidOperationException("ScreenCapture did not produce real pixels.");
                var item = new ResolutionSample
                {
                    requestedWidth = width, requestedHeight = height, actualWidth = image.width, actualHeight = image.height,
                    applicationAttempts = resolutionAttempts,
                    status = image.width == width && image.height == height ? "matched" : "unsupported_actual_size_mismatch",
                    file = "resolution-" + width + "x" + height + ".png"
                };
                byte[] png = image.EncodeToPNG(); Destroy(image);
                File.WriteAllBytes(Path.Combine(output, item.file), png);
                using (SHA256 hash = SHA256.Create()) item.sha256 = BitConverter.ToString(hash.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
                resolutions.Add(item); WriteReport();
            }
            ticking = false;
            yield return SetResolution(1920, 1080);
            yield return SettleAndSample(-1);
            for (int iteration = 0; iteration < 20; iteration++)
            {
                yield return adapter.RebuildAndRestart(iteration);
                yield return SettleAndSample(iteration);
            }
            yield return adapter.Prepare();
            ticking = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            report.benchmarkVSync = QualitySettings.vSyncCount; report.benchmarkTargetFrameRate = Application.targetFrameRate;
            report.benchmarkWidth = Screen.width; report.benchmarkHeight = Screen.height;
            float warmupUntil = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < warmupUntil) yield return null;
            yield return Benchmark();
            report.allResolutionsMatched = resolutions.Count == 4 && resolutions.TrueForAll(r => r.status == "matched");
        }

        private IEnumerator SetResolution(int width, int height)
        {
            // The game's startup fullscreen request can still be pending in the
            // native window system. Finish that transition before sizing, retry
            // a bounded number of times, and still verify the actual PNG pixels.
            Screen.fullScreenMode = FullScreenMode.Windowed;
            yield return new WaitForEndOfFrame();
            resolutionAttempts = 0;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                resolutionAttempts++;
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                float deadline = Time.realtimeSinceStartup + 2f;
                float stableSince = -1f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return new WaitForEndOfFrame();
                    if (Screen.width == width && Screen.height == height && Screen.fullScreenMode == FullScreenMode.Windowed)
                    {
                        if (stableSince < 0) stableSince = Time.realtimeSinceStartup;
                        if (Time.realtimeSinceStartup - stableSince >= .25f) yield break;
                    }
                    else stableSince = -1f;
                }
            }
        }

        private IEnumerator SettleAndSample(int iteration)
        {
            yield return null;
            yield return null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            memory.Add(new MemorySample
            {
                iteration = iteration,
                gameObjects = Resources.FindObjectsOfTypeAll<GameObject>().Length,
                behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Length,
                materials = Resources.FindObjectsOfTypeAll<Material>().Length,
                meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                textures = Resources.FindObjectsOfTypeAll<Texture>().Length,
                unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong(),
                unityReservedBytes = Profiler.GetTotalReservedMemoryLong(),
                monoUsedBytes = Profiler.GetMonoUsedSizeLong(), managedBytes = GC.GetTotalMemory(false),
                adapterSnapshotJson = adapter.SnapshotJson
            });
            WriteReport();
        }

        private IEnumerator Benchmark()
        {
            var frames = new List<float>(seconds * 500);
            double mainThreadTotal = 0, mainThreadMax = 0;
            double gpuTotal = 0, gpuMax = 0;
            int gpuSamples = 0;
            var gpuTiming = new FrameTiming[1];
            long gcTotal = 0;
            using (ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1))
            using (ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            using (var writer = new StreamWriter(Path.Combine(output, "frames.csv"), false))
            {
                report.mainThreadRecorderAvailable = mainThread.Valid; report.gcRecorderAvailable = gc.Valid;
                report.frameTimingFeatureEnabled = FrameTimingManager.IsFeatureEnabled();
                writer.WriteLine("frame,elapsed_seconds,unscaled_frame_ms,main_thread_ns,gc_allocated_bytes,width,height,gpu_frame_ms");
                double started = Time.realtimeSinceStartupAsDouble;
                while (Time.realtimeSinceStartupAsDouble - started < seconds)
                {
                    yield return null;
                    double elapsed = Time.realtimeSinceStartupAsDouble - started;
                    float ms = Time.unscaledDeltaTime * 1000f;
                    frames.Add(ms);
                    long cpu = mainThread.Valid ? mainThread.LastValue : -1;
                    long allocated = gc.Valid ? gc.LastValue : -1;
                    double gpuMs = -1;
                    if (report.frameTimingFeatureEnabled)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, gpuTiming) > 0 && gpuTiming[0].gpuFrameTime > 0)
                        {
                            gpuMs = gpuTiming[0].gpuFrameTime;
                            gpuTotal += gpuMs; gpuMax = Math.Max(gpuMax, gpuMs); gpuSamples++;
                        }
                    }
                    if (cpu >= 0) { mainThreadTotal += cpu / 1000000.0; mainThreadMax = Math.Max(mainThreadMax, cpu / 1000000.0); }
                    if (allocated >= 0) gcTotal += allocated;
                    writer.Write(frames.Count); writer.Write(','); writer.Write(elapsed.ToString("F6", Invariant)); writer.Write(',');
                    writer.Write(ms.ToString("F6", Invariant)); writer.Write(','); writer.Write(cpu); writer.Write(','); writer.Write(allocated);
                    writer.Write(','); writer.Write(Screen.width); writer.Write(','); writer.Write(Screen.height);
                    writer.Write(','); writer.WriteLine(gpuMs.ToString("F6", Invariant));
                    if (frames.Count % 600 == 0) writer.Flush();
                }
                report.measuredSeconds = Time.realtimeSinceStartupAsDouble - started;
            }
            report.frames = frames.Count;
            double sum = 0;
            foreach (float frame in frames) sum += frame;
            frames.Sort();
            report.meanFrameMs = frames.Count == 0 ? 0 : sum / frames.Count;
            report.averageFps = report.measuredSeconds <= 0 ? 0 : frames.Count / report.measuredSeconds;
            report.p95FrameMs = Percentile(frames, .95); report.p99FrameMs = Percentile(frames, .99);
            report.maximumFrameMs = Percentile(frames, 1);
            report.averageMainThreadMs = frames.Count == 0 ? 0 : mainThreadTotal / frames.Count;
            report.maximumMainThreadMs = mainThreadMax; report.recordedGcAllocatedBytes = gcTotal;
            if (!report.gcRecorderAvailable) report.recordedGcAllocatedBytes = -1;
            report.gpuTimingSamples = gpuSamples;
            report.averageGpuFrameMs = gpuSamples > 0 ? gpuTotal / gpuSamples : -1;
            report.maximumGpuFrameMs = gpuSamples > 0 ? gpuMax : -1;
            if (gpuSamples > 0) report.gpuFrameTimingStatus = "available";
            report.performancePassed = seconds >= 600 && report.measuredSeconds >= 600 && report.averageFps >= 60 &&
                report.p95FrameMs <= 20 && report.benchmarkWidth == 1920 && report.benchmarkHeight == 1080;
        }

        private static float Percentile(List<float> sorted, double percentile)
            => sorted.Count == 0 ? 0 : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Count * percentile) - 1)];

        private void WriteReport()
        {
            report.resolutions = resolutions.ToArray(); report.memorySamples = memory.ToArray();
            report.adapterSnapshotJson = adapter.SnapshotJson;
            File.WriteAllText(Path.Combine(output, "audit-summary.json"), JsonUtility.ToJson(report, true));
        }

        private void Restore()
        {
            if (restored) return;
            restored = true;
            adapter?.Restore();
            QualitySettings.SetQualityLevel(originalQuality, true); QualitySettings.vSyncCount = originalVSync;
            Application.targetFrameRate = originalTarget; Application.runInBackground = originalRunInBackground;
            Screen.SetResolution(originalWidth, originalHeight, originalFullScreen);
        }

        private void Finish()
        {
            if (completed) return;
            ticking = false;
            try { Restore(); }
            catch (Exception error) { report.status = "failed"; report.error = (report.error ?? "") + "\nRestore: " + error; }
            report.finishedUtc = DateTime.UtcNow.ToString("o");
            WriteReport(); completed = true;
        }

        private void OnApplicationQuit()
        {
            if (completed || report == null) return;
            report.status = "interrupted"; report.error = "Player quit before the complete audit finished.";
            Finish();
        }
    }
}
