// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3446 — villagers travel by train between rail-connected towns (Lyxette: "the rail line would get a real purpose"). A
/// stop belongs to the town whose box holds it or lies within twelve blocks of it, else to a base whose core is near; only a
/// plain villager goes (no post, no wish a player holds, not away); trips start by day, one per town and at most three per
/// world, only while a train that takes villagers runs on the line. A whole trip out of every player's reach runs on timers
/// (the villager rides the real train) and ends with the villager at home; in reach it walks to the platform and boards at
/// the door. Every failure — no train, the train stowed, the line changed, a frozen world — brings it home, never lost and
/// never doubled. The cab's switch lets the owner say no; it is saved, and an older record takes passengers. Far from every
/// player a trip loads no terrain and an abort never puts the traveller down at a home somebody watches; the waits and the
/// ride grow with a long line.
/// </summary>
public sealed class RailTripTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const int FarX = NpcLifeWorld.Cx + 170;

    private static float Flat(Vector3f a, Vector3f b) => (float)Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static (int NpcId, string Kind, string Phase, bool Returning, string Home, string Away)? Trip(RailTravelArena a, int npc)
        => a.Server.RailTripsForTest.Where(t => t.NpcId == npc).Select(t => ((int, string, string, bool, string, string)?)t).FirstOrDefault();

    /// <summary>Nobody is lost or doubled: the same NPCs as before, nobody on a trip or a train any more.</summary>
    private static void AllHome(RailTravelArena a, int npcsBefore)
    {
        Assert.Equal(npcsBefore, a.Server.NpcCount);
        Assert.Empty(a.Server.RailTripsForTest);
        foreach (var n in a.Server.NpcRosterForTest())
        {
            Assert.False(a.Server.NpcOnRailTripForTest(n.Id));
            Assert.Equal(string.Empty, a.Server.NpcRideForTest(n.Id).TrainId);
        }

        foreach (var t in a.Server.TrainsForTest())
        {
            Assert.Empty(a.Server.NpcPassengersForTest(t.Id));
        }
    }

    [Fact]
    public void TheStops_BelongToTheirPlaces_AndOnlyAPlainVillagerTravels()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_places", foundBase: true);
        int west = a.Town("Westdorf", RailTravelArena.StopWestX)[0];
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        Assert.Equal(("Westdorf", "settlement"), a.Server.RailPlaceForTest(a.StopWest)); // the stop inside the box
        Assert.Equal(("Ostdorf", "settlement"), a.Server.RailPlaceForTest(a.StopEast));

        // Within twelve blocks of a box: that town. In the open near the core: the base. Neither: nothing.
        a.Server.RegisterTestSettlementForTest("Nahdorf", new Vector3i(NpcLifeWorld.Cx + 5, a.W.PadY, RailTravelArena.LineZ + 13),
            new Vector3i(NpcLifeWorld.Cx + 11, a.W.PadY + 6, RailTravelArena.LineZ + 19));
        var near = a.AddStop(NpcLifeWorld.Cx + 8);
        Assert.Equal(("Nahdorf", "settlement"), a.Server.RailPlaceForTest(near));
        var atBase = a.AddStop(NpcLifeWorld.Cx - 8);
        string baseName = a.Server.BaseSnapshots.Single(b => b.Id == a.W.BaseId).Name;
        Assert.Equal((baseName, "base"), a.Server.RailPlaceForTest(atBase));
        Assert.Null(a.Server.RailPlaceForTest(new Vector3i(NpcLifeWorld.Cx + 300, a.W.Feet, NpcLifeWorld.Cz)));

        // Who goes: a plain villager — not one holding a player's wish, not one away visiting a base, not one with a post.
        Assert.True(a.Server.MayTravelByRailForTest(west));
        string key = a.Server.NpcPersonKeyForTest(west);
        a.W.Player.State.Wishes["settle_test"] = new WishLedger { Lanes = { new WishLane { Giver = key } } };
        Assert.False(a.Server.MayTravelByRailForTest(west));
        a.W.Player.State.Wishes.Clear();
        Assert.True(a.Server.MayTravelByRailForTest(west));

        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        Assert.IsType<int>(a.Server.SpawnBaseGuestForTest(a.W.BaseId, "neighbour")); // a village's villager visits the base
        var away = a.Server.NpcRosterForTest().Select(n => n.Id).Where(a.Server.NpcVisitingAwayForTest).ToList();
        Assert.Single(away);
        Assert.False(a.Server.MayTravelByRailForTest(away[0]));

        foreach (string job in new[] { "vendor", "quartermaster", "guard", "innkeeper", "gardener", "craftsman" })
        {
            int worker = a.Server.SpawnVillagerForTest("Westdorf", new Vector3f(RailTravelArena.StopWestX + 2.5f, a.W.Feet, RailTravelArena.LineZ + 6.5f), job: job);
            Assert.False(a.Server.MayTravelByRailForTest(worker), job + " may not leave the post");
        }
    }

    [Fact]
    public void Trips_StartByDay_OnePerTown_AtMostThreePerWorld_AndOnlyWithATrainThatTakesVillagers()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_planner");
        a.AddStop(NpcLifeWorld.Cx - 10);
        a.AddStop(NpcLifeWorld.Cx + 10);
        string[] towns = { "Ahorn", "Birkenau", "Erlenbach", "Lindenhof" };
        int[] xs = { RailTravelArena.StopWestX, NpcLifeWorld.Cx - 10, NpcLifeWorld.Cx + 10, RailTravelArena.StopEastX };
        for (int i = 0; i < towns.Length; i++)
        {
            a.Town(towns[i], xs[i], villagers: 2);
        }

        a.PlaceTrain(NpcLifeWorld.Cx - 20, "wagon_seats");

        // No train for villagers: the switch off, or the autopilot off (a driven train never halts at a stop).
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, passengers: 0);
        a.Server.PlanRailTripsForTest();
        Assert.Empty(a.Server.RailTripsForTest);
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, passengers: 1, autopilot: 0);
        a.Server.PlanRailTripsForTest();
        Assert.Empty(a.Server.RailTripsForTest);
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 1);

        // At night nobody sets off.
        a.Server.SetLocalDayFractionForTest(0.9, NpcLifeWorld.Cx);
        a.Server.PlanRailTripsForTest();
        Assert.Empty(a.Server.RailTripsForTest);

        // By day: one trip per pass, each from another town, never more than three on the world.
        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        for (int i = 0; i < 6; i++)
        {
            a.Server.PlanRailTripsForTest();
        }

        var trips = a.Server.RailTripsForTest;
        Assert.Equal(BlocksBeyondTheStars.GameServer.GameServer.MaxRailTripsPerWorld, trips.Count);
        Assert.Equal(trips.Count, trips.Select(t => t.Home).Distinct().Count());
        Assert.All(trips, t => Assert.NotEqual(t.Home, t.Away));
        Assert.All(trips, t => Assert.Equal("Villager", t.Kind));
        Assert.All(trips, t => Assert.Equal("npc.activity.train_trip", a.Server.NpcRoutineForTest(t.NpcId).Activity));
    }

    [Fact]
    public void ARoundTripOutOfReach_RunsOnTimersWithTheRealTrain_AndTheVillagerIsHomeAfterwards()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_virtual");
        a.Server.PauseRailTripPlannerForTest(); // only the trip the test starts
        int v = a.Town("Westdorf", RailTravelArena.StopWestX)[0];
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        int npcs = a.Server.NpcCount;
        var rest = a.Server.NpcRestForTest(v);
        a.FarAway();

        Assert.Equal(v, a.Server.StartRailTripForTest("Westdorf"));
        var phases = new List<string>();
        bool rode = false;
        a.W.TickAt(0.4, 900, dt: 0.5, until: () => Trip(a, v) is null, every: () =>
        {
            if (Trip(a, v) is { } t)
            {
                string phase = (t.Returning ? "back:" : string.Empty) + t.Phase;
                if (phases.Count == 0 || phases[^1] != phase)
                {
                    phases.Add(phase);
                }

                if (t.Phase == "Riding")
                {
                    rode |= a.Server.NpcPassengersForTest(a.TrainId).Any(p => p.NpcId == v);
                }

                if (t.Phase == "Visiting")
                {
                    a.Server.EndRailTripVisitsForTest();
                }
            }
        });

        Assert.Null(Trip(a, v));
        Assert.True(rode, "the villager never sat in the train");
        Assert.Equal(new[] { "ToStation", "Waiting", "Riding", "ToSight", "Visiting", "back:ToStation", "back:Waiting", "back:Riding", "back:GoingHome" },
            phases.Where(p => p != "back:Visiting").ToArray());
        Assert.True(Flat(rest, a.Server.NpcRideForTest(v).Pos) < 2f, $"not home: {a.Server.NpcRideForTest(v).Pos} vs {rest}");
        AllHome(a, npcs);
    }

    [Fact]
    public void InReach_TheVillagerWalksToThePlatform_BoardsAtTheDoor_AndWalksToTheSquare()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_visible");
        a.Server.PauseRailTripPlannerForTest(); // only the trip the test starts
        int v = a.Town("Westdorf", RailTravelArena.StopWestX)[0];
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        int npcs = a.Server.NpcCount;
        int unseenBefore = a.Server.NpcUnseenMovesForTest;

        Assert.Equal(v, a.Server.StartRailTripForTest("Westdorf"));
        bool walked = false;
        a.W.TickAt(0.4, 60, until: () => Trip(a, v)?.Phase != "ToStation", every: () => walked |= a.Server.NpcHasGoalForTest(v));
        Assert.True(walked, "the villager did not walk to the platform");
        Assert.Equal("Waiting", Trip(a, v)?.Phase);
        var platform = a.Server.RailStopPlatformSpotForTest(a.StopWest)!.Value;
        Assert.True(Flat(platform, a.Server.NpcRideForTest(v).Pos) < 2f);

        // It boards the train that halts at its stop, then rides to the other town, steps off and walks to the square.
        a.W.TickAt(0.4, 240, until: () => Trip(a, v)?.Phase == "Riding");
        Assert.Equal(a.TrainId, a.Server.NpcRideForTest(v).TrainId);
        a.W.TickAt(0.4, 240, until: () => Trip(a, v)?.Phase == "ToSight");
        var off = a.Server.NpcRideForTest(v);
        Assert.Equal(string.Empty, off.TrainId);
        Assert.True(Flat(off.Pos, new Vector3f(a.StopEast.X, 0, a.StopEast.Z)) < 12f, $"stepped off at {off.Pos}, far from its stop");
        a.W.TickAt(0.4, 60, until: () => Trip(a, v)?.Phase == "Visiting");
        Assert.Equal("Visiting", Trip(a, v)?.Phase);
        Assert.Equal("npc.activity.visiting", a.Server.NpcRoutineForTest(v).Activity);
        Assert.Equal(a.Server.NpcUnseenMovesForTest, unseenBefore); // every visible leg was walked, nobody popped

        // The way back runs out of reach: home again, the routine back.
        a.FarAway();
        a.Server.EndRailTripVisitsForTest();
        a.W.TickAt(0.4, 600, dt: 0.5, until: () => Trip(a, v) is null);
        AllHome(a, npcs);
    }

    [Fact]
    public void EveryFailure_BringsTheTravellerHome_NeverLostNorDoubled()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_failures");
        a.Server.PauseRailTripPlannerForTest(); // only the trip the test starts
        int v = a.Town("Westdorf", RailTravelArena.StopWestX)[0];
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        int npcs = a.Server.NpcCount;
        var rest = a.Server.NpcRestForTest(v);

        // A world nobody ticked for a while: the trip is over, the villager simply home.
        Assert.Equal(v, a.Server.StartRailTripForTest("Westdorf"));
        a.Server.AdvanceUptimeForTest(30);
        a.W.TickAt(0.4, 0.25);
        AllHome(a, npcs);
        Assert.True(Flat(rest, a.Server.NpcRideForTest(v).Pos) < 2f);

        // No train halts any more (its autopilot off): after five minutes on the platform the villager walks home.
        Assert.Equal(v, a.Server.StartRailTripForTest("Westdorf"));
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 0);
        a.W.TickAt(0.4, 30, until: () => Trip(a, v)?.Phase == "Waiting");
        a.W.TickAt(0.4, 400, dt: 1.0, until: () => Trip(a, v) is null);
        AllHome(a, npcs);
        Assert.True(Flat(rest, a.Server.NpcRideForTest(v).Pos) < 2f);

        // The train stowed with the villager aboard: set down beside the wagon, it walks home.
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 1);
        Assert.Equal(v, a.Server.StartRailTripForTest("Westdorf"));
        a.W.TickAt(0.4, 300, until: () => Trip(a, v)?.Phase == "Riding");
        a.W.Player.State.Position = a.Server.WagonPoseForTest(a.TrainId, 0)!.Value.Pos;
        a.Server.StowTrainForTest(a.PlayerId, a.TrainId);
        Assert.Empty(a.Server.TrainsForTest());
        Assert.Equal("GoingHome", Trip(a, v)?.Phase);
        a.Home();
        a.W.TickAt(0.4, 240, until: () => Trip(a, v) is null);
        AllHome(a, npcs);

        // The line changed under the platform (its first pylon mined: the train goes back to the pack): home again.
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        Assert.Equal(v, a.Server.StartRailTripForTest("Westdorf"));
        a.W.TickAt(0.4, 30, until: () => Trip(a, v)?.Phase == "Waiting");
        a.W.Player.State.Position = new Vector3f(RailTravelArena.WestX + 0.5f, a.W.Feet, RailTravelArena.LineZ - 2.5f);
        a.Server.MineBlock(a.PlayerId, RailTravelArena.WestX, a.W.Feet, RailTravelArena.LineZ);
        a.Home();
        a.W.TickAt(0.4, 240, until: () => Trip(a, v) is null);
        AllHome(a, npcs);
    }

    [Fact]
    public void TheTimeouts_GrowWithALongLine_AndKeepTheShortLineValues()
    {
        // The arena's 80-block line at the new cab's setting (16 blocks/s, cruising ~9.6) with two stops: a round of under a
        // minute — the platform wait, the ride and a guest's boarding window keep their short-line values.
        var (oneWay, round) = SvGameServer.RailTrainTimes(80, closed: false, stops: 2, setting: 2, publicTrain: false);
        Assert.Equal((80 / 9.6) + 16, oneWay, 3);
        Assert.Equal(2 * oneWay, round, 3);
        Assert.Equal((300.0, 900.0, 180.0), SvGameServer.RailTripTimeouts(round));

        // A 2000-block line at the slowest setting (8 blocks/s, cruising ~4.8) with six stops: ~465 s one way, a round of
        // ~930 s — every timeout allows 1.25 rounds (~1160 s), far more than the short-line values that cut such trips off.
        (oneWay, round) = SvGameServer.RailTrainTimes(2000, closed: false, stops: 6, setting: 1, publicTrain: false);
        Assert.Equal((2000 / 4.8) + 48, oneWay, 3);
        var (wait, ride, guest) = SvGameServer.RailTripTimeouts(round);
        Assert.Equal(1.25 * round, wait, 3);
        Assert.Equal(1.25 * round, ride, 3);
        Assert.Equal(1.25 * round, guest, 3);
        Assert.True(wait > 1100 && ride > 900);

        // A loop's round is one lap; the public train halts 30 s at every stop.
        var loop = SvGameServer.RailTrainTimes(960, closed: true, stops: 2, setting: 2, publicTrain: true);
        Assert.Equal((960 / 9.6) + 60, loop.Round, 3);
        Assert.Equal(loop.OneWay, loop.Round, 3);
    }

    [Fact]
    public void FarFromThePlayers_ATripLoadsNoTerrain_AndAnAbortNeverPopsTheTravellerHomeInFrontOfThem()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_far", configure: c => c.MaxAnchoredChunks = 0);
        a.Server.PauseRailTripPlannerForTest(); // only the trip the test starts
        a.Town("Westdorf", RailTravelArena.StopWestX);
        int v = a.FarTown("Fernstadt", FarX)[0];
        GroundWestOf(a, FarX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        Assert.Equal((300.0, 900.0, 180.0), a.Server.RailTripTimeoutsForTest(a.TrainId)); // a short line: the plain values
        int npcs = a.Server.NpcCount;
        var rest = a.Server.NpcRestForTest(v);

        // The player stays at Fernstadt beside the villager's home, seeing one chunk round: Westdorf, 240 blocks west, is out of
        // everybody's reach.
        a.W.Player.ViewDistance = 1;
        a.W.Player.State.Position = new Vector3f(FarX + 2.5f, a.W.Feet, RailTravelArena.LineZ + 11.5f);
        Assert.Equal(v, a.Server.StartRailTripForTest("Fernstadt"));
        a.W.TickAt(0.4, 400, dt: 0.5, until: () => Trip(a, v)?.Phase == "Riding");
        Assert.Equal(a.TrainId, a.Server.NpcRideForTest(v).TrainId);

        // Westdorf's terrain is unloaded while it rides there. Out of reach it steps off, walks to the square and stays — on
        // guesses and timers, without loading a single chunk of the town on the tick thread.
        var square = new Vector3i(RailTravelArena.StopWestX, a.W.Feet, RailTravelArena.LineZ + 6);
        var town = ChunksRound(a.StopWest, 8).Concat(ChunksRound(square, 8)).Distinct().ToList();
        var here = new ChunkCoord(WorldConstants.WorldToChunk(FarX), WorldConstants.WorldToChunk(a.W.Feet), WorldConstants.WorldToChunk(RailTravelArena.LineZ));
        a.Server.World.UnloadFarChunks(new[] { here }, 4);
        Assert.All(town, c => Assert.False(a.Server.World.IsChunkLoaded(c)));
        a.W.TickAt(0.4, 400, dt: 0.5, until: () => Trip(a, v)?.Phase == "Visiting");
        Assert.Equal("Visiting", Trip(a, v)?.Phase);
        Assert.True(Flat(a.Server.NpcRideForTest(v).Pos, new Vector3f(square.X, 0, square.Z)) < 6f, "not at the town's square");
        Assert.All(town, c => Assert.False(a.Server.World.IsChunkLoaded(c), $"chunk {c.X},{c.Y},{c.Z} of the far town was loaded"));

        // The world is not ticked for a while (nobody was on it) — but the player at Fernstadt watches the villager's home: it
        // is not put down there in front of them. Its way home is a timed leg that comes in from out of sight and walks the
        // last stretch.
        a.Server.AdvanceUptimeForTest(30);
        a.W.TickAt(0.4, 0.25);
        Assert.Equal("GoingHome", Trip(a, v)?.Phase);
        Assert.True(Flat(rest, a.Server.NpcRideForTest(v).Pos) > 100f, $"popped home in front of the player: {a.Server.NpcRideForTest(v).Pos}");
        bool walked = false;
        a.W.TickAt(0.4, 420, dt: 0.5, until: () => Trip(a, v) is null, every: () => walked |= a.Server.NpcHasGoalForTest(v));
        Assert.True(walked, "the traveller did not walk the last stretch home");
        Assert.True(Flat(rest, a.Server.NpcRideForTest(v).Pos) < 2f, $"not home: {a.Server.NpcRideForTest(v).Pos} vs {rest}");
        AllHome(a, npcs);
    }

    /// <summary>The chunks round a cell (<paramref name="r"/> blocks every way).</summary>
    private static IEnumerable<ChunkCoord> ChunksRound(Vector3i at, int r)
    {
        for (int cx = WorldConstants.WorldToChunk(at.X - r); cx <= WorldConstants.WorldToChunk(at.X + r); cx++)
        {
            for (int cy = WorldConstants.WorldToChunk(at.Y - r); cy <= WorldConstants.WorldToChunk(at.Y + r); cy++)
            {
                for (int cz = WorldConstants.WorldToChunk(at.Z - r); cz <= WorldConstants.WorldToChunk(at.Z + r); cz++)
                {
                    yield return new ChunkCoord(cx, cy, cz);
                }
            }
        }
    }

    /// <summary>Open ground west of the far town's pad (a stone floor at the pad's level, cleared above), so a traveller coming
    /// home unseen has somewhere out of the player's sight to come in from.</summary>
    private static void GroundWestOf(RailTravelArena a, int x)
    {
        var stone = a.W.Block("stone");
        for (int cx = x - 44; cx < x - 7; cx++)
        {
            for (int cz = RailTravelArena.LineZ + 4; cz <= RailTravelArena.LineZ + 14; cz++)
            {
                a.Server.World.SetBlock(new Vector3i(cx, a.W.PadY, cz), stone);
                for (int y = a.W.PadY + 1; y <= a.W.PadY + 7; y++)
                {
                    var cell = new Vector3i(cx, y, cz);
                    if (!a.Server.World.GetBlock(cell).IsAir)
                    {
                        a.Server.World.SetBlock(cell, BlockId.Air);
                    }
                }
            }
        }
    }

    [Fact]
    public void ThePassengerSwitch_IsTheOwners_IsSaved_AndAnOlderRecordTakesVillagers()
    {
        using var a = new RailTravelArena(Content.Value, "rail_trip_switch");
        a.Town("Westdorf", RailTravelArena.StopWestX);
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        Assert.True(a.Server.TrainTakesPassengersForTest(a.TrainId)); // a new cab: on
        Assert.True(Assert.Single(a.Server.NetTrainsForTest()).TakesPassengers);

        // A stranger may not switch it; the owner may.
        var stranger = a.Server.AddLocalPlayer("Stranger");
        stranger.State.AboardShip = false;
        a.Server.SetTrainForTest("Stranger", a.TrainId, passengers: 0);
        Assert.True(a.Server.TrainTakesPassengersForTest(a.TrainId));
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, passengers: 0);
        Assert.False(a.Server.TrainTakesPassengersForTest(a.TrainId));
        Assert.False(Assert.Single(a.Server.NetTrainsForTest()).TakesPassengers);
        Assert.Null(a.Server.StartRailTripForTest());

        // Saved with the train (an appended field) and read back; a record from before the switch takes villagers.
        string record = a.Server.TrainSaveRecordForTest();
        Assert.EndsWith(",0", record);
        a.Server.LoadTrainRecordForTest(record);
        Assert.False(a.Server.TrainTakesPassengersForTest(a.TrainId));
        a.Server.LoadTrainRecordForTest(record.Substring(0, record.LastIndexOf(',')));
        Assert.True(a.Server.TrainTakesPassengersForTest(a.TrainId));
        Assert.NotNull(a.Server.StartRailTripForTest());
    }
}
