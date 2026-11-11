// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What a landmark's markers become (#3486, #3488) — see GameServerLandmarks.cs. Re-derived on every load from the pinned
/// layout; everything that must happen only once goes through <see cref="WorldMetadata.GeneratedLoot"/>.
/// <list type="bullet">
/// <item><b>Chests</b> (<see cref="LandmarkRules.ChestMarker"/>): a one-time <c>salvage</c> container in the crate's cell.
/// Its content is the author's: <c>items=iron_plate:2-4,gold_ingot:1</c> (fixed; a range draws), <c>table=&lt;key&gt;</c>
/// (a roll of <c>data/loot_tables.json</c>), or both; neither = the table <see cref="LandmarkDefaultLootTable"/>. Every
/// draw is a hash of the world seed, the body and the chest's cell — never the stamp's random stream — so a chest's
/// content does not depend on what was rolled before it.</item>
/// <item><b>The classic loot markers</b> (<c>loot</c>, <c>data_terminal</c>, <c>relic_cache</c>) keep their hard-coded rolls.</item>
/// <item><b>Doors and pre-built circuits</b> of an intact landmark, like a settlement's.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The block a chest stands as when its marker names none.</summary>
    private const string LandmarkChestBlock = "wood_crate";

    /// <summary>The loot table of a chest whose marker names neither items nor a table.</summary>
    internal const string LandmarkDefaultLootTable = "ruin_common";

    /// <summary>The structure-loot kind of a landmark's classic loot markers.</summary>
    private const string LandmarkLootKind = "landmark";

    /// <summary>Container id prefix of a landmark's chest.</summary>
    internal const string LandmarkChestContainerPrefix = "loot_landmark_chest_";

    private static readonly HashSet<string> LandmarkLootMarkers = new(System.StringComparer.Ordinal) { "loot", "data_terminal", "relic_cache" };

    /// <summary>Everything a stamped landmark is at runtime, from its markers.</summary>
    private void MaterializeLandmark(LandmarkInstance lm)
    {
        var placed = new PlacedSettlement
        {
            Structure = lm.Structure,
            Origin = lm.Origin,
            GroundY = lm.Origin.Y,
            Tier = LandmarkGenerator.Tier,
            Ruined = lm.Layout.Ruined,
            OnIsland = false,
            Name = lm.Key,
            Rng = RngFor(lm.Seed, "markers"),
        };
        var lootRng = RngFor(lm.Seed, "landmark-loot");
        bool doors = false;
        foreach (var marker in lm.Structure.Markers)
        {
            var cell = WorldConstants.CanonicalBlock(lm.Cell(marker.LocalPos), _world.Circumference);
            if (marker.Type == LandmarkRules.ChestMarker)
            {
                SpawnLandmarkChest(cell, MarkerSettings.Parse(marker.Data));
            }
            else if (LandmarkLootMarkers.Contains(marker.Type))
            {
                SpawnStructureLoot(LandmarkLootKind, marker.Type, new Vector3f(cell.X, cell.Y, cell.Z), lootRng);
            }
            else if (DoorBlocks.IsDoorMarker(marker.Type))
            {
                _settlementMarkers.Add((marker.Type, new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f)));
                RecordAuthoredDoor(placed, marker);
                doors = true;
            }
            else if (marker.Type == TemplateDevices.Marker)
            {
                QueueWorldCircuitCell(cell, marker.Data);
            }
        }

        if (doors)
        {
            RegisterDoors();
        }
    }

    /// <summary>The one-time container of a chest (idempotent through <see cref="WorldMetadata.GeneratedLoot"/>: a looted
    /// chest never refills).</summary>
    private void SpawnLandmarkChest(Vector3i cell, MarkerSettings settings)
    {
        string key = $"landmark:chest:{cell.X}:{cell.Y}:{cell.Z}";
        if (_meta.GeneratedLoot.Contains(key))
        {
            return;
        }

        _meta.GeneratedLoot.Add(key);
        if (!LandmarkChestExists(cell, settings))
        {
            return;
        }

        var items = LandmarkChestItems(cell, settings);
        if (items.Count == 0)
        {
            return;
        }

        AddContainer(new StoredContainer
        {
            Id = LandmarkChestContainerPrefix + $"{cell.X}_{cell.Y}_{cell.Z}",
            Planet = _world.LocationId,
            Kind = "salvage",
            Position = cell,
            Items = items,
        });
    }

    /// <summary>What a chest holds: the author's fixed items, the roll of the author's table, or the default table.</summary>
    internal List<ItemStack> LandmarkChestItems(Vector3i cell, MarkerSettings settings)
    {
        ulong seed = StableHashing.Mixed($"landmark:chest:{_world.LocationId}:{cell.X}:{cell.Y}:{cell.Z}", _meta.Seed);
        var items = new List<ItemStack>();
        var fixedItems = MarkerSettings.ParseItems(settings.Get("items"));
        for (int i = 0; i < fixedItems.Count; i++)
        {
            var e = fixedItems[i];
            if (_content.GetItem(e.Item) is null)
            {
                continue;
            }

            int span = e.Max - e.Min + 1;
            int count = e.Min + StableHashing.Index(StableHashing.Mix64(seed ^ ((ulong)(uint)i * 0x9E37_79B9_7F4A_7C15UL)), span);
            items.Add(new ItemStack(e.Item, count));
        }

        string tableKey = settings.Get("table", fixedItems.Count == 0 ? LandmarkDefaultLootTable : string.Empty);
        if (tableKey.Length > 0 && _content.LootTable(tableKey) is { } table)
        {
            items.AddRange(table.Roll(seed, item => _content.GetItem(item) is not null));
        }

        return items;
    }
}
