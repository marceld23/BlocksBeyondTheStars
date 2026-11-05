// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The carried-contents store (#3098, #3099): what a packed transport crate or a filled storage bag holds. The item only
/// carries a record id in its key (<see cref="CarriedContents"/>); the record lives here, save-wide — not per planet — so it
/// resolves wherever the item travels: another planet, a station, the ship's hold, a crate, a trade partner's backpack.
/// <para>
/// Persisted as <see cref="StoredContainer"/> rows of the pseudo-location <see cref="StorePlanet"/>, the way the mission
/// reward depots use the container table (<c>"depot"</c>): every repository (SQLite, PostgreSQL, the in-memory/browser one)
/// already stores containers, atomically, with no schema change and no save migration. The rows never reach a world's
/// container list — a world loads only its own location's rows.
/// </para>
/// <para>
/// Duplication safety rests on three rules: a record key is minted only here (a client-named key is never handed out:
/// the Sandbox catalog and the admin give strip it), it never stacks (<see cref="GameContent.MaxStackOf"/> answers 1), and
/// a record is emptied exactly when its key is consumed (a placed crate, an emptied bag). Every write that moves items
/// between a record and a player runs in one transaction with that player's save — placing a packed crate, filling or
/// emptying a bag, and a player mining a filled transport crate (#3193: the new record and the packed key in the backpack
/// or the hold) — so a crash cannot leave the items in both places, nor a record that no saved item points at. A key that
/// fits nowhere goes to the ground as a drop packet, and a crate a crystal drill breaks hands its key to the crate beside
/// the drill; both are written right after the record.
/// </para>
/// </summary>
internal sealed class CarriedContentsSystem
{
    /// <summary>The pseudo-location the records are filed under in the container table.</summary>
    internal const string StorePlanet = "carried";

    /// <summary>A record's row id: this prefix and the record id of the item key.</summary>
    private const string RowPrefix = "carried_";

    private readonly IServerHost _host;
    private readonly GameContent _content;
    private readonly IWorldRepository _repo;

    /// <summary>Record id → record. Loaded on first use (the repository is only ready once the server has started).</summary>
    private Dictionary<string, StoredContainer>? _records;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal CarriedContentsSystem(IServerHost host, GameContent content, IWorldRepository repo)
    {
        _host = host;
        _content = content;
        _repo = repo;
    }

    private Dictionary<string, StoredContainer> Records => _records ??= LoadRecords();

    /// <summary>How many records the save holds (tests + inspection).</summary>
    internal int RecordCount => Records.Count;

    private Dictionary<string, StoredContainer> LoadRecords()
    {
        var records = new Dictionary<string, StoredContainer>(System.StringComparer.Ordinal);
        foreach (var row in _repo.ListContainers(StorePlanet))
        {
            if (row.Id.StartsWith(RowPrefix, System.StringComparison.Ordinal))
            {
                records[row.Id.Substring(RowPrefix.Length)] = row;
            }
        }

        return records;
    }

    /// <summary>What the record of <paramref name="itemKey"/> holds — empty for a key without a record, or one whose record
    /// is gone (the item then counts as empty).</summary>
    internal IReadOnlyList<ItemStack> ContentsOf(string? itemKey)
    {
        string id = CarriedContents.RecordId(itemKey);
        return id.Length > 0 && Records.TryGetValue(id, out var record) ? record.Items : System.Array.Empty<ItemStack>();
    }

    /// <summary>The number of items in a list of stacks.</summary>
    internal static int TotalOf(IEnumerable<ItemStack> stacks) => stacks.Where(s => !s.IsEmpty).Sum(s => s.Count);

    /// <summary>Makes <paramref name="items"/> the contents of <paramref name="itemKey"/> and returns the key that points at
    /// them now: a new record for a plain key, the same record for a key that has one, and the plain base key (record
    /// deleted) when nothing is left. Persisted at once.</summary>
    internal string Store(string itemKey, IEnumerable<ItemStack> items)
    {
        var kept = items.Where(s => !s.IsEmpty).Select(s => s.Clone()).ToList();
        string id = CarriedContents.RecordId(itemKey);
        if (kept.Count == 0)
        {
            if (id.Length > 0 && Records.Remove(id))
            {
                _repo.DeleteContainer(RowPrefix + id);
            }

            return CarriedContents.WithoutRecord(itemKey);
        }

        if (id.Length == 0)
        {
            id = NewRecordId();
        }

        if (!Records.TryGetValue(id, out var record))
        {
            record = new StoredContainer { Id = RowPrefix + id, Planet = StorePlanet, Kind = ItemKey.Base(itemKey) };
            Records[id] = record;
        }

        record.Items = kept;
        _repo.SaveContainer(record);
        return CarriedContents.WithRecord(itemKey, id);
    }

    /// <summary>Takes everything out of the record of <paramref name="itemKey"/> and deletes the record — the key is being
    /// consumed (a packed crate placed). Empty for a key without a record.</summary>
    internal List<ItemStack> Unpack(string itemKey)
    {
        string id = CarriedContents.RecordId(itemKey);
        if (id.Length == 0 || !Records.TryGetValue(id, out var record))
        {
            return new List<ItemStack>();
        }

        Records.Remove(id);
        _repo.DeleteContainer(record.Id);
        return record.Items.Where(s => !s.IsEmpty).Select(s => s.Clone()).ToList();
    }

    /// <summary>Why an item may not be thrown away (or otherwise destroyed) right now, or null: a bag or a crate that still
    /// holds things has to be emptied first — its contents would be gone with it.</summary>
    internal string? LossRefusal(string? itemKey)
        => CarriedContents.HasRecord(itemKey) && ContentsOf(itemKey).Any(s => !s.IsEmpty) ? "@srv.carried.not_empty" : null;

    private string NewRecordId()
    {
        string id;
        do
        {
            id = System.Guid.NewGuid().ToString("N").Substring(0, CarriedContents.RecordIdLength);
        }
        while (Records.ContainsKey(id));

        return id;
    }

    // ---------------- The bag screen (#3099) ----------------

    /// <summary>Opens the bag screen: answers with what the bag in <see cref="OpenBagIntent.Slot"/> holds (an empty answer
    /// for a slot without a bag, which closes the screen).</summary>
    internal void HandleOpenBag(PlayerSession session, OpenBagIntent intent)
    {
        if (BagAt(session.State.Inventory, intent.Slot) is null)
        {
            _host.Reject(session, "bag", "@srv.bag.none");
        }

        SendBagContents(session, intent.Slot);
    }

    internal void HandleMoveBagItem(PlayerSession session, MoveBagItemIntent intent)
        => MoveBagItem(session, intent.BagSlot, intent.Item, intent.ToBag, intent.All, intent.FromSlot);

    /// <summary>Moves items between the backpack and the bag in <paramref name="bagSlot"/> (see <see cref="MoveBagItemIntent"/>):
    /// into the bag as far as its <see cref="CarriedContents.BagCapacity"/> items reach, out of it as far as the backpack has
    /// room. The bag's key follows its contents (plain when empty, a record when not). Returns true if anything moved.</summary>
    internal bool MoveBagItem(PlayerSession session, int bagSlot, string? item, bool toBag, bool all, int fromSlot = -1)
    {
        var inv = session.State.Inventory;
        if (BagAt(inv, bagSlot) is not { } bag)
        {
            _host.Reject(session, "bag", "@srv.bag.none");
            SendBagContents(session, bagSlot);
            return false;
        }

        var contents = ContentsOf(bag.Item).Where(s => !s.IsEmpty).Select(s => s.Clone()).ToList();
        string key = item ?? string.Empty;
        string? refusal = toBag
            ? (key.Length == 0 ? StoreAll(inv, bagSlot, session.State.SelectedHotbarSlot, contents) : StoreOne(inv, bagSlot, key, all, fromSlot, contents))
            : (key.Length == 0 ? TakeAll(inv, contents) : TakeOne(inv, key, all, contents));
        if (refusal is not null)
        {
            if (refusal.Length > 0)
            {
                _host.Reject(session, "bag", refusal);
            }

            SendBagContents(session, bagSlot);
            return false;
        }

        // The record and the backpack are written in one go: a crash between the two cannot leave the items in both.
        _repo.RunInTransaction(() =>
        {
            inv.SetSlot(bagSlot, new ItemStack(Store(bag.Item, contents), 1));
            _repo.SavePlayer(session.State);
        });
        _host.SendInventory(session);
        SendBagContents(session, bagSlot);
        return true;
    }

    /// <summary>The bag in a backpack slot, or null. A bag never stacks (<c>maxStack</c> 1), so a slot holds one.</summary>
    private static ItemStack? BagAt(Inventory inv, int slot)
        => slot >= 0 && slot < inv.SlotCount && inv.Slots[slot] is { IsEmpty: false, Count: 1 } stack && CarriedContents.IsBag(stack.Item)
            ? stack
            : null;

    /// <summary>One kind into the bag: the clicked stack (or the first one), every stack of it with <paramref name="all"/>.
    /// Null = moved; "" = nothing to move; else the refusal.</summary>
    private string? StoreOne(Inventory inv, int bagSlot, string item, bool all, int fromSlot, List<ItemStack> contents)
    {
        if (!CarriedContents.FitsInBag(item) || _content.GetItem(item) is null)
        {
            return CarriedContents.IsCarrier(item) ? "@srv.bag.no_carriers" : string.Empty;
        }

        int room = CarriedContents.BagCapacity - TotalOf(contents);
        if (room <= 0)
        {
            return "@srv.bag.full";
        }

        int moved = 0;
        if (!all && fromSlot >= 0 && fromSlot < inv.SlotCount && fromSlot != bagSlot
            && inv.Slots[fromSlot] is { IsEmpty: false } clicked && clicked.Item == item)
        {
            moved = TakeFromSlot(inv, fromSlot, room); // exactly the stack that was clicked (#2729's rule for the crate)
        }
        else
        {
            for (int i = 0; i < inv.SlotCount && moved < room; i++)
            {
                if (i != bagSlot && inv.Slots[i] is { IsEmpty: false } s && s.Item == item)
                {
                    moved += TakeFromSlot(inv, i, room - moved);
                    if (!all)
                    {
                        break;
                    }
                }
            }
        }

        if (moved == 0)
        {
            return string.Empty;
        }

        AddTo(contents, item, moved);
        return null;
    }

    /// <summary>"Store all materials": every loose material, component and block of the backpack as far as the bag has room
    /// — the stack in your hand stays, like the hold's "stow all" keeps it (<paramref name="held"/>).</summary>
    private string? StoreAll(Inventory inv, int bagSlot, int held, List<ItemStack> contents)
    {
        int room = CarriedContents.BagCapacity - TotalOf(contents);
        if (room <= 0)
        {
            return "@srv.bag.full";
        }

        bool any = false;
        for (int i = 0; i < inv.SlotCount && room > 0; i++)
        {
            if (i == bagSlot || i == held || inv.Slots[i] is not { IsEmpty: false } s || !CarriedContents.FitsInBag(s.Item)
                || _content.GetItem(s.Item)?.Category is not (ItemCategory.Material or ItemCategory.Component or ItemCategory.Block))
            {
                continue;
            }

            string item = s.Item;
            int moved = TakeFromSlot(inv, i, room);
            AddTo(contents, item, moved);
            room -= moved;
            any = true;
        }

        return any ? null : "@srv.loot.nothing_to_stash";
    }

    /// <summary>One kind out of the bag: one full stack, or all of it with <paramref name="all"/>, as far as there is room.</summary>
    private string? TakeOne(Inventory inv, string item, bool all, List<ItemStack> contents)
    {
        var stack = contents.FirstOrDefault(s => s.Item == item);
        if (stack is null)
        {
            return string.Empty;
        }

        int max = _content.MaxStackOf(item);
        int want = all ? stack.Count : System.Math.Min(stack.Count, max);
        int taken = want - inv.Add(item, want, max);
        if (taken <= 0)
        {
            return "@inventory_full";
        }

        stack.Count -= taken;
        contents.RemoveAll(s => s.IsEmpty);
        return null;
    }

    /// <summary>"Take everything out", as far as the backpack has room.</summary>
    private string? TakeAll(Inventory inv, List<ItemStack> contents)
    {
        if (contents.Count == 0)
        {
            return string.Empty;
        }

        bool any = false;
        foreach (var stack in contents)
        {
            int taken = stack.Count - inv.Add(stack.Item, stack.Count, _content.MaxStackOf(stack.Item));
            stack.Count -= taken;
            any |= taken > 0;
        }

        contents.RemoveAll(s => s.IsEmpty);
        return any ? null : "@inventory_full";
    }

    private static int TakeFromSlot(Inventory inv, int slot, int most)
    {
        var stack = inv.Slots[slot]!;
        int take = System.Math.Min(stack.Count, most);
        stack.Count -= take;
        if (stack.Count <= 0)
        {
            inv.SetSlot(slot, null);
        }

        return take;
    }

    private static void AddTo(List<ItemStack> contents, string item, int count)
    {
        if (contents.FirstOrDefault(s => s.Item == item) is { } stack)
        {
            stack.Count += count;
        }
        else
        {
            contents.Add(new ItemStack(item, count));
        }
    }

    private void SendBagContents(PlayerSession session, int slot)
    {
        var bag = BagAt(session.State.Inventory, slot);
        var items = bag is null ? System.Array.Empty<ItemStack>() : ContentsOf(bag.Item);
        int index = 0;
        _host.Send(session, new BagContents
        {
            Slot = slot,
            BagKey = bag?.Item ?? string.Empty,
            Items = items.Where(s => !s.IsEmpty).Select(s => new NetItemStack { Slot = index++, Item = s.Item, Count = s.Count }).ToArray(),
            Total = TotalOf(items),
            Capacity = CarriedContents.BagCapacity,
        });
    }
}
