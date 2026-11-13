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
        private RectTransform safeRoot, content, pipFrame;
        private Canvas canvas;
        private TMP_FontAsset font;
        private Material fontMaterial;
        private RawImage pipImage;
        private TextMeshProUGUI viewLabel, phaseLabel, pipLabel;
        private readonly List<TextScaleRecord> textSizes = new List<TextScaleRecord>();
        private readonly List<Button> aimButtons = new List<Button>();
        private string panelKey;
        private Rect previousSafeArea;
        private Vector2 previousScreen;
        private float previousTextScale = -1;
        private bool started;
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
            var observer = CreateRect("Observation HUD", safeRoot);
            observer.anchorMin = new Vector2(0.025f, 0.86f); observer.anchorMax = new Vector2(0.715f, 0.975f);
            observer.offsetMin = observer.offsetMax = Vector2.zero;
            var group = observer.gameObject.AddComponent<VerticalLayoutGroup>(); group.spacing = 3; group.childForceExpandHeight = false;
            phaseLabel = Text(observer, "三轴观察 · 从容练习", 25, Ink, 35);
            viewLabel = Text(observer, "", 18, Muted, 28);
            var help = CreateRect("Fixed machine axes", safeRoot);
            help.anchorMin = new Vector2(0.025f, 0.025f); help.anchorMax = new Vector2(0.715f, 0.14f);
            help.offsetMin = help.offsetMax = Vector2.zero;
            var helpBackground = help.gameObject.AddComponent<Image>(); helpBackground.color = new Color(0.97f, 0.95f, 0.90f, 0.86f); helpBackground.raycastTarget = false;
            var helpText = Text(help, "A / D 左右 X    W / S 前后 Z\n1 / 2 / 3 视角 · 右键拖动微调 · 滚轮距离 · C 画中画", 16, Muted, 0);
            Stretch(helpText.rectTransform, new Vector2(12, 8), new Vector2(-12, -8));
            pipFrame = CreateRect("Front and side PIP", safeRoot);
            pipFrame.anchorMin = new Vector2(0.48f, 0.19f); pipFrame.anchorMax = new Vector2(0.72f, 0.49f);
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
            var panel = CreateRect("Ivory control panel", safeRoot);
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
        }
        private void LateUpdate()
        {
            if (!IsReady || !game || !game.Task) return;
            if (previousSafeArea != Screen.safeArea || previousScreen != new Vector2(Screen.width, Screen.height)) UpdateSafeArea();
            string wantedKey = MakePanelKey();
            if (wantedKey != panelKey) RebuildPanel();
            if (!Mathf.Approximately(previousTextScale, game.TextScale)) ApplyTextScale();
            phaseLabel.text = game.InMenu ? "三轴观察 · 从容练习" : StateName(game.Task.Session.State);
            if (game.Cameras) viewLabel.text = game.Cameras.ViewName + " / " + game.Cameras.DistanceName + "距离 · W 始终朝机柜内 +Z";
            bool canAim = !game.InMenu && !game.IsPaused && !game.SettingsOpen && !game.ResetConfirmation && game.Task.Session.State == GraspState.Aiming;
            foreach (Button item in aimButtons) if (item) item.interactable = canAim;
        }
        private string MakePanelKey()
        {
            return (game.ResetConfirmation ? "reset" : game.SettingsOpen ? "settings" : game.IsPaused ? "pause" : game.InMenu ? "menu" : "play") + ":" +
                game.SelectedChallengeIndex + ":" + game.Task.Session.State + ":" + game.Task.Session.Remaining + ":" + game.Task.Session.Awarded + ":" +
                game.Task.Session.Failure + ":" + (game.Task.SelectedPrize ? game.Task.SelectedPrize.GetInstanceID() : 0);
        }
        private void RebuildPanel()
        {
            if (!content) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject; child.SetActive(false); Destroy(child);
            }
            aimButtons.Clear(); textSizes.RemoveAll(item => !item.Text);
            Text(content, "拾光夹物所", 34, Ink, 48);
            Text(content, "CLAW ATELIER / " + (game.Task.Challenge ? game.Task.Challenge.challengeId : ""), 14, Muted, 22);
            if (game.Task.Session.State == GraspState.Error) ErrorPanel();
            else if (game.ResetConfirmation) ResetPanel();
            else if (game.SettingsOpen) SettingsPanel();
            else if (game.IsPaused) PausePanel();
            else if (game.InMenu) MenuPanel();
            else PlayPanel();
            Spacer(8);
            Text(content, "观察方向", 16, Muted, 24);
            RectTransform views = Row(content, 42);
            ActionButton(views, "1 正面", () => game.Cameras.Select(1));
            ActionButton(views, "2 左侧", () => game.Cameras.Select(2));
            ActionButton(views, "3 右侧", () => game.Cameras.Select(3));
            ActionButton(content, "画中画 " + (game.Cameras.PipEnabled ? "关闭" : "开启"), () => { game.Cameras.TogglePip(); InvalidatePanel(); }, false);
            Text(content, "本地物理模拟 · 无真实设备连接\n右侧内容可滚动，所有操作均有文字反馈。", 13, Muted, 46);
            panelKey = MakePanelKey(); ApplyTextScale();
        }
        private void MenuPanel()
        {
            Text(content, "从规则礼盒开始，练习观察、抓持与稳定投放。", 21, Ink, 64);
            for (int i = 0; i < game.Challenges.Count; i++)
            {
                int index = i; ClawChallengeDefinition challenge = game.Challenges[i];
                if (!challenge) continue;
                ActionButton(content, (i == game.SelectedChallengeIndex ? "● " : "○ ") + challenge.title,
                    () => { game.SelectChallenge(index); InvalidatePanel(); }, i == game.SelectedChallengeIndex);
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
                        () => { game.Task.SelectPrize(index); InvalidatePanel(); }, false);
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
            Text(content, "机构、重力与本轮计时均已冻结。可以继续观察三个方向。", 19, Muted, 100);
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
            ActionButton(content, "镜头距离：" + game.Cameras.DistanceName, () => { game.Cameras.CycleDistance(); InvalidatePanel(); }, false);
            ActionButton(content, "镜头平滑：" + (game.Cameras.Smooth ? "开" : "关"), () => { game.Cameras.Smooth = !game.Cameras.Smooth; InvalidatePanel(); }, false);
            ActionButton(content, "还原观察角度", () => game.Cameras.ResetOrbit(), false);
            ActionButton(content, "画中画质量：" + (game.Cameras.PipLowQuality ? "省电 320×240" : "清晰 480×360"),
                () => { game.Cameras.SetPipQuality(!game.Cameras.PipLowQuality); InvalidatePanel(); }, false);
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
            var slider = root.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.direction = Slider.Direction.LeftToRight;
            RectTransform track = CreateRect("Track", root); track.anchorMin = new Vector2(0, 0.38f); track.anchorMax = new Vector2(1, 0.62f); track.offsetMin = track.offsetMax = Vector2.zero;
            track.gameObject.AddComponent<Image>().color = new Color(0.80f, 0.82f, 0.75f);
            RectTransform fill = CreateRect("Fill", track); Stretch(fill); fill.gameObject.AddComponent<Image>().color = Green;
            RectTransform handle = CreateRect("Handle", root); handle.anchorMin = handle.anchorMax = new Vector2(0, 0.5f); handle.sizeDelta = new Vector2(24, 30);
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = Green;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(amount => { label.text = title + " " + Mathf.RoundToInt(amount * multiplier) + suffix; changed(amount); });
        }
        private Button ActionButton(Transform parent, string caption, UnityAction action, bool primary = true)
        {
            RectTransform root = CreateRect(caption, parent); Layout(root, 48);
            var image = root.gameObject.AddComponent<Image>(); image.color = primary ? Green : new Color(0.87f, 0.89f, 0.82f);
            var button = root.gameObject.AddComponent<Button>(); button.targetGraphic = image;
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
            var text = root.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSharedMaterial = fontMaterial; text.text = value ?? "";
            text.fontSize = size; text.color = color; text.richText = false;
            text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false; text.alignment = TextAlignmentOptions.TopLeft;
            if (minimumHeight > 0) { var element = root.gameObject.AddComponent<LayoutElement>(); element.minHeight = minimumHeight; element.flexibleHeight = 0; }
            textSizes.Add(new TextScaleRecord { Text = text, Size = size });
            root.gameObject.SetActive(true);
            return text;
        }
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false); return rect;
        }
        private static void Stretch(RectTransform root) { Stretch(root, Vector2.zero, Vector2.zero); }
        private static void Stretch(RectTransform root, Vector2 min, Vector2 max) { root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = min; root.offsetMax = max; }
        private static void Layout(RectTransform root, float height) { var element = root.gameObject.AddComponent<LayoutElement>(); element.minHeight = height; element.preferredHeight = height; element.flexibleHeight = 0; }
        private static RectTransform Row(Transform parent, float height)
        {
            RectTransform root = CreateRect("Responsive button row", parent); Layout(root, height);
            var row = root.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = 5; row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = true; row.childForceExpandHeight = true; return root;
        }
        private void Spacer(float height) { Layout(CreateRect("Space", content), height); }
        private void InvalidatePanel() { panelKey = null; }
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
        private void UpdateSafeArea()
        {
            previousSafeArea = Screen.safeArea; previousScreen = new Vector2(Screen.width, Screen.height);
            Rect normalized = NormalizeSafeArea(previousSafeArea, previousScreen);
            safeRoot.anchorMin = normalized.min; safeRoot.anchorMax = normalized.max;
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
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
            safeRoot = content = pipFrame = null; pipImage = null; panelKey = null;
            textSizes.Clear(); aimButtons.Clear(); previousTextScale = -1;
        }
        private static string StateName(GraspState state)
        {
            switch (state.ToString())
            {
                case "Aiming": return "定位 / AIM";
                case "Approaching": case "Lowering": return "安全接近 / APPROACH";
                case "ContactDescent": return "低速接触 / CONTACT";
                case "Closing": return "验证抓持 / GRIP";
                case "Lifting": return "提升 / LIFT";
                case "Transporting": return "移送 / RETURN";
                case "Releasing": return "释放 / RELEASE";
                case "Settling": return "等待稳定 / SETTLE";
                case "Result": return "本次结果";
                case "End": return "本局结束";
                case "Error": return "需要重置";
                default: return "准备中";
            }
        }
        private static string Hint(ClawSession session)
        {
            if (session.State == GraspState.Aiming) return "先正面看左右，再侧面看深度。对准对象上方后按 Enter，机构会安全接近并低速寻找抓点。";
            if (session.State != GraspState.Result && session.State != GraspState.End) return "请观察接近、接触和抓持过程。只有目标释放后完整进入出口并稳定下来才获奖。";
            if (session.Awarded) return "目标已稳定进入出口！耐心观察和合适的抓点让这次移送成功了。";
            switch (session.Failure.ToString())
            {
                case "Misaligned": return "抓点偏离可抓区域。下次用侧面检查深度，偏心对象要靠近标记抓点。";
                case "NoContact": return "有效表面接触不足。请对准可包围的部位，避免只碰到边缘。";
                case "Penetration": case "Penetrating": case "Obstructed": return "检测到穿透或障碍，已拒绝建立抓持。请换一个无遮挡的抓点。";
                case "UnstableGrip": return "接触无法稳定支撑重量或重心。请抓更靠近重心的部位。";
                case "Slipped": return "礼物在提升或移送时滑落。请更靠近推荐抓点，减少偏心力矩。";
                case "WrongTarget": return "投放的是其他对象。请先确认任务目标，再选择要抓取的物体。";
                case "Timeout": return "机构或稳定检测超时。可重置当前挑战，再检查接近路径。";
                default: return "目标没有稳定进入出口。按下一次尝试会还原所有未获奖物体，次数继续计算。";
            }
        }
    }
}
