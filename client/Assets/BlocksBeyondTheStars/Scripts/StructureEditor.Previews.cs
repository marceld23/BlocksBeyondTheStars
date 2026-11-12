// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The previews (#3492, #3523): something shown in place of the design — read-only, never saved — until <i>Back to the
    /// design</i> (or the next change of <i>Use as</i>) brings the design back.
    /// <list type="bullet">
    /// <item><b>Quarter turn</b>: the design turned like the server turns a whole settlement template, a landmark or a station
    /// module (<see cref="TemplateTransform.RotateY"/> — shapes, markers and ports turn along).</item>
    /// <item><b>Materials</b>: the material tokens resolved as this target resolves them for the surface field
    /// (<see cref="StampStepsDescriber.MaterialsFor"/>).</item>
    /// <item><b>Ruin</b>: the pass the server runs with the preview seed — a landmark's (<see cref="LandmarkGenerator"/>) or a
    /// whole settlement template's fall on a world of terrain generation 38 (<c>SettlementGenerator.FromTemplate</c> with a
    /// <see cref="TemplateStamp"/>: masonry, the keep cells standing, only the markers a ruin keeps).</item>
    /// <item><b>Entry</b>: a generator entry's build (StructureEditor.Entry.cs).</item>
    /// </list>
    /// While a preview shows, placing, removing and saving are refused.
    /// </summary>
    public sealed partial class StructureEditor
    {
        private enum PreviewKind { None, Ruin, Turn, Materials, Entry }

        private PreviewKind _previewKind;
        private int _turns;

        /// <summary>Shows <paramref name="cells"/> in place of the design; the design is kept aside (once — a second preview
        /// replaces the first, the design stays the one kept).</summary>
        private void ShowPreview(PreviewKind kind, List<CellJson> cells)
        {
            if (!_previewOn)
            {
                _previewBackup = SnapshotCells();
            }

            ApplyCells(cells, new List<string>());
            _previewOn = true;
            _previewKind = kind;
        }

        /// <summary>Brings the design back (nothing happens without a preview).</summary>
        private void EndPreview()
        {
            if (!_previewOn)
            {
                return;
            }

            ApplyCells(_previewBackup ?? new List<CellJson>(), new List<string>());
            DropPreview();
        }

        /// <summary>Forgets a preview without restoring (a load replaced everything anyway).</summary>
        private void DropPreview()
        {
            _previewBackup = null;
            _previewOn = false;
            _previewKind = PreviewKind.None;
            _turns = 0;
        }

        /// <summary>The design's cells — the one kept aside while a preview shows.</summary>
        private List<CellJson> DesignCells() => _previewOn && _previewBackup != null ? new List<CellJson>(_previewBackup) : SnapshotCells();

        /// <summary>The status line of a refused edit while a preview shows.</summary>
        private string PreviewLockedKey()
            => _previewKind == PreviewKind.Entry ? "ui.ed.entry.locked"
                : _previewKind == PreviewKind.Ruin ? "ui.ed.landmark.preview_locked"
                : "ui.ed.preview_locked";

        // ---------------------------------------------------------------- the form

        /// <summary>Whether the target can stand as a ruin the editor previews: a landmark, a whole settlement template.</summary>
        private bool CanPreviewRuin() => _target is StampTarget.Landmark or StampTarget.SettlementWhole;

        /// <summary>Quarter turn and materials for every design; the ruin with its seed where the target can fall; and the way
        /// back while a preview shows.</summary>
        private float BuildPreviewRows(Transform p, float y)
        {
            SectionTitle(p, y, L("ui.struct.preview_title"));
            y += 28f;
            UiKit.AddFitButton(p, 16f, y, 168f, 34f, L("ui.struct.turn"), ShowTurnPreview);
            if (StructureStampRules.ResolvesTokens(_target))
            {
                UiKit.AddFitButton(p, 196f, y, 168f, 34f, L("ui.struct.materials"), ShowMaterialsPreview);
            }

            y += 40f;
            if (CanPreviewRuin())
            {
                y = BuildRuinSeedRow(p, y);
                UiKit.AddFitButton(p, 16f, y, 348f, 34f, L("ui.ed.landmark.preview_on"), ShowRuinPreview);
                y += 40f;
            }

            if (_previewOn)
            {
                UiKit.AddFitButton(p, 16f, y, 348f, 34f, L("ui.ed.landmark.preview_off"), BackToDesign);
                y += 40f;
            }

            return y + 6f;
        }

        private float BuildRuinSeedRow(Transform p, float y)
        {
            var seedLabel = UiKit.AddText(p, 16f, y, 60f, RowH, L("ui.struct.seed"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(seedLabel, 60f, UiKit.EditorMinFontSize, 15);
            var seedInput = UiKit.AddInput(p, 80f, y, 110f, RowH, _previewSeed.ToString(CultureInfo.InvariantCulture), v =>
            {
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                {
                    _previewSeed = s;
                }
            });
            seedInput.contentType = InputField.ContentType.IntegerNumber;
            UiKit.AddFitButton(p, 196f, y, 168f, RowH, L("ui.struct.reroll"), () =>
            {
                _previewSeed = UnityEngine.Random.Range(1, 1000000);
                seedInput.SetTextWithoutNotify(_previewSeed.ToString(CultureInfo.InvariantCulture));
                if (_previewKind == PreviewKind.Ruin)
                {
                    ShowRuinPreview(); // the next ruin straight away
                }
            });
            return y + 36f;
        }

        private void BackToDesign()
        {
            EndPreview();
            SetStatus(L("ui.ed.landmark.preview_back"));
            RebuildUi();
        }

        // ---------------------------------------------------------------- the previews

        /// <summary>One more quarter turn of the design (four bring it back).</summary>
        private void ShowTurnPreview()
        {
            var design = DesignCells();
            if (design.Count == 0)
            {
                SetStatus(L("ui.submit.empty"));
                return;
            }

            int turns = _previewKind == PreviewKind.Turn ? (_turns + 1) & 3 : 1;
            if (turns == 0)
            {
                BackToDesign();
                return;
            }

            var turned = TemplateTransform.RotateY(DesignTemplate("turn", design), turns);
            var cells = new List<CellJson>(turned.Cells.Count);
            foreach (var c in turned.Cells)
            {
                cells.Add(ToCellJson(c));
            }

            ShowPreview(PreviewKind.Turn, cells);
            _turns = turns;
            _status = string.Format(L("ui.ed.turn_shown"), turns * 90);
            RebuildUi();
        }

        /// <summary>The design with every material token in the block this target resolves it to (surface field, style).</summary>
        private void ShowMaterialsPreview()
        {
            if (Shell?.Content == null)
            {
                return;
            }

            var materials = StampStepsDescriber.MaterialsFor(StampFormFields(new StampDesign()), Shell.Content);
            var cells = DesignCells();
            int tokens = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.kind == "block" && MaterialTokens.IsToken(c.id))
                {
                    // A copy: the cells kept aside while another preview shows are the design itself.
                    string block = Shell.Content.BlockById(new BlockId(materials.Resolve(c.id)))?.Key ?? c.id;
                    cells[i] = new CellJson { x = c.x, y = c.y, z = c.z, kind = c.kind, id = block, tint = c.tint, glow = c.glow, shape = c.shape };
                    tokens++;
                }
            }

            ShowPreview(PreviewKind.Materials, cells);
            _status = string.Format(L("ui.ed.materials_shown"), tokens, L("block." + PreviewSurface() + ".name"));
            RebuildUi();
        }

        /// <summary>The ruin the server would derive with the preview seed — a landmark's or a whole settlement template's.</summary>
        private void ShowRuinPreview()
        {
            if (Shell?.Content == null)
            {
                return;
            }

            var design = DesignCells();
            if (design.Count == 0)
            {
                SetStatus(L("ui.submit.empty"));
                return;
            }

            List<CellJson> cells;
            if (_landmarkMode)
            {
                var layout = LandmarkGenerator.Build(BuildLandmarkTemplate("preview", design), _previewSeed, 0, Shell.Content, PreviewSurface(), ruin: true);
                cells = RuinCells(layout);
            }
            else
            {
                string surface = PreviewSurface();
                var t = DesignTemplate("preview", design);
                var fallen = SettlementGenerator.FromTemplate(t, Shell.Content, ModuleMaterials.ForSettlement(t.Tier, surface, alien: false, Shell.Content),
                    new TemplateStamp { Ruined = true, Seed = _previewSeed, Surface = surface });
                cells = SettlementCells(fallen);
            }

            ShowPreview(PreviewKind.Ruin, cells);
            _status = string.Format(L("ui.ed.landmark.preview_shown"), _previewSeed, _design.Count, design.Count);
            RebuildUi();
        }

        /// <summary>The design as a whole template of this editor (keep flags and marker settings included).</summary>
        private StructureTemplate DesignTemplate(string key, List<CellJson> cells)
        {
            var t = new StructureTemplate { Key = key, Name = _name, Tier = _tiers[_tier], Kind = ModeName };
            int maxX = 0, maxY = 0, maxZ = 0;
            foreach (var c in cells)
            {
                bool cavity = c.kind == LandmarkRules.CavityKind;
                t.Cells.Add(new TemplateCell
                {
                    X = c.x, Y = c.y, Z = c.z, Kind = string.IsNullOrEmpty(c.kind) ? "block" : c.kind, Id = cavity ? string.Empty : c.id ?? string.Empty,
                    Tint = c.tint, Glow = c.glow, Shape = c.shape, Port = c.port ?? string.Empty, Mode = c.mode, Config = c.config ?? string.Empty,
                    Label = c.label ?? string.Empty, Keep = c.keep,
                });
                maxX = Mathf.Max(maxX, c.x);
                maxY = Mathf.Max(maxY, c.y);
                maxZ = Mathf.Max(maxZ, c.z);
            }

            t.Width = maxX + 1;
            t.Height = maxY + 1;
            t.Length = maxZ + 1;
            return t;
        }
    }
}
