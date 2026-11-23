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
    public sealed partial class FaceEditor : MonoBehaviour
    {
        /// <summary>One editable thing in the editor: its canvas layout, its payload codec, where an edit
        /// goes, and (for avatar parts) the base colour showing through unpainted pixels. A host with a
        /// single subject can leave <see cref="FaceEditor.Subjects"/> null and use the flat fields.</summary>
        public sealed class Subject
        {
            public string LabelKey;                 // tab label
            public string TitleKey;                 // the single-subject screen's title (multi-subject: the tab says it)
            public string ColorLabelKey;            // its base colour's row on the Figure tab (null: no row of its own)
            public string AboutKey;                 // a note on this subject, shown in the status bar when its tab opens
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
            public Func<string> Fetch;              // re-reads the payload from the host (see ReloadSubjects)
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

        // Outfit shelf: whole saved looks (four colours + face + all four paintings), not single canvases.
        // The HOST owns them, because the two avatar hosts mean different things by "the look you are
        // wearing" — the designer's scratch values in the main menu, the live figure in the game — and only
        // the in-game host has a server to tell. Null = no outfit column (the block-paint tool has none).
        // Each hook returns the status line to show under the column, already localized by the host.
        public Func<List<string>> OutfitNames;
        public Func<string, string> OnSaveOutfit;        // name → status
        public Func<int, string> OnWearOutfit;           // index → status
        public Func<int, string> OnDeleteOutfit;         // index → status
        public Func<int, string, string> OnRenameOutfit; // index, name → status

        // Body-paint hosts (#874): a NON-square grid holding a part's unfolded 32×32 face regions, and
        // custom payload codecs (the wire format is concatenated face chunks, not the grid's row-major
        // order). The editor paints ONE region at a time at full canvas size (16 px cells, same as the
        // face) and shows all regions STACKED as live click-to-select tiles beside the canvas — a whole
        // strip squeezed onto one canvas gave 4 px cells, far too small to draw on (playtest 2026-08-09).
        // All of this defaults off, so the classic square hosts are untouched.
        public int GridW;                       // grid cells wide (0 = square GridSize)
        public int GridH;                       // grid cells high (0 = square GridSize)
        public string TitleKey = "ui.face.title";
        public string AboutKey;                 // single-subject hosts: a note shown in the status bar on opening
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

        // Gamepad (#1198): the paint canvas has no pointer, so the pad drives a CELL cursor over it.
        // false = the tool panel owns the sticks (UiNav), true = the canvas does. Panel first, so a pad
        // player picks a colour before drawing.
        // The mode swap, the cursor walk and the cursor outline live in the shared PadCanvasFocus (#1954).
        private PadCanvasFocus _padFocus;
        private int _regionCols, _regionRows;
        private int _activeRegion;
        private Text _activeRegionLabel;
        private Image[] _regionFrames; // tile highlight frames, indexed by region

        private Texture2D _tex;
        private RectTransform _canvasRt;
        private RawImage _canvas;

        private Canvas _ui;
        private RectTransform _subjectArea; // canvas + region tiles + region label (rebuilt per subject)
        private Image _activeSwatch;
        private readonly Image[] _swatches = new Image[FacePalette.Colors.Length]; // index 0 (transparent) has none: the eraser is a tool

        // Tools (#3693: one segmented bar — pen, fill, pick colour, eraser). Painting is the default; fill and the
        // eyedropper are armed modes, and both can also be reached by modifier so they feel like a paint program (#899).
        private bool _picking;          // eyedropper armed: the next canvas click takes a colour, not paints one
        private bool _filling;          // fill armed: the next canvas click floods an area
        private bool _stroking;         // a drag is in progress (so the undo snapshot is taken once per stroke)

        // Undo/redo. It used to be ONE snapshot that Undo swapped in and out — a fine trick for the last
        // brush stroke and useless for the five before it. The stack itself lives in PixelEditHistory,
        // which also knows that a colour-wheel drag is one step and that the history spans the part tabs.
        // The shell's Undo / Redo buttons and Ctrl+Z / Ctrl+Y drive it (FaceEditor.Document.cs).
        private PixelEditHistory _history = new PixelEditHistory(); // swapped per mode (FaceEditor.HudMode.cs, #3318)
        private byte[] _pendingBefore;  // grid before the edit in progress (BeginGridEdit → CommitGridEdit)
        private int _pendingSubject = -1;

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

        private void Start() => Begin();

        /// <summary>Builds the editor now instead of on its first frame — the Avatar Designer opens it inside its own Start
        /// and must be able to talk to it at once (<see cref="AvatarEditor.CaptureOpenAppearance"/>). Idempotent: Start
        /// does nothing after an early Begin.</summary>
        internal void Begin()
        {
            if (_begun)
            {
                return;
            }

            _begun = true;
            BuildSubjects();
            _charCount = _subjects.Count;
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
                AboutKey = AboutKey,
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
        /// it is rebuilt separately (<see cref="BuildSubjectArea"/>), so switching tabs keeps the panel.</summary>
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
            _pendingBefore = null; // an edit half-open on the old canvas can never be closed on this one
            _pendingSubject = -1;
            _history.EndColorRun();
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
            // #3319: never paint through the submit dialog — nor through the shell's questions or behind the Figure tab.
            if (_canvasRt == null || _figureShown || CreationSubmitDialog.IsAnyOpen || (_shell != null && _shell.IsDialogOpen)) return;

            if (!Input.GetMouseButton(0))
            {
                _history.EndColorRun(); // drag over: the next colour change starts its own history step
            }

            if (!_stroking) UpdateColorWheel(); // #3179: a stroke running off the canvas onto the ring paints on, it does not re-pick

            // Which device is painting decides where the "cursor" is: the mouse pointer, or the pad's cell
            // cursor (#1198). Everything after the split — eyedropper, fill, stroke — is shared, so a tool
            // added here keeps working on both.
            bool pad = InputMap.ActiveDevice == InputDeviceKind.Gamepad;
            // Before the idle bail-out below, or the cursor could only move while painting.
            bool padCanvas = UpdatePadFocus(pad);
            if (pad && InputMap.PadDown(PadButton.Rb))
            {
                EndStroke();
                _shell?.Undo(); // RB is free while this editor is up: the tab screen behind stands its shoulders down
            }

            bool left, right, leftDown, rightDown, middleDown, altHeld, fillAll;
            int gx, gy, cellX, cellY; // the cell of the VISIBLE canvas, and the grid cell it is

            if (padCanvas)
            {
                left = InputMap.PadHeld(PadButton.A);
                right = InputMap.PadHeld(PadButton.X);
                leftDown = InputMap.PadDown(PadButton.A);
                rightDown = InputMap.PadDown(PadButton.X);
                middleDown = InputMap.PadDown(PadButton.Y); // the eyedropper gets its own button on a pad
                altHeld = false;
                fillAll = InputMap.PadHeld(PadButton.Lb); // LB stands in for the fill tool's Shift modifier
                if (!left && !right && !middleDown)
                {
                    EndStroke();
                    return;
                }

                cellX = _padFocus.CellX;
                cellY = _padFocus.CellY;
            }
            else
            {
                left = Input.GetMouseButton(0);
                right = Input.GetMouseButton(1);
                leftDown = Input.GetMouseButtonDown(0);
                rightDown = Input.GetMouseButtonDown(1);
                middleDown = Input.GetMouseButtonDown(2);
                if (!left && !right && !middleDown)
                {
                    EndStroke();
                    return;
                }

                if (!PointerCell(out cellX, out cellY)) return; // #3179: equal hit areas; a stroke off the canvas keeps its edge

                altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                fillAll = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }

            CellToGrid(cellX, cellY, out gx, out gy);

            // Eyedropper: pick up the colour already under the cursor instead of painting over it. Asked for
            // by name ("ein Kopierer für benutzte Farben") — once a drawing has a dozen shades in it, finding
            // the one you used for the left eye by eye in the palette is guesswork. Besides the armed button
            // it answers to the two gestures every paint program uses: Alt+click and the middle button (#899),
            // so it is there when a hand reaches for it without a trip to the toolbar. On a pad that is Y.
            if (middleDown || (leftDown && (altHeld || _picking)))
            {
                PickColour(_grid[gy * _w + gx]); // the ARMED tool is one-shot, like every paint program: pick, carry on
                return;
            }

            if (_filling)
            {
                if (leftDown || rightDown)
                {
                    // Shift (pad: LB) = replace every pixel of that colour in the region, not just the
                    // connected blob — one gesture to recolour an outline a fill would chase around corners.
                    Fill(gx, gy, rightDown ? 0 : _brush, fillAll);
                }

                return; // armed fill never smears paint while the button stays down
            }

            if (leftDown || rightDown)
            {
                BeginStroke(cellX, cellY); // one history step per stroke, not per pixel
            }

            if (_stroking)
            {
                StrokeTo(cellX, cellY, right || _tool == DrawTool.Eraser ? 0 : _brush); // #3179: a gap-free line, the eraser too
            }
        }

        /// <summary>Ends a stroke once every paint button is released — the preview is too costly to refresh
        /// per pixel, and the whole stroke is one undo step, so both land here.</summary>
        private void EndStroke()
        {
            if (_stroking)
            {
                _stroking = false;
                CommitGridEdit();
                RefreshPreview();
            }
        }

        // ── gamepad: a cell cursor instead of a pointer (#1198) ──────────────────────────────────

        /// <summary>Cells across the visible canvas — the whole grid, or one 32×32 region in region mode.</summary>
        private int CellsX => RegionMode ? RegionCells : _w;

        /// <summary>Cells down the visible canvas — see <see cref="CellsX"/>.</summary>
        private int CellsY => RegionMode ? RegionCells : _h;

        /// <summary>Maps a cell of the VISIBLE canvas onto the underlying grid: identity normally, offset by
        /// the active region's block when the canvas is cropped to one region.</summary>
        private void CellToGrid(int cellX, int cellY, out int gx, out int gy)
        {
            if (RegionMode)
            {
                gx = (_activeRegion % _regionCols) * RegionCells + Mathf.Clamp(cellX, 0, RegionCells - 1);
                gy = (_activeRegion / _regionCols) * RegionCells + Mathf.Clamp(cellY, 0, RegionCells - 1);
                return;
            }

            gx = Mathf.Clamp(cellX, 0, _w - 1);
            gy = Mathf.Clamp(cellY, 0, _h - 1);
        }

        /// <summary>Start swaps the pad between the tool panel and the paint canvas; B leaves the canvas.
        /// One stick cannot walk a toolbar and steer a brush at once, so exactly one of the two owns it. The
        /// rules and the cursor are the shared <see cref="PadCanvasFocus"/> (#1954); this editor only picks the
        /// hint wording. Returns true while the pad owns the canvas.</summary>
        private bool UpdatePadFocus(bool pad)
        {
            if (_ui == null || _canvasRt == null)
            {
                return false;
            }

            _padFocus ??= new PadCanvasFocus(_ui.gameObject, _canvasRt);
            _padFocus.EnsureCanvas(_canvasRt); // the subject area may have been rebuilt since the last frame
            _padFocus.SetCells(CellsX, CellsY);
            bool canvas = _padFocus.Tick(pad);
            SetHint(!pad ? HintKeyNow() : (canvas ? "ui.face.hint_pad_short" : "ui.face.hint_pad_panel"));
            return canvas;
        }

        /// <summary>The shell's controls line (right of the status bar): the mouse wording, an armed tool's, or one of the
        /// two pad modes — set only when it changes.</summary>
        private void SetHint(string key)
        {
            if (_shell == null || key == _hintKey)
            {
                return;
            }

            _hintKey = key;
            _shell.SetHint(L(key));
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

            BeginGridEdit();
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
            CommitGridEdit();
            RenderAll();
            RefreshPreview();
        }

        /// <summary>Paints the whole visible surface in the brush colour in ONE press — the active face
        /// region, or the whole canvas in the square hosts, whatever was on it before. The flood tool only
        /// ever takes the blob under the cursor, so "make this side all blue" used to mean clearing the
        /// region first and then filling the emptiness; this is the same move without the detour.</summary>
        private void FillWholeRegion()
        {
            int x0 = 0, y0 = 0, x1 = _w, y1 = _h;
            if (RegionMode)
            {
                x0 = (_activeRegion % _regionCols) * RegionCells;
                y0 = (_activeRegion / _regionCols) * RegionCells;
                x1 = x0 + RegionCells;
                y1 = y0 + RegionCells;
            }

            BeginGridEdit();
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    _grid[y * _w + x] = _brush;
                }
            }

            _dirty = true;
            CommitGridEdit();
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

        // ── undo / redo ──────────────────────────────────────────────────────────────────────────

        /// <summary>Opens an edit: remembers the canvas before a destructive step. Taken once per stroke,
        /// not per pixel — closed again by <see cref="CommitGridEdit"/>.</summary>
        private void BeginGridEdit()
        {
            _pendingBefore = GridToBytes();
            _pendingSubject = _active;
        }

        /// <summary>Closes the edit <see cref="BeginGridEdit"/> opened and pushes it — unless nothing
        /// actually changed, which is most of what a nervous hand does (a stroke that repaints pixels
        /// that were already that colour must not eat an undo step).</summary>
        private void CommitGridEdit()
        {
            if (_pendingBefore == null)
            {
                return;
            }

            if (_pendingSubject == _active)
            {
                _history.PushGrid(_active, _pendingBefore, GridToBytes());
                Changed();
            }

            _pendingBefore = null;
            _pendingSubject = -1;
        }

        /// <summary>Records a base-colour change. A wheel DRAG is ONE gesture, so while the button stays
        /// down the running entry only grows its "after" — otherwise a two-second drag would fill the whole
        /// history with sixty near-identical shades and push every brush stroke out of it.</summary>
        private void NoteColorEdit(Color before, Color after, bool fromDrag)
        {
            _history.PushColor(_active, before, after, fromDrag);
            AfterHostApplied(); // a base colour reaches the host at once
            Changed();
        }

        /// <summary>Takes the newest step back (the shell's Undo, Ctrl+Z, pad RB). The tab the step happened on comes
        /// first — BEFORE the history moves, so leaving the current tab commits it at the right point of the history.</summary>
        private void Undo()
        {
            EndStroke();
            if (_history.PeekUndo() is not { } next)
            {
                return;
            }

            ShowEditTarget(next);
            if (_history.Undo() is { } edit)
            {
                ApplyEdit(edit, toBefore: true);
            }
        }

        /// <summary>Puts the newest undone step back; the mirror of <see cref="Undo"/>.</summary>
        private void Redo()
        {
            EndStroke();
            if (_history.PeekRedo() is not { } next)
            {
                return;
            }

            ShowEditTarget(next);
            if (_history.Redo() is { } edit)
            {
                ApplyEdit(edit, toBefore: false);
            }
        }

        /// <summary>Brings the tab an edit belongs to on screen (and the paint view back from the Figure tab): a change the
        /// player cannot see happening does not read as an undo at all. A colour step taken back on the Figure tab stays
        /// there — its colour rows show it.</summary>
        private void ShowEditTarget(PixelEditHistory.Edit edit)
        {
            if (_figureShown && edit.IsColor)
            {
                return;
            }

            HideFigure();
            if (edit.Subject != _active)
            {
                SwitchSubject(edit.Subject);
            }
        }

        /// <summary>Puts one edit back (or forward): a canvas step on the active subject, a colour step on its own part.</summary>
        private void ApplyEdit(PixelEditHistory.Edit edit, bool toBefore)
        {
            var colorOf = edit.Subject >= 0 && edit.Subject < _subjects.Count ? _subjects[edit.Subject] : null;
            if (!edit.IsColor)
            {
                BytesToGrid(toBefore ? edit.GridBefore : edit.GridAfter);
                _dirty = true;
            }
            else if (colorOf?.SetBaseColor != null)
            {
                _history.EndColorRun();
                colorOf.SetBaseColor(toBefore ? edit.ColorBefore : edit.ColorAfter);
                UpdateBaseSwatch();
                RefreshFigureSwatches();
                AfterHostApplied();
                OnChanged?.Invoke();
            }

            RenderAll(); // both kinds re-tint the canvas: transparent pixels show the base colour
            RefreshPreview();
        }

        /// <summary>The grid as one byte per cell — the palette holds 32 entries, so an int per cell would
        /// cost four times the memory for the same information.</summary>
        private byte[] GridToBytes()
        {
            var bytes = new byte[_grid.Length];
            for (int i = 0; i < _grid.Length; i++)
            {
                bytes[i] = (byte)Mathf.Clamp(_grid[i], 0, 255);
            }

            return bytes;
        }

        private void BytesToGrid(byte[] bytes)
        {
            if (bytes == null || bytes.Length != _grid.Length)
            {
                return; // a snapshot of another part's canvas — never happens, but never corrupt this one
            }

            for (int i = 0; i < _grid.Length; i++)
            {
                _grid[i] = bytes[i];
            }
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
            EditorPlaytest.Loaded(EditorPlaytest.Avatar);
            var decode = Current?.Decode;
            var grid = decode != null ? decode(face) : FacePalette.Decode(face, _w * _h);
            if (grid == null || grid.Length != _grid.Length)
            {
                grid = new int[_grid.Length];
            }

            Array.Copy(grid, _grid, _grid.Length);
            RenderAll();
        }

        /// <summary>Re-reads every tab's payload and colour from the host after it swapped the whole look
        /// under the editor. The history goes with it: the steps that led here belong to a look that is no
        /// longer on the figure, and undoing into it would mix two outfits together.</summary>
        private void ReloadSubjects()
        {
            foreach (var subject in _subjects)
            {
                if (subject.Fetch != null)
                {
                    subject.Pixels = subject.Fetch() ?? string.Empty;
                }
            }

            _history.Clear(); // keeps "unsaved" as it was: the swap itself is not a step

            int active = _active;
            LoadSubject(active);
            BuildSubjectArea();
            UpdateBaseSwatch();
            RefreshFigureSwatches();
            RefreshPreview();
            if (KeepsScratch && !_hudMode)
            {
                _hostChanged = true; // the designer's scratch look changed — Save makes it the player's
            }
            else
            {
                _history.MarkSaved(); // a live host already wears what it just put on
            }

            Changed();
            OnChanged?.Invoke();
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
            _wheelTargetLabel = UiKit.AddText(panel, wheelX, wheelY + wheelPx + 4f, wheelPx + 60f, 24f,
                L("ui.face.wheel"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(_wheelTargetLabel, wheelPx + 60f, UiKit.EditorMinFontSize, 16);

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
                SetBaseColor(picked, fromDrag: true); // a whole drag is one undo step, not sixty
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
            UiKit.AddKicker(panel, x, y, BaseKickerW, L("ui.paint.base_color"));

            var swGo = new GameObject("BaseSwatch", typeof(RectTransform));
            swGo.transform.SetParent(panel, false);
            UiKit.Place(swGo, x + BaseKickerW + 12f, y - 6f, 48f, 36f);
            _baseSwatch = swGo.AddComponent<Image>();
            _baseSwatch.sprite = UiKit.SolidSprite;
            _baseSwatch.raycastTarget = false;

            float gy = y + 36f;
            const float cell = 36f, pitch = 40f; // #3693: no control smaller than the palette's own swatches
            for (int i = 0; i < AppearancePalette.Colors.Length; i++)
            {
                Color c = AppearancePalette.Colors[i];
                MakeSwatch(panel, x + (i % 10) * pitch, gy + (i / 10) * pitch, cell, c, () => SetBaseColor(c));
            }

            // The wheel drives the brush until the player reaches for a base colour, and switches back the
            // moment they pick a palette swatch again — the label above it always says which.
            UpdateBaseSwatch();
        }

        private void SetBaseColor(Color c) => SetBaseColor(c, fromDrag: false);

        private void SetBaseColor(Color c, bool fromDrag)
        {
            if (Current?.SetBaseColor == null)
            {
                return;
            }

            Color before = Current.GetBaseColor?.Invoke() ?? c;
            _wheelPaintsBase = true;
            Current.SetBaseColor(c);
            NoteColorEdit(before, c, fromDrag);
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
            _preview.WorldLook = true; // The figure in the look's light
            _preview.EnsureBuilt(state.Skin, state.Torso, state.Arms, state.Legs, slot: 1);
            _preview.FullTurntable = true; // a painted back has to come round to be judged
            _preview.SetActive(true);

            var imgGo = new GameObject("PreviewImage", typeof(RectTransform));
            imgGo.transform.SetParent(panel, false);
            UiKit.Place(imgGo, x, y, 267f, 400f);
            var img = imgGo.AddComponent<RawImage>();
            img.texture = _preview.Texture;
            img.raycastTarget = false;
            _preview.AddLookToggle(panel, x, y, 267f, 400f, L);
        }

        /// <summary>Pushes the host's stored appearance plus the UNCOMMITTED canvas onto the preview figure.
        /// Called on stroke end / fill / undo / colour change — not per pixel, since each call re-bakes the
        /// part's atlas and meshes.</summary>
        private void RefreshPreview()
        {
            if (_hudMode)
            {
                RefreshHudPreview(); // #3318: the HUD mode previews the frame on a little screen, not the figure
                return;
            }

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

        /// <summary>Paints an armed toggle's button: cyan frame AND a dark label on it, so the state survives
        /// a glance rather than needing a comparison with the button next to it (the HUD mode's size / opacity).</summary>
        private static void Arm(Image button, Text label, bool on)
        {
            if (button != null)
            {
                button.color = on ? UiKit.Cyan : UiKit.PanelFill;
            }

            if (label != null)
            {
                label.color = on ? UiKit.PanelFill : UiKit.TextCol;
            }
        }

        /// <summary>Which controls line the shell's hint should carry right now: an armed tool says what the next click
        /// will do, otherwise the mouse controls.</summary>
        private string HintKeyNow()
            => _filling ? "ui.face.hint_fill_short"
             : _picking ? "ui.face.hint_pick_short"
             : "ui.face.hint_short";

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

        /// <summary>Takes a paint colour (a swatch, the wheel, the eyedropper). A colour means painting: the eraser and the
        /// one-shot eyedropper hand over to the pen; an armed fill stays armed with the new colour.</summary>
        private void SetBrush(int index, Image swatch)
        {
            _brush = index;
            _wheelPaintsBase = false; // picking paint puts the wheel back on the brush
            UpdateBaseSwatch();
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one;
            _activeSwatch = swatch;
            if (_activeSwatch != null) _activeSwatch.transform.localScale = Vector3.one * 1.12f;
            if (index != 0 && (_tool == DrawTool.Eraser || _tool == DrawTool.Pick))
            {
                SelectTool(DrawTool.Pen);
            }
        }

        /// <summary>The eyedropper's result: a colour goes to the brush, a transparent pixel arms the eraser.</summary>
        private void PickColour(int picked)
        {
            if (picked == 0)
            {
                SelectTool(DrawTool.Eraser);
                return;
            }

            SetBrush(picked, _swatches[picked]);
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
            const float tile = 112f, labelH = 18f, gap = 8f, colGap = 24f; // 18: room for the 13 px region names (#3263)
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
                        UiKit.EditorMinFontSize, UiKit.CyanDim, TextAnchor.MiddleCenter);

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
            BeginGridEdit();
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
            CommitGridEdit();
            RenderAll();
            RefreshPreview();
        }

        private string Encode()
        {
            var encode = Current?.Encode;
            return encode != null ? encode(_grid) : FacePalette.Encode(_grid, _grid.Length);
        }

        /// <summary>Hands the active subject's canvas to its host. Called on Save and whenever the player
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
            AfterHostApplied();
            OnChanged?.Invoke();
        }

        /// <summary>Leaves the editor the way the hosts without an own Back did: it goes away (the shell's Back has already
        /// written the draft).</summary>
        private void Close() => Destroy(gameObject);

        private string L(string key) => Localizer?.Invoke(key) ?? key;
    }
}
