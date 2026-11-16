// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3732: a client can only name a seedling or a sample whose species entry it has. The entry used to travel with a
/// sample taken, an analysis and the full book at join — so a seedling out of a trade or a crate read as a plain
/// "Seedling" until the next join (and one in the hold even after it). Now every inventory update first sends the
/// species of the seed-carrying items the client is about to see and does not know yet. One place for every way an
/// item can arrive, instead of a patch per transfer path.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Sends the species entries this session's client lacks for the items in the backpack and, aboard, the
    /// hold — the two lists an inventory update carries. The sample case is left out: nothing enters it without its
    /// entry being sent. Cheap on purpose (it runs with every inventory update): a stack without a modifier costs one
    /// character scan, and a seed already sent one set lookup.</summary>
    private void SendHeldSpecies(PlayerSession session)
    {
        SendSpeciesOfStacks(session, session.State.Inventory);
        if (session.State.AboardShip)
        {
            SendSpeciesOfStacks(session, _ship.Cargo);
        }
    }

    private void SendSpeciesOfStacks(PlayerSession session, Inventory inventory)
    {
        var slots = inventory.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not { IsEmpty: false } stack || stack.Item.IndexOf(ItemKey.Separator) < 0
                || !BioItems.CarriesSpecies(ItemKey.Base(stack.Item)))
            {
                continue;
            }

            uint seed = ItemKey.Seed(stack.Item);
            if (seed != 0 && !session.Sync.BioBookSeeds.Contains(seed) && _bioRegister.ContainsKey(seed))
            {
                SendBioSpecies(session, seed);
            }
        }
    }
}
