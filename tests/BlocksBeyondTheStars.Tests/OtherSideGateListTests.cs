// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The gate list (#3906): a player who is over there is told where the rune gates around them stand — at the crossing,
/// when the set changes, when they walked on — and gets an empty list at the return. Nobody on our side is told.
/// </summary>
public sealed class OtherSideGateListTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void TheCrossing_SendsTheEntryGate_AndTheReturnAnEmptyList()
    {
        using var w = new OtherSideStage(Content, "gates-cross");
        var bystander = w.Player("Friend", new Vector3f(OtherSideStage.Cx + 6.5f, w.PadY + 1, OtherSideStage.Cz + 0.5f));
        var a = w.Player("Kid", w.AtGateA);
        OtherSideStage.Dress(a);
        w.Transport.Sent.Clear();

        w.Use(a, w.GateA[1]);

        var first = w.Transport.To<OtherSideGates>(a).Last();
        var entry = Assert.Single(first.Gates, g => g.Entry);
        Assert.Equal(OtherSideStage.Cx + 0.5f, entry.X, 3); // the middle of the three stones
        Assert.Equal(w.PadY + 1f, entry.Y, 3);
        Assert.Equal(OtherSideStage.Cz + 3.5f, entry.Z, 3);
        Assert.DoesNotContain(first.Gates, g => !g.Entry && Near(g, entry, 14f)); // the entry gate is listed once

        w.Transport.Sent.Clear();
        w.Server.ForceReturnForTest(a);
        Assert.Empty(w.Transport.To<OtherSideGates>(a).Last().Gates);

        w.Tick(5.0);
        Assert.Empty(w.Transport.To<OtherSideGates>(bystander)); // our side is never told
    }

    [Fact]
    public void AGateThePlayerWalksUpTo_JoinsTheList_AndLeavesItWhenItsStonesAreGone()
    {
        using var w = new OtherSideStage(Content, "gates-walk");
        var a = w.Crossed("Kid");
        w.Tick(4.0);
        w.Transport.Sent.Clear();

        // Gate B stands twenty blocks off: the folk pass finds it once the player is near, and the list follows.
        a.State.Position = w.AtGateB;
        w.Tick(8.0);

        var gates = w.Server.OtherSideGatesForTest(a);
        Assert.Contains(gates, g => g.Entry);
        Assert.Contains(gates, IsGateB);
        var sent = w.Transport.To<OtherSideGates>(a);
        Assert.NotEmpty(sent);
        Assert.Equal(gates.Length, sent.Last().Gates.Length);
        Assert.True(sent.Count <= 3, $"the list is paced, not streamed ({sent.Count} in 8 s)");

        // Nothing changes: nothing is sent.
        w.Transport.Sent.Clear();
        w.Tick(6.0);
        Assert.Empty(w.Transport.To<OtherSideGates>(a));

        // The stones of gate B are mined: at the folk pass's next look the gate is gone from the list.
        foreach (var cell in w.GateB)
        {
            w.Server.World.SetBlock(cell, BlockId.Air);
        }

        w.Tick(26.0);
        Assert.DoesNotContain(w.Transport.To<OtherSideGates>(a).Last().Gates, IsGateB);
    }

    /// <summary>Gate B of the stage: three stones in a row, their middle at (Cx − 13.5, Cz − 13.5).</summary>
    private static bool IsGateB(NetOtherSideGate g)
        => !g.Entry && Math.Abs(g.X - (OtherSideStage.Cx - 13.5f)) < 0.01f && Math.Abs(g.Z - (OtherSideStage.Cz - 13.5f)) < 0.01f;

    [Fact]
    public void AnExplicitResend_GoesOutAtOnce_AndOnlyOverThere()
    {
        using var w = new OtherSideStage(Content, "gates-resend");
        var a = w.Crossed("Kid");
        w.Transport.Sent.Clear();

        w.Server.ResendOtherSideGatesForTest(a);
        Assert.Single(w.Transport.To<OtherSideGates>(a));

        w.Server.ForceReturnForTest(a);
        w.Transport.Sent.Clear();
        w.Server.ResendOtherSideGatesForTest(a);
        Assert.Empty(w.Transport.To<OtherSideGates>(a));
    }

    [Fact]
    public void ARuneGroup_IsThreeStonesCloseTogether()
    {
        var row = new[] { new Vector3i(10, 5, 10), new Vector3i(12, 5, 10), new Vector3i(14, 6, 10) };
        var centre = SvGameServer.LargestRuneGroup(row, 8, 3);
        Assert.NotNull(centre);
        Assert.Equal(12.5f, centre!.Value.X, 3);
        Assert.Equal(16f / 3f, centre.Value.Y, 3);
        Assert.Equal(10.5f, centre.Value.Z, 3);

        // Two stones, or three that stand too far apart, are no gate.
        Assert.Null(SvGameServer.LargestRuneGroup(row.Take(2).ToArray(), 8, 3));
        var scattered = new[] { new Vector3i(0, 5, 0), new Vector3i(20, 5, 0), new Vector3i(40, 5, 0) };
        Assert.Null(SvGameServer.LargestRuneGroup(scattered, 8, 3));

        // Of two groups the larger one is the gate.
        var two = row.Concat(new[]
        {
            new Vector3i(60, 5, 60), new Vector3i(61, 5, 60), new Vector3i(62, 5, 60), new Vector3i(63, 5, 60),
        }).ToArray();
        Assert.Equal(62f, SvGameServer.LargestRuneGroup(two, 8, 3)!.Value.X, 3);
    }

    private static bool Near(NetOtherSideGate a, NetOtherSideGate b, float within)
        => Math.Abs(a.X - b.X) <= within && Math.Abs(a.Z - b.Z) <= within;
}
