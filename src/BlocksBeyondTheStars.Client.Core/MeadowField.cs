// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The meadow of the High preset (#3056), Unity-free so the headless suite pins it: which grass top grows a flower and
    /// which flower, the strand seed of a carpet cell, the mirror of the mesher's top-face variant pick (so the carpet takes
    /// its colour from exactly the texel it grows out of) and how many flowers the season lets bloom. Every pick is a pure
    /// function of the canonical world cell, so all clients see the same meadow without any traffic.
    /// </summary>
    public static class MeadowField
    {
        /// <summary>One grass top in this many grows a flower (before the season thins them out).</summary>
        public const int FlowerEvery = 9;

        /// <summary>The flower height range in metres.</summary>
        public const float MinFlowerHeight = 0.15f;

        /// <summary>The flower height range in metres.</summary>
        public const float MaxFlowerHeight = 0.25f;

        /// <summary>How far (metres) a flower may stand off its cell centre, per axis.</summary>
        public const float MaxFlowerOffset = 0.3f;

        /// <summary>The share of the flowers in bloom in summer (and on worlds without seasons); spring opens all of them.</summary>
        public const float SummerDensity = 0.7f;

        /// <summary>The share of the summer flowers autumn leaves standing; winter takes the rest.</summary>
        public const float AutumnShare = 0.3f;

        /// <summary>The autumn/winter look (<see cref="SeasonLook.Target"/>) at which autumn's thinning is complete. Winter
        /// starts with 0.3 of the autumn look left, so the thinning holds across the turn of the season instead of
        /// letting the flowers come back for the first days of winter.</summary>
        private const float AutumnFull = 0.3f;

        /// <summary>The floral species whose per-world colours (<c>FloraTints.For</c>) the meadow flowers wear.</summary>
        public static readonly string[] FlowerSpeciesKeys = { "flora_flower", "flora_daisy", "flora_tulip", "flora_bellflower" };

        private const ulong FlowerSalt = 0x5DEECE66DUL;
        private const ulong SeedSalt = 0x2545F4914F6CDD1DUL;

        /// <summary>One flower: species index into <see cref="FlowerSpeciesKeys"/>, its bloom rank (0..1 — it blooms while
        /// the season's <see cref="FlowerDensity"/> is above it), yaw in degrees, height in metres and its offset off the
        /// cell centre.</summary>
        public readonly struct FlowerPick
        {
            public FlowerPick(int species, float rank, float yaw, float height, float offsetX, float offsetZ)
            {
                Species = species;
                Rank = rank;
                Yaw = yaw;
                Height = height;
                OffsetX = offsetX;
                OffsetZ = offsetZ;
            }

            public int Species { get; }

            public float Rank { get; }

            public float Yaw { get; }

            public float Height { get; }

            public float OffsetX { get; }

            public float OffsetZ { get; }
        }

        /// <summary>Whether the grass block at this canonical cell grows a meadow flower (about one in <see cref="FlowerEvery"/>).</summary>
        public static bool IsFlowerCell(int x, int y, int z) => Mix(x, y, z, FlowerSalt) % (ulong)FlowerEvery == 0UL;

        /// <summary>The flower of a cell (only meaningful where <see cref="IsFlowerCell"/> holds).</summary>
        public static FlowerPick Flower(int x, int y, int z)
        {
            ulong state = Mix(x, y, z, FlowerSalt ^ 0x9E3779B97F4A7C15UL);
            int species = (int)(Next(ref state) % (ulong)FlowerSpeciesKeys.Length);
            float rank = Unit(Next(ref state));
            float yaw = Unit(Next(ref state)) * 360f;
            float height = MinFlowerHeight + (Unit(Next(ref state)) * (MaxFlowerHeight - MinFlowerHeight));
            float ox = (Unit(Next(ref state)) - 0.5f) * 2f * MaxFlowerOffset;
            float oz = (Unit(Next(ref state)) - 0.5f) * 2f * MaxFlowerOffset;
            return new FlowerPick(species, rank, yaw, height, ox, oz);
        }

        /// <summary>The strand seed (0..1) of a carpet cell: the shell shader scatters its strands from it.</summary>
        public static float CellSeed(int x, int y, int z) => Unit(Mix(x, y, z, SeedSalt));

        /// <summary>The mesher's natural-block variation of a top face (ChunkMesher, "Natural-block variation"): which tile
        /// (0 = the block's own, k = its k-th variant) and how many 90° turns of the UVs. Must stay byte-for-byte the
        /// mesher's formula, or the carpet's strands stop matching the texels they grow out of.</summary>
        public static (int Pick, int Turn) TopVariant(int x, int y, int z, int variantCount)
        {
            int hash = unchecked((x * 73856093) ^ (y * 19349663) ^ (z * 83492791));
            int pick = (int)((uint)hash % (uint)(Math.Max(0, variantCount) + 1));
            return (pick, (hash >> 8) & 3);
        }

        /// <summary>The share (0..1) of the meadow flowers in bloom for the season look (<c>_Sc_Season</c>: autumn, winter,
        /// spring bloom — <see cref="SeasonLook.Target"/>, eased by Sky): <see cref="SummerDensity"/> in summer, all of them
        /// in spring, <see cref="AutumnShare"/> of the summer ones in autumn, none in winter. Each flower blooms while the
        /// share is above its <see cref="FlowerPick.Rank"/>, so the field fills and empties smoothly.</summary>
        public static float FlowerDensity(float autumn, float winter, float bloom)
        {
            float a = Clamp01(autumn), w = Clamp01(winter), b = Clamp01(bloom);
            float open = SummerDensity + ((1f - SummerDensity) * b);
            float thinned = 1f - ((1f - AutumnShare) * Clamp01((a + w) / AutumnFull));
            return Clamp01(open * thinned * (1f - w));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>A well-mixed 64-bit hash of a canonical cell (the splitmix finaliser over the three coordinates).</summary>
        private static ulong Mix(int x, int y, int z, ulong salt)
        {
            unchecked
            {
                ulong h = ((ulong)(uint)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL)
                    ^ ((ulong)(uint)z * 0x165667B19E3779F9UL) ^ salt;
                return Finalise(h);
            }
        }

        /// <summary>The next draw of a splitmix64 sequence.</summary>
        private static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                return Finalise(state);
            }
        }

        private static ulong Finalise(ulong h)
        {
            unchecked
            {
                h ^= h >> 30;
                h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 27;
                h *= 0x94D049BB133111EBUL;
                h ^= h >> 31;
                return h;
            }
        }

        /// <summary>A uniform [0, 1) float from the top 24 bits.</summary>
        private static float Unit(ulong h) => (h >> 40) * (1f / 16777216f);
    }
}
