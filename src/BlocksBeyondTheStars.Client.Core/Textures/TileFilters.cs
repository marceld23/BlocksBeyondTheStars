// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Textures;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The soft whole-tile operations of the texture editor (#3939), on one raw 64 × 64 RGBA32 frame (rows
    /// bottom-up, the layout of <see cref="PixelCanvasModel.Frames"/>). Engine-free and deterministic: the same frame in
    /// gives the same bytes out, no randomness, so a saved calm tile can be recognised again (<see cref="SameShown"/>).
    /// Every operation leaves the alpha channel as it is — a plant's or a hide's silhouette is the painter's.
    /// <list type="bullet">
    /// <item><see cref="Calm"/>: every texel a share of the way to the tile's mean colour (the block shader's texture calm,
    /// baked into the pixels by the editor's "calm when saving").</item>
    /// <item><see cref="Smooth"/>: one pass of a 3 × 3 binomial blur that wraps around the edges (tiles repeat).</item>
    /// <item><see cref="MakeSeamless"/>: Moisan's periodic + smooth decomposition, ported from
    /// <c>tools/ai-assets/heal_seams.py</c> (its <c>heal(align=False)</c>), with its seam measure <see cref="SeamRatio"/>.</item>
    /// <item><see cref="WithLuminance"/> / <see cref="Greyscale"/>: the "brightness only" paint mode for tiles the world
    /// tints (plants, leaves, bark are drawn as brightness times a world colour).</item>
    /// </list>
    /// </summary>
    public static class TileFilters
    {
        public const int Size = TextureTiles.Size;

        // Rec. 601 luma, as PixelCanvasModel.Adjust and the seam script use.
        private const double LumaR = 0.299, LumaG = 0.587, LumaB = 0.114;

        // ---------------------------------------------------------------- calm

        /// <summary>A copy of <paramref name="rgba"/> with every texel moved <paramref name="share"/> (0..1) of the way to
        /// the tile's mean colour. The mean is weighted by alpha (a cutout tile's holes do not darken its leaves); a tile
        /// without a visible texel, or a share of 0, comes back unchanged.</summary>
        public static byte[] Calm(byte[] rgba, double share)
        {
            var result = (byte[])rgba.Clone();
            CalmInPlace(result, share);
            return result;
        }

        /// <summary><see cref="Calm"/> in place.</summary>
        public static void CalmInPlace(byte[] rgba, double share)
        {
            share = Math.Max(0.0, Math.Min(1.0, share));
            if (share <= 0.0 || rgba == null || rgba.Length < 4)
            {
                return;
            }

            long r = 0, g = 0, b = 0, weight = 0;
            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                int a = rgba[i + 3];
                r += rgba[i] * a;
                g += rgba[i + 1] * a;
                b += rgba[i + 2] * a;
                weight += a;
            }

            if (weight == 0)
            {
                return;
            }

            double mr = r / (double)weight, mg = g / (double)weight, mb = b / (double)weight;
            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                rgba[i] = ToByte(rgba[i] + ((mr - rgba[i]) * share));
                rgba[i + 1] = ToByte(rgba[i + 1] + ((mg - rgba[i + 1]) * share));
                rgba[i + 2] = ToByte(rgba[i + 2] + ((mb - rgba[i + 2]) * share));
            }
        }

        // ---------------------------------------------------------------- smooth

        /// <summary>One pass of the 3 × 3 binomial blur (1 2 1 / 2 4 2 / 1 2 1) over a copy of the tile, wrapping around the
        /// edges because tiles repeat. The colours are weighted by alpha, so a see-through texel lends no black to its
        /// neighbours; the alpha itself is kept.</summary>
        public static byte[] Smooth(byte[] rgba)
        {
            var result = (byte[])rgba.Clone();
            if (rgba.Length != TextureTiles.BytesPerFrame)
            {
                return result;
            }

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    double r = 0, g = 0, b = 0, w = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int i = Wrapped(x + dx, y + dy);
                            double k = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1) * rgba[i + 3];
                            r += rgba[i] * k;
                            g += rgba[i + 1] * k;
                            b += rgba[i + 2] * k;
                            w += k;
                        }
                    }

                    if (w > 0)
                    {
                        int o = ((y * Size) + x) * 4;
                        result[o] = ToByte(r / w);
                        result[o + 1] = ToByte(g / w);
                        result[o + 2] = ToByte(b / w);
                    }
                }
            }

            return result;
        }

        private static int Wrapped(int x, int y) => (((((y % Size) + Size) % Size) * Size) + (((x % Size) + Size) % Size)) * 4;

        // ---------------------------------------------------------------- seams

        /// <summary>The seam measure of <c>heal_seams.py</c> on the tile's luma (0..1): the mean step across the wrap (first
        /// against last row, first against last column) in units of the mean ordinary neighbour step, the larger of the
        /// two directions. A seamless tile sits around 1; the creature tests allow at most 1.3. A direction without any
        /// inner step counts 0 when its wrap has none either, else infinity.</summary>
        public static double SeamRatio(byte[] rgba)
        {
            var lum = new double[Size * Size];
            for (int i = 0; i < lum.Length; i++)
            {
                int o = i * 4;
                lum[i] = ((LumaR * rgba[o]) + (LumaG * rgba[o + 1]) + (LumaB * rgba[o + 2])) / 255.0;
            }

            double innerV = 0, innerH = 0, seamV = 0, seamH = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    double v = lum[(y * Size) + x];
                    if (y + 1 < Size)
                    {
                        innerV += Math.Abs(lum[((y + 1) * Size) + x] - v);
                    }

                    if (x + 1 < Size)
                    {
                        innerH += Math.Abs(lum[(y * Size) + x + 1] - v);
                    }
                }

                seamH += Math.Abs(lum[y * Size] - lum[(y * Size) + Size - 1]); // row y: first column against last
            }

            for (int x = 0; x < Size; x++)
            {
                seamV += Math.Abs(lum[x] - lum[((Size - 1) * Size) + x]); // column x: first row against last
            }

            innerV /= (Size - 1) * Size;
            innerH /= Size * (Size - 1);
            seamV /= Size;
            seamH /= Size;
            return Math.Max(Ratio(seamV, innerV), Ratio(seamH, innerH));
        }

        private static double Ratio(double seam, double inner)
            => inner > 0 ? seam / inner : seam == 0 ? 0.0 : double.PositiveInfinity;

        /// <summary>A copy of the tile healed so it repeats without a visible seam: each colour channel is replaced by its
        /// periodic part (Moisan, "Periodic plus smooth image decomposition", 2011) — the smooth part that carries the jumps
        /// across the borders is taken away, so only the brightness near the edges bends a little and no texel is blended
        /// with a shifted copy. A constant channel stays exactly as it is, and so does the alpha (unlike the script, which
        /// heals alpha too: in the editor a cutout's silhouette is the painter's). The script's lattice alignment
        /// (<c>align_periods</c>, which resamples bicubically) is not ported.</summary>
        public static byte[] MakeSeamless(byte[] rgba)
        {
            var result = (byte[])rgba.Clone();
            if (rgba.Length != TextureTiles.BytesPerFrame)
            {
                return result;
            }

            var plane = new double[Size * Size];
            for (int channel = 0; channel < 3; channel++)
            {
                bool constant = true;
                for (int i = 0; i < plane.Length; i++)
                {
                    plane[i] = rgba[(i * 4) + channel];
                    constant &= plane[i] == plane[0];
                }

                if (constant)
                {
                    continue;
                }

                var smooth = SeamFft.SmoothComponent(plane, Size);
                for (int i = 0; i < plane.Length; i++)
                {
                    result[(i * 4) + channel] = ToByte(plane[i] - smooth[i]);
                }
            }

            return result;
        }

        // ---------------------------------------------------------------- brightness only

        /// <summary>The luma (0..255) of a colour.</summary>
        public static double Luma(byte r, byte g, byte b) => (LumaR * r) + (LumaG * g) + (LumaB * b);

        /// <summary><paramref name="old"/> with its luma set to <paramref name="luma"/> (0..255) and its hue kept: the colour's
        /// offsets from its own grey are kept and, only where a channel would leave 0..255, shrunk evenly — so the ratios
        /// between the channels' offsets, which make the hue, stay. The alpha is <paramref name="alpha"/>.</summary>
        public static TexelColor WithLuminance(TexelColor old, double luma, byte alpha)
        {
            luma = Math.Max(0.0, Math.Min(255.0, luma));
            double l0 = Luma(old.R, old.G, old.B);
            double dr = old.R - l0, dg = old.G - l0, db = old.B - l0;
            double s = Math.Min(Fit(luma, dr), Math.Min(Fit(luma, dg), Fit(luma, db)));
            return new TexelColor(ToByte(luma + (dr * s)), ToByte(luma + (dg * s)), ToByte(luma + (db * s)), alpha);
        }

        /// <summary>The "brightness only" stroke: <paramref name="old"/> takes the brightness of <paramref name="paint"/> and
        /// keeps its own hue; the alpha is the paint's. An erasing paint (alpha 0) erases as usual.</summary>
        public static TexelColor KeepHue(TexelColor old, TexelColor paint)
            => paint.A == 0 ? paint : WithLuminance(old, Luma(paint.R, paint.G, paint.B), paint.A);

        private static double Fit(double luma, double offset)
        {
            if (offset > 0)
            {
                return Math.Min(1.0, (255.0 - luma) / offset);
            }

            return offset < 0 ? Math.Min(1.0, luma / -offset) : 1.0;
        }

        /// <summary>A copy of the tile in its brightness alone (each texel its luma as grey, alpha kept) — how the editor shows
        /// a tile in the "brightness only" mode: the world puts the colour on.</summary>
        public static byte[] Greyscale(byte[] rgba)
        {
            var result = (byte[])rgba.Clone();
            for (int i = 0; i + 3 < result.Length; i += 4)
            {
                byte l = ToByte(Luma(rgba[i], rgba[i + 1], rgba[i + 2]));
                result[i] = result[i + 1] = result[i + 2] = l;
            }

            return result;
        }

        // ---------------------------------------------------------------- comparing

        /// <summary>True when two frames show the same: the same alpha everywhere and the same colour wherever a texel is
        /// visible (a see-through texel's colour is not shown, and an image codec may not keep it).</summary>
        public static bool SameShown(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i + 3 < a.Length; i += 4)
            {
                if (a[i + 3] != b[i + 3] || (a[i + 3] != 0 && (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A channel value rounded half away from zero and clamped to 0..255 — the one rounding of these filters.</summary>
        public static byte ToByte(double v) => v <= 0.0 ? (byte)0 : v >= 255.0 ? (byte)255 : (byte)Math.Round(v, MidpointRounding.AwayFromZero);
    }
}
