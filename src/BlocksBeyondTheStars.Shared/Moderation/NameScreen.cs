// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Moderation;

/// <summary>Outcome of screening a player/world/account name.</summary>
public enum NameVerdict
{
    Ok,

    /// <summary>The name is allowed, but the operator should be told (log line + admin notification):
    /// the matched term is too ambiguous to hard-block — a human decides, not the filter.</summary>
    Watch,

    /// <summary>The name is rejected outright.</summary>
    Block,
}

/// <summary>Result of <see cref="NameScreen.Screen"/>: the verdict plus the list entry that matched
/// (normalized form) — for the operator's log/notification, never shown to players.</summary>
public readonly struct NameScreenResult
{
    public NameScreenResult(NameVerdict verdict, string matchedTerm)
    {
        Verdict = verdict;
        MatchedTerm = matchedTerm;
    }

    public NameVerdict Verdict { get; }

    public string MatchedTerm { get; }
}

/// <summary>
/// Layered name screening for the kid-facing surfaces (account names, world names, in-game player
/// names). One shared implementation so the WorldHost gates and the game-server join gate cannot
/// drift apart (issue #938).
///
/// Two tiers with different match semantics, because they carry different confidence:
/// <list type="bullet">
/// <item><b>Block list</b> — unambiguous terms, matched as SUBSTRINGS of the normalized name.
/// Normalization folds case, diacritics ("hïtler"), every separator ("h.i.t.l.e.r"), common
/// leetspeak digits/symbols ("h1tl3r", "n4zi") and repeated letters ("fuuck"). The list stays
/// deliberately short: substring semantics make every entry a Scunthorpe risk, so number codes or
/// short abbreviations NEVER belong here.</item>
/// <item><b>Watch list</b> — ambiguous terms (extremist number codes and abbreviations, serial-killer
/// names, authority impersonation). A hit is allowed through but reported to the operator. Short
/// entries match whole name tokens only ("88" flags "Max88", not "Tom1988"); entries of five or more
/// characters also match as substrings; a leading '=' pins an entry to token-only regardless of
/// length ("=support" must not flag "Supporter"). A small high-severity core is additionally
/// fuzzy-matched per token with edit distance ≤ 1, so one-letter evasions ("adof88") still flag.</item>
/// </list>
/// Known limitation: non-Latin homoglyphs (Cyrillic "а" for "a") are not folded; account names are
/// restricted to ASCII by their own regex, and the watch/report pipeline covers the rest.
/// </summary>
public sealed class NameScreen
{
    /// <summary>The default block list: the historical WorldHost list plus unambiguous extremist
    /// phrases/codes nobody carries innocently. Substring-matched — keep entries long and distinctive.</summary>
    public static readonly IReadOnlyList<string> DefaultBlockedWords = new[]
    {
        "hitler", "nazi", "nigger", "neger", "fuck", "bitch", "hurensohn", "fotze", "wichser", "arschloch",
        "heilhitler", "siegheil", "1488", "nsdap", "hakenkreuz", "swastika", "kukluxklan",
    };

    /// <summary>The default watch list (flag, never block). "18"/"28" are documented right-wing codes
    /// but deliberately absent: too many kids put an age or birth day in their name, and a filter that
    /// pings the operator for every "Lisa18" erodes trust in the flags that matter.</summary>
    public static readonly IReadOnlyList<string> DefaultWatchWords = new[]
    {
        // Extremist code words and abbreviations — ambiguous ("88" = birth year, "HH" = Hamburg).
        "afd", "npd", "88", "hh", "kkk", "sieg", "heil", "adolf",
        "reichsbürger", "landser", "wehrmacht", "waffenss", "whitepower",
        // Serial-killer homages.
        "dahmer", "bundy", "manson", "breivik", "gacy", "kürten", "haarmann",
        // Authority impersonation ('=' pins to whole-token so "Supporter"/"Staffan" stay clean).
        "=admin", "administrator", "moderator", "gamemaster", "=support", "=staff", "=operator", "=system",
    };

    // Fuzzy backstop: high-severity names checked per token with edit distance ≤ 1, so one-letter
    // misspellings still flag. Watch, never block — distance 1 is proximity, not proof.
    private static readonly string[] FuzzyWatchCore =
    {
        "hitler", "himmler", "goebbels", "goering", "mengele", "eichmann", "adolf",
    };

    private readonly List<string> _blocked = new();
    private readonly List<(string Term, bool TokenOnly)> _watch = new();

    /// <summary>Builds a screen from the given lists; <c>null</c> uses the defaults. Entries are
    /// normalized once here, so operator extensions get the same separator/diacritic folding.</summary>
    public NameScreen(IEnumerable<string>? blockedWords = null, IEnumerable<string>? watchWords = null)
    {
        foreach (var word in blockedWords ?? DefaultBlockedWords)
        {
            string normalized = NormalizeBasic(word);
            if (normalized.Length > 0)
            {
                _blocked.Add(normalized);
            }
        }

        foreach (var word in watchWords ?? DefaultWatchWords)
        {
            string raw = (word ?? string.Empty).Trim();
            bool tokenOnly = raw.StartsWith("=", StringComparison.Ordinal);
            string normalized = NormalizeBasic(tokenOnly ? raw.Substring(1) : raw);
            if (normalized.Length > 0)
            {
                _watch.Add((normalized, tokenOnly || normalized.Length < 5));
            }
        }
    }

    public bool IsBlocked(string? name) => Screen(name).Verdict == NameVerdict.Block;

    /// <summary>Screens a name. Block beats Watch; the first matching entry wins.</summary>
    public NameScreenResult Screen(string? name)
    {
        string basic = NormalizeBasic(name);
        if (basic.Length == 0)
        {
            return new NameScreenResult(NameVerdict.Ok, string.Empty);
        }

        // The forms a block entry is searched in: plain, both leet foldings ('1' is used for both
        // 'i' and 'l' in the wild), and the repeated-letter collapse of each.
        var forms = new[]
        {
            basic,
            NormalizeLeet(name, oneAs: 'i'),
            NormalizeLeet(name, oneAs: 'l'),
        };

        foreach (var entry in _blocked)
        {
            string collapsedEntry = CollapseRuns(entry);
            foreach (var form in forms)
            {
                if (form.IndexOf(entry, StringComparison.Ordinal) >= 0
                    || CollapseRuns(form).IndexOf(collapsedEntry, StringComparison.Ordinal) >= 0)
                {
                    return new NameScreenResult(NameVerdict.Block, entry);
                }
            }
        }

        // Raw tokens catch digit codes ("Max88" → "88"); tokens of the leet-folded name catch leet
        // spellings that a letter↔digit boundary would otherwise split ("adm1n" → "adm","1","n", but
        // folded it is the single token "admin").
        string foldedName = FoldToLowerAscii(name);
        var tokens = new List<string>(TokenizeFolded(foldedName));
        tokens.AddRange(TokenizeFolded(FoldLeet(foldedName, 'i')));
        tokens.AddRange(TokenizeFolded(FoldLeet(foldedName, 'l')));

        foreach (var (term, tokenOnly) in _watch)
        {
            foreach (var token in tokens)
            {
                if (string.Equals(token, term, StringComparison.Ordinal))
                {
                    return new NameScreenResult(NameVerdict.Watch, term);
                }
            }

            if (!tokenOnly)
            {
                foreach (var form in forms)
                {
                    if (form.IndexOf(term, StringComparison.Ordinal) >= 0)
                    {
                        return new NameScreenResult(NameVerdict.Watch, term);
                    }
                }
            }
        }

        foreach (var token in tokens)
        {
            if (token.Length < 4)
            {
                continue;
            }

            foreach (var core in FuzzyWatchCore)
            {
                if (Math.Abs(token.Length - core.Length) <= 1 && WithinEditDistanceOne(token, core))
                {
                    return new NameScreenResult(NameVerdict.Watch, core);
                }
            }
        }

        return new NameScreenResult(NameVerdict.Ok, string.Empty);
    }

    /// <summary>Lowercases, folds diacritics (ï→i, ü→u, ß→ss) and keeps only ASCII letters/digits —
    /// every separator class disappears, so "h-i.t l_e*r" and "hitler" normalize identically.</summary>
    public static string NormalizeBasic(string? name) => Normalize(name, leetOneAs: null);

    /// <summary>Like <see cref="NormalizeBasic"/>, but additionally folds common leetspeak digits and
    /// symbols to letters (0→o, 3→e, 4→a, 5→s, 7→t, 8→b, @→a, $→s, !→i, €→e); '1' folds to
    /// <paramref name="oneAs"/> ('i' or 'l' — the wild uses both).</summary>
    public static string NormalizeLeet(string? name, char oneAs) => Normalize(name, oneAs);

    private static string Normalize(string? name, char? leetOneAs)
    {
        string folded = FoldToLowerAscii(name);
        var sb = new StringBuilder(folded.Length);
        foreach (char c in folded)
        {
            char mapped = leetOneAs is { } one ? MapLeet(c, one) : c;
            if (mapped is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(mapped);
            }
        }

        return sb.ToString();
    }

    private static string FoldLeet(string token, char oneAs)
    {
        var sb = new StringBuilder(token.Length);
        foreach (char c in token)
        {
            sb.Append(MapLeet(c, oneAs));
        }

        return sb.ToString();
    }

    private static char MapLeet(char c, char oneAs) => c switch
    {
        '0' => 'o',
        '1' => oneAs,
        '3' => 'e',
        '4' => 'a',
        '5' => 's',
        '7' => 't',
        '8' => 'b',
        '@' => 'a',
        '$' => 's',
        '!' => 'i',
        '€' => 'e',
        _ => c,
    };

    /// <summary>Lowercase + Unicode decomposition with combining marks stripped ("hïtler" → "hitler"),
    /// ß expanded to "ss". Non-Latin letters survive untouched (and are then dropped by the
    /// letter/digit filter — see the class-level homoglyph note).</summary>
    private static string FoldToLowerAscii(string? name)
    {
        string lower = (name ?? string.Empty).ToLowerInvariant().Replace("ß", "ss");
        string decomposed = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Collapses runs of the same character ("fuuck" → "fuck", "88" → "8") — used as a
    /// SECONDARY block-list pass with the entry collapsed the same way, never for watch codes.</summary>
    public static string CollapseRuns(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        char last = '\0';
        foreach (char c in value)
        {
            if (c != last)
            {
                sb.Append(c);
                last = c;
            }
        }

        return sb.ToString();
    }

    /// <summary>Splits a name into lowercase tokens at separators AND letter↔digit transitions:
    /// "xX_Max88" → ["xx", "max", "88"]. Watch-list codes match these tokens exactly, so "Tom1988"
    /// (one token "1988") never flags for "88".</summary>
    public static List<string> Tokenize(string? name) => TokenizeFolded(FoldToLowerAscii(name));

    private static List<string> TokenizeFolded(string folded)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool currentIsDigit = false;
        foreach (char c in folded)
        {
            bool isLetter = c is >= 'a' and <= 'z';
            bool isDigit = c is >= '0' and <= '9';
            if (!isLetter && !isDigit)
            {
                FlushToken(tokens, current);
                continue;
            }

            if (current.Length > 0 && isDigit != currentIsDigit)
            {
                FlushToken(tokens, current);
            }

            current.Append(c);
            currentIsDigit = isDigit;
        }

        FlushToken(tokens, current);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are at most one edit
    /// (substitution, insertion or deletion) apart. Lengths may differ by at most one.</summary>
    public static bool WithinEditDistanceOne(string a, string b)
    {
        if (a.Length > b.Length)
        {
            (a, b) = (b, a);
        }

        if (b.Length - a.Length > 1)
        {
            return false;
        }

        int i = 0;
        while (i < a.Length && a[i] == b[i])
        {
            i++;
        }

        if (i == a.Length)
        {
            return true; // equal, or b has one extra trailing char
        }

        if (a.Length == b.Length)
        {
            // One substitution: the rest after the mismatch must be identical.
            for (int j = i + 1; j < a.Length; j++)
            {
                if (a[j] != b[j])
                {
                    return false;
                }
            }

            return true;
        }

        // One insertion in b: skip the mismatching char of b, the rest must align.
        for (int j = i; j < a.Length; j++)
        {
            if (a[j] != b[j + 1])
            {
                return false;
            }
        }

        return true;
    }
}
