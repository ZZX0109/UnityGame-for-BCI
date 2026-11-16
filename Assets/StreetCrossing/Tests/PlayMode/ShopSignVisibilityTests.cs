using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StreetCrossing.Core;
using StreetCrossing.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StreetCrossing.Tests.PlayMode
{
    public sealed class ShopSignVisibilityTests
    {
        [UnityTest]
        public IEnumerator MainShopSignIsFullyVisibleAboveTheRealCanopyAtDestination()
        {
            yield return SceneManager.LoadSceneAsync("StreetCrossingScene", LoadSceneMode.Single);
            StreetGameManager game = Object.FindObjectOfType<StreetGameManager>();
            Assert.NotNull(game);
            GameObject cameraObject = new GameObject("Shop sign visibility regression camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 75; camera.aspect = 16f / 9f;
            camera.nearClipPlane = .05f; camera.farClipPlane = 60;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            RenderTexture target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            Material black = new Material(Shader.Find("Unlit/Color")) { color = Color.black };
            var layers = new Dictionary<GameObject, int>();
            var materials = new Dictionary<Renderer, Material>();
            try
            {
                for (int index = 0; index < 3; index++)
                {
                    game.ReturnToMenu(); game.SelectScenario(index);
                    yield return null; yield return null; yield return null;
                    TextMesh chinese = game.GetComponentsInChildren<TextMesh>().Single(t => t.text == "街 角 便 利 店");
                    TextMesh english = game.GetComponentsInChildren<TextMesh>().Single(t => t.text == game.scenario.visualProfile.shopCaption);
                    Renderer backing = game.GetComponentsInChildren<Renderer>().Single(r => r.name == "Shop sign backing");
                    Renderer[] canopy = game.GetComponentsInChildren<Renderer>().Where(r => r.name == "Striped awning" || r.name == "Awning stripe").ToArray();
                    Assert.AreEqual(12, canopy.Length);
                    Vector3 eye = new Vector3(0, 1.675f, game.scenario.destination.z - .9f);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.identity);
                    // The whole backing clears the projection, rather than placing letters in front of a clipping canopy.
                    foreach (Renderer piece in canopy)
                    {
                        float t = (piece.bounds.min.z - eye.z) / (backing.bounds.min.z - eye.z);
                        float lineHeight = Mathf.Lerp(eye.y, backing.bounds.min.y, t);
                        Assert.Greater(lineHeight, piece.bounds.max.y + .10f, "The complete sign needs ten centimetres of sightline clearance at the canopy front.");
                        layers.Add(piece.gameObject, piece.gameObject.layer); piece.gameObject.layer = 31;
                        materials.Add(piece, piece.sharedMaterial); piece.sharedMaterial = black;
                    }
                    foreach (TextMesh text in new[] { chinese, english })
                    {
                        Renderer renderer = text.GetComponent<Renderer>();
                        Assert.AreEqual("StreetCrossing/WorldText", renderer.sharedMaterial.shader.name);
                        Bounds bounds = renderer.bounds;
                        foreach (Vector3 point in new[] { bounds.min, bounds.max })
                        {
                            Vector3 viewport = camera.WorldToViewportPoint(point);
                            Assert.Greater(viewport.z, 0);
                            Assert.That(viewport.x, Is.InRange(0f, 1f), "The destination view must contain the full sign width.");
                            Assert.That(viewport.y, Is.InRange(0f, 1f), "The destination view must contain the full sign height at the smallest supported FOV.");
                        }
                        layers.Add(text.gameObject, text.gameObject.layer); text.gameObject.layer = 31;
                        foreach (Renderer piece in canopy) piece.enabled = false;
                        int unobstructed = BrightPixels(camera, target);
                        Assert.Greater(unobstructed, 40, "Real sign glyphs must render before comparing canopy occlusion.");
                        foreach (Renderer piece in canopy) piece.enabled = true;
                        int withCanopy = BrightPixels(camera, target);
                        Assert.GreaterOrEqual(withCanopy, unobstructed * .99f, "The real canopy may not cut away the visible sign glyphs.");
                        text.gameObject.layer = layers[text.gameObject]; layers.Remove(text.gameObject);
                    }
                    foreach (var entry in layers) entry.Key.layer = entry.Value;
                    foreach (var entry in materials) entry.Key.sharedMaterial = entry.Value;
                    layers.Clear(); materials.Clear();
                }
            }
            finally
            {
                foreach (var entry in layers) if (entry.Key) entry.Key.layer = entry.Value;
                foreach (var entry in materials) if (entry.Key) { entry.Key.sharedMaterial = entry.Value; entry.Key.enabled = true; }
                Object.Destroy(cameraObject); Object.Destroy(target); Object.Destroy(black);
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
                foreach (Color32 pixel in pixels.GetPixels32()) if (pixel.r > 100 && pixel.g > 100 && pixel.b > 100) count++;
                return count;
            }
            finally { RenderTexture.active = previous; Object.Destroy(pixels); }
        }
    }
}
