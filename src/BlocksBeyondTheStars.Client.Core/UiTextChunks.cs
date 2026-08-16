// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Splits a long (rich-)text block into pieces that a single uGUI <c>Text</c> can render.
    ///
    /// uGUI builds four vertices per character and <c>VertexHelper.FillMesh</c> throws
    /// <c>ArgumentException("Mesh can not have more than 65000 vertices")</c> once one Text holds
    /// roughly 16 250 characters. The exception fires inside the Graphic rebuild, so the Text silently
    /// gets NO mesh at all — the Codex Guide and Items chapters rendered as an empty pane once the
    /// articles / item descriptions grew past that size (#1097). Any UI that feeds data-driven text of
    /// unbounded length into a uGUI Text must go through this splitter (or render per-entry rows).
    ///
    /// Pure string processing — no UnityEngine — so it lives in Client.Core and is unit-tested headless.
    /// </summary>
    public static class UiTextChunks
    {
        /// <summary>Characters at which one uGUI Text stops rendering (65 000 vertices / 4 per glyph). Every
        /// character — including spaces and newlines — produces a quad; rich-text tags do not, so counting raw
        /// characters against this limit is conservative.</summary>
        public const int UguiGlyphLimit = 65000 / 4;

        /// <summary>Default per-chunk budget: comfortably under <see cref="UguiGlyphLimit"/> so wrapped or
        /// slightly mis-counted text still has headroom.</summary>
        public const int DefaultMaxChars = 10_000;

        /// <summary>
        /// Splits <paramref name="text"/> into chunks of at most <paramref name="maxChars"/> characters.
        /// Boundaries are chosen, in order of preference, at a paragraph break (<c>\n\n</c>), a line break
        /// (<c>\n</c>), or — for a single unbroken run longer than the budget — a hard cut that never lands
        /// inside a <c>&lt;tag&gt;</c> or between a surrogate pair. The split is lossless: the separator stays
        /// at the END of the preceding chunk, so concatenating the chunks reproduces the input exactly (a
        /// renderer stacking the chunks keeps the paragraph gap by leaving the trailing newlines in place).
        /// Empty/null input yields an empty list.
        /// </summary>
        public static IReadOnlyList<string> Split(string? text, int maxChars = DefaultMaxChars)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return chunks;
            }

            if (maxChars < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxChars), maxChars, "chunk budget must be at least one character");
            }

            string s = text!;
            int start = 0;
            while (s.Length - start > maxChars)
            {
                int cut = FindCut(s, start, maxChars);
                chunks.Add(s.Substring(start, cut - start));
                start = cut;
            }

            chunks.Add(s.Substring(start));
            return chunks;
        }

        /// <summary>Absolute index just past the end of the next chunk (exclusive), given that at least
        /// <paramref name="maxChars"/> + 1 characters remain from <paramref name="start"/>.</summary>
        private static int FindCut(string s, int start, int maxChars)
        {
            int last = start + maxChars - 1; // last index that may still belong to this chunk

            // 1. Paragraph break: the "\n\n" must fit entirely inside the window, and leave a non-empty chunk.
            int para = s.LastIndexOf("\n\n", last, last - start + 1, StringComparison.Ordinal);
            if (para > start)
            {
                return para + 2;
            }

            // 2. Line break.
            int line = s.LastIndexOf('\n', last, last - start + 1);
            if (line > start)
            {
                return line + 1;
            }

            // 3. Hard cut at the budget — but not inside "<...>" and not between a surrogate pair.
            int cut = start + maxChars;
            int lt = s.LastIndexOf('<', cut - 1, cut - start);
            if (lt > start)
            {
                int gt = s.IndexOf('>', lt);
                if (gt < 0 || gt >= cut)
                {
                    cut = lt; // the tag straddles the cut → end the chunk before it
                }
            }

            if (cut > start + 1 && char.IsHighSurrogate(s[cut - 1]))
            {
                cut--;
            }

            return cut;
        }
    }
}
