// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The colour-slot palette of the form editor, at the right end of its tool row
    /// (<see cref="ShapeSlotPalette"/>). Picking a slot paints with it and switches to Place; the budget line and the
    /// "too detailed" check count the per-slot boxes the world draws.</summary>
    public sealed partial class FormEditor
    {
        private void BuildSlotPalette(Transform card, float x, float y, float w)
        {
            _model.CountSlotBoxes = true;
            BuildSlotButtons(card, x, y, w, slot => _model.PaintValue = slot);
        }
    }
}
