using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds the demo scene programmatically: two hand rigs from FBX imports,
    /// studio lighting, props, UI, ReplayController wiring. Run via
    /// Neuralife.VirtualHand.Editor.VirtualHandBuild.GenerateProject.
    /// </summary>
    public class DemoSceneBuilder
    {
        const string ScenePath = "Assets/VirtualHand/Scenes/Demo.unity";
        const string ModelsDir = "Assets/VirtualHand/Models";

        public void BuildAll()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- lighting ----
            GameObject lightGo = new GameObject("KeyLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(45, -30, 0);
            lightGo.AddComponent<UnityEditor.Rendering.Universal.UniversalAdditionalLightData>(); // harmless in builtin if URP absent

            // ---- cameras ----
            var camGo = new GameObject("MainCamera");
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.position = new Vector3(0, 0.35f, 2.2f);
            camGo.tag = "MainCamera";
            var orbit = camGo.AddComponent<OrbitCamera>();

            // ---- hands ----
            GameObject leftRig = InstantiateHand("L", new Vector3(0.25f, 0f, 0.2f), 180f);
            GameObject rightRig = InstantiateHand("R", new Vector3(-0.25f, 0f, 0.2f), 0f);

            // ---- controller wiring ----
            var controllerGo = new GameObject("ReplayController");
            var source = controllerGo.AddComponent<ReplaySource>();
            var replay = controllerGo.AddComponent<ReplayController>();
            var tasks = controllerGo.AddComponent<TaskController>();
            replay.source = source;
            replay.leftHand = leftRig.GetComponentInChildren<HandRigDriver>();
            replay.rightHand = rightRig.GetComponentInChildren<HandRigDriver>();
            replay.leftHand.SetMirrored(true);
            tasks.replay = replay;

            var ui = controllerGo.AddComponent<DemoUI>();
            BuildUI(controllerGo, ref ui);

            if (!Directory.Exists("Assets/VirtualHand/Scenes"))
                Directory.CreateDirectory("Assets/VirtualHand/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[DemoSceneBuilder] scene saved: " + ScenePath);
        }

        GameObject InstantiateHand(string side, Vector3 pos, float yawDeg)
        {
            string fbx = $"{ModelsDir}/Hand_{side}.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (prefab == null) throw new InvalidOperationException("Missing imported FBX: " + fbx);
            var go = (GameObject)UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0, yawDeg, 0));
            go.name = $"HandRig.{side}";
            var driver = go.AddComponent<HandRigDriver>();
            driver.SetMirrored(side == "L");
            // assign textures
            var renderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard"));
                var diff = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelsDir}/Hand_{side}_diffuse.png");
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelsDir}/Hand_{side}_normal.png");
                var rgh = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelsDir}/Hand_{side}_roughness.png");
                if (diff != null) mat.mainTexture = diff;
                if (nrm != null) mat.EnableKeyword("_NORMALMAP");
                if (rgh != null) { mat.SetTexture("_MetallicGlossMap", rgh); mat.EnableKeyword("_METALLICGLOSSMAP"); }
                renderer.material = mat;
            }
            return go;
        }

        void BuildUI(GameObject root, ref DemoUI ui)
        {
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode =
                UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var statusGo = new GameObject("StatusText");
            statusGo.transform.SetParent(canvasGo.transform);
            var status = statusGo.AddComponent<UnityEngine.UI.Text>();
            status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            status.fontSize = 24; status.color = Color.white;
            status.alignment = TextAnchor.UpperLeft;
            status.rectTransform.anchoredPosition = new Vector2(20, -20);
            status.text = "Game04 虚拟手演示";

            ui.statusText = status;
        }
    }

    /// <summary>Simple orbit camera driven by right-mouse drag; no UI conflict.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Transform focus = new Vector3().x == 0 ? null : null;
        Vector3 _focus = new Vector3(0, 0.3f, 0);
        float _dist = 2.2f, _yaw = 180f, _pitch = 8f;

        void Update()
        {
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * 3f;
                _pitch -= Input.GetAxis("Mouse Y") * 2f;
                _pitch = Mathf.Clamp(_pitch, -30f, 60f);
            }
            _dist *= 1f + Input.GetAxis("Mouse ScrollWheel") * 0.1f;
            _dist = Mathf.Clamp(_dist, 0.8f, 6f);
            var q = Quaternion.Euler(_pitch, _yaw, 0);
            transform.position = _focus + q * new Vector3(0, 0, _dist);
            transform.LookAt(_focus);
        }
    }
}
