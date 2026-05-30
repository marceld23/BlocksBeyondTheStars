using Spacecraft.Shared.Geometry;
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
    public float Health { get; set; } = 100f;
    public float Oxygen { get; set; } = 100f;
    public float SuitEnergy { get; set; } = 100f;
    public int SelectedHotbarSlot { get; set; }
    public bool AboardShip { get; set; } = true;
    public int InventorySlotCount { get; set; } = 24;
    public List<string> UnlockedBlueprints { get; set; } = new();
    public List<InventorySlotDto> Inventory { get; set; } = new();
}

public sealed class ShipSnapshot
{
    public List<string> Modules { get; set; } = new();
    public string CurrentLocationId { get; set; } = string.Empty;
    public int CargoSlotCount { get; set; } = 48;
    public List<InventorySlotDto> Cargo { get; set; } = new();
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
        Health = p.Health,
        Oxygen = p.Oxygen,
        SuitEnergy = p.SuitEnergy,
        SelectedHotbarSlot = p.SelectedHotbarSlot,
        AboardShip = p.AboardShip,
        InventorySlotCount = p.Inventory.SlotCount,
        UnlockedBlueprints = p.UnlockedBlueprints.ToList(),
        Inventory = DumpInventory(p.Inventory),
    };

    public static PlayerState FromSnapshot(PlayerSnapshot s) => new()
    {
        PlayerId = s.Id,
        Name = s.Name,
        Position = new Vector3f(s.X, s.Y, s.Z),
        Yaw = s.Yaw,
        Pitch = s.Pitch,
        Health = s.Health,
        Oxygen = s.Oxygen,
        SuitEnergy = s.SuitEnergy,
        SelectedHotbarSlot = s.SelectedHotbarSlot,
        AboardShip = s.AboardShip,
        Inventory = RestoreInventory(s.InventorySlotCount, s.Inventory),
        UnlockedBlueprints = new HashSet<string>(s.UnlockedBlueprints),
    };

    public static ShipSnapshot ToSnapshot(ShipState ship) => new()
    {
        Modules = ship.Modules.ToList(),
        CurrentLocationId = ship.CurrentLocationId,
        CargoSlotCount = ship.Cargo.SlotCount,
        Cargo = DumpInventory(ship.Cargo),
    };

    public static ShipState FromSnapshot(ShipSnapshot s) => new()
    {
        Modules = s.Modules.ToList(),
        CurrentLocationId = s.CurrentLocationId,
        Cargo = RestoreInventory(s.CargoSlotCount, s.Cargo),
    };
}
