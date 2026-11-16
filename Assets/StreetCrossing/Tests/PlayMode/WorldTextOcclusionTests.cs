using System.Collections;
using NUnit.Framework;
using StreetCrossing.Presentation;
using UnityEngine;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class WorldTextOcclusionTests
    {
        [Test]
        public void ChineseUiCanLoadBothLineBreakingTablesBeforeLayout()
        {
            Assert.NotNull(TMPro.TMP_Settings.leadingCharacters);
            Assert.NotNull(TMPro.TMP_Settings.followingCharacters);
            Assert.DoesNotThrow(TMPro.TMP_Settings.LoadLinebreakingRules);
            Assert.IsTrue(TMPro.TMP_Settings.linebreakingRules.leadingCharacters.ContainsKey('，'));
            Assert.IsTrue(TMPro.TMP_Settings.linebreakingRules.followingCharacters.ContainsKey('（'));
        }

        [UnityTest]
        public IEnumerator OpaqueWallOccludesTextAndRemovingWallRestoresIt()
        {
            GameObject root = new GameObject("World text render regression");
            Material surface = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
            RenderTexture target = new RenderTexture(128, 128, 24);
            StreetWorld world = null;
            try
            {
                Font font = Resources.Load<Font>("StreetCrossing/Fonts/StreetUISans");
                Assert.NotNull(font);
                font.RequestCharactersInTexture("M", 80);
                world = new StreetWorld(root.transform, surface, font);
                TextMesh label = world.Label("M", Vector3.zero, .8f, "FFFFFF", root.transform, 0, 1.5f, 1.5f);
                label.gameObject.layer = 31;
                var cameraObject = new GameObject("Isolated text test camera");
                cameraObject.transform.SetParent(root.transform);
                cameraObject.transform.position = new Vector3(0, 0, -3);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = .1f; camera.farClipPlane = 10;
                camera.cullingMask = 1 << 31; camera.targetTexture = target;
                yield return null;
                yield return null;
                Assert.AreEqual("StreetCrossing/WorldText", label.GetComponent<Renderer>().sharedMaterial.shader.name);
                int visible = BrightPixels(camera, target);
                Assert.Greater(visible, 20, "The front-facing label must actually render before testing occlusion.");
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform); wall.layer = 31;
                wall.transform.position = new Vector3(0, 0, -1);
                wall.transform.localScale = new Vector3(3, 3, .2f);
                wall.GetComponent<Renderer>().sharedMaterial = surface;
                Assert.LessOrEqual(BrightPixels(camera, target), 2, "World text must be hidden by an opaque wall.");
                wall.SetActive(false);
                Assert.Greater(BrightPixels(camera, target), visible / 2, "Occlusion must not permanently disable the label.");
            }
            finally
            {
                world?.Dispose();
                Object.Destroy(root); Object.Destroy(surface); Object.Destroy(target);
            }
        }

        private static int BrightPixels(Camera camera, RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                int count = 0;
                foreach (Color32 pixel in pixels.GetPixels32()) if (pixel.r > 80 && pixel.g > 80 && pixel.b > 80) count++;
                return count;
            }
            finally { RenderTexture.active = previous; Object.Destroy(pixels); }
        }
    }
}
