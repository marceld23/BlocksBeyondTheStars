// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Missions;

/// <summary>Server-validatable objective kinds. MVP fully supports Mine, Collect and Deliver.</summary>
public enum MissionObjectiveType
{
    Collect,  // have N of an item in inventory/cargo
    Mine,     // mine N of a block (tracked as it happens)
    Deliver,  // hand in N of an item at the mission computer (consumed on turn-in)
    Travel,   // reach a location
    Scan,     // scan something — target grammar in ScanTargets (#1205)
    Build,    // place blocks (#1116)
    Defeat,   // drive off N foes / clear a bandit camp (event-tracked, system missions only)
    Contribute, // hand N of an item to a shared build — today the relay network (#1213)
}

public enum MissionStatus
{
    Available,
    Active,
    Completed,
    TurnedIn,
}

/// <summary>Where a mission came from (technical requirements / `anf_mission_editor.md`).</summary>
public enum MissionSource
{
    System,
    Player,
    Admin,
}

/// <summary>A single objective within a mission.</summary>
public sealed class MissionObjective
{
    public MissionObjectiveType Type { get; set; } = MissionObjectiveType.Collect;

    /// <summary>Item key / block key / location id depending on <see cref="Type"/>; for
    /// <see cref="MissionObjectiveType.Scan"/> a <see cref="ScanTargets"/> expression.</summary>
    public string Target { get; set; } = string.Empty;

    public int Required { get; set; } = 1;

    /// <summary>Scan objectives (#1205): when true only FIRST-TIME scans (a new Codex discovery) advance the
    /// count — "discover two plant species"; when false (the default) every matching scan counts, so a kid can
    /// finish "survey the wildlife" on the herd next to the village without hunting for new species.</summary>
    public bool FirstOnly { get; set; }
}

/// <summary>
/// A mission definition. System/admin missions use localization keys; player-created
/// missions use free-text title/description (user content, not localized).
/// </summary>
public sealed class MissionDefinition
{
    public string Id { get; set; } = string.Empty;

    public MissionSource Source { get; set; } = MissionSource.System;

    /// <summary>Localization keys (system/admin missions).</summary>
    public string NameKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;

    /// <summary>Free text (player-created missions).</summary>
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Player id of the creator (player/admin missions).</summary>
    public string? CreatorId { get; set; }

    /// <summary>Name of the mission-giver NPC offering this (item 13) — shown as "Mission from {GiverName}".
    /// Empty for non-board missions.</summary>
    public string GiverName { get; set; } = string.Empty;

    public List<MissionObjective> Objectives { get; set; } = new();

    /// <summary>Reward paid to the player who completes and turns the mission in.</summary>
    public List<ItemAmount> Rewards { get; set; } = new();

    /// <summary>Knowledge points paid on turn-in besides the items (#1205) — small (2–4) and used by the scan
    /// (survey) missions, so research-minded players get research currency for research work. 0 = none.</summary>
    public int KnowledgeReward { get; set; }

    public bool Repeatable { get; set; }

    /// <summary>Whether the mission is currently offered on the board.</summary>
    public bool Active { get; set; } = true;

    // ---- Mission chains (#1212) — all additive; a stand-alone mission leaves them empty. See MissionChains. ----

    /// <summary>Groups the steps of one chain; empty = a stand-alone mission.</summary>
    public string ChainId { get; set; } = string.Empty;

    /// <summary>1-based step inside <see cref="ChainId"/> (0 = stand-alone). Two definitions sharing ChainId AND
    /// Step are ALTERNATIVES — the server offers the first feasible one (lowest id).</summary>
    public int Step { get; set; }

    /// <summary>Mission ids that must be turned in before this one is offered.</summary>
    public List<string> Prerequisites { get; set; } = new();

    /// <summary>The step that follows (informational — the giver's radio nudge looks for it); empty at the end.</summary>
    public string NextMissionId { get; set; } = string.Empty;

    /// <summary>Who hands the step out: quartermaster (default) | vendor | settler | character:&lt;id&gt;.</summary>
    public string GiverRole { get; set; } = string.Empty;

    /// <summary>Minimum relationship stage with the giver: "" | known | trusted.</summary>
    public string MinStage { get; set; } = string.Empty;

    /// <summary>Where the step surfaces: board (default — at a mission board) | dialog (handed out in
    /// conversation through the <c>mission:&lt;id&gt;</c> consequence) | radio (offered anywhere).</summary>
    public string Surface { get; set; } = string.Empty;

    /// <summary>Which kind of place offers a board step: settlement (default) | station | any.</summary>
    public string OfferAt { get; set; } = string.Empty;

    /// <summary>Story flag the world must have reached before this mission is offered at all (#1213):
    /// "" (default — no gate) | guardian_defeated. Unlike <see cref="MinStage"/>, which is about ONE
    /// giver's opinion of the player, this is about the world: the SPS survey orders only make sense
    /// once the Guardian is down and the relay network is the thing left to build.</summary>
    public string RequiresStory { get; set; } = string.Empty;
}

/// <summary>Per-player progress on an accepted mission.</summary>
public sealed class MissionProgress
{
    public string MissionId { get; set; } = string.Empty;
    public MissionStatus Status { get; set; } = MissionStatus.Active;

    /// <summary>Progress per objective index (parallel to the definition's objectives).</summary>
    public List<int> ObjectiveProgress { get; set; } = new();

    /// <summary>Chain membership copied from the definition at accept time (#1212); empty for stand-alone missions.</summary>
    public string ChainId { get; set; } = string.Empty;

    /// <summary>The place (board location key, <c>settle_&lt;hash&gt;</c> / <c>station_&lt;hash&gt;</c>) a chain step
    /// was taken at — the chain's later steps are offered and turned in there. Empty for stand-alone missions.</summary>
    public string AcceptedFrom { get; set; } = string.Empty;

    /// <summary>The body the player was on when a chain step was taken — drives the relative
    /// <see cref="MissionChains.TravelOtherBody"/> travel target. Empty for stand-alone missions.</summary>
    public string AcceptedBodyId { get; set; } = string.Empty;

    /// <summary>The star system the step was taken in, resolved by the server at accept time (#1291) —
    /// drives the relative <see cref="MissionChains.TravelUnlinkedSystem"/> travel target. It cannot be
    /// derived from <see cref="AcceptedBodyId"/>, because a station board's location id is
    /// <c>station:&lt;id&gt;</c>, which the galaxy's body lookup does not resolve. Empty for stand-alone
    /// missions and for rows written before this field existed (those keep the old body-derived
    /// behaviour).</summary>
    public string AcceptedSystemId { get; set; } = string.Empty;
}
