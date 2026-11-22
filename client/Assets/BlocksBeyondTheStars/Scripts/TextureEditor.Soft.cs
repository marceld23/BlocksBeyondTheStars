// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's soft tools and "calm when saving", in one small panel at the foot of
    /// the left card (the centre card's rows are full, and its tool bar holds six tools in every language). The pixel work
    /// is <see cref="PixelCanvasModel"/> (<c>PixelCanvasModel.Soft.cs</c>, <see cref="TileFilters"/>); every action here is
    /// one undo step of the model, like the existing buttons.
    /// <list type="bullet">
    /// <item>Soft brush (S) and smoothing brush (M): tools like the brush, chosen here instead of in the tool bar; size from
    /// the brush size, the right button erases softly, symmetry applies.</item>
    /// <item>Smooth all, calm all (a quarter of the way to the mean per press), make seamless (the status says the seam
    /// measure before and after).</item>
    /// <item>Brightness only: for a tile the world tints (<see cref="ChunkMesher.WorldTintMode"/> 1 flora or 4 bark), paint
    /// changes only brightness and the canvas shows the tile in grey. Off unless chosen; greyed out for other tiles.</item>
    /// <item>Calm when saving, 0 to 100 % in steps of 10: the canvas keeps the painting as painted and shows it as it will
    /// be saved; Save, publish, export, submit and the share code get the calmed pixels (<c>TextureEditor.Calm.cs</c>).</item>
    /// </list>
    /// A painted tile is shown exactly as painted in the world (<see cref="GameTextures.ShowsPaintedTile"/>), so its calm is the painter's choice.
    /// </summary>
    public sealed partial class TextureEditor
    {
        /// <summary>The soft panel's inside width, button width and row pitch (left card inside: 320 from x 20).</summary>
        internal const float SoftPanelW = 320f, SoftButtonW = 156f, SoftRow = 52f, SoftCardFoot = 884f;

        private const float SoftKickerH = 32f, SoftCalmLabelH = 28f, SoftListGap = 12f;

        /// <summary>How much of the list's height the panel takes (its own height and the gap above it).</summary>
        private const float SoftPanelH = SoftKickerH + (3f * SoftRow) + SoftCalmLabelH + RowH + SoftListGap;

        private Button _softBrushButton, _smoothButton, _lumaButton;
        private Text _saveCalmValue;

        private void BuildSoftPanel(Transform card)
        {
            float y = SoftCardFoot - SoftPanelH + SoftListGap;
            Kicker(card, 20f, y, SoftPanelW, "ui.tex.soft.title");
            y += SoftKickerH;
            float x2 = 20f + SoftPanelW - SoftButtonW;
            _softBrushButton = SoftButton(card, 20f, y, "ui.tex.soft.brush", "ui.tex.soft.brush_tip", () => SetTool(Tool.SoftBrush));
            _smoothButton = SoftButton(card, x2, y, "ui.tex.soft.smooth", "ui.tex.soft.smooth_tip", () => SetTool(Tool.Smooth));
            y += SoftRow;
            SoftButton(card, 20f, y, "ui.tex.soft.smooth_all", "ui.tex.soft.smooth_all_tip", () => { _model.SmoothAll(); AfterChange(); });
            SoftButton(card, x2, y, "ui.tex.soft.calm_all", "ui.tex.soft.calm_all_tip", () => { _model.CalmAll(); AfterChange(); });
            y += SoftRow;
            SoftButton(card, 20f, y, "ui.tex.soft.seamless", "ui.tex.soft.seamless_tip", MakeSeamless);
            _lumaButton = SoftButton(card, x2, y, "ui.tex.soft.luma", "ui.tex.soft.luma_tip", ToggleLuminance);
            y += SoftRow;

            var label = UiKit.AddText(card, 20f, y, SoftPanelW, 24f, L("ui.tex.soft.save_calm"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, SoftPanelW, UiKit.EditorMinFontSize, 16);
            UiTooltip.Attach(label.gameObject, () => L("ui.tex.soft.save_calm"), () => L("ui.tex.soft.save_calm_tip"));
            label.raycastTarget = true; // the tooltip needs the pointer
            y += SoftCalmLabelH;
            _saveCalmValue = UiKit.AddCycle(card, 20f, y, SoftPanelW, RowH, SaveCalmText(), StepSaveCalm);

            RefreshSoftPanel();
        }

        private Button SoftButton(Transform card, float x, float y, string labelKey, string tipKey, System.Action onClick)
        {
            var button = TierButton(card, x, y, SoftButtonW, RowH, L(labelKey), onClick);
            UiTooltip.Attach(button.gameObject, () => L(labelKey), () => L(tipKey));
            return button;
        }

        /// <summary>The panel follows the open texture and the chosen tool: the soft tools lit when chosen, "brightness only"
        /// lit when on and greyed for a tile the world does not tint, the calm value of the open painting.</summary>
        private void RefreshSoftPanel()
        {
            if (_softBrushButton != null)
            {
                Tint(_softBrushButton, _tool == Tool.SoftBrush);
                Tint(_smoothButton, _tool == Tool.Smooth);
            }

            if (_lumaButton != null && _model != null)
            {
                bool tinted = IsWorldTinted();
                if (!tinted && _model.LuminanceOnly)
                {
                    _model.LuminanceOnly = false; // another texture: the mode is for tinted tiles only
                }

                _lumaButton.interactable = tinted;
                Tint(_lumaButton, _model.LuminanceOnly);
            }

            if (_saveCalmValue != null && _model != null)
            {
                _saveCalmValue.text = IsCalmLocked() ? "—" : SaveCalmText(); // an icon: nothing to calm (StepSaveCalm ignores it)
            }
        }

        /// <summary>True for a block tile the world recolours: plants and tinted crowns (flora, mode 1), trunks and tinted
        /// plant bodies (bark, mode 4) — drawn as the tile's brightness times a world colour, so a painted hue is lost there.</summary>
        private bool IsWorldTinted()
            => _entry != null && _entry.Block != null && _content != null && ChunkMesher.WorldTintMode(_content, _entry.Block.NumericId) != 0;

        /// <summary>An icon never reaches the block atlas, so "calm when saving" stays at 0 for it.</summary>
        private bool IsCalmLocked() => _entry == null || _entry.IsIcon;

        private string SaveCalmText() => string.Format(L("ui.tex.soft.percent"), _model != null ? _model.SaveCalm : 0);

        /// <summary>The cycle's step: 10 % down or up, clamped at 0 and 100 (no wrap: 100 → 0 would throw a painting's whole
        /// calm away on one click). One undo step.</summary>
        private string StepSaveCalm(int direction)
        {
            if (_model != null && !IsCalmLocked())
            {
                _model.SetSaveCalm(_model.SaveCalm + (direction * 10));
                AfterChange();
            }

            return SaveCalmText();
        }

        private void ToggleLuminance()
        {
            if (!IsWorldTinted())
            {
                SetStatus(L("ui.tex.soft.luma_not_tinted"), EditorStatusSeverity.Info);
                return;
            }

            _model.LuminanceOnly = !_model.LuminanceOnly;
            RefreshSoftPanel();
            UploadActiveFrame(); // grey on the canvas while it is on
            if (_model.LuminanceOnly)
            {
                SetStatus(L("ui.tex.soft.luma_on"), EditorStatusSeverity.Info);
            }
        }

        private void MakeSeamless()
        {
            var (before, after) = _model.MakeSeamless();
            AfterChange();
            SetStatus(string.Format(L("ui.tex.soft.seamless_done"), before.ToString("0.0"), after.ToString("0.0")), EditorStatusSeverity.Info);
        }

        /// <summary>The soft brush and the smoothing brush: one undo step per stroke, like the brush (the stroke closes in
        /// <see cref="EndStroke"/>).</summary>
        private void SoftStrokeTo(int x, int y, TexelColor color, bool down)
        {
            if (down || !_stroking)
            {
                _model.BeginSoftStroke();
                _stroking = true;
                _lastX = x;
                _lastY = y;
            }

            if (_tool == Tool.Smooth)
            {
                _model.SmoothStrokeTo(_lastX, _lastY, x, y, _brushSize);
            }
            else
            {
                _model.SoftStrokeTo(_lastX, _lastY, x, y, color, _brushSize);
            }

            _lastX = x;
            _lastY = y;
            UploadActiveFrame();
        }
    }
}
