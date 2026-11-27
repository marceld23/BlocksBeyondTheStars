// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The screen of the pixel editor on the editor shell (#3693, the approved mock's "Avatar &amp; Aussehen" view): the
    /// shell's header and status bar around opaque cards. With several subjects (the appearance screen in a world, the
    /// Avatar Designer in the menu) ONE tab bar carries the parts — face, torso, arms, legs, helmet — plus "Figure"
    /// (FaceEditor.Figure.cs) and "HUD frame" (the old "Character | HUD frame" switch); under it the paint card (canvas,
    /// region tiles, the tool bar, the palette), the preview card (the turning figure, the part's base colour, the colour
    /// wheel) and the outfits card. A single-subject host (the block paint tool) gets its design library on the left and
    /// one paint card with a larger canvas and the wheel beside it.
    /// </summary>
    public sealed partial class FaceEditor
    {
        /// <summary>The paint tools of the one segmented bar (mock: pen, fill, pick colour, eraser).</summary>
        private enum DrawTool
        {
            Pen,
            Fill,
            Pick,
            Eraser,
        }

        // The appearance screen: tab bar, then paint / preview / outfits cards (1920 × 1080 reference px).
        private const float TabsY = 120f, TabsH = 52f, TabW = 170f, CardsY = 190f, CardsH = 830f;
        private const float PaintX = 24f, PaintW = 900f, SideX = 940f, SideW = 500f, ShelfX = 1456f, ShelfCardW = 440f;

        // A single-subject host: the library card, then one paint card.
        private const float SingleY = 120f, SingleH = 900f, LibraryX = 24f, LibraryW = 360f, SinglePaintX = 400f, SinglePaintW = 860f;

        // Inside the paint card.
        private const float CanvasX = 24f, CanvasY = 20f, ToolH = 48f, ToolMinW = 532f, SwatchSize = 36f, SwatchPitch = 40f;

        /// <summary>The base colour's title; its swatch sits right of it.</summary>
        private const float BaseKickerW = 300f;

        private static readonly Color BackdropColor = new Color(0.02f, 0.04f, 0.08f, 1f);

        /// <summary>
        /// The editor's canvas order in a world: above the console (50), the Codex / DataQubes (55) and VEGA while the console
        /// is up (56, #4071); the HUD renders below every overlay canvas anyway. Below what must stay on top: the F1 feedback
        /// form (60), the console's own small dialogs (61), the submit dialog (70), the transition and loading covers
        /// (70–75), the death flash and prompt (80–85).
        /// </summary>
        internal const int EditorSortingOrder = 59;

        private EditorShell _shell;
        private bool _begun;
        private bool _multi;            // the appearance screen (tabs, regions, base colours) rather than one canvas
        private int _charCount;         // the character subjects the editor started with (the HUD mode swaps _subjects)
        private float _canvasPx = 512f;
        private DrawTool _tool = DrawTool.Pen;
        private UiKit.Segmented _toolBar, _tabs;
        private int _figureTab = -1, _hudTab = -1;
        private bool _figureShown;
        private Transform _paintCard, _sideCard, _shelfCard, _figureCard;
        private InputField _nameField;
        private RectTransform _libList;
        private string _hintKey;

        /// <summary>The shell around this editor (null before <see cref="Begin"/>) — for tests and hosts.</summary>
        public EditorShell Shell => _shell;

        /// <summary>True when this screen has a HUD-frame tab: a HUD host on the appearance screen.</summary>
        private bool HasHudMode => HudHost != null && _charCount > 1;

        private bool HasLibrary => OnSaveDesign != null || LibraryProvider != null;

        private void BuildUi()
        {
            _multi = _subjects.Count > 1 || _subjects.Exists(s => s.ColumnLabelKeys != null && s.ColumnLabelKeys.Length > 0);
            _canvasPx = _multi ? 512f : 576f;
            _ui = UiKit.CreateCanvas("Face Editor UI");
            _ui.sortingOrder = EditorSortingOrder;
            _shell = EditorShell.Build(_ui, this, ShellOptions());
            var work = _shell.WorkArea;

            // Opaque: in a world the console and the world are behind this screen, in the menu the editors' hub.
            var backdrop = UiKit.AddImage(work, 0f, 0f, EditorShellLayout.CanvasW, EditorShellLayout.CanvasH, UiKit.SolidSprite, BackdropColor);
            backdrop.name = "EditorBackdrop";
            backdrop.raycastTarget = true;

            if (_multi)
            {
                BuildAppearanceCards(work);
            }
            else
            {
                BuildSingleCards();
            }

            SelectTool(DrawTool.Pen);
            SetBrush(_brush, _swatches[_brush]);
            RefreshPreview();
            SetHint(HintKeyNow());
            ShowAbout();
            if (_toolBar != null)
            {
                _shell.PreferFocus(_toolBar.Buttons[0]); // the pad starts on the pen, never on Back
            }

            CanvasEditorDrafts.Offer(_shell); // last: "Continue your draft?" when one is left
        }

        /// <summary>The appearance screen: tabs, paint card, preview card, outfits card, the Figure card and the
        /// HUD mode's layers.</summary>
        private void BuildAppearanceCards(Transform work)
        {
            BuildTabBar(work);
            _paintCard = _shell.AddCard(PaintX, CardsY, PaintW, CardsH);
            BuildPaintCard(_paintCard, PaintW, CardsH);
            _sideCard = _shell.AddCard(SideX, CardsY, SideW, CardsH);
            BuildSideCard(_sideCard);
            if (OutfitNames != null || HasHudMode)
            {
                _shelfCard = _shell.AddCard(ShelfX, CardsY, ShelfCardW, CardsH);
                if (OutfitNames != null)
                {
                    BuildOutfitColumn(CharacterLayer(_shelfCard));
                }
            }

            if (_figureTab >= 0)
            {
                _figureCard = _shell.AddCard(PaintX, CardsY, SideX + SideW - PaintX, CardsH);
                BuildFigureCard(_figureCard);
                _figureCard.gameObject.SetActive(false);
            }

            BuildHudMode();
        }

        /// <summary>A single-subject host: its design library on the left (when it has one), the paint card beside it.</summary>
        private void BuildSingleCards()
        {
            if (HasLibrary)
            {
                BuildLibraryCard(_shell.AddCard(LibraryX, SingleY, LibraryW, SingleH));
            }

            _paintCard = _shell.AddCard(SinglePaintX, SingleY, SinglePaintW, SingleH);
            BuildPaintCard(_paintCard, SinglePaintW, SingleH);
            BuildColorWheel(_paintCard, CanvasX + _canvasPx + 24f, CanvasY + 28f);
        }

        /// <summary>ONE tab bar (mock): the parts, the Figure tab, the HUD frame. Nothing for a single subject.</summary>
        private void BuildTabBar(Transform work)
        {
            var labels = new List<string>();
            for (int i = 0; i < _charCount; i++)
            {
                labels.Add(L(_subjects[i].LabelKey));
            }

            if (HasFigureTab)
            {
                _figureTab = labels.Count;
                labels.Add(L(FigureTabKey));
            }

            if (HasHudMode)
            {
                _hudTab = labels.Count;
                labels.Add(L("ui.hudframe.mode_hud"));
            }

            if (labels.Count < 2)
            {
                return;
            }

            float w = Mathf.Min(EditorShellLayout.CanvasW - (2f * PaintX), (labels.Count * TabW) + ((labels.Count - 1) * 8f));
            _tabs = UiKit.AddSegmented(work, PaintX, TabsY, w, TabsH, labels, 0, OnTabSelected);
        }

        private void OnTabSelected(int index)
        {
            if (index == _hudTab)
            {
                EnterHudMode();
            }
            else if (index == _figureTab)
            {
                ShowFigure();
            }
            else
            {
                HideFigure();
                if (_hudMode)
                {
                    EnterCharacterMode();
                }

                SwitchSubject(index);
            }

            HighlightTabs();
            ShowAbout();
        }

        /// <summary>Unattended editor screenshots (<see cref="AvatarEditor.CaptureOpenAppearance"/>): the first part's tab.</summary>
        internal void CaptureShowFirstTab()
        {
            if (_tabs != null)
            {
                OnTabSelected(0);
            }
        }

        /// <summary>The tab bar follows the editor (an undo can carry the tab along, the HUD mode can be entered by code).</summary>
        private void HighlightTabs() => _tabs?.Select(_hudMode ? _hudTab : _figureShown ? _figureTab : _active);

        /// <summary>The canvas with its region tiles, the tool bar, the palette and the two whole-canvas buttons.</summary>
        private void BuildPaintCard(Transform card, float cardW, float cardH)
        {
            var areaGo = new GameObject("SubjectArea", typeof(RectTransform));
            areaGo.transform.SetParent(card, false);
            _subjectArea = UiKit.Place(areaGo, 0f, 0f, cardW, cardH);
            BuildSubjectArea();

            float toolW = Mathf.Max(ToolMinW, _canvasPx);
            float y = CanvasY + _canvasPx + (_multi ? 30f : 12f); // the region name sits under the canvas
            var labels = new[] { L("ui.face.tool.pen"), L("ui.face.fill"), L("ui.face.pick"), L("ui.face.tool.eraser") };
            _toolBar = UiKit.AddSegmented(card, CanvasX, y, toolW, ToolH, labels, 0, i => SelectTool((DrawTool)i));
            y += ToolH + 8f;
            y = BuildPalette(card, y, toolW) + 12f;
            float half = (toolW - 8f) / 2f;
            UiKit.AddTierButton(card, CanvasX, y, half, 44f, L("ui.face.fill_all"), FillWholeRegion, ButtonTier.Quiet);
            UiKit.AddTierButton(card, CanvasX + half + 8f, y, half, 44f, L("ui.face.clear"), ClearCanvas, ButtonTier.Quiet);
        }

        /// <summary>The paint palette: colours 1..N (the eraser is a tool now, no swatch). Returns the bottom edge.</summary>
        private float BuildPalette(Transform card, float y, float width)
        {
            UiKit.AddKicker(card, CanvasX, y, width, L("ui.face.palette"));
            y += 28f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + SwatchPitch - SwatchSize) / SwatchPitch));
            int col = 0;
            float rowY = y;
            for (int i = 1; i < FacePalette.Colors.Length; i++)
            {
                int idx = i;
                _swatches[i] = MakeSwatch(card, CanvasX + (col * SwatchPitch), rowY, SwatchSize, FacePalette.ColorOf(i),
                    () => SetBrush(idx, _swatches[idx]));
                if (++col >= perRow && i < FacePalette.Colors.Length - 1)
                {
                    col = 0;
                    rowY += SwatchPitch;
                }
            }

            return rowY + SwatchSize;
        }

        /// <summary>The preview card: the turning figure and the part's base colour (character mode), the colour wheel
        /// (both modes; the HUD mode's own layer goes in later).</summary>
        private void BuildSideCard(Transform card)
        {
            var layer = CharacterLayer(card);
            UiKit.AddKicker(layer, 24f, 16f, SideW - 48f, L("ui.face.preview"));
            BuildPreview(layer, (SideW - 267f) / 2f, 48f);
            BuildBaseColorPicker(layer, 24f, 460f);
            BuildColorWheel(card, 24f, 626f);
        }

        /// <summary>Switches the paint tool; the bar shows it, the hint says what the next click does.</summary>
        private void SelectTool(DrawTool tool)
        {
            _tool = tool;
            _filling = tool == DrawTool.Fill;
            _picking = tool == DrawTool.Pick;
            _toolBar?.Select((int)tool);
            SetHint(HintKeyNow());
        }

        /// <summary>Commits the current canvas and points the editor at another part, keeping the screen (the
        /// pre-#899 flow destroyed the whole editor per part).</summary>
        private void SwitchSubject(int index)
        {
            if (index == _active || index < 0 || index >= _subjects.Count)
            {
                return;
            }

            EndStroke();
            CommitActive();
            LoadSubject(index);
            BuildSubjectArea();
            SetHint(HintKeyNow());
            HighlightTabs();
            UpdateBaseSwatch();
            RefreshPreview();
        }

        /// <summary>(Re)builds the canvas + region tiles for the active subject.</summary>
        private void BuildSubjectArea()
        {
            if (_subjectArea == null)
            {
                return;
            }

            for (int i = _subjectArea.childCount - 1; i >= 0; i--)
            {
                Destroy(_subjectArea.GetChild(i).gameObject);
            }

            _regionFrames = null;
            _activeRegionLabel = null;
            // The pad cursor lived under the old canvas rect; PadCanvasFocus.EnsureCanvas re-homes it next frame (#1954).

            // Paint surface (point-filtered → crisp big pixels). In region mode it crops the shared texture to the
            // active 32×32 face via uvRect, so every face paints at the same 16 px cell size as the face.
            var canvasGo = new GameObject("FaceCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(_subjectArea, false);
            _canvasRt = UiKit.Place(canvasGo, CanvasX, CanvasY, _canvasPx, _canvasPx);
            _canvas = canvasGo.AddComponent<RawImage>();
            _canvas.texture = _tex;

            if (RegionMode)
            {
                _activeRegionLabel = UiKit.AddText(_subjectArea, CanvasX, CanvasY + _canvasPx + 4f, _canvasPx, 24f, string.Empty, 16,
                    UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                BuildRegionColumn(_subjectArea, CanvasY);
                SetActiveRegion(0);
            }
        }

        // ── the Figure tab ───────────────────────────────────────────────────────────────────────

        /// <summary>The Figure tab: its card replaces the paint and preview cards; the
        /// canvas is committed first, so the figure shows everything painted so far.</summary>
        private void ShowFigure()
        {
            if (_figureCard == null || _figureShown)
            {
                return;
            }

            EndStroke();
            if (_hudMode)
            {
                EnterCharacterMode();
            }

            CommitActive();
            _figureShown = true;
            SetPaintViewActive(false);
            HighlightTabs();
        }

        /// <summary>Back from the Figure tab: the host may have changed colours there, so the canvas re-tints.</summary>
        private void HideFigure()
        {
            if (!_figureShown)
            {
                return;
            }

            _figureShown = false;
            SetPaintViewActive(true);
            UpdateBaseSwatch();
            RenderAll();
            RefreshPreview();
        }

        /// <summary>The paint and preview cards, or the Figure card in their place. The figure in the preview card is the
        /// one the Figure card shows big, so its rig keeps filming either way (outside the HUD mode); the gear preview goes
        /// on only while the Figure card is up.</summary>
        private void SetPaintViewActive(bool on)
        {
            _paintCard?.gameObject.SetActive(on);
            _sideCard?.gameObject.SetActive(on);
            _figureCard?.gameObject.SetActive(!on);
            if (_preview != null)
            {
                _preview.SetActive(!_hudMode);
            }

            RefreshFigureSwatches();
            ApplyPreviewGear();
        }

        // ── the block paint tool's design library ────────────────────────────────────────────────

        /// <summary>The library card: the design's name, "keep in my designs", the share code (desktop only — the browser
        /// has no clipboard for it) and the saved designs in a list that scrolls instead of running over the card.</summary>
        private void BuildLibraryCard(Transform card)
        {
            const float x = 24f, w = LibraryW - 48f;
            UiKit.AddKicker(card, x, 20f, w, L("ui.paint.library"));
            _nameField = UiKit.AddInput(card, x, 52f, w, 44f, _name, v => _name = v ?? string.Empty, L("ui.paint.name"), 24, 18);
            float y = 104f;
            if (OnSaveDesign != null)
            {
                UiKit.AddTierButton(card, x, y, w, 44f, L("ui.paint.keep"), KeepDesign, ButtonTier.Secondary, "btn_new");
                y += 52f;
            }

            if (EditorShell.DesktopFeatures && OnShare != null)
            {
                float half = (w - 8f) / 2f;
                UiKit.AddTierButton(card, x, y, half, 44f, L("ui.shape.custom.export"), () => OnShare(Encode(), _name), ButtonTier.Quiet);
                if (OnImport != null)
                {
                    UiKit.AddTierButton(card, x + half + 8f, y, half, 44f, L("ui.shape.custom.import"), PasteDesign, ButtonTier.Quiet);
                }

                y += 52f;
            }

            _libList = UiKit.AddScrollViewport(card, "PaintLibraryList", x, y + 4f, w, SingleH - y - 28f, out _);
            RebuildLibraryList();
        }

        /// <summary>(Re)fills the library list with one load button per saved design.</summary>
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
            const float rowH = 44f, pitch = 50f;
            for (int i = 0; i < entries.Count; i++)
            {
                var (name, pixels) = entries[i];
                UiKit.AddTierButton(_libList, 0f, i * pitch, LibraryW - 48f, rowH, name, () => LoadDesign(name, pixels), ButtonTier.Quiet);
            }

            _libList.sizeDelta = new Vector2(0f, Mathf.Max(0f, (entries.Count * pitch) - (pitch - rowH)));
        }

        /// <summary>Loads a saved design onto the canvas as one undoable step — after asking, when that would replace
        /// unsaved paint.</summary>
        private void LoadDesign(string name, string pixels) => _shell.ConfirmDiscard(() =>
        {
            PutOnCanvas(pixels);
            _name = name ?? string.Empty;
            if (_nameField != null)
            {
                _nameField.text = _name;
            }
        });

        /// <summary>"Keep in my designs": the canvas goes into the library under its name (the block is painted by Save).</summary>
        private void KeepDesign()
        {
            string pixels = Encode();
            if (FacePalette.IsEmpty(pixels))
            {
                ShowStatus(L("ui.paint.keep_empty"), EditorStatusSeverity.Warning);
                return;
            }

            OnSaveDesign?.Invoke(pixels, _name);
            RebuildLibraryList();
            ShowStatus(L("ui.paint.kept"), EditorStatusSeverity.Info);
        }

        /// <summary>A share code from the clipboard (the host adds it to the library) onto the canvas.</summary>
        private void PasteDesign()
        {
            if (OnImport?.Invoke() is not { } imported)
            {
                return;
            }

            RebuildLibraryList();
            LoadDesign(imported.Name, imported.Pixels);
        }

        /// <summary>Puts a whole picture on the canvas as ONE undoable step.</summary>
        private void PutOnCanvas(string pixels)
        {
            EndStroke();
            BeginGridEdit();
            LoadFrom(pixels ?? string.Empty);
            _dirty = true;
            CommitGridEdit();
            RefreshPreview();
        }
    }
}
