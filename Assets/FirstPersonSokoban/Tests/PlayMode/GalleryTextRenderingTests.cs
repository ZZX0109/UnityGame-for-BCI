using System.Collections;
using FirstPersonSokoban.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FirstPersonSokoban.Tests.PlayMode
{
    public sealed class GalleryTextRenderingTests
    {
        [UnityTest]
        public IEnumerator TextRendersInFrontIsOccludedByWallAndRejectsBackFace()
        {
            var root = new GameObject("Gallery text render regression");
            var surface = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
            var target = new RenderTexture(128, 128, 24);
            try
            {
                LabMaterials palette = root.AddComponent<LabMaterials>();
                Font font = Resources.Load<Font>("Fonts/SokobanUISans");
                Assert.NotNull(font);
                font.RequestCharactersInTexture("M", 80);
                var label = new GameObject("Isolated gallery label", typeof(TextMesh));
                label.transform.SetParent(root.transform, false);
                label.layer = 30;
                TextMesh text = label.GetComponent<TextMesh>();
                text.font = font; text.fontSize = 80; text.characterSize = .06f;
                text.text = "M"; text.anchor = TextAnchor.MiddleCenter; text.color = Color.white;
                label.GetComponent<Renderer>().sharedMaterial = palette.WorldText(font);
                var cameraObject = new GameObject("Isolated gallery camera", typeof(Camera));
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1;
                camera.transform.position = new Vector3(0, 0, -3);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.nearClipPlane = .1f; camera.farClipPlane = 10;
                camera.cullingMask = 1 << 30; camera.targetTexture = target;
                yield return null;
                yield return null;
                int visible = BrightPixels(camera, target);
                Assert.Greater(visible, 20, "The front-facing text must actually render.");
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform, false); wall.layer = 30;
                wall.transform.position = new Vector3(0, 0, -1);
                wall.transform.localScale = new Vector3(3, 3, .2f);
                wall.GetComponent<Renderer>().sharedMaterial = surface;
                Assert.LessOrEqual(BrightPixels(camera, target), 2, "An opaque wall must hide gallery text.");
                wall.SetActive(false);
                Assert.Greater(BrightPixels(camera, target), visible / 2, "Removing the wall must restore the text.");
                camera.transform.position = new Vector3(0, 0, 3);
                camera.transform.rotation = Quaternion.Euler(0, 180, 0);
                Assert.LessOrEqual(BrightPixels(camera, target), 2, "The reverse side must not show mirrored text.");
                camera.transform.position = new Vector3(0, 0, -3);
                camera.transform.rotation = Quaternion.identity;
                font.RequestCharactersInTexture("FORM / SPACE 0123456789", 48);
                yield return null;
                Assert.AreSame(font.material.mainTexture, palette.WorldText(font).mainTexture,
                    "A rebuilt dynamic atlas must remain bound to the owned world material.");
                Assert.Greater(BrightPixels(camera, target), visible / 2);
            }
            finally
            {
                Object.Destroy(root); Object.Destroy(surface); Object.Destroy(target);
            }
        }

        private static int BrightPixels(Camera camera, RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                int count = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                    if (pixel.r > 80 && pixel.g > 80 && pixel.b > 80) count++;
                return count;
            }
            finally { RenderTexture.active = previous; Object.Destroy(pixels); }
        }
    }
}
