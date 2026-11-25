using System.Collections;
using NUnit.Framework;
using Neuralife.VirtualHand.Body;
using StreetCrossing.Core;
using StreetCrossing.Integration;
using StreetCrossing.Player;
using StreetCrossing.Traffic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace StreetCrossing.Tests.PlayMode
{
    /// <summary>
    /// V5-078 acceptance C: the first-person full body must be wired under the real
    /// PedestrianPlayerController in every mission scenario (S01-S03), stay visible in
    /// first person, and produce a real walk cycle driven by actual player movement.
    /// </summary>
    [Category("FullBodyIntegration")]
    public sealed class FullBodyStreetCrossingTests
    {
        const float BodyFollowToleranceMeters = 0.5f;

        sealed class TestInput : IStreetInput
        {
            public StreetGameManager game;
            public Vector2 movement;
            public float yaw, pitch;
            public Vector2 Movement => movement;
            public Vector2 Look => Vector2.zero; // consumer applies Sensitivity itself (precedent: StreetRuntimeAudit.cs)
            public bool FastWalk => false;
            public bool InteractPressed => false;
            public bool PausePressed => false;
            public bool RestartPressed => false;
        }

        StreetGameManager game;
        TestInput input;
        StreetCrossingFullBodyBinding binding;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/StreetCrossing/Scenes/StreetCrossingScene.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
#endif
            yield return null;
            game = Object.FindObjectOfType<StreetGameManager>();
            Assert.NotNull(game);
            game.AutoPauseOnFocusLoss = false;
            game.PersistMissionProgress = false;
            input = new TestInput { game = game };
            game.SetInputSource(input);

            binding = Object.FindObjectOfType<StreetCrossingFullBodyBinding>();
            if (binding == null)
            {
                var host = new GameObject("StreetCrossingFullBodyBinding(Test)");
                Object.DontDestroyOnLoad(host);
                binding = host.AddComponent<StreetCrossingFullBodyBinding>();
            }
        }

        [TearDown]
        public void TearDown()
        {
            input.movement = Vector2.zero;
            if (game) game.ReturnToMenu();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        IEnumerator StartScenario(int index)
        {
            game.ReturnToMenu();
            game.SelectScenario(index);
            game.SetInputSource(input);
            input.yaw = input.pitch = 0f;
            input.movement = Vector2.zero;
            game.StartPractice();
            Assert.AreEqual("S0" + (index + 1), game.scenario.scenarioId);
            yield return null;

            float deadline = Time.realtimeSinceStartup + 10f;
            while (!binding.IsWired && Time.realtimeSinceStartup < deadline)
            {
                binding.EnsureWiring();
                yield return null;
            }
            Assert.IsTrue(binding.IsWired, "Full body was not wired under the player within 10 s (scenario S0" + (index + 1) + ").");
        }

        static int CountVisibleRenderers(GameObject root)
        {
            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled && renderer.isVisible)
                    count++;
            return count;
        }

        static int CountEnabledRenderers(GameObject root)
        {
            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled)
                    count++;
            return count;
        }

        // Batchmode has no game view, so frustum culling never runs on its own and
        // Renderer.isVisible stays false.  Rendering one frame through the camera runs
        // the real culling pass, which makes isVisible meaningful without -nographics.
        static void RenderOneFrame(Camera camera)
        {
            Camera cam = camera != null ? camera : Camera.main;
            if (cam == null && Camera.allCamerasCount > 0)
                cam = Camera.allCameras[0];
            if (cam == null)
                return;
            RenderTexture rt = RenderTexture.GetTemporary(256, 256, 16);
            RenderTexture previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previous;
            RenderTexture.ReleaseTemporary(rt);
        }

        static Transform FindDeepBone(Transform root, string namePart)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains(namePart))
                    return t;
            return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator FullBodyStaysWiredAndVisibleAcrossS01ToS03()
        {
            for (int index = 0; index < 3; index++)
            {
                yield return StartScenario(index);

                var body = binding.BodyInstance;
                Assert.True(body != null && body.activeInHierarchy, "Body instance inactive in S0" + (index + 1) + ".");
                Assert.AreEqual(binding.PlayerRoot, body.transform.parent, "Body must be parented under the player root in S0" + (index + 1) + ".");

                var visibility = body.GetComponentInChildren<FirstPersonVisibility>(true);
                Assert.NotNull(visibility, "FirstPersonVisibility missing on the body in S0" + (index + 1) + ".");
                Assert.IsTrue(visibility.FirstPersonActive, "First-person visibility not active in S0" + (index + 1) + ".");

                // Wiring-level check independent of the batchmode render pipeline: only the
                // head renderer is hidden in first person, so many renderers stay enabled.
                int enabledCount = CountEnabledRenderers(body);
                Assert.Greater(enabledCount, 5, "Body renderers disabled in first person in S0" + (index + 1) + ".");

                // Batchmode never drives culling on its own, so render one frame through the
                // first-person camera before trusting Renderer.isVisible.
                RenderOneFrame(visibility.ResolvedFirstPersonCamera);
                int visible = CountVisibleRenderers(body);
                Assert.Greater(visible, 5, "Body renderers not visible in first person in S0" + (index + 1) + ".");

                // The body tracks the player root (same origin, small first-person offset).
                float gap = Vector3.Distance(body.transform.position, binding.PlayerRoot.position);
                Assert.Less(gap, BodyFollowToleranceMeters, "Body drifted from the player root in S0" + (index + 1) + ": " + gap.ToString("F3") + " m.");

                // One real step of input-driven walking keeps the wiring alive per scenario.
                input.movement = Vector2.up;
                yield return new WaitForSeconds(0.5f);
                input.movement = Vector2.zero;
                Assert.IsTrue(binding.IsWired, "Wiring lost while walking in S0" + (index + 1) + ".");
                Assert.Less(Vector3.Distance(body.transform.position, binding.PlayerRoot.position), BodyFollowToleranceMeters,
                            "Body stopped following the player while walking in S0" + (index + 1) + ".");
            }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator RealPlayerMovementDrivesWalkCycleLegBones()
        {
            yield return StartScenario(0);

            var body = binding.BodyInstance;
            var driver = body.GetComponentInChildren<WalkCycleDriver>();
            var locomotion = body.GetComponentInChildren<LowerBodyPoseController>();
            Assert.NotNull(driver, "WalkCycleDriver missing on the body instance.");
            Assert.NotNull(locomotion, "LowerBodyPoseController missing on the body instance.");

            Assert.Less(driver.MoveBlend, 0.05f, "Walk blend should start near rest before any movement.");
            float pathBefore = locomotion.PathDistanceMeters;

            input.movement = Vector2.up; // real CharacterController movement through the fake input source
            float deadline = Time.realtimeSinceStartup + 10f;
            while (locomotion.PathDistanceMeters - pathBefore < 1.2f && Time.realtimeSinceStartup < deadline)
                yield return null;
            input.movement = Vector2.zero;

            Assert.GreaterOrEqual(locomotion.PathDistanceMeters - pathBefore, 1.0f,
                "Player CharacterController did not accumulate >=1 m of real travel.");
            Assert.GreaterOrEqual(driver.MoveBlend, 0.5f,
                "WalkCycleDriver did not blend into the gait despite >1 m of player movement.");

            float phaseBefore = driver.PhaseRadians;
            // Elena rig thigh (manifest name thigh_l / thigh.l) lives inside a nested prefab instance;
            // resolve it by name instead of guessing at BodyRoot's first child.
            Transform thigh = FindDeepBone(body.transform, "thigh");
            Assert.NotNull(thigh, "Thigh bone not found under the body instance (expected Elena 'thigh_l'/'thigh.l' rig).");

            Quaternion thighRest = thigh.rotation;
            input.movement = Vector2.up;
            deadline = Time.realtimeSinceStartup + 5f;
            float moved = 0f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                moved = Mathf.Max(moved, Quaternion.Angle(thighRest, thigh.rotation));
                if (Mathf.Abs(Mathf.DeltaAngle(driver.PhaseRadians, phaseBefore)) > 2f * Mathf.PI)
                    break;
            }
            input.movement = Vector2.zero;

            Assert.Greater(moved, 2f, "Thigh bone never rotated by more than 2 degrees while walking.");
            Assert.Greater(driver.PhaseRadians, 0f, "Gait phase did not advance.");
            Assert.IsTrue(binding.IsWired, "Wiring lost during the walk-cycle check.");
        }
    }
}
