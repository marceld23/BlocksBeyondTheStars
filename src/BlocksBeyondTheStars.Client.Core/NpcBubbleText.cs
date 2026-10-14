// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2605: the text of an NPC speech bubble — clamped to a readable length and held long enough to read. AI lines on
    /// hosted worlds run up to ~350 characters; a bubble shows at most <see cref="MaxChars"/>: whitespace collapsed, cut at
    /// the last sentence end that keeps enough of the text, else at the last word boundary, else hard (CJK has no spaces)
    /// — a word or hard cut ends in "…". Unity-free; <c>NpcView</c> draws the result with <c>ScreenLabelLayer.Bubble</c>.
    /// </summary>
    public static class NpcBubbleText
    {
        /// <summary>The longest bubble text (characters, the ellipsis included).</summary>
        public const int MaxChars = 200;

        /// <summary>The shortest hold of a bubble (seconds).</summary>
        public const float MinHoldSeconds = 7f;

        /// <summary>The longest hold of a bubble (seconds).</summary>
        public const float MaxHoldSeconds = 15f;

        /// <summary>The ellipsis a word or hard cut ends in.</summary>
        public const string Ellipsis = "…";

        /// <summary>A sentence or word cut must keep at least this share of the allowed length, else the next rule cuts.</summary>
        private const float MinKeep = 0.4f;

        /// <summary>
        /// <paramref name="text"/> fit for a bubble of at most <paramref name="max"/> characters: trimmed, every run of
        /// whitespace (line breaks included) one space; a longer text is cut at its last sentence end (<c>. ! ? 。 ！ ？</c>)
        /// within the limit, else at the last space (plus "…"), else hard (plus "…", never inside a surrogate pair).
        /// </summary>
        public static string Clamp(string? text, int max = MaxChars)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string s = Collapse(text!);
            if (max <= 0)
            {
                return string.Empty;
            }

            if (s.Length <= max)
            {
                return s;
            }

            int keep = (int)(max * MinKeep);

            // 1) The last sentence end that fits: the sentence reads complete, no ellipsis needed.
            for (int i = max - 1; i >= keep; i--)
            {
                if (IsSentenceEnd(s[i]))
                {
                    return s.Substring(0, i + 1);
                }
            }

            // 2) The last word boundary that leaves room for the ellipsis.
            int room = max - Ellipsis.Length;
            for (int i = room; i >= keep; i--)
            {
                if (s[i] == ' ')
                {
                    return s.Substring(0, i).TrimEnd() + Ellipsis;
                }
            }

            // 3) Hard cut (CJK), never between the halves of a surrogate pair.
            int cut = room;
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1]))
            {
                cut--;
            }

            return s.Substring(0, cut) + Ellipsis;
        }

        /// <summary>How long a bubble of <paramref name="length"/> characters stays up: 4 s + one second per 14 characters,
        /// held between <see cref="MinHoldSeconds"/> and <see cref="MaxHoldSeconds"/>.</summary>
        public static float HoldSeconds(int length)
        {
            float hold = 4f + (length < 0 ? 0 : length) / 14f;
            return hold < MinHoldSeconds ? MinHoldSeconds : hold > MaxHoldSeconds ? MaxHoldSeconds : hold;
        }

        private static bool IsSentenceEnd(char c) => c == '.' || c == '!' || c == '?' || c == '。' || c == '！' || c == '？';

        private static string Collapse(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = sb.Length > 0;
                    continue;
                }

                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
