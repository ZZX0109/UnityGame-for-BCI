using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StreetCrossing.UI
{
    /// <summary>Small UI construction helpers. No fixed screen-coordinate drawing or imported UI art.</summary>
    internal sealed class StreetUIElements
    {
        internal static readonly Color Ink = new Color(0.13f, 0.23f, 0.19f);
        internal static readonly Color Muted = new Color(0.32f, 0.39f, 0.33f);
        internal static readonly Color Paper = new Color(0.97f, 0.95f, 0.88f, 0.98f);
        internal static readonly Color Green = new Color(0.22f, 0.38f, 0.29f);
        internal static readonly Color SoftGreen = new Color(0.85f, 0.89f, 0.78f);
        private readonly TMP_FontAsset font;
        internal StreetUIElements(TMP_FontAsset asset) { font = asset; }
        internal RectTransform Rect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false); return go.GetComponent<RectTransform>();
        }
        internal static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
        }
        internal Image Image(RectTransform rect, Color color, bool raycast = false)
        {
            Image image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = raycast; return image;
        }
        internal TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color? color = null)
        {
            RectTransform rect = Rect(name, parent); rect.gameObject.SetActive(false);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSharedMaterial = font.material; text.text = value; text.fontSize = size;
            text.color = color ?? Ink; text.enableWordWrapping = true; text.richText = false;
            text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false; text.margin = Vector4.zero;
            text.alignment = TextAlignmentOptions.TopLeft;
            rect.gameObject.SetActive(true); return text;
        }
        internal static VerticalLayoutGroup Vertical(RectTransform rect, int padding = 0, float spacing = 10)
        {
            VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding); layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; return layout;
        }
        internal static HorizontalLayoutGroup Horizontal(RectTransform rect, float spacing = 12)
        {
            HorizontalLayoutGroup layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter; return layout;
        }
        internal static LayoutElement Size(GameObject go, float height = -1, float width = -1, float flexibleHeight = 0)
        {
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height; element.preferredWidth = width; element.flexibleHeight = flexibleHeight;
            return element;
        }
        internal Button Button(string name, Transform parent, string label, UnityAction action, bool primary = false)
        {
            RectTransform rect = Rect(name, parent); Image background = Image(rect, primary ? Green : SoftGreen, true);
            Button button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = background;
            ColorBlock colors = button.colors; colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.05f, 1.04f, 0.98f); colors.pressedColor = new Color(0.82f, 0.87f, 0.77f);
            colors.selectedColor = new Color(1.08f, 1.07f, 0.92f); colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.6f); button.colors = colors;
            Size(rect.gameObject, 52).minHeight = 48;
            TextMeshProUGUI caption = Text("Caption", rect, label, 20, primary ? Color.white : Ink);
            caption.alignment = TextAlignmentOptions.Midline; Stretch(caption.rectTransform, 14, 14, 8, 8);
            button.onClick.AddListener(action); return button;
        }
        internal RectTransform ScrollContent(Transform parent)
        {
            RectTransform scroll = Rect("Scrollable content", parent); Size(scroll.gameObject, -1, -1, 1).minHeight = 100;
            ScrollRect scrolling = scroll.gameObject.AddComponent<ScrollRect>(); scrolling.horizontal = false;
            scrolling.vertical = true; scrolling.scrollSensitivity = 30; scrolling.movementType = ScrollRect.MovementType.Clamped;
            RectTransform viewport = Rect("Viewport", scroll); Stretch(viewport);
            Image(viewport, new Color(1, 1, 1, 0.001f), true); viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = Vector2.zero;
            Vertical(content, 2, 12); ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrolling.viewport = viewport; scrolling.content = content; return content;
        }
    }
}
