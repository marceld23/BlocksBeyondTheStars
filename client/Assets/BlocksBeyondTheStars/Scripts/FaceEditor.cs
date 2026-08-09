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

        // Body-paint hosts (#874): a NON-square grid holding a part's unfolded 32×32 face regions, and
        // custom payload codecs (the wire format is concatenated face chunks, not the grid's row-major
        // order). The editor paints ONE region at a time at full canvas size (16 px cells, same as the
        // face) and shows all regions STACKED as live click-to-select tiles beside the canvas — a whole
        // strip squeezed onto one canvas gave 4 px cells, far too small to draw on (playtest 2026-08-09).
        // All of this defaults off, so the three classic square hosts are untouched.
        public int GridW;                       // grid cells wide (0 = square GridSize)
        public int GridH;                       // grid cells high (0 = square GridSize)
        public string TitleKey = "ui.face.title";
        public string HintKey = "ui.face.hint";
        public Func<string, int[]> DecodeGrid;  // payload → grid (length GridW×GridH)
        public Func<int[], string> EncodeGrid;  // grid → payload
        public string[] ColumnLabelKeys;        // one label per 32-cell column block (region face names)
        public string[] RowLabelKeys;           // one label per 32-cell row block (Links/Rechts), or null

        private int _size;
        private int _w, _h;
        private int[] _grid;
        private string _name = string.Empty;
        private int _brush = 1; // current palette index (0 = eraser/transparent)

        // Region mode (body-paint hosts): the grid is a row-major arrangement of 32×32 face regions;
        // the main canvas crops the shared texture to the ACTIVE region via RawImage.uvRect, so tiles
        // and canvas both update live from the same SetPixel.
        private const int RegionCells = 32;
        private bool RegionMode => ColumnLabelKeys != null && ColumnLabelKeys.Length > 0;
        private int _regionCols, _regionRows;
        private int _activeRegion;
        private Text _activeRegionLabel;
        private Image[] _regionFrames; // tile highlight frames, indexed by region

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
            _w = GridW > 0 ? Mathf.Clamp(GridW, 8, 256) : _size;
            _h = GridH > 0 ? Mathf.Clamp(GridH, 8, 256) : _size;
            _regionCols = Mathf.Max(1, _w / RegionCells);
            _regionRows = Mathf.Max(1, _h / RegionCells);
            _grid = new int[_w * _h];
            _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false)
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
            int gx, gy;
            if (RegionMode)
            {
                // The canvas shows only the active 32×32 region — map into its cell block.
                gx = (_activeRegion % _regionCols) * RegionCells
                    + Mathf.Clamp(Mathf.RoundToInt(u * (RegionCells - 1)), 0, RegionCells - 1);
                gy = (_activeRegion / _regionCols) * RegionCells
                    + Mathf.Clamp(Mathf.RoundToInt(fromTop * (RegionCells - 1)), 0, RegionCells - 1);
            }
            else
            {
                gx = Mathf.Clamp(Mathf.RoundToInt(u * (_w - 1)), 0, _w - 1);
                gy = Mathf.Clamp(Mathf.RoundToInt(fromTop * (_h - 1)), 0, _h - 1); // top row = gy 0
            }

            // Eyedropper: pick up the colour already under the cursor instead of painting over it. Asked for
            // by name ("ein Kopierer für benutzte Farben") — once a drawing has a dozen shades in it, finding
            // the one you used for the left eye by eye in the palette is guesswork.
            if (_picking && left)
            {
                int picked = _grid[gy * _w + gx];
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
            int cell = gy * _w + gx;
            if (_grid[cell] == index) return;

            _grid[cell] = index;
            _tex.SetPixel(gx, _h - 1 - gy, DisplayColor(index));
            _tex.Apply();
        }

        private static Color DisplayColor(int index)
            => index == 0 ? FacePalette.EditorBackground : (Color)FacePalette.ColorOf(index);

        private void RenderAll()
        {
            for (int gy = 0; gy < _h; gy++)
            for (int gx = 0; gx < _w; gx++)
            {
                _tex.SetPixel(gx, _h - 1 - gy, DisplayColor(_grid[gy * _w + gx]));
            }

            _tex.Apply();
        }

        private void LoadFrom(string face)
        {
            var grid = DecodeGrid != null ? DecodeGrid(face) : FacePalette.Decode(face, _w * _h);
            if (grid == null || grid.Length != _grid.Length)
            {
                grid = new int[_grid.Length];
            }

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
            // Paint + body-paint hosts get a wider panel: a column sits right of the canvas (the design
            // library, or the stacked face-region tiles).
            bool wide = hasLibrary || RegionMode;
            float panelW = wide ? 950f : 700f;
            var (_, panel) = UiKit.AddModalOverlay(root, wide ? 485f : 610f, 60f, panelW, 960f);
            UiKit.AddText(panel, 24f, 18f, panelW - 48f, 30f, L(TitleKey), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Paint surface (point-filtered → crisp big pixels), always the full 512×512 — in region mode
            // it crops the shared texture to the active 32×32 face via uvRect, so every face paints at the
            // same 16 px cell size as the classic face editor.
            var canvasGo = new GameObject("FaceCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(panel, false);
            _canvasRt = UiKit.Place(canvasGo, 94f, 64f, 512f, 512f);
            _canvas = canvasGo.AddComponent<RawImage>();
            _canvas.texture = _tex;

            if (RegionMode)
            {
                _activeRegionLabel = UiKit.AddText(panel, 94f, 40f, 512f, 22f, string.Empty, 15,
                    UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                BuildRegionColumn(panel);
                SetActiveRegion(0);
            }

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
            UiKit.AddButton(panel, 260f, 832f, 180f, 56f, L("ui.face.clear"), ClearCanvas);
            UiKit.AddButton(panel, 456f, 832f, 220f, 56f, L("ui.menu.back"), Close);

            UiKit.AddText(panel, 24f, 904f, panelW - 48f, 24f, L(HintKey), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

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

        // ── region mode (body-paint hosts, #874) ─────────────────────────────────────────────────

        /// <summary>Texture-space UV rect of one 32×32 region (grid row 0 = top → high v).</summary>
        private Rect RegionUvRect(int region)
        {
            int col = region % _regionCols, row = region / _regionCols;
            return new Rect(
                col * (float)RegionCells / _w,
                (_h - (row + 1) * RegionCells) / (float)_h,
                (float)RegionCells / _w,
                (float)RegionCells / _h);
        }

        /// <summary>Human label of a region: the face name, prefixed with the limb ("Links · Vorne").</summary>
        private string RegionLabel(int region)
        {
            int col = region % _regionCols, row = region / _regionCols;
            string label = L(ColumnLabelKeys[Mathf.Min(col, ColumnLabelKeys.Length - 1)]);
            if (RowLabelKeys != null && _regionRows > 1)
            {
                label = L(RowLabelKeys[Mathf.Min(row, RowLabelKeys.Length - 1)]) + " · " + label;
            }

            return label;
        }

        /// <summary>Selects the face region the main canvas paints: crops the canvas onto it, names it in
        /// the header, and highlights its tile in the column.</summary>
        private void SetActiveRegion(int region)
        {
            _activeRegion = Mathf.Clamp(region, 0, _regionCols * _regionRows - 1);
            if (_canvas != null)
            {
                _canvas.uvRect = RegionUvRect(_activeRegion);
            }

            if (_activeRegionLabel != null)
            {
                _activeRegionLabel.text = RegionLabel(_activeRegion);
            }

            if (_regionFrames != null)
            {
                for (int i = 0; i < _regionFrames.Length; i++)
                {
                    if (_regionFrames[i] != null)
                    {
                        _regionFrames[i].color = i == _activeRegion ? UiKit.Cyan : UiKit.PanelFill;
                    }
                }
            }
        }

        /// <summary>
        /// The unfolded part as a column of live tiles right of the canvas — the faces STACKED under each
        /// other (one column per limb for arms/legs, headed Links/Rechts), each tile a labelled button that
        /// makes its face the active paint target. The tiles share the canvas texture (cropped via uvRect),
        /// so they repaint live while drawing — the whole-part overview the strip canvas used to give,
        /// without shrinking the paint surface.
        /// </summary>
        private void BuildRegionColumn(Transform panel)
        {
            const float tile = 112f, labelH = 16f, gap = 8f, colGap = 24f;
            float baseX = 660f;
            float baseY = _regionRows > 1 ? 88f : 64f; // leave room for the limb headers
            _regionFrames = new Image[_regionCols * _regionRows];

            for (int row = 0; row < _regionRows; row++)
            {
                float x = baseX + row * (tile + colGap);
                if (_regionRows > 1 && RowLabelKeys != null && row < RowLabelKeys.Length)
                {
                    UiKit.AddText(panel, x, 64f, tile, 20f, L(RowLabelKeys[row]), 14, UiKit.CyanDim,
                        TextAnchor.MiddleCenter, FontStyle.Bold);
                }

                for (int col = 0; col < _regionCols; col++)
                {
                    int region = row * _regionCols + col;
                    float y = baseY + col * (labelH + tile + gap);
                    UiKit.AddText(panel, x, y, tile, labelH, L(ColumnLabelKeys[Mathf.Min(col, ColumnLabelKeys.Length - 1)]),
                        12, UiKit.CyanDim, TextAnchor.MiddleCenter);

                    // Highlight frame behind the tile; the tile itself is a click-to-select RawImage button.
                    var frameGo = new GameObject("RegionFrame", typeof(RectTransform));
                    frameGo.transform.SetParent(panel, false);
                    UiKit.Place(frameGo, x - 3f, y + labelH - 3f, tile + 6f, tile + 6f);
                    var frame = frameGo.AddComponent<Image>();
                    frame.sprite = UiKit.SolidSprite;
                    frame.raycastTarget = false;
                    _regionFrames[region] = frame;

                    var tileGo = new GameObject("RegionTile", typeof(RectTransform));
                    tileGo.transform.SetParent(panel, false);
                    UiKit.Place(tileGo, x, y + labelH, tile, tile);
                    var img = tileGo.AddComponent<RawImage>();
                    img.texture = _tex;
                    img.uvRect = RegionUvRect(region);
                    var btn = tileGo.AddComponent<Button>();
                    btn.transition = Selectable.Transition.None;
                    btn.targetGraphic = img;
                    int captured = region;
                    btn.onClick.AddListener(() => SetActiveRegion(captured));
                }
            }
        }

        /// <summary>Clear = what you see: the whole canvas for the classic hosts, but only the ACTIVE face
        /// region in region mode — wiping all faces of a part because you wanted to redo one would hurt.</summary>
        private void ClearCanvas()
        {
            if (RegionMode)
            {
                int col = (_activeRegion % _regionCols) * RegionCells, row = (_activeRegion / _regionCols) * RegionCells;
                for (int gy = row; gy < row + RegionCells; gy++)
                {
                    Array.Clear(_grid, gy * _w + col, RegionCells);
                }
            }
            else
            {
                Array.Clear(_grid, 0, _grid.Length);
            }

            RenderAll();
        }

        private void Apply()
        {
            OnApply?.Invoke(EncodeGrid != null ? EncodeGrid(_grid) : FacePalette.Encode(_grid, _grid.Length));
            Close();
        }

        private void Close() => Destroy(gameObject);

        private string L(string key) => Localizer?.Invoke(key) ?? key;
    }
}
