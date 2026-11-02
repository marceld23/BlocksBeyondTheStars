// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Items that carry items (#3098, #3099): the facade of <see cref="CarriedContentsSystem"/> (ADR 0019), plus the glue the
/// world needs — a mined transport crate packs its contents into the dropped item, a placed one unpacks them into the new
/// crate — and the test seams.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3098: a transport crate that holds something keeps it when it is mined. Called by the break path once the
    /// drops are composed: the crate's stacks move into a carried-contents record, the dropped <c>transport_crate</c> item
    /// becomes the key that points at it, and the container is removed — so <c>RemoveCrateContainer</c> finds nothing left
    /// to hand to the miner. An empty crate drops as a plain one and stacks with the others. Record, container and the
    /// container row change in one transaction.</summary>
    private void PackTransportCrate(BlockDefinition def, Vector3i pos, List<ItemAmount> yield)
    {
        if (def.Key != CarriedContents.TransportCrate
            || _containers.FirstOrDefault(c => c.Kind == "crate" && c.Position.Equals(pos)) is not { } container
            || container.Items.All(s => s.IsEmpty))
        {
            return;
        }

        // The crate's own drop (one item): with none to carry them in, the contents go to the miner as for any crate.
        int drop = yield.FindIndex(a => a.Item == CarriedContents.TransportCrate && a.Count == 1);
        if (drop < 0)
        {
            return;
        }

        _repo.RunInTransaction(() =>
        {
            yield[drop] = new ItemAmount(_carriedContentsSystem.Store(CarriedContents.TransportCrate, container.Items), 1);
            _containers.Remove(container);
            _repo.DeleteContainer(container.Id);
        });
        BroadcastContainers();
    }

    /// <summary>#3098: places a storage crate — and when the item was a packed transport crate, what it carried goes into the
    /// new crate and its record is gone (the item was paid for already, see <c>TryPayForPlacement</c>). The record, the new
    /// container and the player's backpack are written in one transaction.</summary>
    private void PlaceCrateFrom(PlayerSession session, Vector3i pos, string itemKey)
    {
        if (!CarriedContents.HasRecord(itemKey))
        {
            PlaceCrate(pos);
            return;
        }

        _repo.RunInTransaction(() =>
        {
            _containerSystem.PlaceCrate(pos, _carriedContentsSystem.Unpack(itemKey));
            _repo.SavePlayer(session.State);
        });
    }

    /// <summary>Why an item must not be destroyed now (a bag or a crate that still holds things), or null.</summary>
    private string? CarriedLossRefusal(string? itemKey) => _carriedContentsSystem.LossRefusal(itemKey);

    private void HandleOpenBag(PlayerSession session, OpenBagIntent intent) => _carriedContentsSystem.HandleOpenBag(session, intent);

    private void HandleMoveBagItem(PlayerSession session, MoveBagItemIntent intent) => _carriedContentsSystem.HandleMoveBagItem(session, intent);

    // ---------------- Test seams ----------------

    /// <summary>Test seam: what a bag or a packed crate key carries (empty for none).</summary>
    public IReadOnlyList<ItemStack> CarriedContentsForTest(string itemKey) => _carriedContentsSystem.ContentsOf(itemKey);

    /// <summary>Test seam: how many carried-contents records the save holds.</summary>
    public int CarriedRecordCountForTest => _carriedContentsSystem.RecordCount;

    /// <summary>Test seam: a click on the bag screen, as <see cref="MoveBagItemIntent"/> would carry it.</summary>
    public bool MoveBagItemForTest(string playerId, int bagSlot, string item, bool toBag, bool all = false, int fromSlot = -1)
        => FindSessionByPlayerId(playerId) is { } session && _carriedContentsSystem.MoveBagItem(session, bagSlot, item, toBag, all, fromSlot);
}
