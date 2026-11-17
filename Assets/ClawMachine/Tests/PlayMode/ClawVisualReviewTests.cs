using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace ClawMachine.Tests.PlayMode
{
    /// <summary>Real 3D camera renders for review; excludes overlay UI and human visual acceptance.</summary>
    [Category("VisualReview")]
    public sealed class ClawVisualReviewTests
    {
        private sealed class QuietInput : IClawInputSource
        {
            public ClawInputFrame Read() => default(ClawInputFrame);
        }

        private sealed class Pose
        {
            public readonly int Challenge;
            public readonly string File;
            public readonly Vector2 Angles;
            public Pose(int challenge, string file, float yaw, float pitch)
            { Challenge = challenge; File = file; Angles = new Vector2(yaw, pitch); }
        }

        [Serializable]
        private sealed class Shot
        {
            public string challenge, file;
            public int width = 1920, height = 1080, linkageCount;
            public bool includesOverlayUI = false, humanVisualAcceptance = false;
            public Vector2 requestedOrbit, actualOrbit;
            public Vector3 cameraPosition, lookTarget;
            public float cameraRadius;
        }

        [Serializable]
        private sealed class Manifest
        {
            public string evidence = "Real Unity 3D Camera.Render to RenderTexture; excludes screen-space overlay UI. Human visual QA and gameplay acceptance remain pending.";
            public string generatedAtUtc, graphicsDevice;
            public bool simulationFrozen = true;
            public List<Shot> shots = new List<Shot>();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator RenderRealSceneFromFiveReviewAnglesAndVerifyVisibleLinkages()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("VisualReview requires an actual Unity graphics device for Camera.Render.");

            float previousTimeScale = Time.timeScale;
            bool previousFocusPause = ClawGameManager.AutoPauseOnFocusLoss;
            ClawGameManager game = null;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BuildOutput", "UserFeedback-20261116", "Visual"));
            var manifest = new Manifest {
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName
            };
            var poses = new[] {
                new Pose(0, "C01-front.png", 35, 8),
                new Pose(0, "C01-side.png", -75, 18),
                new Pose(0, "C01-back.png", 180, 12),
                new Pose(1, "C02-toys.png", 25, 20),
                new Pose(2, "C03-toys.png", -25, 20),
                new Pose(3, "C04-carousel.png", 28, 22)
            };
            try
            {
                ClawGameManager.AutoPauseOnFocusLoss = false;
                Time.timeScale = 1;
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
                game.Cameras.SetPipEnabled(false);
                Directory.CreateDirectory(directory);

                int previousChallenge = -1;
                foreach (Pose pose in poses)
                {
                    if (pose.Challenge != previousChallenge)
                    {
                        Time.timeScale = 1;
                        game.ReturnToMenu();
                        Assert.IsTrue(game.SelectChallenge(pose.Challenge));
                        game.StartGame();
                        Assert.IsFalse(game.InMenu);
                        Assert.AreEqual("C0" + (pose.Challenge + 1), game.Task.Challenge.challengeId);
                        yield return null;
                        yield return null;
                        Time.timeScale = 0;
                        previousChallenge = pose.Challenge;
                    }
                    game.Cameras.ResetOrbit();
                    game.Cameras.OrbitBy(pose.Angles);
                    yield return null;
                    yield return null;

                    ClawGripperLinkage[] linkages = Object.FindObjectsOfType<ClawGripperLinkage>();
                    Assert.Greater(linkages.Length, 0, "The real scene must contain the visible three-finger linkage.");
                    foreach (ClawGripperLinkage linkage in linkages)
                    {
                        linkage.RefreshNow();
                        Transform[] links = ReadField<Transform[]>(linkage, "links");
                        Assert.NotNull(links);
                        Assert.AreEqual(3, links.Length);
                        foreach (Transform link in links)
                        {
                            Assert.IsTrue(link && link.gameObject.activeInHierarchy, "Every strut must exist and be active.");
                            Assert.Greater(link.localScale.y, 0, "The strut must have a visible nonzero length.");
                            Renderer renderer = link.GetComponent<Renderer>();
                            Assert.IsTrue(renderer && renderer.enabled, "Every strut needs its real visible renderer.");
                        }
                    }

                    Camera camera = game.Cameras.CurrentCamera;
                    Transform target = ReadField<Transform>(game.Cameras, "lookTarget");
                    Assert.NotNull(camera); Assert.NotNull(target);
                    float radius = Vector3.Distance(camera.transform.position, target.position);
                    Assert.GreaterOrEqual(radius, 1.75f - 0.00001f, "Orbit must remain outside its minimum cabinet radius.");
                    WriteCameraRender(camera, Path.Combine(directory, pose.File));
                    manifest.shots.Add(new Shot {
                        challenge = game.Task.Challenge.challengeId, file = pose.File, linkageCount = linkages.Length,
                        requestedOrbit = pose.Angles, actualOrbit = game.Cameras.OrbitAngles,
                        cameraPosition = camera.transform.position, lookTarget = target.position, cameraRadius = radius
                    });
                }
                Assert.AreEqual(6, manifest.shots.Count);
                File.WriteAllText(Path.Combine(directory, "visual-review-manifest.json"), JsonUtility.ToJson(manifest, true));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                try
                {
                    if (game)
                    {
                        if (game.Cameras) game.Cameras.SetPipEnabled(false);
                        game.ReturnToMenu();
                    }
                }
                finally
                {
                    ClawGameManager.AutoPauseOnFocusLoss = previousFocusPause;
                    Time.timeScale = previousTimeScale;
                }
            }
        }

        private static void WriteCameraRender(Camera camera, string path)
        {
            Assert.IsNull(camera.targetTexture, "The main camera must not already own a render target.");
            RenderTexture previousActive = RenderTexture.active;
            Rect previousRect = camera.rect;
            float previousAspect = camera.aspect;
            RenderTexture target = null;
            Texture2D texture = null;
            try
            {
                target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) {
                    name = "Claw VisualReview 3D camera target", antiAliasing = 1
                };
                Assert.IsTrue(target.Create());
                camera.targetTexture = target;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = 1920f / 1080;
                camera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0, false);
                texture.Apply(false, false);
                Color32[] pixels = texture.GetPixels32();
                bool varied = false;
                for (int index = 1; index < pixels.Length; index++)
                    if (!pixels[index].Equals(pixels[0])) { varied = true; break; }
                Assert.IsTrue(varied, "A uniform frame cannot provide useful 3D render evidence.");
                byte[] png = texture.EncodeToPNG();
                Assert.Greater(png.Length, 1024);
                File.WriteAllBytes(path, png);
            }
            finally
            {
                camera.targetTexture = null;
                camera.rect = previousRect;
                camera.aspect = previousAspect;
                RenderTexture.active = previousActive;
                if (target) { target.Release(); Object.Destroy(target); }
                if (texture) Object.Destroy(texture);
            }
        }

        private static T ReadField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, name);
            return (T)field.GetValue(target);
        }
    }
}
