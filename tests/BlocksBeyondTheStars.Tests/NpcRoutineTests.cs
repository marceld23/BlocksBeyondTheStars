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
/// The daily routine and the walking under it (#1866, #1867, Marcel 2026-09-13): a resident works by day, sits on
/// its chair in the evening and sleeps in its bed at night, by the LOCAL sun; it walks there on a route — through a
/// wooden door it opens and that closes behind it — and only when no route exists and nobody watches is it set
/// down at its goal. Creatures keep the local sun too.
/// </summary>
public sealed class NpcRoutineTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static (float Dx, float Dz) Horizontal(Vector3f a, Vector3i cell) => (a.X - (cell.X + 0.5f), a.Z - (cell.Z + 0.5f));

    [Fact]
    public void ByDay_AtWork_InTheEvening_OnTheChair_AtNight_InBed_InTheMorning_UpAgain()
    {
        using var w = new NpcLifeWorld(_content, "routine_day");
        int baseId = w.FoundBase();
        var bed = w.Bed(NpcLifeWorld.Cx - 4, NpcLifeWorld.Cz + 2);
        var chair = w.Chair(NpcLifeWorld.Cx - 1, NpcLifeWorld.Cz + 5);
        w.Server.ScanBaseLifeForTest();
        var settler = w.Server.BaseResidentsForTest(baseId).Single(r => r.Slot == 0);
        Assert.Equal(bed, settler.Bed);
        Assert.Equal(chair, settler.Seat);

        w.TickAt(0.45, 20);
        Assert.Equal(0, w.Server.NpcRoutineForTest(settler.NpcId).Pose);

        w.TickAt(0.72, 60, until: () => w.Server.NpcRoutineForTest(settler.NpcId).Pose == 1);
        var sitting = w.Server.NpcRoutineForTest(settler.NpcId);
        Assert.Equal(1, sitting.Pose);
        var (sx, sz) = Horizontal(sitting.Pos, chair);
        Assert.True(sx * sx + sz * sz < 0.01f, $"sits at {sitting.Pos}, not on the chair {chair}");

        w.TickAt(0.9, 60, until: () => w.Server.NpcRoutineForTest(settler.NpcId).Pose == 2);
        var sleeping = w.Server.NpcRoutineForTest(settler.NpcId);
        Assert.Equal(2, sleeping.Pose);
        Assert.Equal("npc.activity.sleeping", sleeping.Activity);
        Assert.True(Math.Abs(sleeping.Pos.X - (bed.X + 0.5f)) < 0.01f && Math.Abs(sleeping.Pos.Z - (bed.Z + 1f)) < 0.01f,
            $"lies at {sleeping.Pos}, not along the bed at {bed}");

        w.TickAt(0.3, 10);
        Assert.Equal(0, w.Server.NpcRoutineForTest(settler.NpcId).Pose);
    }

    [Fact]
    public void WithoutABed_TheNightIsRestedStanding()
    {
        using var w = new NpcLifeWorld(_content, "routine_nobed");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;

        w.TickAt(0.9, 30);
        var r = w.Server.NpcRoutineForTest(id);
        Assert.Equal(0, r.Pose);
        Assert.Equal("npc.activity.resting", r.Activity);
    }

    [Fact]
    public void AWalker_LeavesAHutThroughAShutWoodenDoor_ThatClosesBehindIt()
    {
        using var w = new NpcLifeWorld(_content, "routine_door");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;

        // A hut 12 blocks east of the core with a doorway in its east wall; a wooden door shuts it.
        int hx = NpcLifeWorld.Cx + 12, hz = NpcLifeWorld.Cz;
        w.Hut(hx, hz, half: 3, doorway: true);
        w.Player.State.Inventory.Add("door_wood", 1, 16);
        w.Player.State.Position = new Vector3f(hx + 4.5f, w.Feet, hz + 0.5f);
        w.Server.PlaceBlock(NpcLifeWorld.Owner, hx + 3, w.Feet, hz, "door_wood");
        var door = w.Server.DoorSnapshots.Single(d => d.Kind == "wood");
        Assert.False(door.Open);

        // The walker inside the hut, its goal outside the door; the player steps back.
        w.Server.MoveNpcForTest(id, new Vector3f(hx + 0.5f, w.Feet, hz + 0.5f));
        var goal = new Vector3f(hx + 7.5f, w.Feet, hz + 0.5f);
        w.Server.SetNpcGoalForTest(id, goal);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 4.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);

        bool sawOpen = false;
        w.TickAt(0.45, 60, until: () =>
        {
            sawOpen |= w.Server.DoorSnapshots.Single(d => d.Id == door.Id).Open;
            return !w.Server.NpcHasGoalForTest(id);
        });

        Assert.False(w.Server.NpcHasGoalForTest(id), "never arrived outside the hut");
        Assert.True(sawOpen, "the door never opened");
        var pos = w.Server.NpcSnapshots.Single(n => n.Id == id).Pos;
        Assert.True(pos.X > hx + 3.5f, $"ended at {pos}, still inside the hut");

        w.TickAt(0.45, 8);
        Assert.False(w.Server.DoorSnapshots.Single(d => d.Id == door.Id).Open, "the door stayed open behind the walker");
    }

    [Fact]
    public void NoRoute_TheWalkerIsSetDownAtItsGoal_OnlyWhileNobodyWatches()
    {
        using var w = new NpcLifeWorld(_content, "routine_teleport");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;

        // A sealed hut (no doorway) 14 blocks west; the goal is inside it.
        int hx = NpcLifeWorld.Cx - 14, hz = NpcLifeWorld.Cz;
        w.Hut(hx, hz, half: 2, doorway: false);
        var goal = new Vector3f(hx + 0.5f, w.Feet, hz + 0.5f);
        w.Server.MoveNpcForTest(id, new Vector3f(NpcLifeWorld.Cx + 0.5f, w.Feet, NpcLifeWorld.Cz + 3.5f));
        w.Server.SetNpcGoalForTest(id, goal);

        // Watched (the player stands right beside the walker): it never pops into the sealed hut.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 1.5f, w.Feet, NpcLifeWorld.Cz + 3.5f);
        w.TickAt(0.45, 40);
        Assert.True(w.Server.NpcHasGoalForTest(id));
        var watched = w.Server.NpcSnapshots.Single(n => n.Id == id).Pos;
        Assert.True(Math.Abs(watched.X - goal.X) > 3f, $"teleported in front of the player to {watched}");

        // Nobody near: after the failed routes it is set down at the goal.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 60.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        w.TickAt(0.45, 60, until: () => !w.Server.NpcHasGoalForTest(id));
        Assert.False(w.Server.NpcHasGoalForTest(id));
        var placed = w.Server.NpcSnapshots.Single(n => n.Id == id).Pos;
        Assert.True(Math.Abs(placed.X - goal.X) < 0.6f && Math.Abs(placed.Z - goal.Z) < 0.6f, $"set down at {placed}, not the goal");
    }

    [Fact]
    public void TheLocalSun_IsTheWorldClockShiftedByLongitude()
    {
        using var w = new NpcLifeWorld(_content, "routine_sun");
        int circ = w.Server.World.Circumference;
        w.Server.SetDayFractionForTest(0.5);
        Assert.Equal(0.5, w.Server.LocalDayFractionForTest(0f), 6);
        Assert.Equal(0.0, w.Server.LocalDayFractionForTest(circ / 2f) % 1.0, 6);
        Assert.Equal(0.75, w.Server.LocalDayFractionForTest(circ / 4f), 6);

        w.Server.SetLocalDayFractionForTest(0.9, circ / 3f);
        Assert.Equal(0.9, w.Server.LocalDayFractionForTest(circ / 3f), 6);
    }
}
