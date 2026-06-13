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

/// <summary>A single persisted player block edit (placement or removal) in world space.</summary>
public readonly struct BlockEdit
{
    public readonly Vector3i WorldPosition;
    public readonly ushort Block;

    public BlockEdit(Vector3i worldPosition, ushort block)
    {
        WorldPosition = worldPosition;
        Block = block;
    }
}

/// <summary>
/// Abstraction over savegame persistence. The default implementation is SQLite-backed
/// (portable, Raspberry Pi friendly); a PostgreSQL implementation can be added later
/// without touching the game server (technical requirements §10.2, §23.3).
/// </summary>
public interface IWorldRepository : IDisposable
{
    /// <summary>The world's save folder on disk (for sidecar files like diagnostics/bump snapshots).</summary>
    string WorldDirectory { get; }

    /// <summary>Opens/creates the database and ensures the schema exists.</summary>
    void Initialize();

    WorldMetadata? LoadMetadata();
    void SaveMetadata(WorldMetadata metadata);

    /// <summary>Records a single block change (only deltas against the procedural baseline are stored).</summary>
    void SetBlock(string planet, Vector3i worldPosition, ushort block);

    /// <summary>Loads all stored block edits that fall inside the given chunk.</summary>
    IReadOnlyList<BlockEdit> LoadChunkEdits(string planet, ChunkCoord chunk);

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

    void DeleteBeacon(string planet, int x, int y, int z);

    /// <summary>Stores (inserts or replaces) a player-founded planet base, keyed by its world cell.</summary>
    void SaveBase(StoredBase basePoint);

    /// <summary>Lists all player-founded bases across every body (restored at server start).</summary>
    IReadOnlyList<StoredBase> ListAllBases();

    void DeleteBase(string planet, int x, int y, int z);

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

    /// <summary>Records the generation/discovery status of a location (system or body).</summary>
    void SetLocationStatus(string locationId, string status);

    /// <summary>Loads all stored location statuses (id → status).</summary>
    IReadOnlyDictionary<string, string> LoadLocationStatuses();

    /// <summary>Stores (inserts or replaces) a player/admin-created mission definition.</summary>
    void SaveMission(MissionDefinition mission);

    /// <summary>Lists all stored (player/admin-created) mission definitions.</summary>
    IReadOnlyList<MissionDefinition> ListMissions();

    void DeleteMission(string id);

    /// <summary>Flushes any pending writes durably to disk.</summary>
    void Flush();

    /// <summary>Creates a consistent backup copy of the world and returns its path.</summary>
    string CreateBackup(string label);
}
