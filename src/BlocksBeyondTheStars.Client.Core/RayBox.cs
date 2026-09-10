// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Ray against an axis-aligned box, in plain floats so the Unity client and the tests share one rule.
    /// Used to aim at things that are not blocks — a player-built door is an entity standing in an air cell,
    /// which the voxel march can never hit (#1746) — and to rank such a hit against the first solid cell.
    /// </summary>
    public static class RayBox
    {
        /// <summary>Distance along the ray (in units of the direction's length) at which it enters the box, 0
        /// when the origin already lies inside, or <see cref="float.PositiveInfinity"/> when the ray misses or
        /// the box lies behind the origin.</summary>
        public static float Entry(float ox, float oy, float oz, float dx, float dy, float dz,
            float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            float tMin = 0f, tMax = float.PositiveInfinity;
            if (!Slab(ox, dx, minX, maxX, ref tMin, ref tMax)
                || !Slab(oy, dy, minY, maxY, ref tMin, ref tMax)
                || !Slab(oz, dz, minZ, maxZ, ref tMin, ref tMax))
            {
                return float.PositiveInfinity;
            }

            return tMin;
        }

        private static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            if (Math.Abs(d) < 1e-8f)
            {
                return o >= min && o <= max; // parallel to this pair of planes: inside or never
            }

            float inv = 1f / d;
            float t0 = (min - o) * inv, t1 = (max - o) * inv;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            tMin = Math.Max(tMin, t0);
            tMax = Math.Min(tMax, t1);
            return tMax >= tMin;
        }
    }
}
