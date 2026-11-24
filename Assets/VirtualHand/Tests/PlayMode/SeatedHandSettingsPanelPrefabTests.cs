#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Neuralife.VirtualHand.Tests
{
    public sealed class SeatedHandSettingsPanelPrefabTests
    {
        private const string PrefabPath = "Assets/VirtualHand/Prefabs/SeatedHandSettingsPanel.prefab";

        private GameObject hostCanvasObject;
        private GameObject prefabInstance;
        private string playerPrefsKey;

        [Test]
        public void SavedPrefab_BindsButtons_AndSupportsOpenCancelReopenAndSave()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "The builder must create the reusable prefab.");

            hostCanvasObject = new GameObject("Test Host World Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas hostCanvas = hostCanvasObject.GetComponent<Canvas>();
            hostCanvas.renderMode = RenderMode.WorldSpace;
            RectTransform hostRect = hostCanvasObject.GetComponent<RectTransform>();
            hostRect.sizeDelta = new Vector2(1200f, 1000f);
            hostRect.localScale = Vector3.one * 0.001f;

            prefabInstance = UnityEngine.Object.Instantiate(prefab, hostCanvasObject.transform);
            Assert.IsNotNull(prefabInstance, "The prefab must instantiate under a host canvas.");
            Assert.AreSame(hostCanvas, prefabInstance.GetComponentInParent<Canvas>());

            MonoBehaviour presenter = FindPresenter(prefabInstance);
            Assert.IsNotNull(presenter, "The prefab must contain SeatedHandSettingsPresenter.");

            playerPrefsKey = "Neuralife.VirtualHand.SeatedHandSettings.Tests." + Guid.NewGuid().ToString("N");
            SerializedObject presenterObject = new SerializedObject(presenter);
            SerializedProperty keyProperty = presenterObject.FindProperty("playerPrefsKey");
            Assert.IsNotNull(keyProperty, "The real serialized field playerPrefsKey must be present.");
            keyProperty.stringValue = playerPrefsKey;
            presenterObject.ApplyModifiedPropertiesWithoutUndo();

            CanvasGroup panelGroup = presenterObject.FindProperty("panelGroup").objectReferenceValue as CanvasGroup;
            Assert.IsNotNull(panelGroup, "The real serialized field panelGroup must reference the panel.");

            string[] buttonNames =
            {
                "OpenButton", "SaveButton", "CancelButton", "ResetButton",
                "AffectedUnspecifiedButton", "AffectedLeftButton", "AffectedRightButton",
                "OperatingUnspecifiedButton", "OperatingLeftButton", "OperatingRightButton",
                "MeasuredHandsButton", "SingleOperatingHandButton", "MirrorButton"
            };
            string[] methodNames =
            {
                "Open", "Save", "Cancel", "ResetDraftToDefaults",
                "ChooseAffectedUnspecified", "ChooseAffectedLeft", "ChooseAffectedRight",
                "ChooseOperatingUnspecified", "ChooseOperatingLeft", "ChooseOperatingRight",
                "ChooseMeasuredHands", "ChooseSingleOperatingHand", "ChooseMirror"
            };

            for (int i = 0; i < buttonNames.Length; i++)
            {
                Button button = FindButton(prefabInstance, buttonNames[i]);
                Assert.IsNotNull(button, "Missing button " + buttonNames[i]);
                Assert.AreEqual(1, button.onClick.GetPersistentEventCount(), buttonNames[i] + " must have exactly one persistent action after regeneration.");
                Assert.AreSame(presenter, button.onClick.GetPersistentTarget(0), buttonNames[i] + " must call this prefab's presenter.");
                Assert.AreEqual(methodNames[i], button.onClick.GetPersistentMethodName(0), buttonNames[i] + " has the wrong presenter method.");
                Assert.AreEqual(Selectable.Transition.ColorTint, button.transition, buttonNames[i] + " must show focus with a color change.");
                Assert.AreEqual(Navigation.Mode.Automatic, button.navigation.mode, buttonNames[i] + " must participate in normal UI navigation.");
                Assert.AreNotEqual(button.colors.normalColor, button.colors.highlightedColor, buttonNames[i] + " needs a visible focus color.");
                Assert.AreNotEqual(button.colors.normalColor, button.colors.selectedColor, buttonNames[i] + " needs a visible selected color.");
            }

            Font expectedFont = Resources.Load<Font>("Fonts/NotoSansCJK_Subset");
            Assert.IsNotNull(expectedFont);
            Text[] allText = prefabInstance.GetComponentsInChildren<Text>(true);
            Assert.Greater(allText.Length, 0);
            foreach (Text text in allText)
            {
                Assert.AreSame(expectedFont, text.font, text.gameObject.name + " must use the supplied font.");
                Assert.IsTrue(text.resizeTextForBestFit, text.gameObject.name + " must shrink to fit when needed.");
                Assert.AreEqual(HorizontalWrapMode.Wrap, text.horizontalOverflow, text.gameObject.name + " must wrap long labels.");
            }

            var initial = new ProfileProbe
            {
                schemaVersion = 1,
                affectedHand = 1,
                operatingHand = 2,
                displayMode = 1,
                leftWristOffset = default(WristOffsetProbe),
                rightWristOffset = default(WristOffsetProbe)
            };
            string initialJson = JsonUtility.ToJson(initial);
            PlayerPrefs.SetString(playerPrefsKey, initialJson);
            PlayerPrefs.Save();

            FindButton(prefabInstance, "OpenButton").onClick.Invoke();
            Assert.AreEqual(1f, panelGroup.alpha);
            Assert.IsTrue(panelGroup.interactable);
            Assert.AreEqual("左手（已选）", GetButtonLabel(prefabInstance, "AffectedLeftButton").text);
            Assert.AreEqual("右手（已选）", GetButtonLabel(prefabInstance, "OperatingRightButton").text);

            FindButton(prefabInstance, "AffectedRightButton").onClick.Invoke();
            FindButton(prefabInstance, "OperatingLeftButton").onClick.Invoke();
            FindButton(prefabInstance, "MirrorButton").onClick.Invoke();
            Assert.IsTrue(FindButton(prefabInstance, "SaveButton").interactable, "Changing choices must enable Save.");
            Assert.AreEqual("右手（已选）", GetButtonLabel(prefabInstance, "AffectedRightButton").text);

            FindButton(prefabInstance, "CancelButton").onClick.Invoke();
            Assert.AreEqual(0f, panelGroup.alpha);
            Assert.IsFalse(panelGroup.interactable);
            Assert.AreEqual(initialJson, PlayerPrefs.GetString(playerPrefsKey), "Cancel must discard staged choices without writing preferences.");

            FindButton(prefabInstance, "OpenButton").onClick.Invoke();
            Assert.AreEqual(1f, panelGroup.alpha);
            Assert.AreEqual("左手（已选）", GetButtonLabel(prefabInstance, "AffectedLeftButton").text, "Reopen must reload the last saved choice.");
            Assert.AreEqual("右手", GetButtonLabel(prefabInstance, "AffectedRightButton").text);
            Assert.IsFalse(FindButton(prefabInstance, "SaveButton").interactable, "Reopening without edits must not mark the draft changed.");

            FindButton(prefabInstance, "AffectedRightButton").onClick.Invoke();
            FindButton(prefabInstance, "OperatingLeftButton").onClick.Invoke();
            FindButton(prefabInstance, "MirrorButton").onClick.Invoke();
            FindButton(prefabInstance, "SaveButton").onClick.Invoke();

            ProfileProbe saved = JsonUtility.FromJson<ProfileProbe>(PlayerPrefs.GetString(playerPrefsKey));
            Assert.AreEqual(2, saved.affectedHand);
            Assert.AreEqual(1, saved.operatingHand);
            Assert.AreEqual(2, saved.displayMode);
            Assert.IsTrue(panelGroup.interactable, "Saving keeps the panel open for further edits or cancel.");
        }

        [TearDown]
        public void TearDown()
        {
            if (prefabInstance != null)
                UnityEngine.Object.DestroyImmediate(prefabInstance);
            if (hostCanvasObject != null)
                UnityEngine.Object.DestroyImmediate(hostCanvasObject);
            if (!string.IsNullOrEmpty(playerPrefsKey))
            {
                PlayerPrefs.DeleteKey(playerPrefsKey);
                PlayerPrefs.Save();
            }
        }

        private static MonoBehaviour FindPresenter(GameObject root)
        {
            MonoBehaviour[] components = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour component in components)
            {
                if (component != null && component.GetType().Name == "SeatedHandSettingsPresenter")
                    return component;
            }
            return null;
        }

        private static Button FindButton(GameObject root, string name)
        {
            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            foreach (Button button in buttons)
            {
                if (button.gameObject.name == name)
                    return button;
            }
            return null;
        }

        private static Text GetButtonLabel(GameObject root, string buttonName)
        {
            Button button = FindButton(root, buttonName);
            Assert.IsNotNull(button, "Missing button " + buttonName);
            Transform labelTransform = button.transform.Find("Label");
            Assert.IsNotNull(labelTransform, buttonName + " must have a child named Label.");
            Text label = labelTransform.GetComponent<Text>();
            Assert.IsNotNull(label, buttonName + "/Label must contain Unity UI Text.");
            return label;
        }

        [Serializable]
        private sealed class ProfileProbe
        {
            public int schemaVersion;
            public int affectedHand;
            public int operatingHand;
            public int displayMode;
            public WristOffsetProbe leftWristOffset;
            public WristOffsetProbe rightWristOffset;
        }

        [Serializable]
        private struct WristOffsetProbe
        {
            public Vector3 translationMeters;
            public Vector3 rotationEulerDegrees;
        }
    }
}
#endif
