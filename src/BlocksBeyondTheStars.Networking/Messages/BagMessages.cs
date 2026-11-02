// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>#3099: the client opens the storage bag in backpack slot <see cref="Slot"/> (the bag screen). The server
/// checks the slot holds a bag and answers with <see cref="BagContents"/>. Nothing is kept open on the server — a bag only
/// changes through <see cref="MoveBagItemIntent"/>, and every move answers with the contents again.</summary>
public sealed class OpenBagIntent
{
    public int Slot { get; set; } = -1;
}

/// <summary>#3099: the client moves items between its backpack and the storage bag in backpack slot <see cref="BagSlot"/>
/// — a click on the bag screen. <see cref="ToBag"/> true moves <see cref="Item"/> in (the clicked stack in
/// <see cref="FromSlot"/>, or every stack of it with <see cref="All"/>), as far as the bag's 1000 items reach; false takes
/// it out (one stack, or everything of that kind with <see cref="All"/>). An empty <see cref="Item"/> with
/// <see cref="All"/> moves every kind: "take everything out" or "store all materials". The server validates the slot and
/// refuses a bag in a bag and a packed transport crate.</summary>
public sealed class MoveBagItemIntent
{
    public int BagSlot { get; set; } = -1;
    public string Item { get; set; } = string.Empty;
    public bool ToBag { get; set; }
    public bool All { get; set; }
    public int FromSlot { get; set; } = -1;
}

/// <summary>#3099: server → client, what the storage bag in backpack slot <see cref="Slot"/> holds — on
/// <see cref="OpenBagIntent"/> and after every <see cref="MoveBagItemIntent"/>. <see cref="BagKey"/> is the bag's item key
/// right now (an empty bag is the plain key, a filled one carries its record), <see cref="Total"/> the items inside and
/// <see cref="Capacity"/> the most it holds. An empty <see cref="BagKey"/> means the slot holds no bag any more (the
/// screen closes).</summary>
public sealed class BagContents
{
    public int Slot { get; set; } = -1;
    public string BagKey { get; set; } = string.Empty;
    public NetItemStack[] Items { get; set; } = System.Array.Empty<NetItemStack>();
    public int Total { get; set; }
    public int Capacity { get; set; }
}
