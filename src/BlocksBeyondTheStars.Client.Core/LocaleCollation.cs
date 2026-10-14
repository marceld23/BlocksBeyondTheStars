// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The A–Z order of the player-facing lists (#2566: the crafting list, the Blueprints tab, the Sandbox catalog) in the
    /// ACTIVE GAME LANGUAGE — not the operating system's culture: a German game on an English Windows must still put
    /// "Äpfel" between "Apfel" and "Birne", and a Polish one "Ćma" right after "Cegła". Case is ignored.
    /// <para>
    /// Where the comparer comes from: the game locale code (<see cref="GameLocaleExtensions.TryParse"/>, so "de", "de-DE"
    /// or "Deutsch" all work) picks the matching .NET culture and its linguistic comparer; an unknown code gets the
    /// invariant culture's. Platforms differ in what that comparer can do — the desktop player (Mono) and the .NET test
    /// host carry full collation data, but a runtime in invariant-globalization mode, or an IL2CPP player (the WebGL
    /// build) whose collation tables are missing or stripped, may refuse the culture or compare by code point, which would
    /// sort every "Ä" behind "Z". So each comparer is probed once ("é" must sort before "f", case must not count); one
    /// that fails is replaced by <see cref="Folding"/>, a small platform-independent comparer that folds the accented
    /// Latin letters of the game's languages (and Cyrillic ё/є/і/ї/ґ) onto their base letters. Its order is close to the
    /// real one for every game language and never depends on the platform's culture data.
    /// </para>
    /// </summary>
    public static class LocaleCollation
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, StringComparer> Cache = new Dictionary<string, StringComparer>(StringComparer.Ordinal);

        /// <summary>The platform-independent fallback comparer: accent-folded, case-insensitive, then ordinal.</summary>
        public static StringComparer Folding { get; } = new FoldingComparer();

        /// <summary>The case-insensitive comparer for the game language <paramref name="localeCode"/> — never null, never
        /// throws. Built once per code and cached.</summary>
        public static StringComparer Comparer(string? localeCode)
        {
            string code = (localeCode ?? string.Empty).Trim();
            lock (Gate)
            {
                if (Cache.TryGetValue(code, out var cached))
                {
                    return cached;
                }

                var made = Create(code);
                Cache[code] = made;
                return made;
            }
        }

        /// <summary>The comparer for the game locale <paramref name="locale"/>.</summary>
        public static StringComparer Comparer(GameLocale locale) => Comparer(locale.Code());

        private static StringComparer Create(string code)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            if (GameLocaleExtensions.TryParse(code, out var locale))
            {
                try
                {
                    culture = CultureInfo.GetCultureInfo(CultureName(locale));
                }
                catch (Exception)
                {
                    culture = CultureInfo.InvariantCulture; // culture data missing on this platform
                }
            }

            try
            {
                var comparer = StringComparer.Create(culture, ignoreCase: true);
                if (SortsLinguistically(comparer))
                {
                    return comparer;
                }
            }
            catch (Exception)
            {
                // A runtime without collation support may throw on first use; the folding comparer needs none.
            }

            return Folding;
        }

        /// <summary>The .NET culture name of a game locale; the locale code except for Chinese, whose code is the bare
        /// language but whose texts are Simplified.</summary>
        private static string CultureName(GameLocale locale)
            => locale == GameLocale.ChineseSimplified ? "zh-Hans" : locale.Code();

        /// <summary>True when <paramref name="comparer"/> really collates — an accented letter beside its base letter,
        /// not behind "z" — and ignores case. Holds for every game language's culture; a code-point comparer fails.</summary>
        private static bool SortsLinguistically(StringComparer comparer)
            => comparer.Compare("é", "f") < 0
               && comparer.Compare("e", "é") < 0
               && comparer.Compare("a", "B") < 0
               && comparer.Compare("A", "a") == 0;

        /// <summary>Accent-folding comparer: compares the strings with the accented letters replaced by their base
        /// letters (ß → SS, Æ → AE, Œ → OE) and upper-cased, ordinally; equal folds fall back to an ordinal
        /// case-insensitive comparison, so "Apfel" stays before "Äpfel" and the order is total.</summary>
        private sealed class FoldingComparer : StringComparer
        {
            private static readonly Dictionary<char, string> Folds = BuildFolds();

            public override int Compare(string? x, string? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x is null)
                {
                    return -1;
                }

                if (y is null)
                {
                    return 1;
                }

                int folded = string.CompareOrdinal(Fold(x), Fold(y));
                return folded != 0 ? folded : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            public override int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);

            internal static string Fold(string s)
            {
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                {
                    if (Folds.TryGetValue(c, out var to))
                    {
                        sb.Append(to);
                    }
                    else
                    {
                        sb.Append(char.ToUpperInvariant(c));
                    }
                }

                return sb.ToString();
            }

            private static Dictionary<char, string> BuildFolds()
            {
                var map = new Dictionary<char, string>();
                void Add(string chars, string to)
                {
                    foreach (char c in chars)
                    {
                        map[c] = to;
                    }
                }

                // Both cases are listed: on a runtime with ASCII-only case mapping ToUpperInvariant leaves "ä" alone.
                Add("ÀÁÂÃÄÅĀĂĄàáâãäåāăą", "A");
                Add("Ææ", "AE");
                Add("ÇĆĈĊČçćĉċč", "C");
                Add("ĎĐďđ", "D");
                Add("ÈÉÊËĒĔĖĘĚèéêëēĕėęě", "E");
                Add("ĜĞĠĢĝğġģ", "G");
                Add("ÌÍÎÏĨĪĬĮİìíîïĩīĭįı", "I");
                Add("ĹĻĽĿŁĺļľŀł", "L");
                Add("ÑŃŅŇñńņň", "N");
                Add("ÒÓÔÕÖØŌŎŐòóôõöøōŏő", "O");
                Add("Œœ", "OE");
                Add("ŔŖŘŕŗř", "R");
                Add("ŚŜŞŠśŝşš", "S");
                Add("ß", "SS");
                Add("ŢŤţť", "T");
                Add("ÙÚÛÜŨŪŬŮŰŲùúûüũūŭůűų", "U");
                Add("ÝŸýÿ", "Y");
                Add("ŹŻŽźżž", "Z");

                // Cyrillic letters that sit outside the alphabet's code-point run: Russian ё beside е, and the
                // Ukrainian є, і, ї, ґ beside е, и, г — close to their real places.
                Add("Ёё", "Е");
                Add("Єє", "Е");
                Add("ІіЇї", "И");
                Add("Ґґ", "Г");
                return map;
            }
        }
    }
}
