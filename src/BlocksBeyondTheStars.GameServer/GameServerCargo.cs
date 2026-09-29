// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

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
        => MoveCargo(session, intent.ToCargo, intent.Item, intent.BulkAll, intent.Quiet);

    /// <summary>Shared mover for the cargo intent + the test seam. Returns true if anything actually moved.</summary>
    private bool MoveCargo(PlayerSession session, bool toCargo, string item, bool bulkAll, bool quiet = false)
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
        else if (!string.IsNullOrEmpty(item))
        {
            // Move every stack of one explicitly chosen item, in either direction (no quick-bar exemption —
            // the player picked it deliberately).
            for (int i = 0; i < src.SlotCount; i++)
            {
                if (src.Slots[i] is { IsEmpty: false } s && s.Item == item)
                {
                    moved |= MoveSlot(src, dst, i);
                }
            }
        }

        if (moved)
        {
            SendInventory(session);
        }

        return moved;
    }

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

    /// <summary>Moves slot <paramref name="index"/> of <paramref name="src"/> into <paramref name="dst"/>, leaving
    /// whatever did not fit (full destination) in place. Returns true if at least one item moved.</summary>
    private bool MoveSlot(Inventory src, Inventory dst, int index)
    {
        if (src.Slots[index] is not { IsEmpty: false } s)
        {
            return false;
        }

        int max = _content.GetItem(s.Item)?.MaxStack ?? ItemDefinition.DefaultMaxStack;
        int leftover = dst.Add(s.Item, s.Count, max);
        if (leftover >= s.Count)
        {
            return false; // destination full → nothing moved, leave the slot untouched
        }

        src.SetSlot(index, leftover > 0 ? new ItemStack(s.Item, leftover) : null);
        return true;
    }

    /// <summary>Test seam: drive a cargo move for a player (sets both cursors first, like the dispatch path).</summary>
    public bool MoveCargoForTest(string playerId, bool toCargo, string item = "", bool bulkAll = false, bool quiet = false)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return false;
        }

        Serve(session);
        return MoveCargo(session, toCargo, item, bulkAll, quiet);
    }
}
