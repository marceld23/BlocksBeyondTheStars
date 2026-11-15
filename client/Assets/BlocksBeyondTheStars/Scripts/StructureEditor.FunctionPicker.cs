// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The module function as a pick list (#3600): with the farm functions a settlement module has 23 of them, too many to step
    /// through one click at a time — "…" opens the list, a click picks one. Centred on the screen (#3692: it opened at the top
    /// left corner), rows and Close at the template's 44 px; Esc / pad B close it (<see cref="CloseTopLayer"/>).
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>The function picker's width and its rows' height.</summary>
        internal const float FunctionPickerW = 480f, FunctionRowH = 44f;

        private GameObject _functionPicker;

        private void OpenFunctionPicker()
        {
            CloseFunctionPicker();
            var list = Functions();
            const float pw = FunctionPickerW;
            float ph = Mathf.Min(760f, 96f + (list.Length * (FunctionRowH + 2f)));
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, (EditorShellLayout.CanvasW - pw) / 2f,
                (EditorShellLayout.CanvasH - ph) / 2f, pw, ph);
            _functionPicker = overlay;
            var title = UiKit.AddText(panel, 20f, 14f, pw - 200f, 40f, L("ui.struct.function"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(title, pw - 200f, UiKit.EditorMinFontSize, 20);
            var rows = UiKit.ScrollList(panel, 20f, 68f, pw - 40f, ph - 88f);
            foreach (string f in list)
            {
                string function = f;
                string label = (function == _function ? "▶ " : string.Empty) + FunctionLabel(function);
                UiKit.AddFitButton(rows, 0f, 0f, pw - 60f, FunctionRowH, label, () =>
                {
                    _function = function;
                    SyncRole();
                    CloseFunctionPicker();
                    RebuildUi(); // #3523: a city role makes it a city module — the palette and the stamping panel follow
                });
            }

            // Last: the pad lands on the list, not on Close.
            UiKit.AddTierButton(panel, pw - 170f, 12f, 150f, FunctionRowH, L("ui.kit.close"), CloseFunctionPicker, ButtonTier.Quiet, "btn_close");
            UiKit.OpenModal(overlay);
        }

        private void CloseFunctionPicker()
        {
            if (_functionPicker != null)
            {
                Object.Destroy(_functionPicker);
                _functionPicker = null;
            }
        }
    }
}
