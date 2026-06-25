// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The shared format + palette for the custom pixel face a player draws in the <see cref="FaceEditor"/>.
    /// A face is a <see cref="Size"/>×<see cref="Size"/> grid of palette indices (0 = transparent, 1..15 =
    /// the colours in <see cref="Colors"/>), serialized as one hex char per pixel (row 0 = top). The string
    /// is what travels over the network (<c>SetFaceIntent</c>/<c>PlayerFace</c>) and persists in
    /// <see cref="ClientSettings.FacePixels"/> / the server player record — opaque to the server, owned here.
    /// </summary>
    public static class FacePalette
    {
        /// <summary>Face grid edge length in pixels (16×16, matching the blocky/voxel art).</summary>
        public const int Size = 16;

        /// <summary>Total pixels (and the length of a full face string).</summary>
        public const int Pixels = Size * Size;

        /// <summary>Paint colours for indices 1..15 (index 0 is transparent — no entry here). A compact
        /// pixel-art palette: skin tones, hair/feature darks, and a few accents for eyes/markings.</summary>
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
        };

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
        public static string Encode(int[] grid)
        {
            if (grid == null || grid.Length != Pixels)
            {
                return string.Empty;
            }

            var chars = new char[Pixels];
            for (int i = 0; i < Pixels; i++)
            {
                chars[i] = HexChar(grid[i]);
            }

            return new string(chars);
        }

        /// <summary>Decodes a hex string back to a palette-index grid (always length <see cref="Pixels"/>;
        /// unknown/short input yields all-transparent so callers never crash on bad data).</summary>
        public static int[] Decode(string face)
        {
            var grid = new int[Pixels];
            if (string.IsNullOrEmpty(face))
            {
                return grid;
            }

            int n = Mathf.Min(face.Length, Pixels);
            for (int i = 0; i < n; i++)
            {
                grid[i] = HexValue(face[i]);
            }

            return grid;
        }

        /// <summary>The palette colour for an index, clamped to range (index 0 = transparent).</summary>
        public static Color32 ColorOf(int index)
            => index >= 0 && index < Colors.Length ? Colors[index] : Colors[0];

        /// <summary>Builds a 16×16 point-filtered texture of the face for an avatar head, compositing
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

        private static char HexChar(int v)
        {
            v &= 0xF;
            return (char)(v < 10 ? '0' + v : 'a' + (v - 10));
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }
    }
}
