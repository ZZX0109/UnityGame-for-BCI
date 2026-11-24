using System;
using System.IO;
using UnityEngine;

namespace Neuralife.VirtualHand.EditorTests
{
    /// <summary>Builds an isolated Built-in render scene and checks its rendered pixels.</summary>
    internal static class AvatarBlueOutlineRenderTestHelper
    {
        private const int ImageSize = 128;
        private const int FrontSampleX = 46;
        private const int CenterSampleX = 64;
        private const int CenterSampleY = 64;

        private struct Samples
        {
            public Color front;
            public Color center;
        }

        public static void Run()
        {
            GameObject cameraObject = null;
            GameObject avatarObject = null;
            GameObject foregroundObject = null;
            GameObject rearObject = null;
            RenderTexture renderTexture = null;
            Texture2D readback = null;
            Material avatarOriginalMaterial = null;
            Material foregroundMaterial = null;
            Material rearMaterial = null;

            try
            {
                Shader unlitColor = Shader.Find("Unlit/Color");
                if (!unlitColor)
                    throw new InvalidOperationException("The Built-in Unlit/Color shader is unavailable.");

                string evidenceDirectory = Environment.GetEnvironmentVariable("NEURALIFE_BLUE_TEST_DIR");
                if (!string.IsNullOrEmpty(evidenceDirectory))
                    Directory.CreateDirectory(evidenceDirectory);

                cameraObject = new GameObject("Avatar Blue Render Test Camera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = new Vector3(0f, 0f, -6f);
                camera.orthographic = true;
                camera.orthographicSize = 2f;
                camera.aspect = 1f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 20f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;

                renderTexture = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32);
                renderTexture.name = "Avatar Blue Render Test Target";
                renderTexture.antiAliasing = 1;
                renderTexture.Create();
                camera.targetTexture = renderTexture;
                readback = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false, true);

                avatarOriginalMaterial = new Material(unlitColor) { color = Color.green };
                foregroundMaterial = new Material(unlitColor) { color = Color.white };
                rearMaterial = new Material(unlitColor) { color = Color.magenta };

                avatarObject = CreateCube("Avatar Test Cube", Vector3.zero, Vector3.one * 1.4f, avatarOriginalMaterial);
                Renderer avatarRenderer = avatarObject.GetComponent<Renderer>();
                AvatarVisualStyle style = avatarObject.AddComponent<AvatarVisualStyle>();
                style.targets = new Renderer[] { avatarRenderer };
                style.interiorColor = new Color(0.02f, 0.12f, 1f, 1f);
                style.rimColor = new Color(0.2f, 0.9f, 1f, 1f);
                style.interiorAlpha = 0.9f;
                style.rimAlpha = 0.95f;
                style.rimPower = 2f;

                foregroundObject = CreateCube(
                    "Opaque Foreground Occluder",
                    new Vector3(-0.57f, 0f, -1f),
                    new Vector3(0.45f, 0.65f, 0.45f),
                    foregroundMaterial);
                rearObject = CreateCube(
                    "Opaque Rear Occlusion Reference",
                    new Vector3(0f, 0f, 2f),
                    new Vector3(3f, 3f, 0.2f),
                    rearMaterial);

                avatarRenderer.enabled = false;
                Samples rearOnly = Capture(camera, renderTexture, readback, evidenceDirectory, "avatar-blue-rear-baseline.png");
                AssertNearRgb(rearOnly.center, Color.magenta, 0.08f, "Rear object baseline");
                avatarRenderer.enabled = true;

                style.depthPrepass = true;
                style.SetBlueMode(true);
                Samples depthPrepassOn = Capture(camera, renderTexture, readback, evidenceDirectory, "avatar-blue-depth-prepass-on.png");
                AssertNearRgb(depthPrepassOn.front, Color.white, 0.12f, "Foreground cube with depth prepass enabled");
                AssertBlueOverRear(depthPrepassOn.center, rearOnly.center, "Blue avatar with depth prepass enabled");

                style.depthPrepass = false;
                style.RefreshBlueMaterial();
                Samples depthPrepassOff = Capture(camera, renderTexture, readback, evidenceDirectory, "avatar-blue-depth-prepass-off.png");
                AssertNearRgb(depthPrepassOff.front, Color.white, 0.12f, "Foreground cube with depth prepass disabled");
                AssertBlueOverRear(depthPrepassOff.center, rearOnly.center, "Blue avatar with depth prepass disabled");

                style.RestoreOriginalMaterials();
                Samples restored = Capture(camera, renderTexture, readback, evidenceDirectory, "avatar-blue-restored.png");
                AssertNearRgb(restored.center, Color.green, 0.08f, "Restored original avatar material");
            }
            finally
            {
                if (cameraObject)
                {
                    Camera camera = cameraObject.GetComponent<Camera>();
                    if (camera)
                        camera.targetTexture = null;
                }

                DestroyObject(cameraObject);
                DestroyObject(avatarObject);
                DestroyObject(foregroundObject);
                DestroyObject(rearObject);
                DestroyObject(avatarOriginalMaterial);
                DestroyObject(foregroundMaterial);
                DestroyObject(rearMaterial);
                DestroyObject(readback);
                if (renderTexture)
                {
                    renderTexture.Release();
                    DestroyObject(renderTexture);
                }
            }
        }

        private static GameObject CreateCube(string objectName, Vector3 position, Vector3 scale, Material material)
        {
            GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
            instance.name = objectName;
            instance.transform.position = position;
            instance.transform.localScale = scale;
            instance.GetComponent<Renderer>().sharedMaterial = material;
            return instance;
        }

        private static Samples Capture(
            Camera camera,
            RenderTexture renderTexture,
            Texture2D readback,
            string evidenceDirectory,
            string evidenceFileName)
        {
            camera.Render();
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                readback.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0, false);
                readback.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }

            if (!string.IsNullOrEmpty(evidenceDirectory))
            {
                string evidencePath = Path.Combine(evidenceDirectory, evidenceFileName);
                File.WriteAllBytes(evidencePath, readback.EncodeToPNG());
            }

            return new Samples
            {
                front = readback.GetPixel(FrontSampleX, CenterSampleY),
                center = readback.GetPixel(CenterSampleX, CenterSampleY)
            };
        }

        private static void AssertBlueOverRear(Color actual, Color rear, string label)
        {
            if (actual.r >= rear.r - 0.25f || actual.b < rear.b - 0.15f)
            {
                throw new InvalidOperationException(
                    label + " did not produce the expected blue-over-rear pixels. Actual=" + actual + ", rear=" + rear);
            }
        }

        private static void AssertNearRgb(Color actual, Color expected, float tolerance, string label)
        {
            if (Mathf.Abs(actual.r - expected.r) > tolerance ||
                Mathf.Abs(actual.g - expected.g) > tolerance ||
                Mathf.Abs(actual.b - expected.b) > tolerance)
            {
                throw new InvalidOperationException(
                    label + " pixel mismatch. Actual=" + actual + ", expected=" + expected);
            }
        }

        private static void DestroyObject(UnityEngine.Object instance)
        {
            if (instance)
                UnityEngine.Object.Destroy(instance);
        }
    }
}
