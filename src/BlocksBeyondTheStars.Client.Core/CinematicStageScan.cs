// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-free terrain probing for the staged prologue orbit (#777): the camera used to fly a blind
    /// parametric circle around the landed ship, which on a hillside put it inside the mountain (voxel
    /// backfaces aren't rendered, so the player looked straight through the slope). These helpers let
    /// <c>PrologueCinematic</c> scan the orbit ring against the client voxel world before the shot and
    /// keep a cheap per-frame line-of-sight safety net while it runs. Lives here (not in the
    /// MonoBehaviour) so the geometry rules are covered by plain .NET tests.
    /// </summary>
    public static class CinematicStageScan
    {
        /// <summary>True = the cell is KNOWN to be air. A cell in a chunk that has not streamed yet must
        /// report false (blocked): mistaking not-yet-loaded terrain for open space is exactly the bug
        /// this scan exists to prevent.</summary>
        public delegate bool ClearSampler(int wx, int wy, int wz);

        /// <summary>Sample spacing of the line march, in blocks. Half a block cannot skip a cell wall.</summary>
        private const float MarchStep = 0.5f;

        /// <summary>
        /// Whether a camera can sit at a world position: its cell, the four horizontal neighbours and the
        /// cell above must all be known air (near plane + foliage cross-billboard clearance). The cell
        /// BELOW may be solid — hovering one block over the ground is fine.
        /// </summary>
        public static bool SpotClear(ClearSampler clear, float x, float y, float z)
        {
            int cx = Floor(x), cy = Floor(y), cz = Floor(z);
            return clear(cx, cy, cz)
                   && clear(cx + 1, cy, cz) && clear(cx - 1, cy, cz)
                   && clear(cx, cy, cz + 1) && clear(cx, cy, cz - 1)
                   && clear(cx, cy + 1, cz);
        }

        /// <summary>
        /// Marches the segment in half-block steps; every touched cell must be known air. Start and end
        /// cells are included, so a look target buried in terrain fails immediately.
        /// </summary>
        public static bool PathClear(ClearSampler clear,
            float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
        {
            float dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
            float length = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int steps = Math.Max(1, (int)Math.Ceiling(length / MarchStep));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                if (!clear(Floor(fromX + dx * t), Floor(fromY + dy * t), Floor(fromZ + dz * t)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Spot clearance at the camera position plus line of sight back to the look target —
        /// the full per-position check both the pre-scan and the per-frame safety net use.</summary>
        public static bool CameraClear(ClearSampler clear,
            float lookX, float lookY, float lookZ, float camX, float camY, float camZ)
            => SpotClear(clear, camX, camY, camZ)
               && PathClear(clear, lookX, lookY, lookZ, camX, camY, camZ);

        /// <summary>
        /// Per-angle clearance of an orbit ring around (<paramref name="cx"/>, <paramref name="cy"/>,
        /// <paramref name="cz"/>): sample i covers angle i·360/<paramref name="samples"/> degrees with the
        /// orbit convention pos = center + (sin·r, camHeight, cos·r), looking at center + lookHeight.
        /// </summary>
        public static bool[] ScanRing(ClearSampler clear, float cx, float cy, float cz,
            float lookHeight, float radius, float camHeight, int samples)
        {
            if (samples <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }

            var ring = new bool[samples];
            for (int i = 0; i < samples; i++)
            {
                float rad = i * (360f / samples) * ((float)Math.PI / 180f);
                float px = cx + (float)Math.Sin(rad) * radius;
                float py = cy + camHeight;
                float pz = cz + (float)Math.Cos(rad) * radius;
                ring[i] = CameraClear(clear, cx, cy + lookHeight, cz, px, py, pz);
            }

            return ring;
        }

        /// <summary>
        /// Finds the widest circular run of clear samples. True when its sweep reaches
        /// <paramref name="minSweepDeg"/>; a fully clear ring reports a 360° sweep (callers treat that as
        /// "no arc limit"). <paramref name="arcCenterDeg"/> is the run's middle angle in [0, 360).
        /// </summary>
        public static bool TryFindWidestClearArc(bool[] ring, float minSweepDeg,
            out float arcCenterDeg, out float sweepDeg)
        {
            arcCenterDeg = 0f;
            sweepDeg = 0f;
            if (ring == null || ring.Length == 0)
            {
                return false;
            }

            int n = ring.Length;
            float degPer = 360f / n;

            int bestStart = -1, bestLen = 0;
            int runStart = -1, runLen = 0;

            // Walk twice around so a run wrapping 359°→0° is seen as one run (capped at n samples).
            for (int i = 0; i < 2 * n; i++)
            {
                if (ring[i % n])
                {
                    if (runLen == 0)
                    {
                        runStart = i;
                    }

                    runLen = Math.Min(runLen + 1, n);
                    if (runLen > bestLen)
                    {
                        bestLen = runLen;
                        bestStart = runStart;
                    }
                }
                else
                {
                    runLen = 0;
                }
            }

            if (bestLen == 0)
            {
                return false;
            }

            sweepDeg = bestLen * degPer;
            arcCenterDeg = ((bestStart + (bestLen - 1) * 0.5f) * degPer) % 360f;
            return sweepDeg + 0.001f >= minSweepDeg;
        }

        private static int Floor(float v) => (int)Math.Floor(v);
    }
}
