// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The tiny §-markup of player notes (#1844): <c>§0</c>–<c>§f</c> colour the text that follows,
    /// <c>§l</c> makes it bold, <c>§r</c> resets both. Styles never cross a line break — every line starts
    /// plain, so a forgotten reset cannot colour the rest of the page. Rendered to uGUI rich text for the
    /// note preview; the raw text (with the § codes) is what the player edits and what the server stores.
    /// Any literal "&lt;tag&gt;" the player typed is neutralised FIRST (<see cref="ChatMarkup.RichSafe"/>), so
    /// the only rich-text tags that reach the Text component are the ones this class emits. Unity-free so the
    /// headless test suite covers it.
    /// </summary>
    public static class NoteMarkup
    {
        /// <summary>The markup escape character.</summary>
        public const char Escape = '§';

        /// <summary>
        /// The 16 colours behind <c>§0</c>–<c>§f</c>, as "RRGGBB". Follows the familiar order (dark→light,
        /// blue/green/aqua/red/purple/gold/grey…) but every entry is lifted to stay readable on the dark
        /// menu panel — "§0" is a soft grey here, not black.
        /// </summary>
        public static readonly string[] Palette =
        {
            "9AA5B1", // 0 grey (would be black)
            "6FA8FF", // 1 blue
            "5FD37A", // 2 green
            "5FE0E0", // 3 aqua
            "FF6B6B", // 4 red
            "C48CFF", // 5 purple
            "FFC94D", // 6 gold
            "C8D2DC", // 7 light grey
            "8E9AA6", // 8 dark grey
            "8FC8FF", // 9 light blue
            "9CF08F", // a light green
            "A5F2F2", // b light aqua
            "FF9C9C", // c light red
            "FF9CE1", // d pink
            "FFF27A", // e yellow
            "FFFFFF", // f white
        };

        /// <summary>Renders <paramref name="raw"/> to uGUI rich text: colour + bold codes become tags, unknown
        /// codes are dropped, every open tag is closed at the end of each line and at the end of the text.</summary>
        public static string Render(string? raw)
        {
            string s = ChatMarkup.RichSafe(raw);
            if (s.IndexOf(Escape) < 0)
            {
                return s;
            }

            // The open tags as a stack (innermost last) so they always close in the mirrored order and the
            // rich text stays properly nested: bold opened inside a colour closes before that colour does, and
            // a colour change inside bold only re-opens the colour.
            var sb = new StringBuilder(s.Length + 32);
            var open = new List<char>(2); // 'c' = colour, 'b' = bold
            int color = -1;
            bool bold = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\n')
                {
                    CloseTo(sb, open, 0);
                    color = -1;
                    bold = false;
                    sb.Append(c);
                    continue;
                }

                if (c != Escape)
                {
                    sb.Append(c);
                    continue;
                }

                if (i + 1 >= s.Length)
                {
                    break; // a trailing lone "§" is dropped
                }

                char code = char.ToLowerInvariant(s[++i]);
                int hex = HexValue(code);
                if (hex >= 0)
                {
                    if (hex == color)
                    {
                        continue;
                    }

                    int at = open.IndexOf('c');
                    if (at >= 0)
                    {
                        CloseTo(sb, open, at); // closes bold too if it sits inside the colour…
                    }

                    color = hex;
                    sb.Append("<color=#").Append(Palette[color]).Append('>');
                    open.Add('c');
                    if (bold && !open.Contains('b'))
                    {
                        sb.Append("<b>"); // …so re-open it inside the new colour
                        open.Add('b');
                    }
                }
                else if (code == 'l')
                {
                    if (!bold)
                    {
                        bold = true;
                        sb.Append("<b>");
                        open.Add('b');
                    }
                }
                else if (code == 'r')
                {
                    CloseTo(sb, open, 0);
                    color = -1;
                    bold = false;
                }

                // any other "§x" is stripped (both characters)
            }

            CloseTo(sb, open, 0);
            return sb.ToString();
        }

        /// <summary>Closes the open tags from the innermost down to (and including) index <paramref name="keep"/>.</summary>
        private static void CloseTo(StringBuilder sb, List<char> open, int keep)
        {
            for (int i = open.Count - 1; i >= keep; i--)
            {
                sb.Append(open[i] == 'b' ? "</b>" : "</color>");
                open.RemoveAt(i);
            }
        }

        /// <summary>The colour codes the note editor's toolbar offers (#2577): the vivid entries of
        /// <see cref="Palette"/>, in rainbow order — red, gold, yellow, green, aqua, blue, purple, pink.</summary>
        public static readonly char[] ToolbarColors = { '4', '6', 'e', '2', '3', '1', '5', 'd' };

        /// <summary>
        /// What the note editor's style buttons do (#2577) — so nobody has to know the codes. <paramref name="code"/>
        /// is a colour digit <c>0</c>–<c>f</c>, <c>l</c> for bold or <c>r</c> for plain.
        /// <list type="bullet">
        /// <item>Nothing selected (<paramref name="start"/> == <paramref name="end"/>): the code goes in at the caret,
        /// so what is typed next takes the style.</item>
        /// <item>A selection: the style covers it on every line it touches (styles never cross a line break), and
        /// right after it the style that was active there comes back, so the rest of the line keeps its look. Plain
        /// also removes the codes inside the selection.</item>
        /// </list>
        /// A selection edge that falls between "§" and its code character moves outward, so no code is ever split.
        /// Returns the new raw text; <paramref name="caret"/> is where the caret belongs afterwards (after the inserted
        /// code, or after the styled selection). An unknown <paramref name="code"/> changes nothing.
        /// </summary>
        public static string ApplyStyle(string? raw, int start, int end, char code, out int caret)
        {
            string s = raw ?? string.Empty;
            if (start > end)
            {
                (start, end) = (end, start);
            }

            start = start < 0 ? 0 : (start > s.Length ? s.Length : start);
            end = end < 0 ? 0 : (end > s.Length ? s.Length : end);
            code = char.ToLowerInvariant(code);
            if (HexValue(code) < 0 && code != 'l' && code != 'r')
            {
                caret = end;
                return s;
            }

            if (IsCodeChar(s, start))
            {
                start--; // between "§" and its code: start before the pair
            }

            if (IsCodeChar(s, end))
            {
                end++; // between "§" and its code: take the whole pair
            }

            var sb = new StringBuilder(s.Length + 16);
            if (start == end)
            {
                sb.Append(s, 0, start).Append(Escape).Append(code).Append(s, start, s.Length - start);
                caret = start + 2;
                return sb.ToString();
            }

            int copied = 0;
            caret = end;
            int segStart = start;
            while (true)
            {
                int lineEnd = s.IndexOf('\n', segStart);
                if (lineEnd < 0)
                {
                    lineEnd = s.Length;
                }

                int segEnd = end < lineEnd ? end : lineEnd;
                if (segEnd > segStart)
                {
                    sb.Append(s, copied, segStart - copied);
                    string piece = s.Substring(segStart, segEnd - segStart);
                    bool lineGoesOn = segEnd < lineEnd; // at a line end every style stops anyway
                    var after = StateAt(s, segEnd);
                    if (code == 'r')
                    {
                        if (StateAt(s, segStart) != (-1, false))
                        {
                            sb.Append(Escape).Append('r');
                        }

                        sb.Append(Strip(piece));
                        if (lineGoesOn)
                        {
                            AppendState(sb, after);
                        }
                    }
                    else
                    {
                        sb.Append(Escape).Append(code).Append(piece);
                        if (lineGoesOn)
                        {
                            sb.Append(Escape).Append('r');
                            AppendState(sb, after);
                        }
                    }

                    copied = segEnd;
                    caret = sb.Length;
                }

                if (lineEnd >= end)
                {
                    break;
                }

                segStart = lineEnd + 1;
            }

            sb.Append(s, copied, s.Length - copied);
            return sb.ToString();
        }

        /// <summary>True when <paramref name="index"/> is the code character of a "§x" pair — pairs read from the line
        /// start exactly as <see cref="Render"/> reads them ("§" plus whatever follows it).</summary>
        private static bool IsCodeChar(string s, int index)
        {
            if (index <= 0 || index >= s.Length)
            {
                return false;
            }

            int i = s.LastIndexOf('\n', index - 1) + 1;
            while (i < index)
            {
                if (s[i] == Escape && i + 1 < s.Length)
                {
                    if (i + 1 == index)
                    {
                        return true;
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            return false;
        }

        /// <summary>The style in force just before <paramref name="index"/>: colour (-1 = none) and bold, read from the
        /// start of that line.</summary>
        private static (int Color, bool Bold) StateAt(string s, int index)
        {
            int color = -1;
            bool bold = false;
            int i = index > 0 ? s.LastIndexOf('\n', index - 1) + 1 : 0;
            while (i < index)
            {
                if (s[i] == Escape && i + 1 < s.Length)
                {
                    char c = char.ToLowerInvariant(s[i + 1]);
                    int hex = HexValue(c);
                    if (hex >= 0)
                    {
                        color = hex;
                    }
                    else if (c == 'l')
                    {
                        bold = true;
                    }
                    else if (c == 'r')
                    {
                        color = -1;
                        bold = false;
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            return (color, bold);
        }

        private static void AppendState(StringBuilder sb, (int Color, bool Bold) state)
        {
            if (state.Color >= 0)
            {
                sb.Append(Escape).Append("0123456789abcdef"[state.Color]);
            }

            if (state.Bold)
            {
                sb.Append(Escape).Append('l');
            }
        }

        /// <summary>The text with every markup code removed — what a note "says", for search and list cards.</summary>
        public static string Strip(string? raw)
        {
            string s = raw ?? string.Empty;
            if (s.IndexOf(Escape) < 0)
            {
                return s;
            }

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == Escape)
                {
                    i++; // drop the code character too (a trailing lone "§" just ends the loop)
                    continue;
                }

                sb.Append(s[i]);
            }

            return sb.ToString();
        }

        /// <summary>The first non-empty line of the note without markup, trimmed — the dim preview line under
        /// a note's title in the list. Empty when the note has no text.</summary>
        public static string FirstLine(string? raw)
        {
            string plain = Strip(raw);
            foreach (string line in plain.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0)
                {
                    return t;
                }
            }

            return string.Empty;
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return 10 + (c - 'a');
            }

            return -1;
        }

    }
}
