// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The meadow flower's sprite (#3056), painted in code: the left half is the blossom (five round petals around a
    /// centre), the right half the stem with two leaves. It is a DATA texture, not a picture — the flower shader colours
    /// it per flower from the world's floral palette:
    /// <list type="bullet">
    /// <item>R = shade 0..1 (petals lighten toward the tips and darken in the creases, the centre darkens at its rim),</item>
    /// <item>G = petal mask (1 = petal, takes the flower's colour; 0 on the centre),</item>
    /// <item>B = stem mask (1 = stem or leaf, takes the green),</item>
    /// <item>A = cut-out alpha (a one-pixel soft edge for the mipmaps).</item>
    /// </list>
    /// RGBA32, rows bottom-up (row 0 = v 0), so <c>Texture2D.LoadRawTextureData</c> takes it as it is.
    /// </summary>
    public static class FlowerSprite
    {
        /// <summary>Sprite width in pixels: two square halves.</summary>
        public const int Width = 128;

        /// <summary>Sprite height in pixels.</summary>
        public const int Height = 64;

        /// <summary>The number of petals.</summary>
        public const int Petals = 5;

        /// <summary>The blossom radius in pixels (centre at 32, 32 of the left half).</summary>
        public const float BlossomRadius = 30f;

        /// <summary>The flower centre's radius as a share of the blossom radius.</summary>
        public const float CentreShare = 0.24f;

        private const int Half = 64;

        /// <summary>Paints the sprite: <see cref="Width"/> × <see cref="Height"/> RGBA32 bytes.</summary>
        public static byte[] Paint()
        {
            var px = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Half; x++)
                {
                    PaintBlossom(px, x, y);
                    PaintStem(px, x, y);
                }
            }

            return px;
        }

        private static void PaintBlossom(byte[] px, int x, int y)
        {
            float dx = x + 0.5f - (Half * 0.5f), dy = y + 0.5f - (Height * 0.5f);
            float r = (float)Math.Sqrt((dx * dx) + (dy * dy)) / BlossomRadius;
            float theta = (float)Math.Atan2(dy, dx);
            // |cos(2.5 θ)| peaks five times around the circle: one round petal per peak, a notch between them.
            float lobe = Math.Abs((float)Math.Cos(theta * Petals * 0.5f));
            float edge = 0.42f + (0.58f * (float)Math.Sqrt(lobe));
            float alpha = Clamp01(((edge - r) * BlossomRadius) + 0.5f);
            if (r < CentreShare)
            {
                Put(px, x, y, 0.85f - (0.3f * (r / CentreShare)), 0f, 0f, 1f);
                return;
            }

            float shade = (0.72f + (0.28f * Clamp01(r / edge))) * (1f - (0.15f * (1f - lobe)));
            Put(px, x, y, shade, 1f, 0f, alpha);
        }

        private static void PaintStem(byte[] px, int x, int y)
        {
            float cx = x + 0.5f, cy = y + 0.5f;
            bool stem = Math.Abs(cx - (Half * 0.5f)) <= 1.5f;
            float leafA = Leaf(cx, cy, 24f, 20f, -0.6f);
            float leafB = Leaf(cx, cy, 40f, 31f, 0.6f);
            float leaf = Math.Max(leafA, leafB);
            if (stem)
            {
                Put(px, x + Half, y, 0.85f, 0f, 1f, 1f);
            }
            else if (leaf > 0f)
            {
                Put(px, x + Half, y, 0.7f + (0.3f * leaf), 0f, 1f, 1f);
            }
            else
            {
                Put(px, x + Half, y, 0f, 0f, 0f, 0f);
            }
        }

        /// <summary>A leaf as a tilted ellipse (11 × 3.2 px semi-axes): 0 outside, rising to 1 along its middle rib.</summary>
        private static float Leaf(float x, float y, float cx, float cy, float angle)
        {
            float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
            float u = ((x - cx) * c) + ((y - cy) * s);
            float v = (-(x - cx) * s) + ((y - cy) * c);
            float e = ((u * u) / (11f * 11f)) + ((v * v) / (3.2f * 3.2f));
            return e < 1f ? 1f - (Math.Abs(v) / 3.2f) : 0f;
        }

        private static void Put(byte[] px, int x, int y, float r, float g, float b, float a)
        {
            int i = ((y * Width) + x) * 4;
            px[i] = ToByte(r);
            px[i + 1] = ToByte(g);
            px[i + 2] = ToByte(b);
            px[i + 3] = ToByte(a);
        }

        private static byte ToByte(float v) => (byte)Math.Round(Clamp01(v) * 255f);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
