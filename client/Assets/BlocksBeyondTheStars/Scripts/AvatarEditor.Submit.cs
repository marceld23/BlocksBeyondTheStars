// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Avatar Designer's skin file (#3262) — what Export writes. The file and the picture are
    /// <see cref="AppearanceSubmit"/>'s; since the editor shell (#3693) the designer submits through the character
    /// editor's own Submit (the shell's header button, <c>FaceEditor.Submit.cs</c>, #3319), which sends the very same file.
    /// A player's look has no hair (the suit's helmet covers it); the adopting developer gives the pioneer one with
    /// <c>tools/gen_pioneer_skins.py --hair</c>.
    /// </summary>
    public sealed partial class AvatarEditor
    {
        /// <summary>The designer's current scratch look (the editor's live preview and its Submit read it).</summary>
        private FaceEditor.AppearanceSnapshot Look()
            => AppearanceSubjects.Snapshot(which => _col[which], () => _face, part => _bodyPaint[part]);

        /// <summary>The designer's current look as the skin file (colours as #RRGGBB, paintings as the game stores them).</summary>
        private string SkinFile(string key, bool pretty) => AppearanceSubmit.SkinFile(key, _name, Look(), pretty);
    }
}
