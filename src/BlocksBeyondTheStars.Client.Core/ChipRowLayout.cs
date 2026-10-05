// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Lays a row of chips (small buttons whose label is a word plus a count) out by the width of their texts (#2324):
    /// every chip gets its text plus padding, clamped, and an optional tail control (the effect button of the sample
    /// filters) takes what is left between its minimum and its preferred width. A row that would still not fit shrinks
    /// every chip by the same factor — the caller shrinks the label's font to the chip, so a word is never broken in
    /// the middle — and a row with room to spare lets the chips share it, so the row reads as one bar. Pure; the
    /// widths come from the UI's text measurer.
    /// </summary>
    public static class ChipRowLayout
    {
        /// <summary>The result: one width per chip, and the tail's width (0 without a tail).</summary>
        public readonly struct Result
        {
            public Result(float[] chips, float tail)
            {
                Chips = chips;
                Tail = tail;
            }

            public float[] Chips { get; }

            public float Tail { get; }
        }

        /// <summary>
        /// <paramref name="textWidths"/> are the measured label widths; <paramref name="padding"/> the button's inset
        /// around its label; <paramref name="minChip"/>/<paramref name="maxChip"/> clamp a chip; <paramref name="gap"/>
        /// separates chips (and the tail); <paramref name="rowWidth"/> is all the room there is; a
        /// <paramref name="tailPreferred"/> above 0 adds the tail, never narrower than <paramref name="tailMin"/>.
        /// </summary>
        public static Result Fit(IReadOnlyList<float> textWidths, float padding, float minChip, float maxChip, float gap,
            float rowWidth, float tailMin = 0f, float tailPreferred = 0f)
        {
            if (textWidths == null)
            {
                throw new ArgumentNullException(nameof(textWidths));
            }

            int n = textWidths.Count;
            var chips = new float[n];
            if (n == 0)
            {
                return new Result(chips, tailPreferred > 0f ? Math.Min(rowWidth, tailPreferred) : 0f);
            }

            float sum = 0f;
            for (int i = 0; i < n; i++)
            {
                chips[i] = Math.Max(minChip, Math.Min(maxChip, textWidths[i] + padding));
                sum += chips[i];
            }

            bool hasTail = tailPreferred > 0f;
            float gaps = gap * (n - 1) + (hasTail ? gap : 0f);
            float free = rowWidth - gaps - sum;
            float tail = 0f;
            if (hasTail)
            {
                tail = Math.Max(tailMin, Math.Min(tailPreferred, free));
                free -= tail;
            }

            if (free < 0f && sum > 0f)
            {
                float factor = Math.Max(0.1f, (sum + free) / sum);
                for (int i = 0; i < n; i++)
                {
                    chips[i] *= factor;
                }
            }
            else if (free > 0f)
            {
                float each = free / n;
                for (int i = 0; i < n; i++)
                {
                    chips[i] += each;
                }
            }

            return new Result(chips, tail);
        }

        /// <summary>The font size that keeps a label of <paramref name="measuredWidth"/> (at <paramref name="maxSize"/>) on
        /// one line inside <paramref name="available"/>: the full size when it fits, else scaled down, never below
        /// <paramref name="minSize"/>.</summary>
        public static int SingleLineFontSize(float measuredWidth, float available, int minSize, int maxSize)
        {
            if (measuredWidth <= 0f || measuredWidth <= available)
            {
                return maxSize;
            }

            int size = (int)Math.Floor(maxSize * available / measuredWidth);
            return Math.Max(minSize, Math.Min(maxSize, size));
        }
    }
}
