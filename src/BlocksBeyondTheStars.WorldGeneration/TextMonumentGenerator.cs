// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A stone text tablet in its own frame (#3310, KuBa-W): <c>x</c> runs left to right as the reader sees it, <c>y</c> up,
/// <c>z</c> away from the reader — the letters stand one block proud at <c>z = 0</c>, the slab fills <c>z = 1..</c>.
/// The server turns the frame toward a landing pad; nothing here knows the world.
/// </summary>
public sealed class TextTablet
{
    private readonly ushort[] _blocks;
    private readonly bool[] _letter;

    internal TextTablet(int width, int height, int depth, IReadOnlyList<string> lines)
    {
        Width = width;
        Height = height;
        Depth = depth;
        Lines = lines;
        _blocks = new ushort[width * height * depth];
        _letter = new bool[width * height * depth];
    }

    /// <summary>Extent along the text (reader's left → right).</summary>
    public int Width { get; }

    /// <summary>Extent upward, the bottom border included.</summary>
    public int Height { get; }

    /// <summary>Extent away from the reader: the letter layer plus the slab.</summary>
    public int Depth { get; }

    /// <summary>The text, top line first.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>The block in a cell (0 = air).</summary>
    public ushort Get(int x, int y, int z) => _blocks[Index(x, y, z)];

    /// <summary>True for a cell that is part of a letter.</summary>
    public bool IsLetter(int x, int y, int z) => _letter[Index(x, y, z)];

    /// <summary>True for a cell of the front step that lights the face at night. Only a handful per tablet: every glowing
    /// cell is a light source on the client, so the letters themselves do not glow.</summary>
    public bool IsLamp(int x, int y, int z) => y == TextMonumentGenerator.Border - 1 && z == 0
        && x % TextMonumentGenerator.LampSpacing == TextMonumentGenerator.LampSpacing / 2 && x < Width;

    internal void Set(int x, int y, int z, ushort block, bool letter)
    {
        int i = Index(x, y, z);
        _blocks[i] = block;
        _letter[i] = letter;
    }

    private int Index(int x, int y, int z) => (((x * Height) + y) * Depth) + z;
}

/// <summary>
/// Spells lines of text as a giant stone tablet (#3310, KuBa-W): a dark slab with a border, and on its face the letters of
/// <see cref="MonumentFont"/>, every font pixel <see cref="PixelScale"/>×<see cref="PixelScale"/> blocks, raised one block.
/// Every line is centred; an accent run (KuBa-W: the club leads' initials) is built of a second block. Pure and
/// deterministic — the same lines always give the same tablet.
/// </summary>
public static class TextMonumentGenerator
{
    /// <summary>Blocks per font pixel along each axis of the face.</summary>
    public const int PixelScale = 2;

    /// <summary>The slab's border around the text, in blocks.</summary>
    public const int Border = 2;

    /// <summary>Space between two lines, in font pixels.</summary>
    public const int LineGapPixels = 2;

    /// <summary>The slab's thickness behind the letters, in blocks.</summary>
    public const int SlabDepth = 2;

    /// <summary>One lamp cell in the front step every this many blocks (<see cref="TextTablet.IsLamp"/>).</summary>
    public const int LampSpacing = 8;

    /// <summary>Builds the tablet. <paramref name="accentLine"/>/<paramref name="accentFrom"/> name where the accent run
    /// starts (to the end of that line); a negative line means no accent.</summary>
    public static TextTablet Build(IReadOnlyList<string> lines, ushort slab, ushort letter, ushort accent,
        int accentLine = -1, int accentFrom = 0)
    {
        int maxPixels = 0;
        foreach (string line in lines)
        {
            maxPixels = System.Math.Max(maxPixels, MonumentFont.LineWidth(line));
        }

        int width = (maxPixels * PixelScale) + (2 * Border);
        int height = (TextHeightPixels(lines.Count) * PixelScale) + (2 * Border);
        var tablet = new TextTablet(width, height, 1 + SlabDepth, lines);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 1; z <= SlabDepth; z++)
                {
                    tablet.Set(x, y, z, slab, letter: false);
                }

                if (y < Border)
                {
                    tablet.Set(x, y, 0, slab, letter: false); // a front step under the text, so the bottom line never floats
                }
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            for (int k = 0; k < lines[i].Length; k++)
            {
                bool isAccent = i == accentLine && k >= accentFrom;
                StampGlyph(tablet, lines[i][k], GlyphLeft(lines[i], k, maxPixels), GlyphTop(height, i), isAccent ? accent : letter);
            }
        }

        return tablet;
    }

    /// <summary>The tablet's left x of character <paramref name="index"/> of <paramref name="line"/> (the line centred in
    /// <paramref name="maxPixels"/>, the widest line's font-pixel width).</summary>
    public static int GlyphLeft(string line, int index, int maxPixels)
        => Border + ((((maxPixels - MonumentFont.LineWidth(line)) / 2) + (index * MonumentFont.Advance)) * PixelScale);

    /// <summary>The tablet's y of the TOP block row of line <paramref name="lineIndex"/> on a tablet <paramref name="height"/> tall.</summary>
    public static int GlyphTop(int height, int lineIndex)
        => height - Border - 1 - (lineIndex * (MonumentFont.GlyphHeight + LineGapPixels) * PixelScale);

    private static int TextHeightPixels(int lineCount)
        => lineCount <= 0 ? 0 : (lineCount * MonumentFont.GlyphHeight) + ((lineCount - 1) * LineGapPixels);

    private static void StampGlyph(TextTablet tablet, char c, int left, int top, ushort block)
    {
        for (int row = 0; row < MonumentFont.GlyphHeight; row++)
        {
            for (int col = 0; col < MonumentFont.GlyphWidth; col++)
            {
                if (!MonumentFont.IsInk(c, col, row))
                {
                    continue;
                }

                for (int dx = 0; dx < PixelScale; dx++)
                {
                    for (int dy = 0; dy < PixelScale; dy++)
                    {
                        tablet.Set(left + (col * PixelScale) + dx, top - (row * PixelScale) - dy, 0, block, letter: true);
                    }
                }
            }
        }
    }
}
