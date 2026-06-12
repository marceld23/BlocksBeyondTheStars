using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.State;

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

    /// <summary>Relationship score — interactions raise it; higher = friendlier.</summary>
    public int Value { get; set; }

    /// <summary>The most recent interactions (oldest first), capped to the last few.</summary>
    public List<NpcInteraction> Log { get; set; } = new();
}
