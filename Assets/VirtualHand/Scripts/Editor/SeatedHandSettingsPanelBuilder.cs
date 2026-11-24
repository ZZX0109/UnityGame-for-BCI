#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Neuralife.VirtualHand.Editor
{
    /// <summary>Builds the reusable UGUI panel without modifying a host scene or canvas.</summary>
    public static class SeatedHandSettingsPanelBuilder
    {
        public const string MenuPath = "Tools/Neuralife/Virtual Hand/Regenerate Seated Hand Settings Panel";
        public const string PrefabPath = "Assets/VirtualHand/Prefabs/SeatedHandSettingsPanel.prefab";
        private const string FontResourcePath = "Fonts/NotoSansCJK_Subset";

        private static readonly Color PanelColor = new Color(0.075f, 0.105f, 0.145f, 0.98f);
        private static readonly Color ButtonNormal = new Color(0.12f, 0.20f, 0.28f, 1f);
        private static readonly Color ButtonFocus = new Color(0.10f, 0.43f, 0.55f, 1f);
        private static readonly Color ButtonPressed = new Color(0.48f, 0.32f, 0.13f, 1f);
        private static readonly Color ButtonDisabled = new Color(0.22f, 0.25f, 0.28f, 1f);
        private static readonly Color TextColor = new Color(0.94f, 0.96f, 0.98f, 1f);
        private static readonly Color HeadingColor = new Color(0.63f, 0.84f, 0.91f, 1f);

        [MenuItem(MenuPath)]
        public static void BuildPrefab()
        {
            Font font = Resources.Load<Font>(FontResourcePath);
            if (font == null)
                throw new InvalidOperationException("Missing font Resources/Fonts/NotoSansCJK_Subset.ttf. Import it under a Resources/Fonts folder, then regenerate the prefab.");

            EnsureFolder("Assets/VirtualHand");
            EnsureFolder("Assets/VirtualHand/Prefabs");

            GameObject root = new GameObject("SeatedHandSettingsPanel", typeof(RectTransform));
            try
            {
                BuildHierarchy(root, font);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (savedPrefab == null)
                    throw new InvalidOperationException("Unity could not save the prefab at " + PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Generated reusable prefab: " + PrefabPath);
        }

        private static void BuildHierarchy(GameObject root, Font font)
        {
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(1040f, 940f);
            rootRect.anchoredPosition = Vector2.zero;

            SeatedHandSettingsPresenter presenter = root.AddComponent<SeatedHandSettingsPresenter>();
            SeatedWristNeutralCapture neutralCapture = root.AddComponent<SeatedWristNeutralCapture>();
            RectTransform panel = CreateFixedRect("SettingsPanel", root.transform, Vector2.zero + new Vector2(0f, 45f), new Vector2(980f, 820f));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;
            CanvasGroup panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = 0f;
            panelGroup.interactable = false;
            panelGroup.blocksRaycasts = false;

            Text titleText = CreateTopText("TitleText", panel, font, 20f, 50f, 24f, 24f,
                "坐位手部设置", 34, FontStyle.Bold, TextAnchor.MiddleLeft, HeadingColor);

            RectTransform scrollView = CreateStretchRect("SettingsScrollView", panel, 24f, 28f, 84f, 112f);
            ScrollRect scrollRect = scrollView.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = 34f;

            RectTransform viewport = CreateStretchRect("Viewport", scrollView, 0f, 0f, 0f, 0f);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = CreateStretchRect("Content", viewport, 0f, 0f, 0f, 0f);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 716f);
            scrollRect.viewport = viewport;
            scrollRect.content = content;

            RectTransform scrollbarRect = CreateStretchRect("VerticalScrollbar", scrollView, 3f, 3f, 3f, 3f);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(14f, 0f);
            scrollbarRect.offsetMin = new Vector2(0f, 3f);
            scrollbarRect.offsetMax = new Vector2(-3f, -3f);
            Image trackImage = scrollbarRect.gameObject.AddComponent<Image>();
            trackImage.color = new Color(0.10f, 0.14f, 0.18f, 1f);
            trackImage.raycastTarget = true;
            Scrollbar scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.transition = Selectable.Transition.ColorTint;
            scrollbar.colors = CreateButtonColors();
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            RectTransform handleRect = CreateStretchRect("Handle", scrollbarRect, 2f, 2f, 2f, 2f);
            Image handleImage = handleRect.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.48f, 0.68f, 0.74f, 1f);
            handleImage.raycastTarget = true;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.size = 0.7f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            Button captureLeftWristButton = CreateChoiceButton("CaptureLeftButton", "Label", content, font, 638f, 56f, 0, 2, string.Empty, 20, out Text captureLeftWristLabel);
            Button captureRightWristButton = CreateChoiceButton("CaptureRightButton", "Label", content, font, 638f, 56f, 1, 2, string.Empty, 20, out Text captureRightWristLabel);

            Text affectedHeading = CreateTopText("AffectedHeadingText", content, font, 8f, 36f, 18f, 18f,
                "患侧", 25, FontStyle.Bold, TextAnchor.MiddleLeft, HeadingColor);
            Text operatingHeading = CreateTopText("OperatingHeadingText", content, font, 142f, 36f, 18f, 18f,
                "操作侧（独立选择）", 25, FontStyle.Bold, TextAnchor.MiddleLeft, HeadingColor);
            Text displayHeading = CreateTopText("DisplayHeadingText", content, font, 276f, 36f, 18f, 18f,
                "显示方式", 25, FontStyle.Bold, TextAnchor.MiddleLeft, HeadingColor);

            Button affectedUnspecifiedButton = CreateChoiceButton("AffectedUnspecifiedButton", "Label", content, font, 50f, 72f, 0, 3, "未指定", 24, out Text affectedUnspecifiedLabel);
            Button affectedLeftButton = CreateChoiceButton("AffectedLeftButton", "Label", content, font, 50f, 72f, 1, 3, "左手", 24, out Text affectedLeftLabel);
            Button affectedRightButton = CreateChoiceButton("AffectedRightButton", "Label", content, font, 50f, 72f, 2, 3, "右手", 24, out Text affectedRightLabel);

            Button operatingUnspecifiedButton = CreateChoiceButton("OperatingUnspecifiedButton", "Label", content, font, 184f, 72f, 0, 3, "未指定", 24, out Text operatingUnspecifiedLabel);
            Button operatingLeftButton = CreateChoiceButton("OperatingLeftButton", "Label", content, font, 184f, 72f, 1, 3, "左手", 24, out Text operatingLeftLabel);
            Button operatingRightButton = CreateChoiceButton("OperatingRightButton", "Label", content, font, 184f, 72f, 2, 3, "右手", 24, out Text operatingRightLabel);

            Button measuredHandsButton = CreateChoiceButton("MeasuredHandsButton", "Label", content, font, 318f, 100f, 0, 3, "采集到的双手", 23, out Text measuredHandsLabel);
            Button singleOperatingHandButton = CreateChoiceButton("SingleOperatingHandButton", "Label", content, font, 318f, 100f, 1, 3, "仅操作手", 23, out Text singleOperatingHandLabel);
            Button mirrorButton = CreateChoiceButton("MirrorButton", "Label", content, font, 318f, 100f, 2, 3, "对侧手镜像", 23, out Text mirrorLabel);

            Text displayModeHintText = CreateTopText("DisplayModeHintText", content, font, 428f, 60f, 18f, 18f,
                "把操作手放在舒适的位置，采集该侧腕位后保存。", 21, FontStyle.Normal, TextAnchor.MiddleLeft, TextColor);
            Text resetHintText = CreateTopText("ResetHintText", content, font, 496f, 66f, 18f, 18f,
                "仅恢复本页选项；点击保存后生效。腕部偏移会保留。", 21, FontStyle.Normal, TextAnchor.MiddleLeft, TextColor);
            Text statusText = CreateTopText("StatusText", content, font, 570f, 56f, 18f, 18f,
                string.Empty, 21, FontStyle.Bold, TextAnchor.MiddleLeft, HeadingColor);

            Button saveButton = CreateBottomButton("SaveButton", panel, font, 0, 3, 20f, 62f, "保存", 24, out Text saveButtonLabel);
            Button cancelButton = CreateBottomButton("CancelButton", panel, font, 1, 3, 20f, 62f, "取消", 24, out Text cancelButtonLabel);
            Button resetButton = CreateBottomButton("ResetButton", panel, font, 2, 3, 20f, 62f, "恢复本页默认", 22, out Text resetButtonLabel);
            Button openButton = CreateFixedButton("OpenButton", "Label", root.transform, font, new Vector2(0f, -414f), new Vector2(220f, 62f), "设置", 26, out Text openButtonLabel);

            BindPresenter(presenter, panelGroup,
                openButton, saveButton, cancelButton, resetButton,
                affectedUnspecifiedButton, affectedLeftButton, affectedRightButton,
                operatingUnspecifiedButton, operatingLeftButton, operatingRightButton,
                measuredHandsButton, singleOperatingHandButton, mirrorButton,
                captureLeftWristButton, captureRightWristButton,
                openButtonLabel, saveButtonLabel, cancelButtonLabel, resetButtonLabel,
                affectedUnspecifiedLabel, affectedLeftLabel, affectedRightLabel,
                operatingUnspecifiedLabel, operatingLeftLabel, operatingRightLabel,
                measuredHandsLabel, singleOperatingHandLabel, mirrorLabel,
                captureLeftWristLabel, captureRightWristLabel,
                titleText, affectedHeading, operatingHeading, displayHeading,
                resetHintText, displayModeHintText, statusText);
            BindNeutralCapture(neutralCapture, presenter);

            AddAction(openButton, presenter.Open);
            AddAction(saveButton, presenter.Save);
            AddAction(cancelButton, presenter.Cancel);
            AddAction(resetButton, presenter.ResetDraftToDefaults);
            AddAction(affectedUnspecifiedButton, presenter.ChooseAffectedUnspecified);
            AddAction(affectedLeftButton, presenter.ChooseAffectedLeft);
            AddAction(affectedRightButton, presenter.ChooseAffectedRight);
            AddAction(operatingUnspecifiedButton, presenter.ChooseOperatingUnspecified);
            AddAction(operatingLeftButton, presenter.ChooseOperatingLeft);
            AddAction(operatingRightButton, presenter.ChooseOperatingRight);
            AddAction(measuredHandsButton, presenter.ChooseMeasuredHands);
            AddAction(singleOperatingHandButton, presenter.ChooseSingleOperatingHand);
            AddAction(mirrorButton, presenter.ChooseMirror);
            AddAction(captureLeftWristButton, neutralCapture.CaptureLeftButton);
            AddAction(captureRightWristButton, neutralCapture.CaptureRightButton);
        }

        private static void BindPresenter(
            SeatedHandSettingsPresenter presenter,
            CanvasGroup panelGroup,
            Button openButton, Button saveButton, Button cancelButton, Button resetButton,
            Button affectedUnspecifiedButton, Button affectedLeftButton, Button affectedRightButton,
            Button operatingUnspecifiedButton, Button operatingLeftButton, Button operatingRightButton,
            Button measuredHandsButton, Button singleOperatingHandButton, Button mirrorButton,
            Button captureLeftWristButton, Button captureRightWristButton,
            Text openButtonLabel, Text saveButtonLabel, Text cancelButtonLabel, Text resetButtonLabel,
            Text affectedUnspecifiedLabel, Text affectedLeftLabel, Text affectedRightLabel,
            Text operatingUnspecifiedLabel, Text operatingLeftLabel, Text operatingRightLabel,
            Text measuredHandsLabel, Text singleOperatingHandLabel, Text mirrorLabel,
            Text captureLeftWristLabel, Text captureRightWristLabel,
            Text titleText, Text affectedHeadingText, Text operatingHeadingText, Text displayHeadingText,
            Text resetHintText, Text displayModeHintText, Text statusText)
        {
            SerializedObject serializedPresenter = new SerializedObject(presenter);
            serializedPresenter.Update();
            SetReference(serializedPresenter, "panelGroup", panelGroup);
            SetReference(serializedPresenter, "openButton", openButton);
            SetReference(serializedPresenter, "saveButton", saveButton);
            SetReference(serializedPresenter, "cancelButton", cancelButton);
            SetReference(serializedPresenter, "resetButton", resetButton);
            SetReference(serializedPresenter, "affectedUnspecifiedButton", affectedUnspecifiedButton);
            SetReference(serializedPresenter, "affectedLeftButton", affectedLeftButton);
            SetReference(serializedPresenter, "affectedRightButton", affectedRightButton);
            SetReference(serializedPresenter, "operatingUnspecifiedButton", operatingUnspecifiedButton);
            SetReference(serializedPresenter, "operatingLeftButton", operatingLeftButton);
            SetReference(serializedPresenter, "operatingRightButton", operatingRightButton);
            SetReference(serializedPresenter, "measuredHandsButton", measuredHandsButton);
            SetReference(serializedPresenter, "singleOperatingHandButton", singleOperatingHandButton);
            SetReference(serializedPresenter, "mirrorButton", mirrorButton);
            SetReference(serializedPresenter, "captureLeftWristButton", captureLeftWristButton);
            SetReference(serializedPresenter, "captureRightWristButton", captureRightWristButton);

            SetReference(serializedPresenter, "openButtonLabel", openButtonLabel);
            SetReference(serializedPresenter, "saveButtonLabel", saveButtonLabel);
            SetReference(serializedPresenter, "cancelButtonLabel", cancelButtonLabel);
            SetReference(serializedPresenter, "resetButtonLabel", resetButtonLabel);
            SetReference(serializedPresenter, "affectedUnspecifiedLabel", affectedUnspecifiedLabel);
            SetReference(serializedPresenter, "affectedLeftLabel", affectedLeftLabel);
            SetReference(serializedPresenter, "affectedRightLabel", affectedRightLabel);
            SetReference(serializedPresenter, "operatingUnspecifiedLabel", operatingUnspecifiedLabel);
            SetReference(serializedPresenter, "operatingLeftLabel", operatingLeftLabel);
            SetReference(serializedPresenter, "operatingRightLabel", operatingRightLabel);
            SetReference(serializedPresenter, "measuredHandsLabel", measuredHandsLabel);
            SetReference(serializedPresenter, "singleOperatingHandLabel", singleOperatingHandLabel);
            SetReference(serializedPresenter, "mirrorLabel", mirrorLabel);
            SetReference(serializedPresenter, "captureLeftWristLabel", captureLeftWristLabel);
            SetReference(serializedPresenter, "captureRightWristLabel", captureRightWristLabel);

            SetReference(serializedPresenter, "titleText", titleText);
            SetReference(serializedPresenter, "affectedHeadingText", affectedHeadingText);
            SetReference(serializedPresenter, "operatingHeadingText", operatingHeadingText);
            SetReference(serializedPresenter, "displayHeadingText", displayHeadingText);
            SetReference(serializedPresenter, "resetHintText", resetHintText);
            SetReference(serializedPresenter, "displayModeHintText", displayModeHintText);
            SetReference(serializedPresenter, "statusText", statusText);
            SerializedProperty keyProperty = serializedPresenter.FindProperty("playerPrefsKey");
            if (keyProperty == null)
                throw new InvalidOperationException("SeatedHandSettingsPresenter.playerPrefsKey was not found.");
            keyProperty.stringValue = SeatedHandSettingsPresenter.DefaultPlayerPrefsKey;
            serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindNeutralCapture(SeatedWristNeutralCapture capture, SeatedHandSettingsPresenter presenter)
        {
            SerializedObject serializedCapture = new SerializedObject(capture);
            serializedCapture.Update();
            SetReference(serializedCapture, "presenter", presenter);
            serializedCapture.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReference(SerializedObject target, string fieldName, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(fieldName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("Expected serialized object reference field '" + fieldName + "' on SeatedHandSettingsPresenter.");
            property.objectReferenceValue = value;
        }

        private static void AddAction(Button button, UnityAction action)
        {
            // Each Button is newly created on every build, so it receives exactly one persistent action.
            UnityEventTools.AddPersistentListener(button.onClick, action);
        }

        private static Button CreateChoiceButton(string name, string labelName, Transform parent, Font font,
            float top, float height, int column, int columns, string initialLabel, int fontSize, out Text label)
        {
            RectTransform rect = CreateTopColumnRect(name, parent, top, height, column, columns, 18f, 7f);
            return ConfigureButton(rect.gameObject, labelName, font, initialLabel, fontSize, out label);
        }

        private static Button CreateBottomButton(string name, Transform parent, Font font,
            int column, int columns, float bottom, float height, string initialLabel, int fontSize, out Text label)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(column / (float)columns, 0f);
            rect.anchorMax = new Vector2((column + 1f) / columns, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            float inset = column == 0 || column == columns - 1 ? 24f : 7f;
            rect.offsetMin = new Vector2(inset, bottom);
            rect.offsetMax = new Vector2(-inset, bottom + height);
            return ConfigureButton(buttonObject, "Label", font, initialLabel, fontSize, out label);
        }

        private static Button CreateFixedButton(string name, string labelName, Transform parent, Font font,
            Vector2 position, Vector2 size, string initialLabel, int fontSize, out Text label)
        {
            RectTransform rect = CreateFixedRect(name, parent, position, size);
            return ConfigureButton(rect.gameObject, labelName, font, initialLabel, fontSize, out label);
        }

        private static Button ConfigureButton(GameObject buttonObject, string labelName, Font font,
            string initialLabel, int fontSize, out Text label)
        {
            Image image = buttonObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = CreateButtonColors();
            Navigation navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;

            GameObject labelObject = new GameObject(labelName, typeof(RectTransform));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 5f);
            labelRect.offsetMax = new Vector2(-10f, -5f);
            label = AddText(labelObject, font, initialLabel, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor);
            label.raycastTarget = false;
            return button;
        }

        private static ColorBlock CreateButtonColors()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = ButtonNormal;
            colors.highlightedColor = ButtonFocus;
            colors.selectedColor = ButtonFocus;
            colors.pressedColor = ButtonPressed;
            colors.disabledColor = ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        private static Text CreateTopText(string name, Transform parent, Font font,
            float top, float height, float left, float right, string value, int fontSize,
            FontStyle style, TextAnchor alignment, Color color)
        {
            RectTransform rect = CreateTopRect(name, parent, top, height, left, right);
            Text text = AddText(rect.gameObject, font, value, fontSize, style, alignment, color);
            text.raycastTarget = false;
            return text;
        }

        private static Text AddText(GameObject gameObject, Font font, string value, int fontSize,
            FontStyle style, TextAnchor alignment, Color color)
        {
            Text text = gameObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.color = color;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 18;
            text.resizeTextMaxSize = fontSize;
            text.supportRichText = false;
            text.lineSpacing = 1f;
            return text;
        }

        private static RectTransform CreateTopColumnRect(string name, Transform parent,
            float top, float height, int column, int columns, float outerMargin, float innerGap)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = new Vector2(column / (float)columns, 1f);
            rect.anchorMax = new Vector2((column + 1f) / columns, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            float leftInset = column == 0 ? outerMargin : innerGap;
            float rightInset = column == columns - 1 ? outerMargin : innerGap;
            rect.offsetMin = new Vector2(leftInset, -top - height);
            rect.offsetMax = new Vector2(-rightInset, -top);
            return rect;
        }

        private static RectTransform CreateTopRect(string name, Transform parent,
            float top, float height, float left, float right)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static RectTransform CreateFixedRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform CreateStretchRect(string name, Transform parent,
            float left, float right, float top, float bottom)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;
            int separator = folderPath.LastIndexOf('/');
            if (separator <= 0)
                throw new InvalidOperationException("Invalid asset folder path: " + folderPath);
            string parent = folderPath.Substring(0, separator);
            string name = folderPath.Substring(separator + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
