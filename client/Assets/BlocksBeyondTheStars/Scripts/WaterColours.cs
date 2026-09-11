// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The world's water colour for everything that is water but not the sea (#1758, Marcel 2026-09-11: "bedenke
    /// dabei auch die Farbe des Regens"): the rain drops, the rain beads and streaks on the visor, the wash while
    /// swimming. The shader recolours the sea from the same two environment fields; this is the C# side of it.
    /// Mode 0 (every classic world) returns the caller's own colour untouched, so nothing old changes.
    /// </summary>
    internal static class WaterColours
    {
        /// <summary>True when this world's water is not the classic blue.</summary>
        public static bool Tinted(WorldEnvironment env) => env != null && env.WaterTintMode > 0;

        /// <summary>The world's water colour at this moment: the fixed tint in mode 1; in mode 2 (the rainbow planet,
        /// whose sea carries static bands by position) a hue that cycles slowly with time, so rainbow rain falls
        /// in every colour in turn.</summary>
        public static Color Tint(WorldEnvironment env, float time)
        {
            if (env == null || env.WaterTintMode <= 0)
            {
                return new Color(0.20f, 0.42f, 0.85f); // the classic water blue
            }

            if (env.WaterTintMode >= 2)
            {
                return Color.HSVToRGB(Mathf.Repeat(time / 14f, 1f), 0.85f, 1f);
            }

            int rgb = env.WaterTint;
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
        }

        /// <summary>Pulls <paramref name="baseColor"/> (a rain drop, a bead, a wash) toward the world's water colour by
        /// <paramref name="amount"/>, keeping its alpha and its paleness: the tint is first lightened toward white so
        /// a violet sea gives lilac rain, not purple paint. Classic worlds get <paramref name="baseColor"/> back.</summary>
        public static Color Blend(Color baseColor, WorldEnvironment env, float amount, float time)
        {
            if (!Tinted(env))
            {
                return baseColor;
            }

            float lum = 0.299f * baseColor.r + 0.587f * baseColor.g + 0.114f * baseColor.b;
            var pale = Color.Lerp(Tint(env, time), Color.white, Mathf.Clamp01(lum * 0.6f));
            var mixed = Color.Lerp(baseColor, pale, amount);
            mixed.a = baseColor.a;
            return mixed;
        }
    }
}
