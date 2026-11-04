// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3179: the brush stroke of <see cref="FaceEditor"/> — the face, body-paint and block-paint editors all share it. A
    /// stroke used to paint only the cell under the pointer each frame, so a fast drag on a slow PC (the school club's,
    /// with the live 3-D preview on top) left unpainted cells that showed the base colour as a checker; the pointer was
    /// mapped with <c>RoundToInt(u × (cells − 1))</c>, which gave the outer row and column half a hit area; and leaving the
    /// canvas mid-stroke dropped the sample, so the edge pixels were easy to miss. Now every frame paints a gap-free line
    /// from the last painted cell (<see cref="PixelStroke.Line"/>), every cell has the same hit area
    /// (<see cref="PixelStroke.CellOf"/>), and a stroke that runs off the canvas paints on along its edge. The fill and the
    /// eyedropper are untouched: they act on a click, not along a stroke.
    /// </summary>
    public sealed partial class FaceEditor
    {
        private int _strokeX, _strokeY; // the cell of the visible canvas the stroke painted last

        /// <summary>The cell of the visible canvas under the mouse. Off the canvas it is false — unless a stroke is in
        /// progress, which keeps going on the nearest edge cell instead of dropping the sample.</summary>
        private bool PointerCell(out int cellX, out int cellY)
        {
            cellX = cellY = 0;
            var mouse = Input.mousePosition;
            if (!_stroking && !RectTransformUtility.RectangleContainsScreenPoint(_canvasRt, mouse, null))
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRt, mouse, null, out var lp))
            {
                return false;
            }

            // Place() anchors the rect top-left with pivot (0,1): local x∈[0,w], y∈[-h,0]; the top row is cell 0.
            cellX = PixelStroke.CellOf(lp.x / _canvasRt.rect.width, CellsX);
            cellY = PixelStroke.CellOf(-lp.y / _canvasRt.rect.height, CellsY);
            return true;
        }

        /// <summary>Opens a stroke at a cell of the visible canvas: one undo step for the whole drag.</summary>
        private void BeginStroke(int cellX, int cellY)
        {
            BeginGridEdit();
            _stroking = true;
            _strokeX = cellX;
            _strokeY = cellY;
        }

        /// <summary>Paints the line from the stroke's last cell to this one (both included) and uploads the texture once.
        /// Grid row 0 is the TOP; the texture's row 0 is the BOTTOM, so the display flips vertically.</summary>
        private void StrokeTo(int cellX, int cellY, int index)
        {
            bool changed = false;
            foreach (var (x, y) in PixelStroke.Line(_strokeX, _strokeY, cellX, cellY))
            {
                CellToGrid(x, y, out int gx, out int gy);
                int cell = gy * _w + gx;
                if (_grid[cell] == index)
                {
                    continue;
                }

                _grid[cell] = index;
                _tex.SetPixel(gx, _h - 1 - gy, DisplayColor(index, gx, gy));
                changed = true;
            }

            _strokeX = cellX;
            _strokeY = cellY;
            if (changed)
            {
                _dirty = true;
                _tex.Apply();
            }
        }
    }
}
