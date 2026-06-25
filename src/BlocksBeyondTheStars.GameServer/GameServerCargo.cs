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
        => MoveCargo(session, intent.ToCargo, intent.Item, intent.BulkAll);

    /// <summary>Shared mover for the cargo intent + the test seam. Returns true if anything actually moved.</summary>
    private bool MoveCargo(PlayerSession session, bool toCargo, string item, bool bulkAll)
    {
        if (!session.State.AboardShip)
        {
            Reject(session, "cargo", "Step aboard the ship to use the cargo hold.");
            return false;
        }

        Inventory personal = session.State.Inventory;
        Inventory cargo = _ship.Cargo;
        Inventory src = toCargo ? personal : cargo;
        Inventory dst = toCargo ? cargo : personal;
        bool moved = false;

        if (bulkAll)
        {
            // "Stow all" moves loose materials/components only — tools, weapons and equipment stay with the
            // player — exactly like depositing into a storage crate (which also doesn't spare the quick-bar; the
            // category filter is the real protection). "Take all" pulls everything out of the hold, no filter.
            for (int i = 0; i < src.SlotCount; i++)
            {
                if (src.Slots[i] is { IsEmpty: false } s
                    && (!toCargo || IsStowable(s.Item)))
                {
                    moved |= MoveSlot(src, dst, i);
                }
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

    /// <summary>Loose materials/components stow into cargo; tools, weapons and equipment stay on the player.</summary>
    private bool IsStowable(string item)
        => _content.GetItem(item)?.Category is ItemCategory.Material or ItemCategory.Component;

    /// <summary>Moves slot <paramref name="index"/> of <paramref name="src"/> into <paramref name="dst"/>, leaving
    /// whatever did not fit (full destination) in place. Returns true if at least one item moved.</summary>
    private bool MoveSlot(Inventory src, Inventory dst, int index)
    {
        if (src.Slots[index] is not { IsEmpty: false } s)
        {
            return false;
        }

        int max = _content.GetItem(s.Item)?.MaxStack ?? 99;
        int leftover = dst.Add(s.Item, s.Count, max);
        if (leftover >= s.Count)
        {
            return false; // destination full → nothing moved, leave the slot untouched
        }

        src.SetSlot(index, leftover > 0 ? new ItemStack(s.Item, leftover) : null);
        return true;
    }

    /// <summary>Test seam: drive a cargo move for a player (sets both cursors first, like the dispatch path).</summary>
    public bool MoveCargoForTest(string playerId, bool toCargo, string item = "", bool bulkAll = false)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return false;
        }

        Serve(session);
        return MoveCargo(session, toCargo, item, bulkAll);
    }
}
