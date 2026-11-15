// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where VEGA's speech panel and the touch layer's NEXT button go on a touch device (#3668 follow-up), in HUD units. With
    /// a mouse, keyboard or pad the panel keeps its comms-zone spot at the bottom of the left column; on a tablet that corner
    /// belongs to the move stick, the RUN switch (on foot) or UP / DOWN (in flight), the ACT and ◄ buttons and the gear strip,
    /// and a fixed "right of the stick" offset cannot know where they are at another aspect or UI scale. The panel takes the
    /// free spot nearest its desktop one instead, measured against the live obstacles; NEXT sits beside the panel (or its
    /// folded tab) — the button that turns VEGA's page next to the page.
    /// </summary>
    public static class TouchCommsLayout
    {
        /// <summary>The distance kept from the canvas edges.</summary>
        public const float EdgeMargin = 8f;

        /// <summary>
        /// The speech panel's spot (<paramref name="w"/> × <paramref name="h"/>): the leftmost of the candidate columns — the
        /// desktop x <paramref name="prefX"/>, then just right of each obstacle — where the panel fits with its bottom as low
        /// as <paramref name="prefY"/> + <paramref name="h"/> allows and its top not above <paramref name="minTop"/>, rising
        /// over every obstacle it would lie on. The last <paramref name="softCount"/> obstacles are soft (the chat lane, which
        /// shows only while chat lines arrive): when no spot clears them too — a small canvas at a large UI scale — the search
        /// runs again without them. The desktop spot when even that fails (better a covered panel than none). Allocation-free:
        /// it runs every frame.
        /// </summary>
        public static HudRect SpeechSpot(float w, float h, float prefX, float prefY, float minTop, IReadOnlyList<HudRect> obstacles,
            int softCount, float canvasW, float canvasH)
        {
            int all = obstacles.Count, hard = Math.Max(0, all - softCount);
            if (TrySpot(w, h, prefX, prefY, minTop, obstacles, all, canvasW, canvasH, out var spot)
                || (hard < all && TrySpot(w, h, prefX, prefY, minTop, obstacles, hard, canvasW, canvasH, out spot)))
            {
                return spot;
            }

            return new HudRect(prefX, prefY, w, h);
        }

        /// <summary>One pass of <see cref="SpeechSpot"/> over the first <paramref name="count"/> obstacles.</summary>
        private static bool TrySpot(float w, float h, float prefX, float prefY, float minTop, IReadOnlyList<HudRect> obstacles, int count,
            float canvasW, float canvasH, out HudRect spot)
        {
            float x = prefX;
            while (x + w <= canvasW - EdgeMargin)
            {
                if (TryBottom(x, w, h, prefY + h, minTop, obstacles, count, canvasH, out float bottom))
                {
                    spot = new HudRect(x, bottom - h, w, h);
                    return true;
                }

                if (!NextColumn(x, prefX, obstacles, count, out x))
                {
                    break;
                }
            }

            spot = default;
            return false;
        }

        /// <summary>The next candidate column right of <paramref name="after"/>: an obstacle's right edge plus the gap.</summary>
        private static bool NextColumn(float after, float prefX, IReadOnlyList<HudRect> obstacles, int count, out float next)
        {
            next = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var o = obstacles[i];
                float x = o.Right + TouchHudLayout.Gap;
                if (x > after && x > prefX && x < next)
                {
                    next = x;
                }
            }

            return !float.IsPositiveInfinity(next);
        }

        /// <summary>The lowest bottom ≤ <paramref name="maxBottom"/> at which the panel lies on no obstacle in column
        /// <paramref name="x"/>; false when it would have to rise above <paramref name="minTop"/>.</summary>
        private static bool TryBottom(float x, float w, float h, float maxBottom, float minTop, IReadOnlyList<HudRect> obstacles,
            int count, float canvasH, out float bottom)
        {
            bottom = Math.Min(maxBottom, canvasH - EdgeMargin);
            for (int guard = 0; guard <= count; guard++)
            {
                if (bottom - h < minTop)
                {
                    return false;
                }

                var probe = new HudRect(x, bottom - h, w, h);
                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    var o = obstacles[i];
                    if (o.Overlaps(probe))
                    {
                        bottom = o.Y - TouchHudLayout.Gap; // above it — and every move goes up, so it is never met again
                        moved = true;
                        break;
                    }
                }

                if (!moved)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// NEXT's spot (a square of <paramref name="size"/>) beside <paramref name="anchor"/> — the speech panel, or the folded
        /// tab: right of it level with its bottom edge (where the continue pill is), right of it level with its top, above its
        /// right end, or left of it — the first that stays on the canvas and off every obstacle. The first, pulled onto the
        /// canvas, when none is free.
        /// </summary>
        public static HudRect NextSpot(HudRect anchor, float size, IReadOnlyList<HudRect> obstacles, float canvasW, float canvasH)
        {
            float gap = TouchHudLayout.Gap;
            var first = new HudRect(anchor.Right + gap, anchor.Bottom - size, size, size);
            for (int i = 0; i < 4; i++)
            {
                var spot = i switch
                {
                    0 => first,
                    1 => new HudRect(anchor.Right + gap, anchor.Y, size, size),
                    2 => new HudRect(anchor.Right - size, anchor.Y - gap - size, size, size),
                    _ => new HudRect(anchor.X - gap - size, anchor.Bottom - size, size, size),
                };
                if (spot.InsideCanvas(canvasW, canvasH, EdgeMargin) && !spot.Overlaps(anchor) && !HitsAny(spot, obstacles))
                {
                    return spot;
                }
            }

            float x = Math.Max(EdgeMargin, Math.Min(first.X, canvasW - EdgeMargin - size));
            float y = Math.Max(EdgeMargin, Math.Min(first.Y, canvasH - EdgeMargin - size));
            return first.At(x, y);
        }

        private static bool HitsAny(HudRect rect, IReadOnlyList<HudRect> obstacles)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                if (o.Overlaps(rect))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
