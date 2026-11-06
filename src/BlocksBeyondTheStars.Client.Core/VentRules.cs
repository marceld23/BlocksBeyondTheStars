// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The geyser vents' reach, budget and look (#3275), Unity-free so the headless tests pin them. Every vent within
    /// <see cref="SteamRange"/> breathes a calm steam column (fewer puffs far away); eruptions play only within
    /// <see cref="EruptionRange"/>; at most <see cref="MaxVents"/> vents, nearest first. The steam is white, lightly tinted
    /// by the world's mist colour; it never turns dark or grey (kid-friendly, and steam is bright).
    /// </summary>
    public static class VentRules
    {
        /// <summary>Vents breathe their steady steam within this many blocks.</summary>
        public const float SteamRange = 96f;

        /// <summary>Eruptions play within this many blocks (farther vents keep only the calm column).</summary>
        public const float EruptionRange = 48f;

        /// <summary>The steam's share of its full rate at the edge of <see cref="SteamRange"/>.</summary>
        public const float FarRateShare = 0.3f;

        /// <summary>How strongly the world's mist colour tints the steam (0 = white, 1 = the mist colour).</summary>
        public const float MistTint = 0.15f;

        /// <summary>The darkest any steam channel may get (sRGB 0..1).</summary>
        public const float MinChannel = 0.72f;

        /// <summary>The most vents drawn at once for a quality preset (0 Potato, 1 Low, 2 Medium, 3 High).</summary>
        public static int MaxVents(int preset) => preset switch
        {
            <= 0 => 3,
            1 => 5,
            2 => 8,
            _ => 12,
        };

        /// <summary>The steam rate's share at <paramref name="distance"/> blocks: 1 close by, falling to
        /// <see cref="FarRateShare"/> at <see cref="SteamRange"/> (and 0 beyond).</summary>
        public static float SteamRateScale(float distance)
        {
            if (distance > SteamRange)
            {
                return 0f;
            }

            float t = Clamp01((distance - 12f) / (SteamRange - 12f));
            return 1f - (1f - FarRateShare) * t;
        }

        /// <summary>How long an eruption of <paramref name="strength"/> (0..1) throws puffs, in seconds.</summary>
        public static float EruptionSeconds(float strength) => 1.2f + 2.4f * Clamp01(strength);

        /// <summary>The steam colour (sRGB 0..1) for the world's packed mist colour (0xRRGGBB; −1 = none → white): white
        /// with <see cref="MistTint"/> of the mist colour, never darker than <see cref="MinChannel"/> per channel.</summary>
        public static (float R, float G, float B) SteamTint(int mistColor)
        {
            const float white = 0.97f;
            if (mistColor < 0)
            {
                return (white, white, white);
            }

            float r = ((mistColor >> 16) & 0xFF) / 255f, g = ((mistColor >> 8) & 0xFF) / 255f, b = (mistColor & 0xFF) / 255f;
            return (Tint(r), Tint(g), Tint(b));

            static float Tint(float c) => Math.Max(MinChannel, white + (c - white) * MistTint);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
