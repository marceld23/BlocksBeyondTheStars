// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The story lines in big letters over the loading bar (#3450, Justus' idea): "The Sounders come and go…", "Humans
    /// travel into space…", "Settlements are built in space…", "Everything begins…", "Everything ends…" — and
    /// "You awaken!", as the Vega prologue opens in the pilot's seat.
    /// <para>The first five lines share the bar in five equal bands (<see cref="BandLine"/>); the sixth belongs to the full
    /// bar. The bar sets where the story is heading, but every line stays up at least <see cref="MinLineSeconds"/> before
    /// the next one follows — one line at a time — so a bar that races through its first bands (the desktop ramp, a
    /// browser world) never flashes lines nobody can read. The hand-off frame always shows the last line at once, so
    /// "You awaken!" is the last thing the player reads before the world appears, even after a 0.6-second load (whose story
    /// is then the first line and the last). The bar can stall or creep while the server builds its world (and starts
    /// over after a give-up), but the story never steps back.</para>
    /// Pure, so the headless suite pins the pacing; the Unity loading screen (<c>UiLoading</c>) only times the line on
    /// show and draws it.
    /// </summary>
    public static class LoadingLore
    {
        /// <summary>Key prefix of the lines: <c>ui.loading.lore.1</c> … <c>ui.loading.lore.6</c>.</summary>
        public const string KeyPrefix = "ui.loading.lore.";

        /// <summary>The value of "no line shown yet" — what the screen starts with.</summary>
        public const int None = 0;

        /// <summary>The first line, "The Sounders come and go…".</summary>
        public const int First = 1;

        /// <summary>The last line, "You awaken!" — the one the hand-off always shows.</summary>
        public const int Last = 6;

        /// <summary>The shortest time a line stays on show before the next one follows (seconds) — long enough to read one
        /// short line. Only the hand-off cuts it short.</summary>
        public const float MinLineSeconds = 1.5f;

        /// <summary>The lines that share the bar's bands — all but the last.</summary>
        private const int BandLines = Last - 1;

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
        /// The line the bar's fill names (<see cref="First"/>..<see cref="Last"/>): five equal bands for the first five
        /// lines, the last one at the full bar.
        /// </summary>
        /// <param name="progress">The bar's fill, 0..1 (values outside, and NaN, are clamped).</param>
        public static int BandLine(float progress)
        {
            if (progress >= 1f)
            {
                return Last;
            }

            if (!(progress > 0f)) // also NaN
            {
                return First;
            }

            return First + Math.Min(BandLines - 1, (int)(progress * BandLines));
        }

        /// <summary>
        /// The line (<see cref="First"/>..<see cref="Last"/>) to show for this frame: the first line when none is up yet;
        /// the next line once the one on show has been up <see cref="MinLineSeconds"/> and the bar's band
        /// (<see cref="BandLine"/>) lies beyond it — at most one line per step; the last line in the hand-off frame,
        /// whatever was on show. Never lower than the line on show.
        /// </summary>
        /// <param name="progress">The bar's fill, 0..1 (values outside, and NaN, are clamped).</param>
        /// <param name="handingOff">This is the frame the loading screen hands off to the game.</param>
        /// <param name="shown">The line on show now, <see cref="None"/> before the first — the answer is never lower.</param>
        /// <param name="shownSeconds">How long <paramref name="shown"/> has been on show (seconds; NaN or negative counts
        /// as just shown).</param>
        public static int LineFor(float progress, bool handingOff, int shown, float shownSeconds)
        {
            if (handingOff)
            {
                return Last;
            }

            if (shown < First)
            {
                return First;
            }

            if (shown >= Last)
            {
                return Last;
            }

            bool readLongEnough = shownSeconds >= MinLineSeconds; // false for NaN
            return readLongEnough && BandLine(progress) > shown ? shown + 1 : shown;
        }

        /// <summary>The locale key of <paramref name="line"/>, clamped to the six lines.</summary>
        public static string KeyFor(int line) => KeyPrefix + Math.Max(First, Math.Min(Last, line));
    }
}
