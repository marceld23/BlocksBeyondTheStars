// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A seamless (tileable) fractal value noise for the mist cards (#3273) and the sea of clouds (#3272). Both scroll their
    /// noise with the wind and let it evolve, so it must repeat without a seam — <c>Clouds.GenerateCloudTexture</c> paints
    /// single clamped puffs and cannot be scrolled. The lattice wraps every octave at the tile edge, and the result is
    /// stretched to the full 0..1 range so the shaders' thresholds mean the same on every seed. Unity-free so the headless
    /// tests can pin the seam; the client uploads it once as a mip-mapped repeat texture.
    /// </summary>
    public static class MistNoise
    {
        /// <summary><paramref name="size"/> × <paramref name="size"/> values in 0..1, row-major (<c>y * size + x</c>), that
        /// tile seamlessly: value noise on a wrapping lattice of <paramref name="cells"/> cells per side, with
        /// <paramref name="octaves"/> octaves (each doubles the cells and halves the amplitude). Deterministic per
        /// <paramref name="seed"/>.</summary>
        public static float[] Tileable(int size, int cells, int octaves, int seed)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "The noise needs at least one texel.");
            }

            cells = Math.Max(1, cells);
            octaves = Math.Max(1, octaves);
            var values = new float[size * size];
            float min = float.MaxValue, max = float.MinValue;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = Fbm(x / (float)size, y / (float)size, cells, octaves, seed);
                    values[(y * size) + x] = v;
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }

            float range = max - min;
            if (range > 1e-6f)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = (values[i] - min) / range;
                }
            }

            return values;
        }

        private static float Fbm(float fx, float fy, int cells, int octaves, int seed)
        {
            float sum = 0f, norm = 0f, amp = 1f;
            int c = cells;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Lattice(fx * c, fy * c, c, seed + (o * 1013));
                norm += amp;
                amp *= 0.5f;
                c *= 2;
            }

            return sum / norm;
        }

        /// <summary>Smooth value noise at lattice coordinates (<paramref name="gx"/>, <paramref name="gy"/>) on a lattice that
        /// repeats every <paramref name="period"/> cells.</summary>
        private static float Lattice(float gx, float gy, int period, int seed)
        {
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float tx = gx - x0, ty = gy - y0;
            tx = tx * tx * (3f - (2f * tx));
            ty = ty * ty * (3f - (2f * ty));
            int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period);
            int ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
            float a = Lerp(Hash01(xa, ya, seed), Hash01(xb, ya, seed), tx);
            float b = Lerp(Hash01(xa, yb, seed), Hash01(xb, yb, seed), tx);
            return Lerp(a, b, ty);
        }

        private static int Wrap(int v, int period)
        {
            int r = v % period;
            return r < 0 ? r + period : r;
        }

        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
