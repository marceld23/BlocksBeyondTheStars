// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// People keep personal space (#1272): settlers and station NPCs used to walk straight into each other —
/// only the creature flocks had a separation rule (#651). The nudge is a pure horizontal push, capped per
/// tick, and never fires when the neighbour is already far enough away.
/// </summary>
public sealed class NpcSeparationTests
{
    [Fact]
    public void FarApart_NothingMoves()
    {
        var pos = new Vector3f(10f, 5f, 10f);
        var moved = SvGameServer.NudgeApart(pos, new Vector3f(13f, 5f, 10f), sepDist: 0.8f, maxStep: 0.25f);
        Assert.Equal(pos, moved);
    }

    [Fact]
    public void TooClose_PushesAwayAlongTheLineBetweenThem_CappedPerTick()
    {
        var pos = new Vector3f(10f, 5f, 10f);
        var other = new Vector3f(10.3f, 5f, 10f); // 0.3 m apart, personal space 0.8 m

        var step = SvGameServer.NudgeApart(pos, other, sepDist: 0.8f, maxStep: 0.25f);
        Assert.Equal(9.75f, step.X, 3); // capped: only 0.25 of the missing 0.5 this tick
        Assert.Equal(5f, step.Y);        // never vertical
        Assert.Equal(10f, step.Z, 3);

        var settled = SvGameServer.NudgeApart(pos, other, sepDist: 0.8f, maxStep: 5f);
        Assert.Equal(9.5f, settled.X, 3); // uncapped: exactly personal space apart
    }

    [Fact]
    public void ExactlyOnTopOfEachOther_StillComesApart()
    {
        var pos = new Vector3f(3f, 1f, 3f);
        var moved = SvGameServer.NudgeApart(pos, pos, sepDist: 0.8f, maxStep: 1f);
        Assert.NotEqual(pos, moved);
        float dx = moved.X - pos.X, dz = moved.Z - pos.Z;
        Assert.Equal(0.8f, (float)System.Math.Sqrt(dx * dx + dz * dz), 3);
    }
}
