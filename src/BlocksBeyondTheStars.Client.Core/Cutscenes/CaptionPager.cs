// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>
    /// Splits a cutscene caption into pages a child can read in one look (#2468, d4 §2.2/§2.3): a subtitle page is about
    /// two lines, a speaker box one VEGA panel page, a reading frame a short paragraph. Pages end at a sentence end when
    /// one is in reach, else at a word boundary, else at the character limit (Chinese, Japanese and Korean have no spaces).
    /// Character-based on purpose: the planner must be Unity-free and deterministic, and the director's boxes are sized
    /// for these budgets (the content validator holds every caption to its style's budget). No text is ever lost — the
    /// pages put back together are the input without the whitespace at the cuts.
    /// </summary>
    public static class CaptionPager
    {
        /// <summary>A page shorter than this share of the limit is not cut at a sentence end — "Yes." alone on a page
        /// reads as a glitch; the cut moves to the last word boundary instead.</summary>
        private const float MinSentencePageShare = 0.34f;

        /// <summary>The pages of <paramref name="text"/>, none longer than <paramref name="maxChars"/> (trimmed). An empty
        /// text has no pages; a limit of 0 or less keeps the text on one page.</summary>
        public static IReadOnlyList<string> Pages(string? text, int maxChars)
        {
            var pages = new List<string>();
            string t = (text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                return pages;
            }

            if (maxChars <= 0 || t.Length <= maxChars)
            {
                pages.Add(t);
                return pages;
            }

            int start = 0;
            while (start < t.Length)
            {
                while (start < t.Length && char.IsWhiteSpace(t[start]))
                {
                    start++;
                }

                if (start >= t.Length)
                {
                    break;
                }

                if (t.Length - start <= maxChars)
                {
                    pages.Add(t.Substring(start).Trim());
                    break;
                }

                int limit = start + maxChars;
                int cut = LastSentenceEnd(t, start, limit, (int)(maxChars * MinSentencePageShare));
                if (cut <= start)
                {
                    cut = LastWhitespace(t, start, limit);
                }

                if (cut <= start)
                {
                    cut = limit;
                    if (char.IsLowSurrogate(t[cut]) && cut - 1 > start)
                    {
                        cut--; // never split an emoji or a rare CJK character in two
                    }
                }

                string page = t.Substring(start, cut - start).Trim();
                if (page.Length > 0)
                {
                    pages.Add(page);
                }

                start = cut;
            }

            return pages;
        }

        /// <summary>The index just after the last sentence end in <c>[start, limit]</c> that leaves a page of at least
        /// <paramref name="minLength"/> characters, or -1.</summary>
        private static int LastSentenceEnd(string t, int start, int limit, int minLength)
        {
            for (int i = limit; i > start + minLength; i--)
            {
                if (IsSentenceEndAt(t, i))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether a sentence ends right before index <paramref name="i"/>: a full stop, question or exclamation
        /// mark or ellipsis (optionally followed by a closing quote or bracket) and then whitespace or the end of the text.
        /// The CJK marks end a sentence without a following space.</summary>
        private static bool IsSentenceEndAt(string t, int i)
        {
            if (i <= 0 || i > t.Length)
            {
                return false;
            }

            char before = t[i - 1];
            if (IsCjkSentenceMark(before))
            {
                return true;
            }

            bool followedByBreak = i == t.Length || char.IsWhiteSpace(t[i]);
            if (!followedByBreak)
            {
                return false;
            }

            if (IsSentenceMark(before))
            {
                return true;
            }

            return IsClosing(before) && i >= 2 && IsSentenceMark(t[i - 2]);
        }

        private static bool IsSentenceMark(char c) => c is '.' or '!' or '?' or '…';

        private static bool IsCjkSentenceMark(char c) => c is '。' or '！' or '？';

        private static bool IsClosing(char c) => c is '"' or '\'' or '”' or '“' or '»' or '«' or ')' or '’' or '」' or '』';

        /// <summary>The index of the last whitespace in <c>(start, limit]</c>, or -1.</summary>
        private static int LastWhitespace(string t, int start, int limit)
        {
            for (int i = limit; i > start; i--)
            {
                if (char.IsWhiteSpace(t[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
