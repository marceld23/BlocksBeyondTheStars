// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The small tiling noise sheets behind the shared <c>_Sc_CloudNoise</c> texture (#2394, #3271), baked without Unity so
    /// the headless tests can pin their statistics and the Sky can read the mist's wisp factor at the camera on the CPU —
    /// what the GPU's bilinear sample returns there, so the shader saves that texture fetch. Red is the cloud-shadow sheet of
    /// #2394 (the cloud shadows, caustics and snow edges read it; its seed and recipe are unchanged). Its large octave hangs
    /// on four lattice values, so it is not centred on 0.5 — fine for those effects, useless for a threshold that must mean
    /// "half the area". Green and blue (#3271/#3272) are two more, independent sheets, rank-equalised so every byte value
    /// covers the same area (median 0.5, uniform): the mist's wisps morph between them, the cloud band reads its holes off
    /// green. (The mist cards and the cloud deck use their own, larger texture: <see cref="MistNoise"/>.)
    /// </summary>
    public static class CloudNoiseSheet
    {
        /// <summary>Texels per side.</summary>
        public const int Size = 64;

        /// <summary>The red sheet's seed — the cloud-shadow noise since #2394 (do not change: every effect reading it would shift).</summary>
        public const int ShadowSeed = 20261009;

        /// <summary>The green and the blue sheet's seeds (#3271). Picked so the two patterns are unrelated (correlation ≈ 0.01;
        /// neighbouring seeds of <see cref="Random"/> are not — 20261107 correlates at −0.65), which MistLookTests pins.</summary>
        public const int WispSeedA = 20261106, WispSeedB = 20261113;

        private const int Lattice = 8;

        /// <summary>A sheet like <see cref="Bake"/>, rank-equalised: the texels keep their order but spread evenly over 0..255,
        /// so the share of the area below any value v is v (a threshold at 0.5 splits the sheet in half, at 0.3 leaves 30 %).
        /// Ties are broken by position, so it stays deterministic.</summary>
        public static byte[] BakeEven(int seed)
        {
            byte[] raw = Bake(seed);
            int[] order = new int[raw.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (a, b) => raw[a] != raw[b] ? raw[a].CompareTo(raw[b]) : a.CompareTo(b));
            var even = new byte[raw.Length];
            for (int rank = 0; rank < order.Length; rank++)
            {
                even[order[rank]] = (byte)Math.Round(rank * 255.0 / (order.Length - 1));
            }

            return even;
        }

        /// <summary>One sheet: bilinear value noise on a wrapping 8×8 lattice, three octaves (2, 4 and 8 cells per tile, weighted
        /// 0.55 / 0.3 / 0.15), as bytes (value × 255, row-major, <see cref="Size"/>²). The recipe the Sky used to bake inline
        /// (float arithmetic, round half to even), so the red sheet stays the same.</summary>
        public static byte[] Bake(int seed)
        {
            var rng = new Random(seed);
            float[,] lattice = new float[Lattice, Lattice];
            for (int y = 0; y < Lattice; y++)
            {
                for (int x = 0; x < Lattice; x++)
                {
                    lattice[x, y] = (float)rng.NextDouble();
                }
            }

            var sheet = new byte[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float fx = x / (float)Size, fy = y / (float)Size;
                    float v = (Octave(lattice, fx, fy, 2) * 0.55f) + (Octave(lattice, fx, fy, 4) * 0.3f) + (Octave(lattice, fx, fy, 8) * 0.15f);
                    sheet[(y * Size) + x] = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(v * 255f)));
                }
            }

            return sheet;
        }

        /// <summary>The sheet at texture coordinate (<paramref name="u"/>, <paramref name="v"/>) the way the GPU samples it:
        /// bilinear between texel centres, wrapping (repeat), 0..1. A missing sheet reads as the middle value.</summary>
        public static float Sample(byte[]? sheet, float u, float v)
        {
            if (sheet == null || sheet.Length < Size * Size)
            {
                return 0.5f;
            }

            float gx = (u * Size) - 0.5f, gy = (v * Size) - 0.5f;
            float fx0 = (float)Math.Floor(gx), fy0 = (float)Math.Floor(gy);
            float tx = gx - fx0, ty = gy - fy0;
            int x0 = Wrap(fx0), y0 = Wrap(fy0);
            int x1 = (x0 + 1) % Size, y1 = (y0 + 1) % Size;
            float a = sheet[(y0 * Size) + x0] + ((sheet[(y0 * Size) + x1] - sheet[(y0 * Size) + x0]) * tx);
            float b = sheet[(y1 * Size) + x0] + ((sheet[(y1 * Size) + x1] - sheet[(y1 * Size) + x0]) * tx);
            return (a + ((b - a) * ty)) / 255f;
        }

        private static int Wrap(float texel)
        {
            double m = texel % Size;
            int i = (int)(m < 0 ? m + Size : m);
            return i >= Size ? 0 : i;
        }

        /// <summary>Bilinear value noise on the wrapping lattice with <paramref name="cells"/> points per tile (cells divides 8),
        /// smoothstepped between the lattice points.</summary>
        private static float Octave(float[,] lattice, float fx, float fy, int cells)
        {
            float gx = fx * cells, gy = fy * cells;
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float tx = gx - x0, ty = gy - y0;
            tx = tx * tx * (3f - (2f * tx));
            ty = ty * ty * (3f - (2f * ty));
            int step = Lattice / cells;
            float a = Lerp(At(lattice, x0, y0, cells, step), At(lattice, x0 + 1, y0, cells, step), tx);
            float b = Lerp(At(lattice, x0, y0 + 1, cells, step), At(lattice, x0 + 1, y0 + 1, cells, step), tx);
            return Lerp(a, b, ty);
        }

        private static float At(float[,] lattice, int x, int y, int cells, int step) => lattice[(x % cells) * step, (y % cells) * step];

        // Mathf.Lerp's recipe (a + (b − a) · clamp01(t)), so the baked bytes match the old Unity-side bake.
        private static float Lerp(float a, float b, float t) => a + ((b - a) * (t < 0f ? 0f : t > 1f ? 1f : t));
    }
}
