// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the Avatar Designer adds to the character editor's "Figure" tab (#3693; the tab itself — colour rows, gear
    /// preview, the figure — is the editor's own, <c>FaceEditor.Figure.cs</c>, the same in a world's console): the skin's
    /// name and Export, which only the main-menu designer has.
    /// </summary>
    public sealed partial class AvatarEditor
    {
        private const float FigureColW = 600f;

        private InputField _nameInput;

        /// <summary>The name field and Export under the editor's own rows of the Figure tab.</summary>
        private void BuildFigureExtras(Transform card, float y)
        {
            const float x = 24f;
            UiKit.AddKicker(card, x, y, FigureColW, L("ui.avatar.name"));
            _nameInput = UiKit.AddInput(card, x, y + 30f, FigureColW, 44f, _name, v => _name = v ?? string.Empty);
            UiKit.AddTierButton(card, x, y + 86f, 300f, 44f, L("ui.avatar.export"), Export, ButtonTier.Secondary);
        }
    }
}
