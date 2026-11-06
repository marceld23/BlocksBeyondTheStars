// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The cloud band at altitude (#3272) as Unity-free rules: how much of it the weather brings. The band is never
    /// permanent — on a clear day summits stand in the sun with a sea of clouds (or nothing) below; clouds bring a patchy
    /// band; wet and obscuring weather close it. The haze shader (the band term), the cloud deck and the dome clouds all
    /// read the same cover, so what you see from a summit agrees with what you walk into.
    /// </summary>
    public static class CloudBandRules
    {
        /// <summary>The thinnest band that still counts as one (blocks); a thinner one is treated as absent.</summary>
        public const float MinThickness = 4f;

        /// <summary>Whether the world has a cloud band at all (<paramref name="topY"/> above <paramref name="baseY"/>
        /// by at least <see cref="MinThickness"/>).</summary>
        public static bool HasBand(float baseY, float topY) => topY - baseY >= MinThickness;

        /// <summary>0..1 how much of the band is present right now, from the weather state (the
        /// <c>WorldEnvironment.Weather</c> vocabulary), its 0..1 intensity and the world's 0..1 cloud cover.
        /// Clear = 0 (clear summit days); clouds = a patchy band growing with the cover; rain, drizzle, storms,
        /// snow and fog = a nearly closed band. Events without a cloud deck of their own (heat waves, ion storms,
        /// meteor showers …) leave a thin remnant only on cloudy worlds.</summary>
        public static float Cover(string? weather, float intensity, float cloudDensity)
        {
            float cover = Clamp01(cloudDensity);
            float strength = Clamp01(intensity);
            switch (weather ?? string.Empty)
            {
                case "clear":
                case "":
                    return 0f;
                case "clouds":
                    return Clamp01((0.25f + 0.5f * cover) * (0.6f + 0.4f * strength));
                case "rain":
                case "drizzle":
                case "acid_rain":
                case "fog":
                    return Clamp01(0.7f + 0.3f * strength);
                case "storm":
                case "blizzard":
                case "toxic_storm":
                    return Clamp01(0.85f + 0.15f * strength);
                case "ground_fog":
                    return Clamp01(0.2f * cover); // fog in the valleys, the peaks mostly above it
                default:
                    return Clamp01(0.25f * cover);
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
