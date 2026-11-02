// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The storage bag screen (#3099): open a bag in the backpack, move items in and out.</summary>
    public sealed partial class NetworkClient
    {
        /// <summary>What the storage bag in a backpack slot holds — on opening the bag screen and after every move.</summary>
        public event Action<BagContents>? BagContentsReceived;

        /// <summary>Opens the bag in backpack slot <paramref name="slot"/>: the server answers with its contents.</summary>
        public void SendOpenBag(int slot) => Send(new OpenBagIntent { Slot = slot });

        /// <summary>Moves <paramref name="item"/> between the backpack and the bag in <paramref name="bagSlot"/>: into the bag
        /// the clicked stack (<paramref name="fromSlot"/>) or every stack with <paramref name="all"/>, out of it one stack or all
        /// of it. An empty <paramref name="item"/> with <paramref name="all"/> moves every kind ("store all" / "take all").</summary>
        public void SendMoveBagItem(int bagSlot, string item, bool toBag, bool all = false, int fromSlot = -1)
            => Send(new MoveBagItemIntent { BagSlot = bagSlot, Item = item ?? string.Empty, ToBag = toBag, All = all, FromSlot = fromSlot });
    }
}
