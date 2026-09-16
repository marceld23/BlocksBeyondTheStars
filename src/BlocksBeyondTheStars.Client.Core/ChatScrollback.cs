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
    /// lines hidden BELOW the shown block (0 = the newest line at the bottom). Unity-free so the headless suite covers it.
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
    }
}
