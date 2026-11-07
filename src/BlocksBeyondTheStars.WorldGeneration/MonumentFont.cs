// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The 5×7 pixel font the stone text tablets are spelled in (#3310, KuBa-W). Unlike the arcade's font
/// (<c>Client.Core/Minigames/BitmapFont</c>, uppercase only and out of reach of the world generator) it has LOWERCASE, so
/// "KuBa" reads as the school writes it. Each glyph is 7 rows of 5 bits, row 0 on top, bit 4 the leftmost column; the
/// lowercase letters sit on the same baseline (row 6) with an x-height of five rows, and the few descenders are folded up
/// into the cell. An unknown character is blank but keeps its width, so a layout never shifts.
/// </summary>
public static class MonumentFont
{
    /// <summary>Glyph width in font pixels.</summary>
    public const int GlyphWidth = 5;

    /// <summary>Glyph height in font pixels.</summary>
    public const int GlyphHeight = 7;

    /// <summary>Horizontal advance per character (the glyph plus one pixel of spacing).</summary>
    public const int Advance = GlyphWidth + 1;

    private static readonly Dictionary<char, byte[]> Glyphs = Build();

    /// <summary>The glyph rows of <paramref name="c"/>, or false for a character the font does not draw (blank).</summary>
    public static bool TryGet(char c, out byte[] rows) => Glyphs.TryGetValue(c, out rows!);

    /// <summary>Whether font pixel (<paramref name="column"/>, <paramref name="row"/>) of <paramref name="c"/> is ink.</summary>
    public static bool IsInk(char c, int column, int row)
        => column >= 0 && column < GlyphWidth && row >= 0 && row < GlyphHeight
           && TryGet(c, out var rows) && (rows[row] & (1 << (GlyphWidth - 1 - column))) != 0;

    /// <summary>A line's width in font pixels: one advance per character, without the trailing spacing.</summary>
    public static int LineWidth(string line) => string.IsNullOrEmpty(line) ? 0 : (line.Length * Advance) - 1;

    private static Dictionary<char, byte[]> Build()
    {
        var g = new Dictionary<char, byte[]>
        {
            [' '] = R(0, 0, 0, 0, 0, 0, 0),
            ['0'] = R(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
            ['1'] = R(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
            ['2'] = R(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
            ['3'] = R(0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110),
            ['4'] = R(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
            ['5'] = R(0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110),
            ['6'] = R(0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
            ['7'] = R(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
            ['8'] = R(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
            ['9'] = R(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100),
            ['A'] = R(0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
            ['B'] = R(0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110),
            ['C'] = R(0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110),
            ['D'] = R(0b11100, 0b10010, 0b10001, 0b10001, 0b10001, 0b10010, 0b11100),
            ['E'] = R(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111),
            ['F'] = R(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
            ['G'] = R(0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111),
            ['H'] = R(0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
            ['I'] = R(0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
            ['J'] = R(0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100),
            ['K'] = R(0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001),
            ['L'] = R(0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111),
            ['M'] = R(0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001),
            ['N'] = R(0b10001, 0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001),
            ['O'] = R(0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
            ['P'] = R(0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000),
            ['Q'] = R(0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101),
            ['R'] = R(0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001),
            ['S'] = R(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
            ['T'] = R(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
            ['U'] = R(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
            ['V'] = R(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100),
            ['W'] = R(0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010),
            ['X'] = R(0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001),
            ['Y'] = R(0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100),
            ['Z'] = R(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111),
        };
        foreach (var kv in Lowercase())
        {
            g[kv.Key] = kv.Value;
        }

        return g;
    }

    private static Dictionary<char, byte[]> Lowercase()
    {
        var g = new Dictionary<char, byte[]>
        {
            ['a'] = R(0b00000, 0b00000, 0b01110, 0b00001, 0b01111, 0b10001, 0b01111),
            ['b'] = R(0b10000, 0b10000, 0b10110, 0b11001, 0b10001, 0b10001, 0b11110),
            ['c'] = R(0b00000, 0b00000, 0b01110, 0b10000, 0b10000, 0b10001, 0b01110),
            ['d'] = R(0b00001, 0b00001, 0b01101, 0b10011, 0b10001, 0b10001, 0b01111),
            ['e'] = R(0b00000, 0b00000, 0b01110, 0b10001, 0b11111, 0b10000, 0b01110),
            ['f'] = R(0b00110, 0b01001, 0b01000, 0b11100, 0b01000, 0b01000, 0b01000),
            ['g'] = R(0b00000, 0b01111, 0b10001, 0b10001, 0b01111, 0b00001, 0b01110),
            ['h'] = R(0b10000, 0b10000, 0b10110, 0b11001, 0b10001, 0b10001, 0b10001),
            ['i'] = R(0b00100, 0b00000, 0b01100, 0b00100, 0b00100, 0b00100, 0b01110),
            ['j'] = R(0b00010, 0b00000, 0b00110, 0b00010, 0b00010, 0b10010, 0b01100),
            ['k'] = R(0b10000, 0b10000, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010),
            ['l'] = R(0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
            ['m'] = R(0b00000, 0b00000, 0b11010, 0b10101, 0b10101, 0b10001, 0b10001),
            ['n'] = R(0b00000, 0b00000, 0b10110, 0b11001, 0b10001, 0b10001, 0b10001),
            ['o'] = R(0b00000, 0b00000, 0b01110, 0b10001, 0b10001, 0b10001, 0b01110),
            ['p'] = R(0b00000, 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000),
            ['q'] = R(0b00000, 0b01101, 0b10011, 0b10001, 0b01111, 0b00001, 0b00001),
            ['r'] = R(0b00000, 0b00000, 0b10110, 0b11001, 0b10000, 0b10000, 0b10000),
            ['s'] = R(0b00000, 0b00000, 0b01110, 0b10000, 0b01110, 0b00001, 0b11110),
            ['t'] = R(0b01000, 0b01000, 0b11100, 0b01000, 0b01000, 0b01001, 0b00110),
            ['u'] = R(0b00000, 0b00000, 0b10001, 0b10001, 0b10001, 0b10011, 0b01101),
            ['v'] = R(0b00000, 0b00000, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100),
            ['w'] = R(0b00000, 0b00000, 0b10001, 0b10001, 0b10101, 0b10101, 0b01010),
            ['x'] = R(0b00000, 0b00000, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001),
            ['y'] = R(0b00000, 0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b01110),
            ['z'] = R(0b00000, 0b00000, 0b11111, 0b00010, 0b00100, 0b01000, 0b11111),
            ['-'] = R(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
            ['.'] = R(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100),
            ['!'] = R(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100),
            ['&'] = R(0b01100, 0b10010, 0b10100, 0b01000, 0b10101, 0b10010, 0b01101),
        };
        return g;
    }

    private static byte[] R(int r0, int r1, int r2, int r3, int r4, int r5, int r6)
        => new[] { (byte)r0, (byte)r1, (byte)r2, (byte)r3, (byte)r4, (byte)r5, (byte)r6 };
}
