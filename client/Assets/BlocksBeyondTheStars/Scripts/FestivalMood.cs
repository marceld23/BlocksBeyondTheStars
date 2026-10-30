// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Shared.Festivals;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The festival mood on this client (#3004): which festival effects the scene scripts (Sky, AmbientParticles,
    /// ShootingStars, ClientMusic) read right now. In a game that is the server's list (<c>ServerRules.ActiveFestivals</c>,
    /// primary first) — the server is the truth, a forced <c>/festival</c> shows on any date; on the main menu it is the
    /// client's own calendar over the bundled file. Everything here is cosmetic; the player's "Festival mood" setting
    /// switches it off as a whole.
    /// </summary>
    public static class FestivalMood
    {
        /// <summary>Mirror of <c>ClientSettings.FestivalMood</c> for the scene scripts (set at load and from the settings screen).</summary>
        public static bool Enabled = true;

        /// <summary>The warm wash a positive <see cref="FestivalEffects.Warmth"/> pulls the grade toward.</summary>
        public static readonly Color WarmTint = new Color(1f, 0.92f, 0.80f);

        /// <summary>The cool wash a negative warmth pulls the grade toward (the star festivals' night blue).</summary>
        public static readonly Color CoolTint = new Color(0.84f, 0.90f, 1f);

        /// <summary>The primary festival's effects in a joined game, or null (none on, no effects, mood off, not in a game).</summary>
        public static FestivalEffects Effects(GameBootstrap game)
        {
            if (!Enabled || game == null || game.Rules == null)
            {
                return null;
            }

            return FestivalCard.PrimaryEffects(game.Content, game.Rules.ActiveFestivals);
        }

        /// <summary>The primary festival's effects on the main menu (the client's calendar, today in UTC), or null.</summary>
        public static FestivalEffects MenuEffects(AppShell shell)
        {
            if (!Enabled || shell == null || shell.Settings == null || !shell.Settings.Festivals || shell.Content == null)
            {
                return null;
            }

            var active = FestivalCard.Active(shell.Content, DateTime.UtcNow);
            return active.Count > 0 ? active[0].Festival.Effects : null;
        }

        /// <summary>Parses a <c>#rrggbb</c> fog tint; false for an empty or malformed one.</summary>
        public static bool TryParseTint(string hex, out Color colour)
        {
            colour = Color.white;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out colour);
        }
    }
}
