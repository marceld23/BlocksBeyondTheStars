// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.HudFrames
{
    /// <summary>
    /// One of the eight pieces of a HUD frame on screen: which tile it shows, its rectangle (origin top-left, y down, in the
    /// frame canvas' units) and how the tile is mapped onto it — mirrored (a corner, the right edge) and repeated along an
    /// edge. <see cref="U"/>/<see cref="V"/>/<see cref="UvW"/>/<see cref="UvH"/> are a Unity <c>RawImage.uvRect</c>
    /// for a texture whose rows are stored bottom-up (Unity's convention) with wrap mode Repeat.
    /// </summary>
    public readonly struct HudFramePiece
    {
        public HudFramePiece(HudFrameTile tile, float x, float y, float w, float h, bool flipX, bool flipY, int repeatX, int repeatY)
        {
            Tile = tile;
            X = x;
            Y = y;
            W = w;
            H = h;
            FlipX = flipX;
            FlipY = flipY;
            RepeatX = repeatX;
            RepeatY = repeatY;
        }

        public HudFrameTile Tile { get; }

        public float X { get; }

        public float Y { get; }

        public float W { get; }

        public float H { get; }

        /// <summary>Mirrored left ↔ right (the right corners, the right edge).</summary>
        public bool FlipX { get; }

        /// <summary>Mirrored top ↔ bottom (the bottom corners).</summary>
        public bool FlipY { get; }

        /// <summary>How many times the tile repeats across the piece (an edge); 1 for a corner, 0 for a piece with no room.</summary>
        public int RepeatX { get; }

        /// <summary>How many times the tile repeats down the piece (a side edge); 1 for a corner, 0 for no room.</summary>
        public int RepeatY { get; }

        /// <summary>False for an edge squeezed to nothing (a tiny window) — hide it.</summary>
        public bool Visible => W > 0f && H > 0f && RepeatX > 0 && RepeatY > 0;

        public float U => FlipX ? RepeatX : 0f;

        public float V => FlipY ? RepeatY : 0f;

        public float UvW => FlipX ? -RepeatX : RepeatX;

        public float UvH => FlipY ? -RepeatY : RepeatY;
    }

    /// <summary>
    /// The 9-slice layout of a HUD frame (#3317), engine-free: four corners (the corner tile, mirrored into the other three)
    /// and four edges tiled between them (the top tile along the top, the side tile down the left and — mirrored — the
    /// right, the bottom tile along the bottom). An edge repeats its tile a WHOLE number of times, stretched by a few
    /// percent at most, so a garland that runs from the corner into the edge meets the far corner unbroken. The tile size
    /// is a whole multiple of the 32-pixel art (crisp pixels), picked from the shorter screen side so a portrait phone and a
    /// 21:9 monitor frame the view alike; <see cref="VisorInset"/> keeps the frame off the visor pipeline's corner marker.
    /// </summary>
    public static class HudFrameLayout
    {
        /// <summary>Piece order: the four corners (top-left, top-right, bottom-left, bottom-right), then the top, bottom,
        /// left and right edges.</summary>
        public const int PieceCount = 8;

        /// <summary>The frame sizes of the settings (<c>HudFrameSize</c>).</summary>
        public const int SizeSmall = 0, SizeMedium = 1, SizeLarge = 2;

        /// <summary>The visor's HUD texture masks its two LEFT corners (the orientation marker, <c>Visor.shader</c>:
        /// 8 px + a 4 px seam), and its gentle bow pushes the outermost ~0.6 % of the HUD past the screen edge.</summary>
        public const int VisorMarkerPixels = 12;

        /// <summary>The tile's size in pixels for a screen of <paramref name="screenW"/> × <paramref name="screenH"/> and a
        /// size level (0 small, 1 medium, 2 large): a whole multiple of 32 so every art pixel is the same number of screen
        /// pixels — 64 / 96 / 128 on a 1080p screen. Never so big that two corners and a bit of edge do not fit.</summary>
        public static int TilePixels(int screenW, int screenH, int sizeLevel)
        {
            int shortSide = Math.Max(1, Math.Min(screenW, screenH));
            int medium = Math.Max(1, RoundHalfUp(shortSide / 360.0));
            int scale = sizeLevel <= SizeSmall ? Math.Max(1, RoundHalfUp(shortSide / 540.0))
                : sizeLevel >= SizeLarge ? Math.Max(medium + 1, RoundHalfUp(shortSide / 270.0))
                : medium;
            int fit = Math.Max(1, shortSide / (3 * HudFrameDesign.TileSide));
            return HudFrameDesign.TileSide * Math.Min(scale, fit);
        }

        /// <summary>The inset of the whole frame from the screen edge while the visor pipeline draws the HUD: clear of the
        /// corner marker and of the bow's crop (0.7 % of the longer side, at least <see cref="VisorMarkerPixels"/>).</summary>
        public static int VisorInset(int screenW, int screenH)
            => Math.Max(VisorMarkerPixels, (int)Math.Ceiling(Math.Max(screenW, screenH) * 0.007));

        /// <summary>Lays the frame out on a <paramref name="width"/> × <paramref name="height"/> screen with tiles of
        /// <paramref name="tile"/> units, <paramref name="inset"/> units in from every edge. Fills <paramref name="pieces"/>
        /// (length <see cref="PieceCount"/>) so a relayout allocates nothing.</summary>
        public static void Compute(float width, float height, float tile, float inset, HudFramePiece[] pieces)
        {
            if (pieces == null || pieces.Length < PieceCount)
            {
                throw new ArgumentException("needs room for " + PieceCount + " pieces", nameof(pieces));
            }

            inset = Math.Max(0f, inset);
            float innerW = Math.Max(0f, width - (2f * inset));
            float innerH = Math.Max(0f, height - (2f * inset));
            float t = Math.Max(0f, Math.Min(tile, Math.Min(innerW, innerH) / 2f));
            float right = inset + innerW - t, bottom = inset + innerH - t;
            float edgeW = Math.Max(0f, innerW - (2f * t)), edgeH = Math.Max(0f, innerH - (2f * t));
            int across = Repeats(edgeW, t), down = Repeats(edgeH, t);
            int corner = t > 0f ? 1 : 0;

            pieces[0] = new HudFramePiece(HudFrameTile.Corner, inset, inset, t, t, false, false, corner, corner);
            pieces[1] = new HudFramePiece(HudFrameTile.Corner, right, inset, t, t, true, false, corner, corner);
            pieces[2] = new HudFramePiece(HudFrameTile.Corner, inset, bottom, t, t, false, true, corner, corner);
            pieces[3] = new HudFramePiece(HudFrameTile.Corner, right, bottom, t, t, true, true, corner, corner);
            pieces[4] = new HudFramePiece(HudFrameTile.Top, inset + t, inset, edgeW, t, false, false, across, corner);
            pieces[5] = new HudFramePiece(HudFrameTile.Bottom, inset + t, bottom, edgeW, t, false, false, across, corner);
            pieces[6] = new HudFramePiece(HudFrameTile.Side, inset, inset + t, t, edgeH, false, false, corner, down);
            pieces[7] = new HudFramePiece(HudFrameTile.Side, right, inset + t, t, edgeH, true, false, corner, down);
        }

        /// <summary>The eight pieces of <see cref="Compute(float, float, float, float, HudFramePiece[])"/> in a new array.</summary>
        public static HudFramePiece[] Compute(float width, float height, float tile, float inset)
        {
            var pieces = new HudFramePiece[PieceCount];
            Compute(width, height, tile, inset, pieces);
            return pieces;
        }

        /// <summary>Whole tiles along an edge of <paramref name="length"/>: the nearest count, at least one while there is
        /// any room, none when there is none.</summary>
        public static int Repeats(float length, float tile)
            => length <= 0f || tile <= 0f ? 0 : Math.Max(1, RoundHalfUp(length / tile));

        private static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);
    }
}
