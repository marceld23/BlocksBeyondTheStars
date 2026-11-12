// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
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
    /// <summary>Spawns a one-time lootable container at a structure marker (idempotent across reloads).
    /// <paramref name="replayDraws"/> (#3513): when the container was generated before, still roll its loot and drop it, so
    /// <paramref name="rng"/> advances exactly as at the first spawn — for a caller whose later draws (the next chest's spot)
    /// come from the same stream. Off by default: every other caller's stream ends with the loot, or its later draws were
    /// always made without these on a reload.</summary>
    private void SpawnStructureLoot(string structureKind, string markerType, Vector3f pos, System.Random rng, bool replayDraws = false)
    {
        // Story fragments in structures (#1109): a data terminal or relic cache may also hold a net fragment.
        // Deliberately OUTSIDE the GeneratedLoot guard — fragments re-derive per residency (minus found ones),
        // while the loot container spawns exactly once ever.
        TryPlaceStructureFragment(markerType, pos);

        // #3341: a buried G.D.S. ruin's crate ("gds_cache:<n>") stands exactly in its marker's cell, below zero too, and rolls
        // from its own seed, never the stamp's rng. The older kinds keep their truncated cell: their spawn keys are in saved
        // worlds, and another rounding would fill those caches a second time.
        bool gds = TryParseGdsCache(markerType, out int gdsCache);
        var cell = gds ? pos.ToBlock() : new Vector3i((int)pos.X, (int)pos.Y, (int)pos.Z);
        int bx = cell.X, by = cell.Y, bz = cell.Z;
        string key = $"{structureKind}:{markerType}:{bx}:{by}:{bz}";
        if (_meta.GeneratedLoot.Contains(key))
        {
            if (replayDraws && !gds)
            {
                _ = BuildStructureLoot(markerType, rng); // the first spawn's draws, without their container
            }

            return; // already generated once (container may since have been looted away)
        }

        _meta.GeneratedLoot.Add(key);

        var items = gds ? BuildGdsCacheLoot(gdsCache, cell) : BuildStructureLoot(markerType, rng);
        if (markerType == "relic_cache")
        {
            AddSounderRelicFinds(key, items); // #3352: a rare Song Spindle or Song Disc — from the cache's own key, not rng
        }

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

    /// <summary>Id prefix of the caches a settlement's "loot" markers spawn (<see cref="SpawnStructureLoot"/>: <c>loot_</c> +
    /// the key <c>settlement:loot:x:y:z</c> with every ':' made '_').</summary>
    private const string SettlementLootIdPrefix = "loot_settlement_";

    /// <summary>
    /// #2726: the intact settlement a scavenge cache belongs to, or null when it is anybody's. Until this fix the hand-made
    /// inhabited templates spawned a cache at their "loot" marker as if they were ruins (a player: "I can loot in the
    /// village? I don't want to anger my friendly neighbours"). New stamps no longer do; a cache an existing world already
    /// holds stays where it is (no save migration), but it is the villagers' now: look, don't take
    /// (<see cref="OwnedContainerRefusal"/>), and the HUD names them instead of offering the loot key
    /// (<c>NetContainer.OwnedBy</c>). Only settlement caches — a death capsule dropped in a village stays anybody's to loot,
    /// and a ruin's cache (a ruin is never an intact settlement) stays free.
    /// </summary>
    private SettlementInstance? VillageOwningCache(StoredContainer container)
        => container.Id.StartsWith(SettlementLootIdPrefix, System.StringComparison.Ordinal) ? IntactSettlementAt(container.Position) : null;

    /// <summary>
    /// Why a player may not take from, put into, sort or rearrange this container, or "" when they may: a lodge's store
    /// (#2640, <see cref="LodgeStoreRefusal"/> — "That's Mira's — ask them!") or a cache that stands in an intact village
    /// (#2726, <see cref="VillageOwningCache"/> — "That belongs to the people of Karth Village.") — or #3294 an owner-locked
    /// crate (<see cref="ContainerSystem.IsOwnerLocked"/>): <paramref name="playerId"/> may take only from their own, and
    /// <paramref name="putIn"/> is refused for everyone, the owner too, so nobody can fill a reward crate to grief it.
    /// </summary>
    private string OwnedContainerRefusal(StoredContainer container, string playerId, bool putIn)
    {
        string lodge = LodgeStoreRefusal(container.Id);
        if (lodge.Length > 0)
        {
            return lodge;
        }

        if (ContainerSystem.IsOwnerLocked(container))
        {
            return putIn ? "@srv.loot.owner_locked:" + container.OwnerName
                : container.Owner != playerId ? "@srv.loot.owner_only:" + container.OwnerName
                : string.Empty;
        }

        return VillageOwningCache(container) is { } village ? "@srv.loot.village_owned:" + village.Name : string.Empty;
    }

    /// <summary>#3294: why a player may not mine the block at <paramref name="pos"/> because it is an owner-locked crate, or ""
    /// — nobody mines a locked crate before its owner has emptied it (mining would hand its stacks to the miner). An admin
    /// may, and it is written to the cheat log.</summary>
    private string LockedCrateMineRefusal(PlayerSession session, Vector3i pos)
    {
        if (_containers.FirstOrDefault(c => c.Kind == "crate" && c.Position.Equals(pos)) is not { } crate
            || !ContainerSystem.IsOwnerLocked(crate))
        {
            return string.Empty;
        }

        if (session.State.IsAdmin)
        {
            if (_lockedCrateAdminLogged.Add(crate.Id)) // once per crate — a crate takes several mining hits
            {
                CheatLog(session.State, $"mined {crate.OwnerName}'s locked crate at ({pos.X}, {pos.Y}, {pos.Z})");
            }

            return string.Empty;
        }

        return "@srv.loot.owner_locked_mine:" + crate.OwnerName;
    }

    /// <summary>#3294: the locked crates an admin's mining was already written to the cheat log for.</summary>
    private readonly HashSet<string> _lockedCrateAdminLogged = new(System.StringComparer.Ordinal);

    /// <summary>#3294: whether the cell holds an owner-locked crate — the terrain blaster leaves it standing (its spill would
    /// hand the owner's items to whoever picks them up).</summary>
    private bool IsLockedCrateAt(Vector3i pos)
        => _containers.FirstOrDefault(c => c.Kind == "crate" && c.Position.Equals(pos)) is { } crate && ContainerSystem.IsOwnerLocked(crate);

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
            case "sps_cache": // an abandoned SPS research station (2026-09, Titas): old tech, research data, rusted panels
                AddRandom(new[] { "circuit_board", "energy_cell_1", "cable", "rusted_panel" }, 3, 1, 3);
                if (rng.NextDouble() < 0.5)
                {
                    AddRandom(new[] { "data_fragment" }, 1, 1, 2);
                }

                if (rng.NextDouble() < 0.12 && _content.GetItem("access_code") is not null)
                {
                    items.Add(new ItemStack("access_code", 1)); // the Service's own codes
                }

                break;
            case RailRuinGenerator.CacheMarker: // an abandoned monorail station (#2166): parts of the old line
                AddRandom(new[] { "cable", "copper_wire", "iron_plate", "energy_cell_1", "circuit_board" }, 3, 1, 3);
                if (rng.NextDouble() < 0.5 && _content.GetItem(RailRules.PylonBlockKey) is not null)
                {
                    items.Add(new ItemStack(RailRules.PylonBlockKey, 2 + rng.Next(3))); // salvaged pylons — enough to start a line
                }

                if (rng.NextDouble() < 0.25 && _content.GetItem(RailRules.StopBlockKey) is not null)
                {
                    items.Add(new ItemStack(RailRules.StopBlockKey, 1));
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
