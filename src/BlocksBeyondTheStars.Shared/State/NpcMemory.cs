// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>How much the world may call a player over the radio (#1119) — a PLAYER preference the server
/// stores, because the server initiates the calls. All = missions + hints; MissionsOnly mutes the flavour
/// hints; Off silences NPC calls entirely.</summary>
public enum NpcCallsMode
{
    All,
    MissionsOnly,
    Off,
}

/// <summary>The kind of player↔NPC interaction an NPC remembers (item 14).</summary>
public enum NpcInteractionKind
{
    Dialog,
    Trade,
    MissionAccepted,
}

/// <summary>One remembered interaction in an NPC's log of a player.</summary>
public sealed class NpcInteraction
{
    public NpcInteractionKind Kind { get; set; }
}

/// <summary>
/// An NPC's memory of one player (item 14): a relationship score that interactions raise, plus a log of
/// the most recent interactions (capped). Stored per player keyed by a stable NPC key, so it persists and
/// feeds item 15's dialog backend (name, role, relationship, recent log).
/// </summary>
public sealed class NpcRelationship
{
    /// <summary>The NPC's coined name + role at the time of interaction (so item 15 needn't re-derive them).</summary>
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>Display name of where the NPC lives (settlement/station/base), captured at interaction time
    /// (#1118) — the "People you know" list shows it without having to reverse a location-key hash. Empty in
    /// pre-#1118 memories; refreshed on the next interaction.</summary>
    public string Place { get; set; } = string.Empty;

    /// <summary>Relationship score — interactions raise it; higher = friendlier.</summary>
    public int Value { get; set; }

    /// <summary>The most recent interactions (oldest first), capped to the last few.</summary>
    public List<NpcInteraction> Log { get; set; } = new();
}
