using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ClawMachine.Tests.PlayMode
{
    public sealed class ClawCameraSmokeTests
    {
        private GameObject root;
        private CraneCameraController controller;
        private Camera[] views;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (root) Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pip_OwnsOneRenderCameraAndCleansEveryToggleQualityDisableAndDestroy()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("PIP RenderTexture lifetime requires an actual Unity graphics device.");
            CreateFixture();
            controller.SetPipEnabled(true);
            yield return null;
            Assert.IsTrue(controller.PipEnabled);
            Assert.IsNotNull(controller.PipTexture);
            Assert.IsTrue(controller.PipTexture.IsCreated());
            Assert.AreEqual(320, controller.PipTexture.width);
            Assert.AreEqual(240, controller.PipTexture.height);
            Assert.AreEqual(4, root.GetComponentsInChildren<Camera>(true).Length);
            Assert.AreEqual(0, root.GetComponentsInChildren<AudioListener>(true).Length, "A render-only PIP camera must never add a listener.");
            Camera owned = OwnedCamera();
            Assert.AreEqual(controller.PipTexture, owned.targetTexture);
            Assert.AreEqual(new Rect(0, 0, 1, 1), owned.rect);
            Assert.Less(owned.fieldOfView, views[0].fieldOfView, "The inset must frame the work area rather than shrink the full cabinet.");
            foreach (Camera view in views) Assert.IsNull(view.targetTexture);
            controller.Select(2);
            controller.OrbitBy(new Vector2(140, 25));
            yield return null;
            Assert.AreEqual(views[0], controller.CurrentCamera);
            Assert.AreEqual("侧面辅助", controller.PipViewName);
            Assert.IsTrue(views[0].enabled); Assert.IsFalse(views[1].enabled); Assert.IsFalse(views[2].enabled);
            Assert.AreEqual(views[1].transform.position, owned.transform.position,
                "The fixed side inset must not spin when the main view orbits.");

            RenderTexture low = controller.PipTexture;
            controller.SetPipQuality(false);
            yield return null;
            Assert.IsTrue(low == null, "The replaced low-resolution target must be destroyed.");
            Assert.AreEqual(480, controller.PipTexture.width);
            Assert.AreEqual(360, controller.PipTexture.height);
            Assert.AreEqual(4, root.GetComponentsInChildren<Camera>(true).Length);

            RenderTexture standard = controller.PipTexture;
            controller.SetPipEnabled(false);
            Assert.IsFalse(controller.PipEnabled); Assert.IsNull(controller.PipTexture);
            yield return null;
            Assert.IsTrue(standard == null);
            Assert.AreEqual(3, root.GetComponentsInChildren<Camera>(true).Length);

            controller.SetPipEnabled(true);
            controller.enabled = false;
            Assert.IsFalse(controller.PipEnabled); Assert.IsNull(controller.PipTexture);
            yield return null;
            Assert.AreEqual(3, root.GetComponentsInChildren<Camera>(true).Length);
            controller.enabled = true;
            yield return null;
            Assert.IsTrue(controller.PipEnabled, "Reenable restores the requested PIP setting with new owned resources.");
            RenderTexture restored = controller.PipTexture;
            Object.Destroy(root); root = null;
            yield return null;
            Assert.IsTrue(restored == null);
        }

        private void CreateFixture()
        {
            root = new GameObject("PIP lifetime fixture"); root.SetActive(false);
            var target = new GameObject("Target"); target.transform.SetParent(root.transform, false);
            views = new Camera[3];
            Vector3[] positions = { new Vector3(0, 0.4f, -3.45f), new Vector3(-3.45f, 0.4f, 0), new Vector3(3.45f, 0.4f, 0) };
            for (int i = 0; i < 3; i++)
            {
                var cameraObject = new GameObject("Preset " + i); cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.position = positions[i]; views[i] = cameraObject.AddComponent<Camera>();
            }
            controller = root.AddComponent<CraneCameraController>();
            typeof(CraneCameraController).GetField("views", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, views);
            typeof(CraneCameraController).GetField("lookTarget", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, target.transform);
            root.SetActive(true);
        }
        private Camera OwnedCamera()
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                if (camera.targetTexture == controller.PipTexture) return camera;
            Assert.Fail("Missing owned PIP camera."); return null;
        }

        [UnityTest]
        public IEnumerator ShowroomFill_LightsFacesFromSeveralDirectionsWithoutAddingShadowOrLeakingOnToggle()
        {
            root = new GameObject("Showroom fill ownership fixture");
            root.SetActive(false);
            ClawShowroomLighting lighting = root.AddComponent<ClawShowroomLighting>();
            root.SetActive(true);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                yield return null;
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                Assert.AreEqual(3, lights.Length, "The key plus three fills fit a four-light forward rendering budget.");
                bool above = false, front = false, opposite = false;
                foreach (Light light in lights)
                {
                    Assert.AreEqual(LightType.Point, light.type);
                    Assert.AreEqual(LightShadows.None, light.shadows);
                    Assert.AreEqual(LightRenderMode.ForcePixel, light.renderMode);
                    Assert.That(light.intensity, Is.InRange(0.45f, 0.9f), "Fill should lift dark faces without overpowering the key.");
                    float lowest = Mathf.Min(light.color.r, Mathf.Min(light.color.g, light.color.b));
                    float highest = Mathf.Max(light.color.r, Mathf.Max(light.color.g, light.color.b));
                    Assert.LessOrEqual(highest - lowest, 0.08f, "Fill must not tint the three prize colors.");
                    Vector3 position = light.transform.localPosition;
                    above |= position.y > 1.75f;
                    front |= position.z < -0.6f;
                    opposite |= position.z > 0.35f;
                }
                Assert.IsTrue(above && front && opposite, "Claw and toy faces need top, front and opposite-side coverage.");
                Assert.IsNotNull(lighting.Probe);
                Assert.AreEqual(1, root.GetComponentsInChildren<ReflectionProbe>(true).Length);
                ReflectionProbe previous = lighting.Probe;
                lighting.enabled = false;
                yield return null;
                Assert.IsNull(lighting.Probe);
                Assert.IsTrue(previous == null);
                Assert.AreEqual(0, root.GetComponentsInChildren<Light>(true).Length);
                lighting.enabled = true;
            }
        }
    }
}
