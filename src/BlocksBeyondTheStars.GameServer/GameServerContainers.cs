// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
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

    /// <summary>Distinct item stacks a hand-tier wood box holds (#808). The workshop crate stays
    /// unbounded — capacity is the wood box's price for being craftable from nothing but logs.</summary>
    private const int WoodCrateStackSlots = 8;

    /// <summary>Upper bound on a crate's filter list (#1032) — enough for any real sorting scheme, small
    /// enough that a hostile client can't inflate the container broadcast every player receives.</summary>
    private const int MaxFilterEntries = 32;

    private List<StoredContainer> _containers => _worlds.Active.Containers;

    /// <summary>Blocks that become a storage container when placed (share the "crate" container kind,
    /// so loot/stash/HUD treat them identically — the block key only decides capacity).</summary>
    private static bool IsContainerBlock(string key) => key is "crate" or "wood_crate";

    /// <summary>Lootable containers on the current planet (salvage capsules / corpses / crates). Ground drop
    /// packets share the same store but are not containers to the player — see <see cref="DropPackets"/>.</summary>
    public IReadOnlyList<StoredContainer> Containers
        => _containers.Where(c => c.Kind != DropPacketKind).ToList();

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
            Reject(session, "loot", "@srv.loot.no_container");
            return;
        }

        var center = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
        if (WrapDistSq(session.State.Position, center) > LootReach * LootReach)
        {
            Reject(session, "loot", "@out_of_reach"); // localized client-side (#751)
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

            int max = _content.GetItem(stack.Item)?.MaxStack ?? ItemDefinition.DefaultMaxStack;
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

        // A full backpack used to no-op silently (#751): nothing moved, nothing was sent, and the
        // prompt kept showing the same count — indistinguishable from a dead key. Tell the player.
        if (!took && leftover.Count > 0)
        {
            Reject(session, "loot", "@inventory_full");
            return;
        }

        container.Items = leftover;
        if (container.Items.Count == 0 && container.Kind != "crate")
        {
            _containers.Remove(container);
            _repo.DeleteContainer(container.Id);
            OnAchievementLoot(session); // "Treasure Hunter": a WORLD find looted empty (#1102)
        }
        else
        {
            // Player storage crates are not treasure: emptying one neither counts as a find nor deletes
            // its row — the crate block still stands and must stay stashable (#1153).
            _repo.SaveContainer(container);
        }

        if (took)
        {
            SendInventory(session);

            // Environmental lore (#1111): scavenging a lore-bearing site (wreck log, ruin note, vault plaque,
            // terminal record) may surface a readable text — once per player per text, knowledge-gated.
            var site = LoreSiteOfContainer(container.Id);
            if (site.Length > 0)
            {
                TryRevealLoreText(session, site);
            }

            if (site == "vault")
            {
                RecordStoryMilestone("vault:first"); // the save's first opened vault advances the arc (#1155)
            }
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
            Reject(session, "stash", "@srv.loot.no_crate");
            return;
        }

        var center = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
        if (WrapDistSq(session.State.Position, center) > LootReach * LootReach)
        {
            Reject(session, "stash", "@out_of_reach");
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
            Reject(session, "stash", "@srv.loot.nothing_to_stash");
            return;
        }

        // A wood box (#808) holds only a few distinct stacks; the workshop crate is unbounded. Existing
        // stacks always merge (no per-stack count cap) — only NEW item types are refused once full.
        bool woodBox = _world.GetBlock(container.Position).Value == (_content.GetBlock("wood_crate")?.NumericId.Value ?? 0);

        var merged = container.Items.Where(s => !s.IsEmpty).ToDictionary(s => s.Item, s => s.Count);
        bool stashed = false;
        bool boxFull = false;
        foreach (var (item, count) in toStash)
        {
            // Dedicated crates (#1032): only whitelisted items go in. Matched on the base key so a
            // dyed/shaped variant of an allowed material still fits.
            if (container.Filter.Count > 0 && !container.Filter.Contains(ItemKey.Base(item)))
            {
                continue; // not what this crate is for — this stack stays with the player
            }

            if (woodBox && !merged.ContainsKey(item) && merged.Count >= WoodCrateStackSlots)
            {
                boxFull = true;
                continue; // box full for new item types — this stack stays with the player
            }

            inv.Remove(item, count);
            merged[item] = (merged.TryGetValue(item, out var have) ? have : 0) + count;
            stashed = true;
        }

        if (!stashed)
        {
            // "Box full" only when capacity actually refused something; otherwise the filter did, and
            // saying "full" at an empty-but-dedicated crate would send the player hunting phantom space.
            Reject(session, "stash", boxFull ? "@srv.loot.wood_box_full" : "@srv.loot.filter_blocked");
            return;
        }

        container.Items = merged.Select(kv => new ItemStack(kv.Key, kv.Value)).ToList();
        _repo.SaveContainer(container);
        SendInventory(session);
        BroadcastContainers();
    }

    private void HandleDepositContainer(PlayerSession session, DepositContainerIntent intent)
        => DepositToContainer(session.State.PlayerId, intent.ContainerId);

    /// <summary>The player dedicates a crate to specific items (#1032): press E at a crate, pick what belongs
    /// in it. Server-authoritative — unknown keys and non-stashable categories are dropped here, so the client
    /// UI is a convenience, not the rule. An empty list clears the filter.</summary>
    private void HandleSetContainerFilter(PlayerSession session, SetContainerFilterIntent intent)
    {
        var container = _containers.FirstOrDefault(c => c.Id == intent.ContainerId && c.Kind == "crate");
        if (container is null)
        {
            Reject(session, "stash", "@srv.loot.no_crate");
            return;
        }

        var center = new Vector3f(container.Position.X + 0.5f, container.Position.Y + 0.5f, container.Position.Z + 0.5f);
        if (WrapDistSq(session.State.Position, center) > LootReach * LootReach)
        {
            Reject(session, "stash", "@out_of_reach");
            return;
        }

        container.Filter = (intent.Items ?? Array.Empty<string>())
            .Select(ItemKey.Base)
            .Where(key => _content.GetItem(key)?.Category is Shared.Definitions.ItemCategory.Material or Shared.Definitions.ItemCategory.Component)
            .Distinct()
            .Take(MaxFilterEntries)
            .ToList();

        // Station-derived crates are runtime-only (rebuilt on every board, never saved) — persisting one
        // here would leave a phantom row behind. Only crates that exist as placed blocks are written back.
        if (IsContainerBlock(_content.BlockById(_world.GetBlock(container.Position))?.Key ?? string.Empty))
        {
            _repo.SaveContainer(container);
        }

        BroadcastContainers();
    }

    /// <summary>Test/util entrypoint: set a crate's filter as a given player (mirrors the filter intent).</summary>
    public void SetContainerFilterForTest(PlayerSession session, string containerId, string[] items)
        => HandleSetContainerFilter(session, new SetContainerFilterIntent { ContainerId = containerId, Items = items });

    /// <summary>Test seam: registers a container verbatim (a world loot container or a player crate).</summary>
    public void AddContainerForTest(StoredContainer container) => AddContainer(container);

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
    /// <summary>The stacks a crate at <paramref name="pos"/> would hand back if it were mined. Used to check
    /// there is room for them BEFORE the crate is broken — mining a full crate into a full inventory used to
    /// destroy its contents.</summary>
    private IEnumerable<ItemAmount> CrateContentsAt(Vector3i pos)
    {
        if (_containers.FirstOrDefault(c => c.Kind == "crate" && c.Position.Equals(pos)) is not { } container)
        {
            return Array.Empty<ItemAmount>();
        }

        return container.Items.Where(s => !s.IsEmpty).Select(s => new ItemAmount(s.Item, s.Count)).ToList();
    }

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
        Filter = c.Filter.ToArray(),
    };

    /// <summary>The containers a client renders + loots. Ground drop packets share this store (and its free
    /// persistence) but are NOT containers to the player: they collect themselves and would otherwise steal
    /// the crate/capsule loot prompt, so they are filtered out here and travel on their own list (#853).</summary>
    private ContainerList ContainerMessage() => new()
    {
        Containers = _containers.Where(c => c.Kind != DropPacketKind).Select(ToNetContainer).ToArray(),
    };

    private void BroadcastContainers() => BroadcastToWorld(ContainerMessage());

    /// <summary>Sends this world's containers AND its ground drop packets — the two lists always travel
    /// together, so every join/respawn/travel path stays in sync with one call.</summary>
    private void SendContainers(PlayerSession session)
    {
        Send(session, ContainerMessage());
        SendDropPackets(session);
    }
}
