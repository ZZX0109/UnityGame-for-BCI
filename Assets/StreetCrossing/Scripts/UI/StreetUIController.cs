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
        private ScrollRect pageScroll;
        private TextMeshProUGUI objectiveTitle, objectiveStatus, guideTitle, guideBody, markerText, controls;
        private Button helpButton;
        private TextMeshProUGUI helpCaption;
        private bool settings, confirmRestart, redraw = true, previousLargeText;
        private bool previousHelpRequested;
        private string previousMissionId, lastPageKey;
        private int previousDistrictIndex = -1, previousMissionIndex = -1, previousVariant = -1, previousSegment = -1;
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
        public RectTransform GuidancePanel => guide;
        public Button HelpButton => helpButton;
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
        private Button Button(string name, Transform parent, string label, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            Button button = ui.Button(name, parent, label, action, primary);
            TextMeshProUGUI caption = button.GetComponentInChildren<TextMeshProUGUI>();
            if (caption != null) scalableText.Add(new KeyValuePair<TextMeshProUGUI, float>(caption, caption.fontSize));
            return button;
        }
        private void BuildHud()
        {
            hud = ui.Rect("First person HUD", canvasRect); StreetUIElements.Stretch(hud);
            objective = ui.Rect("Objective card", hud); objective.anchorMin = objective.anchorMax = new Vector2(0, 1); objective.pivot = new Vector2(0, 1); objective.anchoredPosition = new Vector2(24, -24);
            ui.Image(objective, new Color(0.97f, 0.95f, 0.88f, 0.93f)); StreetUIElements.Vertical(objective, 16, 5);
            objectiveTitle = Text("Objective", objective, "", 22); objectiveStatus = Text("Observation status", objective, "", 16, StreetUIElements.Muted);
            Button pause = Button("Pause button", hud, "暂停\nEsc", () => game.Pause());
            RectTransform pauseRect = pause.GetComponent<RectTransform>(); pauseRect.anchorMin = pauseRect.anchorMax = Vector2.one; pauseRect.pivot = Vector2.one; pauseRect.anchoredPosition = new Vector2(-24, -24); pauseRect.sizeDelta = new Vector2(92, 70);
            helpButton = Button("Request help", hud, "查看帮助  F1", OpenHelp);
            RectTransform helpRect = helpButton.GetComponent<RectTransform>(); helpRect.anchorMin = helpRect.anchorMax = Vector2.one; helpRect.pivot = Vector2.one;
            helpRect.anchoredPosition = new Vector2(-24, -108); helpRect.sizeDelta = new Vector2(186, 54);
            helpCaption = helpButton.GetComponentInChildren<TextMeshProUGUI>();
            guide = ui.Rect("Kerb and feedback guide", hud); guide.anchorMin = guide.anchorMax = new Vector2(0.5f, 0); guide.pivot = new Vector2(0.5f, 0); guide.anchoredPosition = new Vector2(0, 48);
            ui.Image(guide, new Color(0.97f, 0.95f, 0.88f, 0.94f)); StreetUIElements.Vertical(guide, 16, 4);
            guideTitle = Text("Guidance title", guide, "", 20); guideBody = Text("Guidance detail", guide, "", 17, StreetUIElements.Muted);
            RectTransform controlsBar = ui.Rect("Controls contrast bar", hud); controlsBar.anchorMin = new Vector2(0, 0); controlsBar.anchorMax = new Vector2(1, 0);
            controlsBar.offsetMin = new Vector2(24, 8); controlsBar.offsetMax = new Vector2(-24, 42);
            ui.Image(controlsBar, StreetUIElements.Paper);
            controls = Text("Controls", controlsBar, "WASD 行走   鼠标观察   Shift 快步走   Esc 暂停", 18, StreetUIElements.Ink);
            controls.alignment = TextAlignmentOptions.Midline; StreetUIElements.Stretch(controls.rectTransform, 8, 8, 4, 4);
            marker = ui.Rect("World destination marker", hud); marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f); marker.pivot = new Vector2(0.5f, 0.5f); marker.sizeDelta = new Vector2(228, 62);
            ui.Image(marker, new Color(0.18f, 0.3f, 0.23f, 0.94f)); markerText = Text("Destination direction", marker, "", 17, Color.white);
            markerText.alignment = TextAlignmentOptions.Midline; StreetUIElements.Stretch(markerText.rectTransform, 12, 12, 6, 6);
        }
        private void Update()
        {
            // The first-person cursor is locked. F1 and the pause-page button make
            // on-demand help reachable without changing the player input interface.
            if (game != null && (game.IsPlaying || game.State == StreetGameState.Paused) && Input.GetKeyDown(KeyCode.F1)) OpenHelp();
        }
        private void OpenHelp()
        {
            if (game == null || game.Mission == null || (game.State != StreetGameState.Playing && game.State != StreetGameState.Paused)) return;
            if (!game.HelpRequested) game.RequestHelp();
            redraw = true;
        }
        private void LateUpdate()
        {
            if (game == null || canvas == null) return;
            if (game.State != previousState) { previousState = game.State; redraw = true; }
            string missionId = game.Mission == null ? "" : game.Mission.missionId;
            if (game.SelectedScenarioIndex != previousDistrictIndex || game.SelectedMissionIndex != previousMissionIndex || missionId != previousMissionId || game.SelectedVariant != previousVariant || game.CurrentSegmentIndex != previousSegment || game.HelpRequested != previousHelpRequested)
            {
                previousDistrictIndex = game.SelectedScenarioIndex; previousMissionIndex = game.SelectedMissionIndex;
                previousMissionId = missionId; previousVariant = game.SelectedVariant;
                previousSegment = game.CurrentSegmentIndex; previousHelpRequested = game.HelpRequested; redraw = true;
            }
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
            objective.sizeDelta = new Vector2(Mathf.Min(620, width - 240), StreetPreferences.LargeText ? 158 : 138);
            guide.sizeDelta = new Vector2(Mathf.Min(800, width - 48), StreetPreferences.LargeText ? 178 : 156);
            if (!playing || game.Evaluation == null || game.scenario == null) return;
            StreetScenarioDefinition scenario = game.scenario; CrossingEvaluator result = game.Evaluation;
            bool guided = game.Mission == null || game.Mission.assistance == StreetAssistance.Guided;
            bool detailed = guided || game.HelpRequested;
            objectiveTitle.text = game.DestinationName;
            string missionTitle = game.Mission == null ? scenario.title : game.Mission.title;
            objectiveStatus.text = missionTitle + "  ·  配置 " + VariantName(game.SelectedVariant) + "\n第 " + (game.CurrentSegmentIndex + 1) + " / " + Mathf.Max(1, game.SegmentCount) + " 段"
                + (detailed ? "  ·  左侧 " + ObservationLabel(result.LeftObserved, scenario.requireLeftObservation) + " / 右侧 " + ObservationLabel(result.RightObserved, scenario.requireRightObservation) : "");
            helpButton.gameObject.SetActive(true);
            helpButton.interactable = !game.HelpRequested;
            helpCaption.text = game.HelpRequested ? "帮助已打开" : "查看帮助  F1";
            controls.text = "WASD 行走   鼠标观察   Shift 快步走   F1 查看帮助   Esc 暂停";
            string notice = game.Notice;
            bool hasNotice = !string.IsNullOrEmpty(notice);
            guide.gameObject.SetActive(detailed || hasNotice);
            guideTitle.text = hasNotice ? "现场提醒" : GuidanceTitle();
            guideBody.text = hasNotice ? notice : GuidanceText();
            if (detailed) UpdateMarker(width, height);
            else marker.gameObject.SetActive(false);
        }
        private string GuidanceTitle()
        {
            if (game.CrossingCompleted) return "已完成过街  /  沿人行道前往目的地";
            if (game.Evaluation.IsCrossing) return "保持在斑马线内";
            return game.IsAtKerb ? "路沿等待区  /  观察、确认，再出发" : "先沿人行道走到斑马线入口";
        }
        private string GuidanceText()
        {
            CrossingEvaluator result = game.Evaluation;
            if (game.CrossingCompleted) return "沿人行道完成本段行程。方向标指向当前安全路线目标；无需抢时间。";
            if (game.IsAtKerb && !result.IsCrossing)
                return "向左看 " + ObservationLabel(result.LeftObserved, game.scenario.requireLeftObservation) + "   ·   查看行人灯   ·   向右看 " + ObservationLabel(result.RightObserved, game.scenario.requireRightObservation)
                    + "\n确认道路可见、车辆已清空，再开始过街。提示不能替代实际信号与车流。";
            return result.Feedback;
        }
        private void UpdateMarker(float width, float height)
        {
            if (game.Player == null || game.Player.View == null) { marker.gameObject.SetActive(false); return; }
            marker.gameObject.SetActive(true);
            Vector3 target = game.NavigationTarget;
            Vector3 point = game.Player.View.WorldToScreenPoint(target + Vector3.up * 2.1f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, point, null, out Vector2 local);
            bool behind = point.z <= 0;
            if (behind) local = -local;
            local.x = Mathf.Clamp(local.x, -width * 0.5f + 136, width * 0.5f - 136);
            float bottom = guide.gameObject.activeSelf ? guide.anchoredPosition.y + guide.sizeDelta.y + 44 : 110;
            local.y = Mathf.Clamp(local.y, -height * 0.5f + bottom, height * 0.5f - objective.sizeDelta.y - 68);
            marker.anchoredPosition = local;
            Vector3 delta = target - game.PlayerPosition; delta.y = 0;
            markerText.text = (behind ? "转身寻找下一目标" : game.DestinationName) + "\n下一路段 " + delta.magnitude.ToString("F0") + " m";
        }
        private void ClearPanel()
        {
            for (int i = panel.childCount - 1; i >= 0; i--)
            { GameObject child = panel.GetChild(i).gameObject; child.SetActive(false); Destroy(child); }
            scalableText.RemoveAll(pair => pair.Key == null || pair.Key.transform.IsChildOf(panel));
        }
        private void RefreshPage()
        {
            if (panel == null) return;
            redraw = false;
            if (game.IsPlaying && !HasModal) return;
            // Layout must also be active before restoring a normalized scroll
            // position; otherwise the new content still has unmeasured bounds.
            overlay.gameObject.SetActive(true);
            string pageKey = settings ? "settings" : confirmRestart ? "restart" : game.State.ToString();
            bool retainPosition = pageKey == lastPageKey;
            string selectedName = retainPosition && eventSystem != null && eventSystem.currentSelectedGameObject != null
                && eventSystem.currentSelectedGameObject.transform.IsChildOf(panel) ? eventSystem.currentSelectedGameObject.name : null;
            float previousScroll = retainPosition && pageScroll != null ? pageScroll.verticalNormalizedPosition : 1;
            if (eventSystem != null && eventSystem.currentSelectedGameObject != null && eventSystem.currentSelectedGameObject.transform.IsChildOf(panel)) eventSystem.SetSelectedGameObject(null);
            ClearPanel(); lastPageKey = pageKey;
            string kicker = game.scenario == null ? "STREET CROSSING" : game.scenario.scenarioId + "  /  FIRST PERSON STREET CROSSING";
            Text("Page eyebrow", panel, kicker, 15, StreetUIElements.Muted);
            string title = confirmRestart ? "重新开始本次任务？" : settings ? "舒适与显示设置" : game.State == StreetGameState.Menu ? "选择街区与任务" : game.State == StreetGameState.Paused ? "任务已暂停" : game.State == StreetGameState.SegmentComplete ? "已到店  /  准备下一段" : game.MissionSafetySuccess ? "已安全完成整个任务" : "本次任务的回顾";
            Text("Page title", panel, title, 32);
            RectTransform content = ui.ScrollContent(panel);
            // Returning from play rebuilds the page before UpdatePresentation
            // reactivates its overlay. Its real scroll ancestor is still valid.
            pageScroll = content.GetComponentInParent<ScrollRect>(true);
            Selectable focus;
            if (confirmRestart) focus = RestartPage(content);
            else if (settings) focus = SettingsPage(content);
            else if (game.State == StreetGameState.Menu) focus = MenuPage(content);
            else if (game.State == StreetGameState.Paused) focus = PausePage(content);
            else if (game.State == StreetGameState.SegmentComplete) focus = SegmentPage(content);
            else focus = ResultPage(content);
            ApplyTextScale();
            if (retainPosition)
            {
                Canvas.ForceUpdateCanvases();
                if (pageScroll != null) pageScroll.verticalNormalizedPosition = previousScroll;
                if (!string.IsNullOrEmpty(selectedName))
                    foreach (Selectable candidate in panel.GetComponentsInChildren<Selectable>())
                        if (candidate.gameObject.activeInHierarchy && candidate.interactable && candidate.name == selectedName) { focus = candidate; break; }
            }
            if (!game.IsPlaying && focus != null && eventSystem != null) eventSystem.SetSelectedGameObject(focus.gameObject);
        }
        private RectTransform Actions() { RectTransform actions = ui.Rect("Page actions", panel); StreetUIElements.Horizontal(actions); StreetUIElements.Size(actions.gameObject, 54); return actions; }
        private Selectable MenuPage(RectTransform content)
        {
            Text("Menu introduction", content, "先选街区，再选任务与固定交通配置。所有任务都可直接练习；等待不扣安全分，帮助和舒适设置不降低评价。", 20);
            Text("District selection heading", content, "街区  /  三种道路情境", 21);
            for (int i = 0; i < game.Scenarios.Count; i++)
            {
                int index = i; StreetScenarioDefinition definition = game.Scenarios[i]; bool selected = index == game.SelectedScenarioIndex;
                RectTransform card = ChoiceCard("Scenario " + definition.scenarioId, content, selected, () => { game.SelectScenario(index); redraw = true; }, StreetPreferences.LargeText ? 112 : 98);
                Text("Scenario title", card, (selected ? "已选择  " : "选择  ") + definition.title, 21);
                Text("Scenario summary", card, "建议从" + TierName(StreetMissionProgress.RecommendedTier(definition.scenarioId)) + "任务开始  ·  可自由选择任意梯度", 17, StreetUIElements.Muted);
            }
            Text("Mission selection heading", content, "任务  /  引导、标准与挑战", 21);
            if (game.Missions != null)
                for (int i = 0; i < game.Missions.Count; i++)
                {
                    int index = i; var definition = game.Missions[i]; bool selected = index == game.SelectedMissionIndex;
                    RectTransform card = ChoiceCard("Mission " + definition.missionId, content, selected, () => { game.SelectMission(index); redraw = true; }, StreetPreferences.LargeText ? 192 : 168);
                    Text("Mission title", card, (selected ? "已选择  " : "选择  ") + TierName(definition.tier) + "  /  " + definition.title, 21);
                    Text("Mission summary", card, definition.description, 17, StreetUIElements.Muted);
                    Text("Mission progress", card, "任务最佳 " + StreetMissionProgress.BestStars(definition) + " / 3 星  ·  安全完成配置 " + StreetMissionProgress.CompletedVariantCount(definition) + " / 3", 17, StreetUIElements.Muted);
                    Text("Mission assistance", card, AssistanceDescription(definition.assistance), 16, StreetUIElements.Muted);
                }
            Text("Traffic variant heading", content, "固定交通配置", 21);
            Text("Traffic variant information", content, "A、B、C 是同一任务的三组固定车流；配置编号不代表难度。重试可复现当前配置。", 17, StreetUIElements.Muted);
            for (int i = 0; i < 3; i++)
            {
                int variant = i; bool selected = variant == game.SelectedVariant;
                int stars = game.Mission == null ? 0 : StreetMissionProgress.VariantStars(game.Mission, variant);
                string completion = stars > 0 ? "已完成 " + stars + " / 3 星" : "尚未完成";
                Button choice = Button("Traffic variant " + variant, content, (selected ? "已选择  " : "选择  ") + "配置 " + VariantName(variant) + "  ·  " + completion, () => { game.SelectVariant(variant); redraw = true; });
                choice.GetComponent<Image>().color = selected ? StreetUIElements.SoftGreen : new Color(0.91f, 0.91f, 0.84f);
            }
            Text("Controls reference", content, "WASD 连续行走  ·  鼠标环顾  ·  Shift 快步走\nE 查看标识  ·  F1 查看帮助  ·  Esc 暂停  ·  R 确认重开", 17, StreetUIElements.Muted);
            RectTransform actions = Actions(); Button start = Button("Start practice", actions, "开始任务", () => game.StartPractice(), true);
            Button("Open settings", actions, "设置", ShowSettings); return start;
        }
        private RectTransform ChoiceCard(string name, RectTransform parent, bool selected, UnityEngine.Events.UnityAction action, float height)
        {
            Button choice = Button(name, parent, "", action);
            choice.GetComponent<Image>().color = selected ? StreetUIElements.SoftGreen : new Color(0.91f, 0.91f, 0.84f);
            choice.GetComponent<LayoutElement>().preferredHeight = height;
            Transform caption = choice.transform.Find("Caption"); caption.gameObject.SetActive(false); Destroy(caption.gameObject);
            RectTransform card = ui.Rect(name + " description", choice.transform); StreetUIElements.Stretch(card, 18, 18, 12, 12);
            StreetUIElements.Vertical(card, 0, 5); return card;
        }
        private Selectable PausePage(RectTransform content)
        {
            Text("Pause explanation", content, "交通、信号和行走已同步暂停。返回后继续观察，确认环境，再完成过街。", 21);
            Text("Pause controls", content, "Esc 可以继续练习。窗口失去焦点时会自动暂停。", 18, StreetUIElements.Muted);
            if (game.Mission != null)
            {
                Button help = Button("Pause request help", content, game.HelpRequested ? "帮助已打开" : "查看帮助  F1", OpenHelp);
                help.interactable = !game.HelpRequested;
                Text("Help scoring information", content, "帮助单独记录，不扣安全分。字号、音量与舒适模式不受任务梯度影响。", 17, StreetUIElements.Muted);
                if (game.HelpRequested)
                {
                    Text("Requested help title", content, GuidanceTitle(), 20);
                    Text("Requested help detail", content, GuidanceText(), 18, StreetUIElements.Muted);
                }
            }
            Button("Pause settings", content, "设置", ShowSettings);
            Button("Restart practice", content, "重新开始整个任务", ShowRestartConfirmation);
            Button("Return to scenario menu", content, "返回任务选择", () => game.ReturnToMenu());
            return Button("Resume practice", Actions(), "继续任务", () => game.Resume(), true);
        }
        private Selectable SegmentPage(RectTransform content)
        {
            Text("Segment destination", content, "本段已抵达：" + game.DestinationName, 22);
            Text("Segment continuation", content, "准备好后继续下一段。你将从当前位置返程；交通和信号保留当前进度，左右观察将重新记录。", 20);
            Text("Segment results", content, game.MissionSummary, 18, StreetUIElements.Muted);
            Text("Segment scoring information", content, "整项任务按所有路段评价。前一段的风险记录不会被返程成功覆盖，等待和帮助不扣安全分。", 17, StreetUIElements.Muted);
            Button("Segment settings", content, "设置", ShowSettings);
            Button("Segment return menu", content, "返回任务选择", () => game.ReturnToMenu());
            return Button("Continue mission", Actions(), "准备好了，继续返程", () => game.ContinueMission(), true);
        }
        private Selectable RestartPage(RectTransform content)
        {
            Text("Restart explanation", content, "整个任务将从第一段重新开始。玩家位置、交通周期、车辆、观察和帮助记录一起重置；同一配置会重现相同车流。", 21);
            Text("Restart note", content, "取消后仍保持暂停，便于准备好再继续。", 18, StreetUIElements.Muted);
            RectTransform actions = Actions(); Button("Confirm restart", actions, "确认重开", () => { CloseModal(); game.StartPractice(); }, true);
            return Button("Cancel restart", actions, "取消", CloseModal);
        }
        private Selectable ResultPage(RectTransform content)
        {
            Text("Result feedback", content, game.MissionSafetySuccess ? "所有必需路段均已安全完成。" : "请查看各段的实际风险与未完成原因，再选择是否重新练习。", 22);
            Text("Safety score", content, "安全评价  " + game.Stars + " / 3", 24);
            Text("Mission segment results", content, game.MissionSummary, 19);
            Text("Contact explanation", content, "危险接近、静止车辆接触和行驶碰撞分别记录。整项评价保留各段风险，不以一次幸运抵达代替安全过程。", 17, StreetUIElements.Muted);
            Text("Timing", content, "任务总时长 " + game.MissionElapsedSeconds.ToString("F1") + " 秒   ·   主动查看帮助 " + game.AssistanceRequests + " 次\n时间和帮助不加减安全分。观察记录表示镜头看到道路，不是眼动或注意力测量。", 17, StreetUIElements.Muted);
            if (game.Mission != null)
                Text("Best result", content, "本任务最佳 " + StreetMissionProgress.BestStars(game.Mission) + " / 3 星  ·  安全完成配置 " + StreetMissionProgress.CompletedVariantCount(game.Mission) + " / 3", 17, StreetUIElements.Muted);
            if (!string.IsNullOrEmpty(game.LogError)) Text("Log availability", content, "本次过程记录暂不可用：" + game.LogError, 16, StreetUIElements.Muted);
            RectTransform actions = Actions(); Button retry = Button("Try again", actions, "重试当前配置", () => game.StartPractice(), true);
            Button("Choose another scenario", actions, "选择其他任务或配置", () => game.ReturnToMenu()); return retry;
        }
        private Selectable SettingsPage(RectTransform content)
        {
            SliderRow(content, "鼠标灵敏度", 0.3f, 5, StreetPreferences.Sensitivity, value => StreetPreferences.Sensitivity = value, "F1");
            SliderRow(content, "视野 FOV", 75, 85, StreetPreferences.FieldOfView, value => StreetPreferences.FieldOfView = value, "F0");
            SliderRow(content, "提示音音量", 0, 1, StreetPreferences.Volume, value => StreetPreferences.Volume = value, "F2");
            SliderRow(content, "街道环境音", 0, 1, StreetPreferences.AmbientVolume, value => StreetPreferences.AmbientVolume = value, "F2");
            ToggleRow(content, "慢速舒适模式", StreetPreferences.ComfortMode, value => StreetPreferences.ComfortMode = value);
            ToggleRow(content, "允许 Shift 快步走", StreetPreferences.FastWalkEnabled, value => StreetPreferences.FastWalkEnabled = value);
            ToggleRow(content, "放大辅助文字", StreetPreferences.LargeText, value => { StreetPreferences.LargeText = value; redraw = true; });
            ToggleRow(content, "全屏显示", Screen.fullScreen, value => Screen.fullScreen = value);
            Text("Comfort information", content, "无镜头摇晃、无跳跃。信号同时使用灯位和文字区分；音效不是唯一的过街信息。", 17, StreetUIElements.Muted);
            return Button("Save settings", Actions(), "保存并返回", CloseModal, true);
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
        private static string VariantName(int variant) => variant == 1 ? "B" : variant == 2 ? "C" : "A";
        private static string TierName(int tier) => tier == 2 ? "标准" : tier == 3 ? "挑战" : "引导";
        private static string AssistanceDescription(StreetAssistance assistance)
        {
            switch (assistance)
            {
                case StreetAssistance.RiskOnly: return "现场提醒：目标与真实风险常显，逐步提示可按 F1 查看。";
                case StreetAssistance.OnDemand: return "按需帮助：先独立判断；需要时按 F1，帮助不扣安全分。";
                default: return "引导帮助：显示路沿观察、确认信号和分段行走提示。";
            }
        }
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
