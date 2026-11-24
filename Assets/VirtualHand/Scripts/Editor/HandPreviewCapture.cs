using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Renders the actual Elena hand prefab and writes PNG evidence for both
    /// appearance modes.
    ///
    /// This is not a mock-up: it instantiates the shipped prefab, assigns the blue
    /// outline shader through the same serialized field the scene builders use, and
    /// renders with a real camera into a RenderTexture.
    ///
    /// Scope note, stated plainly: this captures appearance only. Pose screenshots
    /// are deliberately not produced here, because in edit mode Unity does not
    /// re-evaluate a SkinnedMeshRenderer's skinning after bone transforms change, so
    /// every pose would render as the rest pose and the images would be misleading.
    /// Pose behaviour is covered by the PlayMode tests instead.
    ///
    /// Run without -nographics:
    ///   -executeMethod Neuralife.VirtualHand.Editor.HandPreviewCapture.Capture
    /// </summary>
    public static class HandPreviewCapture
    {
        const int ImageSize = 900;
        const string OutputDir = "Results/hand-previews";
        const string HandPrefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_R.prefab";
        const string BlueShaderPath = "Assets/VirtualHand/Shaders/AvatarBlueOutline.shader";

        [MenuItem("Neuralife/VirtualHand/Capture Hand Previews")]
        public static void Capture()
        {
            Directory.CreateDirectory(OutputDir);

            // Views: a three-quarter view, and a side view that shows whether an
            // opaque object in front still occludes the translucent hand.
            var views = new[]
            {
                (name: "threequarter", direction: new Vector3(0.45f, 0.35f, -1f).normalized),
                (name: "side", direction: new Vector3(0.05f, 0.1f, -1f).normalized)
            };

            foreach (var view in views)
            {
                RenderOne(view.name, view.direction, blue: false);
                RenderOne(view.name, view.direction, blue: true);
            }

            Debug.Log("[HandPreviewCapture] wrote appearance previews to " + OutputDir);
        }

        static void RenderOne(string viewName, Vector3 direction, bool blue)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // An empty scene has no ambient term, which would render the PBR skin
            // almost black. Set a neutral studio ambient so the material is judged on
            // its own values rather than on a lighting accident.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.43f, 0.46f);
            RenderSettings.skybox = null;

            var hand = AssetDatabase.LoadAssetAtPath<GameObject>(HandPrefabPath);
            if (!hand)
                throw new InvalidOperationException("Missing hand prefab: " + HandPrefabPath);

            GameObject instance = null;
            GameObject cameraObject = null;
            var lights = new GameObject[3];
            RenderTexture target = null;
            Texture2D readback = null;

            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(hand);
                instance.transform.position = Vector3.zero;

                var renderer = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (!renderer)
                    throw new InvalidOperationException("Hand prefab has no SkinnedMeshRenderer.");

                if (blue)
                {
                    var style = instance.AddComponent<AvatarVisualStyle>();
                    style.targets = new Renderer[] { renderer };
                    AssignBlueShader(style);
                    style.SetBlueMode(true);
                }

                cameraObject = new GameObject("Preview Camera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f, 1f);
                camera.fieldOfView = 28f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 20f;

                Bounds bounds = renderer.bounds;
                float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
                camera.transform.position = bounds.center + direction * (radius * 3.2f);
                camera.transform.LookAt(bounds.center);

                lights[0] = MakeLight("Key", new Vector3(35f, 145f, 0f), 1.1f, new Color(1f, 0.97f, 0.93f));
                lights[1] = MakeLight("Fill", new Vector3(10f, -60f, 0f), 0.55f, new Color(0.85f, 0.9f, 1f));
                lights[2] = MakeLight("Rim", new Vector3(-25f, 20f, 0f), 0.7f, new Color(0.9f, 0.95f, 1f));

                target = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                readback = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false, false);

                Shoot(camera, target, readback,
                      OutputDir + "/hand-" + (blue ? "blue" : "realistic") + "-" + viewName + ".png");
            }
            finally
            {
                if (cameraObject && cameraObject.GetComponent<Camera>())
                    cameraObject.GetComponent<Camera>().targetTexture = null;
                Destroy(cameraObject);
                foreach (var l in lights) Destroy(l);
                Destroy(instance);
                Destroy(readback);
                if (target) { target.Release(); Destroy(target); }
            }
        }

        static GameObject MakeLight(string name, Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject("Preview Light " + name);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            go.transform.rotation = Quaternion.Euler(euler);
            return go;
        }

        static void Shoot(Camera camera, RenderTexture target, Texture2D readback, string path)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0, false);
                readback.Apply(false, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            File.WriteAllBytes(path, readback.EncodeToPNG());
        }

        static void AssignBlueShader(AvatarVisualStyle style)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(BlueShaderPath);
            if (!shader)
                throw new InvalidOperationException("Missing blue outline shader: " + BlueShaderPath);

            var serialized = new SerializedObject(style);
            var property = serialized.FindProperty("blueShader");
            if (property == null)
                throw new InvalidOperationException("AvatarVisualStyle has no serialized 'blueShader' field.");

            property.objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o) UnityEngine.Object.DestroyImmediate(o);
        }
    }
}