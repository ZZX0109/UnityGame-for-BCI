using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Neuralife.VirtualHand;
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

        static Color32[] CaptureEyeCamera(Camera camera, int width, int height, string path)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply(false, false);
                if (!string.IsNullOrEmpty(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, image.EncodeToPNG());
                }
                return image.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(image);
            }
        }

        static RectInt LandmarkRegion(Camera camera, int width, int height, Transform[] landmarks, int padding)
        {
            int left = width - 1, bottom = height - 1, right = 0, top = 0;
            foreach (var landmark in landmarks)
            {
                Vector3 viewport = camera.WorldToViewportPoint(landmark.position);
                Assert.Greater(viewport.z, camera.nearClipPlane,
                    landmark.name + " landmark is behind the first-person eye camera.");
                Assert.That(viewport.x, Is.InRange(0.01f, 0.99f), landmark.name + " landmark is outside the eye-camera horizontal frame.");
                Assert.That(viewport.y, Is.InRange(0.01f, 0.99f), landmark.name + " landmark is outside the eye-camera vertical frame.");
                int x = Mathf.RoundToInt(viewport.x * width);
                int y = Mathf.RoundToInt(viewport.y * height);
                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x);
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y);
            }

            left = Mathf.Clamp(left - padding, 0, width - 1);
            bottom = Mathf.Clamp(bottom - padding, 0, height - 1);
            right = Mathf.Clamp(right + padding + 1, left + 1, width);
            top = Mathf.Clamp(top + padding + 1, bottom + 1, height);
            return new RectInt(left, bottom, right - left, top - bottom);
        }

        static int CountPixelDifferences(Color32[] first, Color32[] second, int width, RectInt region, byte threshold)
        {
            Assert.AreEqual(first.Length, second.Length);
            int count = 0;
            for (int y = region.yMin; y < region.yMax; y++)
            for (int x = region.xMin; x < region.xMax; x++)
            {
                int index = y * width + x;
                int delta = Mathf.Abs(first[index].r - second[index].r)
                    + Mathf.Abs(first[index].g - second[index].g)
                    + Mathf.Abs(first[index].b - second[index].b);
                if (delta >= threshold) count++;
            }
            return count;
        }

        static int CountMaskPixels(Color32[] mask)
        {
            int count = 0;
            foreach (var pixel in mask)
                if (pixel.r + pixel.g + pixel.b > 36)
                    count++;
            return count;
        }

        static int CountDifferencesInsideMask(Color32[] first, Color32[] second, Color32[] mask, byte threshold)
        {
            Assert.AreEqual(first.Length, second.Length);
            Assert.AreEqual(first.Length, mask.Length);
            int count = 0;
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i].r + mask[i].g + mask[i].b <= 36) continue;
                int delta = Mathf.Abs(first[i].r - second[i].r)
                    + Mathf.Abs(first[i].g - second[i].g)
                    + Mathf.Abs(first[i].b - second[i].b);
                if (delta >= threshold) count++;
            }
            return count;
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

                // At eye height, looking straight ahead should cull most of a naturally
                // lowered body. Look down within the player's supported pitch range.
                Camera eye = visibility.ResolvedFirstPersonCamera;
                Assert.NotNull(eye);
                Quaternion aim = eye.transform.localRotation;
                eye.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                RenderOneFrame(eye);
                eye.transform.localRotation = aim;
                foreach (var head in visibility.HiddenRenderers)
                    Assert.IsFalse(head.enabled, head.name + " visible in first person");
                var torso = body.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .Single(r => r.name == "Body");
                Assert.IsTrue(torso.enabled && torso.isVisible, "Body not visible when looking down in S0" + (index + 1));
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
        public IEnumerator DefaultEyeCameraShowsBothHandsAndBRestoredHeadStaysCulled()
        {
            const int width = 1068, height = 500;
            const float HudSafeViewportY = 0.22f;
            const byte pixelDifferenceThreshold = 48;
            // Ignore up to one 8-bit quantization step per channel on repeated GPU renders.
            const byte visibilityPixelDifferenceThreshold = 4;
            yield return StartScenario(0);
            yield return null;

            var player = binding.PlayerRoot.GetComponent<PedestrianPlayerController>();
            Assert.NotNull(player, "Real street player controller is missing.");
            Camera eye = player.View;
            Assert.NotNull(eye, "PedestrianPlayerController.View eye camera is missing.");
            Assert.IsFalse(eye.orthographic, "The gameplay eye camera must retain perspective projection.");

            var body = binding.BodyInstance;
            var visibility = body.GetComponentInChildren<FirstPersonVisibility>(true);
            Assert.NotNull(visibility);
            Assert.IsTrue(visibility.FirstPersonActive, "The street game must start in first person.");
            Assert.AreSame(eye, visibility.ResolvedFirstPersonCamera,
                "First-person body visibility must use the actual gameplay eye camera.");
            var pose = binding.Adapter.poseController;
            Assert.NotNull(pose);
            Assert.AreEqual(FullBodyPoseController.PoseDesktopHandsVisible, pose.CurrentPoseName,
                "Default desktop first-person mode must use the hands-visible pose.");

            Transform eyeCenter = body.transform.Find("BodyRoot/EyeCenter");
            Assert.NotNull(eyeCenter, "The full-body prefab must expose the skinned-eye center anchor.");
            Vector3 eyeCenterInPlayer = binding.PlayerRoot.InverseTransformPoint(eyeCenter.position);
            Vector3 cameraInPlayer = binding.PlayerRoot.InverseTransformPoint(eye.transform.position);
            Assert.AreEqual(eyeCenterInPlayer.y, cameraInPlayer.y, 0.005f,
                "Desktop eye-camera height must match the skinned eyeball center in player-local space.");
            Assert.AreEqual(eyeCenterInPlayer.x, cameraInPlayer.x, 0.005f,
                "Desktop eye camera must remain centered on the skinned eyeball center.");
            Vector3 eyeOffset = eye.transform.position - eyeCenter.position;
            Assert.Less((eyeOffset - binding.PlayerRoot.forward * 0.05f).magnitude, 0.005f,
                "Desktop eye camera must sit 5 cm forward of the skinned eyeball center.");

            int headLayer = LayerMask.NameToLayer("LocalAvatarHead");
            Assert.GreaterOrEqual(headLayer, 0, "LocalAvatarHead layer is missing.");
            int headBit = 1 << headLayer;
            Assert.AreEqual(0, eye.cullingMask & headBit,
                "The actual wearer camera must cull the self-head layer in first person.");
            Assert.IsNotEmpty(visibility.HiddenRenderers, "No head renderers are wired to first-person visibility.");
            foreach (var head in visibility.HiddenRenderers)
            {
                Assert.NotNull(head);
                Assert.IsFalse(head.enabled, head.name + " must be hidden in the default first-person view.");
                Assert.AreEqual(headLayer, head.gameObject.layer,
                    head.name + " must use the dedicated self-head layer.");
            }

            int originalMask = eye.cullingMask;
            CameraClearFlags originalClearFlags = eye.clearFlags;
            Color originalBackground = eye.backgroundColor;
            float originalAspect = eye.aspect;
            Vector3 originalEyePosition = eye.transform.position;
            Quaternion originalRotation = eye.transform.localRotation;
            bool originalFirstPerson = visibility.FirstPersonActive;
            bool[] originalHeadStates = visibility.HiddenRenderers.Select(renderer => renderer.enabled).ToArray();
            int[] originalHeadLayers = visibility.HiddenRenderers.Select(renderer => renderer.gameObject.layer).ToArray();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Results/FirstPersonEyeCamera"));
            var metrics = new System.Collections.Generic.List<string> {
                "scenario,view,side,landmark,viewport_x,viewport_y,depth_m,pixel_x,pixel_y,roi_x,roi_y,roi_w,roi_h,changed_pixels"
            };
            var acceptanceErrors = new System.Collections.Generic.List<string>();
            try
            {
                eye.aspect = (float)width / height;
                int[] sideSigns = { -1, 1 };
                var handRenderers = new SkinnedMeshRenderer[2];
                var wristRegions = new RectInt[2];
                var fingertipRegions = new RectInt[2];
                var projectedRows = new System.Collections.Generic.List<string>[2];
                for (int i = 0; i < sideSigns.Length; i++)
                {
                    bool left = sideSigns[i] < 0;
                    Transform wrist = left ? pose.leftWrist : pose.rightWrist;
                    Assert.NotNull(wrist, (left ? "Left" : "Right") + " wrist bone is missing.");
                    var handBones = wrist.GetComponentsInChildren<Transform>(true);
                    Transform palm = handBones.Single(t => t.name == "hand.R_02");
                    Transform fingertip = handBones.Single(t => t.name == "middle_tip.R_045");
                    var aligner = palm.GetComponentInParent<ElenaWristAligner>();
                    Assert.NotNull(aligner, (left ? "Left" : "Right") + " Elena hand alignment root is missing.");
                    handRenderers[i] = aligner.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    Assert.NotNull(handRenderers[i], (left ? "Left" : "Right") + " skinned hand mesh is missing.");
                    Assert.IsTrue(handRenderers[i].enabled, (left ? "Left" : "Right") + " skinned hand mesh is disabled.");

                    Vector3 wristViewport = eye.WorldToViewportPoint(palm.position);
                    Vector3 tipViewport = eye.WorldToViewportPoint(fingertip.position);
                    Assert.Greater(wristViewport.z, eye.nearClipPlane, (left ? "Left" : "Right") + " wrist is behind the eye camera.");
                    Assert.Greater(tipViewport.z, eye.nearClipPlane, (left ? "Left" : "Right") + " fingertip is behind the eye camera.");
                    Assert.That(wristViewport.x, Is.InRange(0.01f, 0.99f), (left ? "Left" : "Right") + " wrist is outside the image.");
                    Assert.That(tipViewport.x, Is.InRange(0.01f, 0.99f), (left ? "Left" : "Right") + " fingertip is outside the image.");
                    Assert.That(wristViewport.y, Is.GreaterThan(HudSafeViewportY), (left ? "Left" : "Right") + " wrist falls below the HUD-safe region.");
                    Assert.That(tipViewport.y, Is.GreaterThan(HudSafeViewportY), (left ? "Left" : "Right") + " fingertip falls below the HUD-safe region.");

                    wristRegions[i] = LandmarkRegion(eye, width, height, new[] { palm }, 34);
                    fingertipRegions[i] = LandmarkRegion(eye, width, height, new[] { fingertip }, 28);
                    projectedRows[i] = new System.Collections.Generic.List<string> {
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "S01,first-person,{0},wrist,{1:F5},{2:F5},{3:F4},{4},{5},{6},{7},{8},{9},",
                            left ? "left" : "right", wristViewport.x, wristViewport.y, wristViewport.z,
                            Mathf.RoundToInt(wristViewport.x * width), Mathf.RoundToInt(wristViewport.y * height),
                            wristRegions[i].x, wristRegions[i].y, wristRegions[i].width, wristRegions[i].height),
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "S01,first-person,{0},middle-fingertip,{1:F5},{2:F5},{3:F4},{4},{5},{6},{7},{8},{9},",
                            left ? "left" : "right", tipViewport.x, tipViewport.y, tipViewport.z,
                            Mathf.RoundToInt(tipViewport.x * width), Mathf.RoundToInt(tipViewport.y * height),
                            fingertipRegions[i].x, fingertipRegions[i].y, fingertipRegions[i].width, fingertipRegions[i].height)
                    };
                }

                Color32[] withHands = CaptureEyeCamera(eye, width, height, Path.Combine(output, "hands-visible-1068x500.png"));
                for (int i = 0; i < handRenderers.Length; i++)
                {
                    bool wasEnabled = handRenderers[i].enabled;
                    Color32[] withoutHand;
                    handRenderers[i].enabled = false;
                    try
                    {
                        withoutHand = CaptureEyeCamera(eye, width, height,
                            Path.Combine(output, i == 0 ? "without-left-hand-1068x500.png" : "without-right-hand-1068x500.png"));
                    }
                    finally { handRenderers[i].enabled = wasEnabled; }
                    int wristChanged = CountPixelDifferences(withHands, withoutHand, width, wristRegions[i], pixelDifferenceThreshold);
                    int fingertipChanged = CountPixelDifferences(withHands, withoutHand, width, fingertipRegions[i], pixelDifferenceThreshold);
                    projectedRows[i][0] += wristChanged.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    projectedRows[i][1] += fingertipChanged.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    metrics.AddRange(projectedRows[i]);
                    Debug.Log($"[EyeCameraAcceptance] {(i == 0 ? "left" : "right")} hand: 1068x500 wrist ROI changed pixels={wristChanged}, middle-fingertip ROI changed pixels={fingertipChanged}; both must remain above HUD y={HudSafeViewportY:F2}.");
                    if (wristChanged <= 12 || fingertipChanged <= 1)
                        acceptanceErrors.Add((i == 0 ? "Left" : "Right") + " hand produced wrist/fingertip ROI changes of " + wristChanged + "/" + fingertipChanged + " pixels.");
                }
                Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "projection-and-pixel-metrics.csv"), metrics);

                if (!Application.isBatchMode && Screen.width > 0 && Screen.height > 0)
                {
                    yield return new WaitForEndOfFrame();
                    Texture2D gameView = ScreenCapture.CaptureScreenshotAsTexture();
                    try
                    {
                        File.WriteAllBytes(Path.Combine(output, "game-view-with-hud.png"), gameView.EncodeToPNG());
                    }
                    finally { Object.Destroy(gameView); }
                }

                // At the real eye position/pitch, B may restore the avatar renderer but the
                // wearer camera must continue to cull it. Keep the body pose fixed so the
                // before/after full-frame pixel delta isolates the head visibility change.
                RectInt fullFrame = new RectInt(0, 0, width, height);
                int maskAfterB = originalMask;
                bool firstBMask = true;
                bool wearerCameraStillCullsHead = true;
                foreach (int pitch in new[] { 65, -65 }) // positive pitch looks down; negative pitch looks up
                {
                    string view = pitch > 0 ? "look-down" : "look-up";
                    if (!visibility.FirstPersonActive)
                        binding.ToggleFirstPerson();
                    Assert.IsTrue(visibility.FirstPersonActive, "B must hide the head before the first-person comparison.");
                    Assert.AreEqual(FullBodyPoseController.PoseDesktopHandsVisible, pose.CurrentPoseName,
                        "The default desktop hand pose could not be reapplied for the B visibility comparison.");
                    eye.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    Color32[] firstPerson = CaptureEyeCamera(eye, width, height,
                        Path.Combine(output, "first-person-" + view + "-1068x500.png"));

                    binding.ToggleFirstPerson(); // B restores the avatar head mesh.
                    if (visibility.FirstPersonActive || visibility.HiddenRenderers.Any(renderer => !renderer.enabled))
                        acceptanceErrors.Add("B did not restore every self-head renderer in " + view + ".");
                    Assert.AreEqual(FullBodyPoseController.PoseDesktopHandsVisible, pose.CurrentPoseName,
                        "B-state pose could not be aligned with the first-person comparison.");
                    int currentMaskAfterB = eye.cullingMask;
                    if (firstBMask)
                    {
                        maskAfterB = currentMaskAfterB;
                        wearerCameraStillCullsHead = (maskAfterB & headBit) == 0;
                        firstBMask = false;
                    }
                    else if (currentMaskAfterB != maskAfterB)
                    {
                        acceptanceErrors.Add("The wearer camera mask changed between B pitch checks.");
                    }

                    Color32[] headRestoredAtRealEye = CaptureEyeCamera(eye, width, height,
                        Path.Combine(output, "b-restored-head-" + view + "-1068x500.png"));
                    Assert.IsFalse(visibility.FirstPersonActive, "The test must remain in the B-restored avatar state.");
                    Assert.IsTrue(visibility.HiddenRenderers.All(renderer => renderer.enabled),
                        "B must keep the avatar head renderers restored in " + view + ".");

                    int toggleChangedPixels = CountPixelDifferences(firstPerson, headRestoredAtRealEye, width, fullFrame, visibilityPixelDifferenceThreshold);
                    if (toggleChangedPixels != 0)
                        acceptanceErrors.Add("B changed " + toggleChangedPixels + " full-frame pixels at the real eye position in " + view + ".");
                    metrics.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "S01,{0},avatar,b-restored-eye-view,,,,,,,,,,{1}", view, toggleChangedPixels));
                    Debug.Log($"[EyeCameraAcceptance] {view}: B-restored head/full-frame changed pixels={toggleChangedPixels}; runtime head layer culled={(currentMaskAfterB & headBit) == 0}.");
                }

                // At legal pitch the face mask may be empty: the corrected camera is
                // already in front of the eyes. Build a separate non-empty color positive
                // control by moving the same camera forward and looking back at the face.
                eye.transform.position = eyeCenter.position + binding.PlayerRoot.forward * 0.65f;
                eye.transform.LookAt(eyeCenter.position);
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = Color.black;
                eye.cullingMask = headBit;
                Color32[] faceMask = CaptureEyeCamera(eye, width, height,
                    Path.Combine(output, "face-mask-look-back-positive-control-1068x500.png"));
                int facePixels = CountMaskPixels(faceMask);

                eye.clearFlags = originalClearFlags;
                eye.backgroundColor = originalBackground;
                int maskBeforePositiveControl = eye.cullingMask = maskAfterB;
                eye.cullingMask = maskBeforePositiveControl | headBit;
                Color32[] faceVisible = CaptureEyeCamera(eye, width, height,
                    Path.Combine(output, "face-visible-look-back-positive-control-1068x500.png"));
                eye.cullingMask = maskBeforePositiveControl;
                Color32[] faceCulled = CaptureEyeCamera(eye, width, height,
                    Path.Combine(output, "face-culled-look-back-negative-control-1068x500.png"));
                int faceControlChangedPixels = facePixels > 12
                    ? CountDifferencesInsideMask(faceVisible, faceCulled, faceMask, pixelDifferenceThreshold)
                    : 0;
                if (facePixels <= 12)
                    acceptanceErrors.Add("The forward-and-look-back face positive control produced only " + facePixels + " mask pixels.");
                if (faceControlChangedPixels <= 8)
                    acceptanceErrors.Add("The face positive/negative layer controls changed only " + faceControlChangedPixels + " masked pixels.");
                metrics.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "S01,look-back-control,avatar,face-layer-mask,,,,,,,,,,{0}", faceControlChangedPixels));
                Debug.Log($"[EyeCameraAcceptance] look-back control: face-mask pixels={facePixels}, positive/negative camera-layer changes={faceControlChangedPixels}.");

                if (!wearerCameraStillCullsHead)
                    acceptanceErrors.Add("The wearer camera stopped culling LocalAvatarHead after B restored the avatar.");
                File.WriteAllLines(Path.Combine(output, "projection-and-pixel-metrics.csv"), metrics);
                Assert.IsEmpty(acceptanceErrors, string.Join("\n", acceptanceErrors));
            }
            finally
            {
                eye.cullingMask = originalMask;
                eye.clearFlags = originalClearFlags;
                eye.backgroundColor = originalBackground;
                eye.aspect = originalAspect;
                eye.transform.position = originalEyePosition;
                eye.transform.localRotation = originalRotation;
                if (visibility.FirstPersonActive != originalFirstPerson)
                    binding.ToggleFirstPerson();
                for (int i = 0; i < visibility.HiddenRenderers.Length; i++)
                {
                    if (!visibility.HiddenRenderers[i]) continue;
                    visibility.HiddenRenderers[i].gameObject.layer = originalHeadLayers[i];
                    if (!visibility.FirstPersonActive)
                        visibility.HiddenRenderers[i].enabled = originalHeadStates[i];
                }
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
