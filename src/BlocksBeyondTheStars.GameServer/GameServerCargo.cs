// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Moving items between the player's personal inventory and the ship's cargo hold. The two containers are
/// already a single crafting pool while aboard (<see cref="MaterialPool"/>); this lets the player actively
/// shuffle bulk between them — manually per item, or in one "stow all" / "take all" sweep. Cargo only makes
/// sense while aboard the ship (in flight or standing in the landed cabin, where <c>UpdateAboard</c> has set
/// <see cref="PlayerState.AboardShip"/>), so every move is gated on that. Server-authoritative; the client
/// only sends the intent.
/// </summary>
public sealed partial class GameServer
{
    private void HandleMoveCargoItem(PlayerSession session, MoveCargoItemIntent intent)
        => MoveCargo(session, intent.ToCargo, intent.Item, intent.BulkAll, intent.Quiet, intent.FromSlot, intent.Count);

    /// <summary>Shared mover for the cargo intent + the test seam. Returns true if anything actually moved.
    /// #2729: <paramref name="fromSlot"/> moves exactly one stack of the source side (the one the player clicked), and
    /// <paramref name="count"/> &gt; 0 only that many items — of that stack, or without a slot of the item across its
    /// stacks ("take 5 along"). Both default to the old behaviour: every stack of <paramref name="item"/>.</summary>
    private bool MoveCargo(PlayerSession session, bool toCargo, string item, bool bulkAll, bool quiet = false, int fromSlot = -1, int count = 0)
    {
        if (!session.State.AboardShip)
        {
            Reject(session, "cargo", "@srv.misc.aboard_for_cargo");
            return false;
        }

        Inventory personal = session.State.Inventory;
        Inventory cargo = _ship.Cargo;
        Inventory src = toCargo ? personal : cargo;
        Inventory dst = toCargo ? cargo : personal;
        bool moved = false;

        if (bulkAll)
        {
            // "Stow all" moves loose materials, components and blocks — tools, weapons and suit gear (a spare helmet
            // too) stay with the player, and so does the stack in the player's hand (the selected quick-bar slot):
            // what you hold is what you are about to use. The rest of the quick-bar is stowed, because pickups fill
            // it first — sparing all nine slots would leave the freshly mined ore behind. "Take all" pulls
            // everything out of the hold, no filter.
            int held = toCargo ? session.State.SelectedHotbarSlot : -1;
            bool anyStowable = false;
            int stacks = 0;
            for (int i = 0; i < src.SlotCount; i++)
            {
                if (src.Slots[i] is { IsEmpty: false } s
                    && (!toCargo || (i != held && IsStowable(s.Item))))
                {
                    anyStowable = true;
                    if (MoveSlot(src, dst, i))
                    {
                        moved = true;
                        stacks++;
                    }
                }
            }

            if (toCargo)
            {
                ReportStowAll(session, src, held, anyStowable, stacks, quiet);
            }
        }
        else if (fromSlot >= 0 || !string.IsNullOrEmpty(item))
        {
            // An explicitly chosen item, in either direction (no quick-bar exemption — the player picked it deliberately):
            // one stack, a number of items, or every stack of it.
            int slot = fromSlot >= 0 ? ClickedSlot(src, fromSlot, item) : -1;
            if (slot >= 0)
            {
                item = src.Slots[slot]!.Item; // what the slot really holds (a dyed/shaped stack has a composite key)
            }

            bool any = slot >= 0 || (!string.IsNullOrEmpty(item) && src.CountOf(item) > 0);
            if (slot >= 0)
            {
                moved = MoveSlot(src, dst, slot, count); // #2729: exactly the clicked stack (or a part of it)
            }
            else if (fromSlot >= 0)
            {
                // The clicked slot changed before the click arrived (a pickup, another player's move): still one stack —
                // the first of that item — never every stack, which is not what the player asked for.
                int first = FirstSlotOf(src, item);
                moved = first >= 0 && MoveSlot(src, dst, first, count);
            }
            else if (count > 0)
            {
                moved = MoveCount(src, dst, item, count) > 0; // #2729: "take 5 along"
            }
            else
            {
                for (int i = 0; i < src.SlotCount; i++)
                {
                    if (src.Slots[i] is { IsEmpty: false } s && s.Item == item)
                    {
                        moved |= MoveSlot(src, dst, i);
                    }
                }
            }

            if (any && !moved && !quiet)
            {
                // There was something to move but the other side had no room — say so, a silent click reads as broken.
                Reject(session, "cargo", toCargo ? "@srv.cargo.full" : "@srv.cargo.backpack_full");
            }
        }

        if (moved)
        {
            SendInventory(session);
            if (toCargo)
            {
                RaiseGameEvent(session, GameEventKeys.CargoStowed, bulkAll ? "all" : item); // #2469
            }
        }

        return moved;
    }

    /// <summary>#2729: the slot a single-stack move names, or -1 when it is out of range, empty, or no longer holds
    /// <paramref name="item"/> (an empty <paramref name="item"/> accepts whatever the slot holds).</summary>
    private static int ClickedSlot(Inventory src, int slot, string item)
        => slot < src.SlotCount && src.Slots[slot] is { IsEmpty: false } s && (string.IsNullOrEmpty(item) || s.Item == item)
            ? slot
            : -1;

    /// <summary>The first slot holding <paramref name="item"/>, or -1.</summary>
    private static int FirstSlotOf(Inventory inv, string item)
    {
        for (int i = 0; i < inv.SlotCount && !string.IsNullOrEmpty(item); i++)
        {
            if (inv.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>#2729: moves up to <paramref name="count"/> of <paramref name="item"/> from <paramref name="src"/> into
    /// <paramref name="dst"/> — topping up the destination's stacks first and opening new ones up to the item's stack size
    /// (<see cref="Inventory.Add"/>), so only what fits leaves the source. The source gives from its LAST stack first: out
    /// of the backpack that is the pack before the quick-bar, so the stack in reach stays longest. Returns how many
    /// moved.</summary>
    private int MoveCount(Inventory src, Inventory dst, string item, int count)
    {
        int want = System.Math.Min(count, src.CountOf(item));
        if (want <= 0)
        {
            return 0;
        }

        int moved = want - dst.Add(item, want, MaxStackOf(item));
        int left = moved;
        for (int i = src.SlotCount - 1; i >= 0 && left > 0; i--)
        {
            if (src.Slots[i] is { IsEmpty: false } s && s.Item == item)
            {
                int take = System.Math.Min(left, s.Count);
                left -= take;
                src.SetSlot(i, s.Count > take ? new ItemStack(item, s.Count - take) : null);
            }
        }

        return moved;
    }

    private int MaxStackOf(string item) => _content.GetItem(item)?.MaxStack ?? ItemDefinition.DefaultMaxStack;

    /// <summary>Tells the player what "stow all" did — it used to stay silent when the hold was full or nothing
    /// qualified, which read exactly like a broken button.</summary>
    private void ReportStowAll(PlayerSession session, Inventory pack, int held, bool anyStowable, int stacks, bool quiet)
    {
        if (stacks == 0 && quiet)
        {
            return; // auto-stow on boarding with nothing to do — not worth a line
        }

        if (!anyStowable)
        {
            Reject(session, "cargo", "@srv.loot.nothing_to_stash");
            return;
        }

        if (stacks == 0)
        {
            Reject(session, "cargo", "@srv.cargo.full");
            return;
        }

        bool leftBehind = false;
        for (int i = 0; i < pack.SlotCount && !leftBehind; i++)
        {
            leftBehind = i != held && pack.Slots[i] is { IsEmpty: false } s && IsStowable(s.Item);
        }

        Send(session, new ServerMessage { Text = (leftBehind ? "@srv.cargo.stowed_partial:" : "@srv.cargo.stowed:") + stacks });
    }

    /// <summary>Loose materials, components and building blocks stow into cargo; tools, weapons and suit gear stay on
    /// the player. The same category rule a storage crate stashes by (<c>Stashable</c>) — blocks were missing here
    /// since #1264 added them there, so "stow all" left every stack of stone, glass and wall panels in the pack while
    /// it happily stowed a helmet (#1562). Suit gear is category Component too (a spare helmet, an oxygen tank) —
    /// equipment, not cargo, so it stays with the player (#2138).</summary>
    private bool IsStowable(string item)
        => _content.GetItem(item) is { } def
           && def.Category is ItemCategory.Material or ItemCategory.Component or ItemCategory.Block
           && !SuitEquipment.IsSuitGear(def);

    /// <summary>Moves slot <paramref name="index"/> of <paramref name="src"/> into <paramref name="dst"/> — the whole
    /// stack, or with <paramref name="count"/> &gt; 0 only that many of it (#2729: the stack is split, the part merges
    /// into the destination's stacks) — leaving whatever did not fit (full destination) in place. Returns true if at
    /// least one item moved.</summary>
    private bool MoveSlot(Inventory src, Inventory dst, int index, int count = 0)
    {
        if (src.Slots[index] is not { IsEmpty: false } s)
        {
            return false;
        }

        int want = count > 0 ? System.Math.Min(count, s.Count) : s.Count;
        int leftover = dst.Add(s.Item, want, MaxStackOf(s.Item));
        if (leftover >= want)
        {
            return false; // destination full → nothing moved, leave the slot untouched
        }

        int stays = s.Count - (want - leftover);
        src.SetSlot(index, stays > 0 ? new ItemStack(s.Item, stays) : null);
        return true;
    }

    /// <summary>Test seam: drive a cargo move for a player (sets both cursors first, like the dispatch path).</summary>
    public bool MoveCargoForTest(string playerId, bool toCargo, string item = "", bool bulkAll = false, bool quiet = false,
        int fromSlot = -1, int count = 0)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return false;
        }

        Serve(session);
        return MoveCargo(session, toCargo, item, bulkAll, quiet, fromSlot, count);
    }
}
