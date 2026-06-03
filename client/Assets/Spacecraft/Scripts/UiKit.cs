using System.Collections.Generic;
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
        private static Sprite _solidSprite;

        /// <summary>A plain white sprite (tint via Image.color) — used for fills/bars.</summary>
        public static Sprite SolidSprite
        {
            get
            {
                if (_solidSprite == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color[16];
                    for (int i = 0; i < px.Length; i++)
                    {
                        px[i] = Color.white;
                    }

                    tex.SetPixels(px);
                    tex.Apply();
                    _solidSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                }

                return _solidSprite;
            }
        }

        public static Font Font =>
            _font != null ? _font
            : _font = (Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Arial", "Liberation Sans" }, 16));

        public static Sprite PanelSprite => _panelSprite != null ? _panelSprite : _panelSprite = RoundedSprite(18, 3);
        public static Sprite ButtonSprite => _buttonSprite != null ? _buttonSprite : _buttonSprite = RoundedSprite(14, 2);

        /// <summary>Creates a screen-space overlay canvas that scales with the screen (fixes high-DPI), plus an EventSystem.</summary>
        private static Texture2D _radar;

        /// <summary>A round radar/minimap face (translucent deep-blue fill + a cyan ring, transparent
        /// outside) for IMGUI HUD radars via <c>GUI.DrawTexture</c>. Cached.</summary>
        public static Texture2D RadarCircle
        {
            get
            {
                if (_radar != null)
                {
                    return _radar;
                }

                const int n = 128;
                _radar = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                float c = (n - 1) * 0.5f, r = c;
                var fill = new Color(0.04f, 0.10f, 0.20f, 0.62f);
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        px[y * n + x] = d > r ? new Color(0, 0, 0, 0) : (d > r - 3f ? Cyan : fill);
                    }
                }

                _radar.SetPixels(px);
                _radar.Apply();
                return _radar;
            }
        }

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
            // Expand = scale by the smaller of the width/height ratios, so the whole 1920x1080 layout
            // always fits (no right-edge overflow on non-16:9 / high-res monitors); extra space is margin.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
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

        private static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        /// <summary>Loads a UI icon sprite from Resources/icons by name (runtime Sprite from the
        /// Texture2D, so its import type doesn't matter); returns null if the icon isn't present.</summary>
        public static Sprite Icon(string name)
        {
            if (_icons.TryGetValue(name, out var sprite))
            {
                return sprite;
            }

            var tex = Resources.Load<Texture2D>("icons/" + name);
            sprite = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f) : null;
            _icons[name] = sprite;
            return sprite;
        }

        /// <summary>Places an icon (if it exists) at a square rect; no-op when the icon is missing.</summary>
        public static Image AddIcon(Transform parent, float x, float y, float size, string name)
        {
            var sprite = Icon(name);
            return sprite == null ? null : AddImage(parent, x, y, size, size, sprite, Color.white);
        }

        public static Image AddImage(Transform parent, float x, float y, float w, float h, Sprite sprite, Color color, Image.Type type = Image.Type.Simple)
        {
            var go = new GameObject("Image", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = type;
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

        /// <summary>Draws the title as a layered "logo": a cyan glow + a 3D extruded depth + a bright white face.</summary>
        public static void AddLogo(Transform parent, float x, float y, float w, float h, string text, int size)
        {
            var glow = new Color(0.30f, 0.75f, 1f, 0.35f);
            AddText(parent, x - 4f, y, w, h, text, size, glow, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(parent, x + 4f, y, w, h, text, size, glow, TextAnchor.MiddleLeft, FontStyle.Bold);

            for (int d = 7; d >= 1; d--)
            {
                float k = d / 7f;
                var depth = new Color(0.06f, 0.16f * (1f - k) + 0.06f, 0.30f * (1f - k) + 0.08f, 1f);
                AddText(parent, x + d, y + d, w, h, text, size, depth, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            AddText(parent, x, y, w, h, text, size, new Color(0.93f, 0.97f, 1f), TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        public static Button AddButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick, string icon = null)
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
            go.AddComponent<UiHover>();           // hover blip
            btn.onClick.AddListener(UiSound.Click); // click feedback
            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            float textX = 18f;
            if (!string.IsNullOrEmpty(icon) && AddIcon(go.transform, 14f, (h - 30f) / 2f, 30f, icon) != null)
            {
                textX = 56f;
            }

            AddText(go.transform, textX, 0f, w - textX - 10f, h, label, 22, TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            return btn;
        }

        /// <summary>Builds a themed single-line text field (background + editable text + placeholder).</summary>
        public static InputField AddInput(Transform parent, float x, float y, float w, float h, string value,
            System.Action<string> onChange, string placeholder = "")
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var bg = go.AddComponent<Image>();
            bg.sprite = ButtonSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.03f, 0.07f, 0.14f, 0.95f);

            var input = go.AddComponent<InputField>();

            var text = AddText(go.transform, 10f, 0f, w - 20f, h, string.Empty, 18, TextCol);
            text.supportRichText = false;
            var ph = AddText(go.transform, 10f, 0f, w - 20f, h, placeholder, 18, new Color(0.55f, 0.62f, 0.72f), TextAnchor.MiddleLeft, FontStyle.Italic);

            input.textComponent = text;
            input.placeholder = ph;
            input.text = value ?? string.Empty;
            input.caretColor = Cyan;
            input.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            if (onChange != null)
            {
                input.onValueChanged.AddListener(v => onChange(v));
            }

            return input;
        }

        /// <summary>
        /// A vertically-scrolling list: a masked viewport (top-left anchored rect) plus a content
        /// child with a <see cref="VerticalLayoutGroup"/> + <see cref="ContentSizeFitter"/>. Returns the
        /// content transform — parent pooled rows (with a <c>LayoutElement</c>) to it; inactive rows are
        /// ignored by the layout. Modern replacement for IMGUI <c>BeginScrollView</c>.
        /// </summary>
        public static RectTransform ScrollList(Transform parent, float x, float y, float w, float h, float spacing = 2f)
        {
            var viewGo = new GameObject("Viewport", typeof(RectTransform));
            viewGo.transform.SetParent(parent, false);
            Place(viewGo, x, y, w, h);
            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            viewGo.AddComponent<RectMask2D>();
            var viewImg = viewGo.AddComponent<Image>();
            viewImg.color = new Color(0f, 0f, 0f, 0.18f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            vlg.spacing = spacing;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewGo.GetComponent<RectTransform>();
            scroll.content = content;
            return content;
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
