// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
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
        public static readonly Color Warn = new Color(1.00f, 0.72f, 0.28f); // amber — caution / developer-tool labels
        public static readonly Color TabLocked = new Color(0.30f, 0.34f, 0.42f, 0.85f); // dimmed tab whose context isn't met (stays clickable)

        /// <summary>True while a uGUI text field has keyboard focus — Esc/Tab hotkey handlers must then
        /// leave the keystroke to the field (unfocus/advance) instead of closing their screen (#413 N5).</summary>
        public static bool TextFieldFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }

            var field = selected.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        private static Font _font;
        private static Sprite _panelSprite;
        private static Sprite _buttonSprite;
        private static Sprite _solidSprite;
        private static Sprite _spinnerSprite;

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
            : _font = (Resources.Load<Font>("fonts/Rajdhani-Medium") // bundled sci-fi UI font (OFL, full DE glyphs)
                       ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Arial", "Liberation Sans" }, 16));

        public static Sprite PanelSprite => _panelSprite != null ? _panelSprite : _panelSprite = RoundedSprite(18, 3);
        public static Sprite ButtonSprite => _buttonSprite != null ? _buttonSprite : _buttonSprite = RoundedSprite(14, 2);
        public static Sprite SpinnerSprite => _spinnerSprite != null ? _spinnerSprite : _spinnerSprite = SpinnerRingSprite();

        /// <summary>Set from <see cref="ClientSettings.Apply"/>: reduced-effects users keep instant panel snaps.</summary>
        public static bool ReducedMotion;

        /// <summary>Fade+rise-in transition (~0.14 s, unscaled) on a UI root: attaches/reuses a CanvasGroup
        /// and animates alpha 0→1 plus a small upward slide. Instant under <see cref="ReducedMotion"/>.
        /// Canvas roots only fade (their RectTransform is driven by the canvas).</summary>
        public static void TransitionIn(GameObject root, float slide = 14f)
        {
            if (root == null)
            {
                return;
            }

            var group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.AddComponent<CanvasGroup>();
            }

            if (ReducedMotion)
            {
                group.alpha = 1f;
                return;
            }

            var run = root.GetComponent<UiTransition>();
            if (run == null)
            {
                run = root.AddComponent<UiTransition>();
            }

            run.Begin(group, slide);
        }

        private sealed class UiTransition : MonoBehaviour
        {
            private const float Life = 0.14f;
            private CanvasGroup _group;
            private RectTransform _rt;
            private Vector2 _home;
            private float _slide, _t;
            private bool _homed;

            public void Begin(CanvasGroup group, float slide)
            {
                _group = group;
                _rt = GetComponent<Canvas>() == null ? transform as RectTransform : null; // canvas roots fade only
                if (_rt != null && !_homed)
                {
                    _home = _rt.anchoredPosition;
                    _homed = true;
                }

                _slide = slide;
                _t = 0f;
                enabled = true;
                Apply(0f);
            }

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_t / Life);
                Apply(k * k * (3f - 2f * k)); // smoothstep
                if (k >= 1f)
                {
                    enabled = false;
                }
            }

            private void Apply(float k)
            {
                if (_group != null)
                {
                    _group.alpha = k;
                }

                if (_rt != null)
                {
                    _rt.anchoredPosition = _home + Vector2.down * (_slide * (1f - k));
                }
            }
        }

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

        /// <summary>The visor HUD camera (set by <see cref="VisorHud"/> when the holographic pipeline is
        /// active); null = no visor, diegetic canvases render as a plain screen-space overlay.</summary>
        public static Camera HudCamera;

        /// <summary>Layer the diegetic HUD renders on so only the visor camera (not the main camera) sees
        /// it; -1 until the visor pipeline is set up.</summary>
        public static int HudLayer = -1;

        /// <summary>Creates a canvas for the **diegetic HUD**: routed through the visor HUD camera (so the
        /// <c>BlocksBeyondTheStars/Visor</c> pass can curve/glow it) when that pipeline is up, otherwise a normal
        /// screen-space overlay. Menus/dialogs must keep using <see cref="CreateCanvas"/> (stay flat).</summary>
        public static Canvas CreateDiegeticCanvas(string name, float refW = 1920f, float refH = 1080f)
        {
            // Diegetic == the in-world HUD, so it always follows the player's UI-scale setting (#483).
            var canvas = CreateCanvas(name, refW, refH, userScalable: true);
            DiegeticCanvases.Add(canvas);
            ApplyDiegeticTarget(canvas);
            return canvas;
        }

        // Live diegetic canvases, so the visor pipeline can re-target them when it engages/tears down at
        // RUNTIME (the preset/visor toggle in the pause menu). Destroyed canvases go stale in here and are
        // pruned on the next retarget.
        private static readonly System.Collections.Generic.List<Canvas> DiegeticCanvases
            = new System.Collections.Generic.List<Canvas>();

        /// <summary>Points one diegetic canvas at the CURRENT visor state: through the HUD camera when the
        /// visor RT pipeline is up, else a plain screen-space overlay (the flat-HUD fallback path).</summary>
        private static void ApplyDiegeticTarget(Canvas canvas)
        {
            if (HudCamera != null && HudLayer >= 0)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = HudCamera;
                canvas.planeDistance = 1f;
                canvas.gameObject.layer = HudLayer;
                if (canvas.gameObject.GetComponent<HudLayerEnforcer>() == null)
                {
                    canvas.gameObject.AddComponent<HudLayerEnforcer>().Layer = HudLayer;
                }
            }
            else
            {
                // Overlay mode draws the canvas directly (no camera involved), so a canvas that keeps its
                // HUD layer + enforcer from an earlier visor phase still shows correctly.
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }
        }

        /// <summary>Re-points every live diegetic canvas at the current visor state. Called by
        /// <see cref="VisorHud"/> when its RT pipeline engages or tears down at runtime.</summary>
        public static void RetargetDiegeticCanvases()
        {
            for (int i = DiegeticCanvases.Count - 1; i >= 0; i--)
            {
                var canvas = DiegeticCanvases[i];
                if (canvas == null)
                {
                    DiegeticCanvases.RemoveAt(i); // canvas was destroyed (world teardown)
                    continue;
                }

                ApplyDiegeticTarget(canvas);
            }
        }

        // The HUD canvases use a smaller reference than the 1920×1080 menus, so ScaleWithScreenSize draws the
        // same layout ~1.25× bigger (more readable on high-res monitors) while Expand keeps it fitting any aspect.
        public const float HudRefW = 1536f, HudRefH = 864f;

        /// <summary>Bounds of the player's UI-scale setting (#483).</summary>
        public const float UserScaleMin = 0.8f, UserScaleMax = 1.6f;

        /// <summary>The player's UI-scale multiplier, driven from <see cref="ClientSettings.UiScale"/> via
        /// <see cref="SetUserScale"/>. Applied by DIVIDING a canvas' reference resolution — a smaller
        /// reference means ScaleWithScreenSize draws the same layout bigger. 1 = the shipped default.</summary>
        public static float UserScale { get; private set; } = 1f;

        // Canvases that follow UserScale, with the reference they were BUILT at (dividing the live value
        // repeatedly would compound). Menus are deliberately absent: they lay out in absolute 1920
        // coordinates, so scaling them up pushes content off-screen — see #483.
        private sealed class ScaledCanvas
        {
            public Canvas Canvas;
            public Vector2 BaseRef;
        }

        private static readonly System.Collections.Generic.List<ScaledCanvas> ScalableCanvases
            = new System.Collections.Generic.List<ScaledCanvas>();

        /// <summary>Sets the player's UI scale and re-stamps every live scalable canvas, so the pause-menu
        /// setting takes effect instantly instead of on the next world load.</summary>
        public static void SetUserScale(float scale)
        {
            float s = Mathf.Clamp(scale, UserScaleMin, UserScaleMax);
            if (Mathf.Approximately(s, UserScale))
            {
                return;
            }

            UserScale = s;
            for (int i = ScalableCanvases.Count - 1; i >= 0; i--)
            {
                var entry = ScalableCanvases[i];
                if (entry.Canvas == null)
                {
                    ScalableCanvases.RemoveAt(i); // destroyed with its world
                    continue;
                }

                var scaler = entry.Canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.referenceResolution = entry.BaseRef / UserScale;
                }
            }
        }

        /// <param name="userScalable">True for HUD-style canvases, which follow the player's UI-scale
        /// setting. Menus must stay false — their layout is absolute 1920 coordinates.</param>
        public static Canvas CreateCanvas(string name, float refW = 1920f, float refH = 1080f, bool userScalable = false)
        {
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                var system = es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();

                // Touch devices: scale the tap-vs-drag threshold with the display's DPI. The default (10 px)
                // is tuned for a mouse — on a high-DPI tablet a finger wobbles further than that, so menu taps
                // get misread as drags and silently do nothing. ~1 mm of physical movement is the usual bound.
                if (TouchControlsUi.ShouldShow() && Screen.dpi > 0f)
                {
                    system.pixelDragThreshold = Mathf.Max(10, Mathf.RoundToInt(Screen.dpi / 25.4f)); // ≈1 mm
                }
            }

            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            var baseRef = new Vector2(refW, refH);
            scaler.referenceResolution = userScalable ? baseRef / UserScale : baseRef;
            // Expand = scale by the smaller of the width/height ratios, so the whole 1920x1080 layout
            // always fits (no right-edge overflow on non-16:9 / high-res monitors); extra space is margin.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            if (userScalable)
            {
                ScalableCanvases.Add(new ScaledCanvas { Canvas = canvas, BaseRef = baseRef });
            }

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

        /// <summary>Places a pre-resolved sprite at a square rect (used for content-styled item icons);
        /// no-op when the sprite is null. The tint lets toxic consumables render green.</summary>
        public static Image AddIconSprite(Transform parent, float x, float y, float size, Sprite sprite, Color tint)
        {
            return sprite == null ? null : AddImage(parent, x, y, size, size, sprite, tint);
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

        /// <summary>A full-screen dark scrim for modal overlays: dims the menu/scene behind a dialog so the
        /// foreground panel reads clearly, and swallows clicks meant for the panel (raycast target on). Add it to
        /// the canvas root right before the dialog panel so the panel draws on top; parent the panel under the
        /// returned scrim's transform. Returns the scrim <see cref="Image"/> (use <c>.gameObject</c> to show/hide
        /// the whole overlay).</summary>
        public static Image AddModalDim(Transform parent, float alpha = 0.7f)
        {
            var dim = AddImage(parent, 0f, 0f, 1920f, 1080f, SolidSprite, new Color(0f, 0f, 0f, alpha));
            dim.raycastTarget = true; // swallow clicks behind the dialog
            return dim;
        }

        /// <summary>A modal dialog panel: the themed frame plus an OPAQUE dark backing, so whatever lies
        /// behind the dialog can never mix into its content (the plain panel sprite is translucent by
        /// design — fine for HUD chrome, unreadable for forms). The backing is a CHILD of the panel, so
        /// hiding/destroying the panel takes it along; add dialog content to the returned transform (it
        /// draws above the backing). Two stacked sliced layers ≈ fully opaque with rounded corners.</summary>
        public static Transform AddDialogPanel(Transform parent, float x, float y, float w, float h)
        {
            var panel = AddPanel(parent, x, y, w, h, Panel).transform;
            var dark = new Color(0.02f, 0.05f, 0.11f, 1f);
            AddImage(panel, 2f, 2f, w - 4f, h - 4f, PanelSprite, dark, Image.Type.Sliced);
            AddImage(panel, 2f, 2f, w - 4f, h - 4f, PanelSprite, dark, Image.Type.Sliced);
            return panel;
        }

        /// <summary>A visible vertical scrollbar for a <see cref="ScrollRect"/> — the wheel already
        /// scrolls, but a draggable handle shows WHERE you are and jumps anywhere in one drag (long
        /// lists like the settings screen). Place it along the viewport's right edge; it is wired as
        /// the ScrollRect's verticalScrollbar with Permanent visibility (no viewport resizing).</summary>
        public static Scrollbar AddVerticalScrollbar(Transform parent, ScrollRect scroll, float x, float y, float w, float h)
        {
            var go = new GameObject("VScrollbar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, w, h);
            var track = go.AddComponent<Image>();
            track.sprite = ButtonSprite;
            track.type = Image.Type.Sliced;
            track.color = new Color(0.03f, 0.07f, 0.14f, 0.9f);

            var bar = go.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            var areaGo = new GameObject("SlidingArea", typeof(RectTransform));
            areaGo.transform.SetParent(go.transform, false);
            var area = areaGo.GetComponent<RectTransform>();
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(2f, 2f);
            area.offsetMax = new Vector2(-2f, -2f);

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(areaGo.transform, false);
            var handleRt = handleGo.GetComponent<RectTransform>();
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            var handle = handleGo.AddComponent<Image>();
            handle.sprite = ButtonSprite;
            handle.type = Image.Type.Sliced;
            handle.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.55f);

            bar.handleRect = handleRt;
            bar.targetGraphic = handle;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return bar;
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

            var labelText = AddText(go.transform, textX, 0f, w - textX - 10f, h, label, 22, TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            // Shrink the label to fit its button so a longer word (e.g. German "Einstellungen"/"Schließen") never
            // spills out of the frame (B57); it stays at 22 when it fits and only scales down when it must.
            labelText.resizeTextForBestFit = true;
            labelText.resizeTextMinSize = 11;
            labelText.resizeTextMaxSize = 22;
            return btn;
        }

        /// <summary>Pins a small amber "new content" badge dot to the top-right corner of a freshly built button,
        /// so a menu entry can flag that something new is waiting behind it. Idempotent per rebuild.</summary>
        public static void AddBadge(Component button, float buttonW)
        {
            if (button == null)
            {
                return;
            }

            var dot = AddImage(button.transform, buttonW - 18f, 4f, 12f, 12f, SolidSprite, new Color(1f, 0.72f, 0.18f));
            dot.rectTransform.SetAsLastSibling();
            var glow = AddImage(button.transform, buttonW - 21f, 1f, 18f, 18f, SolidSprite, new Color(1f, 0.72f, 0.18f, 0.35f));
            glow.rectTransform.SetSiblingIndex(dot.rectTransform.GetSiblingIndex()); // soft halo behind the dot
        }

        // --- Quick-bar slot (shared by the on-foot hotbar + the flight ship-systems bar so both read alike) ---

        public static readonly Color SlotIdle = new Color(0.04f, 0.10f, 0.20f, 0.88f);
        public static readonly Color SlotSelected = new Color(0.10f, 0.42f, 0.66f, 0.98f);

        /// <summary>One quick-bar cell: a framed box, a big square icon that fills the cell, a hotkey number and a
        /// name caption, plus a selection-ring overlay. Style with <see cref="StyleQuickSlot"/>.</summary>
        public struct QuickSlot
        {
            public RectTransform Rt;     // the slot box (scaled up when selected)
            public Image Border;         // the box fill / frame (tinted by selection)
            public Image Ring;           // bright selection outline (toggled on the active slot)
            public RawImage Icon;        // the block-atlas / item texture
            public Text Num, Name;
        }

        /// <summary>Builds a quick-bar cell at a top-left anchored rect. The icon nearly fills the box (only a thin
        /// inset) so the graphic reads large; the number sits top-left and the name caption along the bottom.</summary>
        public static QuickSlot MakeQuickSlot(Transform parent, float x, float y, float size)
        {
            var ring = AddImage(parent, x - 3f, y - 3f, size + 6f, size + 6f, PanelSprite, Cyan); // outline, behind the box
            ring.type = Image.Type.Sliced;
            ring.enabled = false;

            var box = AddPanel(parent, x, y, size, size, SlotIdle);

            float inset = 6f;
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(box.transform, false);
            Place(iconGo, inset, inset, size - inset * 2f, size - inset * 2f);
            var icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            icon.enabled = false;

            var num = AddText(box.transform, 5f, 2f, size - 8f, 18f, string.Empty, 15, TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            AddOutline(num);
            var name = AddText(box.transform, 2f, size - 17f, size - 4f, 16f, string.Empty, 12, TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            AddOutline(name);

            return new QuickSlot { Rt = box.rectTransform, Border = box, Ring = ring, Icon = icon, Num = num, Name = name };
        }

        /// <summary>Applies the selected/idle look to a quick-bar cell: a bright fill + a cyan outline ring on the
        /// active slot, so the held tool / active system is unmistakable. (The ring sits just behind the cell and
        /// stays aligned at any size, so no scale-up is used — that would drift against the top-left pivot.)</summary>
        public static void StyleQuickSlot(in QuickSlot s, bool selected)
        {
            if (s.Border != null)
            {
                s.Border.color = selected ? SlotSelected : SlotIdle;
            }

            if (s.Ring != null)
            {
                s.Ring.enabled = selected;
            }
        }

        /// <summary>A dark rounded backplate strip behind a quick-bar row, so the cells read as one HUD element
        /// separated from the busy world, with a faint cyan keyline along the bottom.</summary>
        public static void QuickBackplate(Transform parent, float x, float y, float w, float h)
        {
            AddPanel(parent, x, y, w, h, new Color(0.02f, 0.05f, 0.11f, 0.62f));
            AddImage(parent, x + 6f, y + h - 3f, w - 12f, 2f, SolidSprite, new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f));
        }

        /// <summary>Adds a crisp dark outline to small HUD text so it stays legible over bright terrain/space.</summary>
        public static void AddOutline(Graphic g)
        {
            if (g == null)
            {
                return;
            }

            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
        }

        /// <summary>Builds a themed single-line text field (background + editable text + placeholder).</summary>
        public static InputField AddInput(Transform parent, float x, float y, float w, float h, string value,
            System.Action<string> onChange, string placeholder = "", int maxLength = 0)
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
            if (maxLength > 0) input.characterLimit = maxLength;
            input.caretColor = Cyan;
            input.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            if (onChange != null)
            {
                input.onValueChanged.AddListener(v => onChange(v));
            }

            // WebGL on a touch device can't summon a soft keyboard — route taps through the browser prompt.
            // No-op (not even a component) everywhere else.
            TouchTextEntry.Attach(input, placeholder);

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
            // Unity's default scrollSensitivity is 1.0 → a mouse wheel barely nudges the list (the editors'
            // palette felt "stuck"). Match the rest of the UI's wheel speed and clamp so it can't over-scroll.
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
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

        /// <summary>A cyan ring with an alpha gradient around its circumference (a fading tail) — rotated
        /// by <see cref="SpinnerRotate"/> it reads as a smooth loading spinner.</summary>
        private static Sprite SpinnerRingSprite()
        {
            const int size = 48;
            float c = (size - 1) / 2f, outer = c, inner = c - 7f;
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
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color col = new Color(0f, 0f, 0f, 0f);
                    if (d <= outer && d >= inner)
                    {
                        // Angle 0..1 around the ring → alpha, so the ring fades to transparent (the tail).
                        float t = (Mathf.Atan2(dy, dx) + Mathf.PI) / (2f * Mathf.PI);
                        col = new Color(Cyan.r, Cyan.g, Cyan.b, t);
                    }

                    px[y * size + x] = col;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>A small loading spinner. When <paramref name="follow"/> is given, it is visible only while
        /// that label shows an in-progress message (its text contains "…") — so it auto-appears during
        /// waking/stopping/deleting and hides on the result, with no per-handler wiring.</summary>
        public static GameObject AddSpinner(Transform parent, float x, float y, float size, Text follow = null)
        {
            var go = new GameObject("Spinner", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Place(go, x, y, size, size);
            var img = go.AddComponent<Image>();
            img.sprite = SpinnerSprite;
            img.raycastTarget = false;
            var rot = go.AddComponent<SpinnerRotate>();
            rot.Bind(img, follow);
            return go;
        }

        /// <summary>Spins its image and (when bound to a label) shows it only while an action is in progress.</summary>
        private sealed class SpinnerRotate : MonoBehaviour
        {
            private Image _img;
            private Text _follow;

            public void Bind(Image img, Text follow)
            {
                _img = img;
                _follow = follow;
                if (_follow != null && _img != null)
                {
                    _img.enabled = false; // idle until an in-progress message appears
                }
            }

            private void Update()
            {
                transform.Rotate(0f, 0f, -220f * Time.unscaledDeltaTime);
                if (_follow != null && _img != null)
                {
                    bool busy = !string.IsNullOrEmpty(_follow.text) && _follow.text.Contains("…");
                    if (_img.enabled != busy)
                    {
                        _img.enabled = busy;
                    }
                }
            }
        }
    }
}
