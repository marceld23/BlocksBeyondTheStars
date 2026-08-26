// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Text;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Which set of keys the on-screen keyboard is showing.</summary>
    public enum KeyboardPage
    {
        /// <summary>Digits + letters (plus the German umlauts, which a player name or a world name needs).</summary>
        Letters,

        /// <summary>Punctuation and symbols.</summary>
        Symbols,
    }

    /// <summary>What kind of text the target field takes — the on-screen keyboard's mirror of a uGUI
    /// <c>InputField.contentType</c> (#1289). The Unity side maps the field's type onto this; the filter and
    /// the preview masking live here so they are tested headlessly.</summary>
    public enum KeyboardContentKind
    {
        /// <summary>Anything goes (names, labels, chat, addresses).</summary>
        Text,

        /// <summary>Whole numbers: digits, plus a leading minus.</summary>
        Integer,

        /// <summary>Decimal numbers: digits, one decimal point, plus a leading minus.</summary>
        Decimal,
    }

    /// <summary>
    /// The key layout and the text edits of the gamepad on-screen keyboard (#1211) — the whole model, with
    /// no Unity in it, so the part that can actually be wrong (which key produces what, where the character
    /// limit bites, what backspace does to an empty string) is covered by the headless test suite. The Unity
    /// side is then only buttons: it asks for <see cref="Rows"/>, and hands every press to <see cref="Apply"/>.
    ///
    /// Commands travel as bracketed tokens rather than control characters so a row is just an array of
    /// strings and nothing has to encode "this one is special" a second way.
    /// </summary>
    public static class OnScreenKeyboardLayout
    {
        /// <summary>Delete the character before the caret (the caret is always at the end).</summary>
        public const string Backspace = "[back]";

        /// <summary>Insert a single space.</summary>
        public const string Space = "[space]";

        /// <summary>Switch upper/lower case for the letter page.</summary>
        public const string Shift = "[shift]";

        /// <summary>Switch between the letter page and the symbol page.</summary>
        public const string Page = "[page]";

        /// <summary>Accept the text.</summary>
        public const string Done = "[done]";

        /// <summary>Discard the edit.</summary>
        public const string Cancel = "[cancel]";

        // Four rows per page, each row one string of single-character keys. Deliberately QWERTY (not
        // QWERTZ) on both locales: the German keys a name actually needs are the umlauts and ß, and those
        // get their own places at the end of the bottom row rather than a whole second layout to maintain.
        private static readonly string[] LetterRows =
        {
            "1234567890",
            "qwertyuiop",
            "asdfghjkl",
            "zxcvbnmäöüß",
        };

        // No "<" or ">": chat neutralises uGUI markup on the way in (see ChatMarkup), but a label or a world
        // name has no such pass, and neither needs angle brackets.
        private static readonly string[] SymbolRows =
        {
            "1234567890",
            "@#$%&*-+()",
            "!?,.:;/_'\"",
            "=[]{}~^|\\",
        };

        /// <summary>The key rows for a page. <paramref name="shift"/> uppercases the letter page; it has no
        /// effect on the symbol page (its keys have no case), so the caller may pass either.</summary>
        public static string[] Rows(KeyboardPage page, bool shift)
        {
            if (page == KeyboardPage.Symbols)
            {
                return (string[])SymbolRows.Clone();
            }

            var rows = (string[])LetterRows.Clone();
            if (!shift)
            {
                return rows;
            }

            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = Upper(rows[i]);
            }

            return rows;
        }

        /// <summary>Uppercases per character with the invariant culture. Not <c>ToUpperInvariant()</c> on the
        /// whole string for one reason: ß. Culture-aware casing can turn it into "SS", which would make one
        /// key produce two characters and quietly break the character limit; the invariant per-char rule
        /// leaves it alone, which is what a name field wants anyway.</summary>
        private static string Upper(string row)
        {
            var sb = new StringBuilder(row.Length);
            foreach (char c in row)
            {
                sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString();
        }

        /// <summary>True for the bracketed command tokens above — everything else is literal text.</summary>
        public static bool IsCommand(string key) =>
            key == Backspace || key == Space || key == Shift || key == Page || key == Done || key == Cancel;

        /// <summary>
        /// The text after pressing <paramref name="key"/>. Handles exactly the two commands that edit text
        /// (<see cref="Backspace"/>, <see cref="Space"/>) and literal keys; <see cref="Shift"/>,
        /// <see cref="Page"/>, <see cref="Done"/> and <see cref="Cancel"/> change the keyboard or close it
        /// rather than the string, so they return it unchanged and the caller acts on them.
        ///
        /// <paramref name="maxLength"/> 0 or less means no limit. A press that would exceed the limit is
        /// dropped silently — the same thing a uGUI character limit does when you keep typing.
        /// </summary>
        public static string Apply(string? text, string key, int maxLength)
        {
            text ??= string.Empty;
            if (string.IsNullOrEmpty(key))
            {
                return text;
            }

            if (key == Backspace)
            {
                return text.Length == 0 ? text : text.Substring(0, text.Length - 1);
            }

            string insert = key == Space ? " " : key;
            if (IsCommand(key) && key != Space)
            {
                return text;
            }

            if (maxLength > 0 && text.Length + insert.Length > maxLength)
            {
                return text;
            }

            return text + insert;
        }

        /// <summary>
        /// <see cref="Apply(string?, string, int)"/> with the field's content kind (#1289): a literal key the
        /// kind does not accept is dropped silently, the same way uGUI's own character validation swallows a
        /// letter typed into a number field. Commands (backspace, shift, page, done, cancel) always pass;
        /// space is a literal and so is filtered too.
        /// </summary>
        public static string Apply(string? text, string key, int maxLength, KeyboardContentKind kind)
        {
            text ??= string.Empty;
            if (!Accepts(kind, text, key))
            {
                return text;
            }

            return Apply(text, key, maxLength);
        }

        /// <summary>Whether <paramref name="key"/> may be appended to <paramref name="text"/> for a field of
        /// this kind. Numbers: digits anywhere, '-' only as the first character, '.' only once and only for
        /// <see cref="KeyboardContentKind.Decimal"/>. Commands other than <see cref="Space"/> always pass.</summary>
        public static bool Accepts(KeyboardContentKind kind, string? text, string key)
        {
            if (string.IsNullOrEmpty(key) || kind == KeyboardContentKind.Text)
            {
                return true;
            }

            if (IsCommand(key))
            {
                return key != Space; // a number has no spaces in it
            }

            text ??= string.Empty;
            if (key.Length != 1)
            {
                return false;
            }

            char c = key[0];
            if (c >= '0' && c <= '9')
            {
                return true;
            }

            if (c == '-')
            {
                return text.Length == 0;
            }

            if (c == '.' && kind == KeyboardContentKind.Decimal)
            {
                return text.IndexOf('.') < 0;
            }

            return false;
        }

        /// <summary>The text to show on the preview line: the text itself, or one bullet per character when
        /// the field is a password / PIN (#1289 — the preview is large and readable from across the room).</summary>
        public static string Preview(string? text, bool mask)
        {
            text ??= string.Empty;
            return mask ? new string('•', text.Length) : text;
        }
    }
}
