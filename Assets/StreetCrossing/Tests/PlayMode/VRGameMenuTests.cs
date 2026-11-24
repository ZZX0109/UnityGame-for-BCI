#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using Neuralife.XR;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

namespace VRUsability.Tests
{
    public sealed class VRGameMenuTests
    {
        [UnityTest]
        public IEnumerator RealMenuStartsAndResumesWithAWithoutControllerAim()
        {
            bool preview = EditorPrefs.GetBool(PicoXR.PreviewKey, false);
            try
            {
                EditorPrefs.SetBool(PicoXR.PreviewKey, true);
                yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
                var game = Object.FindObjectOfType<StreetCrossing.Core.StreetGameManager>();
                game.AutoPauseOnFocusLoss = false;
                // Simulated head tracking, intentionally no tracked controller pose/ray.
                foreach (var runtime in Object.FindObjectsOfType<PicoXR>()) { runtime.StopAllCoroutines(); runtime.enabled = false; }
                Set("Ready", false); Set("HeadTracked", true); Set("RightTracked", false);
                Set("HeadPosition", Vector3.up * 1.65f); Set("HeadRotation", Quaternion.identity); Set("FloorTracking", false);
                foreach (var rig in Object.FindObjectsOfType<PicoRig>()) rig.Recenter();
                yield return null;
                var menu = PicoMenuNavigation.Current;
                Assert.IsTrue(menu && menu.HasMenu);
                yield return Capture("menu");
                menu.ProcessInput(Vector2.zero, false, false);
                menu.ProcessInput(Vector2.zero, true, true);
                Assert.IsTrue(game.IsPlaying);
                Assert.NotNull(game.Player.View);
                // Moving dismisses every gameplay plane, including the one-shot controls.
                PicoXR.Controls.Sample(true,true,true,Vector2.zero,Vector2.zero,false,false,false,false,false,false);
                PicoXR.Controls.Sample(true,true,true,Vector2.up,Vector2.zero,false,false,false,false,false,false);
                yield return null; yield return null;
                var gameplayHud = Object.FindObjectsOfType<Canvas>()[0].transform;
                foreach (var rect in Object.FindObjectsOfType<RectTransform>())
                    if (rect.name == "First person HUD") gameplayHud = rect;
                Assert.AreEqual("First person HUD", gameplayHud.name);
                foreach (Transform child in gameplayHud)
                    Assert.IsFalse(child.gameObject.activeSelf, "Moving VR gameplay must have no following HUD: " + child.name);
                PicoXR.Controls.Sample(true,true,true,Vector2.zero,Vector2.zero,false,false,false,false,false,false);
                game.Pause();
                yield return null;
                Assert.IsTrue(menu.HasMenu);
                yield return Capture("pause");
                menu.ProcessInput(Vector2.zero, false, false);
                menu.ProcessInput(Vector2.zero, true, true);
                Assert.IsTrue(game.IsPlaying);
                yield return null;
                yield return Capture("play");

                var ui = game.GetComponent<StreetCrossing.UI.StreetUIController>();
                ui.ShowSettings();
                yield return null; yield return null;
                Confirm(menu);
                yield return null; yield return null;
                Assert.IsFalse(ui.HasModal, "A returns from settings to pause.");
                Confirm(menu);
                Assert.IsTrue(game.IsPlaying);
                ui.ShowRestartConfirmation();
                yield return null; yield return null;
                Confirm(menu);
                yield return null; yield return null;
                Assert.IsFalse(ui.HasModal, "The default restart action must cancel.");
                Assert.AreEqual(StreetCrossing.Core.StreetGameState.Paused, game.State);

                // UI state fixture only: this does not claim a completed physical crossing.
                typeof(StreetCrossing.Core.StreetGameManager).GetProperty("State").SetValue(game, StreetCrossing.Core.StreetGameState.Result);
                yield return null; yield return null;
                Assert.IsTrue(menu.HasMenu);
                yield return Capture("result");
                Confirm(menu);
                Assert.IsTrue(game.IsPlaying, "A must retry from the result popup without aiming.");
                game.ReturnToMenu();
                int roundTrip = -1;
                for (int district = 0; district < game.Scenarios.Count && roundTrip < 0; district++)
                {
                    game.SelectScenario(district);
                    for (int i = 0; i < game.Missions.Count; i++) if (game.Missions[i].roundTrip) { roundTrip = i; break; }
                }
                Assert.GreaterOrEqual(roundTrip, 0);
                game.SelectMission(roundTrip); game.StartPractice();
                typeof(StreetCrossing.Core.StreetGameManager).GetProperty("State").SetValue(game, StreetCrossing.Core.StreetGameState.SegmentComplete);
                yield return null; yield return null;
                yield return Capture("segment");
                Confirm(menu);
                Assert.IsTrue(game.IsPlaying, "A must continue the return trip from its popup.");
                Assert.AreEqual(1, game.CurrentSegmentIndex);

            }
            finally
            {
                foreach (var owner in Object.FindObjectsOfType<StreetCrossing.Core.StreetGameManager>()) Object.DestroyImmediate(owner.gameObject);
                foreach (var runtime in Object.FindObjectsOfType<PicoXR>()) Object.DestroyImmediate(runtime.gameObject);
                Time.timeScale = 1;
                
                EditorPrefs.SetBool(PicoXR.PreviewKey, preview);
                Set("Ready", false); Set("HeadTracked", false); Set("RightTracked", false);
            }
        }
        private static void Set(string name, object value) => typeof(PicoXR).GetProperty(name, BindingFlags.Public | BindingFlags.Static).SetValue(null, value);
        private static void Confirm(PicoMenuNavigation menu)
        {
            menu.ProcessInput(Vector2.zero, false, false);
            menu.ProcessInput(Vector2.zero, true, true);
        }
        private static IEnumerator Capture(string name)
        {
            // Let the 120 ms comfort fade finish before judging readable pixels.
            yield return new WaitForSecondsRealtime(0.25f);
            string directory = Path.GetFullPath("BuildOutput/VRComfort-20261119"); Directory.CreateDirectory(directory);
            var camera = Camera.main; Assert.NotNull(camera);
            var target = new RenderTexture(1600, 1000, 24);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active, oldTarget = camera.targetTexture;
            try {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            } finally { camera.targetTexture = oldTarget; RenderTexture.active = previous; Object.DestroyImmediate(target); Object.DestroyImmediate(image); }
        }
    }
}
#endif
