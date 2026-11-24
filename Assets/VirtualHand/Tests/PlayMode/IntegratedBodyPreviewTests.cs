#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class IntegratedBodyPreviewTests
    {
        private const string PrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/FirstPersonHands.prefab";
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 720;

        private sealed class Fixture
        {
            public GameObject sceneRoot;
            public Transform trackingOrigin;
            public Transform trackedHead;
            public Renderer headRenderer;
            public Transform bodyOrigin;
            public ControllerHandPair pair;
            public SeatedArmPreview preview;
            public AvatarBodyCalibrationBinding binding;
            public ContinuousArmSleeve sleeve;
            public GameObject leftOutputRoot;
            public GameObject rightOutputRoot;
            public Camera firstPersonCamera;
            public Camera outsideCamera;
            public RenderTexture captureTarget;
            public ControllerHandPair.GripSample leftSample;
            public ControllerHandPair.GripSample rightSample;
            public readonly List<Material> ownedMaterials = new List<Material>();
        }

        [UnityTest]
        public IEnumerator IntegratedPreviewUsesPrefabSamplesAndTracksLossAndRecovery()
        {
            var fixture = new Fixture();
            string settingsKey = "Test.IntegratedBodyPreview." + Guid.NewGuid().ToString("N");
            AmbientMode previousAmbientMode = RenderSettings.ambientMode;
            Color previousAmbientLight = RenderSettings.ambientLight;
            bool previousFog = RenderSettings.fog;
            Color previousFogColor = RenderSettings.fogColor;
            FogMode previousFogMode = RenderSettings.fogMode;
            float previousFogDensity = RenderSettings.fogDensity;

            try
            {
                PlayerPrefs.DeleteKey(settingsKey);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.IsNotNull(prefab, "The integrated preview must use the Game04 first-person hands prefab.");
                BuildFixture(fixture, prefab, settingsKey);

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.48f, 0.50f, 0.54f, 1f);
                RenderSettings.fog = false;

                fixture.sceneRoot.SetActive(true);
                // Let prefab components enable and settle before supplying the deterministic tracked samples.
                // All sample, loss, recovery, and render operations below are synchronous so Update polling
                // cannot replace the samples between application and inspection.
                yield return null;

                Assert.IsTrue(fixture.binding.ApplyCalibration(), fixture.binding.LastError);
                Assert.AreEqual(AvatarBodyCalibration.PresentationDefaultShoulderWidthMeters,
                    fixture.binding.AppliedCalibration.shoulderWidth.meters, 1e-6f);
                Assert.AreEqual(AvatarBodyCalibration.PresentationDefaultUpperArmLengthMeters,
                    fixture.binding.AppliedCalibration.upperArmLength.meters, 1e-6f);
                Assert.AreEqual(AvatarBodyCalibration.PresentationDefaultForearmLengthMeters,
                    fixture.binding.AppliedCalibration.forearmLength.meters, 1e-6f);
                Assert.AreNotSame(fixture.trackedHead, fixture.bodyOrigin);
                Assert.IsFalse(fixture.trackedHead.IsChildOf(fixture.bodyOrigin));
                Assert.IsFalse(fixture.bodyOrigin.IsChildOf(fixture.trackedHead));

                ApplyAndRefresh(fixture, fixture.leftSample, fixture.rightSample);
                AssertTrackedPose(fixture, fixture.pair.leftWrist, fixture.leftSample, "left");
                AssertTrackedPose(fixture, fixture.pair.rightWrist, fixture.rightSample, "right");
                Assert.IsTrue(fixture.preview.LeftArm.visible, "The tracked left wrist should produce a visible arm solution.");
                Assert.IsTrue(fixture.preview.RightArm.visible, "The tracked right wrist should produce a visible arm solution.");
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.preview.LeftArm.source);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.preview.RightArm.source);
                Assert.IsTrue(fixture.pair.leftWrist.visualRoot.gameObject.activeInHierarchy);
                Assert.IsTrue(fixture.pair.rightWrist.visualRoot.gameObject.activeInHierarchy);

                Renderer leftSleeveRenderer = fixture.sleeve.LeftRenderer;
                Renderer rightSleeveRenderer = fixture.sleeve.RightRenderer;
                Assert.IsNotNull(leftSleeveRenderer);
                Assert.IsNotNull(rightSleeveRenderer);
                Assert.IsTrue(leftSleeveRenderer.enabled, "The left sleeve mesh should render for a tracked wrist.");
                Assert.IsTrue(rightSleeveRenderer.enabled, "The right sleeve mesh should render for a tracked wrist.");
                AssertMeshRootIsIndependent(fixture, leftSleeveRenderer, fixture.pair.leftWrist);
                AssertMeshRootIsIndependent(fixture, rightSleeveRenderer, fixture.pair.rightWrist);
                Assert.That(Vector3.Dot(fixture.bodyOrigin.up, Vector3.up), Is.GreaterThan(0.999f),
                    "The body frame should stay upright when the synthetic head is pitched.");

                fixture.pair.ApplyControllerSamples(default(ControllerHandPair.GripSample), fixture.rightSample);
                RefreshIntegratedPreview(fixture);
                Assert.AreEqual(ControllerHandPair.PoseSource.Hidden, fixture.pair.LeftSource);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.pair.RightSource);
                Assert.IsFalse(fixture.pair.leftWrist.visualRoot.gameObject.activeInHierarchy);
                Assert.IsFalse(fixture.preview.LeftArm.visible);
                Assert.IsFalse(leftSleeveRenderer.enabled);
                Assert.IsTrue(rightSleeveRenderer.enabled);
                AssertTrackedPose(fixture, fixture.pair.rightWrist, fixture.rightSample, "right after left loss");
                AssertSleeveObjectSurvived(fixture, leftSleeveRenderer);
                Assert.AreSame(leftSleeveRenderer, fixture.sleeve.LeftRenderer);

                ApplyAndRefresh(fixture, fixture.leftSample, fixture.rightSample);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.pair.LeftSource);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.pair.RightSource);
                Assert.IsTrue(fixture.preview.LeftArm.visible);
                Assert.IsTrue(fixture.preview.RightArm.visible);
                Assert.IsTrue(leftSleeveRenderer.enabled);
                Assert.IsTrue(rightSleeveRenderer.enabled);
                AssertTrackedPose(fixture, fixture.pair.leftWrist, fixture.leftSample, "left after recovery");
                AssertTrackedPose(fixture, fixture.pair.rightWrist, fixture.rightSample, "right after recovery");

                fixture.binding.headTrackingValid = false;
                Assert.IsFalse(fixture.binding.RefreshBinding(), "Invalid head tracking should disable body outputs.");
                fixture.preview.Refresh();
                fixture.sleeve.Refresh();
                Assert.IsFalse(fixture.preview.LeftArm.visible);
                Assert.IsFalse(fixture.preview.RightArm.visible);
                Assert.IsFalse(leftSleeveRenderer.enabled);
                Assert.IsFalse(rightSleeveRenderer.enabled);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.pair.LeftSource);
                Assert.AreEqual(ControllerHandPair.PoseSource.Controller, fixture.pair.RightSource);
                AssertTrackedPose(fixture, fixture.pair.leftWrist, fixture.leftSample, "left while head tracking is lost");
                AssertTrackedPose(fixture, fixture.pair.rightWrist, fixture.rightSample, "right while head tracking is lost");
                AssertSleeveObjectSurvived(fixture, leftSleeveRenderer);
                AssertSleeveObjectSurvived(fixture, rightSleeveRenderer);

                fixture.binding.headTrackingValid = true;
                Assert.IsTrue(fixture.binding.RefreshBinding(), fixture.binding.LastError);
                fixture.preview.Refresh();
                fixture.sleeve.Refresh();
                Assert.IsTrue(fixture.preview.LeftArm.visible);
                Assert.IsTrue(fixture.preview.RightArm.visible);
                Assert.IsTrue(leftSleeveRenderer.enabled);
                Assert.IsTrue(rightSleeveRenderer.enabled);

                string captureDirectory = Environment.GetEnvironmentVariable("NEURALIFE_BODY_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(captureDirectory))
                    CaptureStagedViews(fixture, captureDirectory);
            }
            finally
            {
                CleanupFixture(fixture, settingsKey);
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.fog = previousFog;
                RenderSettings.fogColor = previousFogColor;
                RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogDensity = previousFogDensity;
            }
        }

        private static void BuildFixture(Fixture fixture, GameObject prefab, string settingsKey)
        {
            fixture.sceneRoot = new GameObject("Integrated body preview test scene");
            fixture.sceneRoot.SetActive(false);

            GameObject trackingObject = new GameObject("Synthetic tracking origin");
            trackingObject.transform.SetParent(fixture.sceneRoot.transform, false);
            fixture.trackingOrigin = trackingObject.transform;

            GameObject headObject = new GameObject("Synthetic tracked head");
            headObject.transform.SetParent(fixture.trackingOrigin, false);
            headObject.transform.localPosition = new Vector3(0f, 1.12f, 0f);
            headObject.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            fixture.trackedHead = headObject.transform;

            Material headMaterial = CreateMaterial(fixture, "Preview head material", new Color(0.72f, 0.52f, 0.39f, 1f));
            GameObject headVisual = CreatePrimitiveVisual("Synthetic head visual", PrimitiveType.Sphere,
                fixture.trackedHead, headMaterial);
            headVisual.transform.localScale = new Vector3(0.22f, 0.24f, 0.22f);
            fixture.headRenderer = headVisual.GetComponent<Renderer>();

            GameObject bodyOriginObject = new GameObject("Independent upright body origin");
            bodyOriginObject.transform.SetParent(fixture.sceneRoot.transform, false);
            fixture.bodyOrigin = bodyOriginObject.transform;

            Material torsoMaterial = CreateMaterial(fixture, "Preview torso material", new Color(0.12f, 0.20f, 0.30f, 1f));
            GameObject torso = CreatePrimitiveVisual("Seated torso", PrimitiveType.Sphere,
                fixture.bodyOrigin, torsoMaterial);
            torso.transform.localPosition = new Vector3(0f, -0.38f, 0.025f);
            torso.transform.localScale = new Vector3(0.60f, 0.54f, 0.34f);

            GameObject prefabInstance = Object.Instantiate(prefab, fixture.trackingOrigin, false);
            fixture.pair = prefabInstance.GetComponent<ControllerHandPair>();
            Assert.IsNotNull(fixture.pair, "The imported prefab must contain ControllerHandPair.");
            Assert.IsNotNull(fixture.pair.leftWrist);
            Assert.IsNotNull(fixture.pair.rightWrist);
            fixture.pair.enabled = false;
            fixture.pair.trackingOrigin = fixture.trackingOrigin;
            fixture.pair.sagittalPlane = fixture.trackingOrigin;
            fixture.pair.leftWrist.wristPositionOffset = Vector3.zero;
            fixture.pair.leftWrist.wristRotationOffsetEuler = Vector3.zero;
            fixture.pair.rightWrist.wristPositionOffset = Vector3.zero;
            fixture.pair.rightWrist.wristRotationOffsetEuler = Vector3.zero;
            SetSeatedHandSettingsKey(fixture.pair, settingsKey);
            fixture.pair.enabled = true;

            fixture.leftSample = new ControllerHandPair.GripSample
            {
                tracked = true,
                grip = 0.18f,
                pose = new Pose(new Vector3(-0.32f, 0.78f, 0.48f), Quaternion.identity)
            };
            fixture.rightSample = new ControllerHandPair.GripSample
            {
                tracked = true,
                grip = 0.18f,
                pose = new Pose(new Vector3(0.32f, 0.78f, 0.48f), Quaternion.identity)
            };

            Transform leftShoulder;
            Transform leftElbow;
            Transform leftWrist;
            fixture.leftOutputRoot = CreateOutputAnchors(fixture.sceneRoot, "Left sleeve output anchors",
                out leftShoulder, out leftElbow, out leftWrist);
            Transform rightShoulder;
            Transform rightElbow;
            Transform rightWrist;
            fixture.rightOutputRoot = CreateOutputAnchors(fixture.sceneRoot, "Right sleeve output anchors",
                out rightShoulder, out rightElbow, out rightWrist);

            GameObject previewObject = new GameObject("Seated arm preview");
            previewObject.transform.SetParent(fixture.sceneRoot.transform, false);
            fixture.preview = previewObject.AddComponent<SeatedArmPreview>();
            fixture.preview.pair = fixture.pair;
            fixture.preview.leftOutputRoot = fixture.leftOutputRoot;
            fixture.preview.rightOutputRoot = fixture.rightOutputRoot;
            fixture.preview.leftShoulderPoint = leftShoulder;
            fixture.preview.leftElbowPoint = leftElbow;
            fixture.preview.leftWristPoint = leftWrist;
            fixture.preview.rightShoulderPoint = rightShoulder;
            fixture.preview.rightElbowPoint = rightElbow;
            fixture.preview.rightWristPoint = rightWrist;
            fixture.preview.leftElbowPoleBodyDirection = Vector3.down;
            fixture.preview.rightElbowPoleBodyDirection = Vector3.down;

            Material sleeveMaterial = CreateMaterial(fixture, "Preview woven sleeve material",
                new Color(0.18f, 0.27f, 0.37f, 1f));
            GameObject sleeveObject = new GameObject("Continuous arm sleeves");
            sleeveObject.transform.SetParent(fixture.sceneRoot.transform, false);
            fixture.sleeve = sleeveObject.AddComponent<ContinuousArmSleeve>();
            fixture.sleeve.preview = fixture.preview;
            fixture.sleeve.mountRoot = fixture.bodyOrigin;
            fixture.sleeve.weaveMaterial = sleeveMaterial;

            GameObject bindingObject = new GameObject("Avatar body calibration binding");
            bindingObject.transform.SetParent(fixture.sceneRoot.transform, false);
            fixture.binding = bindingObject.AddComponent<AvatarBodyCalibrationBinding>();
            fixture.binding.preview = fixture.preview;
            fixture.binding.trackedHead = fixture.trackedHead;
            fixture.binding.headTrackingValid = true;
            fixture.binding.bodyOrigin = fixture.bodyOrigin;
            fixture.binding.bodyOriginOffsetFromTrackedHeadMeters = Vector3.zero;
            fixture.binding.calibration = AvatarBodyCalibration.CreatePresentationDefaults();

            Material groundMaterial = CreateMaterial(fixture, "Preview ground material", new Color(0.30f, 0.32f, 0.34f, 1f));
            GameObject ground = CreatePrimitiveVisual("Ground plane", PrimitiveType.Plane,
                fixture.sceneRoot.transform, groundMaterial);
            ground.transform.position = Vector3.zero;

            GameObject lightObject = new GameObject("Preview key light");
            lightObject.transform.SetParent(fixture.sceneRoot.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.15f;
            keyLight.color = new Color(1f, 0.93f, 0.82f, 1f);
            keyLight.shadows = LightShadows.Soft;

            fixture.firstPersonCamera = CreateCamera("First person preview camera", fixture.trackedHead);
            fixture.firstPersonCamera.transform.localPosition = Vector3.zero;
            fixture.firstPersonCamera.transform.localRotation = Quaternion.identity;

            fixture.outsideCamera = CreateCamera("Outside preview camera", fixture.sceneRoot.transform);
            fixture.outsideCamera.transform.position = new Vector3(2.25f, 1.62f, 2.85f);
            fixture.outsideCamera.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 0.76f, 0.16f) - fixture.outsideCamera.transform.position, Vector3.up);

            fixture.captureTarget = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "Integrated body preview 1280x720 capture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
        }

        private static void ApplyAndRefresh(Fixture fixture,
            ControllerHandPair.GripSample left, ControllerHandPair.GripSample right)
        {
            fixture.pair.ApplyControllerSamples(left, right);
            RefreshIntegratedPreview(fixture);
        }

        private static void RefreshIntegratedPreview(Fixture fixture)
        {
            Assert.IsTrue(fixture.binding.RefreshBinding(), fixture.binding.LastError);
            fixture.preview.Refresh();
            fixture.sleeve.Refresh();
        }

        private static void AssertTrackedPose(Fixture fixture, FirstPersonWrist wrist,
            ControllerHandPair.GripSample sample, string label)
        {
            Vector3 expectedPosition = fixture.trackingOrigin.TransformPoint(sample.pose.position) +
                                      (fixture.trackingOrigin.rotation * sample.pose.rotation) * wrist.wristPositionOffset;
            Assert.That(Vector3.Distance(wrist.transform.position, expectedPosition), Is.LessThan(0.0001f),
                "The " + label + " wrist should remain driven by its applied controller sample.");
        }

        private static void AssertMeshRootIsIndependent(Fixture fixture, Renderer renderer, FirstPersonWrist wrist)
        {
            Assert.IsNotNull(renderer);
            Transform meshRoot = renderer.transform;
            Assert.AreNotSame(wrist.transform, meshRoot);
            Assert.IsFalse(meshRoot.IsChildOf(wrist.transform));
            Assert.IsFalse(wrist.transform.IsChildOf(meshRoot));
            Assert.IsTrue(meshRoot.IsChildOf(fixture.bodyOrigin));
        }

        private static void AssertSleeveObjectSurvived(Fixture fixture, Renderer renderer)
        {
            Assert.IsNotNull(renderer);
            Assert.IsTrue(renderer.gameObject);
            Assert.IsTrue(renderer.gameObject.activeSelf,
                "Sleeve mesh roots should remain active when their preview anchors are hidden.");
            Assert.IsTrue(renderer.transform.IsChildOf(fixture.bodyOrigin),
                "Sleeve meshes should stay on the independent body mount.");
        }

        private static GameObject CreateOutputAnchors(GameObject sceneRoot, string name,
            out Transform shoulder, out Transform elbow, out Transform wrist)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(sceneRoot.transform, false);
            root.SetActive(false);
            shoulder = CreateAnchor(root.transform, name + " shoulder");
            elbow = CreateAnchor(root.transform, name + " elbow");
            wrist = CreateAnchor(root.transform, name + " wrist");
            return root;
        }

        private static Transform CreateAnchor(Transform parent, string name)
        {
            GameObject anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            return anchor.transform;
        }

        private static GameObject CreatePrimitiveVisual(string name, PrimitiveType type,
            Transform parent, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            Collider collider = visual.GetComponent<Collider>();
            if (collider)
                Object.DestroyImmediate(collider);
            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer)
                renderer.sharedMaterial = material;
            return visual;
        }

        private static Material CreateMaterial(Fixture fixture, string name, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (!shader)
                shader = Shader.Find("Diffuse");
            if (!shader)
                shader = Shader.Find("Unlit/Color");
            Assert.IsNotNull(shader, "A Built-in render pipeline shader is required for the preview stage.");

            Material material = new Material(shader) { name = name, color = color };
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.22f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            fixture.ownedMaterials.Add(material);
            return material;
        }

        private static Camera CreateCamera(string name, Transform parent)
        {
            GameObject cameraObject = new GameObject(name);
            cameraObject.transform.SetParent(parent, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.16f, 0.21f, 1f);
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.035f;
            camera.farClipPlane = 50f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.enabled = false;
            return camera;
        }

        private static void SetSeatedHandSettingsKey(ControllerHandPair pair, string key)
        {
            SerializedObject serialized = new SerializedObject(pair);
            SerializedProperty property = serialized.FindProperty("seatedHandSettingsPlayerPrefsKey");
            Assert.IsNotNull(property, "The prefab should expose its seated hand settings key to the editor.");
            property.stringValue = key;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CaptureStagedViews(Fixture fixture, string directory)
        {
            Directory.CreateDirectory(directory);
            try
            {
                fixture.headRenderer.enabled = false;
                CaptureCamera(fixture.firstPersonCamera, fixture.captureTarget,
                    Path.Combine(directory, "integrated-body-preview-first-person.png"));

                fixture.headRenderer.enabled = true;
                CaptureCamera(fixture.outsideCamera, fixture.captureTarget,
                    Path.Combine(directory, "integrated-body-preview-outside.png"));
                var targets = new List<Renderer>(fixture.pair.GetComponentsInChildren<Renderer>(true));
                targets.Add(fixture.sleeve.LeftRenderer);
                targets.Add(fixture.sleeve.RightRenderer);
                targets.Add(fixture.headRenderer);
                targets.AddRange(fixture.bodyOrigin.GetComponentsInChildren<Renderer>(true));
                var style = fixture.sceneRoot.AddComponent<AvatarVisualStyle>();
                style.targets = targets.ToArray();
                style.SetBlueMode(true);
                fixture.headRenderer.enabled = false;
                CaptureCamera(fixture.firstPersonCamera, fixture.captureTarget,
                    Path.Combine(directory, "integrated-body-blue-first-person.png"));
                fixture.headRenderer.enabled = true;
                CaptureCamera(fixture.outsideCamera, fixture.captureTarget,
                    Path.Combine(directory, "integrated-body-blue-outside.png"));
                style.SetBlueMode(false);
            }
            finally
            {
                fixture.headRenderer.enabled = true;
                fixture.firstPersonCamera.targetTexture = null;
                fixture.outsideCamera.targetTexture = null;
            }
        }

        private static void CaptureCamera(Camera camera, RenderTexture target, string path)
        {
            RenderTexture previousTarget = RenderTexture.active;
            Texture2D image = null;
            try
            {
                if (!target.IsCreated())
                    target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, CaptureWidth, CaptureHeight), 0, 0, false);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                if (image)
                    Object.DestroyImmediate(image);
            }
        }

        private static void CleanupFixture(Fixture fixture, string settingsKey)
        {
            if (fixture.sceneRoot)
                fixture.sceneRoot.SetActive(false);

            if (fixture.sleeve)
            {
                if (fixture.sleeve.LeftMesh)
                    Object.DestroyImmediate(fixture.sleeve.LeftMesh);
                if (fixture.sleeve.RightMesh)
                    Object.DestroyImmediate(fixture.sleeve.RightMesh);
            }

            if (fixture.captureTarget)
            {
                if (fixture.captureTarget.IsCreated())
                    fixture.captureTarget.Release();
                Object.DestroyImmediate(fixture.captureTarget);
            }

            if (fixture.sceneRoot)
                Object.DestroyImmediate(fixture.sceneRoot);

            for (int i = 0; i < fixture.ownedMaterials.Count; i++)
                if (fixture.ownedMaterials[i])
                    Object.DestroyImmediate(fixture.ownedMaterials[i]);

            PlayerPrefs.DeleteKey(settingsKey);
        }
    }
}
#endif
