// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

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
        // Story fragments in structures (#1109): a data terminal or relic cache may also hold a net fragment.
        // Deliberately OUTSIDE the GeneratedLoot guard — fragments re-derive per residency (minus found ones),
        // while the loot container spawns exactly once ever.
        TryPlaceStructureFragment(markerType, pos);

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
            case "data_terminal": // research data + a recoverable shard of VEGA's fleet memory (her story arc)
                AddRandom(new[] { "data_fragment" }, 1, 1, 2);
                AddRandom(new[] { "ai_memory_fragment" }, 1, 1, 1);
                break;
            case "chest": // a standalone treasure cache — richer odds than generic salvage
                AddRandom(new[] { "iron_plate", "titanium_plate", "cable", "energy_cell_1", "circuit_board", "carbon_composite" }, 3, 1, 4);
                if (rng.NextDouble() < 0.35)
                {
                    AddRandom(new[] { "data_fragment", "crystal", "gold_ingot" }, 1, 1, 2);
                }

                // A rare access (SPS) code — the prize that lets a structure be claimed as a base.
                if (rng.NextDouble() < 0.14 && _content.GetItem("access_code") is not null)
                {
                    items.Add(new ItemStack("access_code", 1));
                }

                break;
            case "bandit_stash": // a bandit camp's stash — stolen goods, the raid reward (richer than ruins)
                AddRandom(new[] { "iron_plate", "titanium_plate", "energy_cell_1", "cable", "circuit_board" }, 3, 2, 4);
                if (rng.NextDouble() < 0.5)
                {
                    AddRandom(new[] { "gold_ingot", "crystal", "data_fragment" }, 1, 1, 2);
                }

                break;
            case "relic_cache": // buried at a rune monument — archaeology, not salvage
                AddRandom(new[] { "data_fragment", "crystal", "silicate" }, 2, 1, 3);
                if (rng.NextDouble() < 0.4)
                {
                    AddRandom(new[] { "gold_ingot", "titanium_plate" }, 1, 1, 2);
                }

                if (rng.NextDouble() < 0.2)
                {
                    AddRandom(new[] { "ai_memory_fragment" }, 1, 1, 1); // VEGA's people were here too
                }

                break;
            default: // general salvage cache
                AddRandom(new[] { "iron_plate", "cable", "carbon_composite", "silicate", "iron_ore", "copper_ore" }, 3, 1, 4);
                break;
        }

        // Frontier scaling (#1122): containers out in the full-frontier tier carry one late-game pick on
        // top — the flight out there should pay in exactly the materials the H2 tech ladder wants. The
        // roll comes AFTER the per-type rolls, so home-system loot streams are byte-identical to before.
        if (FrontierTierForBody(_world.LocationId) >= 2)
        {
            AddRandom(new[] { "titanium_plate", "circuit_board", "energy_cell_1", "gold_ingot", "crystal" }, 1, 1, 2);
        }

        return items;
    }
}
