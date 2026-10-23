// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

public sealed class SelectHotbarIntent
{
    public int Slot { get; set; }

    /// <summary>#2525: true when the client re-sends the slot it shows after a join or a world change, rather than the
    /// player picking one. The server adopts the slot all the same (it mines with its own copy) but reports no
    /// <c>hotbar:select</c> game event for it, so a rejoin does not tick off a tutorial step. Older clients never set
    /// it, and older servers ignore it.</summary>
    public bool Resync { get; set; }
}

/// <summary>
/// Client → server: rearrange the personal inventory by swapping two slots (B58 — customising the quick-bar,
/// which is just inventory slots 0–8). The server validates indices + swaps. <see cref="ToSlot"/> = -1 means
/// "stow out of the quick-bar": move the item into the first free backpack slot (≥ the quick-bar size).
/// </summary>
public sealed class MoveItemIntent
{
    public int FromSlot { get; set; }
    public int ToSlot { get; set; }
}

/// <summary>
/// Client → server (#2110): wear the gear in backpack slot <see cref="FromSlot"/> in equipment slot <see cref="Slot"/>
/// (an <c>EquipSlot</c> index; −1 = the slot the item belongs to, a free module slot for a module). The server checks
/// the item's <c>equipSlot</c> and swaps whatever was worn there back into the backpack slot.
/// </summary>
public sealed class EquipItemIntent
{
    public int FromSlot { get; set; }
    public int Slot { get; set; } = -1;

    /// <summary>#2289: <see cref="FromSlot"/> is a slot of the ship's cargo hold, not of the backpack — wear a piece
    /// straight from the hold (requires being aboard). The piece worn before goes into the backpack, or back into the
    /// freed hold slot when the backpack is full.</summary>
    public bool FromCargo { get; set; }
}

/// <summary>Client → server (#2110): take the gear off equipment slot <see cref="Slot"/> into backpack slot
/// <see cref="ToSlot"/> (−1 = the first free backpack slot; a slot holding gear that fits swaps).</summary>
public sealed class UnequipItemIntent
{
    public int Slot { get; set; }
    public int ToSlot { get; set; } = -1;
}

/// <summary>Client eats/uses a consumable item (food heals, poison harms). The server applies it.</summary>
public sealed class ConsumeItemIntent
{
    public string ItemKey { get; set; } = string.Empty;
}

/// <summary>Client right-clicks a held gadget (item 36 — field medkit / stasis projector / terrain blaster).
/// The server validates the gadget, suit energy + cooldown, then applies the keyed effect at the aim point.</summary>
public sealed class UseGadgetIntent
{
    public string GadgetKey { get; set; } = string.Empty;
    public float X { get; set; } // aim/target point (block-blast centre, stasis centre); ignored by the medkit
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>
/// Client permanently destroys an item it no longer wants (#599) — every other path (cargo hold, storage
/// crate, trade) only <i>stores</i> it, so without this a stack of 300 dirt is carried around forever.
/// Addressed by <see cref="Slot"/> rather than by item key: a dyed/shaped stack carries a composite key
/// (<see cref="Shared.State.ItemKey"/>), and the slot is the one unambiguous handle on what the player
/// actually clicked. The server then discards <b>every</b> stack of that same key, so one confirmed click
/// clears the item out completely. The starter kit is refused — see <c>GameServer.IsStarterKitItem</c>.
/// </summary>
public sealed class DiscardItemIntent
{
    /// <summary>The slot the player is discarding (identifies the item key to drop).</summary>
    public int Slot { get; set; }

    /// <summary>true = the slot is in the ship's cargo hold rather than the backpack. The hold is where bulk
    /// junk actually piles up ("stow all" sweeps it there), so it needs its own bin; requires being aboard.</summary>
    public bool FromCargo { get; set; }
}

/// <summary>
/// Client moves items between the personal inventory and the ship's cargo hold. Only valid while aboard the
/// ship (in flight or standing in the landed cabin — see <c>UpdateAboard</c>); the server rejects it otherwise.
/// Modes:
/// <list type="bullet">
/// <item><see cref="BulkAll"/> = true: move ALL eligible — when <see cref="ToCargo"/> only loose materials/
/// components (tools/weapons/equipment stay), like a storage crate; when taking out, everything.</item>
/// <item><see cref="FromSlot"/> set (#2729): move exactly that stack of the source side — or only
/// <see cref="Count"/> items of it.</item>
/// <item><see cref="Item"/> set with a <see cref="Count"/> (#2729): move that many of the item, split off its stacks
/// on the source side and merged into the destination's.</item>
/// <item><see cref="Item"/> set alone: move every stack of that one item in the chosen direction.</item>
/// </list>
/// </summary>
public sealed class MoveCargoItemIntent
{
    /// <summary>true = personal inventory → cargo hold; false = cargo hold → personal inventory.</summary>
    public bool ToCargo { get; set; }

    /// <summary>Move every stack of this item key (ignored when <see cref="BulkAll"/> is set; "" = none). With
    /// <see cref="FromSlot"/> it names what the clicked slot should hold.</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>#2729: the slot of the source side (the backpack when <see cref="ToCargo"/>, else the hold) whose stack
    /// moves — the one the player clicked, not every stack of that item. -1 = none. Contractless-additive: older clients
    /// send none and keep the all-stacks behaviour; an older server ignores it and moves every stack, as before.</summary>
    public int FromSlot { get; set; } = -1;

    /// <summary>#2729: how many items move ("take 5 along"); 0 = the whole stack (with <see cref="FromSlot"/>) or every
    /// stack (with <see cref="Item"/> alone). Ignored by <see cref="BulkAll"/>. Contractless-additive like
    /// <see cref="FromSlot"/>.</summary>
    public int Count { get; set; }

    /// <summary>Move all eligible items at once ("stow all" / "take all"); overrides <see cref="Item"/>.</summary>
    public bool BulkAll { get; set; }

    /// <summary>An automatic sweep (auto-stow on boarding): report what was stowed, but say nothing when there was
    /// nothing to stow or the hold was full — the player didn't ask. Older clients leave it false.</summary>
    public bool Quiet { get; set; }
}

/// <summary>Client loads food from its inventory into the suit's ration dispenser (auto-eaten when hungry).</summary>
public sealed class LoadRationIntent
{
    public string ItemKey { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
}

/// <summary>A single item stack in an inventory update.</summary>
public sealed class NetItemStack
{
    public int Slot { get; set; }
    public string Item { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class InventoryUpdate
{
    public NetItemStack[] Personal { get; set; } = System.Array.Empty<NetItemStack>();
    public NetItemStack[] Cargo { get; set; } = System.Array.Empty<NetItemStack>();

    /// <summary>#2110: the worn suit gear by <c>EquipSlot</c> index, and how many personal slots the server keeps (36
    /// since #2110; a pre-#2110 client never reads it and keeps its own 24).</summary>
    public NetItemStack[] Equipment { get; set; } = System.Array.Empty<NetItemStack>();
    public int PersonalSlotCount { get; set; }

    /// <summary>Blueprint keys the player has unlocked — lets the client show craftable/locked status.</summary>
    public string[] UnlockedBlueprints { get; set; } = System.Array.Empty<string>();

    /// <summary>#1535 (protocol v4): true = the unlocked set has not changed since this session's last
    /// InventoryUpdate, so <see cref="UnlockedBlueprints"/> is omitted and the client keeps what it has (the
    /// list used to ride every pickup — 100+ keys, 2–3 KB, ReliableOrdered).</summary>
    public bool BlueprintsUnchanged { get; set; }

    /// <summary>The player's current knowledge total (kept in sync so the HUD/menu/trade panel show it).</summary>
    public int KnowledgePoints { get; set; }

    /// <summary>Total slots in the ship's cargo hold (capacity, grows with cargo modules), so the cargo tab can
    /// show "used/total". 0 when not aboard (the cargo list is empty then anyway).</summary>
    public int CargoSlotCount { get; set; }

    /// <summary>The sample case (#2201): samples with their species seed in the key, by slot. Omitted (with
    /// <see cref="SamplesUnchanged"/> set) when nothing changed since this session's last update, like the blueprints.</summary>
    public NetItemStack[] Samples { get; set; } = System.Array.Empty<NetItemStack>();
    public bool SamplesUnchanged { get; set; }
}
