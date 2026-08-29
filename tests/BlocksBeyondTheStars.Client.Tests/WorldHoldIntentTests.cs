// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The one copy of "hold the world while my panel is open" that the Esc menu and the feedback dialog (#1330)
/// share: intent on open, release on close, and the 15 s keep-alive in between that lets the server sweep a
/// client that died behind its menu (#973).
/// </summary>
public sealed class WorldHoldIntentTests
{
    private readonly List<bool> _sent = new();
    private readonly WorldHoldIntent _hold;

    public WorldHoldIntentTests()
    {
        _hold = new WorldHoldIntent(_sent.Add);
    }

    [Fact]
    public void Hold_SendsTheIntentOnce_AndReleaseSendsTheRelease()
    {
        _hold.Hold(now: 10f);
        _hold.Hold(now: 11f); // a second open while already holding is a no-op

        Assert.True(_hold.Holding);
        Assert.Equal(new[] { true }, _sent);

        _hold.Release();
        _hold.Release(); // every close path may call it — only the first one talks to the server

        Assert.False(_hold.Holding);
        Assert.Equal(new[] { true, false }, _sent);
    }

    [Fact]
    public void Release_WithoutAHold_SendsNothing()
    {
        _hold.Release();
        Assert.Empty(_sent);
    }

    [Fact]
    public void WhileHolding_TheIntentIsRepeatedOnTheKeepAliveCadence()
    {
        _hold.Hold(now: 0f);

        // Ticks inside the cadence are silent; the first one at/after the deadline re-sends, and the next
        // deadline is measured from THAT send, so a long stalled frame yields one repeat, not a burst.
        _hold.Tick(1f);
        _hold.Tick(WorldHoldIntent.KeepAliveSeconds - 0.01f);
        Assert.Equal(new[] { true }, _sent);

        _hold.Tick(WorldHoldIntent.KeepAliveSeconds);
        Assert.Equal(new[] { true, true }, _sent);

        _hold.Tick(WorldHoldIntent.KeepAliveSeconds + 1f);
        Assert.Equal(2, _sent.Count);

        _hold.Tick(3f * WorldHoldIntent.KeepAliveSeconds); // one stalled frame far past the deadline
        Assert.Equal(3, _sent.Count);
        _hold.Tick(3f * WorldHoldIntent.KeepAliveSeconds + 1f);
        Assert.Equal(3, _sent.Count);
    }

    [Fact]
    public void AfterRelease_NoKeepAliveIsSent()
    {
        _hold.Hold(now: 0f);
        _hold.Release();

        _hold.Tick(100f);
        _hold.Tick(200f);

        Assert.Equal(new[] { true, false }, _sent);
    }

    [Fact]
    public void Forget_DropsTheHoldSilently_AndTheNextHoldStartsFresh()
    {
        _hold.Hold(now: 0f);
        _hold.Forget(); // the world is gone — nothing to tell the server

        _hold.Tick(100f); // a stale keep-alive here would put the NEXT world to sleep
        Assert.False(_hold.Holding);
        Assert.Equal(new[] { true }, _sent);

        _hold.Hold(now: 100f);
        Assert.Equal(new[] { true, true }, _sent);
        _hold.Tick(100f + WorldHoldIntent.KeepAliveSeconds - 1f);
        Assert.Equal(2, _sent.Count); // the cadence restarted from the new hold, not from the old one
    }

    [Fact]
    public void ANullSender_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new WorldHoldIntent(null!));
    }
}
