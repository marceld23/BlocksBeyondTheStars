// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A combined view over the inventories a player may currently draw from: their personal
/// inventory plus, when aboard the ship, the cargo hold (technical requirements §15 — ship
/// cargo counts toward crafting when the player is inside the ship). Adds prefer the
/// personal inventory and spill to cargo.
/// </summary>
public sealed class MaterialPool
{
    private readonly GameContent _content;
    private readonly Inventory _personal;
    private readonly Inventory? _cargo;

    public MaterialPool(GameContent content, PlayerState player, ShipState ship)
    {
        _content = content;
        _personal = player.Inventory;
        _cargo = player.AboardShip ? ship.Cargo : null;
    }

    public int Count(string item) => _personal.CountOf(item) + (_cargo?.CountOf(item) ?? 0);

    public bool Has(IEnumerable<ItemAmount> items)
    {
        foreach (var need in items)
        {
            if (Count(need.Item) < need.Count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes the listed amounts (personal first, then cargo). Caller must check <see cref="Has"/> first.</summary>
    public void Remove(IEnumerable<ItemAmount> items)
    {
        foreach (var need in items)
        {
            int remaining = need.Count;
            int fromPersonal = System.Math.Min(remaining, _personal.CountOf(need.Item));
            if (fromPersonal > 0)
            {
                _personal.Remove(need.Item, fromPersonal);
                remaining -= fromPersonal;
            }

            if (remaining > 0)
            {
                _cargo?.Remove(need.Item, remaining);
            }
        }
    }

    /// <summary>
    /// Total items this pool could not store since it was created (#600). Most callers hand out drops or craft
    /// outputs in a loop and have no use for a per-call leftover — they check this once at the end and warn the
    /// player, so a full backpack + full hold stops eating items unannounced.
    /// <para>
    /// This is the *after the fact* half of the story. Where the action can still be refused, prefer
    /// <see cref="CanFit"/> and never consume anything in the first place.
    /// </para>
    /// </summary>
    public int Overflow { get; private set; }

    /// <summary>
    /// True when every listed amount would fit (personal inventory first, then cargo when aboard).
    /// Dry-runs the adds against <b>clones</b> of both containers, so the answer accounts for stack
    /// top-up, empty-slot allocation and several outputs competing for the same free slots — exactly
    /// what <see cref="Add"/> would do, without mutating anything.
    /// <para>
    /// Callers that consume inputs before producing outputs MUST check this first: crafting with a full
    /// inventory used to destroy the output <i>and</i> the already-consumed inputs while still reporting
    /// success (reported by a player as "Items futsch" — crafted glass vanished, 24/24 slots occupied).
    /// </para>
    /// </summary>
    public bool CanFit(IEnumerable<ItemAmount> items)
    {
        var personal = _personal.Clone();
        var cargo = _cargo?.Clone();

        foreach (var add in items)
        {
            if (add.Count <= 0)
            {
                continue;
            }

            int maxStack = _content.MaxStackOf(add.Item);
            int leftover = personal.Add(add.Item, add.Count, maxStack);
            if (leftover > 0 && cargo is not null)
            {
                leftover = cargo.Add(add.Item, leftover, maxStack);
            }

            if (leftover > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds items, personal inventory first then cargo. Returns the amount that did not fit anywhere
    /// (0 = fully stored) and accumulates it into <see cref="Overflow"/>. <b>A non-zero return means those
    /// items were destroyed</b> — check <see cref="CanFit"/> up front where the action can still be refused,
    /// or handle the leftover (throw it away deliberately, warn via <see cref="Overflow"/>).
    /// </summary>
    public int Add(string item, int count)
    {
        int maxStack = _content.MaxStackOf(item);
        int leftover = _personal.Add(item, count, maxStack);
        if (leftover > 0 && _cargo is not null)
        {
            leftover = _cargo.Add(item, leftover, maxStack);
        }

        Overflow += leftover;
        return leftover;
    }
}
