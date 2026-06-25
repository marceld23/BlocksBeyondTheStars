// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Lootable world containers — salvage capsules dropped on death and (later) defeated-player
/// corpses or ship-salvage drops. The death path drops a capsule with the victim's carried items
/// (see <c>RespawnPlayer</c>); this lets anyone standing next to it <b>loot</b> the contents into
/// their inventory. The capsule persists until emptied, then despawns (combat-loot, §2–3).
/// Server-authoritative: it validates proximity and the item transfer.
/// </summary>
public sealed partial class GameServer
{
    private const float LootReach = 6f;
    private List<StoredContainer> _containers => _worlds.Active.Containers;

    /// <summary>Lootable containers on the current planet (salvage capsules / corpses).</summary>
    public IReadOnlyList<StoredContainer> Containers => _containers;

    private void LoadContainers()
    {
        _containers.Clear();
        _containers.AddRange(_repo.ListContainers(_world.LocationId));
    }

    /// <summary>Registers a new world container (capsule / corpse): persists it, tracks it, broadcasts.</summary>
    private void AddContainer(StoredContainer container)
    {
        _repo.SaveContainer(container);
        _containers.Add(container);
        BroadcastContainers();
    }

    /// <summary>Player loots a nearby container; items transfer into the inventory, then it despawns if empty.</summary>
    public void LootContainer(string playerId, string containerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var container = _containers.FirstOrDefault(c => c.Id == containerId);
        if (container is null)
        {
            Reject(session, "loot", "No such container.");
            return;
        }

        var center = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
        if (WrapDistSq(session.State.Position, center) > LootReach * LootReach)
        {
            Reject(session, "loot", "Container is out of reach.");
            return;
        }

        var inv = session.State.Inventory;
        var leftover = new List<ItemStack>();
        bool took = false;
        foreach (var stack in container.Items)
        {
            if (stack.IsEmpty)
            {
                continue;
            }

            int max = _content.GetItem(stack.Item)?.MaxStack ?? 99;
            int notPlaced = inv.Add(stack.Item, stack.Count, max); // Add returns the leftover it couldn't fit
            if (notPlaced < stack.Count)
            {
                took = true;
            }

            if (notPlaced > 0)
            {
                leftover.Add(new ItemStack(stack.Item, notPlaced)); // inventory full → leave the rest
            }
        }

        container.Items = leftover;
        if (container.Items.Count == 0)
        {
            _containers.Remove(container);
            _repo.DeleteContainer(container.Id);
        }
        else
        {
            _repo.SaveContainer(container);
        }

        if (took)
        {
            SendInventory(session);
        }

        BroadcastContainers();
    }

    private void HandleLootContainer(PlayerSession session, LootContainerIntent intent)
        => LootContainer(session.State.PlayerId, intent.ContainerId);

    /// <summary>Stashes a player's loose raw/refined materials into a nearby storage crate (Task 5 Stage 3b):
    /// every Material/Component stack moves in (tools/weapons/equipment stay with the player). Persisted.</summary>
    public void DepositToContainer(string playerId, string containerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var container = _containers.FirstOrDefault(c => c.Id == containerId && c.Kind == "crate");
        if (container is null)
        {
            Reject(session, "stash", "No such crate.");
            return;
        }

        var center = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
        if (WrapDistSq(session.State.Position, center) > LootReach * LootReach)
        {
            Reject(session, "stash", "Crate is out of reach.");
            return;
        }

        var inv = session.State.Inventory;
        var toStash = new Dictionary<string, int>();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { IsEmpty: false } s
                && _content.GetItem(s.Item)?.Category is Shared.Definitions.ItemCategory.Material or Shared.Definitions.ItemCategory.Component)
            {
                toStash[s.Item] = (toStash.TryGetValue(s.Item, out var have) ? have : 0) + s.Count;
            }
        }

        if (toStash.Count == 0)
        {
            Reject(session, "stash", "No loose materials to store.");
            return;
        }

        var merged = container.Items.Where(s => !s.IsEmpty).ToDictionary(s => s.Item, s => s.Count);
        foreach (var (item, count) in toStash)
        {
            inv.Remove(item, count);
            merged[item] = (merged.TryGetValue(item, out var have) ? have : 0) + count;
        }

        container.Items = merged.Select(kv => new ItemStack(kv.Key, kv.Value)).ToList();
        _repo.SaveContainer(container);
        SendInventory(session);
        BroadcastContainers();
    }

    private void HandleDepositContainer(PlayerSession session, DepositContainerIntent intent)
        => DepositToContainer(session.State.PlayerId, intent.ContainerId);

    /// <summary>Places a storage crate the player just built into the world as an (empty) lootable container.</summary>
    private void PlaceCrate(Vector3i pos)
        => AddContainer(new StoredContainer
        {
            Id = "crate_" + System.Guid.NewGuid().ToString("N"),
            Planet = _world.LocationId,
            Kind = "crate",
            Position = pos,
            Items = new List<ItemStack>(),
        });

    /// <summary>Mining a storage crate returns its stored contents to the miner and removes the container.</summary>
    private void RemoveCrateContainer(Vector3i pos, MaterialPool pool)
    {
        if (_containers.FirstOrDefault(c => c.Kind == "crate" && c.Position.Equals(pos)) is not { } container)
        {
            return;
        }

        foreach (var s in container.Items.Where(s => !s.IsEmpty))
        {
            pool.Add(s.Item, s.Count);
        }

        _containers.Remove(container);
        _repo.DeleteContainer(container.Id);
        BroadcastContainers();
    }

    private static NetContainer ToNetContainer(StoredContainer c) => new()
    {
        Id = c.Id,
        Kind = c.Kind,
        X = c.Position.X,
        Y = c.Position.Y,
        Z = c.Position.Z,
        ItemCount = c.Items.Count,
    };

    private void BroadcastContainers()
        => BroadcastToWorld(new ContainerList { Containers = _containers.Select(ToNetContainer).ToArray() });

    private void SendContainers(PlayerSession session)
        => Send(session, new ContainerList { Containers = _containers.Select(ToNetContainer).ToArray() });
}
