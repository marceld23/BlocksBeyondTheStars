// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The engine-free half of the editors' gamepad canvas (#1954): who owns the stick, and how a held stick walks a
    /// cell cursor. A pad cannot steer a brush and walk a toolbar at once, so an editor canvas has two modes — the
    /// tool panel (the ordinary menu navigation) and the canvas (a cell cursor). Start swaps them, B leaves the
    /// canvas. The cursor steps once on a push, then repeats at a readable rate — not 64 cells in a frame.
    /// Lives here so the headless suite pins it; <c>PadCanvasFocus</c> in the client wires it to real input.
    /// </summary>
    public static class PadCanvasLogic
    {
        /// <summary>Seconds before a held stick starts repeating.</summary>
        public const float StepFirst = 0.30f;

        /// <summary>Seconds between repeated steps while the stick stays pushed.</summary>
        public const float StepRepeat = 0.06f;

        /// <summary>How far the stick must be pushed to count.</summary>
        public const float StickThreshold = 0.5f;

        /// <summary>The next focus mode: <paramref name="menuDown"/> (Start) toggles, <paramref name="cancelDown"/>
        /// (B) only ever leaves the canvas — on the panel B is the screen's own "back".</summary>
        public static bool NextCanvasMode(bool canvasMode, bool menuDown, bool cancelDown)
        {
            if (menuDown)
            {
                return !canvasMode;
            }

            return canvasMode && !cancelDown;
        }

        /// <summary>
        /// Advances a cell cursor from the stick. <paramref name="nextStepAt"/> is the caller's gate state: 0 means
        /// "the stick is at rest — the next push steps at once". Returns true when the cursor moved.
        /// Screen convention: row 0 is the TOP, so pushing the stick up (positive Y) DEcreases the row.
        /// </summary>
        public static bool StepCursor(
            float stickX, float stickY, float now, ref float nextStepAt, ref int cellX, ref int cellY, int cellsX, int cellsY)
        {
            bool pushX = Math.Abs(stickX) >= StickThreshold, pushY = Math.Abs(stickY) >= StickThreshold;
            if (!pushX && !pushY)
            {
                nextStepAt = 0f;
                return false;
            }

            if (nextStepAt > 0f && now < nextStepAt)
            {
                return false;
            }

            nextStepAt = now + (nextStepAt <= 0f ? StepFirst : StepRepeat);
            int beforeX = cellX, beforeY = cellY;
            if (pushX)
            {
                cellX = Clamp(cellX + (stickX > 0f ? 1 : -1), 0, cellsX - 1);
            }

            if (pushY)
            {
                cellY = Clamp(cellY + (stickY > 0f ? -1 : 1), 0, cellsY - 1);
            }

            return cellX != beforeX || cellY != beforeY;
        }

        /// <summary>Keeps a cursor inside a canvas whose size just changed (another texture, another layer grid).</summary>
        public static void ClampCursor(ref int cellX, ref int cellY, int cellsX, int cellsY)
        {
            cellX = Clamp(cellX, 0, Math.Max(0, cellsX - 1));
            cellY = Clamp(cellY, 0, Math.Max(0, cellsY - 1));
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
