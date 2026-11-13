using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ClawMachine.Tests.PlayMode
{
    /// <summary>Optional actual Game-view captures. Capturing is not gameplay or visual acceptance.</summary>
    [Category("VisualCapture")]
    public sealed class VisualCaptureTests
    {
        private sealed class QuietInput : IClawInputSource
        {
            public ClawInputFrame Read() => default(ClawInputFrame);
        }
        [Serializable]
        private sealed class Shot
        {
            public string scenario, view, file;
            public int width, height, selectedView;
            public bool includesHUD = true;
            public bool includesPIP, simulationFrozen, gamePaused;
            public float timeScale;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
        }
        [Serializable]
        private sealed class Manifest
        {
            public int schemaVersion = 1;
            public string game = "claw";
            public string renderer;
            public string evidence = "Actual full Game-view frames; frozen simulation, no pause overlay; human review and gameplay acceptance pending.";
            public List<Shot> screenshots = new List<Shot>();
        }
        private ClawGameManager game;
        private Texture2D pendingTexture;
        private bool initialized, previousFocusPause;
        private float previousTimeScale;

        [UnityTest, Timeout(180000)]
        public IEnumerator CaptureAllThreeChallengesFromActualCamerasAndPip()
        {
            string directory = Environment.GetEnvironmentVariable("BCI_VISUAL_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) Assert.Ignore("BCI_VISUAL_CAPTURE_DIR is not set.");
            Assert.IsTrue(Path.IsPathRooted(directory));
            Assert.IsFalse(Application.isBatchMode, "Keep a visible Editor Game view for end-of-frame capture.");
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            Directory.CreateDirectory(directory);
            string manifestPath = Path.Combine(directory, "capture-manifest.json");
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            previousFocusPause = ClawGameManager.AutoPauseOnFocusLoss;
            previousTimeScale = Time.timeScale;
            initialized = true;
            ClawGameManager.AutoPauseOnFocusLoss = false;
            Time.timeScale = 1f;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/ClawMachine/Scenes/ClawMachineScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("ClawMachineScene", LoadSceneMode.Single);
#endif
            yield return null;
            game = Object.FindObjectOfType<ClawGameManager>();
            Assert.NotNull(game);
            Assert.IsTrue(game.enabled && game.Task.IsConfigured && game.Cameras.IsConfigured);
            game.SetInputSource(new QuietInput());
            game.Cameras.Smooth = false;
            ClawGameUI ui = Object.FindObjectOfType<ClawGameUI>();
            Assert.NotNull(ui);
            Assert.IsTrue(ui.IsReady, ui.BootstrapError);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, ui.OwnedCanvas.renderMode);
            Assert.AreEqual(3, game.Challenges.Count);
            var manifest = new Manifest { renderer = SystemInfo.graphicsDeviceType.ToString() };
            string[] names = { "front", "left", "right", "pip" };
            for (int index = 0; index < game.Challenges.Count; index++)
            {
                Time.timeScale = 1f;
                game.ReturnToMenu();
                Assert.IsTrue(game.SelectChallenge(index));
                game.StartGame();
                string scenario = "C0" + (index + 1);
                Assert.AreEqual(scenario, game.Task.Challenge.challengeId);
                Assert.IsFalse(game.InMenu);
                // Let actual rigid bodies settle, then freeze only simulation so no pause modal hides the machine.
                float deadline = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < deadline) yield return null;
                Time.timeScale = 0f;
                for (int view = 0; view < names.Length; view++)
                {
                    game.Cameras.SetPipEnabled(false);
                    game.Cameras.Select(view == 3 ? 1 : view + 1);
                    game.Cameras.ResetOrbit();
                    if (view == 3) { game.Cameras.SetPipQuality(false); game.Cameras.SetPipEnabled(true); }
                    yield return null;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    if (view == 3)
                    {
                        Assert.IsTrue(game.Cameras.PipEnabled);
                        Assert.IsTrue(game.Cameras.PipTexture.IsCreated());
                        bool displayed = false;
                        foreach (RawImage image in ui.OwnedCanvas.GetComponentsInChildren<RawImage>(false))
                            if (image.texture == game.Cameras.PipTexture && image.gameObject.activeInHierarchy) displayed = true;
                        Assert.IsTrue(displayed, "The PIP texture must be displayed in the actual Canvas, not merely allocated.");
                    }
                    yield return new WaitForEndOfFrame();
                    WriteShot(directory, manifest, scenario, names[view]);
                }
            }
            Assert.AreEqual(12, manifest.screenshots.Count);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            LogAssert.NoUnexpectedReceived();
        }

        private void WriteShot(string directory, Manifest manifest, string scenario, string view)
        {
            pendingTexture = ScreenCapture.CaptureScreenshotAsTexture();
            Assert.NotNull(pendingTexture);
            Assert.GreaterOrEqual(Screen.width, 320);
            Assert.GreaterOrEqual(Screen.height, 240);
            Assert.AreEqual(Screen.width, pendingTexture.width);
            Assert.AreEqual(Screen.height, pendingTexture.height);
            Color32[] pixels = pendingTexture.GetPixels32();
            bool varied = false;
            for (int i = 1; i < pixels.Length; i++) if (!pixels[i].Equals(pixels[0])) { varied = true; break; }
            Assert.IsTrue(varied, "A uniform frame is not useful render evidence.");
            byte[] png = pendingTexture.EncodeToPNG();
            Assert.Greater(png.Length, 1024);
            string file = scenario + "-" + view + ".png";
            File.WriteAllBytes(Path.Combine(directory, file), png);
            Camera camera = game.Cameras.CurrentCamera;
            manifest.screenshots.Add(new Shot {
                scenario = scenario, view = view, file = file, width = pendingTexture.width, height = pendingTexture.height,
                selectedView = game.Cameras.SelectedView, includesPIP = game.Cameras.PipEnabled,
                simulationFrozen = Time.timeScale == 0f, gamePaused = game.IsPaused, timeScale = Time.timeScale,
                cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation
            });
            Object.Destroy(pendingTexture);
            pendingTexture = null;
        }

        [TearDown]
        public void RestoreSession()
        {
            if (pendingTexture) Object.Destroy(pendingTexture);
            if (!initialized) return;
            if (game) { game.Cameras.SetPipEnabled(false); game.ReturnToMenu(); }
            ClawGameManager.AutoPauseOnFocusLoss = previousFocusPause;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
