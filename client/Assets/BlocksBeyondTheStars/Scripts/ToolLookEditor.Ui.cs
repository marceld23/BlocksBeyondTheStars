// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "My tools" on the editor shell (#3693 — the mock's "Formen &amp; Werkzeuge" view, which the tool looks share): the
    /// tools on the left, the layer canvas with its layer strip, the Place | Remove switch, the colours and the helpers in
    /// the centre, the turning preview, "back to the standard look" and the share codes on the right. Save is the shell's.
    /// </summary>
    public sealed partial class ToolLookEditor
    {
        private const float CellPx = 40f, CanvasY = 64f;

        /// <summary>The centre card's right column (switch, colours, helpers).</summary>
        internal const float ColumnX = 452f, ColumnW = CentreW - Inset - ColumnX;

        /// <summary>A helper in a row of two.</summary>
        internal const float HalfW = (ColumnW - RowGap) / 2f;

        private const float SwatchPx = 64f, SwatchPitch = 80f;

        private RectTransform _toolList;
        private Text _budgetLabel, _glowLabel;
        private readonly List<Image> _swatchFrames = new List<Image>();

        /// <summary>Builds the three cards; returns the pad's first stop (the Place tool).</summary>
        private Selectable BuildUi()
        {
            BuildFrame("Tool Look Editor UI", new EditorShellOptions
            {
                TitleKey = "ui.menu.tool_looks",
                SaveSublineKey = "ui.editor.save_sub.tool_looks",
                OnBack = Close,
                OnSave = Save,
            }, inWorld: false);
            BuildToolCard(Frame.AddCard(LeftX, CardY, LeftW, CardH));
            var first = BuildCanvasCard(Frame.AddCard(CentreX, CardY, CentreW, CardH));
            BuildPreviewCard(Frame.AddCard(RightX, CardY, RightW, CardH));
            return first;
        }

        private void BuildToolCard(Transform card)
        {
            const float w = LeftW - (2f * Inset);
            Kicker(card, Inset, 14f, w, "ui.toollook.my_tools");
            // An instruction: normal colour, as many lines as the language needs — the list starts below it (#3263).
            UiKit.AddMeasuredText(card, Inset, 48f, w, L("ui.toollook.note"), 16, UiKit.TextCol, out float noteH);
            float listY = 48f + noteH + 12f;
            _toolList = UiKit.ScrollList(card, Inset, listY, w, CardH - Inset - listY, 4f);
        }

        private void RebuildToolList()
        {
            if (_toolList == null)
            {
                return;
            }

            for (int i = _toolList.childCount - 1; i >= 0; i--)
            {
                Destroy(_toolList.GetChild(i).gameObject);
            }

            const float rowW = LeftW - (2f * Inset) - 16f;
            foreach (var tool in _tools)
            {
                var row = AddListRow(_toolList, rowW);
                var item = tool;
                bool own = Shell?.Settings != null && Shell.Settings.GetToolLook(tool.Key).Length > 0;
                string label = (own ? "★ " : string.Empty) + ToolName(tool);
                var button = UiKit.AddTierButton(row, 0f, 0f, rowW, RowH, label, () => PickTool(item),
                    tool == _tool ? ButtonTier.Secondary : ButtonTier.Quiet, "item_" + tool.Key);
                UiKit.FitButtonLabel(button.GetComponentInChildren<Text>(true));
            }
        }

        private Selectable BuildCanvasCard(Transform card)
        {
            BuildLayerHeader(card, CentreW);
            var canvasGo = new GameObject("LookCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(card, false);
            CanvasRt = UiKit.Place(canvasGo, Inset, CanvasY, ToolLook.SizeX * CellPx, ToolLook.SizeZ * CellPx);
            canvasGo.AddComponent<RawImage>().texture = Tex;
            float canvasW = ToolLook.SizeX * CellPx, canvasH = ToolLook.SizeZ * CellPx;
            var hand = UiKit.AddText(card, Inset, CanvasY + canvasH + 4f, canvasW, 24f, L("ui.toollook.hand_here"), 16, UiKit.CyanDim,
                TextAnchor.MiddleCenter);
            UiKit.FitLabelSingleLine(hand, canvasW, UiKit.EditorMinFontSize, 16);
            float stripX = Inset + canvasW + 16f;
            BuildLayerStrip(card, stripX, CanvasY, ColumnX - 16f - stripX, canvasH);

            var tools = BuildToolSwitch(card, ColumnX, CanvasY, ColumnW);
            BuildPalette(card, CanvasY + RowH + 12f);
            return tools.Buttons[0];
        }

        /// <summary>Fifteen colours (each can glow), the glow switch and the helpers, under the Place | Remove switch.</summary>
        private void BuildPalette(Transform card, float y)
        {
            Kicker(card, ColumnX, y, ColumnW, "ui.toollook.colours");
            y += 32f;
            for (int i = 1; i < ToolLook.Palette.Count; i++)
            {
                int index = i;
                float sx = ColumnX + (((i - 1) % 5) * SwatchPitch), sy = y + (((i - 1) / 5) * SwatchPitch);
                var frame = UiKit.AddImage(card, sx, sy, SwatchPx, SwatchPx, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.55f));
                var swatch = UiKit.AddImage(frame.transform, 5f, 5f, SwatchPx - 10f, SwatchPx - 10f, UiKit.SolidSprite, PaletteColor(i));
                swatch.raycastTarget = false;
                var button = frame.gameObject.AddComponent<Button>();
                button.targetGraphic = frame;
                button.onClick.AddListener(() => SetColor(index));
                _swatchFrames.Add(frame);
            }

            y += (3f * SwatchPitch) + 4f;
            TierButton(card, ColumnX, y, ColumnW, RowH, L("ui.toollook.glow_toggle"), () => Do(() => _model.ToggleGlow(_color)));
            _glowLabel = UiKit.AddText(card, ColumnX, y + RowH + 4f, ColumnW, 24f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += RowH + 40f;
            TierButton(card, ColumnX, y, HalfW, RowH, L("ui.shape.custom.copy_below"), () => Do(_model.CopyLayerBelow));
            TierButton(card, ColumnX + HalfW + RowGap, y, HalfW, RowH, L("ui.shape.custom.mirror_x"), () => Do(_model.MirrorX));
            y += RowH + RowGap;
            TierButton(card, ColumnX, y, HalfW, RowH, L("ui.shape.custom.clear_layer"), () => Do(_model.ClearLayer));
            TierButton(card, ColumnX + HalfW + RowGap, y, HalfW, RowH, L("ui.shape.custom.clear_all"), () => Do(_model.ClearAll));
            y += RowH + 16f;
            _budgetLabel = UiKit.AddText(card, ColumnX, y, ColumnW, 24f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }

        private void BuildPreviewCard(Transform card)
        {
            const float w = RightW - (2f * Inset);
            Kicker(card, Inset, 14f, w, "ui.shape.custom.preview");
            var view = new GameObject("ToolLookPreviewView", typeof(RectTransform));
            view.transform.SetParent(card, false);
            UiKit.Place(view, Inset, 48f, w, 560f);
            PreviewView = view.AddComponent<RawImage>();

            float y = 624f;
            TierButton(card, Inset, y, w, RowH, L("ui.toollook.use_standard"), AskUseStandard, ButtonTier.Danger);
            y += RowH + RowGap;
            if (EditorShell.DesktopFeatures)
            {
                float half = (w - RowGap) / 2f;
                TierButton(card, Inset, y, half, RowH, L("ui.tex.copy_code"), CopyCode);
                TierButton(card, Inset + half + RowGap, y, half, RowH, L("ui.tex.paste_code"), PasteCode);
            }

            y += RowH + 16f;
            UiKit.AddMeasuredText(card, Inset, y, w, L("ui.toollook.rules"), 16, UiKit.TextCol, out _); // an instruction (#3263)
        }

        protected override void RefreshLabels()
        {
            ShowLayerLine(_model.Layer, ToolLook.SizeY);
            if (_budgetLabel != null)
            {
                int parts = _model.PartsUsed();
                _budgetLabel.text = string.Format(L("ui.toollook.budget"), parts, ToolLook.MaxParts);
                _budgetLabel.color = parts > ToolLook.MaxParts ? UiKit.DangerCol : UiKit.CyanDim;
            }

            for (int i = 0; i < _swatchFrames.Count; i++)
            {
                _swatchFrames[i].color = i + 1 == _color ? Color.white : new Color(0f, 0f, 0f, 0.55f);
            }

            if (_glowLabel != null)
            {
                _glowLabel.text = L(_model.Glows(_color) ? "ui.toollook.glow_on" : "ui.toollook.glow_off");
                UiKit.FitLabelSingleLine(_glowLabel, ColumnW, UiKit.EditorMinFontSize, 16);
            }
        }
    }
}
