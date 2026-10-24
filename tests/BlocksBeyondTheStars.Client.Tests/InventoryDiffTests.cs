// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The aggregation rules behind the HUD pickup feed (#745): only positive per-item deltas surface,
/// slot moves and losses stay silent, and modifier variants merge into their base item.
/// </summary>
public sealed class InventoryDiffTests
{
    private static NetItemStack S(int slot, string item, int count) =>
        new() { Slot = slot, Item = item, Count = count };

    [Fact]
    public void Pickup_SurfacesTheGainedAmount()
    {
        var before = new[] { S(0, "berries", 5) };
        var after = new[] { S(0, "berries", 8) };
        var gains = InventoryDiff.Gains(before, after);

        Assert.Single(gains);
        Assert.Equal(("berries", 3), gains[0]);
    }

    [Fact]
    public void NewItem_SurfacesItsFullCount()
    {
        var before = new[] { S(0, "berries", 5) };
        var after = new[] { S(0, "berries", 5), S(7, "mud", 2) };
        var gains = InventoryDiff.Gains(before, after);

        Assert.Single(gains);
        Assert.Equal(("mud", 2), gains[0]);
    }

    [Fact]
    public void Loss_IsNotAPickup()
    {
        var before = new[] { S(0, "berries", 5), S(1, "mud", 4) };
        var after = new[] { S(0, "berries", 2) }; // ate berries, placed all the mud
        Assert.Empty(InventoryDiff.Gains(before, after));
    }

    [Fact]
    public void SlotMove_IsNotAPickup()
    {
        var before = new[] { S(0, "berries", 5) };
        var after = new[] { S(8, "berries", 5) };
        Assert.Empty(InventoryDiff.Gains(before, after));
    }

    [Fact]
    public void SplitStack_IsNotAPickup()
    {
        var before = new[] { S(0, "mud", 10) };
        var after = new[] { S(0, "mud", 4), S(1, "mud", 6) };
        Assert.Empty(InventoryDiff.Gains(before, after));
    }

    [Fact]
    public void ModifierVariants_MergeIntoTheBaseItem()
    {
        // A dyed/shaped variant (key#modifier) counts toward its plain item.
        var before = new[] { S(0, "planks", 3) };
        var after = new[] { S(0, "planks", 3), S(1, "planks#ff0000", 2) };
        var gains = InventoryDiff.Gains(before, after);

        Assert.Single(gains);
        Assert.Equal(("planks", 2), gains[0]);
    }

    [Fact]
    public void MultipleGains_KeepFirstSeenSlotOrder()
    {
        var before = System.Array.Empty<NetItemStack>();
        var after = new[] { S(2, "stone", 4), S(5, "berries", 1) };
        var gains = InventoryDiff.Gains(before, after);

        Assert.Equal(2, gains.Count);
        Assert.Equal(("stone", 4), gains[0]);
        Assert.Equal(("berries", 1), gains[1]);
    }

    [Fact]
    public void NullOrEmptySnapshots_AreSafe()
    {
        Assert.Empty(InventoryDiff.Gains(null, null));
        Assert.Empty(InventoryDiff.Gains(System.Array.Empty<NetItemStack>(), null));

        var gains = InventoryDiff.Gains(null, new[] { S(0, "mud", 2) });
        Assert.Single(gains);
        Assert.Equal(("mud", 2), gains[0]);
    }

    [Fact]
    public void EmptyAndZeroCountStacks_AreIgnored()
    {
        var before = new[] { S(0, string.Empty, 5), S(1, "mud", 0) };
        var after = new NetItemStack?[] { S(0, string.Empty, 9), S(1, "mud", 0), null };
        Assert.Empty(InventoryDiff.Gains(before, after));
    }

    /// <summary>#2964: the feed row says "in your backpack" when the picked-up item has no stack on the quick-bar — the
    /// reported pale logs topped up a backpack stack with a quick-bar slot free.</summary>
    [Fact]
    public void OnlyInBackpack_IsTrueWithoutAQuickBarStack()
    {
        var reported = new[] { S(0, "basic_drill", 1), S(7, "plant_fiber", 18), S(12, "wood_log_b", 11) };
        Assert.True(InventoryDiff.OnlyInBackpack(reported, "wood_log_b", 9));
        Assert.False(InventoryDiff.OnlyInBackpack(reported, "plant_fiber", 9));
        Assert.False(InventoryDiff.OnlyInBackpack(reported, "stone", 9)); // not carried at all
        Assert.False(InventoryDiff.OnlyInBackpack(null, "stone", 9));

        var both = new NetItemStack?[] { null, S(12, "wood_log_b", 11), S(4, "wood_log_b#t3", 2) }; // a dyed one on the bar
        Assert.False(InventoryDiff.OnlyInBackpack(both, "wood_log_b", 9));
    }
}
