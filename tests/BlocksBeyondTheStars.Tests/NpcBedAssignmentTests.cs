// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Beds that belong to someone (#2655, #2656, #2653 point 5): a base resident keeps its bed and its job, a new bed moves
/// nobody, a broken bed sends exactly its sleeper away (and a rebuilt one brings the same person back), a moved bed keeps
/// its sleeper, the assignment survives a reload, and a leak in a bedroom evicts nobody. Villagers keep what they have,
/// take a bed built later, look again when theirs breaks — and the one who has been without a bed longest picks first.
/// </summary>
public sealed class NpcBedAssignmentTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static Dictionary<int, (Vector3i? Bed, string Job)> Residents(NpcLifeWorld w, int baseId)
        => w.Server.BaseResidentsForTest(baseId).ToDictionary(r => r.Slot, r => (r.Bed, r.Job));

    private static Dictionary<int, string> Names(NpcLifeWorld w, int baseId)
        => w.Server.BaseResidentNamesForTest(baseId).ToDictionary(r => r.Slot, r => r.Name);

    /// <summary>Everything about a base's residents that must not change, in slot order: slot, name, bed (and job).</summary>
    private static List<string> Snapshot(NpcLifeWorld w, int baseId, bool withJob = true)
    {
        var names = Names(w, baseId);
        return w.Server.BaseResidentsForTest(baseId).OrderBy(r => r.Slot)
            .Select(r => $"{r.Slot}|{names[r.Slot]}|{r.Bed}" + (withJob ? "|" + r.Job : string.Empty)).ToList();
    }

    private static void RemoveBed(NpcLifeWorld w, Vector3i head)
    {
        w.Server.RemoveBlockForTest(head.X, head.Y, head.Z);
        w.Server.RemoveBlockForTest(head.X, head.Y, head.Z + 1);
    }

    [Fact]
    public void ANewBed_MovesNobody_ItGoesToWhoeverSleptRough()
    {
        using var w = new NpcLifeWorld(_content, "beds_new");
        int baseId = w.FoundBase();
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Bed(NpcLifeWorld.Cx - 3, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        var before = Residents(w, baseId);
        Assert.Equal(3, before.Count);
        int rough = Assert.Single(before, r => r.Value.Bed is null).Key;

        // Lower coordinates than both: the index's first bed — the old rule handed beds out in index order and reshuffled.
        var newBed = w.Bed(NpcLifeWorld.Cx - 6, NpcLifeWorld.Cz - 4);
        w.Server.ScanBaseLifeForTest();
        var after = Residents(w, baseId);
        Assert.Equal(4, after.Count);
        foreach (var (slot, (bed, _)) in before.Where(r => r.Value.Bed is not null))
        {
            Assert.Equal(bed, after[slot].Bed); // everybody keeps their bed
        }

        Assert.Equal(newBed, after[rough].Bed); // the one who slept rough takes it ...
        Assert.Null(after[3].Bed); // ... and the newcomer sleeps rough until one more bed stands
    }

    [Fact]
    public void ABedRemoved_ExactlyItsSleeperMovesOut_TheOthersKeepBedAndJob()
    {
        using var w = new NpcLifeWorld(_content, "beds_removed");
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "station_vendor");
        w.Set(NpcLifeWorld.Cx - 4, w.Feet, NpcLifeWorld.Cz + 4, "mission_board");
        w.Bed(NpcLifeWorld.Cx - 6, NpcLifeWorld.Cz - 2);
        w.Bed(NpcLifeWorld.Cx - 4, NpcLifeWorld.Cz - 2);
        w.Bed(NpcLifeWorld.Cx + 5, NpcLifeWorld.Cz - 2);
        w.Server.ScanBaseLifeForTest();
        var before = Residents(w, baseId);
        var names = Names(w, baseId);
        Assert.Equal(4, before.Count);

        // A resident with a bed and a job who is not the founder: its bed goes.
        var (victim, (victimBed, _)) = before.First(r => r.Key != 0 && r.Value.Bed is not null && r.Value.Job.Length > 0);
        RemoveBed(w, victimBed!.Value);
        w.Server.ScanBaseLifeForTest();

        var after = Residents(w, baseId);
        Assert.Equal(3, after.Count);
        Assert.False(after.ContainsKey(victim), $"slot {victim} lost its bed and should have moved out");
        foreach (var (slot, (bed, job)) in before.Where(r => r.Key != victim))
        {
            Assert.Equal(bed, after[slot].Bed);
            if (job.Length > 0)
            {
                Assert.Equal(job, after[slot].Job);
            }

            Assert.Equal(names[slot], Names(w, baseId)[slot]); // the same people
        }
    }

    [Fact]
    public void ABedMoved_KeepsItsSleeper()
    {
        using var w = new NpcLifeWorld(_content, "beds_moved");
        int baseId = w.FoundBase();
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Bed(NpcLifeWorld.Cx - 3, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        var before = Residents(w, baseId);
        var names = Names(w, baseId);
        var (sleeper, (oldBed, _)) = before.First(r => r.Key != 0 && r.Value.Bed is not null);

        // Picked up and put down elsewhere between two scans.
        RemoveBed(w, oldBed!.Value);
        var moved = w.Bed(NpcLifeWorld.Cx + 5, NpcLifeWorld.Cz - 5);
        w.Server.ScanBaseLifeForTest();

        var after = Residents(w, baseId);
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        Assert.Equal(moved, after[sleeper].Bed);
        Assert.Equal(names.OrderBy(kv => kv.Key), Names(w, baseId).OrderBy(kv => kv.Key));
        foreach (var (slot, (bed, _)) in before.Where(r => r.Key != sleeper))
        {
            Assert.Equal(bed, after[slot].Bed);
        }
    }

    [Fact]
    public void AResidentWhoMovedOut_ComesBackWithANewBed_SameSlotSameName()
    {
        using var w = new NpcLifeWorld(_content, "beds_return");
        int baseId = w.FoundBase();
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Bed(NpcLifeWorld.Cx - 3, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        var names = Names(w, baseId);
        var (leaver, (bed, _)) = Residents(w, baseId).First(r => r.Key != 0 && r.Value.Bed is not null);

        RemoveBed(w, bed!.Value);
        w.Server.ScanBaseLifeForTest();
        Assert.False(Residents(w, baseId).ContainsKey(leaver));

        w.Bed(NpcLifeWorld.Cx + 5, NpcLifeWorld.Cz - 5);
        w.Server.ScanBaseLifeForTest();
        var back = Names(w, baseId);
        Assert.Equal(3, back.Count);
        Assert.Equal(names[leaver], back[leaver]); // the lowest free slot is theirs — the same person
        Assert.Equal(names.OrderBy(kv => kv.Key), back.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void AJob_StaysWithItsResident_WhenAnotherPostIsAdded()
    {
        using var w = new NpcLifeWorld(_content, "beds_jobs");
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz - 3, "workbench");
        w.Server.ScanBaseLifeForTest();
        Assert.Equal("craftsman", Residents(w, baseId)[0].Job);

        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(string.Empty, Residents(w, baseId)[1].Job);

        // A trading post outranks the workbench — but the founder keeps the job it has; the new post goes to the jobless.
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "station_vendor");
        w.Server.ScanBaseLifeForTest();
        var after = Residents(w, baseId);
        Assert.Equal("craftsman", after[0].Job);
        Assert.Equal("vendor", after[1].Job);
    }

    [Fact]
    public void TheAssignment_IsIdenticalAfterAReload()
    {
        using var w = new NpcLifeWorld(_content, "beds_reload");
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz - 3, "workbench");
        w.Server.ScanBaseLifeForTest(); // the founder: craftsman
        w.Bed(NpcLifeWorld.Cx - 5, NpcLifeWorld.Cz + 1);
        w.Server.ScanBaseLifeForTest();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "station_vendor");
        w.Server.ScanBaseLifeForTest(); // slot 1: vendor — a fresh assignment would make the founder the vendor
        w.Bed(NpcLifeWorld.Cx - 6, NpcLifeWorld.Cz - 4); // lower coordinates than the first bed
        w.Server.ScanBaseLifeForTest();

        var residents = Residents(w, baseId);
        Assert.Equal(3, residents.Count);
        Assert.Equal("craftsman", residents[0].Job);
        Assert.Equal("vendor", residents[1].Job);
        var before = Snapshot(w, baseId);

        w.Restart();
        int reloaded = w.Server.BaseSnapshots.Single().Id;
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(before, Snapshot(w, reloaded));
    }

    [Fact]
    public void JobsBeyondTheCoreZone_SurviveAReload_BeforeAnybodyWalksToTheBase()
    {
        // A dedicated server scans its bases right after the start, before anybody walked there: the walled yard reads as
        // open ground over unloaded chunks — the craftsman's workbench in it must not lose its craftsman, nor the walls
        // their guard, and nothing may change once the area loads.
        using var w = new NpcLifeWorld(_content, "beds_reload_unloaded", padHalf: 24);
        int baseId = w.FoundBase();
        w.WallRing(14);
        w.Set(NpcLifeWorld.Cx + 11, w.Feet, NpcLifeWorld.Cz - 2, "workbench"); // in the yard, outside the core zone
        w.Bed(NpcLifeWorld.Cx + 11, NpcLifeWorld.Cz + 2);
        w.Server.ScanBaseLifeForTest();
        var jobs = Residents(w, baseId).Values.Select(r => r.Job).ToList();
        Assert.Contains("guard", jobs);
        Assert.Contains("craftsman", jobs);
        var before = Snapshot(w, baseId);

        w.Restart();
        w.Server.World.UnloadFarChunks(new[] { new ChunkCoord(4000, 0, 4000) }, keepRadius: 1); // nothing of the base is loaded
        int reloaded = w.Server.BaseSnapshots.Single().Id;
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(before, Snapshot(w, reloaded));

        // The owner walks over: the area loads, the index is rebuilt over real walls — and still nothing changes.
        for (int dx = -24; dx <= 24; dx += 8)
            for (int dz = -24; dz <= 24; dz += 8)
                for (int dy = -16; dy <= 16; dy += 8)
                {
                    _ = w.Server.World.GetBlock(new Vector3i(NpcLifeWorld.Cx + dx, w.Feet + dy, NpcLifeWorld.Cz + dz));
                }

        w.Server.ScanBaseLifeForTest();
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(before, Snapshot(w, reloaded));
    }

    [Fact]
    public void ALeakInABedroom_EvictsNobody()
    {
        using var w = new NpcLifeWorld(_content, "beds_leak");
        int baseId = w.FoundBase();

        // A closed hut 16 blocks out (beyond the core zone): the bed in it counts.
        int hx = NpcLifeWorld.Cx + 16, hz = NpcLifeWorld.Cz;
        var bed = w.Bed(hx, hz - 1);
        w.Hut(hx, hz, half: 3, doorway: false);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        var before = Snapshot(w, baseId, withJob: false); // (the hut's walls also give a guard a round — the hole ends that)
        Assert.Equal(2, before.Count);

        // A hole in the wall: the room is no longer closed — the bed still counts, its sleeper stays in it.
        w.Server.RemoveBlockForTest(hx + 3, w.Feet + 1, hz);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
        Assert.Equal(before, Snapshot(w, baseId, withJob: false));
        Assert.Contains(Residents(w, baseId), r => r.Value.Bed == bed);

        // A bed put into the open room afterwards never counted as inside: it brings nobody.
        w.Bed(hx - 1, hz - 1);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal(1, w.Server.BaseFurnitureForTest(baseId).Beds);
    }

    // ---------------- villagers (the shared assignment, #2655) ----------------

    private static Vector3i? BedOf(NpcLifeWorld w, int npcId) => w.Server.NpcFurnitureForTest.Single(n => n.Id == npcId).Bed;

    [Fact]
    public void AVillager_TakesABedBuiltLater_AndLooksAgainWhenItBreaks()
    {
        using var w = new NpcLifeWorld(_content, "beds_villager");
        int v = w.Server.SpawnVillagerForTest("Testdorf", new Vector3f(NpcLifeWorld.Cx + 2.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), looksForFurniture: true);
        w.TickAt(0.45, 2);
        Assert.Null(BedOf(w, v));

        var first = w.Bed(NpcLifeWorld.Cx + 5, NpcLifeWorld.Cz + 3);
        w.TickAt(0.45, 1);
        Assert.Equal(first, BedOf(w, v)); // within a second

        // Another bed stands free; the first one breaks — its sleeper takes the other.
        var second = w.Bed(NpcLifeWorld.Cx - 2, NpcLifeWorld.Cz + 4);
        w.TickAt(0.45, 1);
        Assert.Equal(first, BedOf(w, v)); // a new bed moves nobody
        RemoveBed(w, first);
        w.TickAt(0.45, 1);
        Assert.Equal(second, BedOf(w, v));
    }

    [Fact]
    public void TheFairQueue_TheLongestBedlessPicksFirst()
    {
        using var w = new NpcLifeWorld(_content, "beds_queue");
        var bedA = w.Bed(NpcLifeWorld.Cx + 4, NpcLifeWorld.Cz + 3);
        int a = w.Server.SpawnVillagerForTest("Testdorf", new Vector3f(NpcLifeWorld.Cx + 3.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), looksForFurniture: true);
        w.TickAt(0.45, 1);
        Assert.Equal(bedA, BedOf(w, a));

        // B arrives and finds no free bed; a while later A's bed breaks — B has waited longer.
        int b = w.Server.SpawnVillagerForTest("Testdorf", new Vector3f(NpcLifeWorld.Cx - 3.5f, w.Feet, NpcLifeWorld.Cz + 0.5f), looksForFurniture: true);
        w.TickAt(0.45, 3);
        Assert.Null(BedOf(w, b));
        RemoveBed(w, bedA);
        w.TickAt(0.45, 1);
        Assert.Null(BedOf(w, a));

        // One new bed, nearer to A (and A has the lower id): B, bedless the longest, gets it.
        var bed = w.Bed(NpcLifeWorld.Cx + 2, NpcLifeWorld.Cz + 3);
        w.TickAt(0.45, 1);
        Assert.Equal(bed, BedOf(w, b));
        Assert.Null(BedOf(w, a));
    }
}
