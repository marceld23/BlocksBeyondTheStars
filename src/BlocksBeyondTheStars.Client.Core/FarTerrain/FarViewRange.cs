// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>The "Far view" setting (#1820): how far the low-resolution far terrain reaches, in blocks.</summary>
    public static class FarViewRange
    {
        public const int Off = 0;
        public const int Near = 512;
        public const int Far = 1024;

        /// <summary>#3059: the step only the High preset on a desktop client offers (see <see cref="Effective"/>).</summary>
        public const int Maximum = 2048;

        /// <summary>The settings cycle, in order. The last step (<see cref="Maximum"/>) is offered on desktop High only.</summary>
        public static readonly int[] Steps = { Off, Near, Far, Maximum };

        /// <summary>A stored value not written yet (settings from before #1820, a fresh install).</summary>
        public const int Unset = -1;

        /// <summary>Marcel's defaults (2026-09-12): native desktop 1024, every browser build and phone/tablet 512.</summary>
        public static int DefaultFor(bool browserOrMobile) => browserOrMobile ? Near : Far;

        /// <summary>Snaps any stored value to a supported step (unset → the platform default). The stored choice keeps
        /// <see cref="Maximum"/> on every client; <see cref="Effective"/> decides whether it applies.</summary>
        public static int Normalize(int stored, bool browserOrMobile)
        {
            if (stored < 0)
            {
                return DefaultFor(browserOrMobile);
            }

            if (stored == 0)
            {
                return Off;
            }

            if (stored <= (Near + Far) / 2)
            {
                return Near;
            }

            return stored <= (Far + Maximum) / 2 ? Far : Maximum;
        }

        /// <summary>#3059: the range a stored choice gives on this client — <see cref="Maximum"/> only on the High preset of
        /// a desktop client, otherwise at most <see cref="Far"/> (the stored choice is kept for the next switch to High).</summary>
        public static int Effective(int range, bool highDesktop) => highDesktop || range <= Far ? range : Far;

        /// <summary>The next value of the settings cycle, counted from the range in effect: Off → 512 → 1024
        /// (→ 2048 on desktop High) → Off.</summary>
        public static int Next(int current, bool highDesktop)
        {
            int count = highDesktop ? Steps.Length : Steps.Length - 1;
            int index = Array.IndexOf(Steps, Effective(current, highDesktop));
            return Steps[(index + 1) % count];
        }

        /// <summary>The range actually used on a world: never past half the world's circumference or latitude period,
        /// or the far terrain would wrap into itself on an asteroid.</summary>
        public static int ClampToWorld(int range, int circumference, int latitudePeriod)
        {
            if (range <= 0 || circumference <= 0)
            {
                return 0;
            }

            int limit = Math.Min(circumference, latitudePeriod > 0 ? latitudePeriod : circumference) / 2 - 32;
            return Math.Max(0, Math.Min(range, limit));
        }
    }
}
