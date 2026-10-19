// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texel arithmetic of a creature's coat (#2830): the hide tiles are authored dark (~0.3 mean grey) but the body
    /// uses them as a MULTIPLY over the species colour — a multiply-tint workflow needs near-white maps, or the tile alone
    /// eats ~70 % of the brightness and the body reads black. So each tile is lifted toward white
    /// (<c>v' = 1 − (1 − v) · TileDetail</c>: white stays white, only the shadows rise, the pattern survives) and modulates
    /// as gentle detail. A baked coat mixes body and pattern colour by the mask and multiplies the lifted hide LUMINANCE
    /// over both — only the hide is lifted, the pattern colour keeps its full strength.
    /// </summary>
    public static class CoatBake
    {
        /// <summary>0 = flat white, 1 = the original (too dark) tile. Tunable.</summary>
        public const float TileDetail = 0.45f;

        /// <summary>The bytes of one 64 × 64 RGBA32 tile.</summary>
        public const int TileBytes = CoatMasks.Size * CoatMasks.Size * 4;

        /// <summary>The plain hide texture: each channel lifted toward white, alpha untouched (the shader is opaque).</summary>
        public static byte[] Lift(byte[] raw)
        {
            var outp = new byte[raw.Length];
            for (int p = 0; p + 3 < raw.Length; p += 4)
            {
                outp[p] = LiftByte(raw[p]);
                outp[p + 1] = LiftByte(raw[p + 1]);
                outp[p + 2] = LiftByte(raw[p + 2]);
                outp[p + 3] = raw[p + 3];
            }

            return outp;
        }

        /// <summary>The lifted luminance (0..1) of one hide texel.</summary>
        public static float LiftedLuma(byte r, byte g, byte b)
            => 1f - (1f - CreatureCoat.Luma(r / 255f, g / 255f, b / 255f)) * TileDetail;

        /// <summary>Bakes a coat texture: per texel <c>lerp(body, pattern, mask) · liftedLuma(hide)</c>, opaque. Colours are
        /// sRGB 0..1 (the colours the materials are authored in); <paramref name="hideRaw"/> may be null (no tile: a flat
        /// coat) and must otherwise be one raw 64 × 64 RGBA32 tile.</summary>
        public static byte[] Bake(byte[]? hideRaw, float bodyR, float bodyG, float bodyB,
            float patternR, float patternG, float patternB, float[] mask)
        {
            if (mask.Length != CoatMasks.Size * CoatMasks.Size)
            {
                throw new ArgumentException("A coat mask has 64 × 64 values.", nameof(mask));
            }

            bool hasHide = hideRaw != null && hideRaw.Length == TileBytes;
            var outp = new byte[TileBytes];
            for (int i = 0; i < mask.Length; i++)
            {
                int p = i * 4;
                float l = hasHide ? LiftedLuma(hideRaw![p], hideRaw[p + 1], hideRaw[p + 2]) : 1f;
                float m = mask[i] < 0f ? 0f : mask[i] > 1f ? 1f : mask[i];
                outp[p] = ToByte((bodyR + (patternR - bodyR) * m) * l);
                outp[p + 1] = ToByte((bodyG + (patternG - bodyG) * m) * l);
                outp[p + 2] = ToByte((bodyB + (patternB - bodyB) * m) * l);
                outp[p + 3] = 255;
            }

            return outp;
        }

        private static byte LiftByte(byte v) => (byte)(255f - (255 - v) * TileDetail);

        private static byte ToByte(float v) => (byte)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);
    }
}
