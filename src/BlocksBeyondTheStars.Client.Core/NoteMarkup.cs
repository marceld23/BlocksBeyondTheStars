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
