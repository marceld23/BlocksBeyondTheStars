// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How far a diver sees (#2401, #3335, #3699): the reach at which the underwater murk closes in fully (<c>Sky.Mist</c>
    /// lerps the haze's far edge to it). Unity-free so the headless suite pins it.
    /// <list type="bullet">
    /// <item>The still, glowing liquids (Ressaw, the Grand Technicians' red fluid) reach <see cref="GlowingFar"/>: milky, you
    /// make out shapes, not detail.</item>
    /// <item>A planet type that names its water's view (<c>waterView</c> in data/planets.json — the sea worlds) keeps it.</item>
    /// <item>Everywhere else the world's air stands in for the water's clarity: 5 m in the soupiest air … 23 m in the
    /// clearest.</item>
    /// </list>
    /// </summary>
    public static class UnderwaterView
    {
        /// <summary>The reach in Ressaw and the red fluid (m).</summary>
        public const float GlowingFar = 7f;

        /// <summary>The reach of a planet's own water view is clamped to this range (m) — a typo in the data must neither
        /// blind the diver nor lift the murk past the streamed terrain.</summary>
        public const float MinFar = 4f;
        public const float MaxFar = 64f;

        /// <summary>The murk's far edge (m) for the liquid <paramref name="look"/> the camera is in, the planet type's
        /// <paramref name="planetWaterView"/> (0 = none) and the world's air density (0..1).</summary>
        public static float FarFor(LiquidLookKind look, double planetWaterView, float airDensity)
        {
            if (LiquidLook.GlowsStill(look))
            {
                return GlowingFar;
            }

            if (planetWaterView > 0.0)
            {
                return (float)Math.Max(MinFar, Math.Min(MaxFar, planetWaterView));
            }

            float clarity = 1f - (Math.Max(0f, Math.Min(1f, airDensity)) * 0.6f);
            return 5f + (18f * clarity);
        }
    }
}
