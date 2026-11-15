// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Showcase
{
    /// <summary>One block of the logo: its glyph-grid column and row (row 0 on top) and the block it is built from.</summary>
    public readonly struct LogoBlock
    {
        public LogoBlock(int column, int row, string block)
        {
            Column = column;
            Row = row;
            Block = block;
        }

        public int Column { get; }

        public int Row { get; }

        /// <summary>The block key whose shipped tile the block wears.</summary>
        public string Block { get; }
    }

    /// <summary>
    /// The game's one logo (#3706): "BLOCKS BEYOND / THE STARS" in two lines of real game blocks — every pixel of a 5×7 block
    /// font is a block wearing its shipped 64×64 tile, drawn as a little cube (lit top, shaded side) in a cabinet view, with a
    /// soft dark halo so it reads over any backdrop. "BLOCKS" is built from crystal (the cyan of the old "BLOCKS"), "BEYOND
    /// THE" from silver blocks (the old white title) and "STARS" from gold. The same raster is the title of the splash, the
    /// menu and the loading screen, and the poster capture (<c>-capturePoster</c>) stamps it on the poster the launchers, the
    /// installer and the browser page show. Unity-free: it works on raw RGBA tiles and returns raw RGBA, so it is tested (and
    /// previewed) without the engine.
    /// </summary>
    public static class BlockLogo
    {
        /// <summary>The logo's lines.</summary>
        public static readonly IReadOnlyList<string> Lines = new[] { "BLOCKS BEYOND", "THE STARS" };

        /// <summary>The block every word is built from, by word index over both lines ("BLOCKS", "BEYOND", "THE", "STARS").</summary>
        public static readonly IReadOnlyList<string> WordBlocks = new[] { "crystal", "silver_block", "silver_block", "gold_block" };

        /// <summary>The block size the game renders the logo at (front face in pixels): 1207×295 px, about 1.4 MB.</summary>
        public const int GameBlockPixels = 16;

        /// <summary>Glyph size of the block font.</summary>
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        private const int LetterGap = 1;
        private const int WordGap = 3;
        private const int LineGap = 2;
        private const int TileSize = 64;

        // Face shading of the cabinet view: the top catches the light, the side is in shade, the front sits between.
        private const float TopShade = 1.18f;
        private const float SideShade = 0.58f;
        private const float EdgeShade = 0.80f;

        // The halo: a soft navy shadow around the letters (the shell's own dark blue) for contrast on bright planets.
        private const float HaloAlpha = 0.62f;
        private static readonly (byte R, byte G, byte B) HaloColor = (5, 9, 22);

        /// <summary>The blocks of the logo, laid out on the glyph grid (both lines centred on the wider one).</summary>
        public static List<LogoBlock> Layout()
        {
            var blocks = new List<LogoBlock>();
            int width = 0;
            foreach (string line in Lines)
            {
                width = Math.Max(width, LineWidth(line));
            }

            int word = 0;
            for (int l = 0; l < Lines.Count; l++)
            {
                string line = Lines[l];
                int x = (width - LineWidth(line)) / 2;
                int top = l * (GlyphHeight + LineGap);
                bool inWord = false;
                foreach (char ch in line)
                {
                    if (ch == ' ')
                    {
                        if (inWord)
                        {
                            word++;
                        }

                        inWord = false;
                        x += WordGap - LetterGap;
                        continue;
                    }

                    inWord = true;
                    string block = WordBlocks[Math.Min(word, WordBlocks.Count - 1)];
                    AddGlyph(blocks, ch, x, top, block);
                    x += GlyphWidth + LetterGap;
                }

                if (inWord)
                {
                    word++;
                }
            }

            return blocks;
        }

        /// <summary>The grid size of the laid-out logo (columns, rows).</summary>
        public static (int Columns, int Rows) GridSize()
        {
            int width = 0;
            foreach (string line in Lines)
            {
                width = Math.Max(width, LineWidth(line));
            }

            return (width, Lines.Count * GlyphHeight + (Lines.Count - 1) * LineGap);
        }

        /// <summary>The image size for a block size (front face <paramref name="blockPixels"/> wide).</summary>
        public static (int Width, int Height) ImageSize(int blockPixels)
        {
            var (cols, rows) = GridSize();
            int depth = Depth(blockPixels);
            int margin = blockPixels;
            return (cols * blockPixels + depth + margin * 2, rows * blockPixels + depth + margin * 2);
        }

        /// <summary>Renders the logo. <paramref name="tile"/> returns a block's shipped tile (64×64 RGBA, rows bottom-up as
        /// the game stores them) or null — a missing tile draws as plain grey. The result is RGBA with straight alpha, rows
        /// BOTTOM-UP (Unity's raw texture order); <see cref="RgbaImage.FlipRows"/> turns it for a top-down encoder.</summary>
        public static RgbaImage Render(int blockPixels, Func<string, byte[]?> tile)
        {
            int s = Math.Max(2, blockPixels);
            var (w, h) = ImageSize(s);
            var img = new RgbaImage(w, h);
            var blocks = Layout();
            var occupied = new HashSet<(int, int)>();
            foreach (var b in blocks)
            {
                occupied.Add((b.Column, b.Row));
            }

            var faces = new Dictionary<string, Face>(StringComparer.Ordinal);
            Face FaceOf(string key)
            {
                if (!faces.TryGetValue(key, out var f))
                {
                    f = Face.From(tile(key), s);
                    faces[key] = f;
                }

                return f;
            }

            int depth = Depth(s);
            int margin = s;

            // The faces turned away first (top and side, only where no neighbour hides them), then every front face on top —
            // fronts are nearest, and two depth faces meet only along edges in this projection.
            foreach (var b in blocks)
            {
                int x0 = margin + b.Column * s;
                int y0 = margin + depth + b.Row * s;
                var face = FaceOf(b.Block);
                if (!occupied.Contains((b.Column, b.Row - 1)))
                {
                    DrawTop(img, face, x0, y0, s, depth);
                }

                if (!occupied.Contains((b.Column + 1, b.Row)))
                {
                    DrawSide(img, face, x0, y0, s, depth);
                }
            }

            foreach (var b in blocks)
            {
                DrawFront(img, FaceOf(b.Block), margin + b.Column * s, margin + depth + b.Row * s, s);
            }

            AddHalo(img, Math.Max(2, s * 2 / 3));
            img.FlipRows(); // drawn top-down; the game's textures are bottom-up
            return img;
        }

        private static int Depth(int blockPixels) => Math.Max(2, (int)Math.Round(blockPixels * 0.42));

        private static int LineWidth(string line)
        {
            int w = 0;
            bool first = true;
            foreach (char ch in line)
            {
                if (ch == ' ')
                {
                    w += WordGap - LetterGap;
                    continue;
                }

                w += (first ? 0 : LetterGap) + GlyphWidth;
                first = false;
            }

            return w;
        }

        private static void AddGlyph(List<LogoBlock> blocks, char ch, int x, int top, string block)
        {
            string[] rows = BlockLogoFont.Glyph(ch);
            for (int r = 0; r < GlyphHeight; r++)
            {
                for (int c = 0; c < GlyphWidth; c++)
                {
                    if (rows[r][c] == '#')
                    {
                        blocks.Add(new LogoBlock(x + c, top + r, block));
                    }
                }
            }
        }

        private static void DrawFront(RgbaImage img, Face face, int x0, int y0, int s)
        {
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool edge = x == 0 || y == 0 || x == s - 1 || y == s - 1;
                    var (r, g, b) = face.At(x, y);
                    float k = edge ? EdgeShade : 1f;
                    img.Put(x0 + x, y0 + y, Scale(r, k), Scale(g, k), Scale(b, k), 255);
                }
            }
        }

        /// <summary>The top face: a parallelogram from the front's top edge back and to the right.</summary>
        private static void DrawTop(RgbaImage img, Face face, int x0, int y0, int s, int depth)
        {
            for (int row = 0; row < depth; row++)
            {
                int y = y0 - 1 - row;
                double t = (row + 0.5) / depth;
                double shift = t * depth;
                for (int px = 0; px < s + depth; px++)
                {
                    double u = (px + 0.5 - shift) / s;
                    if (u < 0 || u >= 1)
                    {
                        continue;
                    }

                    var (r, g, b) = face.At((int)(u * s), (int)((1 - t) * (s - 1)));
                    img.Put(x0 + px, y, Scale(r, TopShade), Scale(g, TopShade), Scale(b, TopShade), 255);
                }
            }
        }

        /// <summary>The right side face: a parallelogram from the front's right edge back and up.</summary>
        private static void DrawSide(RgbaImage img, Face face, int x0, int y0, int s, int depth)
        {
            for (int col = 0; col < depth; col++)
            {
                int x = x0 + s + col;
                double t = (col + 0.5) / depth;
                double shift = t * depth;
                for (int py = -depth; py < s; py++)
                {
                    double v = (py + 0.5 + shift) / s;
                    if (v < 0 || v >= 1)
                    {
                        continue;
                    }

                    var (r, g, b) = face.At((int)(t * (s - 1)), (int)(v * s));
                    img.Put(x, y0 + py, Scale(r, SideShade), Scale(g, SideShade), Scale(b, SideShade), 255);
                }
            }
        }

        /// <summary>A soft navy halo behind the letters: the alpha mask, blurred, drawn under the logo.</summary>
        private static void AddHalo(RgbaImage img, int radius)
        {
            int w = img.Width, h = img.Height;
            var mask = new float[w * h];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = img.Rgba[i * 4 + 3] / 255f;
            }

            var blurred = RgbaImage.BoxBlur(RgbaImage.BoxBlur(mask, w, h, radius), w, h, radius);
            for (int i = 0; i < mask.Length; i++)
            {
                float halo = Math.Min(1f, blurred[i] * 1.6f) * HaloAlpha;
                int p = i * 4;
                float a = img.Rgba[p + 3] / 255f;
                float outA = a + halo * (1f - a);
                if (outA <= 0f)
                {
                    continue;
                }

                // The logo's own pixels over the halo (straight alpha, "over").
                img.Rgba[p] = Mix(img.Rgba[p], HaloColor.R, a, halo, outA);
                img.Rgba[p + 1] = Mix(img.Rgba[p + 1], HaloColor.G, a, halo, outA);
                img.Rgba[p + 2] = Mix(img.Rgba[p + 2], HaloColor.B, a, halo, outA);
                img.Rgba[p + 3] = (byte)Math.Round(outA * 255f);
            }
        }

        private static byte Mix(byte top, byte under, float topA, float underA, float outA)
            => (byte)Math.Round((top * topA + under * underA * (1f - topA)) / outA);

        private static byte Scale(byte c, float k) => (byte)Math.Min(255, (int)Math.Round(c * k));

        /// <summary>A block's tile, area-averaged down to the block size (rows top-down).</summary>
        private sealed class Face
        {
            private readonly int _size;
            private readonly byte[] _rgb;

            private Face(int size, byte[] rgb)
            {
                _size = size;
                _rgb = rgb;
            }

            public (byte R, byte G, byte B) At(int x, int y)
            {
                x = Math.Min(Math.Max(x, 0), _size - 1);
                y = Math.Min(Math.Max(y, 0), _size - 1);
                int p = (y * _size + x) * 3;
                return (_rgb[p], _rgb[p + 1], _rgb[p + 2]);
            }

            public static Face From(byte[]? tile, int size)
            {
                var rgb = new byte[size * size * 3];
                bool valid = tile != null && tile.Length >= TileSize * TileSize * 4;
                for (int y = 0; y < size; y++)
                {
                    int sy0 = y * TileSize / size, sy1 = Math.Max(sy0 + 1, (y + 1) * TileSize / size);
                    for (int x = 0; x < size; x++)
                    {
                        int sx0 = x * TileSize / size, sx1 = Math.Max(sx0 + 1, (x + 1) * TileSize / size);
                        int p = (y * size + x) * 3;
                        if (!valid)
                        {
                            rgb[p] = rgb[p + 1] = rgb[p + 2] = 150;
                            continue;
                        }

                        var (r, g, b) = Average(tile!, sx0, sx1, sy0, sy1);
                        rgb[p] = r;
                        rgb[p + 1] = g;
                        rgb[p + 2] = b;
                    }
                }

                return new Face(size, rgb);
            }

            /// <summary>The average colour of a tile area; the tile's rows are bottom-up, the face's top-down.</summary>
            private static (byte, byte, byte) Average(byte[] tile, int x0, int x1, int y0, int y1)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = TileSize - 1 - y;
                    for (int x = x0; x < x1; x++)
                    {
                        int p = (row * TileSize + x) * 4;
                        r += tile[p];
                        g += tile[p + 1];
                        b += tile[p + 2];
                        n++;
                    }
                }

                return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
        }
    }
}
