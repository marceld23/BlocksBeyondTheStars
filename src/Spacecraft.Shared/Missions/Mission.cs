using Spacecraft.Shared.Definitions;

namespace Spacecraft.Shared.Missions;

/// <summary>Server-validatable objective kinds. MVP fully supports Mine, Collect and Deliver.</summary>
public enum MissionObjectiveType
{
    Collect,  // have N of an item in inventory/cargo
    Mine,     // mine N of a block (tracked as it happens)
    Deliver,  // hand in N of an item at the mission computer (consumed on turn-in)
    Travel,   // reach a location (later)
    Scan,     // scan an object (later)
    Build,    // place a structure/module (later)
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

    /// <summary>Item key / block key / location id depending on <see cref="Type"/>.</summary>
    public string Target { get; set; } = string.Empty;

    public int Required { get; set; } = 1;
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

    public List<MissionObjective> Objectives { get; set; } = new();

    /// <summary>Reward paid to the player who completes and turns the mission in.</summary>
    public List<ItemAmount> Rewards { get; set; } = new();

    public bool Repeatable { get; set; }

    /// <summary>Whether the mission is currently offered on the board.</summary>
    public bool Active { get; set; } = true;
}

/// <summary>Per-player progress on an accepted mission.</summary>
public sealed class MissionProgress
{
    public string MissionId { get; set; } = string.Empty;
    public MissionStatus Status { get; set; } = MissionStatus.Active;

    /// <summary>Progress per objective index (parallel to the definition's objectives).</summary>
    public List<int> ObjectiveProgress { get; set; } = new();
}
