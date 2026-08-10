// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using BodyPaint = BlocksBeyondTheStars.Shared.State.BodyPaint;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Client-side format + editor-layout + texture helper for the avatar body paintings (#874) — the
    /// face's siblings for torso, arms, legs and the suit helmet. The wire format is defined by the shared
    /// <see cref="BodyPaint"/> helper (concatenated 32×32 palette-index hex chunks, <see cref="FacePalette"/>
    /// palette, 0 = transparent); this class maps it onto the things only the client cares about:
    ///
    /// <list type="bullet">
    /// <item>the <b>editor canvas</b> — the part unfolded into a strip of side faces with separator lines
    /// ("walk once around the box": Front | Right | Back | Left). Arms/legs are NOT mirrored: the canvas
    /// holds two labelled rows, top = left limb, bottom = right limb. There are no top faces or soles
    /// (dropped by design), so every layout is a flat strip — no dead cross-corner cells;</item>
    /// <item>the <b>atlas texture</b> the avatar renders — the canvas plus a small solid column that
    /// unpainted cube faces (tops, soles, the helmet's open front) point their UVs at, with transparent
    /// pixels composited onto the part's tint colour (same trick as the face on the skin), so the colour
    /// pickers keep working underneath a painting.</item>
    /// </list>
    /// </summary>
    public static class BodyPaintKit
    {
        /// <summary>Chunk edge length in pixels — same resolution as the pixel face.</summary>
        public const int Face = 32;

        /// <summary>Paintable part count (mirror of the shared constant, for hosts without the alias).</summary>
        public const int PartCount = BodyPaint.PartCount;

        /// <summary>Solid-colour columns appended to the atlas right edge; cube faces without a painted
        /// chunk (tops/soles/open helmet front) UV into this region so they show the plain part tint.</summary>
        public const int SolidPad = 4;

        /// <summary>Chunks per canvas row (torso/helmet are one row; arms/legs are two limb rows).</summary>
        public static int Columns(int part) => part == BodyPaint.Helmet ? 5 : 4;

        /// <summary>Limb rows on the canvas (1, or 2 for arms/legs: top = left limb, bottom = right).</summary>
        public static int Rows(int part) => BodyPaint.ChunksOf(part) / Columns(part);

        public static int CanvasW(int part) => Columns(part) * Face;
        public static int CanvasH(int part) => Rows(part) * Face;

        /// <summary>Locale key of a part's editor title / open-button label.</summary>
        public static string PartKey(int part) => part switch
        {
            BodyPaint.Torso => "ui.paint.body.torso",
            BodyPaint.Arms => "ui.paint.body.arms",
            BodyPaint.Legs => "ui.paint.body.legs",
            _ => "ui.paint.body.helmet",
        };

        /// <summary>Locale keys of the face labels in canvas order, one per chunk column.</summary>
        public static string[] ColumnKeys(int part) => part switch
        {
            BodyPaint.Torso => new[] { "ui.paint.body.front", "ui.paint.body.right", "ui.paint.body.back", "ui.paint.body.left" },
            BodyPaint.Helmet => new[] { "ui.paint.body.right", "ui.paint.body.back", "ui.paint.body.left", "ui.paint.body.chin", "ui.paint.body.top" },
            _ => new[] { "ui.paint.body.front", "ui.paint.body.outer", "ui.paint.body.back", "ui.paint.body.inner" },
        };

        /// <summary>Locale keys of the row labels (left/right limb), or null for single-row parts.</summary>
        public static string[] RowKeys(int part)
            => Rows(part) == 2 ? new[] { "ui.paint.row.left", "ui.paint.row.right" } : null;

        /// <summary>Decodes a wire payload into a canvas grid (CanvasW×CanvasH palette indices, row-major
        /// from the top). Empty/malformed input yields an all-transparent grid — callers never crash.</summary>
        public static int[] ToCanvas(int part, string pixels)
        {
            int w = CanvasW(part), h = CanvasH(part), cols = Columns(part);
            var grid = new int[w * h];
            if (string.IsNullOrEmpty(pixels) || pixels.Length != BodyPaint.ExpectedLength(part))
            {
                return grid;
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int chunk = (y / Face) * cols + (x / Face);
                    int idx = chunk * BodyPaint.ChunkPixels + (y % Face) * Face + (x % Face);
                    grid[y * w + x] = HexValue(pixels[idx]);
                }
            }

            return grid;
        }

        /// <summary>Encodes a canvas grid back into the wire payload — or the empty string when nothing is
        /// painted, so a cleared canvas round-trips to "part not painted".</summary>
        public static string FromCanvas(int part, int[] grid)
        {
            int w = CanvasW(part), h = CanvasH(part), cols = Columns(part);
            if (grid == null || grid.Length != w * h)
            {
                return string.Empty;
            }

            bool any = false;
            var chars = new char[BodyPaint.ExpectedLength(part)];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int v = grid[y * w + x] & 0xF;
                    any |= v != 0;
                    int chunk = (y / Face) * cols + (x / Face);
                    chars[chunk * BodyPaint.ChunkPixels + (y % Face) * Face + (x % Face)] = HexChar(v);
                }
            }

            return any ? new string(chars) : string.Empty;
        }

        /// <summary>Builds the point-filtered atlas texture the avatar samples: the canvas (transparent
        /// pixels composited onto <paramref name="baseColor"/>) plus the solid-tint pad columns. Canvas
        /// row 0 is the TOP; texture row 0 is the bottom, so rows flip (same as the face texture).</summary>
        public static Texture2D BuildAtlas(int part, string pixels, Color baseColor)
        {
            int w = CanvasW(part), h = CanvasH(part);
            var grid = ToCanvas(part, pixels);
            var tex = new Texture2D(w + SolidPad, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };

            baseColor.a = 1f;
            for (int y = 0; y < h; y++)
            {
                int row = h - 1 - y; // canvas top → texture top
                for (int x = 0; x < w; x++)
                {
                    int idx = grid[row * w + x];
                    Color c = idx == 0 ? baseColor : (Color)FacePalette.ColorOf(idx);
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }

                for (int x = w; x < w + SolidPad; x++)
                {
                    tex.SetPixel(x, y, baseColor);
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>UV rect of a chunk in the atlas. Chunk order is row-major over the canvas (arms/legs:
        /// chunks 0–3 = left limb / canvas top, 4–7 = right limb); the row flip mirrors the texture bake.</summary>
        public static Rect ChunkRect(int part, int chunk)
        {
            int cols = Columns(part), rows = Rows(part);
            float aw = CanvasW(part) + SolidPad;
            int col = chunk % cols, row = chunk / cols;
            return new Rect(
                col * Face / aw,
                (rows - 1 - row) / (float)rows,
                Face / aw,
                1f / rows);
        }

        /// <summary>A small non-degenerate UV rect inside the solid pad, for cube faces without a chunk.</summary>
        public static Rect SolidRect(int part)
        {
            float aw = CanvasW(part) + SolidPad;
            return new Rect((CanvasW(part) + 1f) / aw, 0.25f, (SolidPad - 2f) / aw, 0.5f);
        }

        // The payload alphabet lives in FacePalette — body paint, faces and block designs all speak it, and
        // private copies of these two helpers are how a palette widening ends up applied in two places out
        // of three (it did: #899 found three copies).
        private static char HexChar(int v) => FacePalette.SymbolOf(v);

        private static int HexValue(char c) => FacePalette.ValueOf(c);
    }
}
