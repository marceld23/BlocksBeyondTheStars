// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Station/Village editor's two cards on the editor shell (#3692, the approved mock's "Dorf / Station" views): the left
    /// card "Blocks &amp; markers" — search, the Place | Remove tool, the palette rows and the kits — and the right card with
    /// the building parts every child needs (name, size, where it stands, "Show as a ruin", the block's form) and, behind the
    /// remembered <b>Advanced</b> switch, the developer parts as collapsible sections (StructureEditor.Advanced.cs). The cards
    /// are rebuilt in place (<see cref="RebuildUi"/>) — never the canvas, which carries the shell, its draft and its status.
    /// Text boxes are mirrored by the headless <c>EditorTextFitTests</c>.
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The two cards' widths (the right one keeps the 348 px content column the developer rows are laid out for).</summary>
        internal const float LeftCardW = 400f, RightCardW = 380f;

        /// <summary>The row grid of the developer rows: label column, value label, its → button (#3263).</summary>
        internal const float LabelW = 140f, ValueX = 160f, ValueW = 150f, ArrowX = 316f, ArrowW = 40f, RowH = 30f;

        /// <summary>A labelled value row: tall enough for two 13 px lines ("Дверь стыковочного узла").</summary>
        internal const float ValueRowH = 36f, ValueRowStep = 40f;

        /// <summary>The right card's scrolling body below its kicker row.</summary>
        internal const float BodyY = 60f;

        /// <summary>A control of the children's rows: the menu template's 44 px.</summary>
        internal const float KidRowH = 44f;

        private Canvas _canvas;
        private Text _blocksLabel;
        private Text _roleLabel;
        private Text _sizeHintLabel;
        private Text _weightLabel;
        private Text _shapeLabel;
        private Text _orientLabel;
        private Transform _palListParent;
        private PaletteListUi _palList;
        private PaletteCard _paletteCard;
        private Button _advancedSwitch;
        private RectTransform _body;
        private float _bodyScroll; // kept across RebuildUi, so a toggle at the bottom does not jump back to the top
        private int _lastPlaced = -1;

        private void OnDestroy()
        {
            _kitPanel?.Close();
            _kitPanel = null;
            _view?.Dispose();
            _ghost?.Dispose();
            _overlay?.Dispose();
            _ghostMeshes?.Dispose();
            _zoneFrames?.Dispose();
            _formPicker?.Close();
            _formPicker = null;
            _atlas?.Release(); // palette sprites reference this texture; the editor holds a reference (#423 lesson, shared since #1523)
            _atlas = null;
            if (_canvas != null)
            {
                EditorSceneKit.Discard(_canvas.gameObject);
            }
        }

        /// <summary>Builds both cards' content (StructureEditor.Shell.cs created the cards themselves).</summary>
        private void BuildCards()
        {
            BuildPaletteCard(_leftContent);
            BuildFormCard(_rightContent);
            SyncZoneBoxes(); // a landmark's ruin zones in the room (StructureEditor.Zones.cs)
        }

        // ---------------------------------------------------------------- the left card

        /// <summary>"Blocks &amp; markers": the build editors' palette card (<see cref="EditorPaletteKit.AddPaletteCard"/> — the search,
        /// the Place | Remove switch, the tiles the design's target reads, #3523; the children's view leaves the developer tools
        /// out) and, at the bottom, the kits.</summary>
        private void BuildPaletteCard(Transform card)
        {
            int shownSelected = RefreshShownPalette();
            _paletteCard = EditorPaletteKit.AddPaletteCard(Shell, card, LeftCardW, CardH - KitsRowH, new PaletteCardSpec
            {
                Title = L("ui.struct.palette"),
                Palette = _shown,
                Selected = shownSelected,
                Search = _search,
                OnSelected = SelectShown,
                OnSearchChanged = v => _search = v ?? string.Empty,
                OnToolChanged = SetTool,
            });
            if (_removeTool)
            {
                _paletteCard.Tools.Select((int)EditorBuildTool.Remove); // a rebuild keeps the tool
            }

            _palListParent = _paletteCard.ListParent;
            _palList = _paletteCard.List;
            UiKit.AddTierButton(card, 16f, CardH - KitsRowH + 8f, LeftCardW - 32f, KidRowH, L("ui.struct.kits"), OpenKitPanel, ButtonTier.Secondary,
                "btn_load");
        }

        /// <summary>The side cards' height (the work area less the margins).</summary>
        private static float CardH => EditorShell.WorkRect.H - (2f * EditorShellLayout.Margin);

        /// <summary>The kits row at the foot of the palette card.</summary>
        private const float KitsRowH = 68f;

        /// <summary>The tool switch: Place puts the picked block or marker, Remove takes the one under the cursor away — the
        /// middle click is no longer the only way (a laptop touchpad has none, V8).</summary>
        private void SetTool(EditorBuildTool tool)
        {
            _removeTool = tool == EditorBuildTool.Remove;
            _ghost?.Update(false, default, false);
        }

        // ---------------------------------------------------------------- the right card

        /// <summary>The kicker with the Advanced switch, then the scrolling body: the children's rows, then — Advanced on, or a
        /// developer design — the developer sections.</summary>
        private void BuildFormCard(Transform card)
        {
            UiKit.AddKicker(card, 16f, 18f, 196f, L(EditorMode == Mode.Station ? "ui.struct.title_station" : "ui.struct.title_settlement"));
            var advancedLabel = UiKit.AddText(card, 212f, 12f, 88f, KidRowH, L("ui.struct.advanced"), 16, UiKit.TextCol, TextAnchor.MiddleRight);
            UiKit.FitLabelSingleLine(advancedLabel, 88f, UiKit.EditorMinFontSize, 16);
            _advancedSwitch = UiKit.AddSwitch(card, 304f, 18f, ShowAdvanced, SetAdvanced);
            _advancedSwitch.interactable = !IsDeveloperDesign; // a module, a landmark … keeps its parts in view
            UiKit.AddLabelHit(card, 212f, 12f, 88f, KidRowH, _advancedSwitch);

            float bodyH = CardH - BodyY - 12f;
            _body = UiKit.AddScrollViewport(card, "MetaBody", 0f, BodyY, RightCardW - 4f, bodyH, out var scroll);
            UiKit.AddInlineScrollbar(scroll, 6f);
            float y = BuildBodyRows(_body);
            _body.sizeDelta = new Vector2(0f, y + 8f);
            _body.anchoredPosition = new Vector2(0f, Mathf.Clamp(_bodyScroll, 0f, Mathf.Max(0f, y + 8f - bodyH)));
            scroll.onValueChanged.AddListener(_ => _bodyScroll = _body.anchoredPosition.y);
        }

        /// <summary>The children's rows, then the developer part (or the line that says where it is). Returns the body's height.</summary>
        private float BuildBodyRows(Transform body)
        {
            float y = BuildKidRows(body, 4f);
            if (ShowAdvanced)
            {
                return BuildAdvancedRows(body, y); // StructureEditor.Advanced.cs
            }

            UiKit.AddMeasuredText(body, 16f, y + 6f, 348f, L("ui.struct.advanced_hint"), 16, UiKit.CyanDim, out float hintH);
            return y + 6f + hintH + 8f;
        }

        /// <summary>Name, size, where it stands (a whole village), "Show as a ruin" (where the target can fall), the block's form
        /// and turn, and the count.</summary>
        private float BuildKidRows(Transform p, float y)
        {
            y = KidLabel(p, y, "ui.struct.name");
            UiKit.AddInput(p, 16f, y, 348f, KidRowH, _name, v => _name = v ?? string.Empty);
            y += KidRowH + 12f;

            y = KidLabel(p, y, "ui.struct.tier");
            UiKit.AddCycle(p, 16f, y, 348f, KidRowH, TierLabel(_tiers[_tier]), StepTier);
            y += KidRowH + 4f;
            _sizeHintLabel = UiKit.AddText(p, 16f, y, 348f, 42f, SizeHint(_tiers[_tier]), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiKit.FitLabelLines(_sizeHintLabel, 16, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            y += 48f;

            if (EditorMode == Mode.Settlement && !IsDeveloperDesign)
            {
                // #3333: a whole village may stand underground — never rolled for the surface, only the underground stamper seats it.
                y = KidLabel(p, y, "ui.struct.where");
                UiKit.AddCycle(p, 16f, y, 348f, KidRowH, WhereLabel(), _ =>
                {
                    _underground = !_underground;
                    RebuildUi(); // the target (and with it the palette and the stamping) follows
                    return WhereLabel();
                });
                y += KidRowH + 12f;
            }

            if (CanPreviewRuin())
            {
                y = KidSwitchRow(p, y, "ui.ed.landmark.preview_on", _previewOn && _previewKind == PreviewKind.Ruin, on =>
                {
                    if (on)
                    {
                        ShowRuinPreview();
                    }
                    else
                    {
                        BackToDesign();
                    }
                });
            }

            return BuildKidBrushRows(p, y);
        }

        private string WhereLabel() => L(_underground ? "ui.struct.where.underground" : "ui.struct.where.surface");

        /// <summary>One step of the size cycle: the size hint follows at once; the developer rows (the stamping panel, a city
        /// module's palette) are rebuilt only when they are on show.</summary>
        private string StepTier(int dir)
        {
            _tier = (_tier + _tiers.Length + dir) % _tiers.Length;
            if (_sizeHintLabel != null)
            {
                _sizeHintLabel.text = SizeHint(_tiers[_tier]);
            }

            if (ShowAdvanced)
            {
                RebuildUi(); // #3523: the tier is the style the stamping panel and a city module's palette follow
            }

            return TierLabel(_tiers[_tier]);
        }

        /// <summary>A label over a children's row (16 px, shrunk on one line when a language runs long). Returns the row's y.</summary>
        private float KidLabel(Transform p, float y, string key)
        {
            var label = UiKit.AddText(p, 16f, y, 348f, 24f, L(key), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, 348f, UiKit.EditorMinFontSize, 16);
            return y + 26f;
        }

        /// <summary>A switch with its label (the label toggles it too, <see cref="UiKit.AddLabelHit"/>).</summary>
        private float KidSwitchRow(Transform p, float y, string key, bool on, System.Action<bool> change)
        {
            var toggle = UiKit.AddSwitch(p, 16f, y + 6f, on, change);
            UiKit.AddLabelHit(p, 84f, y, 280f, KidRowH, toggle);
            var label = UiKit.AddText(p, 88f, y, 276f, KidRowH, L(key), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelLines(label, 18, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            return y + KidRowH + 12f;
        }

        /// <summary>The block's form (icon, name, the in-game form grid) and turn (↻, R on the keyboard, Y on the pad), and the
        /// count of placed cells.</summary>
        private float BuildKidBrushRows(Transform p, float y)
        {
            y = KidLabel(p, y, "ui.struct.shape");
            _formIcon = FormIcon(p, 16f, y + 2f, 40f);
            _shapeLabel = UiKit.AddText(p, 64f, y, 154f, KidRowH, FormName(_brushShape), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelLines(_shapeLabel, 16, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            UiKit.AddFitButton(p, 224f, y, 140f, KidRowH, L("ui.ed.form_pick"), OpenFormPicker); // the in-game form grid (#1975)
            y += KidRowH + 8f;
            var orient = UiKit.AddText(p, 16f, y, 120f, KidRowH, L("ui.struct.orient"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(orient, 120f, UiKit.EditorMinFontSize, 16);
            _orientLabel = UiKit.AddText(p, 140f, y, 80f, KidRowH, (_brushOrient * 90) + "°", 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddFitButton(p, 224f, y, 140f, KidRowH, "↻ R", TurnBrush);
            y += KidRowH + 8f;
            _lastPlaced = _design.Count;
            _blocksLabel = UiKit.AddText(p, 16f, y, 348f, 26f, string.Format(L("ui.ed.placed"), _design.Count), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            return y + 32f;
        }

        private void TurnBrush()
        {
            _brushOrient = (_brushOrient + 1) & 3;
            if (_orientLabel != null)
            {
                _orientLabel.text = (_brushOrient * 90) + "°";
            }
        }

        // ---------------------------------------------------------------- the developer rows' helpers

        /// <summary>A row label of the 140 px column: 16 px down to 13 on one line, else two lines at 13 (#3263).</summary>
        private static Text RowLabel(Transform p, float y, string text)
        {
            var label = UiKit.AddText(p, 16f, y, LabelW, ValueRowH, text, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelLines(label, 16, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            return label;
        }

        /// <summary>A picked value (tier, function, door, style): 150 px, 16 px down to 13, two lines at the most (#3263).</summary>
        private static Text ValueLabel(Transform p, float y, string text)
        {
            var label = UiKit.AddText(p, ValueX, y, ValueW, ValueRowH, text, 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabelLines(label, 16, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            return label;
        }

        /// <summary>A field label over its input: 15 px, shrunk to 13 on one line when a language runs long.</summary>
        private static void FieldLabel(Transform p, float y, string text)
        {
            var label = UiKit.AddText(p, 16f, y, 348f, 22f, text, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, 348f, UiKit.EditorMinFontSize, 15);
        }

        /// <summary>A section title: 16 px bold, shrunk to 13 on one line when a language runs long.</summary>
        private static void SectionTitle(Transform p, float y, string text)
        {
            var label = UiKit.AddText(p, 16f, y, 348f, 24f, text, 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(label, 348f, UiKit.EditorMinFontSize, 16);
        }

        /// <summary>Key and "use as" — the registration of the template (the size is a children's row now). Returns the next y.</summary>
        private float BuildIdentityRows(Transform p, float y)
        {
            FieldLabel(p, y, L("ui.struct.key"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _key, v => _key = v);
            y += 66f;

            // Use as (#1826 / #1877): a complete structure, or a MODULE of a kit with a function — the composers dock
            // station modules port to port and stamp settlement / city modules into plots and districts.
            RowLabel(p, y, L("ui.struct.role"));
            _roleLabel = ValueLabel(p, y, UseLabel());
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", CycleUse); // #3492: whole → kit module → landmark
            return y + ValueRowStep;
        }

        /// <summary>A module's kit, its function, its port door (station) or style (settlement), and the seal / assemble checks.</summary>
        private float BuildModuleRows(Transform p, float y)
        {
            var kitLabel = UiKit.AddText(p, 16f, y, 70f, RowH, L("ui.struct.kit"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(kitLabel, 70f, UiKit.EditorMinFontSize, 15);
            UiKit.AddInput(p, 90f, y, 144f, RowH, _kit, v => _kit = v ?? string.Empty);
            UiKit.AddFitButton(p, 240f, y, 124f, RowH, L("ui.struct.kits"), OpenKitPanel);
            y += 36f;
            RowLabel(p, y, L("ui.struct.function"));
            ValueLabel(p, y, FunctionLabel(_function));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "…", OpenFunctionPicker); // #3600: a pick list (23 functions)
            y += ValueRowStep;
            if (EditorMode == Mode.Station)
            {
                RowLabel(p, y, L("ui.struct.port_door"));
                var doorLabel = ValueLabel(p, y, L("ui.door_opt." + StructurePorts.DoorOptions[_portDoor]));
                UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
                {
                    _portDoor = (_portDoor + 1) % StructurePorts.DoorOptions.Length;
                    doorLabel.text = L("ui.door_opt." + StructurePorts.DoorOptions[_portDoor]);
                });
            }
            else
            {
                // #1890: a settlement module is built for humans or for aliens; a kit uses the settlement's own.
                RowLabel(p, y, L("ui.struct.style"));
                var styleLabel = ValueLabel(p, y, L(_style == StructureTemplate.StyleAlien ? "ui.style.alien" : "ui.style.human"));
                UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
                {
                    _style = _style == StructureTemplate.StyleAlien ? string.Empty : StructureTemplate.StyleAlien;
                    styleLabel.text = L(_style == StructureTemplate.StyleAlien ? "ui.style.alien" : "ui.style.human");
                });
            }

            y += ValueRowStep;
            UiKit.AddFitButton(p, 16f, y, 168f, 34f, L("ui.struct.check_seal"), CheckSeal);
            UiKit.AddFitButton(p, 196f, y, 168f, 34f, L("ui.struct.assemble"), () => _shell.ConfirmDiscard(Assemble)); // replaces the build
            return y + 40f;
        }

        /// <summary>Template pack, selection weight and (settlements) the planet types.</summary>
        private float BuildPackRows(Transform p, float y)
        {
            FieldLabel(p, y, L("ui.struct.pack"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _pack, v => _pack = v);
            y += 66f;
            RowLabel(p, y, L("ui.struct.weight"));
            _weightLabel = UiKit.AddText(p, ValueX, y, 90f, ValueRowH, _weight.ToString(), 16, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddFitButton(p, 260f, y + 3f, 40f, RowH, "−", () => { _weight = Mathf.Max(1, _weight - 1); _weightLabel.text = _weight.ToString(); });
            UiKit.AddFitButton(p, 310f, y + 3f, 40f, RowH, "+", () => { _weight = Mathf.Min(99, _weight + 1); _weightLabel.text = _weight.ToString(); });
            y += ValueRowStep;

            // Settlements may restrict themselves to planet types (#1115); stations float in space.
            if (EditorMode == Mode.Settlement)
            {
                FieldLabel(p, y, L("ui.struct.planet_types"));
                UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _planetTypes, v => _planetTypes = v ?? string.Empty);
                y += 66f;
            }

            return y;
        }

        /// <summary>The block brush's colours — dye and glow for newly placed BLOCK cells (the form is a children's row).</summary>
        private float BuildBrushColourRows(Transform p, float y)
        {
            BrushColourRow(p, y, L("ui.struct.dye"), HexOf(_brushTint), v => _brushTint = ParseHex(v), () => _brushTint = 0);
            y += 36f;
            BrushColourRow(p, y, L("ui.struct.glow"), HexOf(_brushGlow), v => _brushGlow = ParseHex(v), () => _brushGlow = 0);
            return y + 38f;
        }

        /// <summary>Dye / glow: a label, the hex field and "None" (which clears it and rebuilds the panel).</summary>
        private void BrushColourRow(Transform p, float y, string label, string hex, System.Action<string> set, System.Action clear)
        {
            var text = UiKit.AddText(p, 16f, y, 70f, RowH, label, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(text, 70f, UiKit.EditorMinFontSize, 15);
            UiKit.AddInput(p, 92f, y, 150f, RowH, hex, v => set(v));
            UiKit.AddFitButton(p, 250f, y, 114f, RowH, L("ui.struct.brush_none"), () => { clear(); RebuildUi(); });
        }

        /// <summary>Parses a 6-hex-digit colour string to 0xRRGGBB (0 = empty/invalid = "none").</summary>
        private static int ParseHex(string s) => EditorSceneKit.ParseHex(s);

        private static string HexOf(int rgb) => EditorSceneKit.HexOf(rgb);

        private string L(string key) => Shell?.L(key) ?? key;
    }
}
