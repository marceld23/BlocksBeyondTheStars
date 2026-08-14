// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-free line-of-sight march for render-side combat effects (#1004): the scan-drone's laser and
    /// the bandit gunner's tracer are cosmetic mirrors of the server's proximity damage, but they used to
    /// fire on range alone — a drone hovering outside a cave visibly sniped the player straight through
    /// the rock while the server (whose damage and hunt lock are both sight-gated) never dealt a point.
    /// Client mirror of the server's <c>HasLineOfSight</c>: same step size, endpoints skipped. Lives here
    /// (not in the MonoBehaviour) so the march is covered by plain .NET tests.
    /// </summary>
    public static class SightLine
    {
        /// <summary>True = the cell BLOCKS sight. Callers mirror the server's rule: a non-air block whose
        /// definition is missing or flagged <c>Solid</c> occludes (hiding in tall grass works), and so do
        /// water/lava (no aggro through a lake). An unloaded chunk should read as clear — near the player
        /// the world is streamed, and a cosmetic effect must not pop off over a chunk-load hiccup.</summary>
        public delegate bool BlockingSampler(int wx, int wy, int wz);

        /// <summary>Sample spacing of the march, in blocks — matches the server's sight march.</summary>
        private const float MarchStep = 0.25f;

        /// <summary>
        /// Whether nothing sight-blocking stands strictly between the two points. Endpoint cells are
        /// skipped — the shooter's and the target's own bodies aren't occluders. Both points must already
        /// be in the same (unwrapped) frame; cells are handed to the sampler raw, so a sampler backed by
        /// the client world canonicalises seam coordinates itself.
        /// </summary>
        public static bool Clear(BlockingSampler blocking,
            float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
        {
            float dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int steps = Math.Max(1, (int)Math.Ceiling(length / MarchStep));
            for (int i = 1; i < steps; i++)
            {
                float t = (float)i / steps;
                if (blocking(Floor(fromX + dx * t), Floor(fromY + dy * t), Floor(fromZ + dz * t)))
                {
                    return false;
                }
            }

            return true;
        }

        private static int Floor(float v) => (int)Math.Floor(v);
    }
}
