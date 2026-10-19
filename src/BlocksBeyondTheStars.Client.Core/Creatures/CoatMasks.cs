// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The coat pattern masks (#2830), drawn in code at the hide tiles' own 64 × 64 — no assets. A mask is 0 (body colour)
    /// or 1 (pattern colour) per texel, row 0 at the bottom (v = 0, the order <c>LoadRawTextureData</c> reads).
    /// <para>The surface patterns are tileable in both directions (every lattice wraps at 64), because the builder repeats
    /// the texture per metre (<see cref="TiledCube"/>). The texture's u runs along a part's long axis on its four long faces
    /// (<see cref="TiledCube"/>), so a band across u is a ring around the part: stripes cross a torso's spine; on a limb or
    /// neck — long in Y — the stripe patterns are drawn transposed, so they ring the leg too. The two placed bands are the
    /// tail rings (tileable) and the eye mask: a band at eye height, tileable along u only — its head mesh maps v once
    /// over the head's height.</para>
    /// </summary>
    public static class CoatMasks
    {
        /// <summary>The mask (and tile) edge in texels.</summary>
        public const int Size = 64;

        /// <summary>Which mask a pattern draws on a part — the builder's texture cache dedupes on it, since a surface
        /// pattern draws the same mask for every part of one orientation.</summary>
        public enum Variant
        {
            None,
            Full,
            AlongZ,
            AlongY,
            EyeBand,
            Rings,
        }

        /// <summary>The variant <paramref name="pattern"/> draws on <paramref name="part"/>.</summary>
        public static Variant VariantOf(CoatPattern pattern, CoatPart part)
        {
            switch (CreatureCoat.Coverage(pattern, part))
            {
                case CoatCoverage.None:
                    return Variant.None;
                case CoatCoverage.Full:
                    return Variant.Full;
            }

            if (pattern == CoatPattern.EyeMask)
            {
                return Variant.EyeBand;
            }

            if (pattern == CoatPattern.TailRings)
            {
                return Variant.Rings;
            }

            return part is CoatPart.Neck or CoatPart.Limb or CoatPart.LowerLimb ? Variant.AlongY : Variant.AlongZ;
        }

        /// <summary>Draws the mask of <paramref name="pattern"/> on <paramref name="part"/> (<see cref="Size"/>² values, 0 or 1).
        /// The same inputs always give the same mask.</summary>
        public static float[] Draw(CoatPattern pattern, CoatPart part, uint seed)
        {
            var mask = new float[Size * Size];
            var variant = VariantOf(pattern, part);
            if (variant == Variant.None)
            {
                return mask;
            }

            if (variant == Variant.Full)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = 1f;
                }

                return mask;
            }

            bool transpose = variant == Variant.AlongY;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int u = transpose ? y : x;
                    int v = transpose ? x : y;
                    mask[y * Size + x] = Texel(pattern, u, v, seed) ? 1f : 0f;
                }
            }

            return mask;
        }

        private static bool Texel(CoatPattern pattern, int x, int y, uint seed) => pattern switch
        {
            CoatPattern.CrossStripes => CrossStripes(x, y, seed),
            CoatPattern.TigerStripes => TigerStripes(x, y, seed),
            CoatPattern.Spots => Spots(x, y, seed),
            CoatPattern.Rosettes => Rosettes(x, y, seed),
            CoatPattern.Patches => Patches(x, y, seed),
            CoatPattern.Speckles => Hash(x, y, seed ^ 0x51ECu) < 0.07f,
            CoatPattern.Marbling => Marbling(x, y, seed),
            CoatPattern.ScaleBands => ScaleBands(x, y),
            CoatPattern.TailRings => Frac((x + 0.5f) / Size * 2f) < 0.42f,
            CoatPattern.EyeMask => EyeBand(x, y),
            _ => false,
        };

        /// <summary>The rows (v·64) the eye band covers on a pinned head: centred a little above mid-height, where the eyes
        /// sit on the skull box.</summary>
        public const int EyeBandLow = 23;

        /// <inheritdoc cref="EyeBandLow"/>
        public const int EyeBandHigh = 46;

        private static bool EyeBand(int x, int y)
        {
            // A gentle wave along the band (two periods per tile, so it still tiles in u).
            int wave = (int)Math.Round(Math.Sin((x + 0.5) / Size * Math.PI * 4.0));
            return y >= EyeBandLow + wave && y < EyeBandHigh + wave;
        }

        private static bool CrossStripes(int x, int y, uint seed)
        {
            int k = 3 + (int)(seed % 2u);                                       // 3 or 4 bars per tile
            float wobble = (Fbm(x, y, seed) - 0.5f) * 0.14f;
            return Frac((x + 0.5f) / Size * k + wobble) < 0.36f;
        }

        private static bool TigerStripes(int x, int y, uint seed)
        {
            int k = 3 + (int)((seed >> 3) % 3u);                                // 3..5 stripes per tile
            float phase = (x + 0.5f) / Size * k + (Fbm(x, y, seed) - 0.5f) * 0.55f;
            float d = Math.Abs(Frac(phase) - 0.5f) * 2f;                        // 0 at a stripe's middle
            float width = 0.14f + 0.26f * Noise(x, y, 4, seed ^ 0x7163u);    // tapering along the stripe
            return d < width && Noise(x, y, 8, seed ^ 0x2A6Bu) < 0.82f;        // the odd break
        }

        private static bool Spots(int x, int y, uint seed)
        {
            float best = float.MaxValue;
            float radius = 0f;
            NearestCentre(x, y, 4, seed, 0.15f, ref best, ref radius);     // 16-texel cells, one spot each
            return best < radius;
        }

        private static bool Rosettes(int x, int y, uint seed)
        {
            const int Cells = 4;
            int cx = x * Cells / Size, cy = y * Cells / Size;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = Wrap(cx + dx, Cells), gy = Wrap(cy + dy, Cells);
                    var (px, py) = CellCentre(gx, gy, Cells, seed);
                    float ox = WrapDelta(x + 0.5f - px), oy = WrapDelta(y + 0.5f - py);
                    float d = (float)Math.Sqrt(ox * ox + oy * oy);
                    float r = 5.5f + 1.2f * Hash(gx, gy, seed ^ 0x40Fu);
                    if (d < r && d > r * 0.55f)
                    {
                        // A broken ring: three gaps, turned per rosette.
                        double angle = Math.Atan2(oy, ox) / (2.0 * Math.PI) + 0.5;
                        float turn = Hash(gx, gy, seed ^ 0x99Cu);
                        if (Frac((float)(angle * 3.0) + turn) > 0.2f)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool Patches(int x, int y, uint seed)
            => Noise(x, y, 4, seed) * 0.65f + Noise(x, y, 8, seed ^ 0x3Bu) * 0.35f > 0.55f;

        private static bool Marbling(int x, int y, uint seed)
        {
            // Wavy veins: a diagonal phase (integer slopes, so it wraps at 64) bent by noise.
            float phase = (x + 0.5f) / Size * 2f + (y + 0.5f) / Size + Fbm(x, y, seed) * 1.8f;
            return Math.Abs(Frac(phase * 2f) - 0.5f) > 0.43f;
        }

        private static bool ScaleBands(int x, int y)
        {
            // Rows of scallops, 8 texels high and 16 wide, every other row offset by half a scale. A scale is a half disc
            // standing on its row's floor; the corners outside it belong to the overlapping scales of the row above. Every
            // other pair of rows is filled (the bands), and the scale edges show everywhere. 8 rows of 8 and scales of 16
            // divide 64, so it tiles.
            const int RowH = 8, ScaleW = 16;
            int row = y / RowH;
            int shift = (row & 1) == 0 ? 0 : ScaleW / 2;
            int lx = Wrap(x + shift, Size) % ScaleW;
            float dx = lx + 0.5f - ScaleW / 2f;
            float dy = y - row * RowH + 0.5f;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            const float R = ScaleW / 2f;
            int owner = d < R ? row : row + 1;
            bool edge = d >= R - 1.5f && d < R;
            bool band = (owner / 2) % 2 == 0;
            return band ? !edge : edge;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // Tileable lattice helpers: every lattice index wraps, so the mask's left edge continues its right edge.
        // ---------------------------------------------------------------------------------------------------------------

        private static void NearestCentre(int x, int y, int cells, uint seed, float skip, ref float best, ref float radius)
        {
            int cx = x * cells / Size, cy = y * cells / Size;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = Wrap(cx + dx, cells), gy = Wrap(cy + dy, cells);
                    if (Hash(gx, gy, seed ^ 0x5Bu) < skip)
                    {
                        continue;                                               // an empty cell keeps the layout irregular
                    }

                    var (px, py) = CellCentre(gx, gy, cells, seed);
                    float ox = WrapDelta(x + 0.5f - px), oy = WrapDelta(y + 0.5f - py);
                    float d = (float)Math.Sqrt(ox * ox + oy * oy);
                    if (d < best)
                    {
                        best = d;
                        radius = 3.2f + 2.4f * Hash(gx, gy, seed ^ 0x1D7u);
                    }
                }
            }
        }

        private static (float X, float Y) CellCentre(int gx, int gy, int cells, uint seed)
        {
            float cell = (float)Size / cells;
            float jx = 0.2f + 0.6f * Hash(gx, gy, seed ^ 0xA1u);
            float jy = 0.2f + 0.6f * Hash(gx, gy, seed ^ 0xB2u);
            return ((gx + jx) * cell, (gy + jy) * cell);
        }

        /// <summary>The shortest signed distance on the 64-periodic axis.</summary>
        private static float WrapDelta(float d)
        {
            if (d > Size / 2f)
            {
                return d - Size;
            }

            return d < -Size / 2f ? d + Size : d;
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private static float Frac(float v) => v - (float)Math.Floor(v);

        /// <summary>A hash of a lattice point to 0..1.</summary>
        private static float Hash(int x, int y, uint seed)
        {
            uint h = CoatMix.Mix(unchecked(seed ^ ((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u)), 13u);
            return (h >> 8) / 16777216f;
        }

        /// <summary>Value noise with <paramref name="cells"/> lattice cells across the tile (cells divides 64), smoothstep
        /// blended and wrapping — tileable.</summary>
        private static float Noise(int x, int y, int cells, uint seed)
        {
            float fx = (x + 0.5f) * cells / Size, fy = (y + 0.5f) * cells / Size;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            int ax = Wrap(x0, cells), bx = Wrap(x0 + 1, cells), ay = Wrap(y0, cells), by = Wrap(y0 + 1, cells);
            float a = Hash(ax, ay, seed), b = Hash(bx, ay, seed), c = Hash(ax, by, seed), d = Hash(bx, by, seed);
            float top = a + (b - a) * tx, bottom = c + (d - c) * tx;
            return top + (bottom - top) * ty;
        }

        private static float Fbm(int x, int y, uint seed)
            => Noise(x, y, 4, seed) * 0.55f + Noise(x, y, 8, seed ^ 0x9Eu) * 0.3f + Noise(x, y, 16, seed ^ 0x3Cu) * 0.15f;
    }
}
