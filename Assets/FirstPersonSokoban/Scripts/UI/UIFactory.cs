using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FirstPersonSokoban.UI
{
    public static class UIFactory
    {
        public static readonly Color Ink = Hex("0D1921");
        public static readonly Color Panel = Hex("142731", 0.96f);
        public static readonly Color PanelLight = Hex("1C333D", 0.96f);
        public static readonly Color Accent = Hex("58E0C2");
        public static readonly Color Amber = Hex("E7AD70");
        public static readonly Color White = Hex("F2F6F4");
        public static readonly Color Muted = Hex("9BB0B6");
        public static readonly Color Line = Hex("45616A", 0.65f);

        private static Sprite roundedSprite;
        private static Font uiFont;

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

        /// <summary>
        /// 中文字体回退链：优先系统里常见的中日韩字体，都不存在时再退到 Unity 内置字体，
        /// 保证 Text 组件永远拿得到非 null 字体（无字体时中文会整片不渲染）。
        /// 本工程不打包第三方字体文件，交付时不存在授权不明资源。
        /// </summary>
        public static Font Font
        {
            get
            {
                if (uiFont != null) return uiFont;
                try { uiFont = UnityEngine.Font.CreateDynamicFontFromOSFont(FontCandidates, 24); }
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
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? Hex("D8FFF4") : Hex("D6E8EC");
            colors.pressedColor = Hex("A4BBB9");
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = Hex("5B6B70", 0.55f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            Label(image.transform, "Label", content, 18, primary ? Ink : White,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size - new Vector2(24, 8),
                TextAnchor.MiddleCenter, FontStyle.Bold);
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
                texture.Apply();
                texture.wrapMode = TextureWrapMode.Clamp;
                roundedSprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return roundedSprite;
            }
        }
    }
}
