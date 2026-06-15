using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Persistence;

// Serialization-friendly DTOs that decouple the JSON-on-disk shape from the runtime
// state types. The repository stores players/ship as JSON blobs built from these.

public sealed class InventorySlotDto
{
    public int Index { get; set; }
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class PlayerSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float RespawnX { get; set; }
    public float RespawnY { get; set; }
    public float RespawnZ { get; set; }
    public float Health { get; set; } = 100f;
    public float Oxygen { get; set; } = 100f;
    public float SuitEnergy { get; set; } = 100f;
    public float Hunger { get; set; } = 100f;
    public int SelectedHotbarSlot { get; set; }
    public bool AboardShip { get; set; } = true;
    public string CurrentLocationId { get; set; } = string.Empty; // the body the player was last on (load returns here)
    public string Role { get; set; } = "Player";
    public string NameTokenHash { get; set; } = string.Empty;
    public int InventorySlotCount { get; set; } = 24;
    public List<string> UnlockedBlueprints { get; set; } = new();
    public int KnowledgePoints { get; set; }
    public Dictionary<string, int> KnowledgeGivenTo { get; set; } = new();
    public Dictionary<string, NpcRelationship> NpcMemory { get; set; } = new();
    public List<string> Scanned { get; set; } = new();
    public List<InventorySlotDto> RationStore { get; set; } = new();
    public List<InventorySlotDto> Inventory { get; set; } = new();
    public List<MissionProgress> Missions { get; set; } = new();
    public List<string> Milestones { get; set; } = new();
    public List<string> UnlockedGames { get; set; } = new();

    /// <summary>The player's custom pixel face (16×16 palette indices as a hex string; empty = none). Opaque
    /// to the server; the client owns the palette + rendering. Persisted so the face follows the player.</summary>
    public string FacePixels { get; set; } = string.Empty;

    /// <summary>Celestial bodies this player has physically landed on (gates travel-screen quick-travel). Persisted
    /// so the "only travel where you've been" rule survives a reload.</summary>
    public List<string> LandedBodies { get; set; } = new();

    /// <summary>Star systems this player has entered (reveals their bodies + mini map on the travel screen).</summary>
    public List<string> KnownSystems { get; set; } = new();

    /// <summary>Tamed creatures (companions) — named, bound to their home world. Persisted so they survive a
    /// reload (the wild fauna they came from does not — it is regenerated per visit).</summary>
    public List<TamedCreature> TamedCreatures { get; set; } = new();

    /// <summary>Species already tamed once (first-tame knowledge bookkeeping; signature "&lt;body&gt;:&lt;sp&gt;").</summary>
    public List<string> TamedSpecies { get; set; } = new();
}

public sealed class ShipSnapshot
{
    public List<string> Modules { get; set; } = new();
    public string CurrentLocationId { get; set; } = string.Empty;
    public int CargoSlotCount { get; set; } = 48;
    public List<InventorySlotDto> Cargo { get; set; } = new();
    public float Hull { get; set; } = 100f;
    public float Shield { get; set; }
    public string ShipType { get; set; } = "starter";
}

/// <summary>Maps between runtime state objects and their persisted snapshots.</summary>
public static class StateMapper
{
    private static List<InventorySlotDto> DumpInventory(Inventory inv)
    {
        var list = new List<InventorySlotDto>();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { } s && !s.IsEmpty)
            {
                list.Add(new InventorySlotDto { Index = i, Item = s.Item, Count = s.Count });
            }
        }

        return list;
    }

    private static Inventory RestoreInventory(int slotCount, List<InventorySlotDto> slots)
    {
        var inv = new Inventory(slotCount);
        foreach (var dto in slots)
        {
            if (dto.Index >= 0 && dto.Index < slotCount && dto.Count > 0)
            {
                inv.SetSlot(dto.Index, new ItemStack(dto.Item, dto.Count));
            }
        }

        return inv;
    }

    public static PlayerSnapshot ToSnapshot(PlayerState p) => new()
    {
        Id = p.PlayerId,
        Name = p.Name,
        X = p.Position.X,
        Y = p.Position.Y,
        Z = p.Position.Z,
        Yaw = p.Yaw,
        Pitch = p.Pitch,
        RespawnX = p.RespawnPoint.X,
        RespawnY = p.RespawnPoint.Y,
        RespawnZ = p.RespawnPoint.Z,
        Health = p.Health,
        Oxygen = p.Oxygen,
        SuitEnergy = p.SuitEnergy,
        Hunger = p.Hunger,
        SelectedHotbarSlot = p.SelectedHotbarSlot,
        AboardShip = p.AboardShip,
        CurrentLocationId = p.CurrentLocationId,
        Role = p.Role.ToString(),
        NameTokenHash = p.NameTokenHash,
        InventorySlotCount = p.Inventory.SlotCount,
        UnlockedBlueprints = p.UnlockedBlueprints.ToList(),
        KnowledgePoints = p.KnowledgePoints,
        KnowledgeGivenTo = new Dictionary<string, int>(p.KnowledgeGivenTo),
        NpcMemory = CloneNpcMemory(p.NpcMemory),
        Scanned = p.Scanned.ToList(),
        RationStore = DumpInventory(p.RationStore),
        Inventory = DumpInventory(p.Inventory),
        Missions = p.Missions.Select(CloneProgress).ToList(),
        Milestones = p.Milestones.ToList(),
        UnlockedGames = p.UnlockedGames.ToList(),
        LandedBodies = p.LandedBodies.ToList(),
        KnownSystems = p.KnownSystems.ToList(),
        TamedCreatures = p.TamedCreatures.Select(CloneTamed).ToList(),
        TamedSpecies = p.TamedSpecies.ToList(),
        FacePixels = p.FacePixels,
    };

    /// <summary>Copies a tamed creature so a snapshot doesn't alias the live list. The species descriptor is
    /// treated as immutable once tamed, so the reference is shared (it is never mutated in place).</summary>
    private static TamedCreature CloneTamed(TamedCreature t) => new()
    {
        Id = t.Id,
        HomeBodyId = t.HomeBodyId,
        Name = t.Name,
        SpeciesId = t.SpeciesId,
        Species = t.Species,
        SizeScale = t.SizeScale,
        Bond = t.Bond,
        TamedAtUtc = t.TamedAtUtc,
    };

    private static MissionProgress CloneProgress(MissionProgress m) => new()
    {
        MissionId = m.MissionId,
        Status = m.Status,
        ObjectiveProgress = new List<int>(m.ObjectiveProgress),
    };

    /// <summary>Deep-clones the per-NPC memory (item 14) so a snapshot doesn't alias the live state.</summary>
    private static Dictionary<string, NpcRelationship> CloneNpcMemory(Dictionary<string, NpcRelationship>? memory)
    {
        var clone = new Dictionary<string, NpcRelationship>();
        if (memory is null)
        {
            return clone;
        }

        foreach (var (key, rel) in memory)
        {
            clone[key] = new NpcRelationship
            {
                Name = rel.Name,
                Role = rel.Role,
                Value = rel.Value,
                Log = rel.Log.Select(i => new NpcInteraction { Kind = i.Kind }).ToList(),
            };
        }

        return clone;
    }

    public static PlayerState FromSnapshot(PlayerSnapshot s) => new()
    {
        PlayerId = s.Id,
        Name = s.Name,
        Position = new Vector3f(s.X, s.Y, s.Z),
        Yaw = s.Yaw,
        Pitch = s.Pitch,
        RespawnPoint = new Vector3f(s.RespawnX, s.RespawnY, s.RespawnZ),
        Health = s.Health,
        Oxygen = s.Oxygen,
        SuitEnergy = s.SuitEnergy,
        Hunger = s.Hunger,
        SelectedHotbarSlot = s.SelectedHotbarSlot,
        AboardShip = s.AboardShip,
        CurrentLocationId = s.CurrentLocationId,
        Role = Enum.TryParse<PlayerRole>(s.Role, out var role) ? role : PlayerRole.Player,
        NameTokenHash = s.NameTokenHash ?? string.Empty,
        Inventory = RestoreInventory(s.InventorySlotCount, s.Inventory),
        UnlockedBlueprints = new HashSet<string>(s.UnlockedBlueprints),
        KnowledgePoints = s.KnowledgePoints,
        KnowledgeGivenTo = new Dictionary<string, int>(s.KnowledgeGivenTo ?? new Dictionary<string, int>()),
        NpcMemory = CloneNpcMemory(s.NpcMemory),
        Scanned = new HashSet<string>(s.Scanned ?? new List<string>()),
        RationStore = RestoreInventory(BlocksBeyondTheStars.Shared.State.PlayerState.RationStoreSlots, s.RationStore ?? new List<InventorySlotDto>()),
        Missions = s.Missions.Select(CloneProgress).ToList(),
        Milestones = new HashSet<string>(s.Milestones ?? new List<string>()),
        UnlockedGames = new HashSet<string>(s.UnlockedGames ?? new List<string>()),
        LandedBodies = new HashSet<string>(s.LandedBodies ?? new List<string>()),
        KnownSystems = new HashSet<string>(s.KnownSystems ?? new List<string>()),
        TamedCreatures = (s.TamedCreatures ?? new List<TamedCreature>()).Select(CloneTamed).ToList(),
        TamedSpecies = new HashSet<string>(s.TamedSpecies ?? new List<string>()),
        FacePixels = s.FacePixels ?? string.Empty,
    };

    public static ShipSnapshot ToSnapshot(ShipState ship) => new()
    {
        Modules = ship.Modules.ToList(),
        CurrentLocationId = ship.CurrentLocationId,
        CargoSlotCount = ship.Cargo.SlotCount,
        Cargo = DumpInventory(ship.Cargo),
        Hull = ship.Hull,
        Shield = ship.Shield,
        ShipType = ship.ShipType,
    };

    public static ShipState FromSnapshot(ShipSnapshot s) => new()
    {
        Modules = s.Modules.ToList(),
        CurrentLocationId = s.CurrentLocationId,
        Cargo = RestoreInventory(s.CargoSlotCount, s.Cargo),
        Hull = s.Hull,
        Shield = s.Shield,
        ShipType = string.IsNullOrEmpty(s.ShipType) ? "starter" : s.ShipType,
    };
}
