// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection story-pack, Journal and cutscene bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionStory
{
    /// <summary>#2466: the help-escalation clock of each active story-pack step (<c>&lt;pack&gt;:&lt;step&gt;</c> → state).
    /// RAM only and per session ("one session"): a rejoin starts the clocks again.</summary>
    public Dictionary<string, StepEscalationState> StepEscalation { get; } = new(System.StringComparer.Ordinal);

    /// <summary>#2466: active play seconds counted by the story-pack tick (the escalation clocks read it).</summary>
    public double StoryPackPlaySeconds { get; set; }

    /// <summary>#2466: <see cref="StoryPackPlaySeconds"/> of the last chapter offer — offers are spaced out.</summary>
    public double LastChapterOfferAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2459: the chapter VEGA offered last at its moment (<c>&lt;pack&gt;:&lt;chapter&gt;</c>, empty = none) and the
    /// <see cref="StoryPackPlaySeconds"/> of that offer — a tip the course teaches that comes up right after is told by the
    /// offer ("the tip becomes the offer"). The graduation's suggestions are no such offer.</summary>
    public string RecentChapterOfferKey { get; set; } = string.Empty;

    public double RecentChapterOfferAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2466: the join hook of the story packs ran for this session (re-speaks the active step once).</summary>
    public bool StoryPacksJoined { get; set; }

    /// <summary>#2471: something the Journal shows changed (a step, a chapter, an offer, the chip) since its last push —
    /// <c>GameServer.TickJournal</c> sends the Journal messages that differ, at most once per
    /// <c>GameServer.JournalPushGapSeconds</c>. RAM only.</summary>
    public bool JournalDirty { get; set; }

    /// <summary>#2475: this player's resolved leads of their current world-story chapter (re-resolved only when what they
    /// depend on changed), and the lead the last marker went up for. RAM only.</summary>
    internal StoryLeadCache StoryLeads { get; } = new();

    /// <summary>#2471: server uptime of the last Journal push (the coalescing clock).</summary>
    public double JournalPushedAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2471: the encoded <c>StoryPackStates</c> last sent (progress counters zeroed — a progress tick alone never
    /// re-sends it), to send only a change. RAM only.</summary>
    public byte[]? JournalStatesSent { get; set; }

    /// <summary>#2471: the encoded <c>ObjectiveList</c> last sent. RAM only.</summary>
    public byte[]? JournalObjectivesSent { get; set; }

    /// <summary>#2499: the tutorial pack just started by itself for a newcomer whose client draws buttons — the walk that
    /// follows asks "Want me to show you the basics?" before the first step's line (the pack id; empty = nothing to ask).
    /// Set and cleared inside one <c>AdvancePack</c>. RAM only.</summary>
    public string AskSkipDuePack { get; set; } = string.Empty;

    /// <summary>#2499: the pack whose first-start question went out this session and has not been answered with a skip yet
    /// (empty = none) — a skip of that pack then gets VEGA's "the training waits in your Journal". "Let's go" is never
    /// sent (doing nothing is the same answer), so it stays set. RAM only.</summary>
    public string AskSkipOpenPack { get; set; } = string.Empty;

    /// <summary>#2467: server uptime of the network join — no cutscene goes out until the client is in the world (its first
    /// position report, or <c>GameServer.CutsceneJoinSettleSeconds</c> later). Negative infinity = not a network join.</summary>
    public double CutsceneJoinAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2467: cutscenes committed to this player but not sent yet (waiting for the client to be in the world or for
    /// the 90 s cooldown). RAM only — the <c>:sent</c> ledger key re-offers them at the next join.</summary>
    public List<PendingCutscene> CutsceneQueue { get; } = new();

    /// <summary>#2467: server uptime of the last unrequested T2+ cutscene sent (the cooldown clock).</summary>
    public double CutsceneLastSentAt { get; set; } = double.NegativeInfinity;

    /// <summary>#2467: the scenes (<c>&lt;pack&gt;/&lt;id&gt;</c>) that may follow the last sent one at once — its
    /// <c>chainNext</c>.</summary>
    public HashSet<string> CutsceneChain { get; } = new(System.StringComparer.Ordinal);

    /// <summary>#2467: the pack chapters (<c>&lt;pack&gt;:&lt;chapter&gt;</c>) this session presented — a step of them
    /// activated with its line, or one was running at the join. A chapter's close cutscene plays only for these, so a
    /// chapter completed silently by evidence (a veteran) gets none. RAM only.</summary>
    public HashSet<string> PresentedPackChapters { get; } = new(System.StringComparer.Ordinal);
}
