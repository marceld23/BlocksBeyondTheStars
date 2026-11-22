// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The colour-slot palette of the one-block shape designer (<see cref="ShapeSlotPalette"/>). Picking a slot
    /// paints with it and switches to Place; the budget line counts the per-slot boxes the world draws.</summary>
    public sealed partial class ShapeEditor
    {
        private void BuildSlotPalette(Transform card, float x, float y, float w)
        {
            _model.CountSlotBoxes = true;
            Kicker(card, x, y, w, "ui.shape.slot.title");
            ShapeSlotPalette.Build(card, x, y + 30f, w, RowH, L, slot =>
            {
                _model.PaintValue = slot;
                SetRemoving(false);
            });
        }
    }
}
