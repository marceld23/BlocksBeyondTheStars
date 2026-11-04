// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3179: the pointer-to-cell mapping and the brush line of the pixel editors (the face / body-paint / block-paint
    /// editor and the material painter). A stroke used to paint only the cell under the pointer each frame, so a fast drag
    /// on a slow PC left gaps that showed the base colour; and <c>RoundToInt(u × (cells − 1))</c> gave the outer row and
    /// column half a hit area and shifted every cell by up to half a cell. Engine-free, so the headless suite pins both.
    /// </summary>
    public static class PixelStroke
    {
        /// <summary>The cell a pointer at <paramref name="fraction"/> (0 = the canvas's near edge, 1 = its far edge) lies
        /// in, out of <paramref name="cells"/>: every cell gets the same hit area, and a pointer off the canvas (a stroke
        /// running past its edge, or a fraction that is not a number) lands on the nearest edge cell.</summary>
        public static int CellOf(float fraction, int cells)
        {
            if (cells <= 1 || !(fraction > 0f))
            {
                return 0; // also NaN: a zero-size canvas
            }

            if (fraction >= 1f)
            {
                return cells - 1;
            }

            int cell = (int)(fraction * cells);
            return cell >= cells ? cells - 1 : cell;
        }

        /// <summary>The cells of a gap-free straight line from (<paramref name="x0"/>, <paramref name="y0"/>) to
        /// (<paramref name="x1"/>, <paramref name="y1"/>), both ends included, in drawing order (Bresenham: each step moves
        /// to a neighbouring cell, diagonals included). A line of one cell is just that cell.</summary>
        public static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                yield return (x0, y0);
                if (x0 == x1 && y0 == y1)
                {
                    yield break;
                }

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
