// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Scroll-back arithmetic of the chat window (#1922, "/help doesn't help any more"). The window fits its lines to a
    /// small holo box and drops the oldest ones that do not fit, so a long answer (<c>/help admin</c>, a <c>/tp</c> list)
    /// lost its first lines for good. While the chat box is open the player can now page back: the offset counts the
    /// lines hidden BELOW the shown block (0 = the newest line at the bottom). Since #3264 the opened chat is a big centred
    /// window over a 200-line session history: it lays out only the slice that fits (<see cref="SliceStart"/>), stops
    /// scrolling once the oldest line is at the top (<see cref="MaxOffset"/>) and mirrors the offset on a scrollbar.
    /// Unity-free so the headless suite covers it.
    /// </summary>
    public static class ChatScrollback
    {
        /// <summary>Lines one PageUp/PageDown press moves; a wheel notch moves one.</summary>
        public const int PageLines = 3;

        /// <summary>The offset clamped to the log: never below 0, and at least one line always stays in view.</summary>
        public static int Clamp(int offset, int lineCount) => Math.Max(0, Math.Min(offset, lineCount - 1));

        /// <summary>One frame of scrolling: a wheel turn up (positive) or PageUp shows older lines, down or PageDown newer.</summary>
        public static int Step(int offset, int lineCount, float wheel, bool pageUp, bool pageDown)
        {
            int delta = wheel > 0f ? 1 : wheel < 0f ? -1 : 0;
            if (pageUp)
            {
                delta += PageLines;
            }

            if (pageDown)
            {
                delta -= PageLines;
            }

            return Clamp(offset + delta, lineCount);
        }

        /// <summary>The exclusive end index of the shown block for <paramref name="offset"/>.</summary>
        public static int End(int offset, int lineCount) => lineCount - Clamp(offset, lineCount);

        /// <summary>A line arrived (the oldest may have been trimmed for it): a player reading older lines keeps their
        /// view on the same lines; one at the bottom stays at the bottom.</summary>
        public static int OnLineAdded(int offset, int lineCountAfter) => offset > 0 ? Clamp(offset + 1, lineCountAfter) : 0;

        /// <summary>
        /// #3264: the first line of the block that ends (exclusively) at <paramref name="end"/> and fits
        /// <paramref name="capacity"/>, given each line's laid-out height. Only that slice is ever laid out, however long
        /// the history is. At least one line shows whenever <paramref name="end"/> &gt; 0, even one taller than the window.
        /// <paramref name="heightOf"/> is asked only for the lines it walks over, so the caller can measure lazily.
        /// </summary>
        public static int SliceStart(int end, float capacity, Func<int, float> heightOf)
        {
            if (end <= 0)
            {
                return 0;
            }

            int from = end - 1;
            float used = heightOf(from);
            while (from > 0)
            {
                float h = heightOf(from - 1);
                if (used + h > capacity)
                {
                    break;
                }

                used += h;
                from--;
            }

            return from;
        }

        /// <summary>
        /// #3264: the largest offset worth scrolling to — the one whose slice starts with the oldest line and fills the
        /// window. Scrolling further would only empty the window from the bottom.
        /// </summary>
        public static int MaxOffset(int lineCount, float capacity, Func<int, float> heightOf)
        {
            if (lineCount <= 0)
            {
                return 0;
            }

            int fit = 1;
            float used = heightOf(0);
            while (fit < lineCount)
            {
                float h = heightOf(fit);
                if (used + h > capacity)
                {
                    break;
                }

                used += h;
                fit++;
            }

            return lineCount - fit;
        }

        /// <summary>#3264: the scrollbar position for an offset (a bottom-to-top bar: 0 = the newest line at the bottom,
        /// 1 = the oldest line at the top).</summary>
        public static float BarValue(int offset, int maxOffset)
            => maxOffset <= 0 ? 0f : Math.Max(0f, Math.Min(1f, offset / (float)maxOffset));

        /// <summary>#3264: the offset a dragged scrollbar asks for (the inverse of <see cref="BarValue"/>).</summary>
        public static int OffsetForBar(float value, int maxOffset)
            => maxOffset <= 0 ? 0 : (int)Math.Round(Math.Max(0f, Math.Min(1f, value)) * maxOffset, MidpointRounding.AwayFromZero);

        /// <summary>#3264: the scrollbar handle's share of the track — the shown lines out of all of them, never thinner
        /// than a grabbable sliver.</summary>
        public static float BarSize(int shownLines, int lineCount)
            => lineCount <= 0 ? 1f : Math.Max(0.08f, Math.Min(1f, shownLines / (float)lineCount));
    }
}
