// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Fourier half of <see cref="TileFilters.MakeSeamless"/>: Moisan's smooth component of a square plane
    /// whose side is a power of two, ported line by line from <c>smooth_component</c> in
    /// <c>tools/ai-assets/heal_seams.py</c> (an iterative radix-2 FFT, rows then columns; the inverse unnormalised and
    /// divided by the texel count at the end). Pure arithmetic on doubles: deterministic for a given input.
    /// </summary>
    internal static class SeamFft
    {
        /// <summary>The smooth part s of <paramref name="plane"/> (row-major, <paramref name="side"/> × side): the solution
        /// of the Poisson equation whose source is the jumps across the borders alone. The healed plane is plane − s.</summary>
        public static double[] SmoothComponent(double[] plane, int side)
        {
            int n = side * side;
            var re = new double[n];
            var im = new double[n];
            for (int y = 0; y < side; y++)
            {
                double jump = plane[(y * side) + side - 1] - plane[y * side];
                re[y * side] += jump;
                re[(y * side) + side - 1] -= jump;
            }

            for (int x = 0; x < side; x++)
            {
                double jump = plane[((side - 1) * side) + x] - plane[x];
                re[x] += jump;
                re[((side - 1) * side) + x] -= jump;
            }

            Fft2(re, im, side, inverse: false);
            var cos = new double[side];
            for (int q = 0; q < side; q++)
            {
                cos[q] = 2.0 * Math.Cos(2.0 * Math.PI * q / side);
            }

            for (int q = 0; q < side; q++)
            {
                for (int r = 0; r < side; r++)
                {
                    int i = (q * side) + r;
                    if (q == 0 && r == 0)
                    {
                        re[i] = im[i] = 0.0;
                        continue;
                    }

                    double d = cos[q] + cos[r] - 4.0;
                    re[i] /= d;
                    im[i] /= d;
                }
            }

            Fft2(re, im, side, inverse: true);
            var s = new double[n];
            for (int i = 0; i < n; i++)
            {
                s[i] = re[i] / n;
            }

            return s;
        }

        private static void Fft2(double[] re, double[] im, int side, bool inverse)
        {
            var lineRe = new double[side];
            var lineIm = new double[side];
            for (int y = 0; y < side; y++)
            {
                Array.Copy(re, y * side, lineRe, 0, side);
                Array.Copy(im, y * side, lineIm, 0, side);
                Fft(lineRe, lineIm, inverse);
                Array.Copy(lineRe, 0, re, y * side, side);
                Array.Copy(lineIm, 0, im, y * side, side);
            }

            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y++)
                {
                    lineRe[y] = re[(y * side) + x];
                    lineIm[y] = im[(y * side) + x];
                }

                Fft(lineRe, lineIm, inverse);
                for (int y = 0; y < side; y++)
                {
                    re[(y * side) + x] = lineRe[y];
                    im[(y * side) + x] = lineIm[y];
                }
            }
        }

        /// <summary>In-place iterative radix-2 FFT; the inverse is unnormalised (as the script's).</summary>
        private static void Fft(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }

                j |= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            double sign = inverse ? 1.0 : -1.0;
            for (int size = 2; size <= n; size *= 2)
            {
                double angle = sign * 2.0 * Math.PI / size;
                double stepRe = Math.Cos(angle), stepIm = Math.Sin(angle);
                int half = size / 2;
                for (int start = 0; start < n; start += size)
                {
                    double wRe = 1.0, wIm = 0.0;
                    for (int k = start; k < start + half; k++)
                    {
                        double tRe = (wRe * re[k + half]) - (wIm * im[k + half]);
                        double tIm = (wRe * im[k + half]) + (wIm * re[k + half]);
                        re[k + half] = re[k] - tRe;
                        im[k + half] = im[k] - tIm;
                        re[k] += tRe;
                        im[k] += tIm;
                        double nextRe = (wRe * stepRe) - (wIm * stepIm);
                        wIm = (wRe * stepIm) + (wIm * stepRe);
                        wRe = nextRe;
                    }
                }
            }
        }
    }
}
