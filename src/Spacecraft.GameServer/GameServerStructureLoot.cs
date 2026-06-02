using System.Collections.Generic;
using Spacecraft.Persistence;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.State;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// Turns the loot markers of stamped structures (ruined settlements, crashed wrecks) into actual
/// <b>lootable containers</b> the player can scavenge with the existing loot flow. Each marker is
/// spawned once and recorded in <see cref="WorldMetadata.GeneratedLoot"/> so it never re-spawns on
/// reload — not even after it has been looted and removed.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Spawns a one-time lootable container at a structure marker (idempotent across reloads).</summary>
    private void SpawnStructureLoot(string structureKind, string markerType, Vector3f pos, System.Random rng)
    {
        int bx = (int)pos.X, by = (int)pos.Y, bz = (int)pos.Z;
        string key = $"{structureKind}:{markerType}:{bx}:{by}:{bz}";
        if (_meta.GeneratedLoot.Contains(key))
        {
            return; // already generated once (container may since have been looted away)
        }

        _meta.GeneratedLoot.Add(key);

        var items = BuildStructureLoot(markerType, rng);
        if (items.Count == 0)
        {
            return;
        }

        AddContainer(new StoredContainer
        {
            Id = "loot_" + key.Replace(':', '_'),
            Planet = _world.LocationId,
            Kind = markerType switch
            {
                "module" => "salvage_module",
                "data_terminal" => "data_terminal",
                _ => "salvage",
            },
            Position = new Vector3i(bx, by, bz),
            Items = items,
        });
    }

    /// <summary>Rolls the loot for a marker — general salvage, a recoverable module, or a data cache.</summary>
    private List<ItemStack> BuildStructureLoot(string markerType, System.Random rng)
    {
        var items = new List<ItemStack>();

        void AddRandom(string[] pool, int picks, int min, int max)
        {
            for (int i = 0; i < picks; i++)
            {
                string item = pool[rng.Next(pool.Length)];
                int count = min + rng.Next(max - min + 1);
                if (_content.GetItem(item) is not null)
                {
                    items.Add(new ItemStack(item, count));
                }
            }
        }

        switch (markerType)
        {
            case "module": // a recoverable ship component — the valuable salvage
                AddRandom(new[] { "energy_cell_1", "titanium_plate", "cable" }, 2, 1, 3);
                break;
            case "data_terminal": // research data
                AddRandom(new[] { "data_fragment" }, 1, 1, 2);
                break;
            default: // general salvage cache
                AddRandom(new[] { "iron_plate", "cable", "carbon_composite", "silicate", "iron_ore", "copper_ore" }, 3, 1, 4);
                break;
        }

        return items;
    }
}
