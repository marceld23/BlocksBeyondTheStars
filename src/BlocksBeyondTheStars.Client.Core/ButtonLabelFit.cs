// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The editors' button-label rule (#3263): a label stays on ONE line and shrinks down to
    /// <see cref="SingleLineMin"/>; when it still does not fit, it takes TWO lines broken only between words, at
    /// <see cref="TwoLineMin"/> or larger; when even that fails the caller logs a warning — the label never drops to
    /// the 11 px of the old Best Fit (<c>UiKit.FitLabel(label, 11, 22)</c>), which also broke words in the middle
    /// ("Spiegel|n ↕") whenever a word was wider than the button. Pure: the width of a string at a font size comes
    /// from the caller — Unity's <c>TextGenerator</c> in the game, the bundled fonts' advance table in the headless
    /// fit test — so the game and its guard apply the very same rule.
    /// </summary>
    public static class ButtonLabelFit
    {
        /// <summary>The size a label starts from (<c>UiKit.AddButton</c>'s 22 px).</summary>
        public const int MaxSize = 22;

        /// <summary>The smallest size a ONE-line label may shrink to before it takes a second line.</summary>
        public const int SingleLineMin = 14;

        /// <summary>The smallest size of a two-line label (and of every other editor text, #3263).</summary>
        public const int TwoLineMin = 13;

        /// <summary>The line box of the UI font per em (Rajdhani-Medium: hhea ascender 930 − descender −346).</summary>
        public const float LineHeightEm = 1.276f;

        /// <summary>Share of the box a text may fill: the canvas scales the editors' 1920×1080 layout and uGUI
        /// rasterises the text at a whole pixel size, so a word that fills its box exactly at scale 1 can come out a
        /// few per cent wider on another screen — and then uGUI breaks it in the middle.</summary>
        public const float WidthSlack = 0.96f;

        /// <summary>How far the line boxes may overhang the label's rect: the glyphs' ink sits well inside the box.</summary>
        public const float HeightSlack = 2f;

        /// <summary>The chosen font size, the number of lines and whether the rule was met.</summary>
        public readonly struct Result
        {
            public Result(int size, int lines, bool fits)
            {
                Size = size;
                Lines = lines;
                Fits = fits;
            }

            public int Size { get; }

            public int Lines { get; }

            public bool Fits { get; }
        }

        /// <summary>
        /// Fits <paramref name="text"/> into a <paramref name="width"/> × <paramref name="height"/> label:
        /// the largest size from <paramref name="maxSize"/> down to <paramref name="singleLineMin"/> at which it fits
        /// on one line; else the largest size down to <paramref name="twoLineMin"/> at which it wraps into at most
        /// <paramref name="maxLines"/> lines (two for a button) that fit the height, without splitting a word; else
        /// <paramref name="twoLineMin"/> with <see cref="Result.Fits"/> false. <paramref name="measure"/> returns the
        /// one-line width of a string at a font size. A hint line passes its own sizes (16 → 13 on one line, then wrapped
        /// at 13).
        /// </summary>
        public static Result Fit(string text, float width, float height, Func<string, int, float> measure, int maxSize = MaxSize,
            int singleLineMin = SingleLineMin, int twoLineMin = TwoLineMin, int maxLines = 2)
        {
            if (measure == null)
            {
                throw new ArgumentNullException(nameof(measure));
            }

            text ??= string.Empty;
            float room = width * WidthSlack;
            if (text.IndexOf('\n') < 0)
            {
                for (int size = maxSize; size >= singleLineMin; size--)
                {
                    if (LinesHeight(1, size) <= height + HeightSlack && measure(text, size) <= room)
                    {
                        return new Result(size, 1, true);
                    }
                }
            }

            for (int size = Math.Max(maxSize, twoLineMin); size >= twoLineMin; size--)
            {
                if (LinesHeight(2, size) > height + HeightSlack)
                {
                    continue;
                }

                int lines = CountLines(text, room, size, measure, out bool brokeWord);
                if (!brokeWord && lines <= maxLines && LinesHeight(lines, size) <= height + HeightSlack)
                {
                    return new Result(size, lines, true);
                }
            }

            return new Result(twoLineMin, CountLines(text, room, twoLineMin, measure, out _), false);
        }

        /// <summary>The height of <paramref name="lines"/> lines at <paramref name="size"/>.</summary>
        public static float LinesHeight(int lines, int size) => lines * size * LineHeightEm;

        /// <summary>
        /// Wraps like uGUI's legacy text: greedily by words (runs between spaces); a word that does not fit the rest
        /// of a line moves to the next one, and a word wider than a whole line is split by character —
        /// <paramref name="brokeWord"/> then reports a word broken in the middle. A run with CJK characters may break
        /// anywhere (that is how those languages wrap), so splitting it is no broken word. Explicit line breaks count.
        /// </summary>
        public static int CountLines(string text, float width, int size, Func<string, int, float> measure, out bool brokeWord)
        {
            brokeWord = false;
            int lines = 0;
            foreach (string paragraph in (text ?? string.Empty).Split('\n'))
            {
                lines += CountParagraphLines(paragraph, width, size, measure, ref brokeWord);
            }

            return lines;
        }

        private static int CountParagraphLines(string paragraph, float width, int size, Func<string, int, float> measure, ref bool brokeWord)
        {
            int lines = 1;
            string line = string.Empty;
            foreach (string word in paragraph.Split(' '))
            {
                if (word.Length == 0)
                {
                    continue;
                }

                string candidate = line.Length == 0 ? word : line + " " + word;
                if (measure(candidate, size) <= width)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines++;
                }

                line = word;
                if (measure(word, size) <= width)
                {
                    continue;
                }

                if (!HasCjk(word))
                {
                    brokeWord = true;
                }

                foreach (string piece in SplitByCharacter(word, width, size, measure))
                {
                    lines++;
                    line = piece;
                }

                lines--; // the last piece is the line the next word continues
            }

            return lines;
        }

        /// <summary>Splits a word wider than <paramref name="width"/> into pieces that each fit (at least one character).</summary>
        private static IEnumerable<string> SplitByCharacter(string word, float width, int size, Func<string, int, float> measure)
        {
            int start = 0;
            while (start < word.Length)
            {
                int end = start + 1;
                while (end < word.Length && measure(word.Substring(start, end + 1 - start), size) <= width)
                {
                    end++;
                }

                yield return word.Substring(start, end - start);
                start = end;
            }
        }

        /// <summary>True when the run holds a character of a script that breaks between any two characters (Han, kana,
        /// fullwidth forms) — Korean is not one of them, it separates words with spaces.</summary>
        public static bool HasCjk(string run)
        {
            foreach (char c in run ?? string.Empty)
            {
                if ((c >= 0x3000 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFF00 && c <= 0xFFEF))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
