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

    /// <summary>
    /// Hidden POIs an NPC hint has revealed on the map, world-globally. Keys carry the location id (unlike
    /// <see cref="GeneratedLoot"/>) because coordinates repeat across a save's worlds: "{locationId}|wreck"
    /// and "{locationId}|chest:{x}:{y}:{z}". The POIs themselves re-derive from the seed every session.
    /// </summary>
    public System.Collections.Generic.List<string> RevealedPois { get; set; } = new();

    /// <summary>
    /// One-time stamp registry (#467): "{locationId}|ruins" / "|vaults" / "|wreck" once that feature's
    /// blocks were written into the world. The old in-memory guards died with the unload (a world unloads
    /// when its last player leaves), so re-entering re-ran the whole stamp chain — resurrecting mined
    /// ruins/vault/wreck blocks and carving away player builds inside the footprints. A missing entry
    /// simply means "stamp once more", so old saves migrate by stamping one final time.
    /// </summary>
    public System.Collections.Generic.List<string> StampedFeatures { get; set; } = new();

    /// <summary>
    /// Persisted structure placements (#586): where each rolled structure instance actually landed (or that
    /// it could not land), pinned at first stamp. Without this the positions re-derived from re-running the
    /// placement SEARCH on every load — which froze the search algorithm forever: any improvement would have
    /// detached existing worlds' markers/protection/NPCs from their already-stamped blocks. A missing entry
    /// means "this world predates the registry": the frozen legacy search re-derives it once, then records
    /// the outcome here (self-healing migration; monuments pioneered this pattern via StampedFeatures).
    /// </summary>
    public System.Collections.Generic.List<StructurePlacementRecord> Placements { get; set; } = new();

    /// <summary>
    /// Persisted body identity (#468): bodyId → planetType, pinned when a body is first generated. Without
    /// it the whole galaxy's types re-derived from <c>planets.json</c> on every start — ANY data edit (a
    /// new type, a weight rebalance, a reorder) silently re-typed bodies under players' buildings (it
    /// happened twice in shipped history). Bodies absent from the map (fresh saves, new systems) roll from
    /// the generator once and are frozen here (decision #1: freeze — no migration re-roll).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> BodyPlanetTypes { get; set; } = new();

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

/// <summary>Where one rolled structure instance landed (#586), pinned at first stamp so the placement search
/// can evolve without moving structures under existing worlds. <see cref="Placed"/> false records the
/// decision "no spot" permanently (legacy worlds only — the guaranteed search always finds one).</summary>
public sealed class StructurePlacementRecord
{
    public string LocationId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // settlement | factory | banditcamp | ruin | vault
    public int Index { get; set; }                    // instance index within its kind's deterministic roll order
    public bool Placed { get; set; }
    public int X { get; set; }                        // structure-local (0,0,0) world column (vaults: shaft centre)
    public int GroundY { get; set; }
    public int Z { get; set; }
    public bool OnIsland { get; set; }
    public string Seat { get; set; } = "legacy";      // seat style: legacy|flat|slope|shelf|stilts|lava|island|buried|wellhead
    public string Name { get; set; } = string.Empty;  // display name (derives from rng draws AFTER the search, so it must be pinned too)
}

/// <summary>One player claim over a spawned structure: a stable per-world key, the owner, and a display name.
/// The structure itself re-derives from the seed every session; this persisted record re-applies the claim.</summary>
public sealed class StructureClaim
{
    public string Key { get; set; } = string.Empty;       // stable per-world structure id (e.g. "loc|factory|x|y|z")
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
