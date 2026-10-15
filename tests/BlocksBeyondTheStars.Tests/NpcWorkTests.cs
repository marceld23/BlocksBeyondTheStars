// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The job state machine of the wilderness folk (#2641, GameServerNpcWork.cs), on lodge residents: pick a site, walk to a
/// spot beside it, work there a few seconds with the tool, then ask the work core — by day only. The tests marked
/// "needs the wave-1 work cores" check what the cores (#2642–#2645) do with that call; they run once those are merged.
/// </summary>
public sealed class NpcWorkTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static string Lodge(NpcLifeWorld w, string profession)
        => w.Server.SpawnLodgeForTest(NpcLifeWorld.Cx + 4, w.PadY, NpcLifeWorld.Cz - 6, profession, beds: 1);

    private static Vector3i Local(NpcLifeWorld w, int x, int y, int z) => new(NpcLifeWorld.Cx + 4 + x, w.PadY + y, NpcLifeWorld.Cz - 6 + z);

    private static int Resident(NpcLifeWorld w, string key) => w.Server.LodgeResidentsForTest(key)[0].Id;

    private static (string Stage, Vector3i Site, string Activity, int Yields, int CoreCalls, bool Patrolling, int Skipped, Vector3f? Goal, double NextIn) Work(NpcLifeWorld w, int id)
        => w.Server.NpcWorkForTest(id)!.Value;

    private static float Horizontal(Vector3f a, Vector3i cell)
    {
        float dx = a.X - (cell.X + 0.5f), dz = a.Z - (cell.Z + 0.5f);
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void TheMiner_WalksToItsFace_WorksThereWithThePickaxe_ThenAsksTheCore()
    {
        using var w = new NpcLifeWorld(Content, "work_miner");
        string key = Lodge(w, "miner");
        int id = Resident(w, key);
        var face = w.Server.LodgeFaceForTest(key).Select(f => f.Cell).ToList();
        Assert.InRange(face.Count, 4, 8);

        w.TickAt(0.45, 60, until: () => Work(w, id).Stage == "Working");
        var work = Work(w, id);
        Assert.Equal("Working", work.Stage);
        Assert.Contains(work.Site, face);
        Assert.Equal("npc.activity.mining", work.Activity);
        var state = w.Server.NpcRoutineForTest(id);
        Assert.Equal("npc_pickaxe", state.Held);
        Assert.True(Horizontal(state.Pos, work.Site) < 2.2f, $"works at {state.Pos}, not beside the face cell {work.Site}");

        w.TickAt(0.45, 10, until: () => Work(w, id).CoreCalls > 0);
        Assert.Equal(1, Work(w, id).CoreCalls); // a few seconds' work, then exactly one ask
    }

    [Fact]
    public void TheGatherer_WalksToAPlotCrop_WorksThereWithTheBasket_ThenAsksTheCore()
    {
        using var w = new NpcLifeWorld(Content, "work_gatherer");
        string key = Lodge(w, "gatherer");
        int id = Resident(w, key);
        var plot = w.Server.LodgeMarkersForTest(key).Where(m => m.Type == LodgeGenerator.MarkerPlot).Select(m => m.Pos.ToBlock()).ToList();

        w.TickAt(0.45, 60, until: () => Work(w, id).Stage == "Working");
        var work = Work(w, id);
        Assert.Contains(work.Site, plot);
        Assert.Equal("npc.activity.gathering", work.Activity);
        Assert.Equal("npc_basket", w.Server.NpcRoutineForTest(id).Held);
        Assert.True(Horizontal(w.Server.NpcRoutineForTest(id).Pos, work.Site) < 1.6f);

        w.TickAt(0.45, 10, until: () => Work(w, id).CoreCalls > 0);
        Assert.Equal(1, Work(w, id).CoreCalls);
    }

    [Fact]
    public void AHunterWithNothingInSight_WalksTheRoundOfItsHuntingGround()
    {
        using var w = new NpcLifeWorld(Content, "work_hunter");
        string key = Lodge(w, "hunter");
        int id = Resident(w, key);
        var center = w.Server.LodgesForTest.Single(l => l.Key == key).Center;

        w.TickAt(0.45, 20, until: () => Work(w, id).Patrolling);
        var work = Work(w, id);
        Assert.True(work.Patrolling, "the hunter never set out on its round");
        Assert.Equal("npc.activity.hunting", work.Activity);
        Assert.Equal("npc_bow", w.Server.NpcRoutineForTest(id).Held);
        var goal = Assert.IsType<Vector3f>(work.Goal);
        float dx = goal.X - center.X, dz = goal.Z - center.Z;
        Assert.True(dx * dx + dz * dz <= 32f * 32f, $"the round reaches {goal}, beyond the hunting ground round {center}");
    }

    [Fact]
    public void ASiteWithNoPlaceToStandBeside_IsDropped()
    {
        using var w = new NpcLifeWorld(Content, "work_dropped");
        string key = Lodge(w, "gatherer");
        int id = Resident(w, key);
        var stone = w.Block("stone");

        // One crop left in the plot, walled in three high on every side: no spot to stand beside it.
        var keep = Local(w, 10, 1, 4);
        foreach (var c in w.Server.LodgeMarkersForTest(key).Where(m => m.Type == LodgeGenerator.MarkerPlot).Select(m => m.Pos.ToBlock()))
        {
            if (!c.Equals(keep))
            {
                w.Server.World.SetBlock(c, BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
            }
        }

        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = 0; dy <= 2; dy++)
                {
                    if (dx != 0 || dz != 0)
                    {
                        w.Server.World.SetBlock(new Vector3i(keep.X + dx, keep.Y + dy, keep.Z + dz), stone);
                    }
                }

        w.TickAt(0.45, 12);
        var work = Work(w, id);
        Assert.Equal(1, work.Skipped);
        Assert.Equal(0, work.CoreCalls);
        Assert.Equal("Idle", work.Stage);
        Assert.True(work.NextIn > 0, "it waits before it looks again");
    }

    [Fact]
    public void NobodyWorks_AtNight()
    {
        using var w = new NpcLifeWorld(Content, "work_night");
        string key = Lodge(w, "miner");
        int id = Resident(w, key);
        w.TickAt(0.9, 20);
        var work = Work(w, id);
        Assert.Equal(0, work.CoreCalls);
        Assert.NotEqual("Working", work.Stage);
    }

    // ---- with the work cores (#2642-#2645) -----------------------------------------------------------------------

    private static (string Item, int Count)[] Store(NpcLifeWorld w, string key)
    {
        string id = w.Server.LodgesForTest.Single(l => l.Key == key).StoreId;
        return w.Server.Containers.Single(c => c.Id == id).Items.Select(s => (s.Item, s.Count)).ToArray();
    }

    [Fact]
    public void TheGatherer_HarvestsIntoTheStore_ThePlantRegrows_AndTheNextYieldWaitsAMinute()
    {
        using var w = new NpcLifeWorld(Content, "work_gather_yield");
        string key = Lodge(w, "gatherer");
        int id = Resident(w, key);
        w.TickAt(0.45, 90, until: () => Work(w, id).Yields > 0);
        var work = Work(w, id);
        Assert.Equal(1, work.Yields);
        Assert.True(w.Server.World.GetBlock(work.Site).IsAir, "the harvested crop is gone until it regrows");
        Assert.True(w.Server.FloraRegrowTimerForTest(work.Site.X, work.Site.Y, work.Site.Z) > 0, "its regrowth is scheduled");
        Assert.Contains(Store(w, key), s => s.Item is "berries" or "grain");
        Assert.True(work.NextIn >= 59.0, $"the next yield comes after {work.NextIn:0} s");

        w.TickAt(0.45, 50);
        Assert.Equal(1, Work(w, id).Yields); // at most one a minute
    }

    [Fact]
    public void TheMiner_MinesAFaceCellIntoTheStore_AndTheCellFillsUpAgain()
    {
        using var w = new NpcLifeWorld(Content, "work_mine_yield");
        string key = Lodge(w, "miner");
        int id = Resident(w, key);
        var face = w.Server.LodgeFaceForTest(key);
        w.TickAt(0.45, 90, until: () => Work(w, id).Yields > 0);
        var site = Work(w, id).Site;
        Assert.True(w.Server.World.GetBlock(site).IsAir);
        var stamped = face.Single(f => f.Cell.Equals(site)).Block;
        string ore = Content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(stamped))!.Drops[0].Item;
        Assert.Contains(Store(w, key), s => s.Item == ore);

        // The refill: about ten minutes later, with nobody standing right there, the face is whole again.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 20.5f, w.Feet, NpcLifeWorld.Cz - 20.5f);
        w.TickAt(0.45, 660, dt: 1.0, until: () => !w.Server.World.GetBlock(site).IsAir);
        Assert.Equal(stamped, w.Server.World.GetBlock(site).Value);
    }

    [Fact]
    public void TheWoodcutter_FellsAGroveTree_BanksTheLogs_AndPlantsASapling()
    {
        using var w = new NpcLifeWorld(Content, "work_chop_yield");
        string key = Lodge(w, "woodcutter");
        int id = Resident(w, key);
        var trunks = w.Server.LodgeMarkersForTest(key).Where(m => m.Type == LodgeGenerator.MarkerGrove).Select(m => m.Pos.ToBlock()).ToList();
        w.TickAt(0.45, 120, until: () => Work(w, id).Yields > 0);
        var work = Work(w, id);
        Assert.Contains(work.Site, trunks);
        Assert.Equal("flora_sapling", Content.BlockById(w.Server.World.GetBlock(work.Site))!.Key);
        Assert.Contains(Store(w, key), s => s.Item == "wood_log" && s.Count > 0);
        Assert.True(work.NextIn >= 359.0, $"the next tree comes after {work.NextIn:0} s");
    }

    [Fact]
    public void TheHunter_HuntsInItsGround_BanksTheLoot_AtMostThreeADay()
    {
        using var w = new NpcLifeWorld(Content, "work_hunt_yield", configure: c => c.StartPlanet = "meadowlands");
        string key = Lodge(w, "hunter");
        int id = Resident(w, key);
        var center = w.Server.LodgesForTest.Single(l => l.Key == key).Center;
        // A calm, common walker the hunter may take (#2645's rules) — the roster's first standard four-legger, made one.
        var species = w.Server.SpeciesRoster.First(s => s.BodyPlan == CreatureBodyPlan.Standard && s.Legs is > 0 and < 6
                                                      && !s.HasGasSac && !s.HasWings && !s.Id.StartsWith("au_", System.StringComparison.Ordinal));
        species.Habitat = CreatureHabitat.Land;
        species.Temperament = CreatureTemperament.Passive;
        species.Activity = CreatureActivity.Cathemeral;
        species.BegsForFood = false;
        // The watching player stands on the far side of the hut: a hunter never shoots within 4 m of a player or past one.
        w.Player.State.Position = new Vector3f(center.X + 18f, w.Feet, center.Z + 0.5f);
        for (int kill = 0; kill < 4; kill++)
        {
            w.Server.SpawnCreatureAtForTest(new Vector3f(center.X - 14f, w.Feet, center.Z + 0.5f), species.Id);
            w.Server.DueNpcWorkForTest(id);
            w.TickAt(0.45, 60, until: () => Work(w, id).Yields > kill);
        }

        Assert.Equal(3, Work(w, id).Yields); // the fourth animal lives on: three a day at most
        Assert.NotEmpty(Store(w, key));
    }
}
