// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How the sky looks under one weather state — the surface cloud layer's table (#2174).</summary>
    public readonly struct CloudStyle
    {
        public CloudStyle(float cover, float darken, float windScale, float stormTall, float cirrusFade)
        {
            Cover = cover;
            Darken = darken;
            WindScale = windScale;
            StormTall = stormTall;
            CirrusFade = cirrusFade;
        }

        /// <summary>The weather's own cloud cover (the sky shows the larger of this and the planet's base cover).</summary>
        public float Cover { get; }

        /// <summary>Brightness multiplier of the clouds (storms are dark).</summary>
        public float Darken { get; }

        /// <summary>How fast the sky drifts.</summary>
        public float WindScale { get; }

        /// <summary>How tall the low storm towers rise (0 = none).</summary>
        public float StormTall { get; }

        /// <summary>How much of the high cirrus stays (1 = all).</summary>
        public float CirrusFade { get; }
    }

    /// <summary>
    /// One table per weather state for every view that shows weather (#2174): the surface cloud layer, the HUD
    /// label, the orbit cloud shells and the map layers. Before, the surface clouds and the HUD each kept their own
    /// <c>switch</c>, and the orbit view knew nothing — now a storm reads the same everywhere.
    /// </summary>
    public static class WeatherLook
    {
        /// <summary>The surface sky's style for a state (cover, darkening, wind, storm towers, cirrus) — the values
        /// the cloud layer always used; unknown and calm states keep half the planet's base cover.</summary>
        public static CloudStyle Sky(string? state, float cloudDensity) => state switch
        {
            "storm" => new CloudStyle(0.95f, 0.35f, 3.0f, 0.9f, 0.0f),
            "toxic_storm" => new CloudStyle(0.97f, 0.35f, 3.6f, 0.9f, 0.0f), // #2064
            "blizzard" => new CloudStyle(0.98f, 0.45f, 3.4f, 0.7f, 0.0f),
            "ember_fall" => new CloudStyle(0.92f, 0.40f, 1.6f, 0.8f, 0.0f),
            "acid_rain" => new CloudStyle(0.88f, 0.50f, 2.0f, 0.5f, 0.1f),
            "ion_storm" => new CloudStyle(0.55f, 0.70f, 2.6f, 0.2f, 0.3f),
            "rain" => new CloudStyle(0.80f, 0.55f, 1.8f, 0.3f, 0.2f),
            // A gale tears the sky along without soaking it: thin cover, very fast.
            "gale" => new CloudStyle(0.55f, 0.80f, 4.0f, 0.0f, 0.2f),
            "drizzle" => new CloudStyle(0.70f, 0.72f, 1.3f, 0.0f, 0.4f),
            "fog" or "ground_fog" => new CloudStyle(0.72f, 0.85f, 0.4f, 0.0f, 0.7f),
            // A heatwave burns the sky clean.
            "heatwave" => new CloudStyle(cloudDensity * 0.2f, 1.1f, 0.5f, 0.0f, 1.0f),
            "clouds" => new CloudStyle(0.60f, 0.80f, 1.2f, 0.0f, 0.5f),
            _ => new CloudStyle(cloudDensity * 0.5f, 1.0f, 1.0f, 0.0f, 1.0f),
        };

        /// <summary>The cover a view shows for a state: the planet's base cover raised by the weather — exactly the
        /// surface sky's rule (the larger of the two), so orbit and ground agree.</summary>
        public static float Cover(string? state, float cloudDensity)
            => Math.Clamp(Math.Max(cloudDensity, Sky(state, cloudDensity).Cover), 0f, 1f);

        /// <summary>The HUD/map glyph for a state ("☀" for a clear sky on the maps; the HUD hides clear).</summary>
        public static string Glyph(string? state) => state switch
        {
            "clear" => "☀",
            "clouds" => "☁",
            "rain" or "drizzle" => "☂",
            "storm" => "⚡",
            "blizzard" or "gale" => "❄",
            "fog" or "ground_fog" => "≈",
            "heatwave" => "☀",
            "acid_rain" => "☣",
            "toxic_storm" => "☣", // #2064
            "ion_storm" => "⚡",
            "meteor_shower" => "★",
            "ember_fall" => "▲",
            "spore_bloom" => "❋",
            _ => "•",
        };

        /// <summary>The weather family of a state in its wire form ("violent", "exotic", …).</summary>
        public static string Family(string? state)
            => (WeatherCatalog.Find(state)?.Family ?? WeatherFamily.Calm).ToString().ToLowerInvariant();

        /// <summary>The warning colour (rich-text hex) of a family, or null for the mild ones.</summary>
        public static string? FamilyColorHex(string? family) => family switch
        {
            "violent" => "#ff7439",
            "exotic" => "#c58bff",
            "obscuring" => "#9fb4c8",
            _ => null,
        };

        /// <summary>The colour (0xRRGGBB) a cloud shell pixel takes under a state — the world's cloud tint, darkened
        /// for storms and coloured by what falls: snow white, acid sickly yellow-green, ash dark, sand like the
        /// ground, spores in the world's flora hue.</summary>
        public static int OrbitTint(string? state, string? precipitation, int cloudRgb, int groundRgb, int floraRgb)
        {
            int baseRgb = cloudRgb;
            float mix = 0f;
            int target = cloudRgb;
            switch (precipitation)
            {
                case "snow":
                case "hail":
                case "sleet":
                    target = 0xF4F8FF; mix = 0.6f; break;
                case "acid":
                    target = 0xB8C23A; mix = 0.45f; break;
                case "ash":
                    target = 0x3A3330; mix = 0.6f; break;
                case "sandstorm":
                case "dust":
                    target = groundRgb; mix = 0.55f; break;
                case "spores":
                    target = floraRgb; mix = 0.5f; break;
            }

            switch (state)
            {
                case "blizzard":
                    target = 0xF4F8FF; mix = Math.Max(mix, 0.7f); break;
                case "toxic_storm":
                    target = 0x5C7A2E; mix = Math.Max(mix, 0.55f); break;
                case "ember_fall":
                    target = 0x4A2E22; mix = Math.Max(mix, 0.6f); break;
                case "ion_storm":
                    target = 0xB9A6FF; mix = Math.Max(mix, 0.35f); break;
            }

            int rgb = Mix(baseRgb, target, mix);
            float darken = Math.Min(1f, Sky(state, 0f).Darken);
            // Keep storm clouds readable from orbit: never darker than half of their tint.
            return Scale(rgb, 0.5f + 0.5f * darken);
        }

        private static int Mix(int a, int b, float t)
        {
            int ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
            int br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
            int r = (int)(ar + (br - ar) * t + 0.5f);
            int g = (int)(ag + (bg - ag) * t + 0.5f);
            int bl = (int)(ab + (bb - ab) * t + 0.5f);
            return (r << 16) | (g << 8) | bl;
        }

        private static int Scale(int rgb, float f)
        {
            int r = Math.Clamp((int)(((rgb >> 16) & 0xFF) * f + 0.5f), 0, 255);
            int g = Math.Clamp((int)(((rgb >> 8) & 0xFF) * f + 0.5f), 0, 255);
            int b = Math.Clamp((int)((rgb & 0xFF) * f + 0.5f), 0, 255);
            return (r << 16) | (g << 8) | b;
        }
    }
}
