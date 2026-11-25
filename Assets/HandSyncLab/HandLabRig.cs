using Neuralife.XR;
using Neuralife.VirtualHand;
using Neuralife.VirtualHand.Body;
using StreetCrossing.Integration;
using UnityEngine;
using UnityEngine.UI;

namespace HandSyncLab
{
    /// <summary>
    /// Second deployable flavor: a stable, standalone inspection lab for the hand
    /// sync pipeline. Reuses the street app's tested runtime pieces unchanged —
    /// the FirstPersonFullBody prefab (Elena avatar + FullBodyPoseController) and
    /// StreetTrackedHands (optical hand > controller > natural rest per side) —
    /// without any traffic, accident or menu logic.
    ///
    /// The rig wires the body exactly like StreetCrossingFullBodyBinding does
    /// (wrist heal, skin tint, adapter field fill, seated=false, first-person,
    /// desktop hands-visible pose), parents the body under the PICO player root
    /// in VR so roomscale drift carries the avatar, and drives a fixed world-space
    /// status panel that reports, per hand, the active input source and the
    /// gesture the recognizer currently sees. No interaction is expected: the
    /// scene boots straight into inspection and stays put.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HandLabRig : MonoBehaviour
    {
        /// <summary>Resources-relative name of the shared full-body prefab.</summary>
        const string BodyPrefabResourceName = "FirstPersonFullBody";

        const float PanelRefreshSeconds = 0.25f;

        Camera _view;
        PicoRig _xrRig;
        StreetTrackedHands _hands;
        HandGestureRecognizer _gestures;
        Text _leftLine, _rightLine, _subsystemLine;
        float _nextPanelRefresh;

        public void Awake()
        {
            _view = Camera.main; // scene camera placed by HandSyncLabSceneBuilder
            if (!_view)
                _view = CreateInspectionCamera();
            if (PicoXR.Requested)
                _xrRig = PicoRig.Attach(_view, transform, 1.65f);
            WireBody();
            BuildPanel();
        }

        Camera CreateInspectionCamera()
        {
            var owner = new GameObject("Hand Lab Camera");
            Camera view = owner.AddComponent<Camera>();
            owner.tag = "MainCamera";
            owner.transform.position = new Vector3(0f, 1.6f, -2.2f);
            owner.transform.rotation = Quaternion.identity; // looks at the body front (+Z)
            view.nearClipPlane = 0.05f;
            view.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            view.clearFlags = CameraClearFlags.SolidColor;
            view.stereoTargetEye = StereoTargetEyeMask.Both;
            return view;
        }

        void WireBody()
        {
            GameObject prefab = LoadBodyPrefab();
            if (prefab == null)
            {
                Debug.LogError("[HandLabRig] FirstPersonFullBody prefab missing; the lab cannot run.");
                return;
            }

            GameObject body = Instantiate(prefab);
            body.name = "FirstPersonFullBody(HandLab)";
            // In VR the body must live under the PICO player root so roomscale drift
            // moves it with the wearer (same contract as the street binding).
            body.transform.SetParent(_xrRig ? _xrRig.transform : transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            int wristAligned = ElenaWristAligner.HealBody(body.transform);
            Debug.Log($"[HandLabRig] Elena wrist landmark alignment {wristAligned}/2 hands (target <2 cm residual).");

            body.AddComponent<BodySkinTintApplier>().Apply();

            GameIntegrationAdapter adapter = body.GetComponent<GameIntegrationAdapter>();
            if (adapter == null)
                adapter = body.AddComponent<GameIntegrationAdapter>();
            if (adapter.poseController == null)
                adapter.poseController = body.GetComponentInChildren<FullBodyPoseController>(true);
            if (adapter.locomotion == null)
                adapter.locomotion = body.GetComponentInChildren<LowerBodyPoseController>(true);
            if (adapter.visibility == null)
                adapter.visibility = body.GetComponentInChildren<FirstPersonVisibility>(true);
            if (adapter.appearance == null)
                adapter.appearance = body.GetComponentInChildren<FullBodyAppearanceController>(true);

            adapter.SetSeated(false);
            adapter.SetFirstPerson(true);
            if (!PicoXR.Requested)
                adapter.poseController.ApplyPose(FullBodyPoseController.PoseDesktopHandsVisible);

            _gestures = body.GetComponentInChildren<HandGestureRecognizer>(true);
            if (!_gestures)
                _gestures = body.AddComponent<HandGestureRecognizer>();

            _hands = body.AddComponent<StreetTrackedHands>();
            if (_xrRig && _xrRig.TrackingSpace)
                _hands.Bind(adapter.poseController, _xrRig.TrackingSpace);

            if (_view)
                adapter.visibility.FirstPersonCamera = _view;

            Debug.Log($"[HandLabRig] Inspection lab ready: body wired, VR={PicoXR.Requested}, " +
                      "sources update on the world-space panel; no menu input required.");
        }

        void BuildPanel()
        {
            var canvasGO = new GameObject("Hand Status Panel", typeof(Canvas), typeof(Image));
            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20000;
            RectTransform panel = canvasGO.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(1280f, 512f);
            panel.localScale = Vector3.one * (0.0011f); // ~1.41 m wide, ~0.56 m tall

            // Fixed inspection station: 1.1 m in front of the (final, post-rig) camera
            // pose, raised above eye height. Stable — it never follows the head.
            Vector3 cameraPosition = _view ? _view.transform.position : new Vector3(0f, 1.6f, -2.2f);
            Vector3 forward = _view
                ? Vector3.ProjectOnPlane(_view.transform.forward, Vector3.up).normalized
                : Vector3.forward;
            if (forward.sqrMagnitude < 0.5f)
                forward = Vector3.forward;
            Vector3 panelPosition = cameraPosition + forward.normalized * 1.1f + Vector3.up * 0.15f;
            panel.position = panelPosition;
            panel.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

            Image background = canvasGO.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.85f);
            background.raycastTarget = false;

            Font font;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch (System.Exception) { font = null; }
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Rows hang from the panel's top edge: L / R / subsystem state.
            _leftLine = CreateLine(canvas.transform, font, "L: -", 72f, 40f, 130f);
            _rightLine = CreateLine(canvas.transform, font, "R: -", 72f, 190f, 130f);
            _subsystemLine = CreateLine(canvas.transform, font, "optical subsystem: -", 48f, 340f, 90f);
        }

        static Text CreateLine(Transform parent, Font font, string seed, float size, float y, float height)
        {
            var row = new GameObject(seed, typeof(RectTransform), typeof(Text));
            row.transform.SetParent(parent, false);
            RectTransform rect = row.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(48f, -(y + height));
            rect.offsetMax = new Vector2(-48f, -y);
            Text text = row.GetComponent<Text>();
            text.font = font;
            text.fontSize = (int)size;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = seed;
            return text;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextPanelRefresh)
                return;
            _nextPanelRefresh = Time.unscaledTime + PanelRefreshSeconds;
            RefreshPanel();
        }

        void RefreshPanel()
        {
            bool opticalRunning = _hands != null && _hands.OpticalSubsystemRunning;
            if (_subsystemLine)
                _subsystemLine.text = "optical subsystem: " + (opticalRunning ? "running" : "stopped");
            if (_leftLine)
                _leftLine.text = "L: " + Describe(_hands ? _hands.LeftSource : StreetTrackedHands.InputSource.None,
                                                  _gestures ? _gestures.LeftGesture.type : HandGestureType.None);
            if (_rightLine)
                _rightLine.text = "R: " + Describe(_hands ? _hands.RightSource : StreetTrackedHands.InputSource.None,
                                                   _gestures ? _gestures.RightGesture.type : HandGestureType.None);
        }

        static string Describe(StreetTrackedHands.InputSource source, HandGestureType gesture)
        {
            switch (source)
            {
                case StreetTrackedHands.InputSource.OpticalHandTracking: return "optical hand - " + DescribeGesture(gesture);
                case StreetTrackedHands.InputSource.Controller: return "controller";
                case StreetTrackedHands.InputSource.NaturalRest: return "no tracking";
                default: return "no input";
            }
        }

        static string DescribeGesture(HandGestureType gesture)
        {
            switch (gesture)
            {
                case HandGestureType.Open: return "open";
                case HandGestureType.Fist: return "fist";
                case HandGestureType.Pinch: return "pinch";
                case HandGestureType.Point: return "point";
                default: return "no pose";
            }
        }

        static GameObject LoadBodyPrefab()
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                StreetCrossingFullBodyBinding.BodyPrefabPath);
            if (asset != null)
                return asset;
#endif
            return Resources.Load<GameObject>(BodyPrefabResourceName);
        }
    }
}
