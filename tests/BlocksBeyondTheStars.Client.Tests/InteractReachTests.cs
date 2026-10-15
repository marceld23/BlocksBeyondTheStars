// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2613 — the client's interaction picks (<see cref="InteractReach"/>) reach across the world's wrap seam. Lyxette's ice
/// moon has a circumference of 3232 and its home landing pad at X = 0: crates and a settler the server keeps at X just
/// below C stood right beside a player whose scene position read just below 0 — E opened nothing, talked to no one, and
/// the HUD lines stayed blank. Every case is checked from both sides of the seam.
/// </summary>
public sealed class InteractReachTests
{
    private const int C = 3232;

    private static NetNpc Npc(int id, float x, float y, float z, string name = "") => new() { Id = id, X = x, Y = y, Z = z, Name = name };

    private static NetContainer Crate(string id, float x, float y, float z, string kind = "crate")
        => new() { Id = id, Kind = kind, X = x, Y = y, Z = z };

    [Fact]
    public void ASettlerAcrossTheSeam_IsTalkable_FromBothSides()
    {
        var npcs = new[] { Npc(7, C - 1.5f, 70f, 0.5f) };

        // West of X = 0 in the scene (the player loaded east of the seam and walked back over it) — 2 m the short way.
        Assert.Equal(7, InteractReach.NearestTalkableNpc(npcs, -3.5f, 70f, 0.5f, C)?.Id);

        // East of the seam, canonical side: 2 m the short way, a whole world the long way.
        Assert.Equal(7, InteractReach.NearestTalkableNpc(npcs, 0.5f, 70f, 0.5f, C)?.Id);

        // A player one lap further on (scene X = C + 0.5).
        Assert.Equal(7, InteractReach.NearestTalkableNpc(npcs, C + 0.5f, 70f, 0.5f, C)?.Id);
    }

    [Fact]
    public void TheTalkReach_StaysTheSame_AndTheNearestWins()
    {
        var npcs = new[] { Npc(1, C - 4f, 70f, 0.5f), Npc(2, 1.5f, 70f, 0.5f) };

        Assert.Equal(2, InteractReach.NearestTalkableNpc(npcs, -0.5f, 70f, 0.5f, C)?.Id); // 2 m east beats 3.5 m west
        Assert.Null(InteractReach.NearestTalkableNpc(npcs, 20f, 70f, 0.5f, C));           // nobody within 4.5 m
        Assert.Null(InteractReach.NearestTalkableNpc(new[] { Npc(3, 0.5f, 70f, 0.5f + InteractReach.TalkReach) }, 0.5f, 70f, 0.5f, C)); // the edge itself is out
        Assert.Null(InteractReach.NearestTalkableNpc(null, 0f, 0f, 0f, C));
    }

    [Fact]
    public void ASettlerAcrossTheLatitudeSeam_IsTalkable()
    {
        int half = WorldConstants.LatitudePeriodFor(C) / 2;
        var npcs = new[] { Npc(9, 10.5f, 70f, half - 1f) };
        Assert.Equal(9, InteractReach.NearestTalkableNpc(npcs, 10.5f, 70f, -half + 1f, C)?.Id);
    }

    [Fact]
    public void TheCrateInTheCrosshair_IsFound_AcrossTheSeam()
    {
        var crates = new[] { Crate("far", C - 3, 70, 5) };

        Assert.Equal("far", InteractReach.CrateAtCell(crates, -3, 70, 5, C)?.Id);     // scene cell west of X = 0
        Assert.Equal("far", InteractReach.CrateAtCell(crates, C - 3, 70, 5, C)?.Id);  // the canonical side
        Assert.Null(InteractReach.CrateAtCell(crates, -2, 70, 5, C));                 // the cell beside it
        Assert.Null(InteractReach.CrateAtCell(new[] { Crate("capsule", C - 3, 70, 5, "salvage_capsule") }, -3, 70, 5, C)); // crates only
    }

    [Fact]
    public void TheCrateGAndHWorkOn_IsFound_FromBothSides()
    {
        var crates = new[] { Crate("far", C - 3, 70, 5) };

        // The player at scene −1.5 (aiming at cell −3): the crate's centre is 1 m away the short way.
        Assert.Equal("far", InteractReach.NearestContainer(crates, -1.5f, 70.5f, 5.5f, C, crateOnly: true)?.Id);
        Assert.Equal("far", InteractReach.NearestContainer(crates, 1.5f, 70.5f, 5.5f, C, crateOnly: false)?.Id);
        Assert.Null(InteractReach.NearestContainer(crates, 20f, 70.5f, 5.5f, C, crateOnly: false)); // out of the 6 m reach

        // H looks for crates only; G takes any container.
        var capsule = new[] { Crate("capsule", 1, 70, 5, "salvage_capsule") };
        Assert.Null(InteractReach.NearestContainer(capsule, 1.5f, 70.5f, 5.5f, C, crateOnly: true));
        Assert.Equal("capsule", InteractReach.NearestContainer(capsule, 1.5f, 70.5f, 5.5f, C, crateOnly: false)?.Id);
    }

    [Fact]
    public void TheBaseZone_ReachesAcrossTheSeam()
    {
        // NetBase carries the core cell's centre (+0.5); the zone is the 8-block cube around the core cell.
        float coreX = C - 2 + 0.5f, coreY = 70f, coreZ = 0.5f;
        Assert.True(InteractReach.InBaseZone(3, 70, 0, coreX, coreY, coreZ, C));    // 5 cells east, across X = 0
        Assert.True(InteractReach.InBaseZone(-4, 72, 0, coreX, coreY, coreZ, C));   // scene west of 0, the same side
        Assert.False(InteractReach.InBaseZone(7, 70, 0, coreX, coreY, coreZ, C));   // 9 cells away
        Assert.False(InteractReach.InBaseZone(3, 79, 0, coreX, coreY, coreZ, C));   // Y never wraps
    }

    [Fact]
    public void SeamDistance_IsTheShortWay_AndStraightWithoutAWorld()
    {
        Assert.Equal(4.0, InteractReach.SeamDistanceSquared(-1, 0, 0, C - 3, 0, 0, C), 6);
        Assert.Equal((double)(C - 2) * (C - 2), InteractReach.SeamDistanceSquared(-1, 0, 0, C - 3, 0, 0, 0), 3);
    }
}
