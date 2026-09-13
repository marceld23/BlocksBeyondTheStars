// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Base residents (#1865, Marcel 2026-09-13): every bed inside a base brings one more resident — 1 + beds, at most
/// five; a bed counts in the core zone, in a sealed room, inside the walled yard or in a closed room (walls, roof,
/// door), never in the open field; the founding settler still needs the machines. A trading post and a mission board
/// inside the base are staffed by a resident — barter and board jobs at home — and a post nobody can staff says why.
/// Residents are distinct people on the roster, keyed by base and slot; the founding settler keeps its old key.
/// </summary>
public sealed class BaseResidentsTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void NoBed_OneSettler_EveryBedOneMore_UpToFive()
    {
        using var w = new NpcLifeWorld(_content, "residents_count");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        Assert.Single(w.Server.BaseResidentsForTest(baseId));

        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Bed(NpcLifeWorld.Cx - 3, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        var three = w.Server.BaseResidentsForTest(baseId);
        Assert.Equal(3, three.Count);
        Assert.Equal(2, w.Server.BaseFurnitureForTest(baseId).Beds); // a two-cell bed is ONE bed, its foot never counts

        // Six beds: still five people.
        for (int i = 0; i < 4; i++)
        {
            w.Bed(NpcLifeWorld.Cx + 2 + 2 * i, NpcLifeWorld.Cz + 1);
        }

        w.Server.ScanBaseLifeForTest();
        Assert.Equal(6, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(5, w.Server.BaseResidentsForTest(baseId).Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, w.Server.BaseResidentsForTest(baseId).Select(r => r.Slot).OrderBy(s => s));

        // Each resident is a different person (name seeded per slot), each on the owner's roster under its own key;
        // the founding settler keeps the key it always had.
        var roster = w.Player.State.NpcMemory.Where(kv => kv.Key.StartsWith($"base_{baseId}", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, roster.Count);
        Assert.Equal(5, roster.Select(kv => kv.Value.Name).Distinct().Count());
        Assert.Contains($"base_{baseId}:settler", w.Player.State.NpcMemory.Keys);
        Assert.Contains($"base_{baseId}#1:settler", w.Player.State.NpcMemory.Keys);
    }

    [Fact]
    public void BedsGo_AndTheirSleepersMoveOn()
    {
        using var w = new NpcLifeWorld(_content, "residents_leave");
        int baseId = w.FoundBase();
        var bed = w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);

        w.Server.RemoveBlockForTest(bed.X, bed.Y, bed.Z);
        w.Server.RemoveBlockForTest(bed.X, bed.Y, bed.Z + 1);
        w.Server.ScanBaseLifeForTest();
        var left = Assert.Single(w.Server.BaseResidentsForTest(baseId));
        Assert.Equal(0, left.Slot); // the founding settler stays
    }

    [Fact]
    public void ABedInTheOpenField_BringsNobody_TheSameBedInAClosedHut_Does()
    {
        using var w = new NpcLifeWorld(_content, "residents_hut");
        int baseId = w.FoundBase();

        // 16 blocks out: beyond the core zone, no walls, no roof.
        var bed = w.Bed(NpcLifeWorld.Cx + 16, NpcLifeWorld.Cz);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(0, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Single(w.Server.BaseResidentsForTest(baseId));

        // Build a closed hut around the bed (walls, roof, no gap): now it is a home.
        w.Hut(NpcLifeWorld.Cx + 16, NpcLifeWorld.Cz, half: 3, doorway: false);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Bed == bed);
    }

    [Fact]
    public void ABedInsideTheWalledYard_Counts()
    {
        using var w = new NpcLifeWorld(_content, "residents_yard", padHalf: 24);
        int baseId = w.FoundBase();
        w.WallRing(14);
        w.Bed(NpcLifeWorld.Cx + 11, NpcLifeWorld.Cz - 2); // outside the core zone, inside the ring
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(2, w.Server.BaseResidentsForTest(baseId).Count);
    }

    [Fact]
    public void BedsAlone_BringNobody_WithoutTheMachines()
    {
        using var w = new NpcLifeWorld(_content, "residents_nomachines");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz, "base_core");
        int baseId = w.Server.BaseSnapshots.Single().Id;
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        // A bed is a machine-category block, so one bed is not three machines: nobody moves in yet.
        Assert.Empty(w.Server.BaseResidentsForTest(baseId));
    }

    [Fact]
    public void ATradingPostAtHome_IsStaffed_AndBarterWorksBesideIt()
    {
        using var w = new NpcLifeWorld(_content, "residents_vendor");
        int baseId = w.FoundBase();
        var post = new Vector3i(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(post.X, post.Y, post.Z, "station_vendor");
        w.Server.ScanBaseLifeForTest();

        var vendor = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "vendor");
        Assert.Equal("vendor", vendor.Role);

        w.Player.State.Position = new Vector3f(post.X - 1.5f, w.Feet, post.Z + 0.5f);
        Assert.True(w.Server.MarketAvailableForTest(w.Player.State), "barter at the base's trading post");
        w.Player.State.Position = new Vector3f(post.X - 12.5f, w.Feet, post.Z + 0.5f);
        Assert.False(w.Server.MarketAvailableForTest(w.Player.State), "no barter across the yard");

        // The post goes: the trader goes back to being a settler, barter ends.
        w.Server.RemoveBlockForTest(post.X, post.Y, post.Z);
        w.Server.ScanBaseLifeForTest();
        Assert.DoesNotContain(w.Server.BaseResidentsForTest(baseId), r => r.Job == "vendor");
        w.Player.State.Position = new Vector3f(post.X - 1.5f, w.Feet, post.Z + 0.5f);
        Assert.False(w.Server.MarketAvailableForTest(w.Player.State));
    }

    [Fact]
    public void AMissionBoardAtHome_OffersJobs_TakenOnlyAtTheBoard()
    {
        using var w = new NpcLifeWorld(_content, "residents_board");
        int baseId = w.FoundBase();
        var board = new Vector3i(NpcLifeWorld.Cx - 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(board.X, board.Y, board.Z, "mission_board");
        w.Server.ScanBaseLifeForTest();
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Job == "quartermaster");

        string mission = w.Server.BaseBoardMissionIdsForTest(baseId).First(id => id.StartsWith("home_", StringComparison.Ordinal));

        w.Player.State.Position = new Vector3f(board.X + 12.5f, w.Feet, board.Z + 0.5f);
        w.Server.AcceptMission(NpcLifeWorld.Owner, mission);
        Assert.DoesNotContain(w.Player.State.Missions, m => m.MissionId == mission);

        w.Player.State.Position = new Vector3f(board.X + 1.5f, w.Feet, board.Z + 0.5f);
        w.Server.AcceptMission(NpcLifeWorld.Owner, mission);
        Assert.Contains(w.Player.State.Missions, m => m.MissionId == mission);
    }

    [Fact]
    public void APostOutsideTheBase_OrWithNobodyToStaffIt_SaysWhy()
    {
        using var w = new NpcLifeWorld(_content, "residents_posthint");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz, "base_core");

        // No resident yet: placed inside the zone.
        w.Player.State.Inventory.Add("station_vendor", 2, 16);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 2.5f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx + 3, w.Feet, NpcLifeWorld.Cz + 4, "station_vendor");
        Assert.Contains("@srv.base.post_no_resident", w.MessagesToPlayer());

        // In the open, 16 blocks out.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 15.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, NpcLifeWorld.Cx + 17, w.Feet, NpcLifeWorld.Cz, "station_vendor");
        Assert.Contains("@srv.base.post_outside", w.MessagesToPlayer());
    }
}
