// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The clock the client's own world simulation runs on (#908). #612 stopped the SERVER while the Esc menu is up,
/// but micro-fauna, creature bodies, enemies, NPCs and the local sun all kept running on frame time — so a held
/// world still moved. Those systems read this clock instead.
/// </summary>
public sealed class WorldClockTests
{
    [Fact]
    public void ARunningWorld_AdvancesWithRealTime()
    {
        var clock = new WorldClock();

        Assert.Equal(0.25f, clock.Advance(0.25f));
        clock.Advance(0.25f);

        Assert.False(clock.Paused);
        Assert.Equal(0.5f, clock.Now, 4);
    }

    [Fact]
    public void AHeldWorld_DoesNotAdvanceAtAll()
    {
        var clock = new WorldClock();
        clock.Advance(0.5f);
        float held = clock.Now;

        clock.SetPaused(true);
        for (int i = 0; i < 40; i++)
        {
            Assert.Equal(0f, clock.Advance(0.5f)); // 20 seconds of wall clock
        }

        Assert.True(clock.Paused);
        Assert.Equal(held, clock.Now, 4);
    }

    [Fact]
    public void AfterResuming_ItPicksUpWhereItStopped()
    {
        var clock = new WorldClock();
        clock.Advance(0.5f);
        clock.SetPaused(true);
        clock.Advance(9.0f);

        clock.SetPaused(false);
        clock.Advance(0.5f);

        // The paused stretch is skipped, not banked: a timer scheduled for "now + 1" before the pause must not
        // be a second overdue the moment the world resumes, or every creature in earshot would call at once.
        Assert.Equal(1.0f, clock.Now, 4);
    }

    [Fact]
    public void PausingZeroesTheDelta_BeforeTheNextFrame()
    {
        var clock = new WorldClock();
        clock.Advance(0.5f);
        Assert.Equal(0.5f, clock.Delta);

        // A reader that runs between SetPaused and the next Advance must not still see the live frame's delta.
        clock.SetPaused(true);
        Assert.Equal(0f, clock.Delta);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AGarbledFrame_NeverMovesTheClock(float bad)
    {
        var clock = new WorldClock();
        clock.Advance(0.5f);

        Assert.Equal(0f, clock.Advance(bad));
        Assert.Equal(0.5f, clock.Now, 4); // never rewinds, never teleports a simulation integrating against it
    }
}
