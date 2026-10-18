// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2778: the holster and draw of a hotbar switch. These pin the timing, that the item is swapped exactly once at
/// the bottom, that fast scrolling only draws the last item, that a turn-round never jumps, and that the animation can
/// always be cut short.</summary>
public sealed class HeldSwapTests
{
    private const float Frame = 1f / 60f;

    /// <summary>Steps <paramref name="swap"/> for <paramref name="seconds"/> and counts the swap moments.</summary>
    private static int Run(HeldSwap swap, float seconds)
    {
        int swaps = 0;
        for (float t = 0f; t < seconds; t += Frame)
        {
            if (swap.Step(Frame))
            {
                swaps++;
            }
        }

        return swaps;
    }

    [Fact]
    public void ASwitch_PutsTheOldItemAway_SwapsOnceAtTheBottom_AndDrawsTheNewOne()
    {
        var swap = new HeldSwap();
        Assert.False(swap.Active);
        swap.Begin(holster: true);
        Assert.True(swap.HasPending);
        Assert.Equal(HeldSwap.Phase.Holstering, swap.Current);

        Assert.Equal(0, Run(swap, HeldSwap.HolsterSeconds * 0.5f));
        Assert.InRange(swap.Lowered, 0.3f, 0.7f);

        Assert.Equal(1, Run(swap, HeldSwap.HolsterSeconds * 0.6f));
        Assert.False(swap.HasPending);
        Assert.Equal(HeldSwap.Phase.Drawing, swap.Current);

        Assert.Equal(0, Run(swap, HeldSwap.DrawSeconds + Frame));
        Assert.Equal(HeldSwap.Phase.Idle, swap.Current);
        Assert.Equal(0f, swap.Lowered);
    }

    [Fact]
    public void TheDraw_EndsWithASmallSettle_ThenStops()
    {
        var swap = new HeldSwap();
        swap.Begin(holster: true);
        Run(swap, HeldSwap.HolsterSeconds + HeldSwap.DrawSeconds + (2 * Frame));
        Assert.Equal(HeldSwap.Phase.Idle, swap.Current);
        Run(swap, HeldSwap.SettleSeconds * 0.4f);
        Assert.True(swap.Settle > 0.05f, "the item lifts a little as it settles");
        Assert.True(swap.Settle < 0.6f);
        Run(swap, HeldSwap.SettleSeconds);
        Assert.Equal(0f, swap.Settle);
        Assert.False(swap.Active);
    }

    [Fact]
    public void FastScrolling_DrawsOnlyTheLastItem()
    {
        var swap = new HeldSwap();
        swap.Begin(holster: true);
        Run(swap, HeldSwap.HolsterSeconds * 0.4f);
        swap.Begin(holster: true); // the next slot, and the next …
        Run(swap, HeldSwap.HolsterSeconds * 0.2f);
        swap.Begin(holster: true);
        Assert.Equal(1, Run(swap, HeldSwap.HolsterSeconds + HeldSwap.DrawSeconds + HeldSwap.SettleSeconds));
        Assert.False(swap.Active);
    }

    [Fact]
    public void ASwitchDuringTheDraw_TurnsRoundFromWhereItIs()
    {
        var swap = new HeldSwap();
        swap.Begin(holster: true);
        Run(swap, HeldSwap.HolsterSeconds + (HeldSwap.DrawSeconds * 0.5f));
        Assert.Equal(HeldSwap.Phase.Drawing, swap.Current);
        float before = swap.Drop;
        Assert.InRange(before, 0.2f, 0.8f);

        swap.Begin(holster: true);
        Assert.Equal(before, swap.Drop); // no jump
        Assert.Equal(HeldSwap.Phase.Holstering, swap.Current);
        Assert.Equal(1, Run(swap, HeldSwap.HolsterSeconds)); // it reaches the bottom sooner — it was half way up
    }

    [Fact]
    public void AUsedUpStack_OnlyDraws()
    {
        var swap = new HeldSwap();
        swap.Begin(holster: false);
        Assert.False(swap.HasPending);
        Assert.Equal(1f, swap.Lowered);
        Assert.Equal(HeldSwap.Phase.Drawing, swap.Current);
        Assert.Equal(0, Run(swap, HeldSwap.DrawSeconds + Frame));
        Assert.Equal(0f, swap.Lowered);
    }

    [Fact]
    public void Finish_CutsItShort_AndHandsOverTheWaitingItem()
    {
        var swap = new HeldSwap();
        swap.Begin(holster: true);
        Run(swap, HeldSwap.HolsterSeconds * 0.5f);
        Assert.True(swap.Finish());
        Assert.False(swap.Active);
        Assert.Equal(0f, swap.Drop);
        Assert.False(swap.Finish()); // nothing left waiting
        Assert.Equal(0, Run(swap, 1f));
    }

    [Fact]
    public void TheDrop_IsSmoothAndMonotonic()
    {
        var swap = new HeldSwap();
        Assert.Equal(0f, swap.Drop);
        swap.Begin(holster: true);
        float last = 0f;
        for (int i = 0; i < 20 && swap.Current == HeldSwap.Phase.Holstering; i++)
        {
            swap.Step(HeldSwap.HolsterSeconds / 16f);
            Assert.True(swap.Drop >= last);
            Assert.True(swap.Drop - last < 0.2f, "no step bigger than a frame's worth");
            last = swap.Drop;
        }

        Assert.Equal(1f, swap.Drop);
        Assert.False(swap.Step(0f)); // a paused frame moves nothing
    }
}
