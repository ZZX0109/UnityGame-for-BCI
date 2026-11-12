using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FirstPersonSokoban.UI
{
    public static class UIFactory
    {
        public static readonly Color Ink = Hex("0A1622");
        public static readonly Color Panel = Hex("142B3B", 0.98f);
        public static readonly Color PanelLight = Hex("1C384A", 0.98f);
        public static readonly Color Accent = Hex("76E5CD");
        public static readonly Color Amber = Hex("F0C58A");
        public static readonly Color White = Hex("F3F7FA");
        public static readonly Color Muted = Hex("B2C6D3");
        public static readonly Color Line = Hex("7397AE", 0.30f);

        private static Sprite roundedSprite;
        private static Font uiFont;
        private static Texture2D roundedTexture;
        private static Sprite gradientSprite;
        private static Texture2D gradientTexture;
        private static bool ownsFont;
        private static int resourceUsers;

        public static void AcquireResources() => resourceUsers++;

        // Imported fonts belong to Resources; only objects created here are destroyed here.
        public static void ReleaseResources()
        {
            resourceUsers = Mathf.Max(0, resourceUsers - 1);
            if (resourceUsers != 0) return;
            if (roundedSprite != null) Object.Destroy(roundedSprite);
            if (roundedTexture != null) Object.Destroy(roundedTexture);
            if (gradientSprite != null) Object.Destroy(gradientSprite);
            if (gradientTexture != null) Object.Destroy(gradientTexture);
            if (ownsFont && uiFont != null) Object.Destroy(uiFont);
            roundedSprite = gradientSprite = null;
            roundedTexture = gradientTexture = null;
            uiFont = null;
            ownsFont = false;
        }

        public static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            color.a = alpha;
            return color;
        }

        private static readonly string[] FontCandidates =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "PingFang SC",
            "Noto Sans CJK SC", "Source Han Sans SC", "WenQuanYi Micro Hei",
            "Microsoft JhengHei", "Arial Unicode MS", "Segoe UI", "Arial"
        };

        /// <summary>Bundled OFL Chinese font first; OS fallback for developer-only installs.</summary>
        public static Font Font
        {
            get
            {
                if (uiFont != null) return uiFont;
                uiFont = Resources.Load<Font>("Fonts/SokobanUISans");
                if (uiFont != null) return uiFont;
                try
                {
                    uiFont = UnityEngine.Font.CreateDynamicFontFromOSFont(FontCandidates, 24);
                    ownsFont = uiFont != null;
                }
                catch (System.Exception) { uiFont = null; }
                if (uiFont == null)
                {
                    foreach (string builtin in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
                    {
                        try { uiFont = Resources.GetBuiltinResource<Font>(builtin); }
                        catch (System.Exception) { uiFont = null; }
                        if (uiFont != null) break;
                    }
                }
                return uiFont;
            }
        }

        public static Image Background(Transform parent, string name)
        {
            Image image = Stretch(parent, name).gameObject.AddComponent<Image>();
            if (gradientSprite == null)
            {
                const int size = 128;
                gradientTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                gradientTexture.name = "Sokoban UI Gradient";
                gradientTexture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float glow = Mathf.Clamp01(1f - Vector2.Distance(
                        new Vector2(x / 127f, y / 127f), new Vector2(0.25f, 0.65f)));
                    pixels[y * size + x] = Color.Lerp(Hex("08131F"), Hex("1C394A"), glow * 0.88f);
                }
                gradientTexture.SetPixels(pixels);
                gradientTexture.Apply(false, true);
                gradientSprite = Sprite.Create(gradientTexture, new UnityEngine.Rect(0, 0, size, size),
                    Vector2.one * 0.5f);
            }
            image.sprite = gradientSprite;
            image.raycastTarget = false;
            return image;
        }

        public static void Border(Image image, Color color)
        {
            Outline outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>按父节点四边留白拉伸，用于 ScrollRect 的 viewport 等局部布局。</summary>
        public static RectTransform RectStretch(Transform parent, string name,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        public static Image PanelImage(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(parent, name, anchor, pivot, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string name, string content, int fontSize, Color color,
            Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Rect(parent, name, anchor, pivot, position, size);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string name, string content, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, UnityAction action, bool primary = true)
        {
            Image image = PanelImage(parent, name, anchor, pivot, position, size,
                primary ? Accent : PanelLight);
            Button button = image.gameObject.AddComponent<Button>();
            image.raycastTarget = true;
            Border(image, primary ? new Color(0.75f, 1f, 0.93f, 0.45f) : Line);
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? Hex("D8FFF4") : Hex("D6E8EC");
            colors.pressedColor = Hex("A4BBB9");
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = Hex("75818E", 0.72f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            Label(image.transform, "Label", content, 20, primary ? Ink : White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(24, 8),
                TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform label = image.transform.Find("Label") as RectTransform;
            label.anchorMin = Vector2.zero;
            label.anchorMax = Vector2.one;
            label.offsetMin = new Vector2(12f, 4f);
            label.offsetMax = new Vector2(-12f, -4f);
            return button;
        }

        public static Image LineImage(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = Rect(parent, name, anchor, pivot, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite RoundedSprite
        {
            get
            {
                if (roundedSprite != null) return roundedSprite;
                const int size = 64;
                const int radius = 14;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                roundedTexture = texture;
                texture.name = "Sokoban UI Rounded Panel";
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x, 0, x - (size - 1 - radius));
                    float dy = Mathf.Max(radius - y, 0, y - (size - 1 - radius));
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius + 0.5f - distance));
                }
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                texture.wrapMode = TextureWrapMode.Clamp;
                roundedSprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return roundedSprite;
            }
        }
    }
}
