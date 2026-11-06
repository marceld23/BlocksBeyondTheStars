// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The eye adaptation (#2395) as Unity-free rules, so the headless tests pin them: how much exposure the dark earns
    /// (EV, + brightens) and how fast the eyes get there. #3265: the boost is capped — underground (rock overhead) at +0.35 EV
    /// ("Visibility in the dark" raises it to +0.7), above ground at night at +0.2 EV, above ground by day as before (tree
    /// and roof shade keep their look) — and the eyes adapt to the dark slowly
    /// (0.07 EV/s, ~5 s to the underground cap) like real dark adaptation, so a cave or a moonless night stays dark instead
    /// of being brightened back to dusk. The squint back into the light stays quick. Never a flash (WCAG 2.3.1): the fastest
    /// ramp is 2.5 EV/s and nothing oscillates. <c>AtmosphereProbe</c> feeds it and hands the result to the post exposure.
    /// </summary>
    public static class EyeAdaptationRules
    {
        /// <summary>The most the dark can ever earn (EV), before the caps.</summary>
        public const float MaxEv = 0.9f;

        /// <summary>Scene brightness (0..1) from which the eyes need no help at all.</summary>
        public const float LitBrightness = 0.55f;

        /// <summary>EV per second toward the dark (slow, partial — real dark adaptation).</summary>
        public const float DarkRate = 0.07f;

        /// <summary>EV per second toward the light (a quick squint).</summary>
        public const float LightRate = 2.5f;

        /// <summary>The boost underground (rock overhead, <see cref="UndergroundCover"/>) at "Visibility in the dark" 0 / 1 (EV).</summary>
        public const float UndergroundCapEv = 0.35f, UndergroundCapEvMax = 0.7f;

        /// <summary>The boost at night anywhere above ground — open sky, under trees, under a roof (EV). By day the cap above
        /// ground eases up to <see cref="MaxEv"/>, so the shade of a forest or a house reads as before.</summary>
        public const float OpenSkyCapEv = 0.2f;

        /// <summary>Light-stopping blocks overhead (rock, opaque cubes — not leaves, plants, glass or water) from which the
        /// player counts as underground — the same three the skylight's thin-cover rule and VEGA's lamp hint use.</summary>
        public const int UndergroundCover = 3;

        /// <summary>The boost the dark earns before the caps: nothing from <see cref="LitBrightness"/> up, rising to
        /// <see cref="MaxEv"/> in the pitch dark. A lit suit lamp counts as some light (#3230,
        /// <see cref="HeadlampRules.AdaptedBrightness"/>): the eyes adapt to the lit spot.</summary>
        public static float DarkBoost(float sceneBrightness, bool lampLit)
        {
            float dark = 1f - Clamp01(HeadlampRules.AdaptedBrightness(sceneBrightness, lampLit) / LitBrightness);
            return MaxEv * (float)Math.Pow(dark, 1.5);
        }

        /// <summary>The cap for where the player is: underground (rock overhead), where <paramref name="darkVisibility"/>
        /// (0..1, the setting) raises it; else above ground — open sky, a forest, a house — from <see cref="OpenSkyCapEv"/> at
        /// night up to <see cref="MaxEv"/> by day (<paramref name="day"/> 0..1), so tree and roof shade read as before.</summary>
        public static float Cap(bool underground, float day, float darkVisibility)
            => underground
                ? UndergroundCapEv + (UndergroundCapEvMax - UndergroundCapEv) * Clamp01(darkVisibility)
                : OpenSkyCapEv + (MaxEv - OpenSkyCapEv) * Clamp01(day);

        /// <summary>The exposure offset the eyes adapt toward (EV). Reduced effects halve the ceiling.</summary>
        public static float Target(float sceneBrightness, bool lampLit, bool underground, float day, float darkVisibility, bool reducedEffects)
        {
            float ceiling = reducedEffects ? MaxEv * 0.5f : MaxEv;
            return Math.Min(Math.Min(DarkBoost(sceneBrightness, lampLit), Cap(underground, day, darkVisibility)), ceiling);
        }

        /// <summary>One frame of adaptation from <paramref name="current"/> toward <paramref name="target"/>: slow into the
        /// dark, quick into the light. Reduced effects shorten the dark ramp (×2) and soften the squint (×0.6).</summary>
        public static float Step(float current, float target, float dt, bool reducedEffects)
        {
            if (target > current)
            {
                float rate = reducedEffects ? DarkRate * 2f : DarkRate;
                return Math.Min(target, current + rate * dt);
            }

            float lightRate = reducedEffects ? LightRate * 0.6f : LightRate;
            return Math.Max(target, current - lightRate * dt);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
