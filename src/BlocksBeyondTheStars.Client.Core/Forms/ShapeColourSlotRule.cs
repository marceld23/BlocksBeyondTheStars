// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The per-vertex values of a shaped face the block shader reads, as the chunk mesher writes them: vertex colour
    /// (gloss, metal, shade, emission — UNorm8, so 0..1), the tint mode (TEXCOORD1.y, low four bits) and the tint
    /// (TEXCOORD2.yzw, Float16 — may exceed 1).</summary>
    public struct ShapeFaceLook
    {
        public float Gloss;
        public float Metal;
        public float Shade;
        public float Emission;
        public float Mode;
        public float TintR;
        public float TintG;
        public float TintB;
    }

    /// <summary>
    /// What a colour slot does to a face, using only the inputs the block shader already has
    /// (<c>Shaders/BlockAtlas.shader</c>): the shade channel (diffuse × lerp(0.62, 1, shade)), the emission channel (adds
    /// 3 × emission × albedo), gloss and metal, and the tint modes 2 (multiply: albedo × tint), 3 (dye: albedo → lum × tint ×
    /// 1.6 at 85 %), 4 (bark: lum × tint × 1.4 at 72 %) and 1 (flora). "Tinted" below means mode 1, 3 or 4 with a non-black
    /// tint, or mode 2.
    /// <list type="bullet">
    /// <item>Main (1) and 6..f: unchanged.</item>
    /// <item>Second (2): tinted → tint × 1.3 (same mode). Untinted → mode 2 with tint (1.25, 1.25, 1.25): the tile 25 %
    /// brighter, lit like the rest.</item>
    /// <item>Dark (4): shade × 0.3 (the face's diffuse light about 20 to 27 % lower); tinted also tint × 0.7.</item>
    /// <item>Glow (5): shade 1 and emission at least 0.45 (the tile glows in its own colour).</item>
    /// <item>Accent (3): tinted → mode 3 (mode 2 stays 2) with the complementary hue of the tint (saturation at least 0.5,
    /// value at least 0.45). Untinted: no hue can be derived from the tile on the mesher's worker thread without a new
    /// input, so the accent is a polished metal inlay instead — gloss at least 0.85, metal at least 0.8, shade × 0.8.</item>
    /// </list>
    /// Known limit: under the calm tiles the shader calms only modes 0, 1 and 4 without
    /// emission, so untinted second and accent cells and every glow cell keep the full tile detail.
    /// </summary>
    public static class ShapeColourSlotRule
    {
        public const float SecondTint = 1.3f;
        public const float SecondPlain = 1.25f;
        public const float DarkShade = 0.3f;
        public const float DarkTint = 0.7f;
        public const float GlowEmission = 0.45f;

        /// <summary>The look of a face of <paramref name="slot"/>, given the look the block gives it today.</summary>
        public static ShapeFaceLook Apply(int slot, ShapeFaceLook look)
        {
            bool tinted = IsTinted(look);
            switch (slot)
            {
                case ShapeColourSlots.Second:
                    if (tinted)
                    {
                        Scale(ref look, SecondTint);
                    }
                    else
                    {
                        look.Mode = 2f;
                        look.TintR = look.TintG = look.TintB = SecondPlain;
                    }

                    break;
                case ShapeColourSlots.Dark:
                    look.Shade *= DarkShade;
                    if (tinted)
                    {
                        Scale(ref look, DarkTint);
                    }

                    break;
                case ShapeColourSlots.Glow:
                    look.Shade = 1f;
                    look.Emission = Math.Max(look.Emission, GlowEmission);
                    break;
                case ShapeColourSlots.Accent:
                    Accent(ref look, tinted);
                    break;
            }

            return look;
        }

        /// <summary>True when the face's colour comes from its tint vector (see the class summary).</summary>
        public static bool IsTinted(ShapeFaceLook look)
        {
            int mode = (int)Math.Round(look.Mode);
            bool hasTint = look.TintR + look.TintG + look.TintB > 0.01f;
            return mode == 2 || (hasTint && (mode == 1 || mode == 3 || mode == 4));
        }

        private static void Accent(ref ShapeFaceLook look, bool tinted)
        {
            if (!tinted)
            {
                look.Gloss = Math.Max(look.Gloss, 0.85f);
                look.Metal = Math.Max(look.Metal, 0.8f);
                look.Shade *= 0.8f;
                return;
            }

            ToHsv(look.TintR, look.TintG, look.TintB, out float h, out float s, out float v);
            h = (h + 0.5f) % 1f;
            s = Math.Max(s, 0.5f);
            v = Math.Max(v, 0.45f);
            FromHsv(h, s, v, out look.TintR, out look.TintG, out look.TintB);
            if ((int)Math.Round(look.Mode) != 2)
            {
                look.Mode = 3f;
            }
        }

        private static void Scale(ref ShapeFaceLook look, float k)
        {
            look.TintR *= k;
            look.TintG *= k;
            look.TintB *= k;
        }

        /// <summary>RGB (0..1, may exceed 1 for value) to hue, saturation, value, each 0..1.</summary>
        public static void ToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            v = max;
            s = max > 1e-6f ? d / max : 0f;
            if (d <= 1e-6f)
            {
                h = 0f;
                return;
            }

            float hue = max == r ? (g - b) / d : max == g ? 2f + ((b - r) / d) : 4f + ((r - g) / d);
            h = ((hue / 6f) % 1f + 1f) % 1f;
        }

        public static void FromHsv(float h, float s, float v, out float r, out float g, out float b)
        {
            float hh = (h % 1f + 1f) % 1f * 6f;
            int sector = (int)Math.Floor(hh) % 6;
            float f = hh - (float)Math.Floor(hh);
            float p = v * (1f - s), q = v * (1f - (s * f)), t = v * (1f - (s * (1f - f)));
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }
    }
}
