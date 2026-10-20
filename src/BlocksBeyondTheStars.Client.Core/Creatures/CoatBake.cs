// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texel arithmetic of a creature's coat (#2830, #2860, #2862). The hide tiles are authored dark (~0.3 mean grey)
    /// but the body uses them as a MULTIPLY over the species colour, so each tile is brought to a bright mean first. That
    /// used to be one fixed lift toward white (<c>1 − (1 − v) · 0.45</c>), which kept the bodies bright but threw away more
    /// than half of every tile's contrast: 21 of 49 hides read as a flat colour in the game. Now every tile is NORMALISED:
    /// its luma is mapped to <see cref="TargetMean"/> with a spread of <see cref="TargetSpread"/> (clamped), and a coloured
    /// tile keeps its colour as an offset from its luma — the same texels on a plain part and in a baked coat.
    /// <para>A baked coat mixes body and pattern colour by the mask and multiplies the normalised hide over both; a glowing
    /// texel (#2862) takes the glow colour over the hide's shading and alpha 0, which the creature shader reads as "shine".
    /// </para>
    /// </summary>
    public static class CoatBake
    {
        /// <summary>The mean luma every hide tile is normalised to.</summary>
        public const float TargetMean = 0.82f;

        /// <summary>The luma spread (standard deviation) every hide tile is normalised to.</summary>
        public const float TargetSpread = 0.10f;

        /// <summary>The darkest a normalised texel may get (deep cracks stay shadows, never holes).</summary>
        public const float MinLuma = 0.45f;

        /// <summary>The most a tile's contrast is stretched (an almost flat tile stays almost flat instead of turning its
        /// noise into static).</summary>
        public const float MaxGain = 6f;

        /// <summary>The bytes of one 64 × 64 RGBA32 tile.</summary>
        public const int TileBytes = CoatMasks.Size * CoatMasks.Size * 4;

        /// <summary>The normalised hide texture (opaque): each texel's luma mapped to <see cref="TargetMean"/> ±
        /// <see cref="TargetSpread"/> per standard deviation, its colour kept as an offset from the luma.</summary>
        public static byte[] Normalize(byte[] raw)
        {
            int n = raw.Length / 4;
            double sum = 0, sq = 0;
            for (int p = 0; p + 3 < raw.Length; p += 4)
            {
                float l = Luma(raw[p], raw[p + 1], raw[p + 2]);
                sum += l;
                sq += l * l;
            }

            float mean = n == 0 ? 0f : (float)(sum / n);
            float spread = n == 0 ? 0f : (float)Math.Sqrt(Math.Max(0.0, sq / n - mean * mean));
            float gain = spread <= 1e-4f ? 0f : Math.Min(MaxGain, TargetSpread / spread);
            var outp = new byte[raw.Length];
            for (int p = 0; p + 3 < raw.Length; p += 4)
            {
                float r = raw[p] / 255f, g = raw[p + 1] / 255f, b = raw[p + 2] / 255f;
                float l = CreatureCoat.Luma(r, g, b);
                float nl = Clamp(TargetMean + (l - mean) * gain, MinLuma, 1f);
                outp[p] = ToByte(nl + (r - l));
                outp[p + 1] = ToByte(nl + (g - l));
                outp[p + 2] = ToByte(nl + (b - l));
                outp[p + 3] = 255;
            }

            return outp;
        }

        /// <summary>The luma spread (standard deviation, 0..1) of a tile — the measure <see cref="Normalize"/> evens out.
        /// </summary>
        public static float Spread(byte[] tile)
        {
            int n = tile.Length / 4;
            double sum = 0, sq = 0;
            for (int p = 0; p + 3 < tile.Length; p += 4)
            {
                float l = Luma(tile[p], tile[p + 1], tile[p + 2]);
                sum += l;
                sq += l * l;
            }

            double mean = sum / Math.Max(1, n);
            return (float)Math.Sqrt(Math.Max(0.0, sq / Math.Max(1, n) - mean * mean));
        }

        /// <summary>The mean luma (0..1) of a tile.</summary>
        public static float Mean(byte[] tile)
        {
            double sum = 0;
            for (int p = 0; p + 3 < tile.Length; p += 4)
            {
                sum += Luma(tile[p], tile[p + 1], tile[p + 2]);
            }

            return (float)(sum / Math.Max(1, tile.Length / 4));
        }

        /// <summary>Bakes a coat texture: per texel <c>lerp(body, pattern, mask) · hide</c> (the NORMALISED hide,
        /// <see cref="Normalize"/>), opaque — or, where <paramref name="glow"/> is set, the glow colour shaded by the hide's
        /// luma with alpha 0 (the shader adds it unlit). Colours are sRGB 0..1; <paramref name="hide"/> may be null (no
        /// tile: a flat coat) and must otherwise be one normalised 64 × 64 RGBA32 tile; <paramref name="mask"/> and
        /// <paramref name="glow"/> may be null (no pattern / no glow) and otherwise hold 64 × 64 values.</summary>
        public static byte[] Bake(byte[]? hide, float bodyR, float bodyG, float bodyB,
            float patternR, float patternG, float patternB, float[]? mask,
            float[]? glow = null, float glowR = 1f, float glowG = 1f, float glowB = 1f)
        {
            const int Texels = CoatMasks.Size * CoatMasks.Size;
            if ((mask != null && mask.Length != Texels) || (glow != null && glow.Length != Texels))
            {
                throw new ArgumentException("A coat mask has 64 × 64 values.", nameof(mask));
            }

            bool hasHide = hide != null && hide.Length == TileBytes;
            var outp = new byte[TileBytes];
            for (int i = 0; i < Texels; i++)
            {
                int p = i * 4;
                float hr = hasHide ? hide![p] / 255f : 1f;
                float hg = hasHide ? hide![p + 1] / 255f : 1f;
                float hb = hasHide ? hide![p + 2] / 255f : 1f;
                if (glow != null && glow[i] >= 0.5f)
                {
                    float shade = 0.8f + 0.2f * CreatureCoat.Luma(hr, hg, hb);
                    outp[p] = ToByte(glowR * shade);
                    outp[p + 1] = ToByte(glowG * shade);
                    outp[p + 2] = ToByte(glowB * shade);
                    outp[p + 3] = 0;
                    continue;
                }

                float m = mask == null ? 0f : Clamp(mask[i], 0f, 1f);
                outp[p] = ToByte((bodyR + (patternR - bodyR) * m) * hr);
                outp[p + 1] = ToByte((bodyG + (patternG - bodyG) * m) * hg);
                outp[p + 2] = ToByte((bodyB + (patternB - bodyB) * m) * hb);
                outp[p + 3] = 255;
            }

            return outp;
        }

        private static float Luma(byte r, byte g, byte b) => CreatureCoat.Luma(r / 255f, g / 255f, b / 255f);

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        private static byte ToByte(float v) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);
    }
}
