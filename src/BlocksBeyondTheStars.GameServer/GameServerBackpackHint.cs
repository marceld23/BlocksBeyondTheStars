// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2964: a mined drop tops up a stack that already sits in the backpack (<see cref="Inventory.Add"/> fills existing
/// stacks before free slots), so the quick-bar does not change even with a free slot in it — a player cut a birch and
/// reported the pale logs "never arrived". The first time a find lands only in the backpack, VEGA says where it went (an
/// advisor hint, muted with VEGA's hints); the HUD pickup row says it every time (the client's "· in your backpack").
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The once-flag id of the "your find is in the backpack" hint.</summary>
    internal const string BackpackPickupHint = "backpack_pickup";

    /// <summary>The one-time hints a mined block can raise: the first crystal (#2257) and the first find that went into the
    /// backpack only (#2964).</summary>
    private void OnBlockMinedHints(PlayerSession session, BlockDefinition def)
    {
        OnCrystalBlockMinedHint(session, def.Key);
        if (session.State.Milestones.Contains("vega:hint:" + BackpackPickupHint))
        {
            return;
        }

        foreach (var drop in def.Drops)
        {
            if (OnlyInBackpack(session.State.Inventory, drop.Item))
            {
                ShipAiHintOnce(session, BackpackPickupHint);
                return;
            }
        }
    }

    /// <summary>Whether <paramref name="item"/> (any dye, form or paint of it) sits in the backpack and nowhere on the
    /// quick-bar — where a pickup of it cannot be seen without opening the inventory.</summary>
    internal static bool OnlyInBackpack(Inventory inventory, string item)
    {
        bool inBackpack = false;
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            if (inventory.Slots[i] is not { } s || s.IsEmpty || ItemKey.Base(s.Item) != item)
            {
                continue;
            }

            if (i < HotbarSlots)
            {
                return false;
            }

            inBackpack = true;
        }

        return inBackpack;
    }
}
