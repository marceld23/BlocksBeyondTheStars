using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Spacecraft.Shared.State;

namespace Spacecraft.Persistence;

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
    public string Role { get; set; } = "Player";
    public int InventorySlotCount { get; set; } = 24;
    public List<string> UnlockedBlueprints { get; set; } = new();
    public int KnowledgePoints { get; set; }
    public List<string> Scanned { get; set; } = new();
    public List<InventorySlotDto> Inventory { get; set; } = new();
    public List<MissionProgress> Missions { get; set; } = new();
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
        Role = p.Role.ToString(),
        InventorySlotCount = p.Inventory.SlotCount,
        UnlockedBlueprints = p.UnlockedBlueprints.ToList(),
        KnowledgePoints = p.KnowledgePoints,
        Scanned = p.Scanned.ToList(),
        Inventory = DumpInventory(p.Inventory),
        Missions = p.Missions.Select(CloneProgress).ToList(),
    };

    private static MissionProgress CloneProgress(MissionProgress m) => new()
    {
        MissionId = m.MissionId,
        Status = m.Status,
        ObjectiveProgress = new List<int>(m.ObjectiveProgress),
    };

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
        Role = Enum.TryParse<PlayerRole>(s.Role, out var role) ? role : PlayerRole.Player,
        Inventory = RestoreInventory(s.InventorySlotCount, s.Inventory),
        UnlockedBlueprints = new HashSet<string>(s.UnlockedBlueprints),
        KnowledgePoints = s.KnowledgePoints,
        Scanned = new HashSet<string>(s.Scanned ?? new List<string>()),
        Missions = s.Missions.Select(CloneProgress).ToList(),
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
