using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>
    /// Builds the demo scene programmatically: two hand rigs from FBX imports,
    /// studio lighting, table support under the task props, task anchors/target,
    /// full UGUI (EventSystem, replay source buttons with PERSISTENT bindings via
    /// ReplayUIBindings, warning/progress), ReplayController/TaskController wiring.
    /// Run via Neuralife.VirtualHand.Editor.VirtualHandBuild.GenerateProject.
    /// </summary>
    public class DemoSceneBuilder
    {
        const string ScenePath = "Assets/VirtualHand/Scenes/Demo.unity";
        const float BoxSide = 0.12f;
        const float TableTopY = 0f;        // table surface the props rest on

        public void BuildAll()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- lighting: shared studio rig (key/fill/rim + explicit
            // Trilight ambient); CaptureRigPreview calls the same helper so
            // product and preview lighting stay identical ----
            VirtualHandStudioLighting.Create();

            // ---- cameras ----
            var camGo = new GameObject("MainCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.rect = new Rect(360f / 1280f, 0f, 1f - 360f / 1280f, 1f);
            cam.nearClipPlane = 0.04f;
            cam.farClipPlane = 25f;
            cam.tag = "MainCamera";
            cam.fieldOfView = 38f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.20f, 0.24f, 0.25f);
            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.FrameHands();

            // ---- hands ----
            // Keep the two task anchors at their established grip offsets. The
            // prefab builder aligns each visual wrist above its anchor at the
            // center height of the 12 cm box and turns its measured hand axis inward.
            Vector3 leftGripPosition = new Vector3(0.25f, TableTopY + BoxSide * 0.5f, 0f);
            Vector3 rightGripPosition = new Vector3(-0.25f, TableTopY + BoxSide * 0.5f, 0f);
            GameObject leftRig = InstantiateHand("L", leftGripPosition, Vector3.left);
            GameObject rightRig = InstantiateHand("R", rightGripPosition, Vector3.right);

            // ---- support: table under the props + floor (DHS finding 3) ----
            CreateTable();
            CreateFloor();

            // ---- task props: anchors start AT the hand roots; each anchor carries
            // its hand rig (parented below) so anchor motion drives the real hand
            // model root, not just a flying empty (integration review) ----
            var leftAnchor = CreateAnchor("TaskAnchor.Left", leftGripPosition);
            var rightAnchor = CreateAnchor("TaskAnchor.Right", rightGripPosition);
            leftRig.transform.SetParent(leftAnchor.transform, worldPositionStays: true);
            rightRig.transform.SetParent(rightAnchor.transform, worldPositionStays: true);
            var box = CreateTaskBox(new Vector3(0f, TableTopY + BoxSide * 0.5f, 0f));
            var dropZone = CreateDropZone(new Vector3(0f, TableTopY + 0.002f, -0.6f));

            // ---- controller wiring ----
            var controllerGo = new GameObject("ReplayController");
            var source = controllerGo.AddComponent<ReplaySource>();
            var replay = controllerGo.AddComponent<ReplayController>();
            var tasks = controllerGo.AddComponent<TaskController>();
            controllerGo.AddComponent<StandaloneDiagnostics>();
            replay.source = source;
            replay.leftHand = leftRig.GetComponentInChildren<HandRigDriver>();
            replay.rightHand = rightRig.GetComponentInChildren<HandRigDriver>();
            replay.leftHand.SetMirrored(true);
            // These prefab drivers use only the sampled native Elena rig profile.
            if (replay.leftHand == null || replay.rightHand == null ||
                replay.leftHand.nativeProfile == null || replay.rightHand.nativeProfile == null)
                throw new InvalidOperationException("Elena FF hand prefabs must include bound native rig profiles.");
            replay.leftHand.profileJson = null;
            replay.rightHand.profileJson = null;
            tasks.replay = replay;
            tasks.leftAnchor = leftAnchor.transform;
            tasks.rightAnchor = rightAnchor.transform;
            tasks.dropZone = dropZone.transform;
            orbit.tasks = tasks;
            orbit.box = box.transform;
            tasks.boxVisual = box.GetComponent<Renderer>();
            tasks.zoneVisual = dropZone.GetComponent<Renderer>();
            tasks.boxVisual.enabled = tasks.zoneVisual.enabled = false;
            // per-hand grip points around the box, matched to where each rig
            // actually stands (L rig at +x, R rig at -x)
            tasks.leftGripOffset = new Vector3(0.25f, 0f, 0f);
            tasks.rightGripOffset = new Vector3(-0.25f, 0f, 0f);
            tasks.leftHand = replay.leftHand;
            tasks.rightHand = replay.rightHand;
            // Sample the saved native skin once in the editor and serialize a
            // bounded task-only curl, wrist offset, and rotation for both sides.
            tasks.CalibrateTaskHoldFromGeometry();

            var ui = controllerGo.AddComponent<DemoUI>();
            ui.replay = replay;
            ui.tasks = tasks;
            BuildUI(controllerGo, ref ui, tasks);
            orbit.sidebar = ui.statusText.transform.parent.GetComponent<RectTransform>();

            if (!Directory.Exists("Assets/VirtualHand/Scenes"))
                Directory.CreateDirectory("Assets/VirtualHand/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Keep the generated scene loadable by name in PlayMode and in the
            // standalone player, without dropping any pre-existing build scenes.
            var buildScenes = EditorBuildSettings.scenes.ToList();
            if (!buildScenes.Any(s => s.path == ScenePath))
                buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[DemoSceneBuilder] build scenes: " +
                      string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));

            Debug.Log("[DemoSceneBuilder] scene saved: " + ScenePath);
        }

        GameObject InstantiateHand(string side, Vector3 gripPosition, Vector3 inwardDirection)
        {
            string prefabPath = "Assets/VirtualHand/Prefabs/ElenaFF/ElenaHand_" + side + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Missing generated Elena FF hand prefab: " + prefabPath);
            var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (go == null)
                throw new InvalidOperationException("Could not instantiate Elena FF hand prefab: " + prefabPath);
            go.name = "HandRig." + side;

            var driver = go.GetComponentInChildren<HandRigDriver>(true);
            if (driver == null || driver.nativeProfile == null)
                throw new InvalidOperationException("Elena FF prefab has no native HandRigDriver profile: " + prefabPath);
            driver.profileJson = null;
            driver.SetMirrored(side == "L");
            driver.IgnoreGlobalHandProportions = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(driver);

            Transform wrist = FindNativeWrist(go.transform);
            Transform middleTip = FindNamedTransform(go.transform, "middle_tip");
            if (wrist == null || middleTip == null)
                throw new InvalidOperationException("Elena FF prefab is missing its wrist or middle-tip transform: " + prefabPath);

            go.transform.rotation = Quaternion.identity;
            Vector3 wristLocal = go.transform.InverseTransformPoint(wrist.position);
            Vector3 tipLocal = go.transform.InverseTransformPoint(middleTip.position);
            Vector3 handAxis = Vector3.Scale(tipLocal - wristLocal, go.transform.localScale).normalized;
            if (handAxis.sqrMagnitude < 1e-8f || inwardDirection.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException("Could not determine Elena FF hand axis for placement: " + prefabPath);

            Quaternion inwardRotation = Quaternion.FromToRotation(handAxis, inwardDirection.normalized);
            go.transform.rotation = inwardRotation;
            go.transform.position += gripPosition - wrist.position;
            return go;
        }

        static Transform FindNativeWrist(Transform root) => FindNamedTransform(root, "hand.R_02");

        static Transform FindNamedTransform(Transform root, string contains)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            return null;
        }

        /// <summary>Table top under the box so the dynamic rigidbody rests instead of
        /// falling before the task starts (DHS finding 3).</summary>
        static void CreateTable()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Table";
            go.transform.position = new Vector3(0f, TableTopY - 0.05f, -0.3f);
            // Extend the visible work surface beyond the frame while retaining the support collider.
            go.transform.localScale = new Vector3(4f, 0.1f, 4f);
            go.GetComponent<BoxCollider>().size = new Vector3(1.2f / 4f, 1f, 1f / 4f);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = new Material(Shader.Find("Standard"))
            { color = new Color(0.20f, 0.27f, 0.28f) };
        }

        static void CreateFloor()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Floor";
            go.transform.position = new Vector3(0f, -0.001f + TableTopY - 0.6f, 0f);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = new Material(Shader.Find("Standard"))
            { color = new Color(0.25f, 0.25f, 0.28f) };
        }

        static GameObject CreateAnchor(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            return go;
        }

        static GameObject CreateTaskBox(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CarryBox";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * BoxSide;
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard"))
            { color = new Color(0.56f, 0.36f, 0.19f) };
            go.AddComponent<Rigidbody>();   // dynamic at rest on the table; task captures it
            return go;
        }

        static GameObject CreateDropZone(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "DropZone";
            // A flat visual target. The table is the physical support beneath it.
            go.GetComponent<Collider>().enabled = false;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.6f, 0.005f, 0.6f);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = new Material(Shader.Find("Standard"))
            { color = new Color(0.20f, 0.41f, 0.36f) };
            return go;
        }

        void BuildUI(GameObject root, ref DemoUI ui, TaskController tasks)
        {
            // EventSystem is mandatory for clickable UGUI
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }

            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var canvasScaler = canvasGo.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1280f, 720f);
            canvasScaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var sidebarGo = new GameObject("Sidebar", typeof(RectTransform), typeof(Image));
            sidebarGo.transform.SetParent(canvasGo.transform, false);
            var sidebarRect = sidebarGo.GetComponent<RectTransform>();
            sidebarRect.anchorMin = new Vector2(0f, 0f);
            sidebarRect.anchorMax = new Vector2(0f, 1f);
            sidebarRect.pivot = new Vector2(0f, 0.5f);
            sidebarRect.anchoredPosition = Vector2.zero;
            sidebarRect.sizeDelta = new Vector2(360f, 0f);
            sidebarGo.GetComponent<Image>().color = new Color(0.055f, 0.075f, 0.10f, 0.98f);

            var title = CreateText(sidebarGo, "SidebarTitle", "虚拟手动作回放",
                new Vector2(20f, -14f), new Vector2(320f, 34f), 21, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            var status = CreateText(sidebarGo, "StatusText", "Game04 虚拟手演示",
                new Vector2(20f, -54f), new Vector2(320f, 82f), 14, TextAnchor.UpperLeft);
            var warning = CreateText(sidebarGo, "WarningText", "",
                new Vector2(20f, -140f), new Vector2(320f, 42f), 13, TextAnchor.UpperLeft);
            warning.color = new Color(1f, 0.66f, 0.30f);

            // Existing button names and serialized bindings are retained; all controls
            // use top-left anchors inside the fixed-width, resolution-scaled sidebar.
            CreateText(sidebarGo, "ReplayGroupLabel", "回放来源", new Vector2(20f, -188f),
                new Vector2(320f, 22f), 15, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            var db8Btn = CreateButton(sidebarGo, "BtnDB8", "DB8 姿态",
                new Vector2(20f, -216f), new Vector2(100f, 36f));
            var eegBtn = CreateButton(sidebarGo, "BtnEEG", "EEG 预测",
                new Vector2(130f, -216f), new Vector2(100f, 36f));
            var labelsBtn = CreateButton(sidebarGo, "BtnLabels", "明确标签",
                new Vector2(240f, -216f), new Vector2(100f, 36f));
            var subject1Btn = CreateButton(sidebarGo, "BtnSubjectS001", "S001",
                new Vector2(20f, -260f), new Vector2(100f, 32f));
            var subject2Btn = CreateButton(sidebarGo, "BtnSubjectS002", "S002",
                new Vector2(130f, -260f), new Vector2(100f, 32f));
            var subject3Btn = CreateButton(sidebarGo, "BtnSubjectS003", "S003",
                new Vector2(240f, -260f), new Vector2(100f, 32f));

            CreateText(sidebarGo, "Db8GroupLabel", "DB8 手套姿态", new Vector2(20f, -302f),
                new Vector2(320f, 22f), 15, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            CreateDb8ClipMenu(sidebarGo);

            CreateText(sidebarGo, "PlaybackGroupLabel", "播放控制", new Vector2(20f, -462f),
                new Vector2(320f, 22f), 15, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            var playBtn = CreateButton(sidebarGo, "BtnPlayPause", "播放 / 暂停",
                new Vector2(20f, -488f), new Vector2(150f, 36f));
            var restartBtn = CreateButton(sidebarGo, "BtnRestart", "重播",
                new Vector2(190f, -488f), new Vector2(150f, 36f));

            CreateText(sidebarGo, "TaskGroupLabel", "协作任务", new Vector2(20f, -536f),
                new Vector2(320f, 22f), 15, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            var carryBtn = CreateButton(sidebarGo, "BtnCarry", "协作搬运",
                new Vector2(20f, -562f), new Vector2(320f, 36f));
            var hoffL = CreateButton(sidebarGo, "BtnHandoffL2R", "交接：左 → 右",
                new Vector2(20f, -606f), new Vector2(150f, 36f));
            var hoffR = CreateButton(sidebarGo, "BtnHandoffR2L", "交接：右 → 左",
                new Vector2(190f, -606f), new Vector2(150f, 36f));

            // ---- progress bar ----
            // A plain new GameObject has only a Transform: GetComponent would
            // return null (the rev-6 NRE), so create it explicitly with the
            // RectTransform that Slider requires.
            var sliderGo = new GameObject("Progress", typeof(RectTransform));
            sliderGo.transform.SetParent(sidebarGo.transform, false);
            var srt = sliderGo.GetComponent<RectTransform>();
            var slider = sliderGo.AddComponent<Slider>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(320f, 12f);
            srt.anchoredPosition = new Vector2(20f, -666f);
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            CreateFillOnlySlider(sliderGo);

            ui.statusText = status;
            ui.warningText = warning;
            ui.progress = slider;

            // persistent bindings (survive scene save/reload; DHS finding 1):
            // only serialized references — task entries are concrete runtime
            // methods on ReplayUIBindings over its serialized TaskController ref
            var bindings = root.AddComponent<ReplayUIBindings>();
            bindings.ui = ui;
            bindings.tasks = tasks;
            bindings.db8Button = db8Btn;
            bindings.eegButton = eegBtn;
            bindings.labelsButton = labelsBtn;
            bindings.subjectS001Button = subject1Btn;
            bindings.subjectS002Button = subject2Btn;
            bindings.subjectS003Button = subject3Btn;
            bindings.playPauseButton = playBtn;
            bindings.restartButton = restartBtn;
            bindings.carryButton = carryBtn;
            bindings.handoffL2RButton = hoffL;
            bindings.handoffR2LButton = hoffR;
        }

        /// <summary>Dynamically lists every exported DB8 clip as a clickable entry
        /// (rest/thumb/index/cylindrical/lateral/tripod).</summary>
        void CreateDb8ClipMenu(GameObject canvas)
        {
            string dir = Path.Combine("Assets", "StreamingAssets", "Replays", "db8");
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "db8_S1_*.jsonl");
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string clip = Path.GetFileNameWithoutExtension(file);
                string label = Db8ClipLabel(clip);
                int column = i % 2;
                int row = i / 2;
                var btn = CreateButton(canvas, "BtnDb8_" + clip, label,
                    new Vector2(20f + column * 170f, -334f - row * 40f), new Vector2(150f, 34f));
                // Preserve the real source filename; only its visible label is translated.
                var host = new GameObject("Db8Menu_" + clip);
                host.transform.SetParent(canvas.transform, false);
                var menu = host.AddComponent<Db8ClipMenu>();
                menu.button = btn;
                menu.clipPath = Path.GetFileName(file);
            }
        }

        static string Db8ClipLabel(string clip)
        {
            string name = clip.Replace("db8_S1_", "").ToLowerInvariant();
            if (name.Contains("rest")) return "静息";
            if (name.Contains("thumb")) return "拇指屈伸";
            if (name.Contains("index")) return "食指屈伸";
            if (name.Contains("cylindrical")) return "圆柱抓握";
            if (name.Contains("lateral")) return "侧捏";
            if (name.Contains("tripod")) return "三指捏";
            return name.Replace('_', ' ');
        }

        static Text CreateText(GameObject parent, string name, string value,
            Vector2 topLeft, Vector2 size, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = UiFont.Load();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        static Button CreateButton(GameObject canvas, string name, string label,
            Vector2 anchoredPos, Vector2 size)
        {
            var btnGo = new GameObject(name, typeof(RectTransform));
            btnGo.transform.SetParent(canvas.transform, false);
            var rt = btnGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0.13f, 0.20f, 0.27f, 1f);
            var btn = btnGo.AddComponent<Button>();
            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(btnGo.transform, false);
            var txtRect = txtGo.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(5f, 2f);
            txtRect.offsetMax = new Vector2(-5f, -2f);
            var txt = txtGo.AddComponent<Text>();
            txt.font = UiFont.Load();
            txt.fontSize = 15;
            txt.color = Color.white;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.raycastTarget = false;
            txt.text = label;
            return btn;
        }

        static void CreateFillOnlySlider(GameObject sliderGo)
        {
            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(sliderGo.transform, false);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.18f);
            bgImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(bg.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.8f, 1f);
            fillImg.raycastTarget = false;
            var slider = sliderGo.GetComponent<Slider>();
            slider.fillRect = fillRect;
        }
    }

    // Db8ClipMenu and UiFont live in the runtime assembly
    // (Scripts/Runtime/Db8ClipMenu.cs) so scene references survive player builds.
}
