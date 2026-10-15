// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// VEGA notices the player (#2595, #2597, #2596's voice): a "welcome back aboard" when the player boards the ship after
/// at least <see cref="VegaWelcomeAboardMinOutside"/> outside (at most every half hour, never after a death), a "welcome
/// back" a moment after logging in when the player was away for <see cref="NpcWelcomeBackHours"/> or more, and praise
/// when an achievement is earned — several unlocks within <see cref="VegaPraiseCoalesceSeconds"/> become one line, the
/// retro-awards settled at join stay silent, the story category is left to the story, and praise waits
/// <see cref="VegaPraiseGap"/> between lines.
/// <para>Gated like VEGA's banter: silent during the basic training (the tutorial pack's <c>banter</c> gate), for an
/// observer, and — for the advisor/tip channels — while the player switched VEGA's hints off (held back server-side so no
/// cooldown burns on a muted panel). Each chatter line pushes the next context tip back by
/// <see cref="VegaChatterTipPause"/>, so a tip never lands on top of it.</para>
/// <para>Two modes like every line of the package: the authored locale key always works; with AI personalisation on, a
/// re-wording from the shared AI path (<c>vega_welcome</c>, <c>vega_praise</c>, <c>vega_news</c>) is used when it is
/// ALREADY cached — a miss sends the authored line and asks in the background. A VEGA line is never swapped later.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long the player must have been off the ship for boarding to earn a "welcome back aboard" (E3).</summary>
    private const double VegaWelcomeAboardMinOutside = 600.0;

    /// <summary>At most one "welcome back aboard" per this many seconds (E3).</summary>
    private const double VegaWelcomeAboardCooldown = 1800.0;

    /// <summary>The login welcome waits this long after the join (the world and the chip settle first).</summary>
    private const double VegaWelcomeLoginDelay = 15.0;

    /// <summary>Unlocks within this window after the first one are praised in one line.</summary>
    private const double VegaPraiseCoalesceSeconds = 2.0;

    /// <summary>Seconds between two praise lines (E5).</summary>
    private const double VegaPraiseGap = 90.0;

    /// <summary>A chatter line pushes the next context tip back by this much.</summary>
    private const double VegaChatterTipPause = 30.0;

    /// <summary>VEGA's "persona" slot in AI-line cache keys — outside the NPC range (0..13).</summary>
    private const int VegaAiPersonaIndex = 100;

    /// <summary>Most characters of an AI-worded VEGA line.</summary>
    private const int VegaChatterMaxChars = 140;

    /// <summary>VEGA's voice for the AI line path.</summary>
    private const string VegaLinePersona =
        "VEGA, the player's ship AI: dry, laconic, deadpan humour — but warm, loyal and proud of a young pilot";

    /// <summary>The achievement categories with a praise line of their own (<c>vega.praise.&lt;category&gt;</c>); any
    /// other category but "story" gets one of the general lines.</summary>
    private static readonly HashSet<string> PraiseCategories = new(StringComparer.Ordinal)
    {
        "mining", "building", "crafting", "research", "explore", "survival",
    };

    /// <summary>Picks among the authored variants (its own stream — VEGA's evasion rolls stay deterministic).</summary>
    private readonly Random _vegaChatterRng = new(0x7E6A);

    /// <summary>What a VEGA chatter line asks the AI path for: the purpose, a subject for the cache key (never a player),
    /// the authored lines with placeholder tokens as meaning anchors, the values of those tokens for THIS receiver, and
    /// the tokens every AI line must keep.</summary>
    private readonly record struct VegaAiAsk(
        string Purpose,
        string Subject,
        IReadOnlyList<string> Anchors,
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyCollection<string> Required,
        int AwayDays = 0,
        string Situation = "");

    /// <summary>The 1 Hz VEGA chatter pass of one player (from <see cref="TickShipAi"/>): seeds the session's state on its
    /// first pass, then the aboard welcome, the login welcome and the praise flush.</summary>
    private void TickVegaChatterFor(PlayerSession session)
    {
        if (!session.VegaChatterSeeded)
        {
            session.VegaChatterSeeded = true;
            session.VegaWasAboard = session.State.AboardShip;
            session.VegaLeftShipAt = double.NaN;
            if (DaysSince(session.PreviousSeenUtc) is { } days && days * 24.0 >= NpcWelcomeBackHours)
            {
                session.VegaWelcomeLoginDueAt = _uptime + VegaWelcomeLoginDelay;
            }
        }

        TickVegaAboardWelcome(session);
        TickVegaLoginWelcome(session);
        FlushVegaPraise(session);
    }

    /// <summary>Called when the player is put back aboard by the game, not by walking in (a respawn after a death, the
    /// ship lost under them): that is no homecoming — the outside clock starts over.</summary>
    private void ResetVegaAboardWelcome(PlayerSession session)
    {
        session.VegaWasAboard = true;
        session.VegaLeftShipAt = double.NaN;
    }

    /// <summary>Whether VEGA may chat with this player on a channel of <paramref name="kind"/>: not during the basic
    /// training (the banter gate), not on a channel the player muted.</summary>
    private bool VegaChatterOpen(PlayerSession session, byte kind)
        => !session.Spectating && TutorialGate(session, StoryGateNames.Banter) != false && !HintMutedFor(session, kind);

    /// <summary>Boarding = the aboard flag going false → true; the outside time counts from the step off the ship.</summary>
    private void TickVegaAboardWelcome(PlayerSession session)
    {
        bool aboard = session.State.AboardShip;
        bool was = session.VegaWasAboard;
        session.VegaWasAboard = aboard;
        if (!aboard)
        {
            if (was || double.IsNaN(session.VegaLeftShipAt))
            {
                session.VegaLeftShipAt = _uptime; // stepped off (or outside since the join/respawn): the clock starts
            }

            return;
        }

        if (was || double.IsNaN(session.VegaLeftShipAt))
        {
            return; // no boarding edge
        }

        double outside = _uptime - session.VegaLeftShipAt;
        session.VegaLeftShipAt = double.NaN;
        if (outside < VegaWelcomeAboardMinOutside || _uptime < session.VegaWelcomeAboardReadyAt
            || !VegaChatterOpen(session, VegaTipRepeatKind))
        {
            return;
        }

        session.VegaWelcomeAboardReadyAt = _uptime + VegaWelcomeAboardCooldown;
        var anchorKeys = KeyPool("vega.welcome.aboard.", 4);
        string lineKey = anchorKeys[_vegaChatterRng.Next(anchorKeys.Count)];
        string name = session.State.Name ?? string.Empty;
        bool night = LocalDayFraction(session.State.Position) is < 0.15 or > 0.85;
        SpeakVegaChatter(session, lineKey, VegaTipRepeatKind, new[] { name }, new VegaAiAsk(
            NpcLinePurposes.VegaWelcome,
            "aboard." + (night ? "night" : "day"),
            VegaAnchors(session, anchorKeys, playerArg: true),
            new Dictionary<string, string>(StringComparer.Ordinal),
            Array.Empty<string>(),
            Situation: "the pilot just came back aboard the ship after a while outside"));
    }

    /// <summary>The login welcome, once, a moment after a join that followed a long absence.</summary>
    private void TickVegaLoginWelcome(PlayerSession session)
    {
        if (session.VegaWelcomeLoginDueAt <= 0 || _uptime < session.VegaWelcomeLoginDueAt)
        {
            return;
        }

        session.VegaWelcomeLoginDueAt = 0; // once — a closed gate (training, muted) drops it rather than saving it for later
        if (!VegaChatterOpen(session, 1))
        {
            return;
        }

        int days = (int)Math.Floor(DaysSince(session.PreviousSeenUtc) ?? 0);
        var anchorKeys = KeyPool("vega.welcome.login.", 3);
        string lineKey = anchorKeys[_vegaChatterRng.Next(anchorKeys.Count)];
        var anchors = VegaAnchors(session, anchorKeys, playerArg: true);
        SpeakVegaChatter(session, lineKey, 1, new[] { session.State.Name ?? string.Empty }, new VegaAiAsk(
            NpcLinePurposes.VegaWelcome,
            "login." + AwayBucket(days),
            anchors,
            new Dictionary<string, string>(StringComparer.Ordinal),
            AiLineText.TokensIn(anchors),
            AwayDays: days,
            Situation: "the pilot just logged in again after being away"));
    }

    /// <summary>
    /// #2597: an achievement was just earned (<see cref="TryAward"/> with praise) — queue VEGA's praise. The first unlock
    /// opens a <see cref="VegaPraiseCoalesceSeconds"/> window; everything earned inside it is praised in one line. The
    /// story category is skipped (the story has its own moments), and so is anything earned while the join places the
    /// player.
    /// </summary>
    private void QueueVegaPraise(PlayerSession session, AchievementDefinition def)
    {
        if (session.InJoinPlacement || string.Equals(def.Category, "story", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (session.PraisePending.Count == 0)
        {
            session.PraiseDueAt = _uptime + VegaPraiseCoalesceSeconds;
        }

        session.PraisePending.Add(def.Key);
    }

    /// <summary>Speaks the queued praise once its window closed — or drops it when VEGA may not chat right now (the
    /// training, a muted panel) or praised less than <see cref="VegaPraiseGap"/> ago. The banner shows every unlock anyway.</summary>
    private void FlushVegaPraise(PlayerSession session)
    {
        if (session.PraisePending.Count == 0 || _uptime < session.PraiseDueAt)
        {
            return;
        }

        var keys = session.PraisePending.ToList();
        session.PraisePending.Clear();
        if (_uptime < session.VegaPraiseReadyAt || !VegaChatterOpen(session, VegaTipRepeatKind))
        {
            return;
        }

        session.VegaPraiseReadyAt = _uptime + VegaPraiseGap;
        if (keys.Count > 1)
        {
            // Several at once: one authored line with the count (numbers are facts — no AI wording here).
            SpeakVegaChatter(session, "vega.praise.many", VegaTipRepeatKind,
                new[] { keys.Count.ToString(CultureInfo.InvariantCulture) }, ask: null);
            return;
        }

        var def = _content.Achievements.FirstOrDefault(a => a.Key == keys[0]);
        string category = def?.Category ?? string.Empty;
        var anchorKeys = PraiseCategories.Contains(category)
            ? new List<string> { "vega.praise." + category }
            : KeyPool("vega.praise.any.", 2);
        string lineKey = anchorKeys[_vegaChatterRng.Next(anchorKeys.Count)];
        string achievementName = Localize(session.Locale, "achv." + keys[0] + ".name");
        var anchors = VegaAnchors(session, anchorKeys, playerArg: false);
        SpeakVegaChatter(session, lineKey, VegaTipRepeatKind, new[] { achievementName }, new VegaAiAsk(
            NpcLinePurposes.VegaPraise,
            PraiseCategories.Contains(category) ? category : "any",
            anchors,
            new Dictionary<string, string>(StringComparer.Ordinal) { [AiLineTokens.Arg0] = achievementName },
            AiLineText.TokensIn(anchors),
            Situation: "the pilot just earned an achievement; {0} is its name"));
    }

    /// <summary>
    /// Sends one VEGA chatter line: the cached AI re-wording when <paramref name="ask"/> has one (as <c>Text</c>, same
    /// kind, tokens filled for THIS player), else the authored <paramref name="lineKey"/> with <paramref name="args"/> as
    /// its {0}, {1} … (the client localizes). A miss asks the AI path in the background for next time. Pushes the next
    /// context tip back by <see cref="VegaChatterTipPause"/>.
    /// </summary>
    private void SpeakVegaChatter(PlayerSession session, string lineKey, byte kind, string[] args, VegaAiAsk? ask)
    {
        string? text = null;
        if (ask is { } a && AiLinesEnabled)
        {
            var allowed = AiLineText.TokensIn(a.Anchors);
            if (!allowed.Contains(AiLineTokens.Player))
            {
                allowed.Add(AiLineTokens.Player);
            }

            string cacheKey = AiLineCacheKey(a.Purpose, a.Subject, VegaAiPersonaIndex, session.Locale, string.Empty);
            var lines = AiLinesFor(cacheKey, AiLinePriority.Vega, () => new NpcLinesRequest
            {
                Purpose = a.Purpose,
                Language = session.Locale,
                AwayDays = a.AwayDays,
                Situation = a.Situation,
                Speakers = { new NpcLinesSpeaker { Role = "ship_ai", IsRobot = true, Persona = VegaLinePersona } },
                Tokens = allowed.ToList(),
                Authored = a.Anchors.ToList(),
                LineCount = 1,
                MaxChars = VegaChatterMaxChars,
            }, requiredTokens: a.Required, allowedTokens: allowed);
            if (lines is { Length: > 0 })
            {
                text = FillAiLine(lines[0], session, a.Values);
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            // The line key rides along for the log/queue; the client shows the text (any kind keeps its mute rule).
            SendVegaWithObjective(session, new ShipAiLine { LineKey = lineKey, Text = ClampVegaText(text!), Kind = kind });
        }
        else
        {
            SendVegaLine(session, lineKey, kind, string.Join("\u001f", args));
        }

        session.VegaTipReadyAt = Math.Max(session.VegaTipReadyAt, _uptime + VegaChatterTipPause);
    }

    /// <summary>The authored lines of <paramref name="keys"/> in the player's language as AI anchors: with
    /// <paramref name="playerArg"/> their {0} (the player's name) becomes the <c>{player}</c> token, else the positional
    /// tokens stay as they are.</summary>
    private List<string> VegaAnchors(PlayerSession session, IEnumerable<string> keys, bool playerArg)
        => keys.Select(k => Localize(session.Locale, k))
            .Select(t => playerArg ? t.Replace(AiLineTokens.Arg0, AiLineTokens.Player) : t)
            .ToList();

    /// <summary>Whole and fractional days since an ISO "last seen" stamp; null when empty or unreadable.</summary>
    private double? DaysSince(string iso)
        => !string.IsNullOrEmpty(iso) && TryParseUtc(iso, out var then) ? Math.Max(0.0, (WallUtcNow - then).TotalDays) : null;

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#2595/#2597): runs this player's VEGA chatter pass now (the 1 Hz part of <c>TickShipAi</c>).</summary>
    public void TickVegaChatterForTest(PlayerSession session) => TickVegaChatterFor(session);

    /// <summary>Test seam (#2597): <see cref="Advance"/> — bumps an achievement counter as gameplay would.</summary>
    public void AdvanceAchievementForTest(PlayerSession session, string counter, int amount = 1) => Advance(session, counter, amount);
}
