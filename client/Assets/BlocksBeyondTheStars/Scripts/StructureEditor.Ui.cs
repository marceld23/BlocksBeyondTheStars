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
    /// The Station/Town editor's panels (uGUI). Laid out so every text fits in all 14 languages (#3263): a 140 px label
    /// column, 150 px value labels that shrink to 13 px on one line, buttons fitted by <see cref="UiKit.AddFitButton"/>, and
    /// the metadata in a scrolling body — module + settlement mode has more rows than the panel is tall, and the rows ran
    /// under the status line. The boxes are mirrored by the headless <c>EditorTextFitTests</c>.
    /// </summary>
    public sealed partial class StructureEditor
    {
        private const float PanelH = 1048f;

        /// <summary>The row grid of the metadata panel: label column, value label, its → button (#3263).</summary>
        internal const float LabelW = 140f, ValueX = 160f, ValueW = 150f, ArrowX = 316f, ArrowW = 40f, RowH = 30f;

        /// <summary>A labelled value row: tall enough for two 13 px lines ("Дверь стыковочного узла").</summary>
        internal const float ValueRowH = 36f, ValueRowStep = 40f;

        /// <summary>The scrolling body between the title and the status line.</summary>
        private const float BodyY = 48f, BodyH = PanelH - 216f - BodyY;

        private Canvas _canvas;
        private Text _statusLabel;
        private Text _blocksLabel;
        private Text _tierLabel;
        private Text _roleLabel;
        private Text _sizeHintLabel;
        private Text _weightLabel;
        private Text _shapeLabel;
        private Text _orientLabel;
        private Transform _palListParent;
        private PaletteListUi _palList;
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
            _formPicker?.Close();
            _formPicker = null;
            _atlas?.Release(); // palette sprites reference this texture; the editor holds a reference (#423 lesson, shared since #1523)
            _atlas = null;
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private void SetStatus(string s) { _status = s; if (_statusLabel != null) _statusLabel.text = s; }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Structure Editor UI");
            _canvas.sortingOrder = 5;
            var root = _canvas.transform;

            // Left: palette (markers, tools + every placeable block), grouped by category, with a search filter — only what the
            // design's target reads (#3523); the selection stays an index into the whole palette.
            int shownSelected = RefreshShownPalette();
            (_palListParent, _palList) = EditorPaletteKit.AddPalettePanel(Shell, root, PanelH, L("ui.struct.palette"), L("ui.pal.search"),
                _shown, shownSelected, _search, v => { _search = v ?? string.Empty; _palList.Rebuild(_search); }, SelectShown);

            // Right: metadata, in a scrolling body.
            var meta = RightPanel(root, 380f, PanelH);
            string title = EditorMode == Mode.Station ? L("ui.struct.title_station") : L("ui.struct.title_settlement");
            UiKit.AddText(meta, 16f, 12f, 348f, 26f, title, 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            _body = UiKit.AddScrollViewport(meta, "MetaBody", 0f, BodyY, 376f, BodyH, out var scroll);
            UiKit.AddInlineScrollbar(scroll, 6f);
            float y = BuildBodyRows(_body);
            _body.sizeDelta = new Vector2(0f, y + 8f);
            _body.anchoredPosition = new Vector2(0f, Mathf.Clamp(_bodyScroll, 0f, Mathf.Max(0f, y + 8f - BodyH)));
            scroll.onValueChanged.AddListener(_ => _bodyScroll = _body.anchoredPosition.y);

            // Footer: status, then save + submit (#3262) on a row of their own; load + back share the row below.
            _statusLabel = UiKit.AddText(meta, 16f, PanelH - 208f, 348f, 84f, string.Empty, 13, UiKit.Ok, TextAnchor.UpperLeft);
            _statusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            AddSaveAndSubmit(meta, PanelH - 116f);
            UiKit.AddFitButton(meta, 16f, PanelH - 66f, 168f, 40f, L("ui.struct.load"), OpenLoadPicker);
            UiKit.AddFitButton(meta, 196f, PanelH - 66f, 168f, 40f, L("ui.menu.back"), () => Shell?.CloseStructureEditor());

            // Controls hint, between the two side panels.
            EditorSceneKit.AddBottomHint(root, EditorSceneKit.BottomHintWidth(300f, 380f), L("ui.struct.hint"));
        }

        /// <summary>The scrolling body: identity, the rows of the chosen <i>Use as</i>, pack and weight, then — for a design — the
        /// previews and the procedural starting point, the stamping panel (#3523) and the brush. Returns the body's height.</summary>
        private float BuildBodyRows(Transform body)
        {
            float y = BuildIdentityRows(body, 8f);
            if (_entryMode)
            {
                y = BuildEntryRows(body, y); // #3523: a generator entry
            }
            else if (_moduleMode)
            {
                y = BuildModuleRows(body, y);
            }
            else if (_landmarkMode)
            {
                y = BuildLandmarkRows(body, y); // #3492
            }

            y = BuildPackRows(body, y);
            if (!_entryMode)
            {
                y = BuildPreviewRows(body, y); // #3523: quarter turn, materials, ruin
                y = BuildGenerateRows(body, y);
            }

            y = BuildStampingRows(body, y); // #3523: what happens when it is stamped
            return _entryMode ? y : BuildBrushRows(body, y);
        }

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

        /// <summary>Key, name, size tier with its procedural size, "use as" and the kit row. Returns the next y.</summary>
        private float BuildIdentityRows(Transform p, float y)
        {
            FieldLabel(p, y, L("ui.struct.key"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _key, v => _key = v);
            y += 66f;
            FieldLabel(p, y, L("ui.struct.name"));
            UiKit.AddInput(p, 16f, y + 26f, 348f, 30f, _name, v => _name = v);
            y += 70f;

            RowLabel(p, y, L("ui.struct.tier"));
            _tierLabel = ValueLabel(p, y, TierLabel(_tiers[_tier]));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
            {
                _tier = (_tier + 1) % _tiers.Length;
                RebuildUi(); // #3523: the tier is the style the stamping panel and a city module's palette follow
            });
            y += ValueRowStep;

            // What the procedural generator builds for this tier, so a template matches its scale (#1402).
            _sizeHintLabel = UiKit.AddText(p, 16f, y, 348f, 36f, SizeHint(_tiers[_tier]), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            _sizeHintLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += 40f;

            // Use as (#1826 / #1877): a complete structure, or a MODULE of a kit with a function — the composers dock
            // station modules port to port and stamp settlement / city modules into plots and districts.
            RowLabel(p, y, L("ui.struct.role"));
            _roleLabel = ValueLabel(p, y, UseLabel());
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", CycleUse); // #3492: whole → kit module → landmark
            y += ValueRowStep;

            // #1890: the kit panel is reachable in both modes — editing a kit needs no module on the table. A module also
            // names its kit here.
            if (_moduleMode)
            {
                var kitLabel = UiKit.AddText(p, 16f, y, 70f, RowH, L("ui.struct.kit"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(kitLabel, 70f, UiKit.EditorMinFontSize, 15);
                UiKit.AddInput(p, 90f, y, 144f, RowH, _kit, v => _kit = v ?? string.Empty);
                UiKit.AddFitButton(p, 240f, y, 124f, RowH, L("ui.struct.kits"), OpenKitPanel);
            }
            else
            {
                UiKit.AddFitButton(p, 16f, y, 168f, RowH, L("ui.struct.kits"), OpenKitPanel);
            }

            return y + 36f;
        }

        /// <summary>A module's function, its port door (station) or style (settlement), and the seal / assemble checks.</summary>
        private float BuildModuleRows(Transform p, float y)
        {
            RowLabel(p, y, L("ui.struct.function"));
            ValueLabel(p, y, FunctionLabel(_function));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
            {
                var list = Functions();
                int i = System.Array.IndexOf(list, _function ?? string.Empty);
                _function = list[(i + 1) % list.Length];
                SyncRole();
                RebuildUi(); // #3523: a city role makes it a city module — the palette and the stamping panel follow
            });
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
            UiKit.AddFitButton(p, 196f, y, 168f, 34f, L("ui.struct.assemble"), Assemble);
            return y + 40f;
        }

        /// <summary>Template pack, selection weight and (settlements) the planet types and — whole settlements — the
        /// underground toggle (#3333).</summary>
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

                // #3333: a whole settlement may be UNDERGROUND — never rolled for the surface, only the underground stamper
                // seats it. A module takes the flag from its kit (the kit panel's toggle).
                if (!_moduleMode && !_landmarkMode && !_entryMode) // an entry's pool says whether it is underground (#3523)
                {
                    UiKit.AddFitButton(p, 16f, y, 348f, 34f, (_underground ? "☑ " : "☐ ") + L("ui.struct.underground"), () => { _underground = !_underground; RebuildUi(); });
                    y += 40f;
                }
            }

            return y;
        }

        /// <summary>The block brush — dye, glow, form and orientation for newly placed BLOCK cells — and the cell count.</summary>
        private float BuildBrushRows(Transform p, float y)
        {
            SectionTitle(p, y, L("ui.struct.brush"));
            y += 30f;
            BrushColourRow(p, y, L("ui.struct.dye"), HexOf(_brushTint), v => _brushTint = ParseHex(v), () => _brushTint = 0);
            y += 36f;
            BrushColourRow(p, y, L("ui.struct.glow"), HexOf(_brushGlow), v => _brushGlow = ParseHex(v), () => _brushGlow = 0);
            y += 38f;
            // The form row is two 13 px lines tall: "Ліжко (бік для голови)" needs them (#3263).
            var shape = UiKit.AddText(p, 16f, y, 80f, ValueRowH, L("ui.struct.shape"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(shape, 80f, UiKit.EditorMinFontSize, 15);
            _formIcon = FormIcon(p, 100f, y + 4f, 28f);
            _shapeLabel = UiKit.AddText(p, 134f, y, 112f, ValueRowH, FormName(_brushShape), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelLines(_shapeLabel, 15, UiKit.EditorMinFontSize, UiKit.EditorMinFontSize);
            UiKit.AddFitButton(p, 250f, y + 3f, 114f, RowH, L("ui.ed.form_pick"), OpenFormPicker); // the in-game form grid (#1975)
            y += ValueRowStep;
            var orient = UiKit.AddText(p, 16f, y, 120f, RowH, L("ui.struct.orient"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(orient, 120f, UiKit.EditorMinFontSize, 15);
            _orientLabel = UiKit.AddText(p, 140f, y, 112f, RowH, (_brushOrient * 90) + "°", 15, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddFitButton(p, 258f, y, 106f, RowH, "↻ R", () => { _brushOrient = (_brushOrient + 1) & 3; _orientLabel.text = (_brushOrient * 90) + "°"; });
            y += 38f;

            _lastPlaced = _design.Count;
            _blocksLabel = UiKit.AddText(p, 16f, y, 348f, 24f, string.Format(L("ui.ed.placed"), _design.Count), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            return y + 30f;
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

        private static RectTransform RightPanel(Transform root, float w, float h) => EditorSceneKit.RightPanel(root, w, h);

        private string L(string key) => Shell?.L(key) ?? key;
    }
}
