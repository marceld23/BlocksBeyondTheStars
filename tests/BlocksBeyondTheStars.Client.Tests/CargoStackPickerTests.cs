// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2729: moving a single stack or a chosen amount between the backpack and the cargo hold — the menu's arithmetic
/// (which slot "this stack" names, which amounts "How many?" offers) and the new intent fields over the real wire to the
/// real server.
/// </summary>
public sealed class CargoStackPickerTests
{
    private static NetItemStack S(int slot, string item, int count) => new() { Slot = slot, Item = item, Count = count };

    [Fact]
    public void ResolveSlot_KeepsTheClickedStack_ElseTheFirstOfTheItem()
    {
        var stacks = new[] { S(3, "berries", 20), S(5, "stone", 9), S(7, "berries", 4) };

        Assert.Equal(7, CargoStackPicker.ResolveSlot(stacks, "berries", 7));  // the clicked one
        Assert.Equal(3, CargoStackPicker.ResolveSlot(stacks, "berries", 5));  // slot 5 holds stone now → first berries
        Assert.Equal(3, CargoStackPicker.ResolveSlot(stacks, "berries", -1)); // nothing clicked yet
        Assert.Equal(-1, CargoStackPicker.ResolveSlot(stacks, "medpack", 3)); // none of it on this side
        Assert.Equal(-1, CargoStackPicker.ResolveSlot(null, "berries", 3));
    }

    [Fact]
    public void CountsAndTotals_ReadTheSnapshot()
    {
        var stacks = new[] { S(3, "berries", 20), S(5, "stone", 9), S(7, "berries", 4) };

        Assert.Equal(4, CargoStackPicker.CountInSlot(stacks, 7));
        Assert.Equal(0, CargoStackPicker.CountInSlot(stacks, 8));
        Assert.Equal(24, CargoStackPicker.Total(stacks, "berries"));
        Assert.Equal(0, CargoStackPicker.Total(stacks, "medpack"));
    }

    [Fact]
    public void Amounts_OfferOneFiveAndHalf_BelowTheTotal_EachOnce()
    {
        Assert.Equal(new[] { (1, false), (5, false), (10, true) }, CargoStackPicker.Amounts(20));
        Assert.Equal(new[] { (1, false), (5, false), (8, true) }, CargoStackPicker.Amounts(15)); // half rounds up
        Assert.Equal(new[] { (1, false), (5, false) }, CargoStackPicker.Amounts(10));            // half = 5: once
        Assert.Equal(new[] { (1, false), (3, true) }, CargoStackPicker.Amounts(5));              // 5 is "all" already
        Assert.Equal(new[] { (1, false), (2, true) }, CargoStackPicker.Amounts(3));
        Assert.Equal(new[] { (1, false) }, CargoStackPicker.Amounts(2));                         // half = 1: once
        Assert.Empty(CargoStackPicker.Amounts(1));
        Assert.Empty(CargoStackPicker.Amounts(0));
    }

    [Fact]
    public void TheNewFields_CrossTheWire_AndAnOlderCallStillMovesEveryStack()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content, passObjects: false);
        h.Join("Pilot");
        var state = h.Server.Sessions.Values.First(s => s.State.PlayerId == h.JoinAccepted!.PlayerId).State;
        state.AboardShip = true; // no physical ship in this world, so the flag stays as set
        var pack = state.Inventory;
        for (int i = 0; i < pack.SlotCount; i++)
        {
            pack.SetSlot(i, null);
        }

        for (int i = 0; i < h.Server.Ship.Cargo.SlotCount; i++)
        {
            h.Server.Ship.Cargo.SetSlot(i, null);
        }

        pack.SetSlot(10, new ItemStack("berries", 20));
        pack.SetSlot(11, new ItemStack("berries", 12));
        pack.SetSlot(12, new ItemStack("berries", 7));

        // One stack, by its slot.
        h.Client.SendMoveCargoItem(toCargo: true, item: "berries", fromSlot: 11);
        Assert.True(h.PumpUntil(() => h.Server.Ship.Cargo.CountOf("berries") == 12), "the clicked stack should reach the hold");
        Assert.Null(pack.Slots[11]);
        Assert.Equal(27, pack.CountOf("berries"));

        // Five of the item back out ("take 5 along").
        h.Client.SendMoveCargoItem(toCargo: false, item: "berries", count: 5);
        Assert.True(h.PumpUntil(() => h.Server.Ship.Cargo.CountOf("berries") == 7), "five should come back out");
        Assert.Equal(32, pack.CountOf("berries"));

        // The call an older client makes: every stack.
        h.Client.SendMoveCargoItem(toCargo: true, item: "berries");
        Assert.True(h.PumpUntil(() => pack.CountOf("berries") == 0), "every stack should go into the hold");
        Assert.Equal(39, h.Server.Ship.Cargo.CountOf("berries"));
    }
}
