// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

public sealed class RequestMissions { }

public sealed class AcceptMissionIntent
{
    public string MissionId { get; set; } = string.Empty;
}

public sealed class TurnInMissionIntent
{
    public string MissionId { get; set; } = string.Empty;
}

public sealed class NetMissionObjective
{
    public string Type { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Required { get; set; }
    public int Progress { get; set; }
}

public sealed class NetReward
{
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>Client-supplied mission when a player creates a mission for others.</summary>
public sealed class CreateMissionIntent
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NetMissionObjective[] Objectives { get; set; } = System.Array.Empty<NetMissionObjective>();
    public NetReward[] Rewards { get; set; } = System.Array.Empty<NetReward>();
}

public sealed class NetMission
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public NetMissionObjective[] Objectives { get; set; } = System.Array.Empty<NetMissionObjective>();
    public NetReward[] Rewards { get; set; } = System.Array.Empty<NetReward>();

    /// <summary>Knowledge points paid on turn-in besides the item rewards (#1205 scan missions); 0 = none.</summary>
    public int KnowledgeReward { get; set; }

    /// <summary>Mission chains (#1212): 1-based step of this mission inside its chain and the chain's length
    /// ("Part 2 of 4"); both 0 for a stand-alone mission.</summary>
    public int ChainStep { get; set; }
    public int ChainLength { get; set; }

    /// <summary>Mission-giver NPC's name (item 13) — shown as "Mission from {GiverName}"; empty if none.</summary>
    public string GiverName { get; set; } = string.Empty;

    /// <summary>True when <see cref="Title"/>/<see cref="Description"/> are display TEXT (player missions,
    /// L3 LLM board texts) rather than locale keys — the client renders them verbatim then.</summary>
    public bool FreeText { get; set; }

    /// <summary>#2714: where a villager's wish is handed over — the giver's village (display text, never a key); empty for
    /// every other mission and for a wish whose village the server has not seen yet. Additive (contractless).</summary>
    public string PlaceName { get; set; } = string.Empty;

    /// <summary>#2714: the id of the <see cref="NetMarker"/> (<see cref="NetMarker.Wish"/>) that marks <see cref="PlaceName"/>
    /// on the map. The player's marker list holds it only while they are on the village's body — the client takes the
    /// distance and "Navigate" from it then. Additive.</summary>
    public string PlaceMarkerId { get; set; } = string.Empty;
}

public sealed class MissionList
{
    public NetMission[] Available { get; set; } = System.Array.Empty<NetMission>();
    public NetMission[] Active { get; set; } = System.Array.Empty<NetMission>();
}

public sealed class MissionResult
{
    public bool Success { get; set; }
    public string MissionId { get; set; } = string.Empty;
    public string? Reason { get; set; }

    /// <summary>What succeeded (#2462, B24) — one of <see cref="MissionOutcomes"/>: the mission was accepted, turned in
    /// or posted. Empty from an older server, which sent the same message for all three; the client then says
    /// neutrally that the mission was updated. Additive (contractless).</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The mission's title for the toast, by the same rule as <see cref="NetMission.Title"/>: a locale key,
    /// or display text when <see cref="FreeText"/> (player missions, generated board texts). Additive.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>True when <see cref="Title"/> is display text rather than a locale key. Additive.</summary>
    public bool FreeText { get; set; }
}

/// <summary>The values of <see cref="MissionResult.Outcome"/> (#2462).</summary>
public static class MissionOutcomes
{
    public const string Accepted = "accepted";
    public const string Completed = "completed";
    public const string Posted = "posted";
}
