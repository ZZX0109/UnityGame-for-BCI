using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Player;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using StreetCrossing.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>Real scene/camera/Canvas frames; partial route captures are not gameplay acceptance.</summary>
    [Category("VisualCapture")]
    public sealed class VisualCaptureTests
    {
        private sealed class CaptureInput : IStreetInput
        {
            public StreetGameManager game;
            public Vector2 movement;
            public float yaw, pitch;
            public Vector2 Movement => movement;
            public Vector2 Look => new Vector2(Mathf.DeltaAngle(game.Player.Heading, yaw), game.Player.Pitch - pitch) / StreetPreferences.Sensitivity;
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }
        [Serializable]
        private sealed class Shot
        {
            public string scenario, view, file, trafficPhase;
            public int width, height;
            public bool includesHUD = true;
            public bool simulationFrozen, gamePaused;
            public float timeScale, worldTime, yaw, pitch;
            public Vector3 playerPosition, cameraPosition;
            public Quaternion cameraRotation;
        }
        [Serializable]
        private sealed class Manifest
        {
            public int schemaVersion = 1;
            public string game = "street";
            public string renderer;
            public string evidence = "Actual full Game-view frames at input-driven route positions; simulation frozen without a pause overlay. Human review and full gameplay acceptance pending.";
            public List<Shot> screenshots = new List<Shot>();
        }
        private StreetGameManager game;
        private CaptureInput input;
        private Texture2D pendingTexture;
        private bool initialized;
        private float previousTimeScale;

        [UnityTest, Timeout(600000)]
        public IEnumerator CaptureThreeScenariosAtStartKerbAndCrosswalk()
        {
            string directory = Environment.GetEnvironmentVariable("BCI_VISUAL_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(directory)) Assert.Ignore("BCI_VISUAL_CAPTURE_DIR is not set.");
            Assert.IsTrue(Path.IsPathRooted(directory));
            Assert.IsFalse(Application.isBatchMode, "Use a visible Editor Game view for end-of-frame capture.");
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            Directory.CreateDirectory(directory);
            string manifestPath = Path.Combine(directory, "capture-manifest.json");
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            previousTimeScale = Time.timeScale;
            initialized = true;
            Time.timeScale = 1f;
#if UNITY_EDITOR
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/StreetCrossing/Scenes/StreetCrossingScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
#endif
            yield return null;
            game = Object.FindObjectOfType<StreetGameManager>();
            Assert.NotNull(game);
            Assert.IsTrue(game.enabled);
            game.AutoPauseOnFocusLoss = false;
            input = new CaptureInput { game = game };
            game.SetInputSource(input);
            StreetUIController ui = game.GetComponent<StreetUIController>();
            Assert.NotNull(ui);
            Assert.NotNull(ui.RuntimeCanvas);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, ui.RuntimeCanvas.renderMode);
            Assert.AreEqual(3, game.Scenarios.Count);
            var manifest = new Manifest { renderer = SystemInfo.graphicsDeviceType.ToString() };
            for (int index = 0; index < game.Scenarios.Count; index++)
            {
                Time.timeScale = 1f;
                game.ReturnToMenu();
                game.SelectScenario(index);
                game.SetInputSource(input);
                input.yaw = input.pitch = 0f;
                input.movement = Vector2.zero;
                game.StartPractice();
                Assert.AreEqual("S0" + (index + 1), game.scenario.scenarioId);
                yield return Capture(directory, manifest, "start");

                // Reach the waiting area with the actual CharacterController rather than teleporting the camera.
                yield return WalkTo(-game.scenario.roadHalfWidth - 0.75f, 15f);
                AimAt(game.LeftRoadTargets[0]);
                yield return null;
                yield return Capture(directory, manifest, "kerb");

                // S03's opaque shelter is deliberately preserved: move safely within the sidewalk to see past it.
                yield return WalkTo(-game.scenario.roadHalfWidth - 0.12f, 5f);
                yield return Observe(true);
                yield return Observe(false);
                input.yaw = input.pitch = 0f;
                float deadline = Time.realtimeSinceStartup + 160f;
                while (game.IsPlaying && (game.Intersection.Phase != TrafficPhase.Walk || game.Intersection.Remaining < game.scenario.SafeCrossingSeconds) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue(game.IsPlaying);
                Assert.AreEqual(TrafficPhase.Walk, game.Intersection.Phase, "No complete pedestrian window became available.");
                Assert.GreaterOrEqual(game.Intersection.Remaining, game.scenario.SafeCrossingSeconds);
                yield return WalkTo(0f, 8f);
                yield return Capture(directory, manifest, "crosswalk");
                Assert.IsFalse(game.Evaluation.Finished, "Capture route should stop midway, not fabricate full completion.");
            }
            Assert.AreEqual(9, manifest.screenshots.Count);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator WalkTo(float z, float seconds)
        {
            Time.timeScale = 1f;
            input.yaw = input.pitch = 0f;
            input.movement = Vector2.up;
            float deadline = Time.realtimeSinceStartup + seconds * 2f + 5f;
            while (game.IsPlaying && game.PlayerPosition.z < z && Time.realtimeSinceStartup < deadline) yield return null;
            input.movement = Vector2.zero;
            Assert.IsTrue(game.IsPlaying, "The actual route ended before reaching the requested viewpoint.");
            Assert.GreaterOrEqual(game.PlayerPosition.z, z - 0.1f, "Actual CharacterController could not reach the capture position.");
        }
        private void AimAt(Vector3 point)
        {
            Vector3 delta = point - game.Player.View.transform.position;
            input.yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            input.pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg;
        }
        private IEnumerator Observe(bool left)
        {
            AimAt(left ? game.LeftRoadTargets[0] : game.RightRoadTargets[0]);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (game.IsPlaying && !(left ? game.Evaluation.LeftObserved : game.Evaluation.RightObserved) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(left ? game.Evaluation.LeftObserved : game.Evaluation.RightObserved,
                "Normal visible pre-entry observation must work on the capture route.");
        }
        private IEnumerator Capture(string directory, Manifest manifest, string view)
        {
            input.movement = Vector2.zero;
            Time.timeScale = 0f;
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
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
            string file = game.scenario.scenarioId + "-" + view + ".png";
            File.WriteAllBytes(Path.Combine(directory, file), png);
            Camera camera = game.Player.View;
            manifest.screenshots.Add(new Shot {
                scenario = game.scenario.scenarioId, view = view, file = file,
                width = pendingTexture.width, height = pendingTexture.height,
                simulationFrozen = Time.timeScale == 0f, gamePaused = game.State == StreetGameState.Paused, timeScale = Time.timeScale,
                worldTime = game.WorldTime, trafficPhase = game.Intersection.Phase.ToString(),
                playerPosition = game.PlayerPosition, cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                yaw = game.Player.Heading, pitch = game.Player.Pitch
            });
            Object.Destroy(pendingTexture);
            pendingTexture = null;
            Time.timeScale = 1f;
        }

        [TearDown]
        public void RestoreSession()
        {
            if (pendingTexture) Object.Destroy(pendingTexture);
            if (!initialized) return;
            if (game) { game.ReturnToMenu(); game.AutoPauseOnFocusLoss = true; }
            Time.timeScale = previousTimeScale;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
