// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2665: the server→render clock mapping behind the entity interpolators — exact snapshot spacing despite
/// arrival jitter, and an immediate re-sync when the clock really changed.</summary>
public sealed class ServerClockSyncTests
{
    [Fact]
    public void Unstamped_IsStampedOnArrival()
    {
        var sync = new ServerClockSync();
        Assert.Equal(12.5, sync.ToLocal(0, 12.5));
        Assert.False(sync.Synced);
    }

    [Fact]
    public void Jitter_DoesNotBendTheSpacing()
    {
        var sync = new ServerClockSync();
        // Server ticks 0.1 s apart; arrivals jitter by up to 40 ms.
        double a = sync.ToLocal(100.0, 5.00);
        double b = sync.ToLocal(100.1, 5.14); // late
        double c = sync.ToLocal(100.2, 5.20); // on time
        Assert.Equal(0.1, b - a, 2);
        Assert.Equal(0.1, c - b, 2);
    }

    [Fact]
    public void EarlierArrival_LowersTheOffset_LaterOnesCreep()
    {
        var sync = new ServerClockSync();
        sync.ToLocal(100.0, 5.10);
        sync.ToLocal(100.1, 5.15); // 50 ms faster than the first — adopted in bounded steps, not at once
        Assert.Equal(-94.90 - ServerClockSync.MaxDropPerSample, sync.Offset, 6);
        for (int i = 2; i < 10; i++)
        {
            sync.ToLocal(100.0 + (i * 0.1), 5.05 + (i * 0.1)); // the faster path holds
        }

        Assert.Equal(-94.95, sync.Offset, 3); // … and is reached within a few messages
        sync.ToLocal(101.0, 6.15); // 100 ms late — the offset only creeps up
        Assert.True(sync.Offset < -94.94 && sync.Offset > -94.96);
    }

    [Fact]
    public void OneFastPacket_SqueezesASpanOnlyAFewPercent()
    {
        var sync = new ServerClockSync();
        double a = sync.ToLocal(200.0, 10.20);
        double b = sync.ToLocal(200.133, 10.293); // arrived 40 ms quicker than the last one
        Assert.True(b - a > 0.133 - ServerClockSync.MaxDropPerSample - 1e-9, $"span squeezed to {b - a:0.000} s");
    }

    [Fact]
    public void ClockJump_ResyncsAtOnce()
    {
        var sync = new ServerClockSync();
        sync.ToLocal(100.0, 5.0);
        // A held world resumed (the client clock moved on, the server's did not) — or a new server.
        double t = sync.ToLocal(100.1, 9.0);
        Assert.Equal(9.0, t, 6);
    }
}
