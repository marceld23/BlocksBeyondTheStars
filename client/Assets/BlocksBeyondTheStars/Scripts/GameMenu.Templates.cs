// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the in-game appearance screen adds to the shared character editor beyond the outfits: the skin templates of
    /// this world in the outfit column (#3321 — the free ones and those a calendar door unlocked here, <c>skin:&lt;key&gt;</c>),
    /// the HUD mode with the frame templates of this world (#3318, <c>hudframe:&lt;key&gt;</c>) and the editor's Submit
    /// button (#3319). Wearing a skin template is wearing an outfit: the colours and the four paintings go down their
    /// normal paths — the figure and the settings at once, the paintings through the paced appearance queue (the server
    /// takes one every 2 s) — and the face stays the player's own.
    /// </summary>
    public sealed partial class GameMenu
    {
        private void ConfigureTemplatesAndHud(FaceEditor editor)
        {
            editor.SkinTemplateNames = () => AppearanceTemplates.Names(WorldSkins(), s => s.NameKey, L);
            editor.OnWearSkinTemplate = WearSkinTemplate;
            editor.SubmitSettings = Settings;
            editor.HudHost = new HudFrameEditorHost
            {
                Settings = Settings,
                Localize = L,
                Templates = () => AppearanceTemplates.OfferedFrames(Game?.Content, Unlocked),
            };
        }

        /// <summary>Whether this world unlocked the cosmetic <paramref name="key"/> (<c>skin:christmas</c>).</summary>
        private bool Unlocked(string key) => Game?.Network != null && Game.Network.HasCosmetic(key);

        private List<SkinDefinition> WorldSkins() => AppearanceTemplates.OfferedSkins(Game?.Content, Unlocked);

        /// <summary>Puts a skin template on: its colours (an empty entry keeps the player's own), its four paintings, the
        /// player's face. Returns the outfit column's status line.</summary>
        private string WearSkinTemplate(int index)
        {
            var skins = WorldSkins();
            if (Settings == null || index < 0 || index >= skins.Count)
            {
                return string.Empty;
            }

            var look = Settings.CaptureOutfit(string.Empty);
            AppearanceTemplates.ApplySkin(skins[index], look);
            Settings.ApplyOutfit(look);
            ApplyAppearance(); // colours: figure + save + one message
            for (int part = 0; part < BodyPaintKit.PartCount; part++)
            {
                ApplyBodyPaint(part, look.GetBodyPaint(part)); // each painting through the paced queue
            }

            return L("ui.avatar.skin_worn").Replace("{name}", L(skins[index].NameKey));
        }
    }
}
