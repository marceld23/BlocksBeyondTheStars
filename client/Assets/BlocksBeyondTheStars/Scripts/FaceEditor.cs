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
    /// tools, colour wheel, drag painting, Apply/Clear/Back) is identical in every host. Faces are 32×32 as
    /// of #840, the same as block designs. <b>Apply</b> hands the encoded grid to the host; the face hosts
    /// store/send it as the avatar face, the paint host sends it as a <c>PaintBlockIntent</c>.
    /// <para>
    /// Since #899 one editor can host SEVERAL <see cref="Subject"/>s (face, torso, arms, legs, helmet) as
    /// tabs, each with its own layout, payload codec and <b>base colour</b> — that is what turned nine
    /// Character-tab cards into a single "Appearance" screen. Hosts that only ever edit one thing (the block
    /// paint tool) set the flat fields instead and get exactly the old single-subject screen.
    /// </para>
    /// <para>
    /// Because all hosts share this component, a tool added here appears in the main-menu Avatar Designer,
    /// the in-game appearance screen and the block paint tool at once.
    /// </para>
    /// Modern uGUI, mirroring <see cref="MaterialEditor"/>.
    /// </summary>
    public sealed class FaceEditor : MonoBehaviour
    {
        /// <summary>One editable thing in the editor: its canvas layout, its payload codec, where an edit
        /// goes, and (for avatar parts) the base colour showing through unpainted pixels. A host with a
        /// single subject can leave <see cref="FaceEditor.Subjects"/> null and use the flat fields.</summary>
        public sealed class Subject
        {
            public string LabelKey;                 // tab label
            public string TitleKey;                 // panel title (falls back to LabelKey)
            public string HintKey = "ui.face.hint";
            public int GridW, GridH;                // 0 = square, GridSize a side
            public string Pixels = string.Empty;    // current payload (kept up to date on commit)
            public Func<string, int[]> Decode;      // null = the plain FacePalette grid codec
            public Func<int[], string> Encode;
            public string[] ColumnLabelKeys;        // region mode: one label per 32-cell column block
            public string[] RowLabelKeys;           // region mode: one label per 32-cell row block, or null
            public Action<string> OnApply;          // commit this subject's payload
            public Func<Color> GetBaseColor;        // colour behind transparent pixels (null = editor grey)
            public Action<Color> SetBaseColor;      // null = the base colour is shown but not editable
            public int PreviewPart = -2;            // live preview target: -1 = face, 0..3 = body part, -2 = none
        }

        /// <summary>Everything the live avatar preview needs, as the host currently has it stored. The editor
        /// overlays the subject being edited on top, so unapplied changes show up too.</summary>
        public sealed class AppearanceSnapshot
        {
            public Color Skin, Torso, Arms, Legs;
            public string Face = string.Empty;
            public string[] Paints = Array.Empty<string>();
        }

        // Host-supplied hooks so one editor serves the appearance screen, the Avatar Designer and the block-paint
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
        // All of this defaults off, so the classic square hosts are untouched.
        public int GridW;                       // grid cells wide (0 = square GridSize)
        public int GridH;                       // grid cells high (0 = square GridSize)
        public string TitleKey = "ui.face.title";
        public string HintKey = "ui.face.hint";
        public Func<string, int[]> DecodeGrid;  // payload → grid (length GridW×GridH)
        public Func<int[], string> EncodeGrid;  // grid → payload
        public string[] ColumnLabelKeys;        // one label per 32-cell column block (region face names)
        public string[] RowLabelKeys;           // one label per 32-cell row block (Links/Rechts), or null

        /// <summary>Colour shown behind transparent pixels for single-subject hosts — the truth about what
        /// "unpainted" will look like (paper white on a block). Subjects carry their own.</summary>
        public Color BaseColor = FacePalette.EditorBackground;

        /// <summary>Several editable parts as tabs (the merged appearance screen, #899). Null/empty = the
        /// classic single-subject editor built from the flat fields above.</summary>
        public List<Subject> Subjects;

        /// <summary>Host's current appearance, for the live preview beside the canvas. Null = no preview.</summary>
        public Func<AppearanceSnapshot> PreviewState;

        /// <summary>Fires after any committed change, so the host can refresh whatever else it shows.</summary>
        public Action OnChanged;

        private readonly List<Subject> _subjects = new();
        private int _active;                    // index into _subjects
        private int _size;
        private int _w, _h;
        private int[] _grid;
        private string _name = string.Empty;
        private int _brush = 1; // current palette index (0 = eraser/transparent)
        private bool _dirty;    // the active subject has unsaved edits

        // Region mode (body-paint subjects): the grid is a row-major arrangement of 32×32 face regions;
        // the main canvas crops the shared texture to the ACTIVE region via RawImage.uvRect, so tiles
        // and canvas both update live from the same SetPixel.
        private const int RegionCells = 32;
        private bool RegionMode => Current.ColumnLabelKeys != null && Current.ColumnLabelKeys.Length > 0;
        private int _regionCols, _regionRows;
        private int _activeRegion;
        private Text _activeRegionLabel;
        private Image[] _regionFrames; // tile highlight frames, indexed by region

        private Texture2D _tex;
        private RectTransform _canvasRt;
        private RawImage _canvas;

        private Canvas _ui;
        private Text _title, _hint;
        private RectTransform _subjectArea; // canvas + region tiles + region label (rebuilt per subject)
        private Image[] _tabFrames;
        private Image _activeSwatch;
        private readonly Image[] _swatches = new Image[FacePalette.Colors.Length]; // index 0 reused as eraser
        private RectTransform _libList; // library column entries (rebuilt after a save)

        // Tools. Painting is the default; fill and the eyedropper are armed modes with a highlighted button,
        // and both can also be reached by modifier so they feel like a paint program (#899).
        private bool _picking;          // eyedropper armed: the next canvas click takes a colour, not paints one
        private bool _filling;          // fill armed: the next canvas click floods an area
        private Image _pickButton, _fillButton;
        private int[] _undo;            // single-level undo snapshot (doubles as redo — Undo swaps)
        private bool _hasUndo;
        private bool _stroking;         // a drag is in progress (so the undo snapshot is taken once per stroke)

        private RectTransform _wheelRt;  // hue/saturation ring
        private RectTransform _wheelDot; // the draggable marker on it
        private RawImage _wheelImg;
        private RectTransform _valueRt;  // brightness column right of the ring
        private RectTransform _valueDot;
        private Text _wheelTargetLabel;
        private float _wheelHue, _wheelSat, _wheelValue = 1f;
        private bool _wheelPaintsBase;   // the wheel edits the base colour instead of the brush

        private Image _baseSwatch;
        private AvatarPreviewRig _preview;

        private Subject Current => _subjects.Count > 0 ? _subjects[_active] : null;

        private void Start()
        {
            BuildSubjects();
            _size = Mathf.Clamp(GridSize, 8, 64);
            _name = InitialName ?? string.Empty;
            LoadSubject(0);
            BuildUi();
        }

        /// <summary>Host fields → the subject list. A host that set <see cref="Subjects"/> wins; everyone else
        /// gets exactly one subject wrapping the flat fields, which is the pre-#899 editor.</summary>
        private void BuildSubjects()
        {
            if (Subjects != null && Subjects.Count > 0)
            {
                _subjects.AddRange(Subjects);
                return;
            }

            _subjects.Add(new Subject
            {
                LabelKey = TitleKey,
                TitleKey = TitleKey,
                HintKey = HintKey,
                GridW = GridW,
                GridH = GridH,
                Pixels = InitialFace ?? string.Empty,
                Decode = DecodeGrid,
                Encode = EncodeGrid,
                ColumnLabelKeys = ColumnLabelKeys,
                RowLabelKeys = RowLabelKeys,
                OnApply = OnApply,
                GetBaseColor = () => BaseColor,
            });
        }

        /// <summary>Points the editor at a subject: grid, display texture and region geometry. The UI around
        /// it is rebuilt separately (<see cref="RebuildSubjectArea"/>), so switching tabs keeps the panel.</summary>
        private void LoadSubject(int index)
        {
            _active = Mathf.Clamp(index, 0, _subjects.Count - 1);
            var subject = Current;
            _w = subject.GridW > 0 ? Mathf.Clamp(subject.GridW, 8, 256) : _size;
            _h = subject.GridH > 0 ? Mathf.Clamp(subject.GridH, 8, 256) : _size;
            _regionCols = Mathf.Max(1, _w / RegionCells);
            _regionRows = Mathf.Max(1, _h / RegionCells);
            _activeRegion = 0;
            _grid = new int[_w * _h];
            _hasUndo = false;
            _undo = null;
            _dirty = false;

            if (_tex != null)
            {
                Destroy(_tex);
            }

            _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            LoadFrom(subject.Pixels);
        }

        private void OnDestroy()
        {
            if (_ui != null) Destroy(_ui.gameObject);
            if (_tex != null) Destroy(_tex);
            if (_preview != null) Destroy(_preview.gameObject);
            OnClosed?.Invoke();
        }

        // ── live painting ────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_canvasRt == null) return;

            UpdateColorWheel();

            bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
            bool leftDown = Input.GetMouseButtonDown(0), rightDown = Input.GetMouseButtonDown(1);
            bool middleDown = Input.GetMouseButtonDown(2);
            if (!left && !right && !middleDown)
            {
                if (_stroking)
                {
                    _stroking = false;
                    RefreshPreview(); // a stroke ended — show it on the figure (too costly per pixel)
                }

                return;
            }

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
            // the one you used for the left eye by eye in the palette is guesswork. Besides the armed button
            // it answers to the two gestures every paint program uses: Alt+click and the middle button (#899),
            // so it is there when a hand reaches for it without a trip to the toolbar.
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (middleDown || (leftDown && (altHeld || _picking)))
            {
                int picked = _grid[gy * _w + gx];
                SetBrush(picked, _swatches[picked]);
                if (_picking && !altHeld && !middleDown)
                {
                    SetPicking(false); // the ARMED tool is one-shot, like every paint program: pick, carry on
                }

                return;
            }

            if (_filling)
            {
                if (leftDown || rightDown)
                {
                    // Shift = replace every pixel of that colour in the region, not just the connected blob —
                    // one gesture to recolour an outline that a fill would have to chase around corners.
                    Fill(gx, gy, rightDown ? 0 : _brush,
                        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
                }

                return; // armed fill never smears paint while the button stays down
            }

            if (leftDown || rightDown)
            {
                TakeUndoSnapshot(); // one snapshot per stroke, not per pixel
                _stroking = true;
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
            _dirty = true;
            _tex.SetPixel(gx, _h - 1 - gy, DisplayColor(index, gx, gy));
            _tex.Apply();
        }

        /// <summary>
        /// Floods the area around a cell with the brush — the tool a 32×32 canvas most obviously wants, since
        /// colouring a helmet side pixel by pixel is a thousand clicks (#899).
        /// <para>
        /// ⚠ The fill is clamped to the ACTIVE region: a body-paint grid holds several box faces side by side,
        /// and paint running from the torso front onto its back (they touch in the grid, not on the body)
        /// would be a genuinely nasty surprise. In the square hosts the region IS the whole canvas.
        /// </para>
        /// </summary>
        private void Fill(int gx, int gy, int index, bool replaceAll)
        {
            int target = _grid[gy * _w + gx];
            if (target == index)
            {
                return;
            }

            int x0 = 0, y0 = 0, x1 = _w, y1 = _h;
            if (RegionMode)
            {
                x0 = (_activeRegion % _regionCols) * RegionCells;
                y0 = (_activeRegion / _regionCols) * RegionCells;
                x1 = x0 + RegionCells;
                y1 = y0 + RegionCells;
            }

            TakeUndoSnapshot();
            if (replaceAll)
            {
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        if (_grid[y * _w + x] == target)
                        {
                            _grid[y * _w + x] = index;
                        }
                    }
                }
            }
            else
            {
                // Iterative 4-neighbour flood (a recursive one would blow the stack on a full 32×32 region).
                var stack = new Stack<int>();
                stack.Push(gy * _w + gx);
                _grid[gy * _w + gx] = index;
                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    int cx = cell % _w, cy = cell / _w;
                    TryFlood(cx - 1, cy, x0, y0, x1, y1, target, index, stack);
                    TryFlood(cx + 1, cy, x0, y0, x1, y1, target, index, stack);
                    TryFlood(cx, cy - 1, x0, y0, x1, y1, target, index, stack);
                    TryFlood(cx, cy + 1, x0, y0, x1, y1, target, index, stack);
                }
            }

            _dirty = true;
            RenderAll();
            RefreshPreview();
        }

        private void TryFlood(int x, int y, int x0, int y0, int x1, int y1, int target, int index, Stack<int> stack)
        {
            if (x < x0 || y < y0 || x >= x1 || y >= y1)
            {
                return;
            }

            int cell = y * _w + x;
            if (_grid[cell] != target)
            {
                return;
            }

            _grid[cell] = index;
            stack.Push(cell);
        }

        /// <summary>Remembers the canvas before a destructive step. One level, by design — but because
        /// <see cref="Undo"/> SWAPS the snapshot in, pressing it twice puts the change back, which is what a
        /// child actually does with an undo button.</summary>
        private void TakeUndoSnapshot()
        {
            _undo = (int[])_grid.Clone();
            _hasUndo = true;
        }

        private void Undo()
        {
            if (!_hasUndo || _undo == null || _undo.Length != _grid.Length)
            {
                return;
            }

            (_undo, _grid) = (_grid, _undo);
            _dirty = true;
            RenderAll();
            RefreshPreview();
        }

        /// <summary>The colour a grid cell shows in the editor. Transparent pixels are drawn in the subject's
        /// BASE colour — skin for the face, the part tint for body paint, the paper canvas for a block design
        /// — because that is what they will actually look like; the canvas used to show a flat dark slate and
        /// so lied about every unpainted pixel (#899). A faint checker keeps "empty" readable as empty.</summary>
        private Color DisplayColor(int index, int gx, int gy)
        {
            if (index != 0)
            {
                return FacePalette.ColorOf(index);
            }

            Color b = Current?.GetBaseColor?.Invoke() ?? FacePalette.EditorBackground;
            bool dark = ((gx >> 2) + (gy >> 2)) % 2 == 0;
            return dark ? b : new Color(b.r * 0.88f, b.g * 0.88f, b.b * 0.88f, 1f);
        }

        private void RenderAll()
        {
            for (int gy = 0; gy < _h; gy++)
            for (int gx = 0; gx < _w; gx++)
            {
                _tex.SetPixel(gx, _h - 1 - gy, DisplayColor(_grid[gy * _w + gx], gx, gy));
            }

            _tex.Apply();
        }

        private void LoadFrom(string face)
        {
            var decode = Current?.Decode;
            var grid = decode != null ? decode(face) : FacePalette.Decode(face, _w * _h);
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
            bool multi = _subjects.Count > 1;
            bool anyRegion = _subjects.Exists(s => s.ColumnLabelKeys != null && s.ColumnLabelKeys.Length > 0);
            bool wide = multi || anyRegion; // the appearance screen: tabs, region tiles, base colour, preview

            _ui = UiKit.CreateCanvas("Face Editor UI");
            _ui.sortingOrder = 60; // above the in-game menu (CraftingTechShipUI is sortingOrder 50)
            var root = _ui.transform;

            // Shared scrim + opaque panel (#588). The old backdrop was an AddPanel, whose raycastTarget is
            // false, so it never actually blocked clicks reaching the menu behind — AddModalDim does.
            float panelW = wide ? 1240f : (hasLibrary ? 950f : 700f);
            var (_, panel) = UiKit.AddModalOverlay(root, (1920f - panelW) / 2f, 60f, panelW, 960f);
            _title = UiKit.AddText(panel, 24f, 18f, panelW - 48f, 30f, L(Current.TitleKey ?? Current.LabelKey), 22,
                UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Part tabs (#899): face / torso / arms / legs / helmet in ONE screen, instead of five menu cards
            // that each tore the editor down and built it again.
            if (multi)
            {
                BuildTabs(panel);
            }

            float canvasX = wide ? 24f : 94f;
            float canvasY = multi ? 100f : 64f;
            var areaGo = new GameObject("SubjectArea", typeof(RectTransform));
            areaGo.transform.SetParent(panel, false);
            _subjectArea = UiKit.Place(areaGo, 0f, 0f, panelW, 900f);
            BuildSubjectArea(canvasX, canvasY);

            // Palette: colours 1..N then an eraser (index 0). 32 colours (#899) at 12 per row — the widened
            // palette is mostly shading partners, so keeping each hue's tones near each other matters more
            // than the exact row length.
            float paletteLabelY = canvasY + 528f;
            UiKit.AddText(panel, 24f, paletteLabelY, 400f, 24f, L("ui.face.palette"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            float px = 24f, py = paletteLabelY + 32f;
            const float swatch = 32f, pitch = 36f;
            int col = 0;
            for (int i = 1; i < FacePalette.Colors.Length; i++)
            {
                int idx = i;
                _swatches[i] = MakeSwatch(panel, px + col * pitch, py, swatch, FacePalette.ColorOf(i), () => SetBrush(idx, _swatches[idx]));
                if (++col >= 12) { col = 0; py += pitch; }
            }

            // Eraser swatch (transparent → shown as the editor background colour, labelled "E").
            var eraser = MakeSwatch(panel, px + col * pitch, py, swatch, FacePalette.EditorBackground, () => SetBrush(0, _swatches[0]));
            _swatches[0] = eraser;
            UiKit.AddText(eraser.transform, 0f, 0f, swatch, swatch, "E", 15, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);

            // Tool row: fill, eyedropper, undo — grouped so they read as a toolbox rather than as loose buttons.
            // 200 wide because these carry the longest labels in the panel ("Farbe aufnehmen" in German, and
            // longer still in Polish/Turkish): a label only auto-shrinks so far before it stops being readable
            // at arm's length, so give it the room instead (#918).
            float toolsY = paletteLabelY + 32f + 3f * pitch + 12f;
            // The row stops at x=580: that is where the region-tile column starts, and the helmet's fifth tile
            // reaches down to this row's top edge.
            _fillButton = UiKit.AddButton(panel, 24f, toolsY, 200f, 44f, L("ui.face.fill"), () => SetFilling(!_filling)).image;
            _pickButton = UiKit.AddButton(panel, 232f, toolsY, 200f, 44f, L("ui.face.pick"), () => SetPicking(!_picking)).image;
            UiKit.AddButton(panel, 440f, toolsY, 140f, 44f, L("ui.face.undo"), Undo);

            BuildColorWheel(panel, wide ? 900f : 470f, wide ? 300f : paletteLabelY + 8f);
            if (wide)
            {
                BuildBaseColorPicker(panel, 900f, 100f);
                BuildPreview(panel, 900f, 500f);
            }

            // Buttons.
            float buttonsY = toolsY + 58f;
            UiKit.AddButton(panel, 24f, buttonsY, 220f, 56f, L("ui.face.apply"), Apply);
            UiKit.AddButton(panel, 260f, buttonsY, 180f, 56f, L("ui.face.clear"), ClearCanvas);
            UiKit.AddButton(panel, 456f, buttonsY, 220f, 56f, L("ui.menu.back"), Close);

            _hint = UiKit.AddText(panel, 24f, buttonsY + 66f, panelW - 48f, 24f, L(Current.HintKey), 14, UiKit.CyanDim, TextAnchor.MiddleLeft);

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
                            TakeUndoSnapshot();
                            LoadFrom(imported.Pixels);
                            _dirty = true;
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
            RefreshPreview();
        }

        /// <summary>The part tabs across the top of the appearance screen. Switching commits what is on the
        /// canvas first — five parts in one screen means "Apply" can no longer be the only commit point.</summary>
        private void BuildTabs(Transform panel)
        {
            _tabFrames = new Image[_subjects.Count];
            const float tabW = 168f, gap = 8f;
            for (int i = 0; i < _subjects.Count; i++)
            {
                int which = i;
                var btn = UiKit.AddButton(panel, 24f + i * (tabW + gap), 50f, tabW, 36f, L(_subjects[i].LabelKey),
                    () => SwitchSubject(which));
                _tabFrames[i] = btn.image;
            }

            HighlightTabs();
        }

        private void HighlightTabs()
        {
            if (_tabFrames == null) return;
            for (int i = 0; i < _tabFrames.Length; i++)
            {
                if (_tabFrames[i] != null)
                {
                    _tabFrames[i].color = i == _active ? UiKit.Cyan : UiKit.PanelFill;
                }
            }
        }

        /// <summary>Commits the current canvas and points the editor at another part, keeping the panel (the
        /// pre-#899 flow destroyed the whole editor per part).</summary>
        private void SwitchSubject(int index)
        {
            if (index == _active)
            {
                return;
            }

            CommitActive();
            float canvasX = 24f, canvasY = 100f;
            LoadSubject(index);
            BuildSubjectArea(canvasX, canvasY);
            _title.text = L(Current.TitleKey ?? Current.LabelKey);
            _hint.text = L(Current.HintKey);
            HighlightTabs();
            UpdateBaseSwatch();
            RefreshPreview();
        }

        /// <summary>(Re)builds the canvas + region tiles for the active subject.</summary>
        private void BuildSubjectArea(float canvasX, float canvasY)
        {
            for (int i = _subjectArea.childCount - 1; i >= 0; i--)
            {
                Destroy(_subjectArea.GetChild(i).gameObject);
            }

            _regionFrames = null;
            _activeRegionLabel = null;

            // Paint surface (point-filtered → crisp big pixels), always the full 512×512 — in region mode
            // it crops the shared texture to the active 32×32 face via uvRect, so every face paints at the
            // same 16 px cell size as the classic face editor.
            var canvasGo = new GameObject("FaceCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(_subjectArea, false);
            _canvasRt = UiKit.Place(canvasGo, canvasX, canvasY, 512f, 512f);
            _canvas = canvasGo.AddComponent<RawImage>();
            _canvas.texture = _tex;

            if (RegionMode)
            {
                _activeRegionLabel = UiKit.AddText(_subjectArea, canvasX, canvasY + 516f, 512f, 22f, string.Empty, 15,
                    UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                BuildRegionColumn(_subjectArea, canvasY);
                SetActiveRegion(0);
            }
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
                UiKit.AddButton(_libList, 0f, y, 226f, 42f, entries[i].Name, () =>
                {
                    TakeUndoSnapshot();
                    LoadFrom(pixels);
                    _dirty = true;
                });
                y += 50f;
            }
        }

        /// <summary>
        /// A hue/brightness ring you pick a colour on by moving a point around it — the "Kreis" the player
        /// asked for, rather than hunting through a strip of squares, plus the brightness column beside it
        /// (#899: the ring alone was fixed at full brightness, so half the palette's shading tones — and every
        /// dark base colour — could not be reached through it at all).
        /// <para>
        /// For the BRUSH it snaps to the nearest palette entry, because a painting stores one symbol per pixel
        /// and the format holds 32 colours, not free RGB. For a BASE colour it does not snap: those travel as
        /// plain RGB, so any colour a child can find on the ring is a legal skin or suit tone.
        /// </para>
        /// </summary>
        private void BuildColorWheel(Transform panel, float wheelX, float wheelY)
        {
            const int texSize = 128;
            const float wheelPx = 170f;

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
            _wheelImg = go.AddComponent<RawImage>();
            _wheelImg.texture = tex;
            _wheelTargetLabel = UiKit.AddText(panel, wheelX, wheelY + wheelPx + 4f, wheelPx + 60f, 22f,
                L("ui.face.wheel"), 13, UiKit.CyanDim, TextAnchor.MiddleCenter);

            var dot = new GameObject("WheelDot", typeof(RectTransform));
            dot.transform.SetParent(go.transform, false);
            _wheelDot = UiKit.Place(dot, wheelPx / 2f - 7f, wheelPx / 2f - 7f, 14f, 14f);
            var dotImg = dot.AddComponent<Image>();
            dotImg.sprite = UiKit.SolidSprite;
            dotImg.color = Color.white;
            dotImg.raycastTarget = false;

            // Brightness column: black at the bottom, full colour at the top.
            var bar = new Texture2D(1, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;
                bar.SetPixel(0, y, new Color(v, v, v, 1f));
            }

            bar.Apply();

            var valueGo = new GameObject("ColorValue", typeof(RectTransform));
            valueGo.transform.SetParent(panel, false);
            _valueRt = UiKit.Place(valueGo, wheelX + wheelPx + 12f, wheelY, 26f, wheelPx);
            valueGo.AddComponent<RawImage>().texture = bar;

            var vDot = new GameObject("ValueDot", typeof(RectTransform));
            vDot.transform.SetParent(valueGo.transform, false);
            _valueDot = UiKit.Place(vDot, -3f, -4f, 32f, 8f);
            var vImg = vDot.AddComponent<Image>();
            vImg.sprite = UiKit.SolidSprite;
            vImg.color = UiKit.Cyan;
            vImg.raycastTarget = false;
        }

        /// <summary>Drag handling for the ring and the brightness column: the marker follows the cursor and
        /// the picked colour goes to whichever target the wheel currently drives (brush or base colour).</summary>
        private void UpdateColorWheel()
        {
            if (_wheelRt == null || !Input.GetMouseButton(0))
            {
                return;
            }

            if (_valueRt != null
                && RectTransformUtility.RectangleContainsScreenPoint(_valueRt, Input.mousePosition, null)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(_valueRt, Input.mousePosition, null, out var vp))
            {
                float bh = _valueRt.rect.height;
                _wheelValue = Mathf.Clamp01(1f + vp.y / bh); // Place() pivots top-left: local y runs 0 → -h
                if (_valueDot != null)
                {
                    _valueDot.anchoredPosition = new Vector2(-3f, -(1f - _wheelValue) * bh - 4f);
                }

                ApplyWheelColor();
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

            _wheelHue = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 1f) % 1f;
            _wheelSat = Mathf.Clamp01(r);
            ApplyWheelColor();

            if (_wheelDot != null)
            {
                _wheelDot.anchoredPosition = new Vector2(cx + dx * cx - 7f, cy + dy * (h / 2f) + 7f);
            }
        }

        private void ApplyWheelColor()
        {
            var picked = Color.HSVToRGB(_wheelHue, _wheelSat, _wheelValue);
            if (_wheelImg != null)
            {
                _wheelImg.color = new Color(_wheelValue, _wheelValue, _wheelValue, 1f); // the ring dims with it
            }

            if (_wheelPaintsBase && Current?.SetBaseColor != null)
            {
                SetBaseColor(picked);
                return;
            }

            int index = NearestPaletteIndex(picked);
            SetBrush(index, _swatches[index]);
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

        // ── base colour (avatar subjects) ────────────────────────────────────────────────────────

        /// <summary>
        /// The part's base colour, right where it is being painted (#899). It used to live on separate menu
        /// cards, cycled blindly with ←/→ through ten hard-coded colours — while the thing it tints was two
        /// menu levels away. Here it is a swatch grid plus the colour wheel for anything outside it: base
        /// colours travel as plain RGB, so nothing about the format limits the choice.
        /// </summary>
        private void BuildBaseColorPicker(Transform panel, float x, float y)
        {
            UiKit.AddText(panel, x, y, 220f, 24f, L("ui.paint.base_color"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);

            var swGo = new GameObject("BaseSwatch", typeof(RectTransform));
            swGo.transform.SetParent(panel, false);
            UiKit.Place(swGo, x + 230f, y - 6f, 48f, 36f);
            _baseSwatch = swGo.AddComponent<Image>();
            _baseSwatch.sprite = UiKit.SolidSprite;
            _baseSwatch.raycastTarget = false;

            float gy = y + 40f;
            const float cell = 26f, pitch = 30f;
            for (int i = 0; i < AppearancePalette.Colors.Length; i++)
            {
                Color c = AppearancePalette.Colors[i];
                MakeSwatch(panel, x + (i % 10) * pitch, gy + (i / 10) * pitch, cell, c, () => SetBaseColor(c));
            }

            // The wheel drives the brush until the player reaches for a base colour, and switches back the
            // moment they pick a palette swatch again — the label above it always says which.
            UpdateBaseSwatch();
        }

        private void SetBaseColor(Color c)
        {
            if (Current?.SetBaseColor == null)
            {
                return;
            }

            _wheelPaintsBase = true;
            Current.SetBaseColor(c);
            UpdateBaseSwatch();
            RenderAll();   // transparent pixels show the base colour, so the whole canvas re-tints
            RefreshPreview();
            OnChanged?.Invoke();
        }

        private void UpdateBaseSwatch()
        {
            if (_baseSwatch != null)
            {
                _baseSwatch.color = Current?.GetBaseColor?.Invoke() ?? FacePalette.EditorBackground;
            }

            if (_wheelTargetLabel != null)
            {
                _wheelTargetLabel.text = _wheelPaintsBase && Current?.SetBaseColor != null
                    ? L("ui.face.wheel") + " → " + L("ui.paint.base_color")
                    : L("ui.face.wheel");
            }
        }

        // ── live preview ─────────────────────────────────────────────────────────────────────────

        /// <summary>The rotating figure beside the canvas: without it a painted back or an arm's inner face is
        /// invisible until the editor is closed.</summary>
        private void BuildPreview(Transform panel, float x, float y)
        {
            if (PreviewState == null)
            {
                return;
            }

            var state = PreviewState();
            var go = new GameObject("AppearancePreview");
            go.transform.SetParent(transform, false);
            _preview = go.AddComponent<AvatarPreviewRig>();
            // Slot 1: the Character tab keeps its own rig alive behind this screen, and two rigs sharing a
            // parking spot would each film both figures.
            _preview.EnsureBuilt(state.Skin, state.Torso, state.Arms, state.Legs, slot: 1);
            _preview.FullTurntable = true; // a painted back has to come round to be judged
            _preview.SetActive(true);

            var imgGo = new GameObject("PreviewImage", typeof(RectTransform));
            imgGo.transform.SetParent(panel, false);
            UiKit.Place(imgGo, x, y, 267f, 400f);
            var img = imgGo.AddComponent<RawImage>();
            img.texture = _preview.Texture;
            img.raycastTarget = false;
        }

        /// <summary>Pushes the host's stored appearance plus the UNCOMMITTED canvas onto the preview figure.
        /// Called on stroke end / fill / undo / colour change — not per pixel, since each call re-bakes the
        /// part's atlas and meshes.</summary>
        private void RefreshPreview()
        {
            if (_preview == null || PreviewState == null)
            {
                return;
            }

            var state = PreviewState();
            _preview.SetColors(state.Skin, state.Torso, state.Arms, state.Legs);
            _preview.SetFace(state.Face ?? string.Empty);
            for (int part = 0; part < state.Paints.Length; part++)
            {
                _preview.SetBodyPaint(part, state.Paints[part] ?? string.Empty);
            }

            int target = Current?.PreviewPart ?? -2;
            if (target == -2)
            {
                return;
            }

            string live = Encode();
            if (target == -1)
            {
                _preview.SetFace(live);
            }
            else
            {
                _preview.SetBodyPaint(target, live);
            }
        }

        // Fill and the eyedropper are mutually exclusive armed modes — arming one disarms the other, so the
        // highlighted button always tells the truth about what the next click will do.
        private void SetPicking(bool on)
        {
            _picking = on;
            if (_pickButton != null)
            {
                _pickButton.color = on ? UiKit.Cyan : UiKit.PanelFill;
            }

            if (on)
            {
                SetFilling(false);
            }
        }

        private void SetFilling(bool on)
        {
            _filling = on;
            if (_fillButton != null)
            {
                _fillButton.color = on ? UiKit.Cyan : UiKit.PanelFill;
            }

            if (on)
            {
                SetPicking(false);
            }
        }

        private Image MakeSwatch(Transform parent, float x, float y, float size, Color color, Action onClick)
        {
            var go = new GameObject("Swatch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, size, size);
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
            _wheelPaintsBase = false; // picking paint puts the wheel back on the brush
            UpdateBaseSwatch();
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one;
            _activeSwatch = swatch;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one * 1.18f;
        }

        // ── region mode (body-paint subjects, #874) ──────────────────────────────────────────────

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
            var keys = Current.ColumnLabelKeys;
            int col = region % _regionCols, row = region / _regionCols;
            string label = L(keys[Mathf.Min(col, keys.Length - 1)]);
            var rows = Current.RowLabelKeys;
            if (rows != null && _regionRows > 1)
            {
                label = L(rows[Mathf.Min(row, rows.Length - 1)]) + " · " + label;
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
        private void BuildRegionColumn(Transform panel, float canvasY)
        {
            const float tile = 112f, labelH = 16f, gap = 8f, colGap = 24f;
            const float baseX = 580f;
            float baseY = canvasY + (_regionRows > 1 ? 24f : 0f); // leave room for the limb headers
            _regionFrames = new Image[_regionCols * _regionRows];

            for (int row = 0; row < _regionRows; row++)
            {
                float x = baseX + row * (tile + colGap);
                var rowKeys = Current.RowLabelKeys;
                if (_regionRows > 1 && rowKeys != null && row < rowKeys.Length)
                {
                    UiKit.AddText(panel, x, canvasY, tile, 20f, L(rowKeys[row]), 14, UiKit.CyanDim,
                        TextAnchor.MiddleCenter, FontStyle.Bold);
                }

                for (int col = 0; col < _regionCols; col++)
                {
                    int region = row * _regionCols + col;
                    float y = baseY + col * (labelH + tile + gap);
                    var keys = Current.ColumnLabelKeys;
                    UiKit.AddText(panel, x, y, tile, labelH, L(keys[Mathf.Min(col, keys.Length - 1)]),
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
            TakeUndoSnapshot();
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

            _dirty = true;
            RenderAll();
            RefreshPreview();
        }

        private string Encode()
        {
            var encode = Current?.Encode;
            return encode != null ? encode(_grid) : FacePalette.Encode(_grid, _grid.Length);
        }

        /// <summary>Hands the active subject's canvas to its host. Called on Apply and whenever the player
        /// switches to another part, since one screen holding five paintings can't wait for a single OK.</summary>
        private void CommitActive(bool force = false)
        {
            if (!force && !_dirty)
            {
                return;
            }

            string pixels = Encode();
            Current.Pixels = pixels;
            _dirty = false;
            Current.OnApply?.Invoke(pixels);
            OnChanged?.Invoke();
        }

        private void Apply()
        {
            CommitActive(force: true);
            Close();
        }

        private void Close() => Destroy(gameObject);

        private string L(string key) => Localizer?.Invoke(key) ?? key;
    }
}
