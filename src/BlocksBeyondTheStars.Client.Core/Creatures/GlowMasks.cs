// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The glow pattern of a glowing species (#2862): where its body shines.</summary>
    public enum GlowPattern
    {
        None,
        SpotRows,     // a row of round light spots along the flanks (deep-sea fish)
        Freckles,     // small scattered light dots
        Veins,        // thin wandering lines
        Rings,        // thin rings round body, tail and legs
        LateralLines, // a dotted line along each flank
        Blotches,     // a few soft patches
        Tips,         // fins, crests and tentacles glow, a few freckles on the body
    }

    /// <summary>
    /// The glow patterns of glowing species (#2862): a glowing species keeps its hide and colours and shines in a pattern —
    /// spot rows, freckles, veins, rings, lateral lines, blotches, or glowing tips — in a luminous colour. The mask is baked
    /// into the coat texture's alpha (0 = glowing texel, see <see cref="CoatBake"/>); the creature shader adds the texel's
    /// colour unlit there, so the pattern shines at night and in caves. Drawn in code at the tiles' 64², tileable,
    /// deterministic per species; the same part orientation rules as <see cref="CoatMasks"/> (u along a part's long axis,
    /// transposed on parts long in Y).
    /// </summary>
    public static class GlowMasks
    {
        /// <summary>How strongly a glowing texel adds its colour on top of the lighting (the shader's <c>_Glow</c>).</summary>
        public const float Strength = 1.35f;

        /// <summary>The luminous colours a glow may take (the other third is a light shade of the body hue).</summary>
        public static readonly IReadOnlyList<int> Palette = new[]
        {
            0x5FF3FF, // cyan
            0x7CFF6B, // green
            0xFF6FD8, // magenta
            0xFFC548, // amber
            0x77A2FF, // blue
            0xE6FFF6, // pale white
            0xB98CFF, // violet
        };

        /// <summary>The glow pattern of a species: none unless it glows; tips only on a body with fins, a crest or
        /// tentacles.</summary>
        public static GlowPattern PatternFor(NetCreature c)
        {
            if (!c.Glows)
            {
                return GlowPattern.None;
            }

            var options = new List<GlowPattern>
            {
                GlowPattern.SpotRows, GlowPattern.Freckles, GlowPattern.Veins, GlowPattern.Rings,
                GlowPattern.LateralLines, GlowPattern.Blotches,
            };
            if (c.HasFins || c.HasCrest || c.Tentacles > 0)
            {
                options.Add(GlowPattern.Tips);
            }

            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            return options[(int)(CoatMix.Mix(seed, CoatMix.SaltGlow) % (uint)options.Count)];
        }

        /// <summary>The glow colour of a species (packed 0xRRGGBB): one of <see cref="Palette"/>, or for about a third a light,
        /// saturated shade of its body hue.</summary>
        public static int ColourFor(uint speciesSeed, int bodyRgb)
        {
            uint h = CoatMix.Mix(speciesSeed, CoatMix.SaltGlowColour);
            if (h % 3u == 0u)
            {
                CreatureCoat.Unpack(bodyRgb, out float r, out float g, out float b);
                CreatureCoat.RgbToHsv(r, g, b, out float hue, out float sat, out _);
                CreatureCoat.HsvToRgb(hue, Math.Max(0.45f, Math.Min(0.7f, sat)), 1f, out float gr, out float gg, out float gb);
                return CreatureCoat.Pack(gr, gg, gb);
            }

            return Palette[(int)((h >> 4) % (uint)Palette.Count)];
        }

        /// <summary>How a glow pattern covers one part: masked on the body parts it marks, the whole part for the glowing tips
        /// (the accents), none elsewhere (the belly, the jaw).</summary>
        public static CoatCoverage Coverage(GlowPattern pattern, CoatPart part)
        {
            switch (pattern)
            {
                case GlowPattern.None:
                    return CoatCoverage.None;
                case GlowPattern.Tips:
                    return part == CoatPart.Ornament ? CoatCoverage.Full
                        : part is CoatPart.Torso or CoatPart.Tail ? CoatCoverage.Masked
                        : CoatCoverage.None;
                case GlowPattern.SpotRows:
                case GlowPattern.LateralLines:
                    return part is CoatPart.Torso or CoatPart.Neck or CoatPart.Tail or CoatPart.Wing
                        ? CoatCoverage.Masked : CoatCoverage.None;
                case GlowPattern.Rings:
                    return part is CoatPart.Torso or CoatPart.Neck or CoatPart.Tail or CoatPart.Limb or CoatPart.LowerLimb
                        ? CoatCoverage.Masked : CoatCoverage.None;
                default:
                    return part is CoatPart.Torso or CoatPart.Neck or CoatPart.Tail or CoatPart.Head or CoatPart.Limb
                        or CoatPart.Wing
                        ? CoatCoverage.Masked : CoatCoverage.None;
            }
        }

        /// <summary>Draws the glow mask of <paramref name="pattern"/> on <paramref name="part"/> (<see cref="CoatMasks.Size"/>²
        /// values, 0 or 1; all 1 where the part glows fully). The same inputs always give the same mask.</summary>
        public static float[] Draw(GlowPattern pattern, CoatPart part, uint seed)
        {
            const int Size = CoatMasks.Size;
            var mask = new float[Size * Size];
            var coverage = Coverage(pattern, part);
            if (coverage == CoatCoverage.None)
            {
                return mask;
            }

            if (coverage == CoatCoverage.Full)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = 1f;
                }

                return mask;
            }

            bool transpose = part is CoatPart.Neck or CoatPart.Limb or CoatPart.LowerLimb;
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

        private static bool Texel(GlowPattern pattern, int x, int y, uint seed)
        {
            const int Size = CoatMasks.Size;
            switch (pattern)
            {
                case GlowPattern.SpotRows:
                    {
                        // Four spots per tile along u on one row a little above the middle; the row shifts per species.
                        int row = 30 + (int)(seed % 10u);
                        float cx = (CoatMasks.Wrap(x, 16) + 0.5f) - 8f;
                        float cy = y + 0.5f - row;
                        float r = 3.2f + (seed >> 5) % 2u;
                        return cx * cx + cy * cy < r * r;
                    }

                case GlowPattern.Freckles:
                case GlowPattern.Tips:
                    {
                        // One dot in a fifth of the 8-texel cells (fewer on the tips' body), at a jittered spot.
                        int gx = x / 8, gy = y / 8;
                        float keep = pattern == GlowPattern.Tips ? 0.12f : 0.22f;
                        if (CoatMasks.Hash(gx, gy, seed ^ 0xF4Eu) >= keep)
                        {
                            return false;
                        }

                        float px = gx * 8 + 2f + 4f * CoatMasks.Hash(gx, gy, seed ^ 0x11u);
                        float py = gy * 8 + 2f + 4f * CoatMasks.Hash(gx, gy, seed ^ 0x22u);
                        float dx = x + 0.5f - px, dy = y + 0.5f - py;
                        return dx * dx + dy * dy < 4.5f; // about four texels across: still a dot at play distance
                    }

                case GlowPattern.Veins:
                    {
                        // Two smooth diagonal lines per tile, bent by coarse noise (integer slopes, so it wraps at 64).
                        float phase = (x + 0.5f) / Size + (y + 0.5f) / Size + CoatMasks.Noise(x, y, 4, seed) * 1.3f;
                        return Math.Abs(CoatMasks.Frac(phase * 2f) - 0.5f) > 0.455f;
                    }

                case GlowPattern.Rings:
                    {
                        float wobble = (CoatMasks.Noise(x, y, 4, seed) - 0.5f) * 0.08f;
                        return CoatMasks.Frac((x + 0.5f) / Size * 2f + wobble) < 0.09f;
                    }

                case GlowPattern.LateralLines:
                    {
                        // A dashed line at mid-height: dashes three texels high and long, one texel gap along u.
                        int row = 31 + (int)(seed % 4u);
                        return y >= row - 1 && y <= row + 1 && CoatMasks.Wrap(x, 4) < 3;
                    }

                case GlowPattern.Blotches:
                    return CoatMasks.Noise(x, y, 4, seed ^ 0xB10u) * 0.7f + CoatMasks.Noise(x, y, 8, seed ^ 0x7Au) * 0.3f > 0.7f;

                default:
                    return false;
            }
        }
    }
}
