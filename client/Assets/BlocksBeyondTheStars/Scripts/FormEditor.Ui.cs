// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The form editor's screen on the editor shell (#3693, the mock's "Formen &amp; Werkzeuge" view): "My forms" and the
    /// preview material on the left, the layer canvas with its layer strip, the Place | Remove switch and the helpers in
    /// the centre, the preview, material, dye, size in blocks and the name on the right. Save and New are in the shell's
    /// header.
    /// </summary>
    public sealed partial class FormEditor
    {
        private const float CanvasPx = 600f;

        // Centre card rows (card coordinates).
        private const float CanvasY = 64f, ToolRowY = CanvasY + CanvasPx + 12f, HelperRow1Y = ToolRowY + RowH + RowGap,
            HelperRow2Y = HelperRow1Y + RowH + RowGap, BudgetY = HelperRow2Y + RowH + 8f;

        /// <summary>The centre card's row width (the card minus its margins).</summary>
        internal const float CentreRowW = CentreW - (2f * Inset);

        /// <summary>A helper button in a row of four.</summary>
        internal const float HelperW = (CentreRowW - (3f * RowGap)) / 4f;

        private RawImage _canvasImg;
        private InputField _nameInput;
        private Text _budgetLabel, _problemLabel, _footprintLabel, _materialLabel;
        private RectTransform _libraryContent;
        private Button _gridButton;

        /// <summary>Builds the three cards; returns the pad's first stop (the Place tool).</summary>
        private Selectable BuildUi()
        {
            BuildFrame("Form Editor UI", new EditorShellOptions
            {
                TitleKey = "ui.menu.form_editor",
                SaveSublineKey = "ui.editor.save_sub.form",
                OnBack = Close,
                OnSave = Save,
                OnNew = NewForm,
            }, inWorld: false);
            BuildLibraryCard(Frame.AddCard(LeftX, CardY, LeftW, CardH));
            var first = BuildCanvasCard(Frame.AddCard(CentreX, CardY, CentreW, CardH));
            BuildRightCard(Frame.AddCard(RightX, CardY, RightW, CardH));
            return first;
        }

        // ---------------------------------------------------------------- left card: my forms + the preview material

        private void BuildLibraryCard(Transform card)
        {
            const float w = LeftW - (2f * Inset);
            Kicker(card, Inset, 14f, w, "ui.shape.custom.library");
            // The card is shared (#1969): the forms above, the preview material below — each scrolls alone.
            _libraryContent = UiKit.ScrollList(card, Inset, 48f, w, 436f, 4f);
            RebuildLibrary();
            UiKit.AddImage(card, Inset, 496f, w, 2f, UiKit.SolidSprite, new Color(1f, 1f, 1f, 0.12f));
            BuildMaterialList(card, 508f, CardH - 508f - 24f);
        }

        /// <summary>EVERY saved form, each with its size and a delete button that asks first (#3690).</summary>
        private void RebuildLibrary()
        {
            if (_libraryContent == null)
            {
                return;
            }

            for (int i = _libraryContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_libraryContent.GetChild(i).gameObject);
            }

            const float rowW = LeftW - (2f * Inset) - 16f, deleteW = 44f;
            foreach (var entry in CustomShapeLibrary.List())
            {
                var row = AddListRow(_libraryContent, rowW);
                string name = entry.Name, voxels = entry.Voxels;
                int cells = CustomShape.CellCount(voxels);
                string label = cells > 1 ? name + "  · " + string.Format(L("ui.form.blocks"), cells) : name;
                TierButton(row, 0f, 0f, rowW - deleteW - 6f, RowH, label, () => PickEntry(name, voxels));
                TierButton(row, rowW - deleteW, 0f, deleteW, RowH, "✕", () => AskDeleteEntry(name), ButtonTier.Danger);
            }
        }

        /// <summary>The lower part of the left card (#1969): every material a form can be made of, with its real tile in
        /// front of the name, under section headers, filtered by the search field. It only dresses the PREVIEW: in the
        /// world a form takes the material it is made from.</summary>
        private void BuildMaterialList(Transform card, float top, float height)
        {
            const float w = LeftW - (2f * Inset);
            Kicker(card, Inset, top, w, "ui.form.material");
            UiKit.AddInput(card, Inset, top + 32f, w, RowH, string.Empty, v => _materialList?.Rebuild(v), L("ui.tex.search"), 40, 16);
            var content = UiKit.ScrollList(card, Inset, top + 84f, w, height - 84f);

            _materialRows.Clear();
            foreach (var block in _materials)
            {
                _materialRows.Add(new EditorPaletteKit.Entry
                {
                    Id = block.Key,
                    Label = MaterialLabel(block),
                    Kind = "block",
                    Group = MaterialGroup(block),
                    Color = new Color(0.4f, 0.5f, 0.6f),
                    Icon = EditorPaletteKit.TileSprite(_atlas, block.NumericId.Value),
                });
            }

            var shell = Shell != null ? Shell : FindAnyObjectByType<AppShell>();
            _materialList = new PaletteListUi(shell, content, _materialRows, _materialIndex, PaletteRowStyle.Tiles, w - 16f);
            _materialList.OnSelected = index =>
            {
                // Rebuild reports the standing selection on every keystroke of the search; only a real change loads a tile.
                if (index >= 0 && index < _materials.Count && index != _materialIndex)
                {
                    _materialIndex = index;
                    ApplyMaterial();
                }
            };
            _materialList.Rebuild(string.Empty);
        }

        // ---------------------------------------------------------------- centre card: the layer canvas

        private Selectable BuildCanvasCard(Transform card)
        {
            BuildLayerHeader(card, CentreW);
            var frame = UiKit.AddImage(card, Inset, CanvasY, CanvasPx, CanvasPx, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.35f));
            frame.raycastTarget = false;
            var canvasGo = new GameObject("LayerCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(frame.transform, false);
            CanvasRt = UiKit.Place(canvasGo, 0f, 0f, CanvasPx, CanvasPx);
            _canvasImg = canvasGo.AddComponent<RawImage>();
            float stripX = Inset + CanvasPx + 16f;
            BuildLayerStrip(card, stripX, CanvasY, CentreW - Inset - stripX, CanvasPx);

            var tools = BuildToolSwitch(card, Inset, ToolRowY, 380f);
            _gridButton = TierButton(card, Inset + 380f + RowGap, ToolRowY, 200f, RowH, L("ui.shape.custom.grid_toggle"), ToggleGrid);
            BuildHelperRows(card);
            _budgetLabel = UiKit.AddText(card, Inset, BudgetY, CentreRowW, 24f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _problemLabel = UiKit.AddText(card, Inset, BudgetY + 26f, CentreRowW, 40f, string.Empty, 15, UiKit.Warn, TextAnchor.UpperLeft);
            _problemLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            return tools.Buttons[0];
        }

        /// <summary>The helpers under the canvas: copy / clear / fill / clear all, mirror both ways and the six shifts. Each
        /// is one undo step.</summary>
        private void BuildHelperRows(Transform card)
        {
            float x = Inset;
            TierButton(card, x, HelperRow1Y, HelperW, RowH, L("ui.shape.custom.copy_below"), () => Do(_model.CopyLayerBelow));
            TierButton(card, x + (HelperW + RowGap), HelperRow1Y, HelperW, RowH, L("ui.shape.custom.clear_layer"), () => Do(_model.ClearLayer));
            TierButton(card, x + (2f * (HelperW + RowGap)), HelperRow1Y, HelperW, RowH, L("ui.form.fill_layer"), () => Do(_model.FillLayer));
            TierButton(card, x + (3f * (HelperW + RowGap)), HelperRow1Y, HelperW, RowH, L("ui.shape.custom.clear_all"), () => Do(_model.ClearAll));

            TierButton(card, x, HelperRow2Y, HelperW, RowH, L("ui.shape.custom.mirror_x"), () => Do(() => _model.Mirror(alongX: true)));
            TierButton(card, x + HelperW + RowGap, HelperRow2Y, HelperW, RowH, L("ui.shape.custom.mirror_z"), () => Do(() => _model.Mirror(alongX: false)));
            var shifts = new (string Glyph, int Dx, int Dy, int Dz)[]
            {
                ("←", -1, 0, 0), ("→", 1, 0, 0), ("↑", 0, 0, 1), ("↓", 0, 0, -1), ("⤒", 0, 1, 0), ("⤓", 0, -1, 0),
            };
            float shiftX = x + (2f * (HelperW + RowGap));
            float shiftW = (CentreRowW - (shiftX - x) - (5f * RowGap)) / 6f;
            for (int i = 0; i < shifts.Length; i++)
            {
                var s = shifts[i];
                TierButton(card, shiftX + (i * (shiftW + RowGap)), HelperRow2Y, shiftW, RowH, s.Glyph, () => Do(() => _model.Shift(s.Dx, s.Dy, s.Dz)));
            }
        }

        // ---------------------------------------------------------------- right card: preview, material, size, name

        private void BuildRightCard(Transform card)
        {
            const float w = RightW - (2f * Inset), viewW = 300f;
            Kicker(card, Inset, 14f, viewW, "ui.shape.custom.preview");
            var view = new GameObject("FormPreviewView", typeof(RectTransform));
            view.transform.SetParent(card, false);
            UiKit.Place(view, Inset, 48f, viewW, viewW);
            PreviewView = view.AddComponent<RawImage>();
            BuildMaterialAndDye(card, Inset + viewW + 20f, RightW - Inset - (Inset + viewW + 20f));
            BuildFootprint(card, w);
            BuildNameAndMore(card, w);
        }

        /// <summary>Material + dye: what the form will look like — it takes the material it is made of in the world. The
        /// material is picked in the list on the left (#1969); here stands only its name.</summary>
        private void BuildMaterialAndDye(Transform card, float x, float w)
        {
            Kicker(card, x, 48f, w, "ui.form.material");
            _materialLabel = UiKit.AddText(card, x, 76f, w, 28f, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            Kicker(card, x, 116f, w, "ui.form.dye");
            float swatchW = (w - RowGap) / 2f;
            for (int i = 0; i < Dyes.Length; i++)
            {
                int index = i;
                var swatch = UiKit.AddImage(card, x + ((i % 2) * (swatchW + RowGap)), 148f + ((i / 2) * 48f), swatchW, 40f, UiKit.SolidSprite, Dyes[i]);
                var button = swatch.gameObject.AddComponent<Button>();
                button.targetGraphic = swatch;
                button.onClick.AddListener(() =>
                {
                    _dyeIndex = index;
                    ApplyMaterial();
                });
            }
        }

        /// <summary>Footprint: how many blocks the form spans (#1961).</summary>
        private void BuildFootprint(Transform card, float w)
        {
            float y = 364f;
            Kicker(card, Inset, y, w, "ui.form.footprint_title");
            _footprintLabel = UiKit.AddText(card, Inset, y + 30f, w, 24f, string.Empty, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 62f;
            FootprintRow(card, y, "ui.form.width", 1, 0, 0);
            FootprintRow(card, y + RowH + RowGap, "ui.form.height", 0, 1, 0);
            FootprintRow(card, y + (2f * (RowH + RowGap)), "ui.form.length", 0, 0, 1);
            UiKit.AddMeasuredText(card, Inset, y + (3f * (RowH + RowGap)) + 4f, w, L("ui.form.footprint_note"), 16, UiKit.TextCol, out _);
        }

        private void FootprintRow(Transform card, float y, string labelKey, int dw, int dh, int dl)
        {
            var label = UiKit.AddText(card, Inset, y, 300f, RowH, L(labelKey), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, 300f, UiKit.EditorMinFontSize, 16);
            TierButton(card, Inset + 316f, y, 56f, RowH, "−", () => StepFootprint(-dw, -dh, -dl));
            TierButton(card, Inset + 316f + 56f + RowGap, y, 56f, RowH, "+", () => StepFootprint(dw, dh, dl));
        }

        /// <summary>The name field and the side actions: duplicate, and the share codes where the clipboard works (not in
        /// the browser — <see cref="EditorShell.DesktopFeatures"/>).</summary>
        private void BuildNameAndMore(Transform card, float w)
        {
            const float y = 700f;
            Kicker(card, Inset, y, w, "ui.shape.custom.name");
            _nameInput = UiKit.AddInput(card, Inset, y + 28f, w, RowH, _name, v =>
            {
                _name = v;
                Changed();
            }, L("ui.shape.custom.name_hint"), 24, 18);
            float bw = (w - (2f * RowGap)) / 3f, by = y + 28f + RowH + 16f;
            TierButton(card, Inset, by, bw, RowH, L("ui.form.duplicate"), Duplicate);
            if (EditorShell.DesktopFeatures)
            {
                TierButton(card, Inset + bw + RowGap, by, bw, RowH, L("ui.shape.custom.export"), CopyCode);
                TierButton(card, Inset + (2f * (bw + RowGap)), by, bw, RowH, L("ui.shape.custom.import"), PasteCode);
            }
        }

        // ---------------------------------------------------------------- labels

        protected override void RefreshLabels()
        {
            ShowLayerLine(_model.Layer, _model.SizeY);
            if (_footprintLabel != null)
            {
                _footprintLabel.text = string.Format(L("ui.form.footprint"), _model.Width, _model.Height, _model.Length, _model.Cells);
                UiKit.FitLabelSingleLine(_footprintLabel, RightW - (2f * Inset), UiKit.EditorMinFontSize, 16);
            }

            if (_budgetLabel != null)
            {
                int boxes = _model.MaxBoxesUsed();
                _budgetLabel.text = string.Format(L(_model.Cells > 1 ? "ui.form.budget_per_block" : "ui.shape.custom.budget"), boxes, CustomShape.MaxBoxes);
                _budgetLabel.color = boxes > CustomShape.MaxBoxes ? UiKit.DangerCol : UiKit.CyanDim;
            }

            if (_problemLabel != null)
            {
                _problemLabel.text = ProblemText();
            }

            if (_gridButton != null)
            {
                _gridButton.interactable = _model.Cells == 1; // a form over several blocks is always fine-grained
            }
        }

        /// <summary>Six outward quads with planar texture coordinates in BLOCK units — the tile repeats once per
        /// block, exactly as the form shows its material in the world.</summary>
        private static void AddBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 lo, Vector3 hi)
        {
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int axis)
            {
                int i = verts.Count;
                foreach (var p in new[] { a, b, c, d })
                {
                    verts.Add(p);
                    uvs.Add(axis == 1 ? new Vector2(p.x, p.z) : axis == 0 ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y));
                }

                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            Quad(new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, hi.y, lo.z), 1);
            Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, lo.y, lo.z), new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, lo.y, hi.z), 1);
            Quad(new Vector3(hi.x, lo.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(hi.x, lo.y, hi.z), 0);
            Quad(new Vector3(lo.x, lo.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(lo.x, lo.y, lo.z), 0);
            Quad(new Vector3(hi.x, lo.y, hi.z), new Vector3(hi.x, hi.y, hi.z), new Vector3(lo.x, hi.y, hi.z), new Vector3(lo.x, lo.y, hi.z), 2);
            Quad(new Vector3(lo.x, lo.y, lo.z), new Vector3(lo.x, hi.y, lo.z), new Vector3(hi.x, hi.y, lo.z), new Vector3(hi.x, lo.y, lo.z), 2);
        }
    }
}
