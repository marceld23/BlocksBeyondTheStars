// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Lootable world containers on the server: the facade of <see cref="ContainerSystem"/> (ADR 0019), which owns looting,
/// stashing, the crate filter (#1032), the crate screen (#2436), placed and mined crates and the container broadcast. This
/// partial keeps the store accessor the sibling partials share and the members the boot, the join, placement, mining, the
/// dispatch table and the tests call — same names, same signatures, each one line delegating — and the explicit
/// <see cref="IContainerHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IContainerHost
{
    /// <summary>Distinct item stacks a hand-tier wood box holds (#808), read by sibling partials (the value lives in
    /// ContainerSystem).</summary>
    private const int WoodCrateStackSlots = ContainerSystem.WoodCrateStackSlots;

    private List<StoredContainer> _containers => _worlds.Active.Containers;

    /// <summary>Blocks that become a storage container when placed (crate, wood box, station container).</summary>
    private static bool IsContainerBlock(string key) => ContainerSystem.IsContainerBlock(key);

    /// <summary>Lootable containers on the current planet (salvage capsules / corpses / crates). Ground drop
    /// packets share the same store but are not containers to the player — see <see cref="DropPackets"/>.</summary>
    public IReadOnlyList<StoredContainer> Containers => _containerSystem.Containers;

    private void LoadContainers() => _containerSystem.LoadContainers();

    /// <summary>Registers a new world container (capsule / corpse): persists it, tracks it, broadcasts.</summary>
    private void AddContainer(StoredContainer container) => _containerSystem.AddContainer(container);

    /// <summary>Player loots a nearby container; items transfer into the inventory, then it despawns if empty.</summary>
    public void LootContainer(string playerId, string containerId) => _containerSystem.LootContainer(playerId, containerId);

    private void HandleLootContainer(PlayerSession session, LootContainerIntent intent) => _containerSystem.HandleLootContainer(session, intent);

    /// <summary>Stashes a player's loose raw/refined materials and blocks into a nearby storage crate (Task 5
    /// Stage 3b): every Material/Component/Block stack moves in (tools/weapons/equipment stay with the
    /// player). Persisted.</summary>
    public void DepositToContainer(string playerId, string containerId) => _containerSystem.DepositToContainer(playerId, containerId);

    private void HandleDepositContainer(PlayerSession session, DepositContainerIntent intent) => _containerSystem.HandleDepositContainer(session, intent);

    private void HandleSetContainerFilter(PlayerSession session, SetContainerFilterIntent intent) => _containerSystem.HandleSetContainerFilter(session, intent);

    /// <summary>Places a storage crate the player just built into the world as an (empty) lootable container.</summary>
    private void PlaceCrate(Vector3i pos) => _containerSystem.PlaceCrate(pos);

    /// <summary>The stacks a crate at <paramref name="pos"/> would hand back if it were mined.</summary>
    private IEnumerable<ItemAmount> CrateContentsAt(Vector3i pos) => _containerSystem.CrateContentsAt(pos);

    /// <summary>Mining a storage crate returns its stored contents to the miner and removes the container.</summary>
    private void RemoveCrateContainer(Vector3i pos, MaterialPool pool) => _containerSystem.RemoveCrateContainer(pos, pool);

    private void BroadcastContainers() => _containerSystem.BroadcastContainers();

    private void HandleOpenContainer(PlayerSession session, OpenContainerIntent intent) => _containerSystem.HandleOpenContainer(session, intent);

    private void HandleMoveContainerItem(PlayerSession session, MoveContainerItemIntent intent) => _containerSystem.HandleMoveContainerItem(session, intent);

    /// <summary>Moves one kind of item between the player's backpack and a crate (#2436) — a click on the crate screen.
    /// Into the crate: one stack (or every stack with <paramref name="all"/>) of <paramref name="item"/>, any category,
    /// subject to the crate's filter and a wood box's capacity — #2729: the stack in <paramref name="fromSlot"/>, the one
    /// the player clicked, else the first one. Out of the crate: up to one full stack (or everything of that item with
    /// <paramref name="all"/>), as far as the backpack has room. Persisted; the contents go out to every open crate screen
    /// and the count to the world. Returns true if anything moved.</summary>
    public bool MoveContainerItem(string playerId, string containerId, string item, bool toContainer, bool all, int fromSlot = -1)
        => _containerSystem.MoveContainerItem(playerId, containerId, item, toContainer, all, fromSlot);

    /// <summary>Sends this world's containers AND its ground drop packets — the two lists always travel
    /// together, so every join/respawn/travel path stays in sync with one call.</summary>
    private void SendContainers(PlayerSession session) => _containerSystem.SendContainers(session);

    // ---------------- Test seams ----------------

    /// <summary>Test/util entrypoint: set a crate's filter as a given player (mirrors the filter intent).</summary>
    public void SetContainerFilterForTest(PlayerSession session, string containerId, string[] items)
        => HandleSetContainerFilter(session, new SetContainerFilterIntent { ContainerId = containerId, Items = items });

    /// <summary>Test seam: registers a container verbatim (a world loot container or a player crate).</summary>
    public void AddContainerForTest(StoredContainer container) => AddContainer(container);

    // IContainerHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    List<StoredContainer> IContainerHost.ContainerStore => _containers;

    SettlementInstance? IContainerHost.VillageOwningCache(StoredContainer container) => VillageOwningCache(container);

    string IContainerHost.OwnedContainerRefusal(StoredContainer container) => OwnedContainerRefusal(container);

    void IContainerHost.OnAchievementLoot(PlayerSession session) => OnAchievementLoot(session);

    void IContainerHost.RecordFoundDerelictLogs(PlayerSession session, List<string> taken) => RecordFoundDerelictLogs(session, taken);

    void IContainerHost.TryRevealLoreText(PlayerSession session, string site) => TryRevealLoreText(session, site);

    void IContainerHost.RecordStoryMilestone(string onceKey) => RecordStoryMilestone(onceKey);

    void IContainerHost.SendDropPackets(PlayerSession session) => SendDropPackets(session);
}
