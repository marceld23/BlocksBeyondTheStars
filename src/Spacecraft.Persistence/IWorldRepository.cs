using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Spacecraft.Shared.State;
using Spacecraft.Shared.World;

namespace Spacecraft.Persistence;

/// <summary>A persisted world container (storage crate, salvage capsule, ...).</summary>
public sealed class StoredContainer
{
    public string Id { get; set; } = string.Empty;
    public string Planet { get; set; } = string.Empty;
    public string Kind { get; set; } = "container";
    public Vector3i Position { get; set; }
    public List<ItemStack> Items { get; set; } = new();
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

    /// <summary>Records the generation/discovery status of a location (system or body).</summary>
    void SetLocationStatus(string locationId, string status);

    /// <summary>Loads all stored location statuses (id → status).</summary>
    IReadOnlyDictionary<string, string> LoadLocationStatuses();

    /// <summary>Stores (inserts or replaces) a player/admin-created mission definition.</summary>
    void SaveMission(MissionDefinition mission);

    /// <summary>Lists all stored (player/admin-created) mission definitions.</summary>
    IReadOnlyList<MissionDefinition> ListMissions();

    void DeleteMission(string id);

    /// <summary>Stores (inserts or replaces) a player's personal landing zone.</summary>
    void SaveLandingZone(LandingZone zone);

    /// <summary>Lists all landing zones on a location.</summary>
    IReadOnlyList<LandingZone> ListLandingZones(string locationId);

    /// <summary>Flushes any pending writes durably to disk.</summary>
    void Flush();

    /// <summary>Creates a consistent backup copy of the world and returns its path.</summary>
    string CreateBackup(string label);
}
