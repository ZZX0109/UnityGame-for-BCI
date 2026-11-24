using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Neuralife.VirtualHand
{
    /// <summary>
    /// Standalone-only evidence probe. In an editor/test run this component is
    /// inert; in the built player it writes the actual screen resolution and a
    /// rolling unscaled FPS measurement next to VirtualHand.exe.
    /// </summary>
    public class StandaloneDiagnostics : MonoBehaviour
    {
#if UNITY_STANDALONE && !UNITY_EDITOR
        [Serializable]
        class Report
        {
            public int screenWidth;
            public int screenHeight;
            public float fps;
            public string gpu;
            public string utc;
        }

        string _path;
        int _frames;
        float _elapsed;
        float _fps;

        void Start()
        {
            _path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StandaloneCheck.json"));
            Write();

            string requestedPath = ScreenshotArgument();
            if (string.IsNullOrEmpty(requestedPath)) return;
            if (!Path.IsPathRooted(requestedPath))
            {
                Debug.LogError("[StandaloneDiagnostics] -virtualHandScreenshot requires an absolute path.");
                return;
            }
            try
            {
                string fullPath = Path.GetFullPath(requestedPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                StartCoroutine(CaptureAfterRenderedFrames(fullPath));
            }
            catch (Exception e)
            {
                Debug.LogError("[StandaloneDiagnostics] Could not prepare screenshot path: " + e.Message);
            }
        }

        static string ScreenshotArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-virtualHandScreenshot", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        IEnumerator CaptureAfterRenderedFrames(string absolutePath)
        {
            // The first rendered frames can belong to the splash screen.
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            var endOfFrame = new WaitForEndOfFrame();
            for (int attempt = 0; attempt < 8; attempt++)
            {
                yield return endOfFrame;
                var frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame)
                {
                    bool hasScenePixels = false;
                    for (int x = 1; x < 12 && !hasScenePixels; x++)
                        for (int y = 1; y < 8 && !hasScenePixels; y++)
                            hasScenePixels = frame.GetPixel(frame.width * x / 12, frame.height * y / 8).maxColorComponent > 0.05f;
                    if (hasScenePixels)
                    {
                        File.WriteAllBytes(absolutePath, frame.EncodeToPNG());
                        Destroy(frame);
                        Debug.Log("[StandaloneDiagnostics] Saved player framebuffer screenshot: " + absolutePath);
                        yield break;
                    }
                    Destroy(frame);
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            Debug.LogWarning("[StandaloneDiagnostics] No visible pixels in the player framebuffer.");
        }

        void Update()
        {
            _frames++;
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed < 2f) return;
            _fps = _frames / _elapsed;
            _frames = 0;
            _elapsed = 0f;
            Write();
        }

        void Write()
        {
            var report = new Report
            {
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                fps = _fps,
                gpu = SystemInfo.graphicsDeviceName,
                utc = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(_path, JsonUtility.ToJson(report, true));
        }
#endif
    }
}
