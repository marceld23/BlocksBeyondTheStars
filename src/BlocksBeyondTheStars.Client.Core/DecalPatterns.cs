// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The procedural mark textures of the voxel-face decals (#3058), baked into one RGBA32 atlas the client uploads once:
    /// 2×2 tiles of 128² — <see cref="Scorch"/> (soot with spatter rays), <see cref="Dent"/> (a pit with a crack star),
    /// <see cref="QuakeRing"/> (a cracked ring where a giant's foot came down) and <see cref="BurnCrater"/> (a scorched
    /// blast crater with cracks). R = how much the mark darkens (0 = untouched), G = where it glows while hot (embers),
    /// B = 0, A = 255. Every tile is zero well inside its border, so neither the clamp nor the mip chain bleeds one mark into
    /// the next. Soot, scorch and cracks only — no blood (kid-friendly). Unity-free and deterministic, so it is tested headless.
    /// </summary>
    public static class DecalPatterns
    {
        public const int TileSize = 128;
        public const int AtlasSize = TileSize * 2;

        public const int Scorch = 0;
        public const int Dent = 1;
        public const int QuakeRing = 2;
        public const int BurnCrater = 3;

        private static readonly Segment[] DentCracks = Cracks(seed: 11, rays: 7, r0: 0.1f, r1Min: 0.5f, r1Max: 0.88f, width: 0.05f);
        private static readonly Segment[] QuakeCracks = Cracks(seed: 23, rays: 11, r0: 0.18f, r1Min: 0.62f, r1Max: 0.9f, width: 0.045f);
        private static readonly Segment[] BurnCracks = Cracks(seed: 37, rays: 9, r0: 0.2f, r1Min: 0.55f, r1Max: 0.88f, width: 0.04f);

        /// <summary>The atlas u of a tile's lower-left corner (0 or 0.5).</summary>
        public static float TileU(int tile) => (tile & 1) * 0.5f;

        /// <summary>The atlas v of a tile's lower-left corner (0 or 0.5).</summary>
        public static float TileV(int tile) => (tile >> 1) * 0.5f;

        /// <summary>The whole atlas as RGBA32 bytes, rows bottom-up (Unity's texture data order).</summary>
        public static byte[] BuildAtlas()
        {
            var px = new byte[AtlasSize * AtlasSize * 4];
            for (int tile = 0; tile < 4; tile++)
            {
                int x0 = (tile & 1) * TileSize, y0 = (tile >> 1) * TileSize;
                for (int y = 0; y < TileSize; y++)
                {
                    for (int x = 0; x < TileSize; x++)
                    {
                        // Disc coordinates: −1..1 across the tile, the mark's edge at radius 1.
                        float u = (x + 0.5f) / TileSize * 2f - 1f, v = (y + 0.5f) / TileSize * 2f - 1f;
                        var (soot, ember) = Sample(tile, u, v);
                        int i = ((y0 + y) * AtlasSize + x0 + x) * 4;
                        px[i] = ToByte(soot);
                        px[i + 1] = ToByte(ember);
                        px[i + 3] = 255;
                    }
                }
            }

            return px;
        }

        /// <summary>A tile's (soot, ember) at disc coordinates (<paramref name="x"/>, <paramref name="y"/>) ∈ [−1, 1]²: the
        /// centre is 0, the mark ends at radius 1; both are 0 near the tile's border.</summary>
        public static (float Soot, float Ember) Sample(int tile, float x, float y)
        {
            float d = (float)Math.Sqrt(x * x + y * y);
            float ang = (float)Math.Atan2(y, x);
            float grain = 0.55f + 0.45f * Fbm(x * 5f + 11f, y * 5f + 11f, tile * 7 + 3);
            var (soot, ember) = tile switch
            {
                Scorch => ScorchAt(x, y, d, ang, grain),
                Dent => DentAt(x, y, d, grain),
                QuakeRing => QuakeAt(x, y, d, grain),
                _ => BurnAt(x, y, d, ang, grain),
            };
            float border = 1f - Smooth(0.84f, 0.97f, Math.Max(Math.Abs(x), Math.Abs(y)));
            return (Clamp01(soot) * border, Clamp01(ember) * border);
        }

        private static (float, float) ScorchAt(float x, float y, float d, float ang, float grain)
        {
            float wobble = Fbm((float)Math.Cos(ang) * 1.7f + 5f, (float)Math.Sin(ang) * 1.7f + 5f, 41);
            float rr = d / (0.62f + 0.38f * wobble);
            float body = 1f - Smooth(0.35f, 1f, rr);
            float rays = Pow(0.5f + 0.5f * (float)Math.Cos(ang * 11f + 3f * wobble), 8) * (1f - Smooth(0.4f, 1.15f, rr)) * 0.45f;
            float core = 1f - Smooth(0f, 0.35f, d);
            float soot = body * grain * 0.9f + rays * grain + core * 0.25f;
            float ember = (1f - Smooth(0.05f, 0.45f, d)) * (0.45f + 0.55f * Fbm(x * 7f + 2f, y * 7f + 2f, 43));
            return (soot, ember);
        }

        private static (float, float) DentAt(float x, float y, float d, float grain)
        {
            float pit = 1f - Smooth(0.08f, 0.2f, d);
            float halo = (1f - Smooth(0.15f, 0.6f, d)) * 0.3f * grain;
            float crack = CrackAt(DentCracks, x, y);
            return (Math.Max(pit * 0.95f, Math.Max(halo, crack * 0.85f)), 0f);
        }

        private static (float, float) QuakeAt(float x, float y, float d, float grain)
        {
            float ringD = (d - 0.6f) / 0.14f;
            float ring = (float)Math.Exp(-ringD * ringD) * 0.45f * grain;
            float inner = (1f - Smooth(0.25f, 0.7f, d)) * 0.2f * grain;
            float crack = CrackAt(QuakeCracks, x, y);
            return (Math.Max(inner + ring, crack * 0.9f), 0f);
        }

        private static (float, float) BurnAt(float x, float y, float d, float ang, float grain)
        {
            float wobble = Fbm((float)Math.Cos(ang) * 1.9f + 9f, (float)Math.Sin(ang) * 1.9f + 9f, 47);
            float rr = d / (0.72f + 0.28f * wobble);
            float body = (1f - Smooth(0.45f, 1f, rr)) * (0.6f + 0.4f * grain);
            float rimD = (rr - 0.62f) / 0.12f;
            float rim = (float)Math.Exp(-rimD * rimD) * 0.2f;
            float rays = Pow(0.5f + 0.5f * (float)Math.Cos(ang * 13f + 4f * wobble), 6) * (1f - Smooth(0.5f, 1.1f, rr)) * 0.35f;
            float crack = CrackAt(BurnCracks, x, y);
            float soot = Math.Max(body * 0.85f + rim + rays, crack * 0.95f);
            float patches = Smooth(0.5f, 0.75f, Fbm(x * 6f + 7f, y * 6f + 7f, 53));
            float ember = (1f - Smooth(0.15f, 0.7f, d)) * patches + crack * 0.6f * (1f - Smooth(0.3f, 0.8f, d));
            return (soot, ember);
        }

        // ---------------------------------------------------------------- cracks

        private readonly struct Segment
        {
            public Segment(float ax, float ay, float bx, float by, float wa, float wb)
            {
                Ax = ax;
                Ay = ay;
                Bx = bx;
                By = by;
                Wa = wa;
                Wb = wb;
            }

            public float Ax { get; }

            public float Ay { get; }

            public float Bx { get; }

            public float By { get; }

            /// <summary>Crack width at the segment's start and end (disc units; a tile pixel is 2/128).</summary>
            public float Wa { get; }

            public float Wb { get; }
        }

        /// <summary>A star of jagged rays from radius <paramref name="r0"/> out to a random length, each with one side branch.</summary>
        private static Segment[] Cracks(int seed, int rays, float r0, float r1Min, float r1Max, float width)
        {
            var list = new List<Segment>();
            for (int i = 0; i < rays; i++)
            {
                float a = (float)(2.0 * Math.PI * (i + 0.5f + (Hash(i, 1, seed) - 0.5f) * 0.6f) / rays);
                float r1 = r1Min + (r1Max - r1Min) * Hash(i, 3, seed);
                AddRay(list, (float)Math.Cos(a) * r0, (float)Math.Sin(a) * r0, a, r1 - r0, width, seed * 31 + i, branch: true);
            }

            return list.ToArray();
        }

        private static void AddRay(List<Segment> list, float x, float y, float angle, float length, float width, int seed, bool branch)
        {
            const int Steps = 4;
            float step = length / Steps;
            for (int s = 0; s < Steps; s++)
            {
                angle += (Hash(s, 7, seed) - 0.5f) * 0.5f;
                float nx = x + (float)Math.Cos(angle) * step, ny = y + (float)Math.Sin(angle) * step;
                float wa = width * (1f - 0.7f * s / Steps), wb = width * (1f - 0.7f * (s + 1) / Steps);
                list.Add(new Segment(x, y, nx, ny, wa, wb));
                if (branch && s == 1)
                {
                    float side = Hash(s, 9, seed) < 0.5f ? -0.65f : 0.65f;
                    AddRay(list, nx, ny, angle + side, length * 0.35f, wb * 0.75f, seed + 101, branch: false);
                }

                x = nx;
                y = ny;
            }
        }

        /// <summary>How much of a crack covers the point: 1 on a crack's centre line, fading over its width.</summary>
        private static float CrackAt(Segment[] cracks, float x, float y)
        {
            float best = 0f;
            foreach (var s in cracks)
            {
                float ex = s.Bx - s.Ax, ey = s.By - s.Ay;
                float len2 = ex * ex + ey * ey;
                float t = len2 > 1e-8f ? Clamp01(((x - s.Ax) * ex + (y - s.Ay) * ey) / len2) : 0f;
                float px = s.Ax + ex * t - x, py = s.Ay + ey * t - y;
                float dist = (float)Math.Sqrt(px * px + py * py);
                float w = s.Wa + (s.Wb - s.Wa) * t;
                best = Math.Max(best, 1f - Smooth(w * 0.35f, w, dist));
            }

            return best;
        }

        // ---------------------------------------------------------------- noise

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Noise(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            float u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed), e = Hash(ix + 1, iy + 1, seed);
            return a + (b - a) * u + (c - a) * v + (a - b - c + e) * u * v;
        }

        /// <summary>Three octaves of value noise, 0..1.</summary>
        private static float Fbm(float x, float y, int seed)
            => 0.5f * Noise(x, y, seed) + 0.3f * Noise(x * 2f, y * 2f, seed + 1) + 0.2f * Noise(x * 4f, y * 4f, seed + 2);

        private static float Smooth(float e0, float e1, float v)
        {
            float t = Clamp01((v - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static float Pow(float v, int n)
        {
            float r = 1f;
            for (int i = 0; i < n; i++)
            {
                r *= v;
            }

            return r;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static byte ToByte(float v) => (byte)Math.Round(Clamp01(v) * 255f);
    }
}
