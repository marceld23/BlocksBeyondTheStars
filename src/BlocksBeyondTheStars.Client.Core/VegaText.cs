// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-free logic behind VEGA's speech panel and the re-readable tips log (#736, #737): splitting an
    /// over-long line into pages that fit the fixed panel, and reconstructing the tips a player has already
    /// received from their server-persisted <c>vega:*</c> milestones. Both live here (not in the
    /// MonoBehaviours) so the paging and mapping rules are covered by plain .NET tests.
    /// </summary>
    public static class VegaText
    {
        /// <summary>The onboarding chain in lesson order — must match the server's <c>VegaStages</c>.
        /// The journal shows canonical order because the milestone set does not preserve receipt order.</summary>
        private static readonly string[] StageOrder =
            { "mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land" };

        /// <summary>Advisor hints in teaching order (vitals first, then situational).</summary>
        private static readonly string[] HintOrder =
            { "o2", "breathe", "energy", "cold", "heat", "hunger", "shipfood", "invfull", "night", "poi" };

        private static readonly string[] WorldOrder =
            { "asteroid", "ocean", "corrupted", "fungal", "ice", "volcanic" };

        /// <summary>
        /// Groups wrapped text lines into pages that fit a fixed-height box. Inputs are the wrap layout the
        /// UI text engine produced: the first character index and the height of each rendered line.
        /// Returns (start, length) character ranges, one per page; a single range covering everything when
        /// it already fits. A line taller than <paramref name="maxHeight"/> still gets a page of its own —
        /// pages never come back empty, so paging can never lose text (the bug this replaces, #736).
        /// </summary>
        public static List<(int Start, int Length)> PageRanges(
            IReadOnlyList<int> lineStarts, IReadOnlyList<float> lineHeights, int textLength, float maxHeight)
        {
            var pages = new List<(int, int)>();
            if (textLength <= 0)
            {
                return pages;
            }

            int count = Math.Min(lineStarts.Count, lineHeights.Count);
            if (count == 0)
            {
                pages.Add((0, textLength)); // no layout info — behave like the unpaged panel
                return pages;
            }

            int pageStart = 0; // index into lineStarts
            float pageHeight = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > pageStart && pageHeight + lineHeights[i] > maxHeight)
                {
                    pages.Add(Range(lineStarts, pageStart, i, textLength));
                    pageStart = i;
                    pageHeight = 0f;
                }

                pageHeight += lineHeights[i];
            }

            pages.Add(Range(lineStarts, pageStart, count, textLength));
            return pages;
        }

        private static (int, int) Range(IReadOnlyList<int> lineStarts, int firstLine, int endLine, int textLength)
        {
            int start = lineStarts[firstLine];
            int end = endLine < lineStarts.Count ? lineStarts[endLine] : textLength;
            return (start, Math.Max(0, end - start));
        }

        /// <summary>
        /// Rebuilds the "VEGA tips" log from a player's persisted <c>vega:*</c> milestones: every onboarding
        /// lesson and advisor hint they have already received, as locale keys in canonical order. Story and
        /// memory content (<c>vega:mem:*</c>) is excluded — the Story tab logs those live (Kind 2 lines).
        /// Unknown hint ids still map generically so future server hints appear without a client update
        /// (the UI drops keys with no translation).
        /// </summary>
        public static List<string> JournalKeys(IEnumerable<string> milestones)
        {
            var set = new HashSet<string>(milestones, StringComparer.Ordinal);
            var keys = new List<string>();

            if (set.Contains("vega:intro"))
            {
                keys.Add("vega.intro.1");
                keys.Add("vega.intro.2");
            }

            bool allStages = true;
            foreach (var id in StageOrder)
            {
                if (set.Contains("vega:stage:" + id))
                {
                    keys.Add("vega.s." + id + ".start");
                    keys.Add("vega.s." + id + ".done");
                }
                else
                {
                    allStages = false;
                }
            }

            if (allStages)
            {
                keys.Add("vega.done"); // the send-off — itself one of the longest (truncated) lines
            }

            foreach (var id in HintOrder)
            {
                if (set.Remove("vega:hint:" + id))
                {
                    keys.Add("vega.hint." + id);
                }
            }

            foreach (var id in WorldOrder)
            {
                if (set.Remove("vega:hint:world:" + id))
                {
                    keys.Add("vega.hint.world." + id);
                }
            }

            // The bandit briefing burns its once-flag directly (GameServerBanditShips), with a line key
            // that does not follow the vega.hint.<id> pattern.
            if (set.Remove("vega:hint:bandit_brief"))
            {
                keys.Add("vega.brief.bandits");
            }

            // Anything else under vega:hint: maps generically (future hints; world types beyond the list).
            // Sorted — set iteration order is arbitrary and the log should be stable across joins.
            var rest = new List<string>();
            foreach (var m in set)
            {
                if (m.StartsWith("vega:hint:", StringComparison.Ordinal))
                {
                    rest.Add("vega.hint." + m["vega:hint:".Length..].Replace(':', '.'));
                }
            }

            rest.Sort(StringComparer.Ordinal);
            keys.AddRange(rest);

            return keys;
        }
    }
}
