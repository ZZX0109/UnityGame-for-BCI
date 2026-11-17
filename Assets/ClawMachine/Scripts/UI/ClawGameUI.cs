using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace ClawMachine
{
    /// <summary>Scene-owned responsive Canvas. No IMGUI or default/system font dependency.</summary>
    public sealed class ClawGameUI : MonoBehaviour
    {
        [SerializeField] private ClawGameManager game;
        [SerializeField] private Font uiFont;
        [SerializeField] private Shader bitmapShader;
        [SerializeField] private Shader bitmapCompatibilityShader;
        private GameObject canvasObject, ownedEventSystem;
        private RectTransform safeRoot, content, pipFrame, panel, observer, help;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private RawImage pipImage;
        private TextMeshProUGUI viewLabel, phaseLabel, pipLabel, feedbackLabel, progressLabel, assistLabel;
        private RectTransform toolMarker, targetMarker, massMarker;
        private ScrollRect panelScroll;
        private readonly Dictionary<string, RectTransform> panelNodes = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, int> poolUses = new Dictionary<string, int>();
        private readonly Dictionary<string, float> scrollPositions = new Dictionary<string, float>();
        private struct FocusRecord { public GameObject Control; public string ActionKey; }
        private readonly Dictionary<string, FocusRecord> focusPositions = new Dictionary<string, FocusRecord>();
        private readonly Dictionary<GameObject, string> controlActions = new Dictionary<GameObject, string>();
        private bool poolingPanel;
        private string panelFamily;
        private readonly List<TextScaleRecord> textSizes = new List<TextScaleRecord>();
        private readonly List<Button> aimButtons = new List<Button>();
        private PanelStamp panelKey;
        private bool panelInvalid = true;
        private struct PanelStamp : IEquatable<PanelStamp>
        {
            public int Mode, Challenge, State, Remaining, Failure, Prize, Distance, Flags;
            public bool Equals(PanelStamp other) => Mode == other.Mode && Challenge == other.Challenge && State == other.State && Remaining == other.Remaining &&
                Failure == other.Failure && Prize == other.Prize && Distance == other.Distance && Flags == other.Flags;
        }
        private Rect previousSafeArea;
        private Vector2 previousScreen;
        private float previousTextScale = -1;
        private int previousView = -1, previousDistance = -1, previousProgress = int.MinValue, previousAssistX = int.MinValue, previousAssistZ = int.MinValue;
        private int previousAssistMode = -1;
        private bool started, detailsExpanded;
        private static readonly Color Ink = new Color(0.15f, 0.24f, 0.21f);
        private static readonly Color Muted = new Color(0.34f, 0.42f, 0.37f);
        private static readonly Color Paper = new Color(0.97f, 0.95f, 0.90f, 0.98f);
        private static readonly Color Green = new Color(0.17f, 0.37f, 0.30f);
        private struct TextScaleRecord { public TMP_Text Text; public float Size; }
        public bool IsReady => canvas && font && fontMaterial;
        public Canvas OwnedCanvas => canvas;
        public TMP_FontAsset OwnedFont => font;
        public bool OwnsEventSystem => ownedEventSystem;
        public string BootstrapError { get; private set; } = "";
        public bool DetailsExpanded => detailsExpanded;
        public int PooledNodeCount => panelNodes.Count;
        public string FeedbackText => feedbackLabel ? feedbackLabel.text : "";
        public bool PositionMarkersVisible => toolMarker && toolMarker.gameObject.activeSelf;
        public bool MassMarkerVisible => massMarker && massMarker.gameObject.activeSelf;
        public bool CompactPlay => game && game.Task && !game.InMenu && !game.IsPaused && !game.SettingsOpen && !game.ResetConfirmation &&
            game.Task.Session.State != GraspState.Error && !detailsExpanded;

        private void Start() { started = true; Build(); }
        private void OnEnable() { if (started) Build(); }
        private void OnDisable() { ReleaseRuntime(); }
        private void OnDestroy() { ReleaseRuntime(); }

        public void Build()
        {
            if (IsReady || !game || !game.Task) return;
            BootstrapError = "";
            if (!game.Cameras || !game.Cameras.IsConfigured || game.Challenges == null)
            { FailBootstrap("Claw UI camera or challenge references are incomplete."); return; }
            if (!uiFont) { FailBootstrap("Claw UI source font is not assigned."); return; }
            // The resources are original shared compatibility shaders, included in the
            // player before TMP creates a material internally. No Essential Resources import.
            if (!bitmapShader) bitmapShader = Resources.Load<Shader>("BCIShared/TMP/BCITMPBitmap");
            if (!bitmapCompatibilityShader) bitmapCompatibilityShader = Resources.Load<Shader>("BCIShared/TMP/BCITMPBitmapCompatibility");
            if (!bitmapShader || !bitmapCompatibilityShader || !bitmapShader.isSupported)
            { FailBootstrap("Shared TMP bitmap shaders are missing or unsupported."); return; }
            if (!TMP_Settings.instance) { FailBootstrap("PrepareForValidation must create the canonical TMP Settings resource before UI startup."); return; }
            font = TMP_FontAsset.CreateFontAsset(uiFont, 48, 4, GlyphRenderMode.RASTER_HINTED, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (!font || !font.material) { FailBootstrap("The licensed source font could not create a TMP bitmap atlas."); ReleaseRuntime(); return; }
            font.name = "Claw UI Sans - owned dynamic TMP bitmap";
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            fontMaterial = font.material;
            fontMaterial.name = "Claw UI Sans - owned bitmap material";
            fontMaterial.shader = bitmapShader;
            fontMaterial.SetColor("_FaceColor", Color.white);
            // Prewarm ASCII without changing any global TMP default or another game's font.
            font.TryAddCharacters("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz /:-+.%()[]");
            canvasObject = new GameObject("Claw responsive Canvas", typeof(RectTransform));
            canvasObject.layer = 5;
            canvasObject.SetActive(false); // Assign every TMP font before its first OnEnable.
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            safeRoot = CreateRect("Safe area", canvasObject.transform);
            Stretch(safeRoot);
            BuildObserverOverlay();
            BuildSidePanel();
            if (!EventSystem.current)
            {
                ownedEventSystem = new GameObject("Claw owned EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                ownedEventSystem.transform.SetParent(transform, false);
            }
            if (game.Cameras) game.Cameras.Changed += RefreshPip;
            UpdateSafeArea();
            RebuildPanel();
            RefreshPip();
            ApplyTextScale();
            canvasObject.SetActive(true);
        }
        private void FailBootstrap(string error) { BootstrapError = error; Debug.LogError(error, this); }

        private void BuildObserverOverlay()
        {
            observer = CreateRect("Observation HUD", safeRoot);
            observer.anchorMin = new Vector2(0.025f, 0.62f); observer.anchorMax = new Vector2(0.31f, 0.975f);
            observer.offsetMin = observer.offsetMax = Vector2.zero;
            var group = observer.gameObject.AddComponent<VerticalLayoutGroup>(); group.spacing = 3; group.childForceExpandHeight = false;
            phaseLabel = Text(observer, "三轴观察 · 从容练习", 22, Ink, 32);
            viewLabel = Text(observer, "", 16, Muted, 42);
            feedbackLabel = Text(observer, "", 17, Ink, 66);
            progressLabel = Text(observer, "", 16, Muted, 26);
            assistLabel = Text(observer, "", 16, Green, 46);
            toolMarker = Marker("Claw floor projection", new Color(0.15f, 0.65f, 0.62f, 0.9f), 12);
            targetMarker = Marker("Target floor reference", new Color(0.9f, 0.65f, 0.20f, 0.85f), 8);
            massMarker = Marker("Read-only center of mass", new Color(1, 0.65f, 0.12f, 0.95f), 14);
            help = CreateRect("Fixed machine axes", safeRoot);
            help.anchorMin = new Vector2(0.29f, 0.025f); help.anchorMax = new Vector2(0.715f, 0.14f);
            help.offsetMin = help.offsetMax = Vector2.zero;
            var helpBackground = help.gameObject.AddComponent<Image>(); helpBackground.color = new Color(0.97f, 0.95f, 0.90f, 0.86f); helpBackground.raycastTarget = false;
            var helpText = Text(help, "A / D 左右 X    W / S 前后 Z\n右键拖动环绕 · 滚轮缩放 · C 画中画", 16, Muted, 0);
            Stretch(helpText.rectTransform, new Vector2(12, 8), new Vector2(-12, -8));
            pipFrame = CreateRect("Front and side PIP", safeRoot);
            pipFrame.anchorMin = new Vector2(0.025f, 0.025f); pipFrame.anchorMax = new Vector2(0.265f, 0.35f);
            pipFrame.offsetMin = pipFrame.offsetMax = Vector2.zero;
            var pipBorder = pipFrame.gameObject.AddComponent<Image>(); pipBorder.color = Paper; pipBorder.raycastTarget = false;
            var picture = CreateRect("PIP picture", pipFrame); Stretch(picture, new Vector2(5, 28), new Vector2(-5, -5));
            pipImage = picture.gameObject.AddComponent<RawImage>(); pipImage.raycastTarget = false;
            var aspect = picture.gameObject.AddComponent<AspectRatioFitter>(); aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 4f / 3;
            pipLabel = Text(pipFrame, "", 14, Ink, 0);
            pipLabel.rectTransform.anchorMin = Vector2.zero; pipLabel.rectTransform.anchorMax = new Vector2(1, 0);
            pipLabel.rectTransform.pivot = new Vector2(0.5f, 0); pipLabel.rectTransform.sizeDelta = new Vector2(-12, 26); pipLabel.rectTransform.anchoredPosition = Vector2.zero;
        }
        private void BuildSidePanel()
        {
            panel = CreateRect("Ivory control panel", safeRoot);
            panel.anchorMin = new Vector2(0.745f, 0); panel.anchorMax = Vector2.one;
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = Paper;
            var viewport = CreateRect("Scrollable viewport", panel); Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 32;
            content = CreateRect("Responsive vertical content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 24, 24); layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            panelScroll = scroll;
        }
        private void LateUpdate()
        {
            if (!IsReady || !game || !game.Task) return;
            if (previousSafeArea != Screen.safeArea || previousScreen != new Vector2(Screen.width, Screen.height)) UpdateSafeArea();
            PanelStamp wantedKey = MakePanelKey();
            if (panelInvalid || !wantedKey.Equals(panelKey)) RebuildPanel();
            if (!Mathf.Approximately(previousTextScale, game.TextScale)) ApplyTextScale();
            SetText(phaseLabel, game.InMenu ? "三轴观察 · 从容练习" : StateName(game.Task.Session.State));
            if (game.Cameras && (previousView != game.Cameras.SelectedView || previousDistance != game.Cameras.ObservationDistance))
            {
                previousView = game.Cameras.SelectedView; previousDistance = game.Cameras.ObservationDistance;
                SetText(viewLabel, game.Cameras.ViewName + " / " + game.Cameras.DistanceName + "距离 · W 始终朝机柜内 +Z");
            }
            UpdateReadOnlyFeedback();
            bool canAim = !game.InMenu && !game.IsPaused && !game.SettingsOpen && !game.ResetConfirmation && game.Task.Session.State == GraspState.Aiming;
            foreach (Button item in aimButtons) if (item) item.interactable = canAim;
        }
        private PanelStamp MakePanelKey()
        {
            return new PanelStamp {
                Mode = game.ResetConfirmation ? 0 : game.SettingsOpen ? 1 : game.IsPaused ? 2 : game.InMenu ? 3 : 4,
                Challenge = game.SelectedChallengeIndex, State = (int)game.Task.Session.State, Remaining = game.Task.Session.Remaining,
                Failure = (int)game.Task.Session.Failure, Prize = game.Task.SelectedPrize ? game.Task.SelectedPrize.GetInstanceID() : 0,
                Distance = game.Cameras.ObservationDistance,
                Flags = (game.Task.Session.Awarded ? 1 : 0) | (detailsExpanded ? 2 : 0) | (game.Cameras.PipEnabled ? 4 : 0) |
                    (game.Cameras.PipLowQuality ? 8 : 0) | (game.Cameras.Smooth ? 16 : 0) | (game.PositionAssist ? 32 : 0) | (game.CenterOfMassAssist ? 64 : 0)
            };
        }
        private void RebuildPanel()
        {
            if (!content) return;
            SavePanelPosition();
            panelFamily = game.ResetConfirmation ? "reset" : game.SettingsOpen ? "settings" : game.IsPaused ? "pause" : game.InMenu ? "menu" : detailsExpanded ? "details" : "compact";
            foreach (RectTransform node in panelNodes.Values) if (node) node.gameObject.SetActive(false);
            poolUses.Clear(); poolingPanel = true;
            aimButtons.Clear();
            if (CompactPlay) { CompactPlayPanel(); FinishPanel(); return; }
            Text(content, "拾光夹物所", 30, Ink, 44);
            Text(content, "CLAW ATELIER / " + (game.Task.Challenge ? game.Task.Challenge.challengeId : ""), 14, Muted, 22);
            if (!game.InMenu && !game.IsPaused && !game.SettingsOpen && !game.ResetConfirmation)
                ActionButton(content, "返回观察", () => SetDetailsExpanded(false), false);
            if (game.Task.Session.State == GraspState.Error) ErrorPanel();
            else if (game.ResetConfirmation) ResetPanel();
            else if (game.SettingsOpen) SettingsPanel();
            else if (game.IsPaused) PausePanel();
            else if (game.InMenu) MenuPanel();
            else PlayPanel();
            Spacer(8);
            Text(content, "按住右键拖动环绕观察，滚轮缩放。", 16, Muted, 48);
            ActionButton(content, "画中画 " + (game.Cameras.PipEnabled ? "关闭" : "开启"), () => { game.Cameras.TogglePip(); InvalidatePanel(); }, false, "camera.pip");
            FinishPanel();
        }
        private void FinishPanel()
        {
            poolingPanel = false;
            panelKey = MakePanelKey(); panelInvalid = false; ApplyLayout(); ApplyTextScale(); RefreshPip();
            Canvas.ForceUpdateCanvases();
            if (panelScroll) { panelScroll.StopMovement(); panelScroll.verticalNormalizedPosition = scrollPositions.TryGetValue(panelFamily, out float position) ? position : 1; }
            if (EventSystem.current)
            {
                FocusRecord saved = focusPositions.TryGetValue(panelFamily, out FocusRecord record) ? record : default(FocusRecord);
                GameObject selected = saved.Control;
                Selectable control = selected ? selected.GetComponent<Selectable>() : null;
                // Pool identity is not action identity: the same button can mean
                // Grab, Help or Retry in consecutive phases. Only the same action
                // may inherit keyboard focus after rebinding its node.
                bool sameAction = selected && controlActions.TryGetValue(selected, out string actionKey) && actionKey == saved.ActionKey;
                EventSystem.current.SetSelectedGameObject(sameAction && selected.activeInHierarchy && control && control.IsInteractable() ? selected : null);
            }
        }
        private void SavePanelPosition()
        {
            if (string.IsNullOrEmpty(panelFamily)) return;
            if (panelScroll) scrollPositions[panelFamily] = panelScroll.verticalNormalizedPosition;
            GameObject selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
            if (selected && selected.transform.IsChildOf(content) && controlActions.TryGetValue(selected, out string actionKey))
                focusPositions[panelFamily] = new FocusRecord { Control = selected, ActionKey = actionKey };
            else focusPositions.Remove(panelFamily);
        }
        public void SetDetailsExpanded(bool value)
        {
            detailsExpanded = value;
            if (IsReady) RebuildPanel();
        }
        private void CompactPlayPanel()
        {
            ClawSession session = game.Task.Session;
            Text(content, game.Task.Challenge.title, 20, Ink, 32);
            Text(content, "任务目标：" + (game.Task.Prize ? game.Task.Prize.DisplayName : game.Task.Challenge.targetId), 17, Muted, 28);
            Text(content, "剩余尝试 " + session.Remaining + " / " + game.Task.Challenge.attemptLimit, 18, Ink, 28);
            if (session.State == GraspState.Aiming)
                aimButtons.Add(ActionButton(content, "开始抓取 · Enter", game.Grab));
            else if (session.State == GraspState.Result)
            {
                Text(content, Hint(session), 16, Ink, 72);
                ActionButton(content, session.Awarded || session.Remaining == 0 ? "查看本局结果 · Enter" : "还原布局后再试 · Enter", game.Retry);
            }
            else if (session.State == GraspState.End)
            {
                Text(content, Hint(session), 16, Ink, 72);
                ActionButton(content, "返回挑战选择", game.ReturnToMenu);
            }
            else Text(content, StateName(session.State), 20, Green, 34);
            ActionButton(content, "抓持提示 / 操作说明", () => SetDetailsExpanded(true), false);
            ActionButton(content, "暂停 · Esc", () => game.Pause(true), false);
        }
        private void ApplyLayout()
        {
            if (!panel || !game || !game.Cameras) return;
            // Keep the central cabinet unobstructed. Only the compact action card
            // occupies the lower right; complete controls remain one tap away.
            panel.anchorMin = new Vector2(0.745f, CompactPlay ? 0.025f : 0);
            panel.anchorMax = new Vector2(CompactPlay ? 0.98f : 1, CompactPlay ? 0.67f : 1);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            Rect normalized = NormalizeSafeArea(Screen.safeArea, new Vector2(Screen.width, Screen.height));
            float right = CompactPlay ? 1 : normalized.xMin + normalized.width * 0.74f;
            game.Cameras.SetObservationViewport(new Rect(0, 0, right, 1));
        }
        private void MenuPanel()
        {
            Text(content, "从规则礼盒开始，练习观察、抓持与稳定投放。", 21, Ink, 64);
            for (int i = 0; i < game.Challenges.Count; i++)
            {
                int index = i; ClawChallengeDefinition challenge = game.Challenges[i];
                if (!challenge) continue;
                ActionButton(content, (i == game.SelectedChallengeIndex ? "● " : "○ ") + challenge.title,
                    () => { game.SelectChallenge(index); InvalidatePanel(); }, i == game.SelectedChallengeIndex, "challenge:" + challenge.challengeId);
            }
            if (game.Task.Challenge)
            {
                Text(content, game.Task.Challenge.instructions, 17, Muted, 74);
                Text(content, "抓取提示：" + game.Task.Challenge.gripHint, 17, Ink, 56);
            }
            ActionButton(content, "开始挑战", game.StartGame);
            ActionButton(content, "设置与无障碍", game.OpenSettings, false);
        }
        private void PlayPanel()
        {
            ClawSession session = game.Task.Session;
            Text(content, game.Task.Challenge.title, 22, Ink, 34);
            Text(content, "剩余尝试 " + session.Remaining + " / " + game.Task.Challenge.attemptLimit, 20, Ink, 30);
            Text(content, StateName(session.State), 24, Green, 38);
            Text(content, Hint(session), 18, Ink, 86);
            Text(content, "任务目标：" + (game.Task.Prize ? game.Task.Prize.DisplayName : game.Task.Challenge.targetId), 15, Muted, 32);
            if (game.Task.SelectedPrize)
            {
                Text(content, "当前对象：" + game.Task.SelectedPrize.DisplayName, 18, Ink, 32);
                Text(content, game.Task.SelectedPrize.GripHint, 16, Muted, 56);
            }
            if (session.State == GraspState.Aiming)
            {
                Text(content, "选择观察对象与抓持提示（实际抓取以爪下接触为准）", 16, Muted, 54);
                for (int i = 0; i < game.Task.Prizes.Count; i++)
                {
                    int index = i; PrizePhysics prize = game.Task.Prizes[i];
                    if (!prize || !prize.gameObject.activeInHierarchy) continue;
                    ActionButton(content, (prize == game.Task.SelectedPrize ? "● " : "○ ") + prize.DisplayName,
                        () => { game.Task.SelectPrize(index); InvalidatePanel(); }, false, "prize:" + prize.SlotId);
                }
                aimButtons.Add(ActionButton(content, "开始抓取 · Enter", game.Grab));
            }
            else if (session.State == GraspState.Result)
                ActionButton(content, session.Awarded || session.Remaining == 0 ? "查看本局结果 · Enter" : "还原布局后再试 · Enter", game.Retry);
            else if (session.State == GraspState.End)
            {
                ActionButton(content, "相同布局重新练习", game.StartGame);
                ActionButton(content, "返回挑战选择", game.ReturnToMenu, false);
            }
            else ActionButton(content, "自动执行中…", null, false).interactable = false;
            Text(content, "失败后点下一次尝试：所有未获奖物体恢复本局初始位置，剩余次数保留。", 14, Muted, 58);
            ActionButton(content, "暂停 · Esc", () => game.Pause(true), false);
            ActionButton(content, "重新挑战 · R", game.RequestReset, false);
        }
        private void PausePanel()
        {
            Text(content, "已暂停", 27, Ink, 42);
            Text(content, "机构、重力与本轮计时均已冻结。仍可按住右键环绕观察。", 19, Muted, 100);
            ActionButton(content, "继续 · Esc", () => game.Pause(false));
            ActionButton(content, "设置", game.OpenSettings, false);
            ActionButton(content, "重新挑战", game.RequestReset, false);
            ActionButton(content, "取消并返回挑战选择", game.ReturnToMenu, false);
        }
        private void ResetPanel()
        {
            Text(content, "重新挑战？", 26, Ink, 42);
            Text(content, "本轮连接与速度将清除，所有奖品恢复相同 seed 的初始布局，次数重新开始。", 19, Muted, 124);
            ActionButton(content, "确认重新挑战", game.ConfirmReset);
            ActionButton(content, "保留当前进度", game.DeclineReset, false);
        }
        private void SettingsPanel()
        {
            Text(content, "设置与无障碍", 25, Ink, 40);
            AddSlider("机械提示音", 0, 1, game.Volume, game.SetVolume, "%", 100);
            AddSlider("文字大小", 0.9f, 1.2f, game.TextScale, game.SetTextScale, "%", 100);
            ActionButton(content, "镜头距离：" + game.Cameras.DistanceName, () => { game.Cameras.CycleDistance(); InvalidatePanel(); }, false, "camera.distance");
            ActionButton(content, "镜头平滑：" + (game.Cameras.Smooth ? "开" : "关"), () => { game.SetCameraSmooth(!game.Cameras.Smooth); InvalidatePanel(); }, false, "camera.smooth");
            ActionButton(content, "还原观察角度", () => game.Cameras.ResetOrbit(), false);
            ActionButton(content, "画中画质量：" + (game.Cameras.PipLowQuality ? "省电 320×240" : "清晰 480×360"),
                () => { game.SetPipQuality(!game.Cameras.PipLowQuality); InvalidatePanel(); }, false, "camera.pipQuality");
            ActionButton(content, "C01 定位辅助：" + (game.PositionAssist ? "开" : "关"), () => { game.SetPositionAssist(!game.PositionAssist); InvalidatePanel(); }, false, "assist.position");
            ActionButton(content, "C03 重心提示：" + (game.CenterOfMassAssist ? "开" : "关"), () => { game.SetCenterOfMassAssist(!game.CenterOfMassAssist); InvalidatePanel(); }, false, "assist.mass");
            Text(content, "画中画会增加一次相机渲染。低性能设备可关闭或使用省电质量。", 15, Muted, 70);
            ActionButton(content, "返回", game.CloseSettings);
        }
        private void ErrorPanel()
        {
            Text(content, "场景尚未就绪", 25, Ink, 42);
            Text(content, game.Task.Diagnostic, 18, Muted, 130);
            ActionButton(content, "返回菜单", game.ReturnToMenu);
        }
        private void AddSlider(string title, float min, float max, float value, Action<float> changed, string suffix, float multiplier)
        {
            TextMeshProUGUI label = Text(content, title + " " + Mathf.RoundToInt(value * multiplier) + suffix, 18, Ink, 30);
            RectTransform root = CreateRect(title + " slider", content); Layout(root, 44);
            var slider = Ensure<Slider>(root); slider.onValueChanged.RemoveAllListeners(); slider.minValue = min; slider.maxValue = max; slider.direction = Slider.Direction.LeftToRight;
            controlActions[root.gameObject] = "slider:" + title;
            RectTransform track = CreateRect("Track", root); track.anchorMin = new Vector2(0, 0.38f); track.anchorMax = new Vector2(1, 0.62f); track.offsetMin = track.offsetMax = Vector2.zero;
            Ensure<Image>(track).color = new Color(0.80f, 0.82f, 0.75f);
            RectTransform fill = CreateRect("Fill", track); Stretch(fill); Ensure<Image>(fill).color = Green;
            RectTransform handle = CreateRect("Handle", root); handle.anchorMin = handle.anchorMax = new Vector2(0, 0.5f); handle.sizeDelta = new Vector2(24, 30);
            var handleImage = Ensure<Image>(handle); handleImage.color = Green;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(amount => { label.text = title + " " + Mathf.RoundToInt(amount * multiplier) + suffix; changed(amount); });
        }
        private Button ActionButton(Transform parent, string caption, UnityAction action, bool primary = true, string actionKey = null)
        {
            RectTransform root = CreateRect("Action button", parent); root.name = caption; Layout(root, 48);
            var image = Ensure<Image>(root); image.color = primary ? Green : new Color(0.87f, 0.89f, 0.82f);
            var button = Ensure<Button>(root); button.targetGraphic = image; button.interactable = true; button.onClick.RemoveAllListeners();
            controlActions[root.gameObject] = "button:" + (actionKey ?? caption);
            ColorBlock colors = button.colors; colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f); colors.pressedColor = new Color(0.84f, 0.90f, 0.84f); colors.disabledColor = new Color(0.72f, 0.74f, 0.69f); button.colors = colors;
            var label = Text(root, caption, 17, primary ? Color.white : Ink, 0);
            label.alignment = TextAlignmentOptions.Center; Stretch(label.rectTransform, new Vector2(8, 5), new Vector2(-8, -5));
            if (action != null) button.onClick.AddListener(action);
            return button;
        }
        private TextMeshProUGUI Text(Transform parent, string value, float size, Color color, float minimumHeight)
        {
            RectTransform root = CreateRect("Text", parent);
            // RebuildPanel also runs after the Canvas is active. Suppress Awake/
            // OnEnable until this particular text has its independently owned font.
            root.gameObject.SetActive(false);
            var text = Ensure<TextMeshProUGUI>(root);
            text.font = font; text.fontSharedMaterial = fontMaterial; text.text = value ?? "";
            text.fontSize = size; text.color = color; text.richText = false;
            text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft;
            if (minimumHeight > 0 || root.GetComponent<LayoutElement>()) { var element = Ensure<LayoutElement>(root); element.minHeight = minimumHeight; element.preferredHeight = -1; element.flexibleHeight = 0; }
            int record = textSizes.FindIndex(item => item.Text == text);
            var entry = new TextScaleRecord { Text = text, Size = size };
            if (record < 0) textSizes.Add(entry); else textSizes[record] = entry;
            root.gameObject.SetActive(true);
            return text;
        }
        private RectTransform CreateRect(string name, Transform parent)
        {
            string key = null;
            if (poolingPanel)
            {
                string group = parent.GetInstanceID() + ":" + name;
                int index = poolUses.TryGetValue(group, out int used) ? used : 0;
                poolUses[group] = index + 1; key = group + ":" + index;
                if (panelNodes.TryGetValue(key, out RectTransform existing) && existing)
                { existing.gameObject.SetActive(true); existing.SetAsLastSibling(); return existing; }
            }
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            if (key != null) panelNodes.Add(key, rect);
            return rect;
        }
        private static T Ensure<T>(RectTransform root) where T : Component => root.GetComponent<T>() ?? root.gameObject.AddComponent<T>();
        private static void Stretch(RectTransform root) { Stretch(root, Vector2.zero, Vector2.zero); }
        private static void Stretch(RectTransform root, Vector2 min, Vector2 max) { root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = min; root.offsetMax = max; }
        private static void Layout(RectTransform root, float height) { var element = Ensure<LayoutElement>(root); element.minHeight = height; element.preferredHeight = height; element.flexibleHeight = 0; }
        private RectTransform Row(Transform parent, float height)
        {
            RectTransform root = CreateRect("Responsive button row", parent); Layout(root, height);
            var row = Ensure<HorizontalLayoutGroup>(root); row.spacing = 5; row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = true; row.childForceExpandHeight = true; return root;
        }
        private void Spacer(float height) { Layout(CreateRect("Space", content), height); }
        private void InvalidatePanel() { panelInvalid = true; }
        private void ApplyTextScale()
        {
            if (!game) return; previousTextScale = game.TextScale;
            textSizes.RemoveAll(item => !item.Text);
            foreach (TextScaleRecord item in textSizes) item.Text.fontSize = item.Size * game.TextScale;
        }
        private void RefreshPip()
        {
            if (!pipFrame || !game || !game.Cameras) return;
            pipFrame.gameObject.SetActive(game.Cameras.PipEnabled && game.Cameras.PipTexture);
            pipImage.texture = game.Cameras.PipTexture;
            pipLabel.text = game.Cameras.PipViewName + " · 补充观察";
        }
        public bool AllowsObservationPointer(Vector2 point)
        {
            if (!IsReady || !isActiveAndEnabled) return true;
            return !ContainsScreenPoint(panel, point) && !ContainsScreenPoint(pipFrame, point) &&
                !ContainsScreenPoint(observer, point) && !ContainsScreenPoint(help, point);
        }
        private static bool ContainsScreenPoint(RectTransform rect, Vector2 point)
            => rect && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, point, null);
        private RectTransform Marker(string label, Color color, float size)
        {
            RectTransform root = CreateRect(label, safeRoot);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.one * size;
            var graphic = root.gameObject.AddComponent<Image>(); graphic.color = color; graphic.raycastTarget = false;
            root.gameObject.SetActive(false); return root;
        }
        private void PlaceMarker(RectTransform marker, Vector3 world, bool visible)
        {
            UnityEngine.Camera view = game.Cameras.CurrentCamera;
            Vector3 screen = view ? view.WorldToScreenPoint(world) : Vector3.zero;
            bool show = visible && view && screen.z > view.nearClipPlane && view.pixelRect.Contains(screen);
            marker.gameObject.SetActive(show);
            if (show && RectTransformUtility.ScreenPointToLocalPointInRectangle(safeRoot, screen, null, out Vector2 local)) marker.anchoredPosition = local;
        }
        private void UpdateReadOnlyFeedback()
        {
            var task = game.Task;
            bool playing = !game.InMenu;
            SetText(feedbackLabel, playing ? Hint(task.Session) : "");
            int progress = !playing ? -1000 : task.Session.State == GraspState.Settling ? 100 + Mathf.RoundToInt(task.StableProgress * 100) : task.ContactCount;
            if (previousProgress != progress)
            {
                previousProgress = progress;
                SetText(progressLabel, !playing ? "" : progress >= 100 ? "出口稳定 " + (progress - 100) + "%" : "有效接触 " + progress + " / 3");
            }
            PrizePhysics target = task.Prize;
            bool aiming = playing && !game.IsPaused && task.Session.State == GraspState.Aiming && target && task.Challenge;
            bool position = aiming && task.Challenge.challengeId == "C01" && game.PositionAssist;
            bool mass = aiming && task.Challenge.challengeId == "C03" && game.CenterOfMassAssist;
            if (target)
            {
                Vector3 origin = task.Driver.CurrentToolPose.position;
                Vector3 reference = target.Body.worldCenterOfMass;
                float floor = target.AllBounds.min.y + 0.006f;
                PlaceMarker(toolMarker, new Vector3(origin.x, floor, origin.z), position);
                PlaceMarker(targetMarker, new Vector3(reference.x, floor, reference.z), position);
                PlaceMarker(massMarker, reference, mass);
                Vector3 delta = task.TaskSpace.ToLocal(reference) - task.TaskSpace.ToLocal(origin);
                int mode = position ? 1 : mass ? 2 : 0, x = Mathf.RoundToInt(delta.x * 1000), z = Mathf.RoundToInt(delta.z * 1000);
                if (previousAssistMode != mode || position && (previousAssistX != x || previousAssistZ != z))
                {
                    previousAssistMode = mode; previousAssistX = x; previousAssistZ = z;
                    SetText(assistLabel, position ? "青色爪尖 / 金色目标\nX " + (x / 10f).ToString("+0.0;-0.0;0.0") + " cm    Z " + (z / 10f).ToString("+0.0;-0.0;0.0") + " cm" :
                        mass ? "金色标记：真实重心\n外形中心与重心可能不同" : "");
                }
            }
            else
            {
                toolMarker.gameObject.SetActive(false); targetMarker.gameObject.SetActive(false); massMarker.gameObject.SetActive(false);
                previousAssistMode = -1;
                SetText(assistLabel, "");
            }
        }
        private static void SetText(TMP_Text text, string value) { if (text && text.text != value) text.text = value; }
        private void UpdateSafeArea()
        {
            previousSafeArea = Screen.safeArea; previousScreen = new Vector2(Screen.width, Screen.height);
            Rect normalized = NormalizeSafeArea(previousSafeArea, previousScreen);
            safeRoot.anchorMin = normalized.min; safeRoot.anchorMax = normalized.max;
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
            ApplyLayout();
        }
        public static Rect NormalizeSafeArea(Rect area, Vector2 screen)
        {
            if (!Finite(screen.x) || !Finite(screen.y) || !Finite(area.x) || !Finite(area.y) || !Finite(area.width) || !Finite(area.height) ||
                screen.x <= 0 || screen.y <= 0 || area.width <= 0 || area.height <= 0) return new Rect(0, 0, 1, 1);
            float x0 = Mathf.Clamp01(area.xMin / screen.x), y0 = Mathf.Clamp01(area.yMin / screen.y);
            float x1 = Mathf.Clamp01(area.xMax / screen.x), y1 = Mathf.Clamp01(area.yMax / screen.y);
            return Rect.MinMaxRect(x0, y0, Mathf.Max(x0, x1), Mathf.Max(y0, y1));
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void ReleaseRuntime()
        {
            if (game && game.Cameras) game.Cameras.Changed -= RefreshPip;
            if (canvasObject) { canvasObject.SetActive(false); Destroy(canvasObject); }
            if (ownedEventSystem) Destroy(ownedEventSystem);
            if (font)
            {
                Texture2D[] atlases = font.atlasTextures;
                if (atlases != null) foreach (Texture2D atlas in atlases) if (atlas) Destroy(atlas);
                if (fontMaterial) Destroy(fontMaterial);
                Destroy(font);
            }
            canvasObject = ownedEventSystem = null; canvas = null; font = null; fontMaterial = null;
            if (game && game.Cameras) game.Cameras.SetObservationViewport(new Rect(0, 0, 1, 1));
            safeRoot = content = pipFrame = panel = observer = help = null; pipImage = null; panelInvalid = true;
            textSizes.Clear(); aimButtons.Clear(); panelNodes.Clear(); poolUses.Clear(); scrollPositions.Clear(); focusPositions.Clear(); controlActions.Clear();
            panelFamily = null; panelScroll = null; poolingPanel = false; previousTextScale = -1;
            previousView = previousDistance = previousAssistMode = -1; previousProgress = previousAssistX = previousAssistZ = int.MinValue;
        }
        private static string StateName(GraspState state)
        {
            switch (state)
            {
                case GraspState.Aiming: return "定位 / AIM";
                case GraspState.Approaching: return "安全接近 / APPROACH";
                case GraspState.ContactDescent: return "低速接触 / CONTACT";
                case GraspState.Closing: return "验证抓持 / GRIP";
                case GraspState.Lifting: return "提升 / LIFT";
                case GraspState.Transporting: return "移送 / RETURN";
                case GraspState.Returning: return "空爪安全回位 / HOME";
                case GraspState.Releasing: return "释放 / RELEASE";
                case GraspState.Settling: return "等待稳定 / SETTLE";
                case GraspState.Result: return "本次结果";
                case GraspState.End: return "本局结束";
                case GraspState.Error: return "需要重置";
                default: return "准备中";
            }
        }
        private static string Hint(ClawSession session)
        {
            if (session.State == GraspState.Aiming) return "按住右键环绕，确认左右与深度。对准对象上方后按 Enter，机构会安全接近并低速寻找抓点。";
            if (session.Failure == GraspFailure.None && session.State != GraspState.Result && session.State != GraspState.End) return "请观察接近、接触和抓持过程。只有目标释放后完整进入出口并稳定下来才获奖。";
            if (session.Awarded) return "目标已稳定进入出口！耐心观察和合适的抓点让这次移送成功了。";
            switch (session.Failure)
            {
                case GraspFailure.Misaligned: return "抓点偏离可抓区域。下次用侧面检查深度，偏心对象要靠近标记抓点。";
                case GraspFailure.NoContact: return "有效表面接触不足。请对准可包围的部位，避免只碰到边缘。";
                case GraspFailure.Penetrating: case GraspFailure.Obstructed: return "检测到穿透或障碍，已拒绝建立抓持。请换一个无遮挡的抓点。";
                case GraspFailure.UnstableGrip: return "接触无法稳定支撑重量或重心。请抓更靠近重心的部位。";
                case GraspFailure.Slipped: return "礼物在提升或移送时滑落。请更靠近推荐抓点，减少偏心力矩。";
                case GraspFailure.WrongTarget: return "投放的是其他对象。请先确认任务目标，再选择要抓取的物体。";
                case GraspFailure.Timeout: return "机构或稳定检测超时。可重置当前挑战，再检查接近路径。";
                default: return "目标没有稳定进入出口。按下一次尝试会还原所有未获奖物体，次数继续计算。";
            }
        }
    }
}
