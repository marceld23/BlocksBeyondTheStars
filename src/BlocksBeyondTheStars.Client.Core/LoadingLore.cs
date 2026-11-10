// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Justus' story lines on the black world-entry veil (#3450, moved there by #3460): "The Sounders come and go…", "Humans
    /// travel into space…", "Settlements are built in space…", "Everything begins…", "Everything ends…" — and "You awaken!",
    /// as the Vega prologue opens in the pilot's seat. They replace the veil's "Loading world…" footer, only on the first
    /// entry into a world (not on landings, station boarding or entering the ship).
    /// <para>The veil has no progress value, only time and "the world is ready", and it never waits for the story: the
    /// first line comes up with the veil, the next one every <see cref="MinLineSeconds"/> while the world loads (up to the
    /// fifth), and "You awaken!" the moment the world is ready — however far the story got. A fast world therefore reads
    /// e.g. the first line and then "You awaken!"; the story never steps back.</para>
    /// Pure, so the headless suite pins the pacing; the Unity veil (<c>WorldLoadingOverlay</c>) only times the line on
    /// show and draws it.
    /// </summary>
    public static class LoadingLore
    {
        /// <summary>Key prefix of the lines: <c>ui.loading.lore.1</c> … <c>ui.loading.lore.6</c>.</summary>
        public const string KeyPrefix = "ui.loading.lore.";

        /// <summary>The value of "no line shown yet" — what the veil starts with.</summary>
        public const int None = 0;

        /// <summary>The first line, "The Sounders come and go…".</summary>
        public const int First = 1;

        /// <summary>The last line, "You awaken!" — shown once the world is ready.</summary>
        public const int Last = 6;

        /// <summary>The time a line stays on show before the next one follows while the world still loads (seconds) — long
        /// enough to read one short line. Only "the world is ready" cuts it short.</summary>
        public const float MinLineSeconds = 1.5f;

        /// <summary>The last line the clock alone reaches — "Everything ends…"; only a ready world goes on to "You awaken!".</summary>
        public const int LastWhileLoading = Last - 1;

        /// <summary>Every line's key, in story order — for the locale and fit tests.</summary>
        public static IReadOnlyList<string> AllKeys
        {
            get
            {
                var keys = new List<string>(Last);
                for (int line = First; line <= Last; line++)
                {
                    keys.Add(KeyFor(line));
                }

                return keys;
            }
        }

        /// <summary>
        /// The line (<see cref="First"/>..<see cref="Last"/>) to show for this frame: "You awaken!" once the world is ready;
        /// the first line when none is up yet; the next line once the one on show has been up <see cref="MinLineSeconds"/>
        /// — one line per step, up to <see cref="LastWhileLoading"/>. Never lower than the line on show.
        /// </summary>
        /// <param name="ready">The world is ready (or the veil gave up waiting and fades) — the story ends now.</param>
        /// <param name="shown">The line on show now, <see cref="None"/> before the first — the answer is never lower.</param>
        /// <param name="shownSeconds">How long <paramref name="shown"/> has been on show (seconds; NaN or negative counts
        /// as just shown).</param>
        public static int LineFor(bool ready, int shown, float shownSeconds)
        {
            if (ready || shown >= Last)
            {
                return Last;
            }

            if (shown < First)
            {
                return First;
            }

            bool readLongEnough = shownSeconds >= MinLineSeconds; // false for NaN
            return readLongEnough && shown < LastWhileLoading ? shown + 1 : shown;
        }

        /// <summary>The locale key of <paramref name="line"/>, clamped to the six lines.</summary>
        public static string KeyFor(int line) => KeyPrefix + Math.Max(First, Math.Min(Last, line));
    }
}
