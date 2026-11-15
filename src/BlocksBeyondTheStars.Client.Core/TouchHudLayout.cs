// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The HUD zones' answers to the touch layer (#3668 follow-up), in HUD units: where the right column's map part starts
    /// (under the touch row at the top), how a tall card fits above the action buttons, how wide the centred toast lane may
    /// be, and how far the bottom-right controls line keeps from the browser's fullscreen button. Each rule leaves the plain
    /// desktop layout as it is when nothing is reserved (<see cref="TouchReservation.None"/>). The HUD constants it needs are
    /// mirrored here and pinned against the Unity side by an EditMode test (TouchHudLayoutEditModeTests).
    /// </summary>
    public static class TouchHudLayout
    {
        /// <summary>The gap between two stacked HUD elements (HudZones.Gap).</summary>
        public const float Gap = 8f;

        /// <summary>The right column: its left edge (HudZones.RightX) and its right edge (HudZones.RightEdge) in the HUD frame.</summary>
        public const float RightX = 1196f, RightEdge = 1526f;

        /// <summary>Where the right column's map part (compass, clock, radar) starts without touch (HudZones.RadarTop).</summary>
        public const float MapTop = 10f;

        /// <summary>The planet card's width (PlanetOverviewCard.CardWidth) — in space a second card column keeps left of it.</summary>
        public const float PlanetCardW = 400f;

        /// <summary>
        /// How far the right column's map part — the compass with its captions and the clock on foot, the radar with its two
        /// lines in space, and everything that stacks under them — moves down on a touch device: under the touch row along
        /// the top (≡, CHAT, VIEW, MAP) where it lies over the column, plus a gap. 0 without touch and on a screen so wide that
        /// those right-anchored buttons sit past the HUD's 1536 frame.
        /// </summary>
        public static float MapShift(TouchReservation touch)
        {
            if (!touch.Any)
            {
                return 0f;
            }

            float row = touch.TopRowBottom(RightX, RightEdge);
            return row <= 0f ? 0f : Math.Max(0f, row + Gap - MapTop);
        }

        /// <summary>
        /// Where the second card column ends on the right: left of the planet card in space (it shows in the first column
        /// there), left of the first column on foot.
        /// </summary>
        public static float SecondColumnRight(bool inSpace) => inSpace ? RightEdge - PlanetCardW - Gap : RightX - Gap;

        /// <summary>
        /// A tall card (the planet card, up to 520 units) that may not cross <paramref name="limit"/>: at
        /// <paramref name="preferredTop"/> when it fits; otherwise it keeps at least <paramref name="minViewH"/> of its height
        /// visible — rising, as far as <paramref name="minTop"/> (over the objectives above it), when the room is smaller —
        /// and the caller scrolls the rest of its content through the returned view height.
        /// </summary>
        public static (float Top, float ViewH) FitTallCard(float preferredTop, float contentH, float limit, float minTop, float minViewH)
        {
            if (preferredTop + contentH <= limit)
            {
                return (preferredTop, contentH);
            }

            float want = Math.Min(contentH, minViewH);
            float top = limit - preferredTop >= want ? preferredTop : Math.Max(minTop, limit - want);
            return (top, Math.Max(0f, Math.Min(contentH, limit - top)));
        }

        /// <summary>
        /// The width a lane centred on <paramref name="centreX"/> (the toast line, <paramref name="maxW"/> wide at most) may
        /// take on the row <paramref name="y"/> … <paramref name="y"/> + <paramref name="h"/> without running under a touch
        /// control — at 16:9 the on-foot MAP button reaches into the toast lane's right end. A control straddling the centre
        /// cannot be avoided by narrowing and is ignored (none does: VEGA's NEXT, which used to, sits beside her panel now).
        /// </summary>
        public static float CentredLaneWidth(TouchReservation touch, float centreX, float y, float h, float maxW)
        {
            float half = maxW / 2f;
            for (int i = 0; i < touch.Controls.Count; i++)
            {
                var r = touch.Controls[i];
                if (r.Bottom <= y || r.Y >= y + h)
                {
                    continue;
                }

                if (r.Right <= centreX)
                {
                    half = Math.Min(half, centreX - r.Right - Gap);
                }
                else if (r.X >= centreX)
                {
                    half = Math.Min(half, r.X - centreX - Gap);
                }
            }

            return Math.Max(0f, Math.Min(maxW, 2f * half));
        }

        /// <summary>
        /// The toast lane on a touch device: centred on <paramref name="centreX"/> at <paramref name="y"/>, narrowed to clear
        /// the touch row along the top (<see cref="CentredLaneWidth"/>) as long as it keeps <paramref name="minW"/>; else, at
        /// its full width, dropped just under that row — at a large UI scale the row reaches a few units into the lane across
        /// most of its right half. Unchanged without touch.
        /// </summary>
        public static HudRect ToastLane(TouchReservation touch, float centreX, float y, float h, float maxW, float minW)
        {
            float w = touch.Any ? CentredLaneWidth(touch, centreX, y, h, maxW) : maxW;
            if (w >= minW)
            {
                return new HudRect(centreX - (w / 2f), y, w, h);
            }

            float row = touch.TopRowBottom(centreX - (maxW / 2f), centreX + (maxW / 2f));
            return new HudRect(centreX - (maxW / 2f), Math.Max(y, row + Gap), maxW, h);
        }

        /// <summary>
        /// The opened chat window (<see cref="ChatWindowLayout"/>; a touch device centres it in the upper half) kept off the
        /// touch controls: its top under the touch row along the top where that lies over it, its bottom above the first
        /// control under it. Unchanged without touch.
        /// </summary>
        public static ChatRect ClearOfTouch(ChatRect window, TouchReservation touch)
        {
            if (!touch.Any)
            {
                return window;
            }

            float row = touch.TopRowBottom(window.X, window.Right);
            float top = row > 0f ? Math.Max(window.Y, row + Gap) : window.Y;
            float bottom = Math.Min(window.Bottom, touch.FloorBelow(window.X, window.Right, top) - Gap);
            return new ChatRect(window.X, top, window.W, Math.Max(0f, bottom - top));
        }

        /// <summary>The browser page's fullscreen button (WebGLTemplates index.html): 14 CSS px from the right edge, 34 wide, and
        /// the room kept beside it — in CSS pixels.</summary>
        public const float BrowserButtonCssInset = 14f + 34f + 10f;

        /// <summary>The least and the most the bottom-right controls line moves in for that button, in HUD units.</summary>
        public const float BrowserInsetMin = 60f, BrowserInsetMax = 120f;

        /// <summary>
        /// How far the bottom-right controls line keeps from the canvas's right edge in a browser, in HUD units: the fullscreen
        /// button's CSS inset in device pixels (<paramref name="devicePixelRatio"/>) over the canvas's pixels per HUD unit
        /// (<paramref name="scaleFactor"/>), kept within <see cref="BrowserInsetMin"/> … <see cref="BrowserInsetMax"/> so an
        /// unknown or odd pixel ratio still clears the button.
        /// </summary>
        public static float BrowserButtonInset(float scaleFactor, float devicePixelRatio)
        {
            float dpr = devicePixelRatio > 0f ? Math.Min(Math.Max(devicePixelRatio, 1f), 3f) : 1f;
            float units = scaleFactor > 0f ? BrowserButtonCssInset * dpr / scaleFactor : BrowserInsetMin;
            return Math.Min(BrowserInsetMax, Math.Max(BrowserInsetMin, units));
        }
    }
}
