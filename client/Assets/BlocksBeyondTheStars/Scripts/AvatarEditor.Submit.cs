// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Avatar Designer's skin file (#3262) — what Export writes. The file and the picture are
    /// <see cref="AppearanceSubmit"/>'s; since the editor shell (#3693) the designer submits through the character
    /// editor's own Submit (the shell's header button, <c>FaceEditor.Submit.cs</c>, #3319), which sends the very same file.
    /// A player's look has no hair (the suit's helmet covers it); the adopting developer gives the pioneer one with
    /// <c>tools/gen_pioneer_skins.py --hair</c>.
    /// </summary>
    public sealed partial class AvatarEditor : IPlaytestWorkSource
    {
        /// <summary>The look on the bench for a playtest report ("send my work along"): the skin file Export writes.</summary>
        public FeedbackAttachment WorkForPlaytest()
        {
            string key = CreationSubmission.SafeKey(EditorSceneKit.Slug(_name), CreationSubmission.KindAvatar);
            return PlaytestWork.OfCreation(CreationSubmission.KindAvatar, key, new CreationPayload().Add("skin.json", SkinFile(key, pretty: false)));
        }

        /// <summary>The designer's current scratch look (the editor's live preview and its Submit read it).</summary>
        private FaceEditor.AppearanceSnapshot Look()
            => AppearanceSubjects.Snapshot(which => _col[which], () => _face, part => _bodyPaint[part]);

        /// <summary>The designer's current look as the skin file (colours as #RRGGBB, paintings as the game stores them).</summary>
        private string SkinFile(string key, bool pretty) => AppearanceSubmit.SkinFile(key, _name, Look(), pretty);
    }
}
