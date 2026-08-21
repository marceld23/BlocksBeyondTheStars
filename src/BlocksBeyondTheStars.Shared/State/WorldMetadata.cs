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

    /// <summary>
    /// VEGA's relay-network insight stages already spoken ("relay" / "lane" / "growth", F-2 of #1125) —
    /// each epilogue insight plays exactly once per save. Additive JSON field, no migration.
    /// </summary>
    public System.Collections.Generic.List<string> RelayInsights { get; set; } = new();

    /// <summary>
    /// Pinned station interiors (#1115): station id → template key ("" = procedurally generated), written
    /// at the station's FIRST interior stamp. Replays use the pin, so a growing template pool never morphs
    /// an already-boarded station's interior under its persisted blocks. Additive JSON field; a station
    /// absent from the map replays against the legacy pool (the pre-#1115 behaviour, draw-for-draw).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> StationTemplates { get; set; } = new();

    /// <summary>
    /// Growing galaxy (#1123): how many systems were appended BEYOND the description's
    /// <c>StarSystemCount</c> by frontier jumps. The galaxy is re-derived from the seed on every start,
    /// so persisting the COUNT is enough — system N is a pure function of (seed, N), and regenerating
    /// with count + grown reproduces every grown system byte-identically. 0 (absent in older saves) means
    /// the galaxy is exactly the description's fixed one.
    /// </summary>
    public int GalaxyGrownSystems { get; set; }

    /// <summary>
    /// SPS relay upgrades (#1125, Track F): per commissioned player station, what has been contributed
    /// toward its relay conversion and whether it is complete. World-shared (co-op contributable) and tiny,
    /// so it lives in the metadata blob — an additive JSON field, no migration. Jump lanes are NOT stored:
    /// they re-derive from the completed relays' systems + the data-driven link range on every start.
    /// </summary>
    public System.Collections.Generic.List<RelayStationRecord> Relays { get; set; } = new();

    /// <summary>
    /// True for worlds created after ships became placed objects (#870): such a save never persisted a
    /// stamped hull, so the legacy stamp-residue cleanup must never run on it (it would delete the player's
    /// own builds beside a pad on their first landing there). False (missing) on older saves — they migrate
    /// via <see cref="ShipResidueCleaned"/>.
    /// </summary>
    public bool CreatedWithShipObjects { get; set; }

    /// <summary>
    /// One-time ship-stamp residue cleanups already performed on pre-object saves (#870):
    /// "{locationId}|shipresidue:{padX}:{padZ}" once the legacy stamped-hull migration ran for that pad.
    /// The cleanup used to run on EVERY ship placement (join, respawn, landing, ship switch), deleting all
    /// persisted block edits in a box around the parked ship — wiping the player's own builds beside their
    /// pad on each rejoin ("singleplayer doesn't save"). A missing entry means "clean once more", so
    /// pre-object saves migrate by cleaning one final time (the <see cref="StampedFeatures"/> pattern).
    /// </summary>
    public System.Collections.Generic.List<string> ShipResidueCleaned { get; set; } = new();

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

    /// <summary>The hand-designed template this instance was stamped from (#1115), "" for procedural — or
    /// for records from before template pinning existed, which replay against the LEGACY template pool so
    /// a growing pool never morphs their layout under the stamped blocks.</summary>
    public string Template { get; set; } = string.Empty;
}

/// <summary>One player station's SPS relay conversion (#1125): what has been poured into it so far, and
/// whether it is done. Keyed by the station's persisted id ("pstation:…"); contributions are per item key.
/// The station build itself lives in its own repository row — this records only the relay meter.</summary>
public sealed class RelayStationRecord
{
    public string StationId { get; set; } = string.Empty;

    /// <summary>Item key → amount contributed so far (clamped to the definition's required amounts).</summary>
    public System.Collections.Generic.Dictionary<string, int> Contributed { get; set; } = new();

    /// <summary>True once every cost line is fully contributed — the station IS a relay from then on.</summary>
    public bool Completed { get; set; }
}

/// <summary>One player claim over a spawned structure: a stable per-world key, the owner, and a display name.
/// The structure itself re-derives from the seed every session; this persisted record re-applies the claim.</summary>
public sealed class StructureClaim
{
    public string Key { get; set; } = string.Empty;       // stable per-world structure id (e.g. "loc|factory|x|y|z")
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
