// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared format + palette for the custom pixel face a player draws in the <see cref="FaceEditor"/>.
    /// A face is a <see cref="Size"/>×<see cref="Size"/> grid of palette indices (0 = transparent, 1..31 =
    /// the colours in <see cref="Colors"/>), serialized as one <see cref="Alphabet"/> char per pixel (row 0 =
    /// top; the alphabet is base32 as of #899, hex before that — old payloads decode unchanged). The string
    /// is what travels over the network (<c>SetFaceIntent</c>/<c>PlayerFace</c>) and persists in
    /// <see cref="ClientSettings.FacePixels"/> / the server player record — opaque to the server, owned here.
    /// </summary>
    public static class FacePalette
    {
        /// <summary>
        /// Face grid edge length in pixels. Raised from 16 to <b>32</b> (#840): a player who spent his whole
        /// session in the editor asked for it to "be more precise, more pixel resolution" — at 16×16 an eye is
        /// two pixels and there is no room for anything but a symbol of a face.
        /// <para>
        /// Faces drawn at 16×16 are NOT lost: <see cref="Decode(string, int)"/> upscales any smaller square
        /// payload, so old saves, old servers and older clients' faces all still render — just blockier.
        /// </para>
        /// </summary>
        public const int Size = 32;

        /// <summary>The original face size. Payloads this big are legacy and get upscaled on decode.</summary>
        public const int LegacySize = 16;

        /// <summary>Total pixels (and the length of a full face string).</summary>
        public const int Pixels = Size * Size;

        /// <summary>
        /// Paint colours for indices 1..31 (index 0 is transparent — no entry here).
        /// <para>
        /// <b>1..15 are frozen</b>: they are the original 16-colour palette, and every face, body painting and
        /// block design ever drawn stores those indices. Changing one would silently repaint existing art, so
        /// new colours only ever get appended.
        /// </para>
        /// <para>
        /// 16..31 (#899) are the shading partners the first 15 lacked: a lighter and a darker sibling for the
        /// hues people actually shade with, two extra greys and a deep skin tone. What was missing at 16
        /// colours was never another hue — it was the second tone that turns a flat shape into a lit one.
        /// </para>
        /// </summary>
        public static readonly Color32[] Colors =
        {
            new Color32(0, 0, 0, 0),          // 0 = transparent (skin/helmet shows through)
            new Color32(20, 18, 24, 255),     // 1  near-black (outlines, pupils)
            new Color32(74, 64, 78, 255),     // 2  dark slate
            new Color32(150, 150, 158, 255),  // 3  grey
            new Color32(240, 240, 236, 255),  // 4  white (eye whites)
            new Color32(120, 72, 40, 255),    // 5  brown (hair/brows)
            new Color32(214, 160, 110, 255),  // 6  tan skin
            new Color32(247, 206, 170, 255),  // 7  light skin
            new Color32(196, 60, 50, 255),    // 8  red (mouth/markings)
            new Color32(232, 150, 64, 255),   // 9  orange
            new Color32(238, 206, 76, 255),   // 10 yellow
            new Color32(96, 176, 90, 255),    // 11 green
            new Color32(70, 150, 210, 255),   // 12 blue (eyes)
            new Color32(150, 96, 200, 255),   // 13 purple
            new Color32(240, 150, 190, 255),  // 14 pink (cheeks/lips)
            new Color32(60, 200, 200, 255),   // 15 cyan
            new Color32(46, 44, 54, 255),     // 16 charcoal   (between near-black and dark slate)
            new Color32(104, 100, 112, 255),  // 17 mid grey   (the missing step 2 → 3)
            new Color32(198, 198, 204, 255),  // 18 light grey (the missing step 3 → 4)
            new Color32(70, 40, 22, 255),     // 19 dark brown (hair shadow)
            new Color32(176, 122, 74, 255),   // 20 mid brown  (wood, leather, hair light)
            new Color32(150, 96, 58, 255),    // 21 deep skin
            new Color32(120, 30, 28, 255),    // 22 dark red   (shadow under 8)
            new Color32(236, 122, 104, 255),  // 23 light red  (highlight over 8)
            new Color32(168, 92, 26, 255),    // 24 dark orange
            new Color32(250, 226, 150, 255),  // 25 light yellow
            new Color32(48, 110, 56, 255),    // 26 dark green
            new Color32(158, 214, 120, 255),  // 27 light green
            new Color32(34, 84, 140, 255),    // 28 dark blue
            new Color32(148, 202, 240, 255),  // 29 light blue
            new Color32(92, 52, 132, 255),    // 30 dark purple
            new Color32(206, 166, 236, 255),  // 31 light purple
        };

        /// <summary>
        /// Symbol alphabet of the payload format: one character per pixel, so <b>its length IS the palette
        /// size</b>. Widened from hex (16) to base32 (#899) — the first sixteen symbols are the hex digits, so
        /// every payload ever written stays valid and decodes to exactly the same colours.
        /// <para>
        /// ⚠ <b>Lower case only.</b> The server lower-cases block-design payloads before validating them
        /// (<c>GameServerPaint.HandlePaintBlock</c>), so an alphabet with upper-case symbols would silently
        /// fold two colours into one. That rules out base64-style alphabets; <c>w</c>..<c>z</c> are left free
        /// for a later top-up.
        /// </para>
        /// </summary>
        public const string Alphabet = "0123456789abcdefghijklmnopqrstuv";

        /// <summary>Backdrop shown behind transparent pixels in the editor canvas (so "nothing here" reads as
        /// empty, not as a colour). The avatar instead composites transparent pixels onto the skin.</summary>
        public static readonly Color EditorBackground = new Color(0.10f, 0.10f, 0.13f, 1f);

        /// <summary>True if the string holds no painted (non-transparent) pixel — treated as "no custom face".</summary>
        public static bool IsEmpty(string face)
        {
            if (string.IsNullOrEmpty(face))
            {
                return true;
            }

            foreach (char c in face)
            {
                if (c != '0')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Encodes a palette-index grid (length <see cref="Pixels"/>, row-major from the top) to a hex string.</summary>
        public static string Encode(int[] grid) => Encode(grid, Pixels);

        /// <summary>Encodes a palette-index grid of an arbitrary pixel count (the 32×32 block-paint designs
        /// share the face's palette + hex encoding, just bigger — see <see cref="PaintDesignAtlas"/>).</summary>
        public static string Encode(int[] grid, int pixels)
        {
            if (grid == null || grid.Length != pixels)
            {
                return string.Empty;
            }

            var chars = new char[pixels];
            for (int i = 0; i < pixels; i++)
            {
                chars[i] = SymbolOf(grid[i]);
            }

            return new string(chars);
        }

        /// <summary>Decodes a hex string back to a palette-index grid (always length <see cref="Pixels"/>;
        /// unknown/short input yields all-transparent so callers never crash on bad data).</summary>
        public static int[] Decode(string face) => Decode(face, Pixels);

        /// <summary>Decodes a hex string to a grid of an arbitrary pixel count (see the Encode overload).</summary>
        public static int[] Decode(string face, int pixels)
        {
            var grid = new int[pixels];
            if (string.IsNullOrEmpty(face))
            {
                return grid;
            }

            // A payload from a SMALLER square grid is upscaled nearest-neighbour instead of being pasted into
            // the top-left corner. This is the single point every reader goes through — the editor, the avatar
            // texture, other players' faces off the wire — so one check here is what keeps every 16×16 face
            // ever drawn (in a save, on an older server, on a friend's older client) showing up correctly
            // after the move to 32×32.
            int srcSide = SquareSide(face.Length);
            int dstSide = SquareSide(pixels);
            if (srcSide > 0 && dstSide > srcSide && dstSide % srcSide == 0)
            {
                int scale = dstSide / srcSide;
                for (int y = 0; y < dstSide; y++)
                {
                    for (int x = 0; x < dstSide; x++)
                    {
                        grid[y * dstSide + x] = ValueOf(face[(y / scale) * srcSide + (x / scale)]);
                    }
                }

                return grid;
            }

            int n = Mathf.Min(face.Length, pixels);
            for (int i = 0; i < n; i++)
            {
                grid[i] = ValueOf(face[i]);
            }

            return grid;
        }

        /// <summary>The edge length if <paramref name="count"/> is a perfect square, else 0.</summary>
        private static int SquareSide(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int side = Mathf.RoundToInt(Mathf.Sqrt(count));
            return side * side == count ? side : 0;
        }

        /// <summary>The palette colour for an index, clamped to range (index 0 = transparent).</summary>
        public static Color32 ColorOf(int index)
            => index >= 0 && index < Colors.Length ? Colors[index] : Colors[0];

        /// <summary>Builds a <see cref="Size"/>×<see cref="Size"/> point-filtered texture of the face for an avatar head, compositing
        /// transparent pixels onto <paramref name="skin"/> (so empty areas blend into the head without
        /// needing a transparent shader). Returns null if the face is empty.</summary>
        public static Texture2D BuildAvatarTexture(string face, Color skin)
        {
            if (IsEmpty(face))
            {
                return null;
            }

            var grid = Decode(face);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            for (int y = 0; y < Size; y++)
            {
                // Grid row 0 is the TOP; texture row 0 is the BOTTOM — flip vertically.
                int row = Size - 1 - y;
                for (int x = 0; x < Size; x++)
                {
                    int idx = grid[row * Size + x];
                    Color c = idx == 0 ? skin : (Color)Colors[idx];
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>The payload symbol for a palette index (out-of-range folds to transparent). Public because
        /// <see cref="BodyPaintKit"/> and <see cref="PaintDesignAtlas"/> encode/decode the same format — they
        /// each used to carry a private copy of these two helpers, which is exactly how a palette widening
        /// ends up applied in two places out of three.</summary>
        public static char SymbolOf(int index)
            => index > 0 && index < Alphabet.Length ? Alphabet[index] : Alphabet[0];

        /// <summary>The palette index for a payload symbol; anything unknown (including symbols from a NEWER
        /// client's wider palette) reads as 0 = transparent, so a strange payload degrades to unpainted rather
        /// than to garbage. Upper case is accepted on read only — payloads are always written lower case.</summary>
        public static int ValueOf(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'v') return c - 'a' + 10;
            if (c >= 'A' && c <= 'V') return c - 'A' + 10;
            return 0;
        }

    }
}
