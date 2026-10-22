// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The one AI path for every new NPC/VEGA line (#2591) on the server: the facade of <see cref="AiLinesSystem"/>
/// (ADR 0019), which owns the /npc-lines budget, the variant cache, the in-flight dedupe, the top-up roll and the
/// request/validate/cache path. This partial keeps the members sibling partials and the tests call — same names, same
/// signatures, each one line delegating — and the explicit <see cref="IAiLinesHost"/> implementation. The text rules
/// and the budget/cache primitives are in <c>AiLineText.cs</c>, <c>AiLineBudget.cs</c> and <c>AiVariantCache.cs</c>.
/// </summary>
public sealed partial class GameServer : IAiLinesHost
{
    /// <summary>Most variants per cache key (/npc-line greetings and /npc-lines alike). A constant, not a forward: a
    /// field initializer in <c>GameServerNpcGreeting.cs</c> reads it.</summary>
    private const int AiLineMaxVariants = AiLinesSystem.AiLineMaxVariants;

    private bool AiLinesEnabled => _aiLinesSystem.AiLinesEnabled;

    private static string AiLineCacheKey(string purpose, string subject, int personaIndex, string locale, string stage)
        => AiLinesSystem.AiLineCacheKey(purpose, subject, personaIndex, locale, stage);

    private string[]? CachedAiLines(string cacheKey) => _aiLinesSystem.CachedAiLines(cacheKey);

    private string[]? CachedAiLines(string cacheKey, int variantSeed) => _aiLinesSystem.CachedAiLines(cacheKey, variantSeed);

    private bool RequestAiLines(string cacheKey, NpcLinesRequest request, AiLinePriority priority,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
        => _aiLinesSystem.RequestAiLines(cacheKey, request, priority, requiredTokens, allowedTokens);

    private string[]? AiLinesFor(string cacheKey, AiLinePriority priority, Func<NpcLinesRequest> buildRequest,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
        => _aiLinesSystem.AiLinesFor(cacheKey, priority, buildRequest, requiredTokens, allowedTokens);

    private static string? FillAiLine(string line, PlayerSession receiver, IReadOnlyDictionary<string, string>? values = null)
        => AiLinesSystem.FillAiLine(line, receiver, values);

    private bool TryTakeAiBudget(AiLinePriority priority) => _aiLinesSystem.TryTakeAiBudget(priority);

    private void ReleaseAiBudget() => _aiLinesSystem.ReleaseAiBudget();

    private bool AiTopUpRoll() => _aiLinesSystem.AiTopUpRoll();

    private void RunAiOffThread(Action work) => _aiLinesSystem.RunAiOffThread(work);

    // ---------------- Test seams (other agents' tests use these too) ----------------

    /// <summary>Test seam: swaps the AI provider (still wrapped in the world's content screen, exactly like the
    /// constructor does) — lets fixtures without a provider parameter (e.g. NpcLifeWorld) inject a fake. Combine with
    /// a config whose <see cref="ServerConfig.AiLevel"/> is not Off.</summary>
    public void SetAiProviderForTest(IAiMissionProvider provider) => _ai = new ScreenedAiTextProvider(provider, ScreenAiText);

    /// <summary>Test seam: run every AI request (greetings, VEGA banter, /npc-lines) inline instead of on the thread pool,
    /// so a test sees the cached result right after the triggering call.</summary>
    public void RunAiLinesSynchronouslyForTest(bool inline = true) => _aiLinesSystem.AiRunInlineForTest = inline;

    /// <summary>Test seam: forces the variant top-up roll (0 = always ask for another variant, 1 = never); null = random.</summary>
    public void SetAiVariantRollForTest(double? roll) => _aiLinesSystem.AiVariantRollForTest = roll;

    /// <summary>Test seam: <see cref="RequestAiLines"/> from a test.</summary>
    public bool RequestAiLinesForTest(string cacheKey, NpcLinesRequest request, AiLinePriority priority,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
        => RequestAiLines(cacheKey, request, priority, requiredTokens, allowedTokens);

    /// <summary>Test seam: <see cref="AiLinesFor"/> from a test.</summary>
    public string[]? AiLinesForTest(string cacheKey, AiLinePriority priority, NpcLinesRequest request,
        IReadOnlyCollection<string>? requiredTokens = null, IReadOnlyCollection<string>? allowedTokens = null)
        => AiLinesFor(cacheKey, priority, () => request, requiredTokens, allowedTokens);

    /// <summary>Test seam: the validated variants cached for a /npc-lines key (tokens unfilled).</summary>
    public IReadOnlyList<string[]> AiLinesCacheForTest(string cacheKey) => _aiLinesSystem.AiLinesCacheForTest(cacheKey);

    /// <summary>Test seam: how many /npc-lines keys are cached.</summary>
    public int AiLinesCacheCountForTest => _aiLinesSystem.AiLinesCacheCountForTest;

    /// <summary>Test seam: answers dropped by validation so far (wrong count, tokens, digits …).</summary>
    public int AiLinesRejectedForTest => _aiLinesSystem.AiLinesRejectedForTest;

    /// <summary>Test seam: the budget bucket's level now.</summary>
    public double AiLineTokensForTest => _aiLinesSystem.AiLineTokensForTest;

    /// <summary>Test seam: AI requests running right now (greetings + /npc-lines).</summary>
    public int AiLinesInFlightForTest => _aiLinesSystem.AiLinesInFlightForTest;

    /// <summary>Test seam: <see cref="FillAiLine"/> for a joined player.</summary>
    public string? FillAiLineForTest(string playerId, string line, IReadOnlyDictionary<string, string>? values = null)
        => FindSessionByPlayerId(playerId) is { } session ? FillAiLine(line, session, values) : null;

    // IAiLinesHost: delegates to the server's own field (never to the interface member, which would recurse).
    IAiMissionProvider IAiLinesHost.Ai => _ai;
}
