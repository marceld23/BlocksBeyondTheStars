// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Text;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Keeps text that is merely *displayed* in the chat scrollback from being parsed as uGUI rich-text
    /// markup. The log renders with <c>supportRichText = true</c> (it colours system lines and bolds the
    /// sender), so a chat line containing "&lt;color=#ff0000&gt;" would otherwise recolour everyone's log —
    /// and a "&lt;size=200&gt;" would blow it up. Unity-free so the headless test suite covers it.
    /// </summary>
    public static class ChatMarkup
    {
        /// <summary>
        /// Neutralises rich-text markup in <paramref name="text"/> by inserting a space after any "&lt;"
        /// that would start a tag: uGUI only treats "&lt;" as markup when a letter or "/" follows it
        /// immediately, so "&lt; b&gt;" renders literally. A space is used rather than an escape character
        /// (‹, &amp;lt;) because the bundled Rajdhani font is not guaranteed to carry those glyphs, and
        /// because the player still sees every character they typed.
        /// </summary>
        public static string RichSafe(string? text)
        {
            string s = text ?? string.Empty;
            if (s.IndexOf('<') < 0)
            {
                return s;
            }

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if (s[i] == '<' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '/'))
                {
                    sb.Append(' ');
                }
            }

            return sb.ToString();
        }
    }
}
