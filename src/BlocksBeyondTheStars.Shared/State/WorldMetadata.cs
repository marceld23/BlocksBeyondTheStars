// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// Top-level, rarely changing world parameters. Combined with player deltas this fully
/// describes a save: the procedural baseline is regenerated from <see cref="Seed"/>.
/// </summary>
public sealed class WorldMetadata
{
    public string WorldName { get; set; } = "New World";

    /// <summary>Master world seed driving all procedural generation.</summary>
    public long Seed { get; set; }

    /// <summary>Planet type key the player starts on / the active surface for the MVP.</summary>
    public string DefaultPlanetType { get; set; } = "rocky";

    /// <summary>Logical id of the currently active planet/location.</summary>
    public string ActiveLocationId { get; set; } = "rocky";

    /// <summary>Schema/content version for future migrations.</summary>
    public int SaveVersion { get; set; } = 1;

    /// <summary>Total wall-clock seconds this world has been actively played (accumulated server-side only
    /// while at least one player is joined, so an idle dedicated server doesn't inflate it). Shown in the HUD
    /// and the save picker. 0 on saves from before playtime tracking existed.</summary>
    public long CumulativePlaytimeSeconds { get; set; }

    /// <summary>Admin-defined universe description; combined with the seed it yields the galaxy.</summary>
    public WorldDescription Description { get; set; } = new();

    /// <summary>
    /// Keys of structure loot markers (wreck/ruin caches) already turned into containers, so they
    /// aren't re-spawned on reload — even after the container has been looted and removed.
    /// </summary>
    public System.Collections.Generic.List<string> GeneratedLoot { get; set; } = new();

    /// <summary>
    /// Player claims over spawned structures (factories): each maps a structure's stable per-world key to its
    /// owner. A claimed structure becomes an editable player base for the owner + their allies. Founded by
    /// consuming an access code at the structure. Persisted (the structures themselves re-derive from the seed).
    /// </summary>
    public System.Collections.Generic.List<StructureClaim> Claims { get; set; } = new();

    // --- Singleplayer "Creative" world options (chosen at creation; persisted so they reapply on every load).
    // A head-start sandbox: everything available + a starter set, while survival mechanics stay on. All false =
    // the normal "Explorer" world. Blueprints + ships are re-applied per join (idempotent); the kit is one-time. ---
    public bool CreativeUnlockAllBlueprints { get; set; }
    public bool CreativeStartAllShips { get; set; }
    public bool CreativeStarterKit { get; set; }

    /// <summary>True once the one-time creative starter kit has been granted, so reloads don't refill it.</summary>
    public bool CreativeKitGranted { get; set; }

    /// <summary>
    /// World rules chosen at creation (world options) and updated by in-game admin edits — the world
    /// OWNS its rules once created: on load this replaces the launch config's rules, so singleplayer
    /// relaunches (which pass creation options only once) and dedicated restarts keep the chosen set.
    /// Null on saves from before world options existed (the launch config's rules apply then).
    /// </summary>
    public BlocksBeyondTheStars.Shared.Configuration.GameRules? RulesOverride { get; set; }
}

/// <summary>One player claim over a spawned structure: a stable per-world key, the owner, and a display name.
/// The structure itself re-derives from the seed every session; this persisted record re-applies the claim.</summary>
public sealed class StructureClaim
{
    public string Key { get; set; } = string.Empty;       // stable per-world structure id (e.g. "loc|factory|x|y|z")
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
