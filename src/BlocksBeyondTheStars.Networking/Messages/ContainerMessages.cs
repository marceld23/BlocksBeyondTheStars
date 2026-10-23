// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client loots a nearby container (salvage capsule / corpse). Server validates proximity + transfers.</summary>
public sealed class LootContainerIntent
{
    public string ContainerId { get; set; } = string.Empty;
}

/// <summary>Client stashes its loose materials into a nearby storage crate (Task 5 Stage 3b).</summary>
public sealed class DepositContainerIntent
{
    public string ContainerId { get; set; } = string.Empty;
}

/// <summary>Client sets which items a storage crate accepts on a stash (#1032). <see cref="Items"/> carries
/// base item keys (no colour/shape modifiers — the server matches stashed stacks by their base key, so a
/// dyed variant of an allowed material still fits). An empty array clears the filter (everything allowed).
/// The server validates proximity and drops unknown or non-stashable keys.</summary>
public sealed class SetContainerFilterIntent
{
    public string ContainerId { get; set; } = string.Empty;
    public string[] Items { get; set; } = System.Array.Empty<string>();
}

/// <summary>#2436: the client opens (or closes) the crate screen for a storage crate. While a crate is open the server
/// answers with <see cref="ContainerContents"/> and keeps re-sending it after every change — the contents are not on
/// the count-only <see cref="NetContainer"/> broadcast on purpose. The server validates proximity.</summary>
public sealed class OpenContainerIntent
{
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>True = opened (send the contents, keep them fresh); false = the screen closed.</summary>
    public bool Open { get; set; } = true;
}

/// <summary>#2436: the client moves one kind of item between its backpack and a storage crate — a click on a stack in the
/// crate screen. <see cref="ToContainer"/> true moves it in (the crate's filter and a wood box's capacity still apply),
/// false takes it out. <see cref="All"/> moves every stack of that item, else one stack. Any item category goes in by
/// hand — tools and food too; only the bulk stash (H) keeps to loose materials. The server validates proximity.</summary>
public sealed class MoveContainerItemIntent
{
    public string ContainerId { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public bool ToContainer { get; set; }
    public bool All { get; set; }

    /// <summary>#2729: the backpack slot that was clicked, so exactly that stack goes in (not the first stack of the same
    /// item). Only read when <see cref="ToContainer"/> is set and <see cref="All"/> is not; a slot that no longer holds
    /// <see cref="Item"/> falls back to the first stack. Contractless-additive: older clients send none (-1 = the first
    /// stack, as before), an older server ignores it.</summary>
    public int FromSlot { get; set; } = -1;
}

/// <summary>#2436: server → client, the stacks inside one storage crate — sent on <see cref="OpenContainerIntent"/> and
/// after every change while the crate screen is open. <see cref="StackLimit"/> is the number of distinct stacks a wood
/// box holds (0 = unbounded, the workshop crate); <see cref="Filter"/> mirrors the crate's whitelist.</summary>
public sealed class ContainerContents
{
    public string ContainerId { get; set; } = string.Empty;
    public NetItemStack[] Items { get; set; } = System.Array.Empty<NetItemStack>();
    public int StackLimit { get; set; }
    public string[] Filter { get; set; } = System.Array.Empty<string>();
}

/// <summary>A lootable container in the world — a salvage capsule or a defeated player's corpse.</summary>
public sealed class NetContainer
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "container";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int ItemCount { get; set; }

    /// <summary>Base item keys this crate accepts on a stash (#1032); empty = everything. Contents stay
    /// count-only on purpose — the filter is small and changes rarely, an item list would ride every
    /// stash/loot broadcast.</summary>
    public string[] Filter { get; set; } = System.Array.Empty<string>();

    /// <summary>#2726: who this container belongs to when it is not the players' to take — the name of the intact village a
    /// scavenge cache stands in (an older world spawned those in lived-in villages). Empty = anybody may loot it. The HUD
    /// says whose it is instead of offering the loot key; the server refuses the take either way. Contractless-additive:
    /// an older client never reads it and only meets the refusal.</summary>
    public string OwnedBy { get; set; } = string.Empty;
}

/// <summary>Lootable containers on the current planet (salvage capsules / corpses), sent on join + on change.
/// Ground drop packets are deliberately NOT in here — they ride <see cref="DropPacketList"/> so they cannot
/// hijack the crate/capsule loot prompt (#853).</summary>
public sealed class ContainerList
{
    public NetContainer[] Containers { get; set; } = System.Array.Empty<NetContainer>();
}

/// <summary>One ground drop packet: the block bundle a full inventory left lying in the world (#853). The
/// client renders a small tumbling mini-block for it; picking it up is automatic (server-side proximity),
/// so there is no matching intent. <see cref="TopItem"/> is the biggest stack in the bundle and decides the
/// icon; <see cref="StackCount"/>/<see cref="TotalCount"/> label it.</summary>
public sealed class NetDropPacket
{
    public string Id { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string TopItem { get; set; } = string.Empty;
    public int StackCount { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>All ground drop packets on the player's current body (server → client), sent on join and
/// whenever one is spilled, topped up or collected.</summary>
public sealed class DropPacketList
{
    public NetDropPacket[] Packets { get; set; } = System.Array.Empty<NetDropPacket>();
}
