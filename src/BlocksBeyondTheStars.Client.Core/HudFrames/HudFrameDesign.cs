// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client.HudFrames
{
    /// <summary>The four tiles of a HUD frame, in the order the design string and the editor's grid hold them.</summary>
    public enum HudFrameTile
    {
        /// <summary>The top-left corner — mirrored to the other three corners.</summary>
        Corner = 0,

        /// <summary>The top edge, tiled between the two top corners.</summary>
        Top = 1,

        /// <summary>The left edge, tiled between the two left corners — mirrored to the right edge.</summary>
        Side = 2,

        /// <summary>The bottom edge, tiled between the two bottom corners (kept light: the hotbar lives there).</summary>
        Bottom = 3,
    }

    /// <summary>
    /// A player's HUD frame (#3317): four 32 × 32 tiles of pixel-editor palette indices (the same base32 alphabet as the face
    /// and body paint, one char per pixel, row 0 = top, 0 = transparent) and the optional twinkle — the palette indices whose
    /// pixels cycle through <see cref="TwinkleColors"/> (a garland's lights). Immutable; the editor builds a new one per
    /// commit.
    /// <para>
    /// The string form (<see cref="Encode"/> / <see cref="TryParse"/>) is what the client settings keep and what a template
    /// copy carries: <c>hf1|corner|top|side|bottom|twinkle|colours</c> — a fully transparent tile is written empty, the
    /// twinkle as <c>25,27</c>, the colours as <c>#fff4d6,#7cff8a</c>. The empty string is "no frame". Anything else that
    /// does not parse is rejected whole: a hand-edited settings file never paints half a frame.
    /// </para>
    /// </summary>
    public sealed class HudFrameDesign
    {
        public const int TileSide = 32;
        public const int TilePixels = TileSide * TileSide;
        public const int TileCount = 4;

        /// <summary>The pixel editor's palette size: indices 0 (transparent) to 31.</summary>
        public const int PaletteSize = 32;

        /// <summary>At most this many twinkling indices and twinkle colours.</summary>
        public const int MaxTwinkle = 8;

        /// <summary>The editor's grid: the four tiles side by side, 128 × 32.</summary>
        public const int EditorGridW = TileSide * TileCount, EditorGridH = TileSide;

        /// <summary>The version tag the string form starts with.</summary>
        public const string Tag = "hf1";

        /// <summary>The pixel editor's alphabet (<c>FacePalette.Alphabet</c>).</summary>
        public const string Alphabet = "0123456789abcdefghijklmnopqrstuv";

        private const char Separator = '|';
        private const int PartCount = 7;

        private static readonly string TransparentTile = new string('0', TilePixels);
        private static readonly int[] NoTwinkle = Array.Empty<int>();
        private static readonly string[] NoColors = Array.Empty<string>();

        private readonly string[] _tiles;
        private readonly int[] _twinkle;
        private readonly string[] _twinkleColors;

        /// <summary>No frame at all.</summary>
        public static readonly HudFrameDesign Empty = new HudFrameDesign(
            new[] { TransparentTile, TransparentTile, TransparentTile, TransparentTile }, NoTwinkle, NoColors);

        private HudFrameDesign(string[] tiles, int[] twinkle, string[] twinkleColors)
        {
            _tiles = tiles;
            _twinkle = twinkle;
            _twinkleColors = twinkleColors;
            IsEmpty = Array.TrueForAll(tiles, t => string.Equals(t, TransparentTile, StringComparison.Ordinal));
        }

        /// <summary>True when every pixel of every tile is transparent — such a design shows no frame.</summary>
        public bool IsEmpty { get; }

        /// <summary>The palette indices (1–31) whose pixels twinkle, in phase order.</summary>
        public IReadOnlyList<int> Twinkle => _twinkle;

        /// <summary>The colours (<c>#rrggbb</c>, lower-case) a twinkling pixel cycles through; empty = the default set.</summary>
        public IReadOnlyList<string> TwinkleColors => _twinkleColors;

        /// <summary>A tile as its 1024 alphabet characters (always the full length, lower-case).</summary>
        public string Tile(HudFrameTile tile) => _tiles[(int)tile];

        /// <summary>The position of <paramref name="paletteIndex"/> in <see cref="Twinkle"/>, or -1.</summary>
        public int TwinkleSlot(int paletteIndex) => paletteIndex <= 0 ? -1 : Array.IndexOf(_twinkle, paletteIndex);

        /// <summary>A tile's palette indices, row-major from the top-left.</summary>
        public int[] TileGrid(HudFrameTile tile)
        {
            string text = _tiles[(int)tile];
            var grid = new int[TilePixels];
            for (int i = 0; i < TilePixels; i++)
            {
                grid[i] = ValueOf(text[i]);
            }

            return grid;
        }

        /// <summary>The editor's grid: the four tiles side by side (corner, top, side, bottom), 128 × 32, row-major.</summary>
        public int[] ToEditorGrid()
        {
            var grid = new int[EditorGridW * EditorGridH];
            for (int t = 0; t < TileCount; t++)
            {
                string text = _tiles[t];
                for (int y = 0; y < TileSide; y++)
                {
                    for (int x = 0; x < TileSide; x++)
                    {
                        grid[(y * EditorGridW) + (t * TileSide) + x] = ValueOf(text[(y * TileSide) + x]);
                    }
                }
            }

            return grid;
        }

        /// <summary>This design's twinkle with the tiles of an editor grid (see <see cref="ToEditorGrid"/>); a grid of the
        /// wrong size gives an empty frame, an out-of-range cell is transparent.</summary>
        public HudFrameDesign WithEditorGrid(int[]? grid)
        {
            if (grid == null || grid.Length != EditorGridW * EditorGridH)
            {
                return Empty;
            }

            var tiles = new string[TileCount];
            var chars = new char[TilePixels];
            for (int t = 0; t < TileCount; t++)
            {
                for (int y = 0; y < TileSide; y++)
                {
                    for (int x = 0; x < TileSide; x++)
                    {
                        chars[(y * TileSide) + x] = SymbolOf(grid[(y * EditorGridW) + (t * TileSide) + x]);
                    }
                }

                tiles[t] = new string(chars);
            }

            return new HudFrameDesign(tiles, _twinkle, _twinkleColors);
        }

        /// <summary>The string form; the empty string for an empty design (no frame).</summary>
        public string Encode()
        {
            if (IsEmpty)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(Tag.Length + (TileCount * (TilePixels + 1)) + 64);
            sb.Append(Tag);
            foreach (string tile in _tiles)
            {
                sb.Append(Separator);
                if (!string.Equals(tile, TransparentTile, StringComparison.Ordinal))
                {
                    sb.Append(tile);
                }
            }

            sb.Append(Separator).Append(string.Join(",", _twinkle));
            sb.Append(Separator).Append(string.Join(",", _twinkleColors));
            return sb.ToString();
        }

        /// <summary>The design a string holds, or <see cref="Empty"/> when it holds none or is not valid.</summary>
        public static HudFrameDesign Parse(string? text) => TryParse(text, out var design) ? design : Empty;

        /// <summary>Reads the string form. The empty string is a valid "no frame"; anything else must be a complete
        /// <c>hf1</c> design — wrong tag, a missing part, a tile that is not exactly 1024 alphabet characters (or empty), a
        /// twinkle index outside 1–31 or repeated, more than <see cref="MaxTwinkle"/> entries or a colour that is not
        /// <c>#rrggbb</c> rejects it whole (<paramref name="design"/> is then <see cref="Empty"/>).</summary>
        public static bool TryParse(string? text, out HudFrameDesign design)
        {
            design = Empty;
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            string[] parts = text!.Split(Separator);
            if (parts.Length != PartCount || !string.Equals(parts[0], Tag, StringComparison.Ordinal))
            {
                return false;
            }

            if (!TryReadIndices(parts[5], out int[] twinkle) || !TryReadColors(parts[6], out string[] colors))
            {
                return false;
            }

            return TryCreate(new[] { parts[1], parts[2], parts[3], parts[4] }, twinkle, colors, out design);
        }

        /// <summary>A template's design (its tiles may be empty = transparent), or false when the template is broken.</summary>
        public static bool TryFromTemplate(HudFrameDefinition? template, out HudFrameDesign design)
        {
            design = Empty;
            if (template == null)
            {
                return false;
            }

            var colors = new List<string>();
            foreach (string color in template.TwinkleColors ?? NoColors)
            {
                if (!TryNormalizeColor(color, out string normalized))
                {
                    return false;
                }

                colors.Add(normalized);
            }

            var twinkle = template.Twinkle ?? NoTwinkle;
            return ValidIndices(twinkle) && colors.Count <= MaxTwinkle
                && TryCreate(new[] { template.Corner, template.Top, template.Side, template.Bottom }, (int[])twinkle.Clone(), colors.ToArray(), out design);
        }

        /// <summary>Builds a design from four tiles (each 1024 alphabet characters, or empty = transparent).</summary>
        public static bool TryCreate(string?[] tiles, int[] twinkle, string[] twinkleColors, out HudFrameDesign design)
        {
            design = Empty;
            if (tiles == null || tiles.Length != TileCount || !ValidIndices(twinkle) || twinkleColors == null || twinkleColors.Length > MaxTwinkle)
            {
                return false;
            }

            var normalized = new string[TileCount];
            for (int t = 0; t < TileCount; t++)
            {
                if (!TryNormalizeTile(tiles[t], out normalized[t]))
                {
                    return false;
                }
            }

            design = new HudFrameDesign(normalized, twinkle, twinkleColors);
            return true;
        }

        /// <summary>The palette index of an alphabet character (upper case accepted); -1 for anything else.</summary>
        public static int ValueOf(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'v')
            {
                return c - 'a' + 10;
            }

            return c >= 'A' && c <= 'V' ? c - 'A' + 10 : -1;
        }

        /// <summary>The alphabet character of a palette index; anything out of range is transparent.</summary>
        public static char SymbolOf(int index) => index > 0 && index < PaletteSize ? Alphabet[index] : Alphabet[0];

        /// <summary>A <c>#rrggbb</c> colour (any case) as lower-case <c>#rrggbb</c>.</summary>
        public static bool TryNormalizeColor(string? color, out string normalized)
        {
            normalized = string.Empty;
            if (color == null || color.Length != 7 || color[0] != '#')
            {
                return false;
            }

            for (int i = 1; i < 7; i++)
            {
                if (!Uri.IsHexDigit(color[i]))
                {
                    return false;
                }
            }

            normalized = color.ToLowerInvariant();
            return true;
        }

        private static bool TryNormalizeTile(string? tile, out string normalized)
        {
            normalized = TransparentTile;
            if (string.IsNullOrEmpty(tile))
            {
                return true;
            }

            if (tile!.Length != TilePixels)
            {
                return false;
            }

            var chars = new char[TilePixels];
            for (int i = 0; i < TilePixels; i++)
            {
                int value = ValueOf(tile[i]);
                if (value < 0)
                {
                    return false;
                }

                chars[i] = Alphabet[value];
            }

            normalized = new string(chars);
            return true;
        }

        private static bool ValidIndices(int[]? indices)
        {
            if (indices == null || indices.Length > MaxTwinkle)
            {
                return false;
            }

            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 1 || indices[i] >= PaletteSize || Array.IndexOf(indices, indices[i]) != i)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadIndices(string text, out int[] indices)
        {
            indices = NoTwinkle;
            if (text.Length == 0)
            {
                return true;
            }

            string[] items = text.Split(',');
            var values = new int[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Length == 0 || items[i].Length > 2 || !int.TryParse(items[i], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            indices = values;
            return ValidIndices(values);
        }

        private static bool TryReadColors(string text, out string[] colors)
        {
            colors = NoColors;
            if (text.Length == 0)
            {
                return true;
            }

            string[] items = text.Split(',');
            if (items.Length > MaxTwinkle)
            {
                return false;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (!TryNormalizeColor(items[i], out items[i]))
                {
                    return false;
                }
            }

            colors = items;
            return true;
        }
    }
}
