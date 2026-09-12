// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.FarTerrain
{
    /// <summary>One far-terrain patch: a square of grid cells on one detail level, in unwrapped scene coordinates.</summary>
    public readonly struct FarPatchKey : IEquatable<FarPatchKey>
    {
        public readonly int Level;
        public readonly int Px;
        public readonly int Pz;

        public FarPatchKey(int level, int px, int pz)
        {
            Level = level;
            Px = px;
            Pz = pz;
        }

        public int OriginX => Px * FarTerrainLayout.PatchSize(Level);
        public int OriginZ => Pz * FarTerrainLayout.PatchSize(Level);

        public bool Equals(FarPatchKey other) => Level == other.Level && Px == other.Px && Pz == other.Pz;
        public override bool Equals(object? obj) => obj is FarPatchKey k && Equals(k);
        public override int GetHashCode() => unchecked((Level * 73856093) ^ (Px * 19349663) ^ (Pz * 83492791));
        public override string ToString() => $"L{Level}({Px},{Pz})";
    }

    /// <summary>
    /// The far terrain's two detail levels (#1820). The near level (8-block cells, 128-block patches) reaches
    /// <see cref="NearRadius"/>; the far level (32-block cells, 256-block patches) takes over from there to the far-view
    /// range and discards its fragments inside <see cref="InnerRadius"/>, so a coarse slope never pokes through the
    /// near level. Patches live in the player's unwrapped scene space — the generator's noise wraps on the torus, so a
    /// patch sampled "past the seam" is simply the terrain on the other side.
    /// </summary>
    public static class FarTerrainLayout
    {
        public const int NearRadius = 384;

        /// <summary>How far the far level reaches back under the near level (its discard radius sits this far inside).</summary>
        public const int LevelOverlap = 96;

        public static int CellSize(int level) => level == 0 ? 8 : 32;
        public static int CellsPerPatch(int level) => level == 0 ? 16 : 8;
        public static int PatchSize(int level) => CellSize(level) * CellsPerPatch(level);

        /// <summary>The near level's reach for a far-view range.</summary>
        public static int NearReach(int range) => Math.Min(range, NearRadius);

        /// <summary>The radius (blocks, from the player) inside which a level's fragments are discarded.</summary>
        public static float InnerRadius(int level, int range) => level == 0 ? 0f : Math.Max(0, NearReach(range) - LevelOverlap);

        /// <summary>Vertical offset of a level's surface below the true top, so real chunks and the finer level win the
        /// depth test where they overlap.</summary>
        public static float HeightOffset(int level) => level == 0 ? -0.35f : -2.5f;

        /// <summary>The patches a player at (x, z) needs for <paramref name="range"/>, nearest first.</summary>
        public static List<FarPatchKey> Desired(float x, float z, int range)
        {
            var result = new List<(FarPatchKey Key, float D)>();
            if (range <= 0)
            {
                return new List<FarPatchKey>();
            }

            int near = NearReach(range);
            Collect(0, x, z, 0f, near, result);
            if (range > near)
            {
                Collect(1, x, z, InnerRadius(1, range), range, result);
            }

            result.Sort((a, b) => a.D.CompareTo(b.D));
            var keys = new List<FarPatchKey>(result.Count);
            foreach (var r in result)
            {
                keys.Add(r.Key);
            }

            return keys;
        }

        private static void Collect(int level, float x, float z, float inner, float outer, List<(FarPatchKey, float)> into)
        {
            int size = PatchSize(level);
            int minPx = (int)Math.Floor((x - outer) / size);
            int maxPx = (int)Math.Floor((x + outer) / size);
            int minPz = (int)Math.Floor((z - outer) / size);
            int maxPz = (int)Math.Floor((z + outer) / size);
            for (int px = minPx; px <= maxPx; px++)
                for (int pz = minPz; pz <= maxPz; pz++)
                {
                    float x0 = px * size, z0 = pz * size;
                    float nearDx = Math.Max(Math.Max(x0 - x, 0f), x - (x0 + size));
                    float nearDz = Math.Max(Math.Max(z0 - z, 0f), z - (z0 + size));
                    float nearSq = nearDx * nearDx + nearDz * nearDz;
                    if (nearSq > outer * outer)
                    {
                        continue;
                    }

                    float farDx = Math.Max(Math.Abs(x0 - x), Math.Abs(x0 + size - x));
                    float farDz = Math.Max(Math.Abs(z0 - z), Math.Abs(z0 + size - z));
                    if (farDx * farDx + farDz * farDz < inner * inner)
                    {
                        continue; // wholly inside the discard radius — the finer level covers it
                    }

                    into.Add((new FarPatchKey(level, px, pz), nearSq));
                }
        }
    }
}
