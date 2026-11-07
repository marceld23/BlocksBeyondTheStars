// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.HudFrames
{
    /// <summary>
    /// Paints a HUD frame into a plain RGBA picture (#3319: the preview that travels with a submitted frame), with the same
    /// <see cref="HudFrameLayout"/> the game draws it with — nearest-neighbour, so the pixel art stays crisp. Engine-free;
    /// the Unity side only turns the bytes into a PNG.
    /// </summary>
    public static class HudFrameComposer
    {
        /// <summary>
        /// The frame on a <paramref name="width"/> × <paramref name="height"/> picture of <paramref name="backgroundRgb"/>:
        /// RGBA bytes, rows top-down. <paramref name="paletteRgb"/> holds the 32 palette colours as 0xRRGGBB (index 0 is
        /// transparent whatever it holds); twinkling pixels show their colour of <paramref name="twinkleStep"/>.
        /// </summary>
        public static byte[] Compose(HudFrameDesign design, int width, int height, int tile, int inset, int[] paletteRgb,
            int backgroundRgb, float opacity = 1f, int twinkleStep = 0)
        {
            if (design == null || paletteRgb == null || paletteRgb.Length < HudFrameDesign.PaletteSize || width <= 0 || height <= 0)
            {
                throw new ArgumentException("needs a design, a 32-colour palette and a picture size", nameof(design));
            }

            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                Put(rgba, i, backgroundRgb);
            }

            int[] colors = ResolveColors(design, paletteRgb, twinkleStep);
            var grids = new int[HudFrameDesign.TileCount][];
            for (int t = 0; t < grids.Length; t++)
            {
                grids[t] = design.TileGrid((HudFrameTile)t);
            }

            float alpha = Math.Max(0f, Math.Min(1f, opacity));
            foreach (var piece in HudFrameLayout.Compute(width, height, tile, inset))
            {
                if (piece.Visible)
                {
                    Paint(rgba, width, height, piece, grids[(int)piece.Tile], colors, alpha);
                }
            }

            return rgba;
        }

        /// <summary>The colour of every palette index at this twinkle step; -1 = transparent.</summary>
        private static int[] ResolveColors(HudFrameDesign design, int[] paletteRgb, int step)
        {
            var twinkle = HudFrameTwinkle.ColorsOf(design);
            var colors = new int[HudFrameDesign.PaletteSize];
            colors[0] = -1;
            for (int i = 1; i < colors.Length; i++)
            {
                int slot = HudFrameTwinkle.ColorSlot(design, i, step);
                colors[i] = slot >= 0 && HudFrameTwinkle.TryParseRgb(twinkle[slot], out int rgb) ? rgb : paletteRgb[i] & 0xFFFFFF;
            }

            return colors;
        }

        private static void Paint(byte[] rgba, int width, int height, HudFramePiece piece, int[] grid, int[] colors, float alpha)
        {
            int x0 = Math.Max(0, (int)Math.Round(piece.X)), x1 = Math.Min(width, (int)Math.Round(piece.X + piece.W));
            int y0 = Math.Max(0, (int)Math.Round(piece.Y)), y1 = Math.Min(height, (int)Math.Round(piece.Y + piece.H));
            const int side = HudFrameDesign.TileSide;
            for (int y = y0; y < y1; y++)
            {
                int row = Texel((y + 0.5f - piece.Y) / piece.H * piece.RepeatY, piece.FlipY);
                for (int x = x0; x < x1; x++)
                {
                    int col = Texel((x + 0.5f - piece.X) / piece.W * piece.RepeatX, piece.FlipX);
                    int color = colors[grid[(row * side) + col]];
                    if (color >= 0)
                    {
                        Blend(rgba, (y * width) + x, color, alpha);
                    }
                }
            }
        }

        /// <summary>The tile row/column at a position along the piece measured in tiles (fractional part = inside a tile).</summary>
        private static int Texel(float tiles, bool flip)
        {
            float inside = tiles - (float)Math.Floor(tiles);
            int texel = Math.Min(HudFrameDesign.TileSide - 1, Math.Max(0, (int)(inside * HudFrameDesign.TileSide)));
            return flip ? HudFrameDesign.TileSide - 1 - texel : texel;
        }

        private static void Put(byte[] rgba, int pixel, int rgb)
        {
            rgba[pixel * 4] = (byte)(rgb >> 16);
            rgba[(pixel * 4) + 1] = (byte)(rgb >> 8);
            rgba[(pixel * 4) + 2] = (byte)rgb;
            rgba[(pixel * 4) + 3] = 255;
        }

        private static void Blend(byte[] rgba, int pixel, int rgb, float alpha)
        {
            int at = pixel * 4;
            rgba[at] = Mix(rgba[at], (byte)(rgb >> 16), alpha);
            rgba[at + 1] = Mix(rgba[at + 1], (byte)(rgb >> 8), alpha);
            rgba[at + 2] = Mix(rgba[at + 2], (byte)rgb, alpha);
        }

        private static byte Mix(byte under, byte over, float alpha) => (byte)Math.Round(under + ((over - under) * alpha));
    }
}
