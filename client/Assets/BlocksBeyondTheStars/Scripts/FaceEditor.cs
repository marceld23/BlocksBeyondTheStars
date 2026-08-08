// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-game pixel editor over the shared <see cref="FacePalette"/>: originally the 16×16 face editor
    /// (Character tab + main-menu Avatar Designer), generalized to serve the block-paint host too (#818) —
    /// the grid size and an optional design-library column are host-supplied, everything else (palette,
    /// eraser, eyedropper, colour wheel, drag painting, Apply/Clear/Back) is identical in every host. Faces
    /// are 32×32 as of #840, the same as block designs. <b>Apply</b> hands the encoded grid to the host; the
    /// face hosts store/send it as the avatar face, the paint host sends it as a <c>PaintBlockIntent</c>.
    /// <para>
    /// Because all three hosts share this component, a tool added here appears in the main-menu Avatar
    /// Designer, the in-game Character tab and the paint tool at once.
    /// </para>
    /// Modern uGUI, mirroring <see cref="MaterialEditor"/>.
    /// </summary>
    public sealed class FaceEditor : MonoBehaviour
    {
        // Host-supplied hooks so one editor serves the Character tab, the Avatar Designer and the block-paint
        // tool. Set these right after AddComponent (before Start runs, which is the next frame).
        public string InitialFace;              // encoded grid to preload onto the canvas
        public Func<string, string> Localizer;  // localization lookup (key → text)
        public Action<string> OnApply;          // receives the encoded grid when the player hits Apply
        public int GridSize = FacePalette.Size; // pixels per side: faces and block paint are both 32 now
        public Action OnClosed;                 // fires when the editor goes away (any path) — hosts release input here
        public Action<string, string> OnSaveDesign; // paint host: save the canvas (pixels, name) to the local library
        public Func<List<(string Name, string Pixels)>> LibraryProvider; // paint host: saved designs to load
        public string InitialName;              // paint host: the design's name, when one is being re-opened/copied
        public Action<string, string> OnShare;  // paint host: put (pixels, name) on the clipboard as a share code
        public Func<(string Pixels, string Name)?> OnImport; // paint host: read a share code off the clipboard

        private int _size;
        private int[] _grid;
        private string _name = string.Empty;
        private int _brush = 1; // current palette index (0 = eraser/transparent)

        private Texture2D _tex;
        private RectTransform _canvasRt;
        private RawImage _canvas;

        private Canvas _ui;
        private Image _activeSwatch;
        private readonly Image[] _swatches = new Image[FacePalette.Colors.Length]; // index 0 reused as eraser
        private RectTransform _libList; // library column entries (rebuilt after a save)

        private bool _picking;          // eyedropper armed: the next canvas click takes a colour, not paints one
        private Image _pickButton;      // so the armed state is visible
        private RectTransform _wheelRt; // hue/brightness ring
        private RectTransform _wheelDot; // the draggable marker on it

        private void Start()
        {
            _size = Mathf.Clamp(GridSize, 8, 64);
            _grid = new int[_size * _size];
            _tex = new Texture2D(_size, _size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            _name = InitialName ?? string.Empty;
            LoadFrom(InitialFace);
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_tex != null) Destroy(_tex);
            OnClosed?.Invoke();
        }

        // ── live painting ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_canvasRt == null) return;

            UpdateColorWheel();

            bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
            if (!left && !right) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, Input.mousePosition, null)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, Input.mousePosition, null, out var lp)) return;

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0].
            float w = _canvasRt.rect.width, h = _canvasRt.rect.height;
            float u = Mathf.Clamp01(lp.x / w);
            float fromTop = Mathf.Clamp01(-lp.y / h);
            int gx = Mathf.Clamp(Mathf.RoundToInt(u * (_size - 1)), 0, _size - 1);
            int gy = Mathf.Clamp(Mathf.RoundToInt(fromTop * (_size - 1)), 0, _size - 1); // top row = gy 0

            // Eyedropper: pick up the colour already under the cursor instead of painting over it. Asked for
            // by name ("ein Kopierer für benutzte Farben") — once a drawing has a dozen shades in it, finding
            // the one you used for the left eye by eye in the palette is guesswork.
            if (_picking && left)
            {
                int picked = _grid[gy * _size + gx];
                SetBrush(picked, _swatches[picked]);
                SetPicking(false); // one-shot, like every paint program: pick, then carry on drawing
                return;
            }

            Paint(gx, gy, right ? 0 : _brush);
        }

        /// <summary>Sets one pixel (grid + display texture) and applies it. Grid row 0 is the TOP; the texture's
        /// row 0 is the BOTTOM, so the display flips vertically.</summary>
        private void Paint(int gx, int gy, int index)
        {
            int cell = gy * _size + gx;
            if (_grid[cell] == index) return;

            _grid[cell] = index;
            _tex.SetPixel(gx, _size - 1 - gy, DisplayColor(index));
            _tex.Apply();
        }

        private static Color DisplayColor(int index)
            => index == 0 ? FacePalette.EditorBackground : (Color)FacePalette.ColorOf(index);

        private void RenderAll()
        {
            for (int gy = 0; gy < _size; gy++)
            for (int gx = 0; gx < _size; gx++)
            {
                _tex.SetPixel(gx, _size - 1 - gy, DisplayColor(_grid[gy * _size + gx]));
            }

            _tex.Apply();
        }

        private void LoadFrom(string face)
        {
            var grid = FacePalette.Decode(face, _size * _size);
            Array.Copy(grid, _grid, _grid.Length);
            RenderAll();
        }

        // ── UI ───────────────────────────────────────────────────────────────────────────────────

        private void BuildUi()
        {
            bool hasLibrary = OnSaveDesign != null || LibraryProvider != null;

            _ui = UiKit.CreateCanvas("Face Editor UI");
            _ui.sortingOrder = 60; // above the in-game menu (CraftingTechShipUI is sortingOrder 50)
            var root = _ui.transform;

            // Shared scrim + opaque panel (#588). The old backdrop was an AddPanel, whose raycastTarget is
            // false, so it never actually blocked clicks reaching the menu behind — AddModalDim does.
            // The paint host gets a wider panel: the design-library column sits right of the canvas.
            float panelW = hasLibrary ? 950f : 700f;
            var (_, panel) = UiKit.AddModalOverlay(root, hasLibrary ? 485f : 610f, 60f, panelW, 960f);
            UiKit.AddText(panel, 24f, 18f, panelW - 48f, 30f, L("ui.face.title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Paint surface (point-filtered → crisp big pixels).
            var canvasGo = new GameObject("FaceCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            _canvasRt = UiKit.Place(canvasGo, 94f, 64f, 512f, 512f);
            _canvas = canvasGo.AddComponent<RawImage>();
            _canvas.texture = _tex;

            // Palette: colours 1..N then an eraser (index 0).
            UiKit.AddText(panel, 24f, 592f, 400f, 24f, L("ui.face.palette"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            float px = 24f, py = 624f;
            int col = 0;
            for (int i = 1; i < FacePalette.Colors.Length; i++)
            {
                int idx = i;
                _swatches[i] = MakeSwatch(panel, px + col * 50f, py, FacePalette.ColorOf(i), () => SetBrush(idx, _swatches[idx]));
                if (++col >= 8) { col = 0; py += 50f; }
            }

            // Eraser swatch (transparent → shown as the editor background colour, labelled "E").
            var eraser = MakeSwatch(panel, px + col * 50f, py, FacePalette.EditorBackground, () => SetBrush(0, _swatches[0]));
            _swatches[0] = eraser;
            UiKit.AddText(eraser.transform, 0f, 0f, 44f, 44f, "E", 18, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Eyedropper + colour wheel — the two tools asked for by name.
            _pickButton = UiKit.AddButton(panel, 24f, 736f, 220f, 48f, L("ui.face.pick"), () => SetPicking(!_picking)).image;
            BuildColorWheel(panel);

            // Buttons.
            UiKit.AddButton(panel, 24f, 832f, 220f, 56f, L("ui.face.apply"), Apply);
            UiKit.AddButton(panel, 260f, 832f, 180f, 56f, L("ui.face.clear"), () => { Array.Clear(_grid, 0, _grid.Length); RenderAll(); });
            UiKit.AddButton(panel, 456f, 832f, 220f, 56f, L("ui.menu.back"), Close);

            UiKit.AddText(panel, 24f, 904f, panelW - 48f, 24f, L("ui.face.hint"), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // Design library column (paint host only): name + save the current canvas, reload saved designs,
            // and share one as a code (#846) — the same set of moves the form library offers.
            if (hasLibrary)
            {
                UiKit.AddText(panel, 700f, 64f, 226f, 24f, L("ui.paint.library"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddInput(panel, 700f, 92f, 226f, 42f, _name, v => _name = v, L("ui.paint.name"), 24, 16);
                if (OnSaveDesign != null)
                {
                    UiKit.AddButton(panel, 700f, 140f, 226f, 44f, L("ui.paint.save"), () =>
                    {
                        OnSaveDesign(FacePalette.Encode(_grid, _grid.Length), _name);
                        RebuildLibraryList();
                    });
                }

                if (OnShare != null)
                {
                    UiKit.AddButton(panel, 700f, 190f, 110f, 40f, L("ui.shape.custom.export"),
                        () => OnShare(FacePalette.Encode(_grid, _grid.Length), _name));
                    UiKit.AddButton(panel, 816f, 190f, 110f, 40f, L("ui.shape.custom.import"), () =>
                    {
                        if (OnImport?.Invoke() is { } imported)
                        {
                            _name = imported.Name;
                            LoadFrom(imported.Pixels);
                            RebuildLibraryList();
                        }
                    });
                }

                var listGo = new GameObject("PaintLibraryList", typeof(RectTransform));
                listGo.transform.SetParent(panel, false);
                _libList = UiKit.Place(listGo, 700f, 240f, 226f, 580f);
                RebuildLibraryList();
            }

            SetBrush(_brush, _swatches[_brush]);
        }

        /// <summary>(Re)fills the library column with one load-button per saved design (newest saves appear
        /// as the provider returns them; capped to what fits the column).</summary>
        private void RebuildLibraryList()
        {
            if (_libList == null || LibraryProvider == null)
            {
                return;
            }

            for (int i = _libList.childCount - 1; i >= 0; i--)
            {
                Destroy(_libList.GetChild(i).gameObject);
            }

            var entries = LibraryProvider() ?? new List<(string, string)>();
            const int maxShown = 13;
            float y = 0f;
            for (int i = 0; i < entries.Count && i < maxShown; i++)
            {
                string pixels = entries[i].Pixels;
                UiKit.AddButton(_libList, 0f, y, 226f, 42f, entries[i].Name, () => LoadFrom(pixels));
                y += 50f;
            }
        }

        /// <summary>
        /// A hue/brightness ring you pick a colour on by moving a point around it — the "Kreis" the player
        /// asked for, rather than hunting through a strip of squares.
        /// <para>
        /// It SNAPS to the nearest palette entry, and that is a deliberate limit, not an oversight: a face is
        /// stored as one hex character per pixel, so the format holds 16 colours and cannot carry a free RGB
        /// value. The wheel gives the gesture and makes the palette legible by hue; widening the palette
        /// itself would mean changing the wire/save format for faces and block designs alike.
        /// </para>
        /// </summary>
        private void BuildColorWheel(Transform panel)
        {
            // Right of the palette (which ends at x≈418) and below the paint canvas (which ends at y=576),
            // so it overlaps neither — a wheel sitting on the canvas would eat paint clicks.
            const int texSize = 128;
            const float wheelPx = 170f;
            const float wheelX = 470f, wheelY = 600f;

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float mid = (texSize - 1) / 2f;
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    float dx = (x - mid) / mid, dy = (y - mid) / mid;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 1f)
                    {
                        tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                        continue;
                    }

                    // Angle = hue all the way round; radius = saturation, so the middle is the greys the face
                    // palette is full of (outlines, eye whites) and the rim is the accents.
                    float hue = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
                    tex.SetPixel(x, y, Color.HSVToRGB(hue, Mathf.Clamp01(r), 1f));
                }
            }

            tex.Apply();

            var go = new GameObject("ColorWheel", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            _wheelRt = UiKit.Place(go, wheelX, wheelY, wheelPx, wheelPx);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            UiKit.AddText(panel, wheelX, wheelY + wheelPx + 4f, wheelPx, 22f, L("ui.face.wheel"), 13,
                UiKit.CyanDim, TextAnchor.MiddleCenter);

            var dot = new GameObject("WheelDot", typeof(RectTransform));
            dot.transform.SetParent(go.transform, false);
            _wheelDot = UiKit.Place(dot, wheelPx / 2f - 7f, wheelPx / 2f - 7f, 14f, 14f);
            var dotImg = dot.AddComponent<Image>();
            dotImg.sprite = UiKit.SolidSprite;
            dotImg.color = Color.white;
            dotImg.raycastTarget = false;
        }

        /// <summary>Drag handling for the wheel: the point follows the cursor and the brush becomes whichever
        /// palette entry is closest to the colour under it.</summary>
        private void UpdateColorWheel()
        {
            if (_wheelRt == null || !Input.GetMouseButton(0))
            {
                return;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(_wheelRt, Input.mousePosition, null)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_wheelRt, Input.mousePosition, null, out var lp)) return;

            float w = _wheelRt.rect.width, h = _wheelRt.rect.height;
            float cx = w / 2f, cy = -h / 2f;               // Place() pivots top-left: local y runs 0 → -h
            float dx = (lp.x - cx) / cx, dy = (lp.y - cy) / (h / 2f);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > 1f)
            {
                dx /= r; dy /= r; r = 1f; // clamp onto the rim instead of ignoring the drag
            }

            float hue = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
            int index = NearestPaletteIndex(Color.HSVToRGB(hue, Mathf.Clamp01(r), 1f));
            SetBrush(index, _swatches[index]);

            if (_wheelDot != null)
            {
                _wheelDot.anchoredPosition = new Vector2(cx + dx * cx - 7f, cy + dy * (h / 2f) + 7f);
            }
        }

        /// <summary>The palette entry closest to a colour in plain RGB distance. Index 0 (transparent) is
        /// never a match — the wheel picks paint, and the eraser has its own swatch.</summary>
        private static int NearestPaletteIndex(Color target)
        {
            int best = 1;
            float bestD = float.MaxValue;
            for (int i = 1; i < FacePalette.Colors.Length; i++)
            {
                Color c = FacePalette.Colors[i];
                float d = (c.r - target.r) * (c.r - target.r)
                        + (c.g - target.g) * (c.g - target.g)
                        + (c.b - target.b) * (c.b - target.b);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }

            return best;
        }

        private void SetPicking(bool on)
        {
            _picking = on;
            if (_pickButton != null)
            {
                _pickButton.color = on ? UiKit.Cyan : UiKit.PanelFill;
            }
        }

        private Image MakeSwatch(Transform parent, float x, float y, Color color, Action onClick)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, 44f, 44f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());
            return img;
        }

        private void SetBrush(int index, Image swatch)
        {
            _brush = index;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one;
            _activeSwatch = swatch;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one * 1.18f;
        }

        private void Apply()
        {
            OnApply?.Invoke(FacePalette.Encode(_grid, _grid.Length));
            Close();
        }

        private void Close() => Destroy(gameObject);

        private string L(string key) => Localizer?.Invoke(key) ?? key;
    }
}
