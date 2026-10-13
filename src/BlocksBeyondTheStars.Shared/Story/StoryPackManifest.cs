// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Story;

// The story-pack manifest (#2466, synthesis §5.4): the optional fields a pack's story.json may carry next to the
// world-arc fields of StoryDefinition. Every field defaults to today's behaviour, so a pack written before these
// existed (data/stories/vega_protocol/story.json) loads unchanged. The per-player step runtime that runs chapters and
// steps lives on the server (GameServerStoryPacks.cs); the reference for authors is docs/developer/TUTORIAL_PACK.md.

/// <summary>Who speaks a pack's lines (synthesis §5.4). On the wire as <c>ShipAiLine.Speaker</c>; a client maps an
/// unknown id to VEGA's panel style, so a new narrator needs no code.</summary>
public sealed class StoryNarrator
{
    /// <summary>The narrator every pack has unless it names another.</summary>
    public const string DefaultId = "vega";

    /// <summary>Narrator id (<c>vega</c> by default).</summary>
    public string Id { get; set; } = DefaultId;

    /// <summary>Locale key of the narrator's display name; empty = the client's default for the id.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Portrait icon name (client resource); empty = the id's default.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Panel accent colour (<c>#RRGGBB</c>); empty = the id's default.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>Vocoder chatter clips played under the lines; empty = the id's default.</summary>
    public List<string> Voice { get; set; } = new();

    /// <summary>Sound played when a line starts; empty = the id's default.</summary>
    public string LineStart { get; set; } = string.Empty;
}

/// <summary>Help escalation timings in seconds of active play without progress on a step (synthesis §3.1): a more
/// concrete hint line, a marker ping for this player only (a "where" step: one with an <c>onStart.reveal</c> provider)
/// and an offer to skip (MUST steps). The pack carries the defaults; a step may override any of them. Unset = inherit
/// (step → pack → built-in default); 0 or less switches that stage off.</summary>
public sealed class StoryEscalation
{
    /// <summary>Built-in seconds before the hint line.</summary>
    public const int DefaultHintAfter = 180;

    /// <summary>Built-in seconds before the marker ping.</summary>
    public const int DefaultMarkerAfter = 360;

    /// <summary>Built-in seconds before the skip offer.</summary>
    public const int DefaultOfferSkipAfter = 600;

    /// <summary>Seconds before the step's hint line (and before an open CORE step stops blocking its chapter).</summary>
    public int? HintAfter { get; set; }

    /// <summary>Seconds before the marker ping of a "where" step.</summary>
    public int? MarkerAfter { get; set; }

    /// <summary>Seconds before the offer to skip a MUST step.</summary>
    public int? OfferSkipAfter { get; set; }
}

/// <summary>When a pack starts for a player (synthesis §5.4). Empty <see cref="After"/> = at the player's first join
/// (subject to the world's tutorial policy for a tutorial pack).</summary>
public sealed class StoryStartRule
{
    /// <summary>Conditions that must all hold before the pack starts by itself.</summary>
    public List<string> After { get; set; } = new();

    /// <summary>Whether the player may start the pack before <see cref="After"/> holds ("Start the story now").</summary>
    public bool EarlyOptIn { get; set; }
}

/// <summary>A chapter lead of a world story (#2475, synthesis §4.8 option O2) — one entry of the chapter's compass: shown on
/// VEGA's chip while it is the player's first open, feasible lead, listed in the Journal as a task, and worth the chapter's
/// once-per-save bonus (<see cref="StoryDefinition.LeadBonusMilestones"/>) when a player follows it. See
/// <see cref="StoryLeadTypes"/> for the vocabulary and <see cref="StoryLeads"/> for the rules.</summary>
public sealed class StoryLead
{
    /// <summary>The lead vocabulary word (<c>fragment</c>, <c>unique_site:observatory</c>, <c>research:radar_array</c>,
    /// <c>finale</c>, <c>relay</c> …).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Locale key of the chip / Journal text while this lead is current; empty = the built-in key of its word
    /// (<see cref="StoryLeads.ObjKey"/>). The rules-aware variants (a fragment on an unnamed world, the finale without a jump
    /// generator) always use the built-in keys.</summary>
    public string ObjKey { get; set; } = string.Empty;
}

/// <summary>
/// One chapter of a pack (synthesis §5.4). In a player pack a chapter runs its <see cref="Steps"/> per player: a
/// non-optional chapter starts by itself once the previous non-optional chapter is done or skipped (or its own
/// <see cref="Requires"/> holds), an <see cref="Optional"/> one is offered when <see cref="OfferWhen"/> holds and runs
/// once the player starts it. In a world pack a chapter is "reached" by the arc (<see cref="FromBeat"/> /
/// <see cref="FromEvent"/>, step 11).
/// </summary>
public sealed class StoryChapter
{
    /// <summary>Chapter id, unique within the pack (<c>b3</c>, <c>c8</c>, <c>awakening</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of the chapter title.</summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>Locale key of a one-line summary for the Journal; empty = none.</summary>
    public string SummaryKey { get; set; } = string.Empty;

    /// <summary>A deep dive: not needed for the pack to be done, offered instead of started.</summary>
    public bool Optional { get; set; }

    /// <summary>Conditions before the chapter is available. Empty = a non-optional chapter waits for the previous
    /// non-optional chapter; an optional chapter is available at once.</summary>
    public List<string> Requires { get; set; } = new();

    /// <summary>Conditions that make the runtime offer an optional chapter (once; never while the pack's mandatory
    /// chapters still run).</summary>
    public List<string> OfferWhen { get; set; } = new();

    /// <summary>#2482: conditions under which an optional chapter exists for the player at all — a deep dive the world's rules
    /// can rule out (combat switched on, a world for more than one player). While they do not hold the chapter is left out of
    /// the Journal and is never offered, suggested or started; one the player runs, finished or skipped stays listed. Empty =
    /// always there.</summary>
    public List<string> VisibleIf { get; set; } = new();

    /// <summary>Evidence that the player knows the chapter already: when it holds as the chapter becomes available, the
    /// chapter completes silently. Ignored while the chapter is replayed.</summary>
    public List<string> AutoCompleteIf { get; set; } = new();

    /// <summary>Whether the chapter starts by itself; unset = true for non-optional chapters, false for optional ones.</summary>
    public bool? AutoStart { get; set; }

    /// <summary>Cutscene played when the chapter starts presenting to a player (its first step activates; not during a
    /// replay, not for a chapter that completes silently) — #2467.</summary>
    public string OpenCutscene { get; set; } = string.Empty;

    /// <summary>Cutscene played when a chapter the player was shown is done (not when skipped, replayed or completed
    /// silently by evidence) — #2467.</summary>
    public string CloseCutscene { get; set; } = string.Empty;

    /// <summary>World packs: the beat index that reaches this chapter (-1 = none).</summary>
    public int FromBeat { get; set; } = -1;

    /// <summary>World packs: the arc event that reaches this chapter (<c>guardian_defeated</c> …); empty = none.</summary>
    public string FromEvent { get; set; } = string.Empty;

    /// <summary>World packs: the chapter's leads (#2475) — the compass while the player is in this chapter. The fallbacks
    /// (<see cref="StoryLeadTypes.Fallbacks"/>, then <see cref="StoryLeadTypes.Explore"/>) follow them on their own.</summary>
    public List<StoryLead> Leads { get; set; } = new();

    /// <summary>World packs: the chapter's story missions (#2476, synthesis §4.8 option O3) — VEGA hands them to each player
    /// whose story track reaches this chapter, one at a time in this order, and lets an unfinished one go when the player's
    /// chapter moves on. They never hold a beat back. See <see cref="StoryMission"/>.</summary>
    public List<StoryMission> Missions { get; set; } = new();

    /// <summary>The chapter's steps in order.</summary>
    public List<StoryStep> Steps { get; set; } = new();

    /// <summary>Whether the player may skip this chapter; unset = allowed.</summary>
    public bool? Skippable { get; set; }
}

/// <summary>
/// A story mission of a world-story chapter (#2476, synthesis §4.8 option O3: "VEGA story missions per chapter —
/// automatically accepted steps, layered on the leads"). A mission is a short, titled task list in the narrator's voice that
/// points at the chapter's story drivers: when a player's story track reaches the chapter (and the chapter's scene went out,
/// and no basic training runs), VEGA starts the chapter's first mission that is feasible here — her first step line is the
/// briefing — and the Journal's VEGA page lists it under "Missions in this chapter". Its steps are ordinary pack steps run by
/// the player-pack runtime (events, a mission bridge, or a lead bridge: <see cref="StoryObjectiveTypes.Lead"/>), one after the
/// other. Per player: in multiplayer everyone does their own. Finishing one counts as following a lead for the chapter bonus
/// (<see cref="StoryDefinition.LeadBonusMilestones"/>, still once per save and chapter); there is no other reward. A mission
/// never holds a beat back: the score keeps revealing them, and when the player's chapter moves on an unfinished mission is let
/// go (closed as skipped). Older clients (no <c>packs1</c>) get no missions — they keep the story chip they know.
/// </summary>
public sealed class StoryMission
{
    /// <summary>Mission id, unique within the pack (and distinct from its chapter ids — it shares their keys:
    /// <c>story:&lt;pack&gt;:ch:&lt;mission&gt;:active|done|skipped</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Locale key of the mission title (the Journal's row).</summary>
    public string TitleKey { get; set; } = string.Empty;

    /// <summary>Locale key of a one-line summary for the Journal; empty = none.</summary>
    public string SummaryKey { get; set; } = string.Empty;

    /// <summary>Conditions under which the mission can be done in this world (checked when its turn comes): false ⇒ it is
    /// skipped silently and its <see cref="Alternative"/> runs instead, if it has one (no dead ends).</summary>
    public List<string> FeasibleIf { get; set; } = new();

    /// <summary>Mission id (same chapter) that stands in when this one is infeasible; it runs only standing in.</summary>
    public string Alternative { get; set; } = string.Empty;

    /// <summary>Whether the player may skip the mission in the Journal; unset = allowed.</summary>
    public bool? Skippable { get; set; }

    /// <summary>The mission's steps in order (two to four is the rule of thumb; every step needs a chip text — the Journal's
    /// line).</summary>
    public List<StoryStep> Steps { get; set; } = new();
}

/// <summary>What completes a step (synthesis §5.4): an event count since activation (<see cref="Event"/>, optional
/// <see cref="Arg"/>, <see cref="Required"/>), a mission bridge (<see cref="Type"/> = <c>Mission</c>,
/// <see cref="Target"/> = mission id — satisfied on that mission's turn-in, or at activation when it is already turned in) or,
/// in a world story's mission, a lead bridge (#2476: <see cref="Type"/> = <c>Lead</c>, <see cref="Target"/> = a lead word of
/// <see cref="StoryLeadTypes"/> — satisfied the way that lead is followed, its evidence included).</summary>
public sealed class StoryStepObjective
{
    /// <summary>The game event (<c>GameEventKeys</c>), a prefix pattern ending in <c>*</c>, or a client teaching flag
    /// <c>ui:&lt;id&gt;</c> (only here, never in a condition). Empty for a mission bridge or a read step.</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>The event argument that counts: exact, several separated by <c>|</c> (<c>flight|quick</c>), or empty
    /// for any.</summary>
    public string Arg { get; set; } = string.Empty;

    /// <summary>How many times (the events' amounts summed) since the step activated.</summary>
    public int Required { get; set; } = 1;

    /// <summary><c>Mission</c> for a mission bridge, <c>Lead</c> for a lead bridge (#2476); empty for an event count.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The bridged mission id (must exist in <c>data/missions.json</c>), or the bridged lead word
    /// (<c>unique_site:observatory</c>, <c>research:radar_array</c> …).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Whether this is a mission bridge.</summary>
    public bool IsMission => string.Equals(Type, StoryObjectiveTypes.Mission, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is a lead bridge (#2476).</summary>
    public bool IsLead => string.Equals(Type, StoryObjectiveTypes.Lead, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A step's texts (locale keys): the line spoken at activation, the chip text, the escalation hint and the
/// line spoken on completion. Any may be empty.</summary>
public sealed class StoryStepExplain
{
    /// <summary>Spoken when the step activates (and again at a rejoin while it is on the chip).</summary>
    public string StartKey { get; set; } = string.Empty;

    /// <summary>The objective chip text while the step is on the chip.</summary>
    public string ChipKey { get; set; } = string.Empty;

    /// <summary>The more concrete line of the help escalation.</summary>
    public string HintKey { get; set; } = string.Empty;

    /// <summary>Spoken when the step completes while it was the chip's step.</summary>
    public string DoneKey { get; set; } = string.Empty;

    /// <summary>A Codex article id for "More" (presentation only).</summary>
    public string MoreGuide { get; set; } = string.Empty;
}

/// <summary>A whole-line variant of a step's texts for a world rule or mode (synthesis G8: never sentence pieces). The
/// first variant whose <see cref="When"/> holds wins; its empty fields fall back to the step's <c>explain</c>.</summary>
public sealed class StoryStepVariant
{
    /// <summary>The condition (e.g. <c>world:creative</c>, <c>not:world:hunger</c>).</summary>
    public string When { get; set; } = string.Empty;

    /// <inheritdoc cref="StoryStepExplain.StartKey"/>
    public string StartKey { get; set; } = string.Empty;

    /// <inheritdoc cref="StoryStepExplain.ChipKey"/>
    public string ChipKey { get; set; } = string.Empty;

    /// <inheritdoc cref="StoryStepExplain.HintKey"/>
    public string HintKey { get; set; } = string.Empty;

    /// <inheritdoc cref="StoryStepExplain.DoneKey"/>
    public string DoneKey { get; set; } = string.Empty;
}

/// <summary>What happens around a step: a cutscene binding (#2467: played before the step's start line, on
/// <c>onComplete</c> before its done line; never for a silent completion) and, on <c>onStart</c>, the marker
/// provider of a "where" step (<see cref="StoryRevealProviders"/>) the help escalation pings for this player only.</summary>
public sealed class StoryStepHook
{
    /// <summary>Cutscene id (pack-local); empty = none.</summary>
    public string Cutscene { get; set; } = string.Empty;

    /// <summary>Marker provider (<c>nearest_ore:&lt;block&gt;</c>, <c>nearest_settlement</c>); empty = not a "where" step.</summary>
    public string Reveal { get; set; } = string.Empty;
}

/// <summary>
/// One step of a chapter (synthesis §5.4, d1 §1.7): <c>do</c> steps are verified by their <see cref="Objective"/>,
/// <c>read</c> steps complete on "continue" (<c>ui:vega_continue</c>) or when the chapter's next do step completes and
/// never block, <c>react</c> steps are armed from the chapter start, never block and stay armed until they fire once.
/// <see cref="Tier"/> decides what a non-optional chapter waits for: every <c>must</c> do step; an open <c>core</c> step
/// blocks until its hint time passes and then becomes an "open extra" that still counts silently; <c>opt</c> never
/// blocks. An optional chapter waits for every do step.
/// </summary>
public sealed class StoryStep
{
    /// <summary>Step id, unique within the pack; by convention it carries its chapter (<c>b3.1</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>do</c> (default), <c>read</c> or <c>react</c> (<see cref="StoryStepKinds"/>).</summary>
    public string Kind { get; set; } = StoryStepKinds.Do;

    /// <summary><c>must</c> (default), <c>core</c> or <c>opt</c> (<see cref="StoryStepTiers"/>).</summary>
    public string Tier { get; set; } = StoryStepTiers.Must;

    /// <summary>Official tutorial only: the legacy milestone (<c>vega:stage:&lt;id&gt;</c>) written next to the step's own
    /// key on completion and skip, so older builds keep their onboarding state, and read once by the migration
    /// (<c>story:&lt;pack&gt;:migrated:v1</c>). It also lets a client without <c>packs1</c> follow the step through the
    /// old keys.</summary>
    public string LegacyKey { get; set; } = string.Empty;

    /// <summary>What completes the step.</summary>
    public StoryStepObjective Objective { get; set; } = new();

    /// <summary>Conditions before the step activates. Empty = the chapter order (the previous blocking step done).</summary>
    public List<string> Requires { get; set; } = new();

    /// <summary>Conditions under which the step can be done here. False at activation ⇒ the <see cref="Alternative"/>
    /// runs instead, or the step is skipped silently (synthesis G7: no dead ends).</summary>
    public List<string> FeasibleIf { get; set; } = new();

    /// <summary>Step id (same chapter) that replaces this one when it is infeasible.</summary>
    public string Alternative { get; set; } = string.Empty;

    /// <summary>Evidence that the step is known already: when it holds at activation, the step completes silently.
    /// Ignored while the chapter is replayed.</summary>
    public List<string> AlreadyDoneIf { get; set; } = new();

    /// <summary>Whole-line text variants per world rule; the first match wins.</summary>
    public List<StoryStepVariant> Variants { get; set; } = new();

    /// <summary>The step's texts.</summary>
    public StoryStepExplain Explain { get; set; } = new();

    /// <summary>Activation hook (cutscene, marker provider).</summary>
    public StoryStepHook OnStart { get; set; } = new();

    /// <summary>Completion hook (cutscene).</summary>
    public StoryStepHook OnComplete { get; set; } = new();

    /// <summary>Overrides of the pack's escalation timings; null = the pack's.</summary>
    public StoryEscalation? Escalate { get; set; }

    /// <summary>Whether the player may skip this step ("I know this"); unset = allowed.</summary>
    public bool? Skippable { get; set; }

    /// <summary>#2469 (synthesis G12, "one teaching voice"): VEGA's advisor hints and context tips this step teaches
    /// itself (<c>vega:hint:&lt;id&gt;</c> ids such as <c>invfull</c>, <c>lamp_off</c>). While the step is still ahead of the
    /// player in a running pack they stay quiet; when the step's own line is shown they count as told; a step that is
    /// skipped gives them back. #2459: a client-only lesson the step teaches (<c>climb</c>, <c>planet_card</c> …) is named
    /// here too — the client keeps it quiet while the step's chapter runs with the step active or ahead — and an optional
    /// chapter's offer stands in for the hints and tips its steps name ("the tip becomes the offer"). Empty = none.</summary>
    public List<string> HintRefs { get; set; } = new();
}

/// <summary>What a player pack does when a player finishes its mandatory chapters by playing them (#2469, synthesis
/// §3.3 "Absolvent"): a closing line, an achievement counter and up to three suggested optional chapters. Skipping the
/// pack, or a veteran's migration, graduates nobody.</summary>
public sealed class StoryGraduation
{
    /// <summary>The most chapters a graduation suggests.</summary>
    public const int MaxSuggestions = 3;

    /// <summary>The closing line (a client with <c>packs1</c>; an older client of a tutorial hears the old <c>vega.done</c>).
    /// Empty = none.</summary>
    public string LineKey { get; set; } = string.Empty;

    /// <summary>The achievement counter advanced once per graduation (<c>tutorial:graduated</c>); achievements.json says
    /// which achievement watches it. Empty = none.</summary>
    public string Counter { get; set; } = string.Empty;

    /// <summary>Optional chapters of this pack offered at graduation (at most <see cref="MaxSuggestions"/>; only those
    /// whose <c>requires</c> hold and that are not done, declined or running).</summary>
    public List<string> Suggest { get; set; } = new();

    /// <summary>#2473: a cutscene of the pack played at the graduation, before the closing line (the tutorial's
    /// <c>tut.graduation</c> card). Empty = none.</summary>
    public string Cutscene { get; set; } = string.Empty;
}

/// <summary>Data form of a VEGA insight (d3 §5.1.1) — read by the insight code from step 16 on.</summary>
public sealed class StoryInsight
{
    /// <summary>The insight event (<c>companion_ward</c>, <c>shape_noncube</c>).</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>Beat index that must be revealed first.</summary>
    public int MinBeat { get; set; }

    /// <summary>Locale key of the line.</summary>
    public string TextKey { get; set; } = string.Empty;

    /// <summary>Per-player once-key (the existing <c>story:insight:*</c> keys).</summary>
    public string OnceKey { get; set; } = string.Empty;
}

/// <summary>World packs: the finale's type and its cutscene bindings (presentation only in v1; d3 §5.1.1).</summary>
public sealed class StoryFinaleBinding
{
    /// <summary><c>guardian_core</c> or empty (= none).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Cutscene per finale stage (<see cref="StoryFinaleStages"/>: <c>arrival</c>, <c>descent</c>, <c>breach</c>,
    /// <c>duel</c>, <c>standdown</c>, <c>resolution</c>). A stage without a binding plays nothing (its narrator line, if
    /// any, still speaks).</summary>
    public Dictionary<string, string> Cutscenes { get; set; } = new();
}

/// <summary>
/// The finale stages a world pack binds a cutscene to (<see cref="StoryFinaleBinding.Cutscenes"/>, synthesis §5.3
/// "Bindungsfelder"; #2478). The finale code plays each at its moment, once per player, only for players whose story track
/// has started.
/// </summary>
public static class StoryFinaleStages
{
    /// <summary>The player's flight enters the Guardian system (a hyperjump in flight) — a short "now or never" card.</summary>
    public const string Arrival = "arrival";

    /// <summary>The player lands on the core world — the way to the iron-ringed shaft (the <c>guardian_core</c> hint).</summary>
    public const string Descent = "descent";

    /// <summary>The hack is complete and the duel begins — for the players on the core world.</summary>
    public const string Breach = "breach";

    /// <summary>The duel begins — the banner for the players of the finale who are NOT on the core world (#2478: the duel
    /// dialogue itself shows only there). Not in the synthesis' binding list; added with the dialogue mode.</summary>
    public const string Duel = "duel";

    /// <summary>The duel is won — the machines stand down (before the ending; also owed to whoever missed the win).</summary>
    public const string Standdown = "standdown";

    /// <summary>The ending (card → credits → epilogue) on the director; older clients get <c>StoryResolved</c>.</summary>
    public const string Resolution = "resolution";

    /// <summary>Every stage.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Arrival, Descent, Breach, Duel, Standdown, Resolution };
}

/// <summary>The values of <see cref="StoryStep.Kind"/>.</summary>
public static class StoryStepKinds
{
    /// <summary>A verified action.</summary>
    public const string Do = "do";

    /// <summary>Read and continue — never blocks.</summary>
    public const string Read = "read";

    /// <summary>Armed by a situation — never blocks, stays armed until it fires once.</summary>
    public const string React = "react";

    /// <summary>Every kind.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Do, Read, React };
}

/// <summary>The values of <see cref="StoryStep.Tier"/>.</summary>
public static class StoryStepTiers
{
    /// <summary>The chapter waits for it.</summary>
    public const string Must = "must";

    /// <summary>Blocks until its hint time passes, then counts silently as an open extra.</summary>
    public const string Core = "core";

    /// <summary>Never blocks a non-optional chapter.</summary>
    public const string Opt = "opt";

    /// <summary>Every tier.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Must, Core, Opt };
}

/// <summary>The values of <see cref="StoryStepObjective.Type"/>.</summary>
public static class StoryObjectiveTypes
{
    /// <summary>A mission bridge.</summary>
    public const string Mission = "Mission";

    /// <summary>A lead bridge (#2476, a world story's missions only): the step is the chapter-lead word in
    /// <see cref="StoryStepObjective.Target"/> — completed by the same actions and evidence that follow that lead, shown with the
    /// lead's own place-aware text (and marker) wherever it can be followed from where the player is.</summary>
    public const string Lead = "Lead";

    /// <summary>The lead words a lead bridge may name: every word that a player's action follows — not <c>explore</c> (never
    /// done) and not the finale's (the finale is chapter 7's own set piece, never a mission step).</summary>
    public static bool IsBridgeableLead(string? leadType)
    {
        string kind = StoryLeadTypes.KindOf(leadType);
        return StoryLeadTypes.Problem(leadType) is null && kind != StoryLeadTypes.Explore && !StoryLeadTypes.IsFinale(kind);
    }
}

/// <summary>The gate names a pack's <see cref="StoryDefinition.Gates"/> may set (synthesis §5.3 "Tore"). A tutorial pack
/// says with them when the global advisor systems open; a gate a pack does not set keeps today's behaviour.</summary>
public static class StoryGateNames
{
    /// <summary>The opportunity/progression context tips (today: eat + mine + craft done).</summary>
    public const string OpportunityTips = "opportunityTips";

    /// <summary>VEGA's LLM banter (today: the whole onboarding done).</summary>
    public const string Banter = "banter";

    /// <summary>The <c>station_near</c> tip (today: while the dock lesson is open).</summary>
    public const string StationNear = "stationNear";

    /// <summary>Every known gate.</summary>
    public static readonly IReadOnlyList<string> All = new[] { OpportunityTips, Banter, StationNear };
}

/// <summary>The marker providers a step's <c>onStart.reveal</c> may name (synthesis §5.3: the help marker is a ping for
/// one player, in RAM, for 30 s — never <c>RevealPoi</c>).</summary>
public static class StoryRevealProviders
{
    /// <summary><c>nearest_ore:&lt;block key&gt;</c> — the nearest block of that kind around the player (a bounded probe).</summary>
    public const string NearestOre = "nearest_ore";

    /// <summary><c>nearest_settlement</c> — the nearest settlement on the player's body.</summary>
    public const string NearestSettlement = "nearest_settlement";

    /// <summary><c>nearest_food_plant</c> — the nearest plant whose harvest is food (berries …) around the player (#2469).</summary>
    public const string NearestFoodPlant = "nearest_food_plant";

    /// <summary><c>nearest_station</c> — the nearest space station of the player's system (#2471, the dock lesson): in flight the
    /// station of the player's orbit nearest to their ship, landed the first station of the system. Not a ping on the ground
    /// but the client's space waypoint (<c>ShipAiLine.SpaceWaypointId</c>), so the radar and the edge arrow lead to it.</summary>
    public const string NearestStation = "nearest_station";

    /// <summary><c>unique_site:&lt;observatory|shrine|derelict&gt;</c> — the spot of that one-of-a-kind site while the player is on its
    /// world (#2476: "read the observatory's last log" once they landed there). Nothing elsewhere.</summary>
    public const string UniqueSite = "unique_site";

    /// <summary>Whether <paramref name="reveal"/> names a known provider (with its argument where one is needed).</summary>
    public static bool IsKnown(string? reveal)
    {
        if (string.IsNullOrEmpty(reveal))
        {
            return false;
        }

        if (string.Equals(reveal, NearestSettlement, StringComparison.Ordinal) || string.Equals(reveal, NearestFoodPlant, StringComparison.Ordinal)
            || string.Equals(reveal, NearestStation, StringComparison.Ordinal))
        {
            return true;
        }

        if (reveal!.StartsWith(UniqueSite + ":", StringComparison.Ordinal))
        {
            string kind = reveal.Substring(UniqueSite.Length + 1);
            foreach (string known in StoryLeadTypes.UniqueSiteKinds)
            {
                if (string.Equals(known, kind, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        return reveal.StartsWith(NearestOre + ":", StringComparison.Ordinal) && reveal.Length > NearestOre.Length + 1;
    }
}

/// <summary>Narrator line keys the story runtime sends and the client treats specially (#2471).</summary>
public static class StoryNarratorLines
{
    /// <summary>The help line of a <see cref="StoryRevealProviders.NearestStation"/> step ({0} = the station's name), sent
    /// with the station as the space waypoint.</summary>
    public const string StepMarkedStation = "vega.step.marked_station";

    /// <summary>The one line a started player gets at the join instead of one line per missed story beat ({0} = how many
    /// entries the Journal gained) — the beats go to the Journal silently (#2471, B09). The client never keeps it in the
    /// notes (it points at them).</summary>
    public const string JournalNews = "vega.journal.news";

    /// <summary>The answer to a Start or Replay of a chapter with nothing left to practise ({0} = the chapter title, #2500). A
    /// reply to a click, so the client never keeps it in the notes.</summary>
    public const string ChapterNothingLeft = "vega.chapter.nothing_left";
}

/// <summary>The legacy onboarding vocabulary (the nine VEGA stages before the tutorial pack): the milestone a step's
/// <see cref="StoryStep.LegacyKey"/> double-writes and the keys an older client (no <c>packs1</c>) understands. Never
/// renamed (synthesis §5.7).</summary>
public static class StoryLegacy
{
    /// <summary>Prefix of a legacy stage milestone.</summary>
    public const string StagePrefix = "vega:stage:";

    /// <summary>The intro milestone older builds read as "the tutorial intro was shown".</summary>
    public const string IntroMilestone = "vega:intro";

    /// <summary>The nine stage ids, in the old lesson order.</summary>
    public static readonly IReadOnlyList<string> StageIds = new[] { "mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land" };

    /// <summary>The intro lines an older client speaks before its first lesson.</summary>
    public static readonly IReadOnlyList<string> IntroLines = new[] { "vega.intro.1", "vega.intro.2", "vega.intro.menu", "vega.intro.codex" };

    /// <summary>The send-off line after the last lesson.</summary>
    public const string DoneLine = "vega.done";

    /// <summary>The stage id of a legacy key (<c>vega:stage:mine</c> → <c>mine</c>), or null for anything else.</summary>
    public static string? StageIdOf(string? legacyKey)
    {
        if (string.IsNullOrEmpty(legacyKey) || !legacyKey!.StartsWith(StagePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string id = legacyKey.Substring(StagePrefix.Length);
        foreach (string known in StageIds)
        {
            if (string.Equals(known, id, StringComparison.Ordinal))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>The old start line of a stage (<c>vega.s.mine.start</c>).</summary>
    public static string StartLine(string stageId) => "vega.s." + stageId + ".start";

    /// <summary>The old completion line of a stage (<c>vega.s.mine.done</c>).</summary>
    public static string DoneLineOf(string stageId) => "vega.s." + stageId + ".done";

    /// <summary>The old chip key of a stage (<c>vega.obj.mine</c>).</summary>
    public static string ChipKey(string stageId) => "vega.obj." + stageId;
}
