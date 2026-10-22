// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2591: how urgent an AI line request is. The per-world budget (<see cref="AiLineBudget"/>) keeps a few tokens back
/// for the more important kinds: a lower priority only gets a token while the bucket still holds its reserve
/// afterwards, so background chatter can never starve a line a player is waiting for.
/// </summary>
public enum AiLinePriority
{
    /// <summary>The player asked for it right now (E-greeting, a wish dialog, a dialog opening). Reserve 0.</summary>
    PlayerInitiated = 0,

    /// <summary>Lines for one player: hello, welcome back, friend gift, thanks. Reserve 1.</summary>
    Personal = 1,

    /// <summary>VEGA's lines (welcome aboard, base news, praise, and the /npc-line banter since #2618). Reserve 1.</summary>
    Vega = 2,

    /// <summary>News headlines (deeds). Reserve 2.</summary>
    News = 3,

    /// <summary>Villagers talking among themselves, festival cheers. Reserve 3.</summary>
    Chatter = 4,
}

/// <summary>
/// #2591: the placeholder tokens AI lines use for exact facts. The model writes them literally; the server fills
/// them in per RECEIVING player (<see cref="AiLineText.FillTokens(string, IReadOnlyDictionary{string, string})"/>),
/// so a cached line never holds a real name and one generated line serves every player of that language.
/// </summary>
public static class AiLineTokens
{
    /// <summary>The receiving player's name — always filled by the server, never cached as a real name.</summary>
    public const string Player = "{player}";

    /// <summary>The first speaker of a conversation.</summary>
    public const string A = "{a}";

    /// <summary>The second speaker of a conversation.</summary>
    public const string B = "{b}";

    /// <summary>The NPC who speaks (or is spoken about).</summary>
    public const string Npc = "{npc}";

    /// <summary>A place name (settlement, base, body).</summary>
    public const string Place = "{place}";

    /// <summary>A trader's name.</summary>
    public const string Trader = "{trader}";

    /// <summary>An item's localized name.</summary>
    public const string Item = "{item}";

    /// <summary>A count.</summary>
    public const string Count = "{count}";

    /// <summary>A thing (an achievement name, a creature …).</summary>
    public const string Thing = "{thing}";

    /// <summary>Positional argument 0 (mirrors an authored locale line's <c>{0}</c>).</summary>
    public const string Arg0 = "{0}";

    /// <summary>Positional argument 1 (mirrors an authored locale line's <c>{1}</c>).</summary>
    public const string Arg1 = "{1}";
}

/// <summary>
/// #2591: the pure text rules of the AI line path — validating an answer, filling tokens per receiver. No server
/// state, so every rule is unit-tested on its own (NpcAiLineTests).
/// </summary>
internal static class AiLineText
{
    /// <summary>A line may run this many characters past <see cref="NpcLinesRequest.MaxChars"/> untouched; a longer
    /// one is cut at a word boundary to MaxChars (with "…").</summary>
    public const int LengthSlack = 16;

    private const string Ellipsis = "…";

    /// <summary>Quote marks a model likes to wrap a spoken line in (any language) — trimmed from both ends.</summary>
    private static readonly char[] QuoteChars =
    {
        '"', '\'', '`', '“', '”', '„', '‚', '‘', '’', '«', '»', '‹', '›',
        '「', '」', '『', '』',
    };

    private static readonly char[] CutTrim = { ' ', ',', ';', ':', '-', '–', '—' };

    /// <summary>
    /// Checks one model answer against the request and returns the cleaned lines, or null when ANY rule fails (the
    /// caller then keeps its authored lines — never a half-good answer):
    /// <list type="bullet">
    /// <item>exactly <paramref name="lineCount"/> lines, none empty after trimming whitespace and wrapping quotes;</item>
    /// <item>every <c>{…}</c> token is in <paramref name="allowedTokens"/>; no stray or nested brace;</item>
    /// <item>no digit outside a token (numbers are facts — they only travel as tokens such as <c>{count}</c>);</item>
    /// <item>a line longer than <paramref name="maxChars"/> + <see cref="LengthSlack"/> is cut at a word boundary to
    /// <paramref name="maxChars"/> ("…"); a hard cut (no spaces, e.g. CJK) never splits a token;</item>
    /// <item>every token in <paramref name="requiredTokens"/> appears at least once in the whole set (after cutting) —
    /// for a one-line purpose that means "in the line".</item>
    /// </list>
    /// </summary>
    internal static string[]? Validate(IReadOnlyList<string>? raw, int lineCount, int maxChars,
        IReadOnlyCollection<string> allowedTokens, IReadOnlyCollection<string> requiredTokens)
    {
        if (raw is null || lineCount <= 0 || raw.Count != lineCount)
        {
            return null;
        }

        var lines = new string[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            string line = Normalize(raw[i]);
            if (line.Length == 0 || !TokensAllowed(line, allowedTokens) || HasDigitOutsideTokens(line))
            {
                return null;
            }

            string? clamped = ClampLine(line, maxChars);
            if (clamped is null)
            {
                return null;
            }

            lines[i] = clamped;
        }

        foreach (var token in requiredTokens)
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            bool found = false;
            foreach (var line in lines)
            {
                if (line.Contains(token, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return null;
            }
        }

        return lines;
    }

    /// <summary>One line as spoken: control characters and line breaks become spaces, runs of spaces collapse, and
    /// wrapping quote marks are trimmed.</summary>
    internal static string Normalize(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(line!.Length);
        bool space = false;
        foreach (char c in line)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
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

        return sb.ToString().Trim(QuoteChars).Trim();
    }

    /// <summary>True when every <c>{…}</c> in the line is one of <paramref name="allowedTokens"/> and no brace is
    /// stray or nested.</summary>
    internal static bool TokensAllowed(string line, IReadOnlyCollection<string> allowedTokens)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '}')
            {
                return false;
            }

            if (c != '{')
            {
                i++;
                continue;
            }

            int close = line.IndexOf('}', i + 1);
            if (close < 0)
            {
                return false;
            }

            string token = line.Substring(i, close - i + 1);
            if (token.IndexOf('{', 1) >= 0 || !Contains(allowedTokens, token))
            {
                return false;
            }

            i = close + 1;
        }

        return true;
    }

    /// <summary>True when a digit appears anywhere outside a <c>{…}</c> token.</summary>
    internal static bool HasDigitOutsideTokens(string line)
    {
        bool inToken = false;
        foreach (char c in line)
        {
            if (c == '{')
            {
                inToken = true;
            }
            else if (c == '}')
            {
                inToken = false;
            }
            else if (!inToken && char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Keeps a line within <paramref name="maxChars"/> + <see cref="LengthSlack"/>; a longer one is cut to
    /// <paramref name="maxChars"/> at a word boundary with "…". Null when nothing sensible is left.</summary>
    internal static string? ClampLine(string line, int maxChars)
    {
        if (maxChars <= 0 || line.Length <= maxChars + LengthSlack)
        {
            return line;
        }

        int limit = Math.Max(1, maxChars - Ellipsis.Length);
        int cut = line.LastIndexOf(' ', limit);
        if (cut < limit / 2)
        {
            // No usable word boundary (CJK has no spaces): a hard cut — but never inside a token or a surrogate pair.
            cut = limit;
            int open = line.LastIndexOf('{', cut - 1);
            if (open >= 0 && line.IndexOf('}', open) >= cut)
            {
                cut = open;
            }

            if (cut > 0 && char.IsHighSurrogate(line[cut - 1]))
            {
                cut--;
            }
        }

        string head = line.Substring(0, cut).TrimEnd(CutTrim);
        return head.Length == 0 ? null : head + Ellipsis;
    }

    /// <summary>
    /// Fills every <c>{…}</c> token of one line from <paramref name="values"/> — keyed by the token as written
    /// (<c>"{player}"</c>) or by its bare name (<c>"player"</c>). Returns null when a token has no value or a brace is
    /// stray (the leftover-brace check: the caller shows its authored line instead). Values are inserted verbatim and
    /// never scanned again, so a player called "{a}" cannot inject a token.
    /// </summary>
    internal static string? FillTokens(string line, IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder(line.Length + 24);
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '}')
            {
                return null;
            }

            if (c != '{')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int close = line.IndexOf('}', i + 1);
            if (close < 0)
            {
                return null;
            }

            string token = line.Substring(i, close - i + 1);
            if (token.IndexOf('{', 1) >= 0)
            {
                return null;
            }

            if (!values.TryGetValue(token, out var value)
                && !values.TryGetValue(token.Substring(1, token.Length - 2), out value))
            {
                return null;
            }

            sb.Append(value);
            i = close + 1;
        }

        return sb.ToString();
    }

    /// <summary>Fills a whole set of lines (see <see cref="FillTokens(string, IReadOnlyDictionary{string, string})"/>);
    /// null when any line fails.</summary>
    internal static string[]? FillTokens(IReadOnlyList<string> lines, IReadOnlyDictionary<string, string> values)
    {
        var filled = new string[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if (FillTokens(lines[i], values) is not { } line)
            {
                return null;
            }

            filled[i] = line;
        }

        return filled;
    }

    /// <summary>The distinct <c>{…}</c> tokens of some lines in first-seen order — e.g. the tokens of the authored
    /// lines, to pass as the required set so every fact of the written line survives the re-wording.</summary>
    internal static List<string> TokensIn(IEnumerable<string> lines)
    {
        var tokens = new List<string>();
        foreach (var line in lines)
        {
            int i = 0;
            while (line != null && (i = line.IndexOf('{', i)) >= 0)
            {
                int close = line.IndexOf('}', i + 1);
                if (close < 0)
                {
                    break;
                }

                string token = line.Substring(i, close - i + 1);
                if (token.IndexOf('{', 1) < 0 && !tokens.Contains(token))
                {
                    tokens.Add(token);
                }

                i = close + 1;
            }
        }

        return tokens;
    }

    private static bool Contains(IReadOnlyCollection<string> set, string token)
    {
        if (set is ISet<string> hashed)
        {
            return hashed.Contains(token);
        }

        foreach (var t in set)
        {
            if (string.Equals(t, token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
