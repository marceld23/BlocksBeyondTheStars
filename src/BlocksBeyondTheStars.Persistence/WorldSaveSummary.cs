// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Headline stats mirrored into the <c>world.meta.json</c> sidecar beside each save's <c>world.db</c>.
/// The authoritative copy lives in the DB's <see cref="BlocksBeyondTheStars.Shared.State.WorldMetadata"/>;
/// this lightweight file exists only so the Unity client's world-picker can show name/playtime/last-played/mode
/// without a SQLite dependency (the picker runs in the menu, before any server process is launched).
/// Field names are PascalCase so Unity's <c>JsonUtility</c> can read them into a matching struct verbatim.
/// </summary>
public sealed class WorldSaveSummary
{
    /// <summary><see cref="Mode"/> of a normal world: no Creative world option, the rules in Survival.</summary>
    public const string ModeExplorer = "explorer";

    /// <summary><see cref="Mode"/> of a head-start world: a Creative world option (all blueprints, all ships, the starter
    /// kit) is set while the survival mechanics stay on.</summary>
    public const string ModeCreative = "creative";

    /// <summary><see cref="Mode"/> of a sandbox world: the world's rules run in <see cref="GameMode.Creative"/>.</summary>
    public const string ModeSandbox = "sandbox";

    public string WorldName { get; set; } = string.Empty;
    public long PlaytimeSeconds { get; set; }

    /// <summary>ISO-8601 UTC timestamp of the last save (round-trip "o" format).</summary>
    public string LastPlayedUtc { get; set; } = string.Empty;

    /// <summary>The world's game mode for the picker (#3664): <see cref="ModeExplorer"/>, <see cref="ModeCreative"/> or
    /// <see cref="ModeSandbox"/>, by <see cref="ModeOf"/>. Absent from sidecars written before #3664.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>The <see cref="Mode"/> of a world (#3664) — the one rule both repositories write: <see cref="ModeSandbox"/>
    /// when the world's rules run in <see cref="GameMode.Creative"/>; else <see cref="ModeCreative"/> when a Creative world
    /// option is set (<see cref="WorldMetadata.CreativeUnlockAllBlueprints"/>, <see cref="WorldMetadata.CreativeStartAllShips"/>,
    /// <see cref="WorldMetadata.CreativeStarterKit"/>); else <see cref="ModeExplorer"/>.</summary>
    public static string ModeOf(WorldMetadata metadata)
    {
        if (metadata.RulesOverride?.GameMode == GameMode.Creative)
        {
            return ModeSandbox;
        }

        return metadata.CreativeUnlockAllBlueprints || metadata.CreativeStartAllShips || metadata.CreativeStarterKit
            ? ModeCreative
            : ModeExplorer;
    }
}
