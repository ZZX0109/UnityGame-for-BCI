using System;
using System.Collections;
using FirstPersonSokoban.Core;
using FirstPersonSokoban.SaveSystem;
using UnityEngine;
using UnityEngine.UI;
using static FirstPersonSokoban.UI.UIFactory;

namespace FirstPersonSokoban.UI
{
    public sealed class GameUIController : MonoBehaviour
    {
        private PuzzleGameManager game;
        private Transform canvasRoot;
        private CanvasGroup hud;
        private CanvasGroup menu;
        private CanvasGroup pause;
        private CanvasGroup victory;
        private CanvasGroup settings;
        private CanvasGroup settingsOrigin;
        private CanvasGroup interaction;
        private CanvasGroup toast;
        private CanvasGroup guidance;
        private CanvasGroup deadlock;

        private Text levelNumber;
        private Text levelTitle;
        private Text goalCounter;
        private Text pushCounter;
        private Text interactionText;
        private Text toastText;
        private Text guidanceText;
        private Text deadlockText;
        private Text victoryStats;
        private Text victoryBest;
        private Button nextButton;
        private Button[] levelButtons;
        private Text[] levelNotes;
        private Button[] settingButtons;

        private RectTransform identityGroup;
        private RectTransform levelPanel;
        private RectTransform levelContent;
        private RectTransform continueButtonRect;
        private MenuLayoutController menuLayout;
        private Coroutine toastRoutine;

        public string CurrentGuidance { get; private set; } = string.Empty;
        public string CurrentDeadlockHint { get; private set; } = string.Empty;
        public string CurrentPrompt => interactionText == null ? string.Empty : interactionText.text;
        public RectTransform LevelPanelRect => levelPanel;
        public RectTransform IdentityGroupRect => identityGroup;
        public RectTransform LevelContentRect => levelContent;
        public RectTransform ContinueButtonRect => continueButtonRect;
        public MenuLayoutController MenuLayout => menuLayout;
        public int LevelCardCount => levelButtons == null ? 0 : levelButtons.Length;
        public RectTransform LevelCardRect(int index) => levelButtons[index].GetComponent<RectTransform>();

        public void Initialize(PuzzleGameManager manager)
        {
            game = manager;
            levelButtons = new Button[PuzzleGameManager.LevelCount];
            levelNotes = new Text[PuzzleGameManager.LevelCount];
            BuildCanvas();
            BuildHUD();
            BuildMenu();
            BuildPause();
            BuildSettings();
            BuildVictory();
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new GameObject("Game UI · Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasRoot = canvasObject.transform;
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void BuildHUD()
        {
            hud = Group(canvasRoot, "HUD");
            PanelImage(hud.transform, "Level Plate", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(32, -28), new Vector2(300, 88), Panel);
            LineImage(hud.transform, "Level Accent", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(32, -28), new Vector2(5, 88), Accent);
            levelNumber = Label(hud.transform, "Level Number", "LEVEL 01", 13, Accent,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -41), new Vector2(240, 22), style: FontStyle.Bold);
            levelTitle = Label(hud.transform, "Level Title", "初次接触", 25, White,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(54, -66), new Vector2(250, 42), style: FontStyle.Bold);

            PanelImage(hud.transform, "Stats Plate", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-32, -28), new Vector2(290, 88), Panel);
            goalCounter = Label(hud.transform, "Goals", "0 / 1", 29, Accent,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-270, -40), new Vector2(120, 40), style: FontStyle.Bold);
            Label(hud.transform, "Goals Caption", "目标归位", 13, Muted,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-267, -83), new Vector2(115, 25));
            pushCounter = Label(hud.transform, "Pushes", "00", 29, White,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128, -40), new Vector2(80, 40), TextAnchor.MiddleRight, FontStyle.Bold);
            Label(hud.transform, "Pushes Caption", "有效推动", 13, Muted,
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(-127, -83), new Vector2(78, 25), TextAnchor.MiddleRight);

            Image outer = PanelImage(hud.transform, "Crosshair Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(18, 18), Hex("58E0C2", 0.22f));
            outer.raycastTarget = false;
            Image dot = PanelImage(hud.transform, "Crosshair Dot", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(4, 4), White);
            dot.raycastTarget = false;

            interaction = Group(hud.transform, "Interaction");
            PanelImage(interaction.transform, "Prompt Plate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -92), new Vector2(400, 48), Panel);
            interactionText = Label(interaction.transform, "Prompt", "E  推动箱子", 17, Accent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -92), new Vector2(380, 44), TextAnchor.MiddleCenter, FontStyle.Bold);
            interaction.alpha = 0f;
            interaction.gameObject.SetActive(false);

            guidance = Group(hud.transform, "Guidance");
            PanelImage(guidance.transform, "Guidance Plate", new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 86), new Vector2(760, 42), Hex("142731", 0.9f));
            guidanceText = Label(guidance.transform, "Guidance Text", "", 15, Muted,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 87), new Vector2(730, 38), TextAnchor.MiddleCenter);
            guidance.gameObject.SetActive(false);

            deadlock = Group(hud.transform, "Deadlock");
            PanelImage(deadlock.transform, "Deadlock Plate", new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -142), new Vector2(720, 50), Hex("3A2A1C", 0.94f));
            deadlockText = Label(deadlock.transform, "Deadlock Text", "", 16, Amber,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -143), new Vector2(690, 46), TextAnchor.MiddleCenter, FontStyle.Bold);
            deadlock.gameObject.SetActive(false);

            PanelImage(hud.transform, "Controls", new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(32, 28), new Vector2(690, 44), Hex("0D1921", 0.82f));
            Label(hud.transform, "Control Labels", "WASD  移动     E  推动     Z  撤销     R  重置     ESC  暂停", 15, Muted,
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(50, 30), new Vector2(650, 40));

            toast = Group(hud.transform, "Toast");
            PanelImage(toast.transform, "Toast Plate", new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 148), new Vector2(560, 46), Hex("1C333D", 0.98f));
            toastText = Label(toast.transform, "Toast Text", "", 15, White,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 149), new Vector2(530, 42), TextAnchor.MiddleCenter);
            toast.alpha = 0f;
            toast.gameObject.SetActive(false);
        }

        private void BuildMenu()
        {
            menu = Group(canvasRoot, "Main Menu");
            menu.gameObject.AddComponent<Image>().color = Hex("09141C", 0.965f);

            // 左侧品牌区作为一个整体，窄屏时由 MenuLayoutController 挪到上方。
            identityGroup = RectStretch(menu.transform, "Menu Identity", Vector2.zero, Vector2.zero);
            Transform left = identityGroup;
            LineImage(left, "Title Accent", new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(82, 84), new Vector2(5, 350), Accent);
            Label(left, "Eyebrow", "SPATIAL LOGIC  /  VOL. 01", 15, Accent,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(116, 244), new Vector2(650, 28), style: FontStyle.Bold);
            Label(left, "Title Small", "FIRST PERSON", 52, White,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 176), new Vector2(850, 80), style: FontStyle.Bold);
            Label(left, "Title Large", "SOKOBAN", 92, White,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(104, 86), new Vector2(880, 128), style: FontStyle.Bold);
            Label(left, "Chinese Title", "第一人称 · 三维推箱子", 27, Amber,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(114, 5), new Vector2(700, 48), style: FontStyle.Bold);
            Label(left, "Description", "走进空间，而不只是俯视地图。\n观察、绕行、推动，让每一个箱子准确归位。", 18, Muted,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(114, -82), new Vector2(700, 78));
            FeatureChip(left, "自由探索", new Vector2(114, -190), 132);
            FeatureChip(left, "网格逻辑", new Vector2(258, -190), 132);
            FeatureChip(left, "空间解谜", new Vector2(402, -190), 132);

            // 右侧关卡面板：标题改为面板局部左上锚点，卡片进 ScrollRect，底部按钮独立固定。
            levelPanel = PanelImage(menu.transform, "Level Select", new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-64, 18), new Vector2(480, 670), Panel).rectTransform;
            Label(levelPanel, "Select Eyebrow", "CHAPTER SELECT", 13, Accent,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -26), new Vector2(424, 22), style: FontStyle.Bold);
            Label(levelPanel, "Select Title", "选择关卡", 32, White,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -54), new Vector2(424, 54), style: FontStyle.Bold);
            LineImage(levelPanel, "Select Divider", new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(28, -108), new Vector2(424, 2), Line);

            RectTransform viewport = RectStretch(levelPanel, "Level Viewport",
                new Vector2(24, 92), new Vector2(-24, -120));
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var contentObject = new GameObject("Level Cards", typeof(RectTransform));
            levelContent = (RectTransform)contentObject.transform;
            levelContent.SetParent(viewport, false);
            levelContent.anchorMin = new Vector2(0f, 1f);
            levelContent.anchorMax = new Vector2(1f, 1f);
            levelContent.pivot = new Vector2(0.5f, 1f);
            levelContent.anchoredPosition = Vector2.zero;
            levelContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup list = contentObject.AddComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset(0, 0, 4, 4);
            list.spacing = 12f;
            list.childAlignment = TextAnchor.UpperCenter;
            list.childControlWidth = true;
            list.childControlHeight = false;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = levelPanel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = levelContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            var levels = LevelCatalog.Entries;
            for (int i = 0; i < levels.Count; i++)
            {
                int index = i;
                Button card = Button(levelContent, $"Level {i + 1}", "",
                    new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                    new Vector2(424, 104), () => game.StartLevel(index), false);
                LayoutElement element = card.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 104f;
                element.preferredHeight = 104f;
                levelButtons[i] = card;
                Label(card.transform, "Number", $"{i + 1:00}", 32, Accent,
                    new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(62, 58), style: FontStyle.Bold);
                Label(card.transform, "Title", levels[i].Title, 21, White,
                    new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(94, 16), new Vector2(240, 36), style: FontStyle.Bold);
                Label(card.transform, "Subtitle", levels[i].Subtitle, 13, Muted,
                    new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(94, -19), new Vector2(280, 30));
                levelNotes[i] = Label(card.transform, "Note", "可开始", 12, Accent,
                    new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(92, 58), TextAnchor.MiddleRight);
            }

            Button continueButton = Button(levelPanel, "Continue", "进入最新关卡",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(424, 58),
                () => game.StartLevel(SaveManager.UnlockedCount(PuzzleGameManager.LevelCount) - 1));
            continueButtonRect = continueButton.GetComponent<RectTransform>();
            Button(levelPanel, "Settings", "设置", new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-28, -24), new Vector2(96, 34), ShowSettings, false)
                .GetComponentInChildren<Text>().fontSize = 14;

            menuLayout = menu.gameObject.AddComponent<MenuLayoutController>();
            menuLayout.identityGroup = identityGroup;
            menuLayout.levelPanel = levelPanel;
            menuLayout.Apply(Screen.width, Screen.height, true);
        }

        private void BuildPause()
        {
            pause = Group(canvasRoot, "Pause");
            pause.gameObject.AddComponent<Image>().color = Hex("071118", 0.82f);
            PanelImage(pause.transform, "Pause Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560, 680), Panel);
            Label(pause.transform, "Pause Eyebrow", "GAME PAUSED", 14, Accent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(440, 28), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(pause.transform, "Pause Title", "暂停探索", 38, White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(440, 64), TextAnchor.MiddleCenter, FontStyle.Bold);
            Button(pause.transform, "Resume", "继续游戏", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 84), new Vector2(360, 58), () => game.Resume());
            Button(pause.transform, "Restart", "重新开始", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 12), new Vector2(360, 54), () => game.RestartLevel(), false);
            Button(pause.transform, "Settings", "设置与舒适度", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -60), new Vector2(360, 54), ShowSettings, false);
            Button(pause.transform, "Menu", "返回主菜单", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -132), new Vector2(360, 54), () => game.ShowMenu(), false);
            Button(pause.transform, "Exit", "退出游戏", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -204), new Vector2(360, 54), ExitGame, false);
            Label(pause.transform, "Pause Hint", "ESC 继续  ·  失焦会自动暂停", 13, Muted,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -280), new Vector2(460, 26), TextAnchor.MiddleCenter);
        }

        private void BuildSettings()
        {
            settings = Group(canvasRoot, "Settings");
            settings.gameObject.AddComponent<Image>().color = Hex("071118", 0.88f);
            PanelImage(settings.transform, "Settings Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(660, 700), Panel);
            Label(settings.transform, "Settings Title", "设置与舒适度", 36, White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 272), new Vector2(560, 62), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(settings.transform, "Settings Note", "点击右侧数值循环切换；改动即时生效并保存", 14, Muted,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 222), new Vector2(560, 30), TextAnchor.MiddleCenter);

            settingButtons = new Button[5];
            settingButtons[0] = CycleRow("鼠标灵敏度", 150f, CycleSensitivity);
            settingButtons[1] = CycleRow("视野 FOV", 82f, CycleFov);
            settingButtons[2] = CycleRow("反转 Y 视角", 14f, () => GameSettings.InvertY = !GameSettings.InvertY);
            settingButtons[3] = CycleRow("舒适模式 · 慢速推动", -54f, () => GameSettings.ComfortMode = !GameSettings.ComfortMode);
            settingButtons[4] = CycleRow("全屏显示", -122f, () => GameSettings.FullScreen = !GameSettings.FullScreen);

            Button(settings.transform, "Restore Defaults", "恢复默认", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-110, -216), new Vector2(260, 56), () => { GameSettings.ResetToDefaults(); RefreshSettings(); }, false);
            Button(settings.transform, "Settings Back", "返回", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(160, -216), new Vector2(260, 56), CloseSettings);
            RefreshSettings();
            settings.gameObject.SetActive(false);
        }

        private Button CycleRow(string caption, float y, Action cycle)
        {
            Label(settings.transform, caption + " Label", caption, 19, White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-160, y), new Vector2(300, 44));
            return Button(settings.transform, caption, "", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(170, y), new Vector2(220, 48), () => { cycle(); RefreshSettings(); }, false);
        }

        private static void CycleSensitivity()
        {
            float[] steps = { 0.8f, 1.2f, 1.6f, 2.2f, 3.0f, 4.0f, 5.0f };
            float current = GameSettings.MouseSensitivity;
            float next = steps[0];
            for (int i = 0; i < steps.Length; i++)
                if (steps[i] > current + 0.01f) { next = steps[i]; break; }
            GameSettings.MouseSensitivity = next;
        }

        private static void CycleFov()
        {
            float[] steps = { 60f, 70f, 75f, 85f, 95f };
            float current = GameSettings.FieldOfView;
            float next = steps[0];
            for (int i = 0; i < steps.Length; i++)
                if (steps[i] > current + 0.5f) { next = steps[i]; break; }
            GameSettings.FieldOfView = next;
        }

        private void RefreshSettings()
        {
            if (settingButtons == null) return;
            SetButtonText(0, GameSettings.MouseSensitivity.ToString("0.0"));
            SetButtonText(1, GameSettings.FieldOfView.ToString("0"));
            SetButtonText(2, GameSettings.InvertY ? "开" : "关");
            SetButtonText(3, GameSettings.ComfortMode ? "开（0.55 秒推动）" : "关（0.38 秒推动）");
            SetButtonText(4, GameSettings.FullScreen ? "开" : "关");
        }

        private void SetButtonText(int index, string value)
        {
            Text label = settingButtons[index].GetComponentInChildren<Text>();
            if (label != null) label.text = value;
        }

        private void BuildVictory()
        {
            victory = Group(canvasRoot, "Victory");
            victory.gameObject.AddComponent<Image>().color = Hex("071118", 0.84f);
            PanelImage(victory.transform, "Victory Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(690, 650), Panel);
            LineImage(victory.transform, "Victory Line", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, 279), new Vector2(110, 5), Accent);
            Label(victory.transform, "Victory Eyebrow", "ALL BOXES IN PLACE", 14, Accent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(570, 28), TextAnchor.MiddleCenter, FontStyle.Bold);
            Label(victory.transform, "Victory Title", "关卡完成", 46, White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 166), new Vector2(570, 76), TextAnchor.MiddleCenter, FontStyle.Bold);
            victoryStats = Label(victory.transform, "Stats", "", 19, White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 56), new Vector2(570, 100), TextAnchor.MiddleCenter);
            victoryBest = Label(victory.transform, "Best", "", 14, Muted,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -24), new Vector2(570, 48), TextAnchor.MiddleCenter);
            nextButton = Button(victory.transform, "Next", "下一关", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -92), new Vector2(440, 60), NextLevel);
            Button(victory.transform, "Replay", "再玩一次", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -172), new Vector2(440, 54), () => game.RestartLevel(), false);
            Button(victory.transform, "Menu", "返回主菜单", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -246), new Vector2(440, 54), () => game.ShowMenu(), false);
        }

        public void ShowMenu()
        {
            RefreshLevelCards();
            SetOnly(menu);
            hud.gameObject.SetActive(false);
        }

        public void ShowHUD()
        {
            hud.gameObject.SetActive(true);
            SetOnly(null);
            guidance.gameObject.SetActive(!string.IsNullOrEmpty(CurrentGuidance));
            deadlock.gameObject.SetActive(!string.IsNullOrEmpty(CurrentDeadlockHint));
        }

        public void ShowPause() => SetOnly(pause);

        public void ShowSettings()
        {
            settingsOrigin = ActiveOverlay();
            RefreshSettings();
            SetOnly(settings);
        }

        public void CloseSettings()
        {
            if (settingsOrigin == null)
            {
                hud.gameObject.SetActive(true);
                SetOnly(null);
            }
            else SetOnly(settingsOrigin);
        }

        public void ExitGame() => Application.Quit();

        public void ShowVictory(int levelIndex, int levelCount, float seconds, int totalPushes,
            int effectivePushes, int undos, float distance, int goalsCompleted, int goalsTotal)
        {
            int minutes = Mathf.FloorToInt(seconds / 60f);
            int secs = Mathf.FloorToInt(seconds % 60f);
            bool hasNext = levelIndex + 1 < levelCount;
            victoryStats.text = $"目标归位  {goalsCompleted} / {goalsTotal}     {minutes:00}:{secs:00}  完成时间\n" +
                                $"{effectivePushes:00}  有效推动     {totalPushes:00}  累计推动     {undos:00}  撤销\n" +
                                $"{distance:0.0} m  行走";
            int best = SaveManager.GetBestPushes(levelIndex);
            victoryBest.text = (best >= 0 ? $"最佳有效解法  ·  {best} 次推动" : "首次完成") +
                               (hasNext ? $"  ·  下一关 {levelIndex + 2:00} 已解锁" : "  ·  全部关卡已完成");
            nextButton.GetComponentInChildren<Text>().text = hasNext ? "进入下一关" : "返回主菜单";
            SetOnly(victory);
        }

        public void SetLevel(int number, string title)
        {
            levelNumber.text = $"LEVEL {number:00}";
            levelTitle.text = title;
        }

        public void SetStats(int completed, int total, int pushes)
        {
            goalCounter.text = $"{completed} / {total}";
            pushCounter.text = pushes.ToString("00");
        }

        public void SetInteractionPrompt(string content, bool visible, bool valid = true)
        {
            interaction.gameObject.SetActive(visible);
            interaction.alpha = visible ? 1f : 0f;
            interactionText.text = content;
            interactionText.color = valid ? Accent : Amber;
        }

        public void SetGuidance(string content)
        {
            CurrentGuidance = content ?? string.Empty;
            guidanceText.text = CurrentGuidance;
            guidance.gameObject.SetActive(!string.IsNullOrEmpty(CurrentGuidance) && hud.gameObject.activeSelf);
        }

        public void SetDeadlock(bool active, string content)
        {
            CurrentDeadlockHint = active ? content : string.Empty;
            deadlockText.text = CurrentDeadlockHint;
            deadlock.gameObject.SetActive(active && hud.gameObject.activeSelf);
        }

        public void FlashMessage(string message)
        {
            if (toastRoutine != null) StopCoroutine(toastRoutine);
            toastRoutine = StartCoroutine(ShowToast(message));
        }

        private IEnumerator ShowToast(string message)
        {
            toastText.text = message;
            toast.gameObject.SetActive(true);
            toast.alpha = 1f;
            yield return new WaitForSeconds(1.4f);
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                toast.alpha = 1f - t / 0.25f;
                yield return null;
            }
            toast.alpha = 0f;
            toast.gameObject.SetActive(false);
            toastRoutine = null;
        }

        private void NextLevel()
        {
            if (game.CurrentLevelIndex + 1 < PuzzleGameManager.LevelCount) game.StartLevel(game.CurrentLevelIndex + 1);
            else game.ShowMenu();
        }

        private void RefreshLevelCards()
        {
            int unlocked = SaveManager.UnlockedCount(PuzzleGameManager.LevelCount);
            for (int i = 0; i < levelButtons.Length; i++)
            {
                levelButtons[i].interactable = i < unlocked;
                int best = SaveManager.GetBestPushes(i);
                levelNotes[i].text = best >= 0 ? $"最佳 {best} 推" : i < unlocked ? "可开始" : "未解锁";
            }
        }

        private CanvasGroup ActiveOverlay()
        {
            foreach (CanvasGroup group in new[] { menu, pause, victory })
                if (group != null && group.gameObject.activeSelf) return group;
            return null;
        }

        private void SetOnly(CanvasGroup visible)
        {
            foreach (CanvasGroup group in new[] { menu, pause, victory, settings })
            {
                bool active = group == visible;
                group.gameObject.SetActive(active);
                group.alpha = active ? 1f : 0f;
                group.blocksRaycasts = active;
            }
        }

        private static CanvasGroup Group(Transform parent, string name)
        {
            RectTransform rect = Stretch(parent, name);
            CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            return group;
        }

        private static void FeatureChip(Transform parent, string content, Vector2 position, float width)
        {
            PanelImage(parent, content + " Chip", new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                position, new Vector2(width, 42), PanelLight);
            Label(parent, content, content, 14, White, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                position, new Vector2(width, 40), TextAnchor.MiddleCenter);
        }
    }
}
