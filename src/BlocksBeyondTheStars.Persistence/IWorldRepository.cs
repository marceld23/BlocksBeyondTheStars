// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>A persisted world container (storage crate, salvage capsule, ...).</summary>
public sealed class StoredContainer
{
    public string Id { get; set; } = string.Empty;
    public string Planet { get; set; } = string.Empty;
    public string Kind { get; set; } = "container";
    public Vector3i Position { get; set; }
    public List<ItemStack> Items { get; set; } = new();
}

/// <summary>A player-built door, persisted by its world cell so it survives the deterministic door rebuild.</summary>
public sealed class StoredDoor
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Kind { get; set; } = "hinge"; // "slide" | "hinge"
    public bool AxisX { get; set; }
}

/// <summary>A player-painted block design: a 32×32 pixel bitmap (palette indices as a hex string) registered
/// once per save and referenced from painted blocks by id (packed into the shape descriptor's design bits).
/// Save-global, not per planet — the id must resolve wherever the shape descriptor travels.</summary>
public sealed class StoredPaintDesign
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Display name of the designer, kept so a player copying the design off a block into their own
    /// library can credit it (#846). Empty for designs registered before that shipped.</summary>
    public string OwnerName { get; set; } = string.Empty;
    public string Pixels { get; set; } = string.Empty;
}

/// <summary>A player-designed block form (#843): a micro-voxel bitmap registered once per save and referenced
/// from blocks + items by an ordinary shape index. Save-global for the same reason a paint design is — the
/// index must resolve wherever the item or the shape descriptor travels. Unlike a design, a form carries a
/// player-chosen NAME: it is offered by name in the crafting menu, so the name is part of the record.</summary>
public sealed class StoredCustomShape
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Voxels { get; set; } = string.Empty;
}

/// <summary>A player report against a painted block or a player-designed form (moderation v1): who reported
/// what where, kept for operator review. Deliberately append-only; wiping the design does not delete its
/// reports. <see cref="Kind"/> tells the two apart ("paint" — the original rows — or "shape").</summary>
public sealed class StoredPaintReport
{
    public string ReporterId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public int DesignId { get; set; }

    /// <summary>"paint" (default, and what every pre-existing row is) or "shape".</summary>
    public string Kind { get; set; } = "paint";

    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public long CreatedUnix { get; set; }
}

/// <summary>A placed radio beacon, persisted by its world cell with its player-typed label + owner (item 37).</summary>
public sealed class StoredBeacon
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Label { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>A placed beam block (teleporter pad), persisted by its world cell with its player-typed name + owner.
/// The beam_block voxel itself comes back via the normal block-edit store; this row carries the metadata + lets
/// the player beam between their own and allied pads on the same world.</summary>
public sealed class StoredBeam
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>A player-founded planet base (Grundstein), persisted by its world cell with its player-typed name +
/// owner. The base_core block itself comes back via the normal block-edit store; this row carries the metadata.</summary>
public sealed class StoredBase
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
}

/// <summary>A persisted alliance between two players (server-wide, not per-world). Pairwise + mutual: the two
/// ids are stored normalised (<see cref="PlayerA"/> sorts before <see cref="PlayerB"/>) so each pair is one row.
/// Allied players co-own each other's stations + bases and cannot harm one another. <see cref="FormedUtc"/> is an
/// ISO-8601 timestamp shown as "allied since" in the menu.</summary>
public sealed class StoredAlliance
{
    public string PlayerA { get; set; } = string.Empty;
    public string PlayerB { get; set; } = string.Empty;
    public string FormedUtc { get; set; } = string.Empty;
}

/// <summary>The persisted per-save state of one active story pack (server-wide, like the alliance graph —
/// not per-world): the progress counters, how far the ordered beat arc has been revealed, the finale flags,
/// and the set of net fragments already found (dedupe). Stored as one JSON-blob row keyed by
/// <see cref="StoryId"/>, mirroring the player/ship/metadata blobs. Per-player "seen beats" live in the
/// player blob, not here.</summary>
public sealed class StoredStoryState
{
    public string StoryId { get; set; } = string.Empty;
    public int FragmentsFound { get; set; }
    public int MachineKills { get; set; }
    public int Milestones { get; set; }
    public int BeatsRevealed { get; set; }
    public bool GuardianSystemRevealed { get; set; }
    public bool GuardianDefeated { get; set; }
    public List<string> FoundFragmentKeys { get; set; } = new();
}

/// <summary>A persisted player-built space station (item 20 S4): its voxel cells + registry row (owner, name,
/// the body it orbits, flight-scene position). Reappears on the star map + boardable across sessions.</summary>
public sealed class StoredSpaceStructure
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>The body id whose space instance this station floats in (e.g. "sys0-p1").</summary>
    public string Location { get; set; } = string.Empty;
    public float PosX { get; set; }
    public float PosY { get; set; }
    public float PosZ { get; set; }
    public bool Boardable { get; set; }

    /// <summary>The voxel grid, serialized as "x:y:z:block" cells joined by ';'.</summary>
    public string Blocks { get; set; } = string.Empty;
}

/// <summary>A single persisted player block edit (placement or removal) in world space, with its
/// optional per-voxel colour modifier (dyed surface tint / glow light colour; 0 = none) and its packed
/// shape descriptor (non-cube building form + orientation; 0 = plain cube).</summary>
public readonly struct BlockEdit
{
    public readonly Vector3i WorldPosition;
    public readonly ushort Block;
    public readonly int Tint;
    public readonly int Glow;
    public readonly int Shape;

    /// <summary>Player id of the <b>last</b> editor of this cell, or empty when unknown (server-internal write,
    /// or a cell edited before attribution existed — issue #490). The table is keyed by cell and updated in
    /// place, so this is deliberately "who changed it last", not a history: griefing is by definition the most
    /// recent edit, which is the question the data has to answer.</summary>
    public readonly string Owner;

    /// <summary>When the last edit happened (UTC), or null when unknown.</summary>
    public readonly DateTime? EditedUtc;

    public BlockEdit(Vector3i worldPosition, ushort block, int tint = 0, int glow = 0, int shape = 0,
        string owner = "", DateTime? editedUtc = null)
    {
        WorldPosition = worldPosition;
        Block = block;
        Tint = tint;
        Glow = glow;
        Shape = shape;
        Owner = owner ?? string.Empty;
        EditedUtc = editedUtc;
    }
}

/// <summary>A scheduled surface-flora regrowth: a harvested plant that returns on its cell after a delay,
/// as long as its host block stays intact. Persisted so the regrow survives a server restart — otherwise a
/// harvest-then-restart removes the plant for good (the harvest leaves a persisted air edit that overrides
/// the procedural baseline, and the in-memory regrow timer that would bring it back is lost).</summary>
public readonly struct StoredFloraRegrow
{
    public readonly Vector3i WorldPosition;
    public readonly ushort Block;
    public readonly double Timer;

    public StoredFloraRegrow(Vector3i worldPosition, ushort block, double timer)
    {
        WorldPosition = worldPosition;
        Block = block;
        Timer = timer;
    }
}

/// <summary>A block the WEATHER laid down (#900) — snow settling during a blizzard — with the warm seconds it
/// has left before it melts. Tracked in its own table rather than as a plain block edit for one reason: the
/// melt pass must be able to tell weather snow from snow a PLAYER placed, and only ever remove its own.
/// Persisted so a restart doesn't strand cells that could then never melt.</summary>
public readonly struct StoredWeatherDeposit
{
    public readonly Vector3i WorldPosition;
    public readonly ushort Block;
    public readonly double Timer;

    public StoredWeatherDeposit(Vector3i worldPosition, ushort block, double timer)
    {
        WorldPosition = worldPosition;
        Block = block;
        Timer = timer;
    }
}

/// <summary>A persisted <b>flowing</b> fluid cell: its level (1..8) and whether it was filled from above (feeds
/// a waterfall column). Sources are deliberately NOT stored — an untracked fluid block IS a source by definition.
/// Persisted because the fluid block itself survives a restart as a block edit while the in-memory level table
/// would not: without this row every flowing tongue reloads as untracked, i.e. as a permanent full source that
/// can never dry up (#657).</summary>
public readonly struct StoredFluidCell
{
    public readonly Vector3i WorldPosition;
    public readonly byte Level;
    public readonly bool Falling;

    public StoredFluidCell(Vector3i worldPosition, byte level, bool falling)
    {
        WorldPosition = worldPosition;
        Level = level;
        Falling = falling;
    }
}

/// <summary>A persisted <b>burning</b> cell: how much burn time it has left and how many hops of spread it is
/// from the fire's origin. Persisted for the same reason fluid levels are (#784): the <c>fire</c> block itself
/// survives a restart as a block edit, so without this row a burning cell reloads untracked — a permanent,
/// inert flame that never turns to ash yet still burns whoever stands in it.</summary>
public readonly struct StoredFireCell
{
    public readonly Vector3i WorldPosition;
    public readonly double Remaining;
    public readonly int Generation;

    public StoredFireCell(Vector3i worldPosition, double remaining, int generation)
    {
        WorldPosition = worldPosition;
        Remaining = remaining;
        Generation = generation;
    }
}

/// <summary>
/// Abstraction over savegame persistence. SQLite remains the portable default; PostgreSQL is available
/// for hosted dedicated servers that need managed storage and easier cloud operations.
/// </summary>
public interface IWorldRepository : IDisposable
{
    /// <summary>The world's save folder on disk (for sidecar files like diagnostics/bump snapshots).</summary>
    string WorldDirectory { get; }

    /// <summary>Opens/creates the database and ensures the schema exists.</summary>
    void Initialize();

    /// <summary>Records the current block-id palette (numeric id → block key) and, if a stored palette from an
    /// earlier content set is present and differs, remaps every persisted numeric block id (block edits,
    /// structure edits, flora regrowth, stored space structures) to the current assignment BEFORE any world
    /// loads. Call once at startup after <see cref="Initialize"/>. This is what stops a content update that
    /// inserts a block — which shifts the sort-order-assigned ids — from silently decoding every existing
    /// save's edits to the wrong blocks. A block key no longer in content maps to air (0).</summary>
    void EnsureBlockPalette(IReadOnlyDictionary<ushort, string> currentPalette);

    WorldMetadata? LoadMetadata();
    void SaveMetadata(WorldMetadata metadata);

    /// <summary>Records a single block change (only deltas against the procedural baseline are stored).
    /// <paramref name="tint"/>/<paramref name="glow"/> carry the optional per-voxel colour modifier (0 = none);
    /// <paramref name="shape"/> the packed non-cube shape descriptor (0 = plain cube).
    /// <paramref name="owner"/> is the player who made the change (empty for server-internal writes like
    /// worldgen stamps or flora regrowth) — see <see cref="BlockEdit.Owner"/> for the "last editor wins"
    /// semantics this stores.</summary>
    void SetBlock(string planet, Vector3i worldPosition, ushort block, int tint = 0, int glow = 0, int shape = 0, string owner = "");

    /// <summary>Attribution for one cell: who last changed it and when (issue #490). Null when the cell has no
    /// stored edit at all; the owner is empty for cells edited before attribution existed or written by the
    /// server itself.</summary>
    (string Owner, DateTime? EditedUtc)? GetBlockAttribution(string planet, Vector3i worldPosition);

    /// <summary>Loads all stored block edits that fall inside the given chunk.</summary>
    IReadOnlyList<BlockEdit> LoadChunkEdits(string planet, ChunkCoord chunk);

    /// <summary>Stores (inserts or replaces) a scheduled flora regrowth, keyed by its world cell.</summary>
    void SaveFloraRegrow(string planet, Vector3i worldPosition, ushort block, double timer);

    /// <summary>Lists all scheduled flora regrowths on a planet (restored into the regrow queue on world load).</summary>
    IReadOnlyList<StoredFloraRegrow> ListFloraRegrow(string planet);

    /// <summary>Removes a scheduled flora regrowth (the plant returned, or its host was lost).</summary>
    void DeleteFloraRegrow(string planet, Vector3i worldPosition);

    /// <summary>Stores (inserts or replaces) a weather-deposited cell — snow the sky laid down (#900).</summary>
    void SaveWeatherDeposit(string planet, Vector3i worldPosition, ushort block, double timer);

    /// <summary>Lists all weather deposits on a planet (restored on world load so they can still melt).</summary>
    IReadOnlyList<StoredWeatherDeposit> ListWeatherDeposits(string planet);

    /// <summary>Removes a weather deposit (it melted, or the player mined/replaced the cell).</summary>
    void DeleteWeatherDeposit(string planet, Vector3i worldPosition);

    /// <summary>Stores (inserts or replaces) a flowing fluid cell's level state, keyed by its world cell.</summary>
    void SaveFluidCell(string planet, Vector3i worldPosition, byte level, bool falling);

    /// <summary>Lists all flowing fluid cells on a planet (restored into the fluid automaton on world load,
    /// so a restart doesn't promote them to sources).</summary>
    IReadOnlyList<StoredFluidCell> ListFluidCells(string planet);

    /// <summary>Removes a flowing fluid cell's level state (it dried up, settled into a source, or its block
    /// was replaced).</summary>
    void DeleteFluidCell(string planet, Vector3i worldPosition);

    /// <summary>Stores (inserts or replaces) a burning cell's remaining burn time + spread generation.</summary>
    void SaveFireCell(string planet, Vector3i worldPosition, double remaining, int generation);

    /// <summary>Lists all burning cells on a planet (restored into the fire automaton on world load, so a
    /// restart doesn't strand them as permanent flames).</summary>
    IReadOnlyList<StoredFireCell> ListFireCells(string planet);

    /// <summary>Removes a burning cell's state (it burned out, was doused, or its block was replaced).</summary>
    void DeleteFireCell(string planet, Vector3i worldPosition);

    PlayerState? LoadPlayer(string playerId);
    void SavePlayer(PlayerState player);
    IReadOnlyList<string> ListPlayerIds();

    ShipState? LoadShip(string shipId);
    void SaveShip(string shipId, ShipState ship);

    /// <summary>Stores (inserts or replaces) a world container.</summary>
    void SaveContainer(StoredContainer container);

    /// <summary>Lists all containers on a planet (e.g. to retrieve salvage capsules).</summary>
    IReadOnlyList<StoredContainer> ListContainers(string planet);

    void DeleteContainer(string id);

    /// <summary>Stores (inserts or replaces) a player-built door, keyed by its world cell.</summary>
    void SaveDoor(StoredDoor door);

    /// <summary>Lists all player-built doors on a planet (re-added after the generated doors on load).</summary>
    IReadOnlyList<StoredDoor> ListDoors(string planet);

    void DeleteDoor(string planet, int x, int y, int z);

    /// <summary>Stores (inserts or replaces) a placed radio beacon, keyed by its world cell.</summary>
    void SaveBeacon(StoredBeacon beacon);

    /// <summary>Lists all placed radio beacons on a planet (restored on world load).</summary>
    IReadOnlyList<StoredBeacon> ListBeacons(string planet);

    /// <summary>Lists placed beacons across <b>every</b> body. The per-world lists only cover the resident
    /// world; the admin build inventory (issue #488) means "everywhere", so it reads the save instead.</summary>
    IReadOnlyList<StoredBeacon> ListAllBeacons();

    void DeleteBeacon(string planet, int x, int y, int z);

    /// <summary>Stores (inserts or replaces) a paint design, keyed by its save-global id.</summary>
    void SavePaintDesign(StoredPaintDesign design);

    /// <summary>Lists every registered paint design (restored once at server start).</summary>
    IReadOnlyList<StoredPaintDesign> ListPaintDesigns();

    /// <summary>Removes a paint design (moderation wipe — every referencing block goes blank).</summary>
    void DeletePaintDesign(int id);

    /// <summary>Stores (inserts or replaces) a player-designed form, keyed by its save-global shape id.</summary>
    void SaveCustomShape(StoredCustomShape shape);

    /// <summary>Lists every registered player-designed form (restored once at server start).</summary>
    IReadOnlyList<StoredCustomShape> ListCustomShapes();

    /// <summary>Removes a player-designed form (moderation wipe — the id is freed and every referencing
    /// block falls back to a plain cube).</summary>
    void DeleteCustomShape(int id);

    /// <summary>Appends a paint report row (moderation v1) for operator review.</summary>
    void SavePaintReport(StoredPaintReport report);

    /// <summary>Lists every stored paint report.</summary>
    IReadOnlyList<StoredPaintReport> ListPaintReports();

    /// <summary>Stores (inserts or replaces) a placed beam block, keyed by its world cell.</summary>
    void SaveBeam(StoredBeam beam);

    /// <summary>Lists all placed beam blocks on a planet (restored on world load).</summary>
    IReadOnlyList<StoredBeam> ListBeams(string planet);

    /// <summary>Lists placed beam pads across <b>every</b> body — see <see cref="ListAllBeacons"/>.</summary>
    IReadOnlyList<StoredBeam> ListAllBeams();

    void DeleteBeam(string planet, int x, int y, int z);

    /// <summary>Stores (inserts or replaces) a player-founded planet base, keyed by its world cell.</summary>
    void SaveBase(StoredBase basePoint);

    /// <summary>Lists all player-founded bases across every body (restored at server start).</summary>
    IReadOnlyList<StoredBase> ListAllBases();

    void DeleteBase(string planet, int x, int y, int z);

    /// <summary>Stores (inserts or replaces) a player alliance, keyed by the normalised player-id pair.</summary>
    void SaveAlliance(StoredAlliance alliance);

    /// <summary>Lists every alliance across the server (restored once at server start).</summary>
    IReadOnlyList<StoredAlliance> ListAlliances();

    /// <summary>Removes the alliance between the two players (order-independent).</summary>
    void DeleteAlliance(string playerA, string playerB);

    /// <summary>Stores (inserts or replaces) the per-save state of one story pack, keyed by its story id.</summary>
    void SaveStoryState(StoredStoryState state);

    /// <summary>Lists every persisted story-pack state (restored once at server start).</summary>
    IReadOnlyList<StoredStoryState> ListStoryStates();

    /// <summary>Stores (inserts or replaces) a player-built space station (item 20 S4).</summary>
    void SaveSpaceStructure(StoredSpaceStructure structure);

    /// <summary>Lists all persisted player-built space stations (restored at server start).</summary>
    IReadOnlyList<StoredSpaceStructure> ListSpaceStructures();

    void DeleteSpaceStructure(string id);

    /// <summary>Records a single player edit (mine or place, incl. air) on an in-space voxel structure —
    /// the own-ship hull during an EVA. Only deltas against the deterministic baseline are stored, keyed by
    /// the structure id (e.g. "ship:&lt;playerId&gt;"), mirroring the per-cell planet block-edit model.</summary>
    void SetStructureBlock(string structureId, Vector3i position, ushort block);

    /// <summary>Loads all stored edits for an in-space voxel structure (re-applied on top of the rebuilt
    /// baseline when the structure is reconstructed on space entry / server restart).</summary>
    IReadOnlyList<BlockEdit> LoadStructureEdits(string structureId);

    /// <summary>Removes all stored edits for an in-space voxel structure (e.g. the ship hull was reset).</summary>
    void DeleteStructureEdits(string structureId);

    /// <summary>Deletes all world block edits inside an axis-aligned box (inclusive). Ship-as-object
    /// migration: pre-object saves persisted the stamped hull as block edits — placing the ship object
    /// clears that residue from its volume so the old block hull doesn't reappear.</summary>
    void DeleteBlockEdits(string planet, Vector3i min, Vector3i max);

    /// <summary>True if any block edit inside an axis-aligned box (inclusive) carries a player
    /// <see cref="BlockEdit.Owner"/> — i.e. somebody built, dyed or mined there. Server-internal writes
    /// (worldgen stamps, fluid flow, fire, flora regrowth) have no owner and do not count. Used by worldgen
    /// to keep a structure that ships in a later release off ground players already claimed (#527);
    /// <see cref="LoadChunkEdits"/> deliberately drops attribution (it runs per streamed chunk), so this
    /// question needs its own bounded query.</summary>
    bool HasPlayerBlockEdits(string planet, Vector3i min, Vector3i max);

    /// <summary>True if the location holds ANY persisted block edit, by any writer — worldgen stamps
    /// included. This is the ground truth for "was this world ever materialised before?" (#586): a world
    /// with zero edits has never had its stamp chain run, so the placement search may use the current
    /// algorithm freely; a world with edits must re-derive legacy positions so structures stay attached
    /// to their stamped blocks. Metadata markers can't answer this — saves from before the stamp registry
    /// carry none.</summary>
    bool HasAnyBlockEdits(string planet);

    /// <summary>Records the generation/discovery status of a location (system or body).</summary>
    void SetLocationStatus(string locationId, string status);

    /// <summary>Loads all stored location statuses (id → status).</summary>
    IReadOnlyDictionary<string, string> LoadLocationStatuses();

    /// <summary>Stores (inserts or replaces) a player/admin-created mission definition.</summary>
    void SaveMission(MissionDefinition mission);

    /// <summary>Lists all stored (player/admin-created) mission definitions.</summary>
    IReadOnlyList<MissionDefinition> ListMissions();

    void DeleteMission(string id);

    /// <summary>Runs <paramref name="body"/> inside a single database transaction so a burst of writes (e.g.
    /// stamping a station voxel-by-voxel, or saving every player at once) commits once instead of paying a
    /// WAL commit per row — turning a multi-second stall on the tick thread into a single commit. Reentrant:
    /// a nested call just runs the body in the already-open transaction. Rolls back if the body throws.</summary>
    void RunInTransaction(Action body);

    /// <summary>Flushes any pending writes durably to disk.</summary>
    void Flush();

    /// <summary>Creates a consistent backup copy of the world and returns its path.</summary>
    string CreateBackup(string label);
}
