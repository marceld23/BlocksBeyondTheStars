// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What night sight (#2202: the bio lab's night-sight preparation; #3339/#3414: a lit Ressaw lamp, worn) does to the look
    /// of the world, Unity-free so the headless tests pin it. "The world around you looks like day": <c>Sky</c> lights the
    /// world with a raised light level (<see cref="LightDay"/>: the block light, the models' light and the ambient follow it),
    /// colours the sky, the camera background, the distance haze and the mist with a raised day level
    /// (<see cref="SeenDay"/>, before the weather tints them, so a sandstorm stays sandy), lifts the fill of sky-less faces
    /// like a faint cabin light (<see cref="CaveFill"/>), and the starfield gets a boost (<see cref="StarBoost"/>) so it keeps
    /// its contrast against the brighter sky. Only the LOOK changes: the time of day, the sun's height, direction and
    /// intensity stay, so the stars still come out by the sun's height and the moons keep their own clock. The eye adaptation
    /// counts night sight as light (<see cref="AdaptedLight"/>), so it does not brighten on top. Strength 0 changes nothing,
    /// and a day is never darkened.
    /// </summary>
    public static class NightSightRules
    {
        /// <summary>The raw day level (0 midnight, 0.5 the sun on the horizon, 1 noon) the sky, haze and mist colours take at
        /// full strength: the sky of a sun about 17° up — a bright afternoon, not noon.</summary>
        public const float SeenDayFull = 0.65f;

        /// <summary>The eased light level (the Sky's <c>dayLit</c>: 0 night … 1 noon) the world light takes at full strength.
        /// The Sky's light brightness runs from 0.35 at night to 1 at noon, so 0.6 lands on 0.74 — a late afternoon, the light
        /// floor night sight lifted the world to before #3414.</summary>
        public const float LightDayFull = 0.6f;

        /// <summary>The fill of sky-less faces (caves, deep shade) at full strength; 1 is a lit ship cabin.</summary>
        public const float CaveFillFull = 0.8f;

        /// <summary>The starfield's extra brightness at full strength (×1.35 — still inside the star shader's 0..2 range at the
        /// field's 1.3 cap).</summary>
        public const float StarBoostFull = 0.35f;

        /// <summary>The raw day level the sky, haze and mist colours are lit with: the real <paramref name="day01"/>, raised to
        /// <see cref="SeenDayFull"/> × the strength — never below the real day.</summary>
        public static float SeenDay(float day01, float sight01)
        {
            float sight = Clamp01(sight01);
            return sight <= 0f ? day01 : Math.Max(day01, SeenDayFull * sight);
        }

        /// <summary>The eased light level the world light is drawn with: the real <paramref name="dayLit01"/>, raised to
        /// <see cref="LightDayFull"/> × the strength — never below the real light.</summary>
        public static float LightDay(float dayLit01, float sight01)
        {
            float sight = Clamp01(sight01);
            return sight <= 0f ? dayLit01 : Math.Max(dayLit01, LightDayFull * sight);
        }

        /// <summary>The least fill sky-less faces get (0 without night sight): the Sky takes the larger of this and its own
        /// interior fill.</summary>
        public static float CaveFill(float sight01) => CaveFillFull * Clamp01(sight01);

        /// <summary>The factor on the starfield's brightness (1 without night sight, never below 1).</summary>
        public static float StarBoost(float sight01) => 1f + (StarBoostFull * Clamp01(sight01));

        /// <summary>The light (0..1 scene brightness) night sight counts as for the eye adaptation: at full strength it is above
        /// the adaptation's "lit" level, so the eyes add no dark boost on top of the lift.</summary>
        public static float AdaptedLight(float sight01) => LightDayFull * Clamp01(sight01);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
