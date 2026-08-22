// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Shared.Moderation;

/// <summary>Operator-level chat filter switch (<c>ServerConfig.ChatFilter</c> / <c>BBS_CHAT_FILTER</c>). It
/// caps or overrides the world rule <c>GameRules.ChatMode</c>: <see cref="Off"/> disables screening for every
/// world on this server (private family LAN), <see cref="Strict"/> forces the Safe mode everywhere (public
/// kids' fleet), <see cref="Mask"/> lets the world rule decide (the default).</summary>
public enum ChatFilterLevel
{
    Off,
    Mask,
    Strict,
}

/// <summary>Outcome of screening one chat line.</summary>
public enum ChatVerdict
{
    /// <summary>Relay the line as typed.</summary>
    Ok,

    /// <summary>Relay the line with the matched words replaced by asterisks (<see cref="ChatScreenResult.Text"/>).</summary>
    Mask,

    /// <summary>Do not relay the line; tell the sender.</summary>
    Block,
}

/// <summary>Result of <see cref="ChatScreen.Screen"/>. <see cref="MatchedTerm"/> is the normalized list entry
/// (or the PII kind) that decided the verdict — for the operator's log/notification, never shown to players.</summary>
public readonly struct ChatScreenResult
{
    public ChatScreenResult(ChatVerdict verdict, string text, string matchedTerm, bool watch, bool pii)
    {
        Verdict = verdict;
        Text = text;
        MatchedTerm = matchedTerm;
        Watch = watch;
        Pii = pii;
    }

    public ChatVerdict Verdict { get; }

    /// <summary>The line to relay: the original text for <see cref="ChatVerdict.Ok"/>/<see cref="ChatVerdict.Block"/>,
    /// the masked text for <see cref="ChatVerdict.Mask"/>.</summary>
    public string Text { get; }

    public string MatchedTerm { get; }

    /// <summary>A watch-list term (or a mixed-script line) was seen: the line is relayed per the verdict, but the
    /// operator should be told. Orthogonal to the verdict — a masked line can also be a watch hit.</summary>
    public bool Watch { get; }

    /// <summary>The verdict was (also) driven by personal data (phone number, e-mail, link).</summary>
    public bool Pii { get; }
}

/// <summary>
/// Chat content screening for the kid-facing servers (#1207). Sibling of <see cref="NameScreen"/> and built on its
/// folding helpers, but with <b>different match semantics</b>: a name is 24 characters and can be substring-matched,
/// a sentence cannot — "Assistent", "Klasse" and "Dickicht" must pass. So chat matches <b>whole tokens</b> (after
/// case/diacritic/leet/repeat folding and a Cyrillic/Greek confusable fold), joins runs of spaced-out single letters
/// ("f u c k"), and uses substring matching only for a tiny hard core of long hate terms ("h.i.t.l.e.r").
/// <list type="bullet">
/// <item><b>Block list</b> — slurs and hate terms: the line is not relayed, the sender is told.</item>
/// <item><b>Mask list</b> — plain profanity: the word is replaced by asterisks, the line is relayed.</item>
/// <item><b>Watch list</b> — ambiguous extremist codes: relayed, operator notified (never silent, never blocking).</item>
/// <item><b>Allow list</b> — tokens that never match anything (operator escape hatch for false positives).</item>
/// <item><b>PII</b> — phone numbers, e-mail addresses and links are masked in <see cref="Shared.Configuration.ChatMode.Filtered"/>
/// and block the line in <see cref="Shared.Configuration.ChatMode.Safe"/>.</item>
/// </list>
/// The filter is never silent: a blocked or masked line always produces a notice to the sender (the server does
/// that), and nothing about the line content is logged beyond the matched list entry.
/// </summary>
public sealed class ChatScreen
{
    /// <summary>Hate terms and slurs — the line is dropped. Entries of five or more characters are additionally
    /// substring-matched on the separator-collapsed line (catches "h.i.t.l.e.r"); shorter ones match tokens only,
    /// so a number code never hides inside a longer number.</summary>
    public static readonly IReadOnlyList<string> DefaultBlockedWords = new[]
    {
        "hitler", "heilhitler", "siegheil", "nazi", "nsdap", "hakenkreuz", "swastika", "kukluxklan", "1488",
        "nigger", "neger", "kanake", "schwuchtel", "faggot", "hurensohn", "fotze",
    };

    /// <summary>Plain profanity — masked, relayed. Whole-token semantics, so keep entries as the word itself
    /// (the folding catches "fuuuck", "f-u-c-k", "sh!t", "a$$hole", "fvck" is NOT caught — accepted).</summary>
    public static readonly IReadOnlyList<string> DefaultMaskedWords = new[]
    {
        "fuck", "fucking", "fucker", "fucked", "motherfucker", "shit", "bullshit", "bitch", "bitches", "asshole",
        "bastard", "cunt", "pussy", "whore", "slut", "dumbass", "wtf", "stfu",
        "arschloch", "arsch", "wichser", "wixer", "scheisse", "scheiss", "fick", "ficken", "fickt", "gefickt",
        "schlampe", "hure", "huren", "penner", "missgeburt", "verpiss", "verpisst", "spast", "spasti",
    };

    /// <summary>Ambiguous extremist codes — relayed, operator notified. Token-only; deliberately tiny, because a
    /// chat line is full of innocent numbers and words ("88 blocks", "Sieg!" after a race).</summary>
    public static readonly IReadOnlyList<string> DefaultWatchWords = new[]
    {
        "kkk", "whitepower", "waffenss", "reichsbuerger", "landser", "adolf",
    };

    private const int SpacedRunMinimum = 3;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(50);

    // Personal data that has no place in a kids' chat. Each pattern is compiled once and bounded by a timeout.
    // Phone: ten or more digits with optional separators in between ("0151 234 56 78", "+49-30-1234567"). Ten, not
    // seven, so block coordinates ("1234 64 -567") and version strings never read as a phone number.
    private static readonly Regex PhonePattern = new(@"(?<!\d)(?:\+?\d[\s\-.()/]*){9,}\d",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex EmailPattern = new(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    // Links: protocol or www prefixes, discord invites, and bare host names with a common TLD.
    private static readonly Regex LinkPattern = new(
        @"(?:https?://\S+|www\.\S+|discord\.gg/\S+|\b[\w\-]+(?:\.[\w\-]+)*\.(?:com|net|org|de|io|gg|tv|me|ly|app|xyz|info|eu|ch|at)\b(?:/\S*)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);

    private readonly HashSet<string> _blocked = new(StringComparer.Ordinal);
    private readonly List<string> _blockedLong = new();
    private readonly HashSet<string> _masked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _watch = new(StringComparer.Ordinal);
    private readonly HashSet<string> _allow = new(StringComparer.Ordinal);

    /// <summary>Builds a screen from the given lists; <c>null</c> uses the defaults. Entries are normalized once
    /// (case, diacritics, separators), so operator extensions get the same folding as the built-ins.</summary>
    public ChatScreen(
        IEnumerable<string>? blockedWords = null,
        IEnumerable<string>? maskedWords = null,
        IEnumerable<string>? watchWords = null,
        IEnumerable<string>? allowWords = null)
    {
        foreach (var word in blockedWords ?? DefaultBlockedWords)
        {
            string normalized = NameScreen.NormalizeBasic(word);
            if (normalized.Length == 0)
            {
                continue;
            }

            _blocked.Add(normalized);
            if (normalized.Length >= 5)
            {
                _blockedLong.Add(NameScreen.CollapseRuns(normalized));
            }
        }

        AddAll(_masked, maskedWords ?? DefaultMaskedWords);
        AddAll(_watch, watchWords ?? DefaultWatchWords);
        AddAll(_allow, allowWords ?? Array.Empty<string>());
    }

    private static void AddAll(HashSet<string> target, IEnumerable<string> words)
    {
        foreach (var word in words)
        {
            string normalized = NameScreen.NormalizeBasic(word);
            if (normalized.Length > 0)
            {
                target.Add(normalized);
            }
        }
    }

    /// <summary>Screens one chat line under the given world chat mode. <see cref="Shared.Configuration.ChatMode.Open"/>
    /// returns the line untouched.</summary>
    public ChatScreenResult Screen(string? text, Configuration.ChatMode mode)
    {
        string line = text ?? string.Empty;
        if (line.Length == 0 || mode == Configuration.ChatMode.Open)
        {
            return new ChatScreenResult(ChatVerdict.Ok, line, string.Empty, watch: false, pii: false);
        }

        var tokens = TokenizeWithSpans(line);
        var maskSpans = new List<(int Start, int End)>();
        bool watch = HasMixedScript(line);
        string watchTerm = string.Empty;

        // 1) Whole tokens.
        foreach (var token in tokens)
        {
            var forms = FormsOf(token.Text);
            if (IsAllowed(forms))
            {
                continue;
            }

            if (FirstMatch(forms, _blocked) is { } blockedTerm)
            {
                return new ChatScreenResult(ChatVerdict.Block, line, blockedTerm, watch, pii: false);
            }

            if (FirstMatch(forms, _masked) is not null)
            {
                maskSpans.Add((token.Start, token.End));
            }

            if (!watch && FirstMatch(forms, _watch) is { } watchHit)
            {
                watch = true;
                watchTerm = watchHit;
            }
        }

        // 2) Spaced-out single letters: "f u c k" / "n a z i" — join every run of three or more one-character
        //    tokens and screen the joined word (masked as one span from the first to the last letter).
        for (int i = 0; i < tokens.Count;)
        {
            int j = i;
            while (j < tokens.Count && tokens[j].Text.Length == 1)
            {
                j++;
            }

            if (j - i >= SpacedRunMinimum)
            {
                var joined = new StringBuilder();
                for (int k = i; k < j; k++)
                {
                    joined.Append(tokens[k].Text);
                }

                var forms = FormsOf(joined.ToString());
                if (!IsAllowed(forms))
                {
                    if (FirstMatch(forms, _blocked) is { } blockedTerm)
                    {
                        return new ChatScreenResult(ChatVerdict.Block, line, blockedTerm, watch, pii: false);
                    }

                    if (FirstMatch(forms, _masked) is not null)
                    {
                        maskSpans.Add((tokens[i].Start, tokens[j - 1].End));
                    }
                }
            }

            i = Math.Max(j, i + 1);
        }

        // 3) Hard core as substrings of the separator-collapsed whole line ("h.i.t.l.e.r", "na-zi"). Only the long
        //    entries take part, so this pass cannot fire on a number inside a longer number or on a short word
        //    inside a compound.
        if (_blockedLong.Count > 0)
        {
            string confusableFolded = FoldConfusables(line);
            var lineForms = new[]
            {
                NameScreen.CollapseRuns(NameScreen.NormalizeBasic(confusableFolded)),
                NameScreen.CollapseRuns(NameScreen.NormalizeLeet(confusableFolded, oneAs: 'i')),
                NameScreen.CollapseRuns(NameScreen.NormalizeLeet(confusableFolded, oneAs: 'l')),
            };
            foreach (var entry in _blockedLong)
            {
                foreach (var form in lineForms)
                {
                    if (form.IndexOf(entry, StringComparison.Ordinal) >= 0)
                    {
                        return new ChatScreenResult(ChatVerdict.Block, line, entry, watch, pii: false);
                    }
                }
            }
        }

        // 4) Personal data: masked in Filtered, a hard stop in Safe.
        bool pii = false;
        string piiKind = string.Empty;
        foreach (var (pattern, kind) in new[] { (PhonePattern, "phone"), (EmailPattern, "email"), (LinkPattern, "link") })
        {
            MatchCollection matches;
            try
            {
                matches = pattern.Matches(line);
            }
            catch (RegexMatchTimeoutException)
            {
                continue; // a pathological line is not worth stalling the tick; the word lists still applied
            }

            foreach (Match m in matches)
            {
                if (m.Length == 0)
                {
                    continue;
                }

                pii = true;
                if (piiKind.Length == 0)
                {
                    piiKind = kind;
                }

                maskSpans.Add((m.Index, m.Index + m.Length));
            }
        }

        if (pii && mode == Configuration.ChatMode.Safe)
        {
            return new ChatScreenResult(ChatVerdict.Block, line, piiKind, watch, pii: true);
        }

        if (maskSpans.Count == 0)
        {
            return new ChatScreenResult(ChatVerdict.Ok, line, watchTerm, watch, pii: false);
        }

        return new ChatScreenResult(ChatVerdict.Mask, ApplyMask(line, maskSpans), pii ? piiKind : watchTerm, watch, pii);
    }

    private bool IsAllowed(IReadOnlyList<string> forms)
    {
        if (_allow.Count == 0)
        {
            return false;
        }

        foreach (var form in forms)
        {
            if (_allow.Contains(form))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FirstMatch(IReadOnlyList<string> forms, HashSet<string> list)
    {
        if (list.Count == 0)
        {
            return null;
        }

        foreach (var form in forms)
        {
            if (form.Length > 0 && list.Contains(form))
            {
                return form;
            }
        }

        return null;
    }

    /// <summary>The folded forms one token is compared in: basic, both leet foldings, and the repeat-collapsed
    /// variant of each — all after the Cyrillic/Greek confusable fold.</summary>
    private static IReadOnlyList<string> FormsOf(string token)
    {
        string folded = FoldConfusables(token);
        string basic = NameScreen.NormalizeBasic(folded);
        string leetI = NameScreen.NormalizeLeet(folded, oneAs: 'i');
        string leetL = NameScreen.NormalizeLeet(folded, oneAs: 'l');
        return new[]
        {
            basic, leetI, leetL,
            NameScreen.CollapseRuns(basic), NameScreen.CollapseRuns(leetI), NameScreen.CollapseRuns(leetL),
        };
    }

    private readonly record struct Token(string Text, int Start, int End);

    /// <summary>Splits the ORIGINAL line into tokens with their character spans, so a masked word can be
    /// replaced in place. A token is a run of letters/digits plus the leet symbols that stand in for letters
    /// ('@', '$', '!', '€'); everything else separates.</summary>
    private static List<Token> TokenizeWithSpans(string line)
    {
        var tokens = new List<Token>();
        int start = -1;
        for (int i = 0; i <= line.Length; i++)
        {
            bool isTokenChar = i < line.Length && IsTokenChar(line[i]);
            if (isTokenChar)
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                tokens.Add(new Token(line.Substring(start, i - start), start, i));
                start = -1;
            }
        }

        return tokens;
    }

    private static bool IsTokenChar(char c)
        => char.IsLetterOrDigit(c) || c is '@' or '$' or '!' or '€';

    private static string ApplyMask(string line, List<(int Start, int End)> spans)
    {
        var chars = line.ToCharArray();
        foreach (var (start, end) in spans)
        {
            for (int i = Math.Max(0, start); i < Math.Min(chars.Length, end); i++)
            {
                chars[i] = '*';
            }
        }

        return new string(chars);
    }

    /// <summary>Cyrillic and Greek letters that look like Latin ones, mapped to their Latin twin so a
    /// "nаzi" with a Cyrillic 'а' folds to the same token as the plain word. Names keep their non-Latin
    /// letters (the name screen drops them); chat is where the trick is actually played.</summary>
    public static string FoldConfusables(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                'а' => 'a',
                'е' => 'e',
                'о' => 'o',
                'р' => 'p',
                'с' => 'c',
                'у' => 'y',
                'х' => 'x',
                'і' => 'i',
                'ј' => 'j',
                'ѕ' => 's',
                'һ' => 'h',
                'к' => 'k',
                'м' => 'm',
                'т' => 't',
                'в' => 'b',
                'н' => 'h',
                'А' => 'A',
                'Е' => 'E',
                'О' => 'O',
                'Р' => 'P',
                'С' => 'C',
                'У' => 'Y',
                'Х' => 'X',
                'І' => 'I',
                'К' => 'K',
                'М' => 'M',
                'Т' => 'T',
                'В' => 'B',
                'Н' => 'H',
                'α' => 'a',
                'ο' => 'o',
                'ε' => 'e',
                'ι' => 'i',
                'κ' => 'k',
                'ν' => 'v',
                'τ' => 't',
                'ρ' => 'p',
                'Α' => 'A',
                'Ο' => 'O',
                'Ε' => 'E',
                'Ι' => 'I',
                'Κ' => 'K',
                'Τ' => 'T',
                'Ρ' => 'P',
                _ => c,
            });
        }

        return sb.ToString();
    }

    /// <summary>True when the line mixes Latin letters with Cyrillic or Greek ones — the classic homoglyph
    /// evasion. A purely Cyrillic line (a Russian-speaking player) is NOT flagged.</summary>
    public static bool HasMixedScript(string text)
    {
        bool latin = false, other = false;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (c < 0x250)
            {
                latin = true;
            }
            else if ((c >= 0x370 && c <= 0x3FF) || (c >= 0x400 && c <= 0x4FF))
            {
                other = true;
            }

            if (latin && other)
            {
                return true;
            }
        }

        return false;
    }
}
