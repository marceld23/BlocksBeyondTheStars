// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Festivals;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Festivals;
using BlocksBeyondTheStars.Shared.Localization;
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

        /// <summary>The leading festival's effects in a joined game, or null (none on, no effects, mood off, not in a game).
        /// The server's list is already this player's festivals in order (#3021/#3022); the mood shows on a festival's core
        /// days only, unless it was switched on by hand.</summary>
        public static FestivalEffects Effects(GameBootstrap game)
        {
            if (!Enabled || game == null || game.Rules == null)
            {
                return null;
            }

            // Sky, motes, shooting stars and music ask every frame: the answer only changes with a new rules message (a new
            // array), the forced flag or the UTC day, so it is worked out once per change, not per call.
            var keys = game.Rules.ActiveFestivals;
            var day = DateTime.UtcNow.Date;
            if (!ReferenceEquals(keys, _cachedKeys) || !ReferenceEquals(game.Content, _cachedContent)
                || game.Rules.FestivalForced != _cachedForced || day != _cachedDay)
            {
                _cachedKeys = keys;
                _cachedContent = game.Content;
                _cachedForced = game.Rules.FestivalForced;
                _cachedDay = day;
                _cachedEffects = FestivalCard.PrimaryEffects(game.Content, keys, day, _cachedForced);
            }

            return _cachedEffects;
        }

        private static string[] _cachedKeys;
        private static GameContent _cachedContent;
        private static bool _cachedForced;
        private static DateTime _cachedDay;
        private static FestivalEffects _cachedEffects;

        /// <summary>The leading celebrated festival's effects on the main menu (the client's calendar, today in UTC, core days
        /// only), or null.</summary>
        public static FestivalEffects MenuEffects(AppShell shell)
        {
            if (!Enabled || shell == null || shell.Settings == null || shell.Content == null)
            {
                return null;
            }

            var active = FestivalCard.Active(shell.Content, DateTime.UtcNow, Celebrated(shell.Content, shell.Settings));
            return FestivalCard.PrimaryEffects(shell.Content, active.Select(l => l.Festival.Key).ToList(), DateTime.UtcNow, forced: false);
        }

        /// <summary>#3021: the player's preset from the settings.</summary>
        public static FestivalPreset Preset(ClientSettings settings)
            => FestivalSelection.Parse(settings != null ? settings.FestivalPreset : 0);

        /// <summary>#3021: the player's own list from the settings.</summary>
        public static string[] Custom(ClientSettings settings) => FestivalSelection.ParseCustom(settings?.FestivalCustom);

        /// <summary>#3021: the language the "my language" part follows — the game's chosen language.</summary>
        public static string LocaleCode(ClientSettings settings) => GameLocaleExtensions.Parse(settings?.Language).Code();

        /// <summary>#3021: the festivals this player celebrates ("My festivals"), resolved like the server does.</summary>
        public static HashSet<string> Celebrated(GameContent content, ClientSettings settings)
            => content == null
                ? new HashSet<string>()
                : FestivalSelection.Resolve(content.Festivals, Preset(settings), LocaleCode(settings), Custom(settings));

        /// <summary>#3021: tells the server of a joined game about a changed selection (nothing to do on the menu).</summary>
        public static void SendPrefs(ClientSettings settings)
        {
            var game = UnityEngine.Object.FindAnyObjectByType<GameBootstrap>();
            game?.Network?.SendFestivalPrefs((int)Preset(settings), Custom(settings), LocaleCode(settings));
        }

        /// <summary>Parses a <c>#rrggbb</c> fog tint; false for an empty or malformed one.</summary>
        public static bool TryParseTint(string hex, out Color colour)
        {
            colour = Color.white;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out colour);
        }
    }
}
