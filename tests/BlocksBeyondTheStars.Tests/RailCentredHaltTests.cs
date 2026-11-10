// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3463 — Lyxette: "the whole train's length should be taken into account at a station and its middle found: with the
/// stop at the end of the station the train is all in or all out by its direction; in the middle it only ever pulls in
/// halfway". The autopilot now halts the train centred on its stop, whichever way it runs (the cab pulling or pushing) —
/// and never so near an open line's end that a wagon would hang past it.
/// </summary>
public sealed class RailCentredHaltTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    /// <summary>The middle of the train along the line (the arena's line runs along X): halfway between the cab and the last wagon.</summary>
    private static float TrainMiddleX(RailTravelArena a, int wagons)
    {
        var cab = a.Server.WagonPoseForTest(a.TrainId, 0)!.Value.Pos;
        var last = a.Server.WagonPoseForTest(a.TrainId, wagons - 1)!.Value.Pos;
        return (cab.X + last.X) * 0.5f;
    }

    [Fact]
    public void ATrain_HaltsCentredOnItsStop_PullingAndPushing()
    {
        using var a = new RailTravelArena(Content.Value, "rail_centred_halt");
        a.PlaceTrain(NpcLifeWorld.Cx - 6, "wagon_seats");
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 1);

        var first = a.TickUntilHalted();
        Assert.NotNull(first);
        Assert.InRange(TrainMiddleX(a, 2) - (first!.Value.X + 0.5f), -1.2f, 1.2f);

        // The other stop, the other way round (the cab now pushes from behind): centred again.
        var second = a.TickUntilHalted(240, other: first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.InRange(TrainMiddleX(a, 2) - (second!.Value.X + 0.5f), -1.2f, 1.2f);
    }

    [Fact]
    public void NearTheLinesEnd_TheTrainMovesInward_SoNoWagonHangsPastTheEnd()
    {
        // The arena's east stop lies about seven blocks from the line's end: a three-wagon train centred on it would hang a
        // wagon past the end, so it halts pulled inward — beside the stop, its end wagon at the line's end.
        using var a = new RailTravelArena(Content.Value, "rail_centred_halt_end");
        a.PlaceTrain(NpcLifeWorld.Cx - 6, "wagon_seats", "wagon_bar");
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 1);
        var stop = a.TickUntilHalted();
        Assert.Equal(a.StopEast, stop);

        float offset = TrainMiddleX(a, 3) - (stop!.Value.X + 0.5f);
        Assert.InRange(offset, -RailRules.WagonLength, -0.5f); // pulled in toward the line's middle, still beside the stop
        float eastMost = Math.Max(a.Server.WagonPoseForTest(a.TrainId, 0)!.Value.Pos.X, a.Server.WagonPoseForTest(a.TrainId, 2)!.Value.Pos.X);
        Assert.True(eastMost + (RailRules.WagonLength * 0.5f) <= RailTravelArena.EastX + 1.01f, $"a wagon hangs past the line's end ({eastMost})");
    }
}
