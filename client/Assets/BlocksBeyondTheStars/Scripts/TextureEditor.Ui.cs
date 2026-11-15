// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's screen on the <see cref="EditorShell"/> (#3693), as the approved mock draws it: the shell's
    /// header and status bar, and three opaque cards between them — the texture list with its search on the left, the
    /// canvas in the middle (the frames beside it, ONE segmented tool bar and the edit buttons under it) and colour,
    /// previews and "More" on the right (<c>TextureEditor.Panels.cs</c>). Laid out so every label fits in all 14
    /// languages (#3263); the boxes are mirrored by the headless <c>EditorTextFitTests</c> — move a button, update its
    /// entry there.
    /// </summary>
    public sealed partial class TextureEditor
    {
        /// <summary>The three cards (mock "Textur"): x, width; all from y 120, 900 high.</summary>
        internal const float LeftX = 24f, LeftW = 360f, CentreX = 400f, CentreW = 860f, RightX = 1276f, RightW = 620f,
            CardY = 120f, CardH = 900f;

        /// <summary>The canvas in the centre card.</summary>
        internal const float CanvasX = 24f, CanvasY = 48f;

        /// <summary>The frame column right of the canvas.</summary>
        internal const float FrameColumnX = CanvasX + CanvasPx + 20f, FrameColumnW = 152f;

        /// <summary>The rows under the canvas: frame label, the tool bar, two rows of edit buttons.</summary>
        internal const float FrameLabelY = 696f, ToolBarY = 728f, ToolBarH = 48f, EditRow1Y = 784f, EditRow2Y = 836f,
            RowH = 44f, RowW = 812f;

        /// <summary>The tool bar's label inset on each side of a segment.</summary>
        internal const float ToolLabelInset = 6f;

        /// <summary>The width of a stepper's label ("Light", "Contrast", "Colour" between − and +).</summary>
        internal const float StepperLabelW = 96f;

        /// <summary>The tools of the segmented bar, left to right (mock: pen, fill, pick a colour, eraser — then line, box).</summary>
        private static readonly Tool[] BarTools = { Tool.Brush, Tool.Fill, Tool.Pipette, Tool.Eraser, Tool.Line, Tool.Rectangle };

        private static readonly string[] BarKeys =
        {
            "ui.tex.tool.brush", "ui.tex.tool.fill", "ui.tex.tool.pipette", "ui.tex.tool.eraser", "ui.tex.tool.line", "ui.tex.tool.rect",
        };

        private EditorShell _shell;
        private PadCanvasFocus _pad;
        private UiKit.Segmented _toolBar;
        private RectTransform _canvasRt;
        private RectTransform _slotLayer;
        private readonly RawImage[] _thumbImgs = new RawImage[TextureTiles.MaxFrames];
        private readonly Image[] _thumbFrames = new Image[TextureTiles.MaxFrames];
        private readonly List<EditorPaletteKit.Entry> _rows = new List<EditorPaletteKit.Entry>();
        private PaletteListUi _list;
        private bool _selectingRow;       // the list is being moved by code, not by a click
        private string _search = string.Empty;
        private Text _frameLabel, _sizeLabel, _layerLabel;
        private Texture2D _checker;

        /// <summary>The drafts folder EditMode tests set (by reflection) before Start; null = the player's drafts folder.</summary>
        private string _draftRootForTests = null;

        private void BuildUi()
        {
            _ui = UiKit.CreateCanvas("Texture Editor UI");
            _ui.sortingOrder = WorldHost != null ? 60 : 5; // in a world: above the console (sortingOrder 50)
            _shell = EditorShell.Build(_ui, this, new EditorShellOptions
            {
                EditorKey = DraftKind,
                TitleKey = "ui.menu.texture_editor",
                SaveSublineKey = "ui.editor.save_sub.texture",
                OnBack = Close,
                OnSave = SaveForMe,
                OnSubmit = OpenSubmit,
                HasUndo = true,
                DraftRoot = _draftRootForTests ?? CanvasEditorDrafts.DraftRoot,
            });

            if (WorldHost != null)
            {
                // Over a running world the scene and the console must not shine through: an opaque backdrop, the work
                // area's first child (the header and the status bar are drawn over it).
                var backdrop = UiKit.AddImage(_shell.WorkArea, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0.02f, 0.04f, 0.09f, 1f));
                backdrop.raycastTarget = true;
                backdrop.name = "TextureEditorBackdrop";
            }

            BuildBrowser(_shell.AddCard(LeftX, CardY, LeftW, CardH));
            BuildCanvasPanel(_shell.AddCard(CentreX, CardY, CentreW, CardH));
            BuildRightPanel(_shell.AddCard(RightX, CardY, RightW, CardH));

            _pad = new PadCanvasFocus(_ui.gameObject, _canvasRt);
            SetTool(Tool.Brush);
            SetBrushSize(1);
            SetPrimary(_primary);
            _shell.PreferFocus(_toolBar.Buttons[0]); // the pad starts on the pen, never on Back
        }

        /// <summary>A section title (the mock's kicker): upper case, cyan, never under 13 px.</summary>
        private static Text Kicker(Transform parent, float x, float y, float w, string key)
        {
            var label = UiKit.AddKicker(parent, x, y, w, L(key));
            UiKit.FitLabelSingleLine(label, w, UiKit.EditorMinFontSize, 16);
            return label;
        }

        /// <summary>A button of a tier with its label fitted the editors' way (one line ≥ 14 px, else two lines ≥ 13 px), a
        /// glyph (−, +, ←) centred over a narrow one.</summary>
        private static Button TierButton(Transform parent, float x, float y, float w, float h, string label, System.Action onClick,
            ButtonTier tier = ButtonTier.Quiet)
        {
            var button = UiKit.AddTierButton(parent, x, y, w, h, label, onClick, tier);
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null && w < 60f)
            {
                UiKit.Place(text.gameObject, 2f, 0f, w - 4f, h);
                text.alignment = TextAnchor.MiddleCenter;
            }

            UiKit.FitButtonLabel(text);
            return button;
        }

        /// <summary>A switch-like button (symmetry, play, parts): the "on" state is drawn as a secondary button.</summary>
        private static void Tint(Button button, bool on) => UiKit.StyleTier(button, on ? ButtonTier.Secondary : ButtonTier.Quiet);

        // ---------------------------------------------------------------- left card: the textures

        private void BuildBrowser(Transform card)
        {
            Kicker(card, 20f, 16f, 320f, "ui.tex.browser");
            UiKit.AddInput(card, 20f, 50f, 320f, RowH, string.Empty, v =>
            {
                _search = v;
                RebuildList();
            }, L("ui.tex.search"), 40, 16);

            var listContent = UiKit.ScrollList(card, 20f, 106f, 320f, 778f);
            _rows.Clear();
            foreach (var e in _entries)
            {
                _rows.Add(new EditorPaletteKit.Entry
                {
                    Id = e.Key,
                    Label = e.Label,
                    Kind = "block",
                    Group = e.Group,
                    Color = new Color(0.4f, 0.5f, 0.6f),
                    Icon = e.Block != null ? EditorPaletteKit.TileSprite(_atlas, e.Block.NumericId.Value) : null,
                });
            }

            _list = new PaletteListUi(Shell != null ? Shell : FindAnyObjectByType<AppShell>(), listContent, _rows);
            _list.OnSelected = OnRowPicked;
            RebuildList();
        }

        private void RebuildList()
        {
            _selectingRow = true; // Rebuild reports the current selection again — that is no pick
            _list?.Rebuild(_search);
            _selectingRow = false;
        }

        /// <summary>A click on a row of the list: another texture — after asking when the canvas holds unsaved work
        /// (#3690). Until the player agrees, the open texture stays the highlighted row.</summary>
        private void OnRowPicked(int index)
        {
            if (_selectingRow || index < 0 || index >= _entries.Count || ReferenceEquals(_entries[index], _entry))
            {
                return;
            }

            var next = _entries[index];
            HighlightEntry(_entry);
            _shell.ConfirmDiscard(() =>
            {
                HighlightEntry(next);
                Open(next);
            });
        }

        /// <summary>Moves the list's highlight to <paramref name="entry"/> without opening anything.</summary>
        private void HighlightEntry(TextureEntry entry)
        {
            int index = entry == null ? -1 : _entries.IndexOf(entry);
            if (_list == null || index < 0)
            {
                return;
            }

            _selectingRow = true;
            _list.Select(index);
            _selectingRow = false;
        }

        // ---------------------------------------------------------------- centre card: the canvas

        private void BuildCanvasPanel(Transform card)
        {
            _layerLabel = UiKit.AddText(card, CanvasX, 12f, RowW, 28f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(_layerLabel, RowW, UiKit.EditorMinFontSize, 16);

            // A checkerboard under the canvas, so see-through texels read as see-through and not as black.
            var checker = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            checker.SetPixels32(new[] { new Color32(70, 74, 84, 255), new Color32(104, 108, 118, 255), new Color32(104, 108, 118, 255), new Color32(70, 74, 84, 255) });
            checker.Apply(false);
            var checkerGo = new GameObject("Checker", typeof(RectTransform));
            checkerGo.transform.SetParent(card, false);
            UiKit.Place(checkerGo, CanvasX, CanvasY, CanvasPx, CanvasPx);
            var checkerImg = checkerGo.AddComponent<RawImage>();
            checkerImg.texture = checker;
            checkerImg.uvRect = new Rect(0f, 0f, 16f, 16f);
            checkerImg.raycastTarget = false;
            _checker = checker;

            var canvasGo = new GameObject("TextureCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(card, false);
            _canvasRt = UiKit.Place(canvasGo, CanvasX, CanvasY, CanvasPx, CanvasPx);
            canvasGo.AddComponent<RawImage>().texture = _tex;

            var slotGo = new GameObject("FaceSlots", typeof(RectTransform));
            slotGo.transform.SetParent(_canvasRt, false);
            _slotLayer = UiKit.Place(slotGo, 0f, 0f, CanvasPx, CanvasPx);

            BuildFrameColumn(card);
            _frameLabel = UiKit.AddText(card, CanvasX, FrameLabelY, FrameColumnX - CanvasX - 10f, 24f, string.Empty, 16, UiKit.TextCol,
                TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(_frameLabel, FrameColumnX - CanvasX - 10f, UiKit.EditorMinFontSize, 16);
            BuildToolBar(card);
            BuildEditRows(card);
        }

        /// <summary>The frames beside the canvas: eight thumbnails two by two, then add / remove / move, speed, play — and
        /// the canvas switches "parts" and "fill everything".</summary>
        private void BuildFrameColumn(Transform card)
        {
            for (int i = 0; i < TextureTiles.MaxFrames; i++)
            {
                int index = i;
                float x = FrameColumnX + 2f + ((i % 2) * 84f), y = CanvasY + 2f + ((i / 2) * 72f);
                _thumbFrames[i] = UiKit.AddImage(card, x - 2f, y - 2f, 68f, 68f, UiKit.SolidSprite, Color.white);
                var go = new GameObject("Frame" + i, typeof(RectTransform));
                go.transform.SetParent(card, false);
                UiKit.Place(go, x, y, 64f, 64f);
                _thumbImgs[i] = go.AddComponent<RawImage>();
                _thumbImgs[i].texture = _thumbs[i];
                var btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SelectFrame(index));
            }

            const float x0 = FrameColumnX, w = FrameColumnW, step = RowH + 8f;
            float by = CanvasY + 296f;
            TierButton(card, x0, by, w, RowH, L("ui.tex.frame_add"), AddFrame);
            TierButton(card, x0, by + step, w, RowH, L("ui.tex.frame_del"), () => { _model.DeleteFrame(); AfterChange(); });
            TierButton(card, x0, by + (2f * step), 72f, RowH, "◀", () => { _model.MoveFrame(-1); AfterChange(); });
            TierButton(card, x0 + 80f, by + (2f * step), 72f, RowH, "▶", () => { _model.MoveFrame(1); AfterChange(); });
            TierButton(card, x0, by + (3f * step), w, RowH, L("ui.tex.fps"), CycleFps);
            Button play = null;
            play = TierButton(card, x0, by + (4f * step), w, RowH, L("ui.tex.play"), () => Tint(play, TogglePlay()));
            Button slots = null;
            slots = TierButton(card, x0, by + (5f * step), w, RowH, L("ui.tex.slots"), () =>
            {
                _showSlots = !_showSlots;
                Tint(slots, _showSlots);
                RebuildSlotOverlay();
            });
            Tint(slots, _showSlots);
            TierButton(card, x0, by + (6f * step), w, RowH, L("ui.tex.fill_all"), () => { _model.Clear(_primary); AfterChange(); });
        }

        /// <summary>The tools as ONE segmented bar under the canvas (mock: pen, fill, pick a colour, eraser; plus line and
        /// box). The selected tool is drawn filled.</summary>
        private void BuildToolBar(Transform card)
        {
            var labels = new string[BarKeys.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = L(BarKeys[i]);
            }

            _toolBar = UiKit.AddSegmented(card, CanvasX, ToolBarY, RowW, ToolBarH, labels, 0, i => SetTool(BarTools[i]));
            float segW = (RowW - ((BarKeys.Length - 1) * 8f)) / BarKeys.Length;
            foreach (var button in _toolBar.Buttons)
            {
                // Six tools share 812 px: the label gets the segment's width but for a 6 px inset on each side, centred
                // ("Прямоугольник (R)" needs it, #3263).
                var text = button.GetComponentInChildren<Text>(true);
                UiKit.Place(text.gameObject, ToolLabelInset, 0f, segW - (2f * ToolLabelInset), ToolBarH);
                text.alignment = TextAnchor.MiddleCenter;
                UiKit.FitButtonLabel(text);
            }
        }

        /// <summary>Brush size, symmetry and flips on one row; shift arrows and the light / contrast / colour steppers on the
        /// next (quiet buttons — every one of them is one undo step).</summary>
        private void BuildEditRows(Transform card)
        {
            float y = EditRow1Y;
            TierButton(card, CanvasX, y, RowH, RowH, "−", () => SetBrushSize(_brushSize - 1));
            _sizeLabel = UiKit.AddText(card, CanvasX + 48f, y, 136f, RowH, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.FitLabelSingleLine(_sizeLabel, 136f, UiKit.EditorMinFontSize, 16);
            TierButton(card, CanvasX + 188f, y, RowH, RowH, "+", () => SetBrushSize(_brushSize + 1));
            Button mirrorX = null, mirrorY = null;
            mirrorX = TierButton(card, 264f, y, 136f, RowH, L("ui.tex.mirror_x"), () => { _model.MirrorX = !_model.MirrorX; Tint(mirrorX, _model.MirrorX); });
            mirrorY = TierButton(card, 408f, y, 136f, RowH, L("ui.tex.mirror_y"), () => { _model.MirrorY = !_model.MirrorY; Tint(mirrorY, _model.MirrorY); });
            TierButton(card, 552f, y, 138f, RowH, L("ui.tex.flip_h"), () => { _model.Flip(true); AfterChange(); });
            TierButton(card, 698f, y, 138f, RowH, L("ui.tex.flip_v"), () => { _model.Flip(false); AfterChange(); });

            y = EditRow2Y;
            TierButton(card, CanvasX, y, RowH, RowH, "←", () => { _model.Shift(-8, 0); AfterChange(); });
            TierButton(card, CanvasX + 50f, y, RowH, RowH, "→", () => { _model.Shift(8, 0); AfterChange(); });
            TierButton(card, CanvasX + 100f, y, RowH, RowH, "↑", () => { _model.Shift(0, -8); AfterChange(); });
            TierButton(card, CanvasX + 150f, y, RowH, RowH, "↓", () => { _model.Shift(0, 8); AfterChange(); });
            Stepper(card, 228f, y, "ui.tex.light", () => _model.Adjust(-0.06f, 0f, 0f), () => _model.Adjust(0.06f, 0f, 0f));
            Stepper(card, 428f, y, "ui.tex.contrast", () => _model.Adjust(0f, -0.12f, 0f), () => _model.Adjust(0f, 0.12f, 0f));
            Stepper(card, 628f, y, "ui.tex.colourfulness", () => _model.Adjust(0f, 0f, -0.15f), () => _model.Adjust(0f, 0f, 0.15f));
        }

        /// <summary>A − label + group for one colour adjustment (each press is one undo step).</summary>
        private void Stepper(Transform card, float x, float y, string labelKey, System.Action less, System.Action more)
        {
            TierButton(card, x, y, RowH, RowH, "−", () => { less(); AfterChange(); });
            var label = UiKit.AddText(card, x + 48f, y, StepperLabelW, RowH, L(labelKey), 16, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.FitLabelSingleLine(label, StepperLabelW, UiKit.EditorMinFontSize, 16);
            TierButton(card, x + 52f + StepperLabelW, y, RowH, RowH, "+", () => { more(); AfterChange(); });
        }
    }
}
