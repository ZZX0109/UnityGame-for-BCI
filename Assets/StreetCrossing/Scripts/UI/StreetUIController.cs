using System.Collections.Generic;
using StreetCrossing.Core;
using StreetCrossing.Evaluation;
using StreetCrossing.Save;
using StreetCrossing.Traffic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StreetCrossing.UI
{
    /// <summary>Responsive runtime Canvas/TMP interface; world signals remain the source for crossing decisions.</summary>
    public sealed class StreetUIController : MonoBehaviour
    {
        private StreetGameManager game;
        private StreetTMPFont runtimeFont;
        private StreetUIElements ui;
        private Canvas canvas;
        private RectTransform canvasRect, hud, objective, guide, marker, overlay, panel;
        private TextMeshProUGUI objectiveTitle, objectiveStatus, guideTitle, guideBody, markerText, controls;
        private bool settings, confirmRestart, redraw = true, previousLargeText;
        private StreetGameState previousState;
        private EventSystem ownedEventSystem, eventSystem;
        private bool previousNavigationEvents;
        private readonly List<KeyValuePair<TextMeshProUGUI, float>> scalableText = new List<KeyValuePair<TextMeshProUGUI, float>>();
        public bool HasModal => settings || confirmRestart;
        // Stable hooks for PlayMode layout/import checks without coupling tests to text content.
        public Canvas RuntimeCanvas => canvas;
        public TMP_FontAsset RuntimeFont => runtimeFont?.Asset;
        public RectTransform ModalPanel => panel;
        public RectTransform DestinationMarker => marker;
        public bool SettingsVisible => settings;

        public void Initialize(StreetGameManager owner, Font ownFont)
        {
            game = owner; runtimeFont = new StreetTMPFont(ownFont); ui = new StreetUIElements(runtimeFont.Asset);
            canvasRect = ui.Rect("Street Canvas", transform);
            canvas = canvasRect.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasRect.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = 0.5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null)
            {
                GameObject events = new GameObject("Street UI EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false); ownedEventSystem = events.GetComponent<EventSystem>();
            }
            eventSystem = ownedEventSystem != null ? ownedEventSystem : EventSystem.current;
            previousNavigationEvents = eventSystem != null && eventSystem.sendNavigationEvents;
            BuildHud(); overlay = ui.Rect("Menu and modal overlay", canvasRect); StreetUIElements.Stretch(overlay);
            ui.Image(overlay, new Color(0.12f, 0.18f, 0.14f, 0.6f), true);
            panel = ui.Rect("Responsive modal panel", overlay); panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f); panel.pivot = new Vector2(0.5f, 0.5f);
            ui.Image(panel, StreetUIElements.Paper, true); StreetUIElements.Vertical(panel, 26, 12);
            previousState = game.State; previousLargeText = StreetPreferences.LargeText;
            RefreshPage(); UpdatePresentation();
        }
        public void CloseModal() { settings = confirmRestart = false; StreetPreferences.Save(); redraw = true; }
        public void ShowRestartConfirmation() { if (game != null && game.IsPlaying) game.Pause(); confirmRestart = true; settings = false; redraw = true; }
        public void ShowSettings() { if (game != null && game.IsPlaying) game.Pause(); settings = true; confirmRestart = false; redraw = true; }
        private TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color? color = null)
        {
            TextMeshProUGUI text = ui.Text(name, parent, value, size, color); scalableText.Add(new KeyValuePair<TextMeshProUGUI, float>(text, size)); return text;
        }
        private void BuildHud()
        {
            hud = ui.Rect("First person HUD", canvasRect); StreetUIElements.Stretch(hud);
            objective = ui.Rect("Objective card", hud); objective.anchorMin = objective.anchorMax = new Vector2(0, 1); objective.pivot = new Vector2(0, 1); objective.anchoredPosition = new Vector2(24, -24);
            ui.Image(objective, new Color(0.97f, 0.95f, 0.88f, 0.93f)); StreetUIElements.Vertical(objective, 16, 5);
            objectiveTitle = Text("Objective", objective, "", 22); objectiveStatus = Text("Observation status", objective, "", 16, StreetUIElements.Muted);
            Button pause = ui.Button("Pause button", hud, "暂停\nEsc", () => game.Pause());
            RectTransform pauseRect = pause.GetComponent<RectTransform>(); pauseRect.anchorMin = pauseRect.anchorMax = Vector2.one; pauseRect.pivot = Vector2.one; pauseRect.anchoredPosition = new Vector2(-24, -24); pauseRect.sizeDelta = new Vector2(92, 70);
            guide = ui.Rect("Kerb and feedback guide", hud); guide.anchorMin = guide.anchorMax = new Vector2(0.5f, 0); guide.pivot = new Vector2(0.5f, 0); guide.anchoredPosition = new Vector2(0, 48);
            ui.Image(guide, new Color(0.97f, 0.95f, 0.88f, 0.94f)); StreetUIElements.Vertical(guide, 16, 4);
            guideTitle = Text("Guidance title", guide, "", 20); guideBody = Text("Guidance detail", guide, "", 17, StreetUIElements.Muted);
            controls = Text("Controls", hud, "WASD 行走   鼠标观察   Shift 快步走   Esc 暂停", 15, Color.white);
            controls.alignment = TextAlignmentOptions.Midline; controls.rectTransform.anchorMin = new Vector2(0, 0); controls.rectTransform.anchorMax = new Vector2(1, 0);
            controls.rectTransform.offsetMin = new Vector2(24, 12); controls.rectTransform.offsetMax = new Vector2(-24, 38);
            marker = ui.Rect("World destination marker", hud); marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f); marker.pivot = new Vector2(0.5f, 0.5f); marker.sizeDelta = new Vector2(228, 62);
            ui.Image(marker, new Color(0.18f, 0.3f, 0.23f, 0.94f)); markerText = Text("Destination direction", marker, "", 17, Color.white);
            markerText.alignment = TextAlignmentOptions.Midline; StreetUIElements.Stretch(markerText.rectTransform, 12, 12, 6, 6);
        }
        private void LateUpdate()
        {
            if (game == null || canvas == null) return;
            if (game.State != previousState) { previousState = game.State; redraw = true; }
            if (redraw) RefreshPage();
            if (previousLargeText != StreetPreferences.LargeText) { previousLargeText = StreetPreferences.LargeText; ApplyTextScale(); }
            UpdatePresentation();
            ScrollSelectionIntoView();
        }
        private void ScrollSelectionIntoView()
        {
            if (game.IsPlaying || eventSystem == null || eventSystem.currentSelectedGameObject == null) return;
            RectTransform selected = eventSystem.currentSelectedGameObject.GetComponent<RectTransform>();
            ScrollRect scroll = selected == null ? null : selected.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null || scroll.viewport == null) return;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, selected);
            Rect visible = scroll.viewport.rect; Vector2 position = scroll.content.anchoredPosition;
            if (bounds.min.y < visible.yMin) position.y += visible.yMin - bounds.min.y;
            else if (bounds.max.y > visible.yMax) position.y -= bounds.max.y - visible.yMax;
            position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, scroll.content.rect.height - visible.height));
            scroll.content.anchoredPosition = position;
        }
        private void UpdatePresentation()
        {
            bool playing = game.IsPlaying; hud.gameObject.SetActive(playing); overlay.gameObject.SetActive(!playing);
            if (eventSystem != null) eventSystem.sendNavigationEvents = !playing;
            float width = canvasRect.rect.width, height = canvasRect.rect.height;
            if (width <= 0 || height <= 0) return;
            panel.sizeDelta = new Vector2(Mathf.Min(900, width - 48), Mathf.Min(670, height - 48));
            objective.sizeDelta = new Vector2(Mathf.Min(500, width - 164), StreetPreferences.LargeText ? 120 : 104);
            guide.sizeDelta = new Vector2(Mathf.Min(760, width - 48), StreetPreferences.LargeText ? 126 : 108);
            if (!playing || game.Evaluation == null || game.scenario == null) return;
            StreetScenarioDefinition scenario = game.scenario; CrossingEvaluator result = game.Evaluation;
            objectiveTitle.text = scenario.scenarioId + "  /  " + scenario.destinationName;
            objectiveStatus.text = result.IsCrossing ? "沿斑马线完成过街，继续观察车辆" : "左侧 " + ObservationLabel(result.LeftObserved, scenario.requireLeftObservation) + "    右侧 " + ObservationLabel(result.RightObserved, scenario.requireRightObservation);
            Vector3 position = game.PlayerPosition;
            bool atKerb = game.IsAtKerb;
            guideTitle.text = atKerb && !result.IsCrossing ? "路沿等待区  /  观察、确认，再出发" : result.IsCrossing ? "保持在斑马线内" : position.z > scenario.roadHalfWidth ? "继续前往目的地" : "先沿人行道走到斑马线入口";
            guideBody.text = game.Notice.Length > 0 ? game.Notice : atKerb && !result.IsCrossing
                ? "向左看 " + ObservationLabel(result.LeftObserved, scenario.requireLeftObservation) + "   ·   查看行人灯   ·   向右看 " + ObservationLabel(result.RightObserved, scenario.requireRightObservation) + "\n确认来车已停稳。辅助提示不能替代街道中的信号与车流。"
                : result.Feedback;
            UpdateMarker(scenario, width, height);
        }
        private void UpdateMarker(StreetScenarioDefinition scenario, float width, float height)
        {
            if (game.Player == null || game.Player.View == null) { marker.gameObject.SetActive(false); return; }
            marker.gameObject.SetActive(true);
            Vector3 point = game.Player.View.WorldToScreenPoint(scenario.destination + Vector3.up * 2.1f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, point, null, out Vector2 local);
            bool behind = point.z <= 0;
            if (behind) local = -local;
            local.x = Mathf.Clamp(local.x, -width * 0.5f + 136, width * 0.5f - 136);
            local.y = Mathf.Clamp(local.y, -height * 0.5f + 208, height * 0.5f - 174);
            marker.anchoredPosition = local;
            Vector3 delta = scenario.destination - game.PlayerPosition; delta.y = 0;
            markerText.text = (behind ? "转身寻找目的地" : scenario.destinationName) + "\n" + delta.magnitude.ToString("F0") + " m";
        }
        private void ClearPanel()
        {
            for (int i = panel.childCount - 1; i >= 0; i--)
            { GameObject child = panel.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
            scalableText.RemoveAll(pair => pair.Key == null || pair.Key.transform.IsChildOf(panel));
        }
        private void RefreshPage()
        {
            if (panel == null) return; redraw = false; ClearPanel();
            string kicker = game.scenario == null ? "STREET CROSSING" : game.scenario.scenarioId + "  /  FIRST PERSON STREET CROSSING";
            Text("Page eyebrow", panel, kicker, 15, StreetUIElements.Muted);
            string title = confirmRestart ? "重新开始本次练习？" : settings ? "舒适与显示设置" : game.State == StreetGameState.Menu ? "街角的一次过街" : game.State == StreetGameState.Paused ? "练习已暂停" : game.Evaluation.SafetySuccess ? "安全抵达" : "这次过街的回顾";
            Text("Page title", panel, title, 32);
            RectTransform content = ui.ScrollContent(panel); Selectable focus;
            if (confirmRestart) focus = RestartPage(content);
            else if (settings) focus = SettingsPage(content);
            else if (game.State == StreetGameState.Menu) focus = MenuPage(content);
            else if (game.State == StreetGameState.Paused) focus = PausePage(content);
            else focus = ResultPage(content);
            ApplyTextScale();
            if (!game.IsPlaying && focus != null && eventSystem != null) eventSystem.SetSelectedGameObject(focus.gameObject);
        }
        private RectTransform Actions() { RectTransform actions = ui.Rect("Page actions", panel); StreetUIElements.Horizontal(actions); StreetUIElements.Size(actions.gameObject, 54); return actions; }
        private Selectable MenuPage(RectTransform content)
        {
            Text("Menu introduction", content, "选择情境，沿斑马线前往对面的目的地。安全和完整观察比速度更重要。", 20);
            for (int i = 0; i < game.Scenarios.Count; i++)
            {
                int index = i; StreetScenarioDefinition definition = game.Scenarios[i]; bool selected = index == game.SelectedScenarioIndex;
                Button choice = ui.Button("Scenario " + definition.scenarioId, content, "", () => { game.SelectScenario(index); redraw = true; });
                choice.GetComponent<Image>().color = selected ? StreetUIElements.SoftGreen : new Color(0.91f, 0.91f, 0.84f);
                choice.GetComponent<LayoutElement>().preferredHeight = StreetPreferences.LargeText ? 124 : 110;
                Destroy(choice.transform.Find("Caption").gameObject);
                RectTransform card = ui.Rect("Scenario description", choice.transform); StreetUIElements.Stretch(card, 18, 18, 12, 12); StreetUIElements.Vertical(card, 0, 5);
                Text("Scenario title", card, (selected ? "已选择  " : "选择  ") + definition.scenarioId + "  " + definition.title, 21);
                Text("Scenario summary", card, definition.description, 17, StreetUIElements.Muted);
            }
            Text("Controls reference", content, "WASD 连续行走  ·  鼠标环顾  ·  Shift 快步走\nE 查看标识  ·  Esc 暂停  ·  R 确认重开", 17, StreetUIElements.Muted);
            Text("Best result", content, "当前情境最佳安全评价  " + StreetPreferences.BestStars(game.scenario.scenarioId) + " / 3", 17, StreetUIElements.Muted);
            RectTransform actions = Actions(); Button start = ui.Button("Start practice", actions, "开始练习", () => game.StartPractice(), true);
            ui.Button("Open settings", actions, "设置", ShowSettings); return start;
        }
        private Selectable PausePage(RectTransform content)
        {
            Text("Pause explanation", content, "交通、信号和行走已同步暂停。返回后继续观察，确认环境，再完成过街。", 21);
            Text("Pause controls", content, "Esc 可以继续练习。窗口失去焦点时会自动暂停。", 18, StreetUIElements.Muted);
            ui.Button("Pause settings", content, "设置", ShowSettings);
            ui.Button("Restart practice", content, "重新开始", ShowRestartConfirmation);
            ui.Button("Return to scenario menu", content, "返回情境选择", () => game.ReturnToMenu());
            return ui.Button("Resume practice", Actions(), "继续练习", () => game.Resume(), true);
        }
        private Selectable RestartPage(RectTransform content)
        {
            Text("Restart explanation", content, "玩家位置、交通周期、车辆和本次观察记录将一起重置。同一情境会重现相同的车流。", 21);
            Text("Restart note", content, "取消后仍保持暂停，便于准备好再继续。", 18, StreetUIElements.Muted);
            RectTransform actions = Actions(); ui.Button("Confirm restart", actions, "确认重开", () => { CloseModal(); game.StartPractice(); }, true);
            return ui.Button("Cancel restart", actions, "取消", CloseModal);
        }
        private Selectable ResultPage(RectTransform content)
        {
            CrossingEvaluator result = game.Evaluation;
            Text("Result feedback", content, result.Feedback, 22);
            Text("Safety score", content, "安全评价  " + game.Stars + " / 3", 24);
            Text("Signal at entry", content, "进入时信号  /  " + (result.Attempts == 0 ? "尚未进入道路" : PhaseName(result.SignalAtEntry)), 19);
            Text("Observation details", content, "左侧观察  " + ObservationLabel(result.LeftObserved, game.scenario.requireLeftObservation) + "   ·   右侧观察  " + ObservationLabel(result.RightObserved, game.scenario.requireRightObservation)
                + "\n本情境观察要求  " + (result.ObservationRequirementsSatisfied ? "已完成" : "尚未完成"), 19);
            Text("Event distinctions", content, "规则违规 " + result.Violations + "   ·   危险接近 " + result.NearMisses
                + "\n静止车辆接触 " + result.StationaryContacts + "   ·   行驶碰撞 " + result.MovingCollisions, 19);
            Text("Contact explanation", content, "静止车辆接触、危险接近和行驶碰撞分别记录。静止接触不作为行驶碰撞；违规或未完成必要观察会影响安全完成。", 17, StreetUIElements.Muted);
            Text("Timing", content, "等待 " + result.WaitingSeconds.ToString("F1") + " 秒   ·   总时长 " + game.WorldTime.ToString("F1") + " 秒\n没有抢时间奖励。观察记录表示镜头看到道路，不是注意力测量。", 17, StreetUIElements.Muted);
            if (!string.IsNullOrEmpty(game.LogError)) Text("Log availability", content, "本次过程记录暂不可用：" + game.LogError, 16, StreetUIElements.Muted);
            RectTransform actions = Actions(); Button retry = ui.Button("Try again", actions, "再次练习", () => game.StartPractice(), true);
            ui.Button("Choose another scenario", actions, "选择情境", () => game.ReturnToMenu()); return retry;
        }
        private Selectable SettingsPage(RectTransform content)
        {
            SliderRow(content, "鼠标灵敏度", 0.3f, 5, StreetPreferences.Sensitivity, value => StreetPreferences.Sensitivity = value, "F1");
            SliderRow(content, "视野 FOV", 75, 85, StreetPreferences.FieldOfView, value => StreetPreferences.FieldOfView = value, "F0");
            SliderRow(content, "提示音音量", 0, 1, StreetPreferences.Volume, value => StreetPreferences.Volume = value, "F2");
            ToggleRow(content, "慢速舒适模式", StreetPreferences.ComfortMode, value => StreetPreferences.ComfortMode = value);
            ToggleRow(content, "允许 Shift 快步走", StreetPreferences.FastWalkEnabled, value => StreetPreferences.FastWalkEnabled = value);
            ToggleRow(content, "放大辅助文字", StreetPreferences.LargeText, value => { StreetPreferences.LargeText = value; redraw = true; });
            ToggleRow(content, "全屏显示", Screen.fullScreen, value => Screen.fullScreen = value);
            Text("Comfort information", content, "无镜头摇晃、无跳跃。信号同时使用灯位和文字区分；音效不是唯一的过街信息。", 17, StreetUIElements.Muted);
            return ui.Button("Save settings", Actions(), "保存并返回", CloseModal, true);
        }
        private void SliderRow(RectTransform parent, string label, float minimum, float maximum, float value, UnityEngine.Events.UnityAction<float> changed, string format)
        {
            RectTransform row = ui.Rect(label, parent); StreetUIElements.Vertical(row, 0, 8); StreetUIElements.Size(row.gameObject, 78);
            TextMeshProUGUI caption = Text(label + " value", row, label + "  " + value.ToString(format), 20);
            RectTransform control = ui.Rect(label + " slider", row); StreetUIElements.Size(control.gameObject, 30);
            Image track = ui.Image(control, new Color(0.78f, 0.8f, 0.71f), true);
            Slider slider = control.gameObject.AddComponent<Slider>(); slider.targetGraphic = track; slider.minValue = minimum; slider.maxValue = maximum; slider.direction = Slider.Direction.LeftToRight;
            RectTransform handleArea = ui.Rect("Handle area", control); StreetUIElements.Stretch(handleArea, 12, 12, 0, 0);
            RectTransform handle = ui.Rect("Handle", handleArea); handle.sizeDelta = new Vector2(22, 32); Image handleImage = ui.Image(handle, StreetUIElements.Green, true);
            slider.handleRect = handle; slider.targetGraphic = handleImage; slider.value = value;
            slider.onValueChanged.AddListener(next => { caption.text = label + "  " + next.ToString(format); changed(next); });
        }
        private void ToggleRow(RectTransform parent, string label, bool initial, UnityEngine.Events.UnityAction<bool> changed)
        {
            RectTransform row = ui.Rect(label, parent); StreetUIElements.Size(row.gameObject, 52); Image background = ui.Image(row, new Color(0.91f, 0.92f, 0.85f), true);
            Toggle toggle = row.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = background;
            RectTransform box = ui.Rect("Checkbox", row); box.anchorMin = box.anchorMax = new Vector2(0, 0.5f); box.pivot = new Vector2(0, 0.5f); box.anchoredPosition = new Vector2(14, 0); box.sizeDelta = new Vector2(24, 24); ui.Image(box, Color.white);
            RectTransform check = ui.Rect("Checked", box); StreetUIElements.Stretch(check, 4, 4, 4, 4); toggle.graphic = ui.Image(check, StreetUIElements.Green);
            TextMeshProUGUI text = Text("Toggle label", row, label, 20); StreetUIElements.Stretch(text.rectTransform, 52, 12, 10, 8);
            toggle.isOn = initial; toggle.onValueChanged.AddListener(changed);
        }
        private void ApplyTextScale()
        {
            float scale = StreetPreferences.LargeText ? 1.14f : 1;
            foreach (KeyValuePair<TextMeshProUGUI, float> item in scalableText) if (item.Key != null) item.Key.fontSize = item.Value * scale;
        }
        private static string ObservationLabel(bool observed, bool required) => observed ? "已记录" : required ? "待完成" : "可选";
        private static string PhaseName(TrafficPhase phase)
        {
            switch (phase)
            {
                case TrafficPhase.Walk: return "行人通行";
                case TrafficPhase.WalkFlashing: return "行人清空（不开始新的过街）";
                case TrafficPhase.VehicleGreen: return "机动车通行 / 行人等待";
                case TrafficPhase.VehicleAmber: return "机动车黄灯 / 行人等待";
                default: return "全红清空 / 行人等待";
            }
        }
        private void OnDestroy()
        {
            if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); }
            if (ownedEventSystem != null) Destroy(ownedEventSystem.gameObject);
            else if (eventSystem != null) eventSystem.sendNavigationEvents = previousNavigationEvents;
            runtimeFont?.Dispose();
        }
    }
}
