// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Text;
using BlocksBeyondTheStars.Shared.Configuration;

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

/// <summary>
/// #2591: the per-world AI line budget — a token bucket (capacity 6, refilled at
/// <see cref="ServerConfig.AiLinesPerMinute"/>) plus a cap of 2 requests in flight. Each request costs one token; a
/// priority may only take one when its reserve stays in the bucket afterwards:
/// <code>
///   PlayerInitiated 0 · Personal 1 · Vega 1 · News 2 · Chatter 3
/// </code>
/// so with a full bucket of 6 chatter gets at most 3 lines before it waits for the refill, and the last token is
/// always kept for a player who is waiting. A refused request is simply not made (the authored line stays) — there
/// is no queue. A rate of 0 refuses everything. Thread-safe (requests finish on background threads).
/// </summary>
internal sealed class AiLineBudget
{
    public const double Capacity = 6.0;
    public const int MaxInFlight = 2;

    private readonly Lock _gate = new();
    private double _tokens = Capacity;
    private double _lastAt = double.NaN;
    private int _inFlight;

    /// <summary>Tokens that must stay in the bucket after a request of this priority takes one.</summary>
    public static int ReserveFor(AiLinePriority priority) => priority switch
    {
        AiLinePriority.PlayerInitiated => 0,
        AiLinePriority.Personal => 1,
        AiLinePriority.Vega => 1,
        AiLinePriority.News => 2,
        _ => 3,
    };

    /// <summary>Requests currently running.</summary>
    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    /// <summary>Takes one token + one in-flight slot when the priority's rule allows it. <paramref name="now"/> is
    /// the server uptime in seconds.</summary>
    public bool TryTake(AiLinePriority priority, double now, double perMinute)
    {
        lock (_gate)
        {
            Refill(now, perMinute);
            if (perMinute <= 0 || _inFlight >= MaxInFlight || _tokens + 1e-9 < 1.0 + ReserveFor(priority))
            {
                return false;
            }

            _tokens -= 1.0;
            _inFlight++;
            return true;
        }
    }

    /// <summary>Frees the in-flight slot of a finished request (the token stays spent).</summary>
    public void Release()
    {
        lock (_gate)
        {
            if (_inFlight > 0)
            {
                _inFlight--;
            }
        }
    }

    /// <summary>The bucket's level at <paramref name="now"/> (inspection/tests).</summary>
    public double TokensAt(double now, double perMinute)
    {
        lock (_gate)
        {
            Refill(now, perMinute);
            return _tokens;
        }
    }

    private void Refill(double now, double perMinute)
    {
        if (!double.IsNaN(_lastAt) && now > _lastAt && perMinute > 0)
        {
            _tokens = Math.Min(Capacity, _tokens + (now - _lastAt) * perMinute / 60.0);
        }

        if (double.IsNaN(_lastAt) || now > _lastAt)
        {
            _lastAt = now;
        }
    }
}

/// <summary>
/// #2590/#2591: a bounded, thread-safe cache of up to <see cref="MaxVariants"/> variants per key. Keys are evicted
/// first-in-first-out once more than <see cref="Capacity"/> are held, so the cache can never grow without bound on a
/// long-running world. Variants are picked at random so an NPC doesn't repeat one sentence forever.
/// </summary>
internal sealed class AiVariantCache<T>
    where T : class
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly Func<T, T, bool> _same;
    private readonly Random _rng;

    public AiVariantCache(int capacity, int maxVariants, Func<T, T, bool> same, int seed = 0x5EED)
    {
        Capacity = Math.Max(1, capacity);
        MaxVariants = Math.Max(1, maxVariants);
        _same = same;
        _rng = new Random(seed);
    }

    /// <summary>Most keys held at once.</summary>
    public int Capacity { get; }

    /// <summary>Most variants per key.</summary>
    public int MaxVariants { get; }

    /// <summary>Keys currently held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>How many variants a key holds (0 = none).</summary>
    public int VariantCount(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) ? e.Variants.Count : 0;
        }
    }

    /// <summary>A random variant of the key, or null.</summary>
    public T? Pick(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) && e.Variants.Count > 0 ? e.Variants[_rng.Next(e.Variants.Count)] : null;
        }
    }

    /// <summary>A deterministic variant of the key (<paramref name="seed"/> modulo the variant count), or null.</summary>
    public T? PickAt(string key, int seed)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) && e.Variants.Count > 0
                ? e.Variants[(seed & int.MaxValue) % e.Variants.Count]
                : null;
        }
    }

    /// <summary>All variants of the key (a copy).</summary>
    public IReadOnlyList<T> Variants(string key)
    {
        lock (_gate)
        {
            return _map.TryGetValue(key, out var e) ? e.Variants.ToArray() : Array.Empty<T>();
        }
    }

    /// <summary>Adds a variant. False when it equals one already held or the key is full. Evicts the oldest key when
    /// the cache is over capacity.</summary>
    public bool Add(string key, T value)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var e))
            {
                e = new Entry(_order.AddLast(key));
                _map[key] = e;
                while (_map.Count > Capacity && _order.First is { } oldest)
                {
                    _order.RemoveFirst();
                    _map.Remove(oldest.Value);
                }
            }

            if (e.Variants.Count >= MaxVariants)
            {
                return false;
            }

            foreach (var v in e.Variants)
            {
                if (_same(v, value))
                {
                    return false;
                }
            }

            e.Variants.Add(value);
            return true;
        }
    }

    /// <summary>Drops every key matching <paramref name="match"/>; returns how many went.</summary>
    public int RemoveWhere(Func<string, bool> match)
    {
        lock (_gate)
        {
            var gone = _map.Keys.Where(match).ToList();
            foreach (var key in gone)
            {
                _order.Remove(_map[key].Node);
                _map.Remove(key);
            }

            return gone.Count;
        }
    }

    private sealed class Entry
    {
        public Entry(LinkedListNode<string> node) => Node = node;

        public LinkedListNode<string> Node { get; }

        public List<T> Variants { get; } = new();
    }
}

/// <summary>
/// #2591 — ONE AI path for every new NPC/VEGA line (village-life package, epic #2589). Every caller follows the same
/// two-mode rule: the authored, localized line is always there and shown at once; with AI on (<see cref="AiLevel"/>
/// != Off, hosted worlds run <c>TextOnly</c>) a re-worded line from <c>POST /npc-lines</c> is used when it is ALREADY
/// cached — generation never blocks and never swaps a dialog or VEGA line after it appeared.
///
/// <para><b>How to use it (game thread):</b></para>
/// <code>
///   string key = AiLineCacheKey(NpcLinePurposes.PassingHello, job, PersonaIndexFor(personKey, npc.IsRobot, npc.Role),
///                                session.Locale, stage);          // never a player id or name in the key
///   string[]? ai = AiLinesFor(key, AiLinePriority.Personal, () => new NpcLinesRequest
///   {
///       Purpose = NpcLinePurposes.PassingHello, Language = session.Locale, Stage = stage, LineCount = 1, MaxChars = 90,
///       Speakers = { new NpcLinesSpeaker { Role = npc.Role, Job = npc.Job, IsRobot = npc.IsRobot,
///                                         Persona = PersonaFor(personKey, npc.Theme, npc.IsRobot, npc.Role) } },
///       Tokens = { AiLineTokens.Player }, Authored = { authoredWithTokens },
///   }, requiredTokens: new[] { AiLineTokens.Player });
///   string text = ai is { } lines &amp;&amp; FillAiLine(lines[0], session) is { } filled ? filled : authoredFilled;
/// </code>
/// <list type="bullet">
/// <item><see cref="AiLinesFor"/> = cached variant (or null) + a background request on a miss, and — while fewer than
/// three variants exist — a 30 % chance of one more (that top-up runs one priority step lower).</item>
/// <item><see cref="CachedAiLines(string)"/> / <see cref="RequestAiLines"/> are the two halves for callers that
/// prefetch (e.g. warm a dialog opening while the player walks past, a guest's greeting during its approach).</item>
/// <item>Answers are validated (<see cref="AiLineText.Validate"/>) and screened (<see cref="ScreenedAiTextProvider"/>)
/// before they are cached; any failure drops the whole answer.</item>
/// <item><see cref="FillAiLine"/> fills tokens per RECEIVER (<c>{player}</c> = their own name) and returns null on a
/// leftover brace — show the authored line then.</item>
/// <item>The budget (<see cref="AiLineBudget"/>) and in-flight dedupe live here; callers never reimplement them.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Most cache keys held for /npc-lines answers (FIFO beyond).</summary>
    private const int AiLinesCacheCapacity = 4096;

    /// <summary>Most variants per cache key (/npc-line greetings and /npc-lines alike).</summary>
    private const int AiLineMaxVariants = 3;

    /// <summary>Chance that a cache hit asks for one more variant while fewer than <see cref="AiLineMaxVariants"/> exist.</summary>
    private const double AiLineTopUpChance = 0.3;

    private readonly AiLineBudget _aiLineBudget = new();

    private readonly AiVariantCache<string[]> _aiLinesCache =
        new(AiLinesCacheCapacity, AiLineMaxVariants, (a, b) => a.SequenceEqual(b, StringComparer.Ordinal), seed: 0x41A1);

    /// <summary>Cache keys with a /npc-lines request running, so a burst of triggers asks only once.</summary>
    private readonly ConcurrentDictionary<string, byte> _aiLinesInFlight = new(StringComparer.Ordinal);

    /// <summary>Game-thread rolls (variant top-ups).</summary>
    private readonly Random _aiRng = new(0x7A11);

    private int _aiLinesRejected;
    private bool _aiRunInlineForTest;
    private double? _aiVariantRollForTest;

    /// <summary>AI text personalisation is on for this world (any <see cref="AiLevel"/> but Off — hosted worlds run
    /// <c>TextOnly</c>). Off is the default in singleplayer and the browser build: no request is ever made.</summary>
    private bool AiLinesEnabled => _config.AiLevel != AiLevel.Off;

    /// <summary>AI may propose CONTENT (not just wording), e.g. a wish's item from a whitelist — only at Suggest/Auto,
    /// like <c>AiMissionGate</c>. Text personalisation needs only <see cref="AiLinesEnabled"/>.</summary>
    private bool AiContentEnabled => _config.AiLevel is AiLevel.Suggest or AiLevel.Auto;

    /// <summary>The cache key shape every /npc-lines caller uses: <c>purpose|subject|p{persona}|locale|stage</c>.
    /// <paramref name="subject"/> is the topic / wish template / guest kind / achievement category …; it and every
    /// other part must never contain a player id or name — the whole point is that one line serves every player of
    /// the language. Variants (up to 3) live inside the entry, not in the key.</summary>
    private static string AiLineCacheKey(string purpose, string subject, int personaIndex, string locale, string stage)
        => $"{purpose}|{subject}|p{personaIndex}|{locale}|{stage}";

    /// <summary>A random cached variant for the key (validated, tokens UNFILLED), or null — also null while AI is off.</summary>
    private string[]? CachedAiLines(string cacheKey)
        => AiLinesEnabled ? _aiLinesCache.Pick(cacheKey) : null;

    /// <summary>A deterministic cached variant (<paramref name="variantSeed"/> modulo the variant count), or null —
    /// e.g. seeded with a greeting id so a re-send shows the same variant.</summary>
    private string[]? CachedAiLines(string cacheKey, int variantSeed)
        => AiLinesEnabled ? _aiLinesCache.PickAt(cacheKey, variantSeed) : null;

    /// <summary>How many validated variants the key holds (0–3).</summary>
    private int AiLineVariantCount(string cacheKey) => _aiLinesCache.VariantCount(cacheKey);

    /// <summary>
    /// Asks the backend for one more variant of <paramref name="cacheKey"/> in the background. Returns true when a
    /// request was started; false when AI is off, the key already holds 3 variants or has a request running, or the
    /// budget refused this priority. The answer is validated against <paramref name="request"/>'s LineCount/MaxChars,
    /// the <paramref name="allowedTokens"/> (default: <see cref="NpcLinesRequest.Tokens"/> plus every speaker token) and
    /// the <paramref name="requiredTokens"/> (each at least once in the set), screened, and cached; anything else is
    /// dropped silently. Call on the game thread; don't touch <paramref name="request"/> afterwards.
    /// </summary>
    private bool RequestAiLines(string cacheKey, NpcLinesRequest request, AiLinePriority priority,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
    {
        if (!AiLinesEnabled || string.IsNullOrEmpty(cacheKey) || request.LineCount <= 0
            || _aiLinesCache.VariantCount(cacheKey) >= AiLineMaxVariants)
        {
            return false;
        }

        if (!_aiLinesInFlight.TryAdd(cacheKey, 1))
        {
            return false;
        }

        if (!TryTakeAiBudget(priority))
        {
            _aiLinesInFlight.TryRemove(cacheKey, out _);
            return false;
        }

        var allowed = new HashSet<string>(allowedTokens ?? AllowedTokensOf(request), StringComparer.Ordinal);
        var required = (requiredTokens ?? Array.Empty<string>()).ToArray();
        int lineCount = request.LineCount;
        int maxChars = request.MaxChars;
        RunAiOffThread(() =>
        {
            try
            {
                var lines = AiLineText.Validate(_ai.GenerateNpcLines(request), lineCount, maxChars, allowed, required);
                if (lines is null)
                {
                    Interlocked.Increment(ref _aiLinesRejected);
                }
                else
                {
                    _aiLinesCache.Add(cacheKey, lines);
                }
            }
            catch
            {
                // best-effort flavour — the authored line is already on screen
            }
            finally
            {
                _aiLinesInFlight.TryRemove(cacheKey, out _);
                _aiLineBudget.Release();
            }
        });
        return true;
    }

    /// <summary>
    /// The one-call form most callers want: returns a random cached variant (tokens UNFILLED — use
    /// <see cref="FillAiLine"/>), or null on a miss / with AI off. A miss starts a background request at
    /// <paramref name="priority"/>; a hit with fewer than 3 variants asks for one more with a 30 % chance, one
    /// priority step lower. <paramref name="buildRequest"/> only runs when a request is actually made.
    /// </summary>
    private string[]? AiLinesFor(string cacheKey, AiLinePriority priority, Func<NpcLinesRequest> buildRequest,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
    {
        if (!AiLinesEnabled)
        {
            return null;
        }

        var cached = _aiLinesCache.Pick(cacheKey);
        bool ask = cached is null
            || (_aiLinesCache.VariantCount(cacheKey) < AiLineMaxVariants && AiTopUpRoll());
        if (ask && !_aiLinesInFlight.ContainsKey(cacheKey))
        {
            var p = cached is null ? priority : (AiLinePriority)Math.Min((int)priority + 1, (int)AiLinePriority.Chatter);
            RequestAiLines(cacheKey, buildRequest(), p, requiredTokens, allowedTokens);
        }

        return cached;
    }

    /// <summary>Fills one cached line for ONE receiver: <c>{player}</c> is always their own name (unless
    /// <paramref name="values"/> sets it), every other token comes from <paramref name="values"/>. Null on a missing
    /// value or a leftover brace — show the authored line then.</summary>
    private static string? FillAiLine(string line, PlayerSession receiver, IReadOnlyDictionary<string, string>? values = null)
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is not null)
        {
            foreach (var kv in values)
            {
                all[kv.Key] = kv.Value;
            }
        }

        if (!all.ContainsKey(AiLineTokens.Player) && !all.ContainsKey("player"))
        {
            all[AiLineTokens.Player] = receiver.State.Name ?? string.Empty;
        }

        return AiLineText.FillTokens(line, all);
    }

    /// <summary>Drops cached answers whose key starts with <paramref name="cacheKeyPrefix"/> (e.g. a fulfilled wish's
    /// lines). Returns how many keys went.</summary>
    private int ForgetAiLines(string cacheKeyPrefix)
        => _aiLinesCache.RemoveWhere(k => k.StartsWith(cacheKeyPrefix, StringComparison.Ordinal));

    /// <summary>The tokens a request's answer may use: its <see cref="NpcLinesRequest.Tokens"/> plus every speaker's token.</summary>
    private static List<string> AllowedTokensOf(NpcLinesRequest request)
    {
        var tokens = new List<string>();
        foreach (var t in request.Tokens)
        {
            if (!string.IsNullOrWhiteSpace(t))
            {
                tokens.Add(t.Trim());
            }
        }

        foreach (var s in request.Speakers)
        {
            if (!string.IsNullOrWhiteSpace(s.Token))
            {
                tokens.Add(s.Token.Trim());
            }
        }

        return tokens;
    }

    /// <summary>Takes one budget token for <paramref name="priority"/> (and an in-flight slot) — shared by /npc-lines
    /// and the classic /npc-line greetings, so the world has ONE budget.</summary>
    private bool TryTakeAiBudget(AiLinePriority priority)
        => _aiLineBudget.TryTake(priority, _uptime, _config.AiLinesPerMinute);

    /// <summary>The 30 % "ask for another variant" roll (game thread).</summary>
    private bool AiTopUpRoll() => (_aiVariantRollForTest ?? _aiRng.NextDouble()) < AiLineTopUpChance;

    /// <summary>Runs AI work off the game thread (inline under <see cref="RunAiLinesSynchronouslyForTest"/>). Only ever
    /// reached with AI on, which the browser build never is — no thread is started there.</summary>
    private void RunAiOffThread(Action work)
    {
        if (_aiRunInlineForTest)
        {
            work();
            return;
        }

        _ = Task.Run(work);
    }

    // ---------------- Test seams (other agents' tests use these too) ----------------

    /// <summary>Test seam: swaps the AI provider (still wrapped in the world's content screen, exactly like the
    /// constructor does) — lets fixtures without a provider parameter (e.g. NpcLifeWorld) inject a fake. Combine with
    /// a config whose <see cref="ServerConfig.AiLevel"/> is not Off.</summary>
    public void SetAiProviderForTest(IAiMissionProvider provider) => _ai = new ScreenedAiTextProvider(provider, ScreenAiText);

    /// <summary>Test seam: run every AI request (greetings, VEGA banter, /npc-lines) inline instead of on the thread pool,
    /// so a test sees the cached result right after the triggering call.</summary>
    public void RunAiLinesSynchronouslyForTest(bool inline = true) => _aiRunInlineForTest = inline;

    /// <summary>Test seam: forces the variant top-up roll (0 = always ask for another variant, 1 = never); null = random.</summary>
    public void SetAiVariantRollForTest(double? roll) => _aiVariantRollForTest = roll;

    /// <summary>Test seam: <see cref="RequestAiLines"/> from a test.</summary>
    public bool RequestAiLinesForTest(string cacheKey, NpcLinesRequest request, AiLinePriority priority,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
        => RequestAiLines(cacheKey, request, priority, requiredTokens, allowedTokens);

    /// <summary>Test seam: <see cref="AiLinesFor"/> from a test.</summary>
    public string[]? AiLinesForTest(string cacheKey, AiLinePriority priority, NpcLinesRequest request,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
        => AiLinesFor(cacheKey, priority, () => request, requiredTokens, allowedTokens);

    /// <summary>Test seam: the validated variants cached for a /npc-lines key (tokens unfilled).</summary>
    public IReadOnlyList<string[]> AiLinesCacheForTest(string cacheKey) => _aiLinesCache.Variants(cacheKey);

    /// <summary>Test seam: how many /npc-lines keys are cached.</summary>
    public int AiLinesCacheCountForTest => _aiLinesCache.Count;

    /// <summary>Test seam: answers dropped by validation so far (wrong count, tokens, digits …).</summary>
    public int AiLinesRejectedForTest => Volatile.Read(ref _aiLinesRejected);

    /// <summary>Test seam: the budget bucket's level now.</summary>
    public double AiLineTokensForTest => _aiLineBudget.TokensAt(_uptime, _config.AiLinesPerMinute);

    /// <summary>Test seam: AI requests running right now (greetings + /npc-lines).</summary>
    public int AiLinesInFlightForTest => _aiLineBudget.InFlight;

    /// <summary>Test seam: <see cref="FillAiLine"/> for a joined player.</summary>
    public string? FillAiLineForTest(string playerId, string line, IReadOnlyDictionary<string, string>? values = null)
        => FindSessionByPlayerId(playerId) is { } session ? FillAiLine(line, session, values) : null;
}
