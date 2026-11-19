// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The dive of an NPC ship into a body of the flight scene (#3824: a landing trader is seen landing). Unity-free
    /// numbers, so the suite can hold them: where the ship is at a moment of the dive, how small it has become and how
    /// hot its entry glows. The ship starts where the server holds it (in landing range of the body), falls ever faster
    /// along a straight line to the body's surface and shrinks on the way — at the scene's compact scale a hull that
    /// kept its size would read as a ship sitting ON the planet, not as one going down into its air.
    /// </summary>
    public static class NpcDescentPath
    {
        /// <summary>The ship's size at the end of the dive, as a fraction of its flight size.</summary>
        public const float EndScale = 0.12f;

        /// <summary>The fraction of the dive after which the ship is no longer drawn (it is in the clouds).</summary>
        public const float HideAt = 0.97f;

        /// <summary>Where the dive ends: the point of the body's surface under the ship.</summary>
        public static Vector3 SurfacePoint(Vector3 from, Vector3 body, float radius)
        {
            var outward = from - body;
            float len = outward.Length();
            outward = len > 1e-4f ? outward / len : Vector3.UnitY;
            return body + (outward * radius);
        }

        /// <summary>The ship's place at <paramref name="t"/> (0..1) of the dive: slow off the mark, fast at the end.</summary>
        public static Vector3 PositionAt(Vector3 from, Vector3 to, float t)
        {
            float k = Clamp01(t);
            return Vector3.Lerp(from, to, k * k);
        }

        /// <summary>The ship's size at <paramref name="t"/>: full for the first fifth, then down to <see cref="EndScale"/>.</summary>
        public static float ScaleAt(float t)
            => 1f + ((EndScale - 1f) * Smooth(0.2f, 1f, Clamp01(t)));

        /// <summary>The entry glow at <paramref name="t"/>, 0..1: it builds as the ship meets the air and dies as the
        /// clouds take it. An airless body has none (the caller draws a retro burn instead).</summary>
        public static float HeatAt(float t)
        {
            float k = Clamp01(t);
            return Smooth(0.12f, 0.4f, k) * (1f - Smooth(0.8f, HideAt, k));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float Smooth(float a, float b, float x)
        {
            float t = Clamp01((x - a) / Math.Max(1e-4f, b - a));
            return t * t * (3f - (2f * t));
        }
    }
}
