// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What <see cref="AiLinesSystem"/> asks of the server beyond <see cref="IServerHost"/> (ADR 0019): the world's AI text
/// provider. Implemented explicitly by <c>GameServer</c> in <c>GameServerAiLines.cs</c>, delegating to the server's own
/// field.
/// </summary>
internal interface IAiLinesHost : IServerHost
{
    /// <summary>The world's screened AI provider (the server's <c>_ai</c>). A test swaps it after construction, so it is
    /// read at use time — inside the background work, never cached.</summary>
    IAiMissionProvider Ai { get; }
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
internal sealed class AiLinesSystem
{
    /// <summary>Most cache keys held for /npc-lines answers (FIFO beyond).</summary>
    private const int AiLinesCacheCapacity = 4096;

    /// <summary>Most variants per cache key (/npc-line greetings and /npc-lines alike).</summary>
    internal const int AiLineMaxVariants = 3;

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

    /// <summary>Set by <see cref="GameServer.RunAiLinesSynchronouslyForTest"/>: run AI work inline.</summary>
    internal bool AiRunInlineForTest { get; set; }

    /// <summary>Set by <see cref="GameServer.SetAiVariantRollForTest"/>: the forced top-up roll; null = random.</summary>
    internal double? AiVariantRollForTest { get; set; }

    private readonly IAiLinesHost _host;
    private readonly ServerConfig _config;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal AiLinesSystem(IAiLinesHost host, ServerConfig config)
    {
        _host = host;
        _config = config;
    }

    /// <summary>AI text personalisation is on for this world (any <see cref="AiLevel"/> but Off — hosted worlds run
    /// <c>TextOnly</c>). Off is the default in singleplayer and the browser build: no request is ever made.</summary>
    internal bool AiLinesEnabled => _config.AiLevel != AiLevel.Off;

    /// <summary>AI may propose CONTENT (not just wording), e.g. a wish's item from a whitelist — only at Suggest/Auto,
    /// like <c>AiMissionGate</c>. Text personalisation needs only <see cref="AiLinesEnabled"/>.</summary>
    private bool AiContentEnabled => _config.AiLevel is AiLevel.Suggest or AiLevel.Auto;

    /// <summary>The cache key shape every /npc-lines caller uses: <c>purpose|subject|p{persona}|locale|stage</c>.
    /// <paramref name="subject"/> is the topic / wish template / guest kind / achievement category …; it and every
    /// other part must never contain a player id or name — the whole point is that one line serves every player of
    /// the language. Variants (up to 3) live inside the entry, not in the key.</summary>
    internal static string AiLineCacheKey(string purpose, string subject, int personaIndex, string locale, string stage)
        => $"{purpose}|{subject}|p{personaIndex}|{locale}|{stage}";

    /// <summary>A random cached variant for the key (validated, tokens UNFILLED), or null — also null while AI is off.</summary>
    internal string[]? CachedAiLines(string cacheKey)
        => AiLinesEnabled ? _aiLinesCache.Pick(cacheKey) : null;

    /// <summary>A deterministic cached variant (<paramref name="variantSeed"/> modulo the variant count), or null —
    /// e.g. seeded with a greeting id so a re-send shows the same variant.</summary>
    internal string[]? CachedAiLines(string cacheKey, int variantSeed)
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
    internal bool RequestAiLines(string cacheKey, NpcLinesRequest request, AiLinePriority priority,
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
                var lines = AiLineText.Validate(_host.Ai.GenerateNpcLines(request), lineCount, maxChars, allowed, required);
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
    internal string[]? AiLinesFor(string cacheKey, AiLinePriority priority, Func<NpcLinesRequest> buildRequest,
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
    internal static string? FillAiLine(string line, PlayerSession receiver, IReadOnlyDictionary<string, string>? values = null)
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
    internal bool TryTakeAiBudget(AiLinePriority priority)
        => _aiLineBudget.TryTake(priority, _host.Uptime, _config.AiLinesPerMinute);

    /// <summary>Frees the in-flight slot a <see cref="TryTakeAiBudget"/> took (the token stays spent) — the classic
    /// /npc-line greetings and VEGA banter call it when their request ends.</summary>
    internal void ReleaseAiBudget() => _aiLineBudget.Release();

    /// <summary>The 30 % "ask for another variant" roll (game thread).</summary>
    internal bool AiTopUpRoll() => (AiVariantRollForTest ?? _aiRng.NextDouble()) < AiLineTopUpChance;

    /// <summary>Runs AI work off the game thread (inline under
    /// <see cref="GameServer.RunAiLinesSynchronouslyForTest"/>). Only ever reached with AI on, which the browser build
    /// never is — no thread is started there.</summary>
    internal void RunAiOffThread(Action work)
    {
        if (AiRunInlineForTest)
        {
            work();
            return;
        }

        _ = Task.Run(work);
    }

    // ---------------- Test seams (the public *ForTest seams on GameServer delegate here) ----------------

    /// <summary>Test seam: the validated variants cached for a /npc-lines key (tokens unfilled).</summary>
    internal IReadOnlyList<string[]> AiLinesCacheForTest(string cacheKey) => _aiLinesCache.Variants(cacheKey);

    /// <summary>Test seam: how many /npc-lines keys are cached.</summary>
    internal int AiLinesCacheCountForTest => _aiLinesCache.Count;

    /// <summary>Test seam: answers dropped by validation so far (wrong count, tokens, digits …).</summary>
    internal int AiLinesRejectedForTest => Volatile.Read(ref _aiLinesRejected);

    /// <summary>Test seam: the budget bucket's level now.</summary>
    internal double AiLineTokensForTest => _aiLineBudget.TokensAt(_host.Uptime, _config.AiLinesPerMinute);

    /// <summary>Test seam: AI requests running right now (greetings + /npc-lines).</summary>
    internal int AiLinesInFlightForTest => _aiLineBudget.InFlight;
}
