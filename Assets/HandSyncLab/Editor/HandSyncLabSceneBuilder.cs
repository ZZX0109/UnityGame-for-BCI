using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using HandSyncLab;

namespace HandSyncLab.Editor
{
    /// <summary>
    /// Builds the Hand Sync Lab scene: a quiet studio room with the HandLabRig
    /// bootstrap, one inspection camera, neutral lighting and scale anchors.
    /// Deliberately free of street, traffic or menu content. The scene is created
    /// on demand (menu or the PICO lab build entries); rebuilding overwrites it.
    /// </summary>
    public static class HandSyncLabSceneBuilder
    {
        public const string ScenePath = "Assets/HandSyncLab/Scenes/HandSyncLab.unity";

        [MenuItem("Neuralife/Hand Sync Lab/Build Lab Scene")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.52f, 0.56f);
            RenderSettings.fog = false;

            var lightGO = new GameObject("Key Light", typeof(Light));
            Light light = lightGO.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            CreateFloor();
            CreateScaleAnchors();

            var cameraGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            Camera view = cameraGO.GetComponent<Camera>();
            cameraGO.tag = "MainCamera";
            cameraGO.transform.position = new Vector3(0f, 1.6f, -2.2f);
            cameraGO.transform.rotation = Quaternion.identity;
            view.nearClipPlane = 0.05f;
            view.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            view.clearFlags = CameraClearFlags.SolidColor;

            var lab = new GameObject("HandSyncLab", typeof(HandLabRig));

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/HandSyncLab/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[HandSyncLabSceneBuilder] Lab scene written to " + ScenePath);
        }

        /// <summary>Invoked by Neuralife.XR.Editor.PicoProject before a lab build.</summary>
        public static void EnsureScene()
        {
            if (File.Exists(ScenePath))
                return;
            BuildScene();
        }

        static void CreateFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(2f, 1f, 2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.55f, 0.55f, 0.55f)
            };
        }

        /// <summary>Four 1 m posts at ±1.5 m: stable visual anchors for reach/scale judgement.</summary>
        static void CreateScaleAnchors()
        {
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = $"ScaleAnchor{(x < 0 ? "W" : "E")}{(z < 0 ? "S" : "N")}";
                post.transform.position = new Vector3(x * 1.5f, 0.5f, z * 1.5f);
                post.transform.localScale = new Vector3(0.1f, 1f, 0.1f);
                post.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Standard"))
                {
                    color = new Color(0.75f, 0.42f, 0.12f)
                };
            }
        }
    }
}
