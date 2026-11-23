// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The engine-free half of the editors' "as in the world" preview: the small preview atlas
    /// the texture editor's block wall draws from has the world atlas's layout (32 × 32 cells, so the block shader's cell
    /// maths holds), but only the handful of cells the wall shows are filled, each with its own mip chain down to one
    /// texel (the texture calm reads a cell's mean there). Here: the box filter for those chains, the still tint mode of
    /// an animated face, the cell a UV lies in, the two variant tiles a ground block wears (restated from
    /// <c>BlockTextureAtlas.PaintVariant</c> and <c>PaintCalmVariant</c> so the variants follow the tile being painted),
    /// and the three example world colours a plant, leaf or bark tile can be looked at in.
    /// Pixels are RGBA32 (or one byte per texel), rows bottom-up, as Unity's <c>GetPixels32</c> gives them.
    /// </summary>
    public static class LookPreviewCells
    {
        /// <summary>Cells per side of the block atlas (<c>AtlasBands.Cols</c>; the block shader's 32.0).</summary>
        public const int AtlasCells = 32;

        /// <summary>The tile size the editor paints and the world atlas stores (<c>BlockTextureAtlas.Tile</c>).</summary>
        public const int TileSize = 64;

        /// <summary>How many example world colours a world-tinted tile can be looked at in.</summary>
        public const int ExampleCount = 3;

        /// <summary>log2 of a power-of-two size (64 → 6): the mip that holds one texel per cell.</summary>
        public static int Log2(int size)
        {
            int n = 0;
            while ((1 << (n + 1)) <= size)
            {
                n++;
            }

            return n;
        }

        /// <summary>A square RGBA32 picture at half its size: every texel the rounded mean of the four below it (the box
        /// filter Unity's mip generation uses, alpha included).</summary>
        public static byte[] Halve(byte[] rgba, int size) => HalveChannels(rgba, size, 4);

        /// <summary>As <see cref="Halve"/> for one byte per texel (the height field).</summary>
        public static byte[] HalveR8(byte[] r8, int size) => HalveChannels(r8, size, 1);

        /// <summary>The picture and every halving of it down to one texel: level 0 is <paramref name="rgba"/> itself.</summary>
        public static List<byte[]> MipChain(byte[] rgba, int size) => Chain(rgba, size, 4);

        /// <summary>As <see cref="MipChain"/> for one byte per texel.</summary>
        public static List<byte[]> MipChainR8(byte[] r8, int size) => Chain(r8, size, 1);

        /// <summary>The tint mode of a face without its animation: the mesher adds 16·frames + 256·speed + 1024·strip to
        /// the mode (<c>BlockTextureAtlas.AnimationCode</c>); the preview shows the frame being painted, so it keeps the
        /// low four bits only and the shader never moves the face onto a strip the preview atlas does not hold.</summary>
        public static float StillTintMode(float code)
        {
            float mode = code - (16f * (float)Math.Floor(code / 16f));
            return mode < 0f ? 0f : mode;
        }

        /// <summary>The atlas slot a texture coordinate lies in (row-major from the bottom left, as the block shader's
        /// <c>floor(uv * 32)</c>).</summary>
        public static int SlotOf(float u, float v)
        {
            int x = Clamp((int)Math.Floor(u * AtlasCells), 0, AtlasCells - 1);
            int y = Clamp((int)Math.Floor(v * AtlasCells), 0, AtlasCells - 1);
            return (y * AtlasCells) + x;
        }

        /// <summary>
        /// One of the two variant tiles of a ground block (stone, dirt, sand …), as the world atlas paints it from the
        /// block's own tile: for a calm tile the tile mirrored (variant 0 left-right, variant 1 top-bottom) and a small
        /// <paramref name="tone"/> step darker or lighter; for any other tile variant 0 a touch darker with three thin
        /// cracks, variant 1 a touch lighter with small two-tone speckles — the same random walk (seeded by the block id
        /// and the variant) as <c>BlockTextureAtlas.PaintVariant</c>. Keep the two in step.
        /// <paramref name="painted"/> = a tile a player painted (<c>BlockTextureAtlas.PaintCalmVariant</c>): the
        /// painting itself with only the tone step — not mirrored and without cracks, so a painted arrow stays readable.
        /// </summary>
        public static byte[] GroundVariant(byte[] rgba, int baseId, int variant, bool calm, float tone, bool painted = false)
        {
            const int t = TileSize;
            var px = new float[t * t * 4];
            for (int i = 0; i < px.Length && i < rgba.Length; i++)
            {
                px[i] = rgba[i] / 255f;
            }

            if (calm || painted)
            {
                return ToBytes(CalmVariant(px, variant, tone, mirror: !painted));
            }

            var rng = new Random((baseId * 31) + variant);
            if (variant == 0)
            {
                Cracks(px, rng);
            }
            else
            {
                Speckles(px, rng);
            }

            return ToBytes(px);
        }

        /// <summary>
        /// The world colour (RGB 0..1, sRGB) a tile shows in example world <paramref name="example"/> (0..2): a leafy plant
        /// or a leaf crown one of three typical leaf hues, a common trunk one of three dark barks, the pale bark a light
        /// one and the giant tree its mahogany (as on every world). The real world rolls these per species and seed
        /// (<see cref="FloraTints"/>); the examples only have to differ enough to show what the tile does under any of them.
        /// </summary>
        public static (float R, float G, float B) ExampleTint(int example, string blockKey)
        {
            int e = ((example % ExampleCount) + ExampleCount) % ExampleCount;
            if (blockKey == TreeWood.GiantLogKey)
            {
                return FloraTints.Mahogany;
            }

            if (blockKey == TreeWood.PaleLogKey)
            {
                return Hsv(PaleHues[e], 0.12f, 0.9f);
            }

            return TreeWood.IsKey(blockKey) ? Hsv(BarkHues[e], 0.5f, 0.44f) : Hsv(LeafHues[e], 0.62f, 0.95f);
        }

        /// <summary>True for the blocks the world gives a colour of its own (<c>GameBootstrap.RebuildFloraTints</c>): wild
        /// plants, the tinted leaf crowns, the saguaro (<c>ChunkMesher.SaguaroKey</c>) and the trunks — never a farmed crop
        /// or an authored plant, which keep their painted colours on every world.</summary>
        public static bool TakesWorldTint(string? blockKey)
        {
            if (string.IsNullOrEmpty(blockKey) || FloraCatalog.KeepsOwnColour(blockKey!))
            {
                return false;
            }

            return blockKey!.StartsWith("flora_", StringComparison.Ordinal) || TreeFoliage.IsTinted(blockKey)
                || blockKey == "saguaro" || TreeWood.IsKey(blockKey);
        }

        private static readonly float[] LeafHues = { 0.30f, 0.50f, 0.08f };   // leaf green, teal, autumn orange
        private static readonly float[] BarkHues = { 0.07f, 0.76f, 0.40f };   // brown, violet-grey, moss-grey
        private static readonly float[] PaleHues = { 0.12f, 0.58f, 0.95f };   // cream, silver-blue, rose

        private static float[] CalmVariant(float[] px, int variant, float tone, bool mirror)
        {
            const int t = TileSize;
            var turned = new float[px.Length];
            float k = variant == 0 ? 1f - tone : 1f + tone;
            for (int y = 0; y < t; y++)
            {
                for (int x = 0; x < t; x++)
                {
                    int from = (!mirror ? (y * t) + x : variant == 0 ? (y * t) + (t - 1 - x) : ((t - 1 - y) * t) + x) * 4;
                    int to = ((y * t) + x) * 4;
                    turned[to] = Clamp01(px[from] * k);
                    turned[to + 1] = Clamp01(px[from + 1] * k);
                    turned[to + 2] = Clamp01(px[from + 2] * k);
                    turned[to + 3] = px[from + 3];
                }
            }

            return turned;
        }

        private static void Cracks(float[] px, Random rng)
        {
            const int t = TileSize;
            ScaleAll(px, 0.96f);
            for (int c = 0; c < 3; c++)
            {
                int x = rng.Next(t), y = rng.Next(t);
                int steps = (t / 2) + rng.Next(t / 2);
                for (int s = 0; s < steps; s++)
                {
                    Scale(px, (Clamp(y, 0, t - 1) * t) + Clamp(x, 0, t - 1), 0.70f);
                    x += rng.Next(3) - 1;
                    y += rng.Next(3) - 1;
                }
            }
        }

        private static void Speckles(float[] px, Random rng)
        {
            const int t = TileSize;
            ScaleAll(px, 1.06f);
            for (int s = 0; s < 30; s++)
            {
                int x = rng.Next(t - 1), y = rng.Next(t - 1);
                float k = rng.Next(2) == 0 ? 0.72f : 1.18f;
                Scale(px, (y * t) + x, k);
                Scale(px, (y * t) + x + 1, k);
                Scale(px, ((y + 1) * t) + x, k);
            }
        }

        private static void ScaleAll(float[] px, float k)
        {
            for (int i = 0; i < px.Length / 4; i++)
            {
                Scale(px, i, k);
            }
        }

        private static void Scale(float[] px, int texel, float k)
        {
            int o = texel * 4;
            px[o] = Clamp01(px[o] * k);
            px[o + 1] = Clamp01(px[o + 1] * k);
            px[o + 2] = Clamp01(px[o + 2] * k);
        }

        private static byte[] ToBytes(float[] px)
        {
            var bytes = new byte[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                bytes[i] = (byte)Clamp((int)((px[i] * 255f) + 0.5f), 0, 255);
            }

            return bytes;
        }

        private static byte[] HalveChannels(byte[] src, int size, int channels)
        {
            int half = Math.Max(1, size / 2);
            var dst = new byte[half * half * channels];
            for (int y = 0; y < half; y++)
            {
                for (int x = 0; x < half; x++)
                {
                    int x0 = Math.Min(x * 2, size - 1), x1 = Math.Min((x * 2) + 1, size - 1);
                    int y0 = Math.Min(y * 2, size - 1), y1 = Math.Min((y * 2) + 1, size - 1);
                    for (int c = 0; c < channels; c++)
                    {
                        int sum = src[(((y0 * size) + x0) * channels) + c] + src[(((y0 * size) + x1) * channels) + c]
                            + src[(((y1 * size) + x0) * channels) + c] + src[(((y1 * size) + x1) * channels) + c];
                        dst[(((y * half) + x) * channels) + c] = (byte)((sum + 2) / 4);
                    }
                }
            }

            return dst;
        }

        private static List<byte[]> Chain(byte[] level0, int size, int channels)
        {
            var levels = new List<byte[]> { level0 };
            var current = level0;
            for (int s = size; s > 1; s /= 2)
            {
                current = HalveChannels(current, s, channels);
                levels.Add(current);
            }

            return levels;
        }

        private static (float R, float G, float B) Hsv(float h, float s, float v)
        {
            float hh = (h - (float)Math.Floor(h)) * 6f;
            int sector = (int)Math.Floor(hh);
            float f = hh - sector;
            float p = v * (1f - s), q = v * (1f - (s * f)), t = v * (1f - (s * (1f - f)));
            var rgb = sector switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q),
            };
            return (Clamp01(rgb.Item1), Clamp01(rgb.Item2), Clamp01(rgb.Item3));
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private static float Clamp01(float f) => f < 0f ? 0f : (f > 1f ? 1f : f);
    }
}
