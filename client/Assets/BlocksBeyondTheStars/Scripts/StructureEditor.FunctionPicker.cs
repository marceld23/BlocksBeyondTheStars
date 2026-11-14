// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The module function as a pick list (#3600): with the farm functions a settlement module has 23 of them, too many to step
    /// through one click at a time — "…" opens the list, a click picks one.
    /// </summary>
    public sealed partial class StructureEditor
    {
        private GameObject _functionPicker;

        private void OpenFunctionPicker()
        {
            CloseFunctionPicker();
            var list = Functions();
            const float PW = 420f, RowHeight = 32f;
            float ph = Mathf.Min(720f, 96f + (list.Length * (RowHeight + 2f)));
            var (overlay, panel) = UiKit.AddModalOverlay(_canvas.transform, 0f, 0f, PW, ph);
            _functionPicker = overlay;
            UiKit.AddText(panel, 20f, 12f, PW - 140f, 30f, L("ui.struct.function"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddFitButton(panel, PW - 120f, 12f, 100f, 30f, L("ui.kit.close"), CloseFunctionPicker);
            var rows = UiKit.ScrollList(panel, 20f, 56f, PW - 40f, ph - 76f);
            foreach (string f in list)
            {
                string function = f;
                string label = (function == _function ? "▶ " : string.Empty) + FunctionLabel(function);
                UiKit.AddFitButton(rows, 0f, 0f, PW - 60f, RowHeight, label, () =>
                {
                    _function = function;
                    SyncRole();
                    CloseFunctionPicker();
                    RebuildUi(); // #3523: a city role makes it a city module — the palette and the stamping panel follow
                });
            }
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
