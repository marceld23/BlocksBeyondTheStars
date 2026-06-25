using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Minigames
{
    /// <summary>
    /// A tiny built-in 5×7 bitmap font for the native Arcade — the web games drew labels/numbers with the canvas
    /// <c>ctx.fillText</c>, which <see cref="Canvas2D"/> has no equivalent for, so games that show on-canvas text
    /// (scores, glyphs, prompts) use this. Pure (Unity-free) so it is unit-tested headless. Uppercase only
    /// (lowercase folds to uppercase); unknown chars render blank but still advance, so layout stays stable.
    ///
    /// Each glyph is 7 rows of 5 bits (bit 4 = leftmost column). Characters are 5 wide; <see cref="Advance"/> adds
    /// one column of spacing.
    /// </summary>
    public static class BitmapFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        /// <summary>Pixels a character occupies horizontally including the 1-column gap, before scaling.</summary>
        public const int Advance = GlyphWidth + 1;

        private static readonly Dictionary<char, byte[]> Glyphs = Build();

        public static bool TryGet(char c, out byte[] rows) => Glyphs.TryGetValue(Fold(c), out rows!);

        private static char Fold(char c) => c >= 'a' && c <= 'z' ? (char)(c - 32) : c;

        private static Dictionary<char, byte[]> Build()
        {
            var g = new Dictionary<char, byte[]>
            {
                [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
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
                [':'] = R(0b00000, 0b00100, 0b00100, 0b00000, 0b00100, 0b00100, 0b00000),
                ['/'] = R(0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000),
                ['-'] = R(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
                ['+'] = R(0b00000, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0b00000),
                ['.'] = R(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100),
                [','] = R(0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b00100, 0b01000),
                ['!'] = R(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100),
                ['?'] = R(0b01110, 0b10001, 0b00001, 0b00110, 0b00100, 0b00000, 0b00100),
                ['('] = R(0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010),
                [')'] = R(0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000),
                ['='] = R(0b00000, 0b00000, 0b11111, 0b00000, 0b11111, 0b00000, 0b00000),
                ['%'] = R(0b11001, 0b11010, 0b00100, 0b01000, 0b10110, 0b01011, 0b00011),
                ['*'] = R(0b00000, 0b10101, 0b01110, 0b11111, 0b01110, 0b10101, 0b00000),
            };
            return g;
        }

        private static byte[] R(int r0, int r1, int r2, int r3, int r4, int r5, int r6)
            => new[] { (byte)r0, (byte)r1, (byte)r2, (byte)r3, (byte)r4, (byte)r5, (byte)r6 };
    }
}
