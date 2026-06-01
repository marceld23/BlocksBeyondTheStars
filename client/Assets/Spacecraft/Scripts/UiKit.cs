using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// uGUI sci-fi theme toolkit (M27 UI rework). Builds a DPI-scaled overlay canvas + an
    /// EventSystem, and provides procedural holographic panel/button sprites, a font and element
    /// helpers — all in code, no bundled assets. Colours match the deep-blue / cyan mockup.
    /// Layout helpers use top-left anchored coordinates in a 1920×1080 reference space.
    /// </summary>
    public static class UiKit
    {
        public static readonly Color Cyan = new Color(0.40f, 0.82f, 1.00f);
        public static readonly Color CyanDim = new Color(0.30f, 0.55f, 0.72f);
        public static readonly Color Panel = new Color(0.05f, 0.12f, 0.24f, 0.80f);
        public static readonly Color PanelFill = new Color(1f, 1f, 1f, 1f);
        public static readonly Color TextCol = new Color(0.86f, 0.93f, 1.00f);
        public static readonly Color Ok = new Color(0.35f, 0.95f, 0.55f);

        private static Font _font;
        private static Sprite _panelSprite;
        private static Sprite _buttonSprite;

        public static Font Font =>
            _font != null ? _font
            : _font = (Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Arial", "Liberation Sans" }, 16));

        public static Sprite PanelSprite => _panelSprite != null ? _panelSprite : _panelSprite = RoundedSprite(18, 3);
        public static Sprite ButtonSprite => _buttonSprite != null ? _buttonSprite : _buttonSprite = RoundedSprite(14, 2);

        /// <summary>Creates a screen-space overlay canvas that scales with the screen (fixes high-DPI), plus an EventSystem.</summary>
        public static Canvas CreateCanvas(string name)
        {
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Positions a UI element by a top-left anchored rect in the 1920×1080 reference space.</summary>
        public static RectTransform Place(GameObject go, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        public static Image AddPanel(Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.sprite = PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text AddText(Transform parent, float x, float y, float w, float h, string text, int size,
            Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button AddButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.sprite = ButtonSprite;
            img.type = Image.Type.Sliced;
            img.color = PanelFill;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var c = btn.colors;
            c.normalColor = new Color(0.70f, 0.74f, 0.80f, 1f); // dim
            c.highlightedColor = Color.white;                    // brighten on hover
            c.pressedColor = Cyan;
            c.selectedColor = Color.white;
            c.fadeDuration = 0.08f;
            btn.colors = c;
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            AddText(go.transform, 18f, 0f, w - 28f, h, label, 22, TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            return btn;
        }

        /// <summary>
        /// A rounded-rect sprite with a baked bright-cyan border and a translucent dark-blue fill,
        /// set up for 9-slicing so panels/buttons scale without distorting the corners.
        /// </summary>
        private static Sprite RoundedSprite(int radius, int border)
        {
            int size = radius * 2 + 8;
            var edge = Cyan;
            var fill = new Color(0.05f, 0.12f, 0.24f, 0.82f);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance into a rounded corner (0 along the straight edges).
                    float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                    float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c;
                    if (dist > radius)
                    {
                        c = new Color(0f, 0f, 0f, 0f);
                    }
                    else if (dist > radius - border)
                    {
                        c = edge;
                    }
                    else
                    {
                        c = fill;
                    }

                    px[y * size + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply();

            int b = radius + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }
    }
}
