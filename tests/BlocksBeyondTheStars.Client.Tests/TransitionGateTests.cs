// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3139/#3140/#3142: the arrival cover over a landing or a docking opens only once the server has really moved the player
/// AND the ground has settled — a hyperjump's landing used to show the planet the player had just left while the server
/// was still building the new one.
/// </summary>
public sealed class TransitionGateTests
{
    private static TransitionGate Covering(TransitionArrival kind)
    {
        var gate = new TransitionGate();
        gate.Begin(kind);
        return gate;
    }

    [Fact]
    public void Landing_StaysShut_WhileTheOldWorldIsStillTheOneLoaded()
    {
        // The #3139 case: SpaceClosed is in, the old world is settled and "ready" — but nothing says the player moved.
        var gate = Covering(TransitionArrival.Landing);

        gate.Tick(5f);

        Assert.False(gate.CanOpen(settled: true));
    }

    [Fact]
    public void Landing_OpensAfterTheTouchdownSnap_OnceSettled()
    {
        var gate = Covering(TransitionArrival.Landing);
        gate.NoteWorldLoad();
        gate.NotePlaced();
        gate.Tick(1f);

        Assert.False(gate.CanOpen(settled: false)); // the floor chunk is still streaming
        Assert.True(gate.CanOpen(settled: true));
    }

    [Fact]
    public void Landing_BackOnTheSameWorld_OpensOnTheSnapAlone()
    {
        // L home: no world reset — the server relocates the player on the world the client already holds.
        var gate = Covering(TransitionArrival.Landing);
        gate.NotePlaced();
        gate.Tick(0.7f);

        Assert.True(gate.CanOpen(settled: true));
    }

    [Fact]
    public void Landing_WithAWorldResetButNoSnap_CountsAsPlacedAfterTheGrace()
    {
        var gate = Covering(TransitionArrival.Landing);
        gate.NoteWorldLoad();
        gate.Tick(TransitionGate.PlacementGrace - 0.1f);
        Assert.False(gate.CanOpen(settled: true));

        gate.Tick(0.2f);
        Assert.True(gate.CanOpen(settled: true));
    }

    [Fact]
    public void Docking_OpensOnTheStationWorld_NotBefore()
    {
        var gate = Covering(TransitionArrival.Docking);
        gate.Tick(2f);
        Assert.False(gate.CanOpen(settled: true));

        gate.NoteWorldLoad();
        Assert.True(gate.CanOpen(settled: true));
        Assert.False(gate.CanOpen(settled: false));
    }

    [Fact]
    public void NeverFlickers_ShorterThanTheMinimum()
    {
        var gate = Covering(TransitionArrival.Docking);
        gate.NoteWorldLoad();
        gate.Tick(TransitionGate.MinClosed * 0.5f);

        Assert.False(gate.CanOpen(settled: true));
    }

    [Fact]
    public void SignalsFromBeforeTheCover_DoNotCount()
    {
        var gate = new TransitionGate();
        gate.NoteWorldLoad(); // a reset that belonged to something earlier
        gate.NotePlaced();
        gate.Begin(TransitionArrival.Landing);
        gate.Tick(3f);

        Assert.False(gate.WorldLoadSeen);
        Assert.False(gate.CanOpen(settled: true));
    }

    [Fact]
    public void ACancelledTransition_OpensAtOnce()
    {
        var gate = Covering(TransitionArrival.Docking);
        gate.Cancel();

        Assert.True(gate.CanOpen(settled: false));
    }

    [Fact]
    public void ALostMessage_NeverTrapsThePlayer()
    {
        var gate = Covering(TransitionArrival.Landing);
        gate.Tick(TransitionGate.GiveUp);

        Assert.True(gate.CanOpen(settled: false));
    }

    [Fact]
    public void NoCover_IsAlwaysOpen()
    {
        var gate = new TransitionGate();
        Assert.True(gate.CanOpen(settled: false));

        gate.Begin(TransitionArrival.Landing);
        gate.End();
        Assert.True(gate.CanOpen(settled: false));
    }
}
