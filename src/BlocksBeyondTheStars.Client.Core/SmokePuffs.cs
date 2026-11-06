// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The procedural smoke puffs (#3274), baked into one small RGBA32 atlas the client uploads once: 2×2 tiles of
    /// <see cref="TileSize"/>², each a different billowy puff — a few overlapping round lobes, their outline frayed by a
    /// little domain warp. The <c>ParticleSmoke</c> shader picks a tile per particle and rotates it.
    /// <list type="bullet">
    /// <item>A = the puff's density (soft edge, solid core);</item>
    /// <item>G = the billows: high on each lobe's dome, low in the creases between them — the shader lets the lumps
    /// catch the sun unevenly;</item>
    /// <item>R = an independent noise the shader erodes the puff against as it ages, so the edges fray and holes open
    /// instead of the whole puff fading evenly;</item>
    /// <item>B = 0.</item>
    /// </list>
    /// Every tile is empty well inside its border, so neither the clamp nor the mip chain bleeds one puff into the next.
    /// Unity-free and deterministic, so it is tested headless.
    /// </summary>
    public static class SmokePuffs
    {
        public const int TileSize = 64;
        public const int AtlasSize = TileSize * 2;
        public const int TileCount = 4;

        private const int LobesPerTile = 7;

        /// <summary>The lobes of each tile: centre (x, y) in −1..1 and radius, the first one the big central body.</summary>
        private static readonly (float X, float Y, float R)[][] Lobes = BuildLobes();

        /// <summary>The whole atlas as RGBA32 bytes, rows bottom-up (Unity's texture data order).</summary>
        public static byte[] BuildAtlas()
        {
            var px = new byte[AtlasSize * AtlasSize * 4];
            for (int tile = 0; tile < TileCount; tile++)
            {
                int x0 = (tile & 1) * TileSize, y0 = (tile >> 1) * TileSize;
                for (int y = 0; y < TileSize; y++)
                {
                    for (int x = 0; x < TileSize; x++)
                    {
                        float u = (x + 0.5f) / TileSize * 2f - 1f, v = (y + 0.5f) / TileSize * 2f - 1f;
                        var s = Sample(tile, u, v);
                        int i = ((y0 + y) * AtlasSize + x0 + x) * 4;
                        px[i] = ToByte(s.Erosion);
                        px[i + 1] = ToByte(s.Lumps);
                        px[i + 3] = ToByte(s.Density);
                    }
                }
            }

            return px;
        }

        /// <summary>A tile's values at (<paramref name="x"/>, <paramref name="y"/>) ∈ [−1, 1]²: the density is 1 in the core
        /// and 0 near the border; lumps and erosion are 0..1.</summary>
        public static (float Density, float Lumps, float Erosion) Sample(int tile, float x, float y)
        {
            int t = ((tile % TileCount) + TileCount) % TileCount;
            float seed = t * 13.7f;

            // A little domain warp frays the round lobes into a ragged, smoky outline.
            float wx = x + (Fbm(x * 2.6f + seed, y * 2.6f + 3.1f, t * 5 + 1) - 0.5f) * 0.22f;
            float wy = y + (Fbm(x * 2.6f + 7.7f, y * 2.6f + seed, t * 5 + 2) - 0.5f) * 0.22f;

            float height = 0f;   // the tallest dome over this point, in units of the central lobe's radius
            float second = 0f;   // the next tallest: where the two meet is a crease between billows
            foreach (var lobe in Lobes[t])
            {
                float dx = wx - lobe.X, dy = wy - lobe.Y;
                float q = 1f - (dx * dx + dy * dy) / (lobe.R * lobe.R);
                if (q <= 0f)
                {
                    continue;
                }

                float h = (float)Math.Sqrt(q) * lobe.R / Lobes[t][0].R;
                if (h > height)
                {
                    second = height;
                    height = h;
                }
                else if (h > second)
                {
                    second = h;
                }
            }

            float crease = Clamp01((height - second) * 4f); // 0 in a crease, 1 on a lone dome

            float grain = Fbm(x * 6f + seed, y * 6f + 1.3f, t * 5 + 3);
            float density = Smooth(0.02f, 0.55f, height) * (0.82f + 0.18f * grain);
            float lumps = Clamp01(0.25f + 0.55f * Clamp01(height) + 0.35f * crease + (grain - 0.5f) * 0.25f);
            float erosion = Clamp01((Fbm(x * 3.4f + seed + 9f, y * 3.4f + 5.3f, t * 5 + 4) - 0.5f) * 1.8f + 0.5f);

            float border = 1f - Smooth(0.82f, 0.95f, Math.Max(Math.Abs(x), Math.Abs(y)));
            return (Clamp01(density) * border, density > 0f ? lumps : 0f, erosion);
        }

        private static (float X, float Y, float R)[][] BuildLobes()
        {
            var all = new (float, float, float)[TileCount][];
            for (int t = 0; t < TileCount; t++)
            {
                var lobes = new (float, float, float)[LobesPerTile];
                lobes[0] = ((Hash(t, 0, 1) - 0.5f) * 0.1f, (Hash(t, 0, 2) - 0.5f) * 0.1f, 0.42f + 0.06f * Hash(t, 0, 3));
                float start = Hash(t, 0, 4) * 6.2832f;
                for (int i = 1; i < LobesPerTile; i++)
                {
                    float a = start + i * 6.2832f / (LobesPerTile - 1) + (Hash(t, i, 1) - 0.5f) * 0.5f;
                    float d = 0.24f + 0.16f * Hash(t, i, 2);
                    float r = 0.2f + 0.14f * Hash(t, i, 3);
                    lobes[i] = ((float)Math.Cos(a) * d, (float)Math.Sin(a) * d, r); // d + r ≤ 0.74: inside the border
                }

                all[t] = lobes;
            }

            return all;
        }

        /// <summary>Four-octave value noise, 0..1.</summary>
        private static float Fbm(float x, float y, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < 4; o++)
            {
                sum += Value(x, y, seed + o * 31) * amp;
                norm += amp;
                x *= 2.03f;
                y *= 2.03f;
                amp *= 0.5f;
            }

            return sum / norm;
        }

        private static float Value(float x, float y, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float fx = x - xi, fy = y - yi;
            float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
        }

        /// <summary>0..1 from three integers (a MurmurHash3-style mix).</summary>
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Smooth(float e0, float e1, float v)
        {
            float t = Clamp01((v - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static byte ToByte(float v) => (byte)Math.Round(Clamp01(v) * 255f);
    }
}
