// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The intercity monorail (#2125, terrain generation 19; Marcel's rules: no tickets, no ID cards, no vending machines): a
/// world with at least two inhabited towns or cities gets — with a chance the tests pin to 1 — one generated line between
/// the closest pair whose route fits. A station at each town's edge, the stops on the line, pylons along a clear corridor,
/// a public train that shuttles between the stations and waits at each one, that anyone may ride and nobody may steer,
/// stow or couple, protected pylons and stations, both stations on the map, the same line for the same seed, and all of it
/// back after a restart. Older generations, airless bodies, restricted types and the city world never get one.
/// </summary>
public sealed class IntercityRailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_intercity_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A meadow world whose seed carries two towns about 200 blocks apart (probed 2026-09-28); the search behind it
    /// only runs when a content change moved the towns.</summary>
    private const string Planet = "meadowlands";
    private const long KnownSeed = 5;

    private SvGameServer Start(string save, long seed, out SqliteWorldRepository repo, string planet = Planet, Action<ServerConfig>? configure = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            IntercityRailChance = 1.0, // a line wherever one fits
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A world with a line: the known seed first, a short search behind it.</summary>
    private SvGameServer StartWithLine(string save, out SqliteWorldRepository repo, out long seed)
    {
        foreach (long s in new[] { KnownSeed }.Concat(Enumerable.Range(1, 10).Select(i => (long)i).Where(i => i != KnownSeed)))
        {
            var server = Start(save + s, s, out repo);
            if (server.IntercityRailForTest() is not null)
            {
                seed = s;
                return server;
            }

            server.Stop();
            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"No intercity line on '{Planet}' across eleven seeds.");
    }

    [Fact]
    public void TheLine_JoinsTwoTowns_ItsStopsLieOnIt_ItsCorridorIsClear_AndBothStationsAreOnTheMap()
    {
        var server = StartWithLine("line", out var repo, out _);
        using (repo)
        {
            var ic = server.IntercityRailForTest()!.Value;
            Assert.Equal(2, ic.Stations.Count);
            Assert.NotEqual(ic.Stations[0].Settlement, ic.Stations[1].Settlement);

            // Both stations stand at an inhabited town or city, level with its foundation row.
            var names = server.SettlementNamesForTest;
            var tiers = server.SettlementTiersForTest;
            var boxes = server.SettlementBoxesForTest;
            foreach (var st in ic.Stations)
            {
                int i = names.ToList().IndexOf(st.Settlement);
                Assert.True(i >= 0, $"the station's town '{st.Settlement}' exists");
                Assert.Contains(tiers[i], new[] { "town", "city" });
                Assert.False(boxes[i].Ruined);
                Assert.Equal(boxes[i].GroundY, st.Origin.Y);
                Assert.Equal(_content.GetBlock(RailRules.StopBlockKey)!.NumericId, server.World.GetBlock(st.Stop));
                Assert.NotNull(server.CrystalDeviceOutput(st.Stop)); // the stop is a Crystal-Net device
            }

            // One line through every generated pylon, open, with both stops on it.
            var line = Assert.Single(server.RailLinesForTest());
            Assert.False(line.Closed);
            Assert.Equal(2, line.Stops);
            Assert.Equal(ic.Pylons.Count, line.Pylons.Count);
            Assert.Equal(ic.Pylons.ToHashSet(), line.Pylons.ToHashSet());
            Assert.True(line.TotalArc > 40f);

            // Every pylon top is a pylon block, and every link passes the player's own clearance rule.
            var pylonId = _content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
            for (int j = 0; j < ic.Pylons.Count; j++)
            {
                Assert.Equal(pylonId, server.World.GetBlock(ic.Pylons[j]));
                if (j > 0)
                {
                    Assert.True(server.RailLinkClearForTest(ic.Pylons[j - 1], ic.Pylons[j]), $"the corridor between pylons {j - 1} and {j} is clear");
                    Assert.Contains(ic.Pylons[j - 1], server.RailLinksForTest(ic.Pylons[j]));
                }
            }

            // Both stations are on the world map, named after their towns.
            var p = server.AddLocalPlayer("Mapper");
            var pois = server.PlanetPoisForTest(p.State.PlayerId).Where(x => x.Type == "rail_station").ToList();
            Assert.Equal(2, pois.Count);
            foreach (var st in ic.Stations)
            {
                Assert.Contains(pois, x => x.Name == st.Settlement + " Station");
            }

            // A stop a player sets beside the line does not join it: the public train halts at the two stations only.
            p.State.AboardShip = false;
            p.State.Fly = true;
            p.State.Inventory.SetSlot(5, new ItemStack(RailRules.StopBlockKey, 1));
            var mid = ic.Pylons[ic.Pylons.Count / 2];
            var beside = new Vector3i(mid.X + 1, mid.Y, mid.Z);
            server.World.SetBlock(beside, BlockId.Air); // right against the pylon's top (the ground may reach up to it)
            p.State.Position = new Vector3f(beside.X + 0.5f, beside.Y + 2.5f, beside.Z + 2.5f);
            server.PlaceBlock(p.State.PlayerId, beside.X, beside.Y, beside.Z, RailRules.StopBlockKey);
            Assert.Equal(_content.GetBlock(RailRules.StopBlockKey)!.NumericId, server.World.GetBlock(beside));
            Assert.Equal(2, Assert.Single(server.RailLinesForTest()).Stops);

            // The placement is pinned: the line and both stations have their records.
            var recs = server.PlacementRecordsForTest;
            Assert.Contains(recs, r => r.Kind == "rail_line" && r.Placed && r.Composition!.Count == ic.Pylons.Count);
            Assert.Equal(2, recs.Count(r => r.Kind == "rail_station" && r.Placed));
        }
    }

    [Fact]
    public void ThePublicTrain_ShuttlesAndWaitsAtBothStations_AnyoneRides_NobodyControlsIt_AndTheLineIsProtected()
    {
        var server = StartWithLine("train", out var repo, out _);
        using (repo)
        {
            var ic = server.IntercityRailForTest()!.Value;
            var train = Assert.Single(server.TrainsForTest());
            Assert.True(RailRules.IsPublic(train.OwnerId));
            Assert.Equal(RailRules.PublicTrainWagons, train.Wagons);
            Assert.True(train.Autopilot);
            Assert.Equal(RailRules.PublicTrainSpeed, train.Speed);

            // A stranger — allied with nobody — may ride it.
            var stranger = server.AddLocalPlayer("Stranger");
            stranger.State.AboardShip = false;
            stranger.State.Fly = true;
            stranger.State.Inventory.SetSlot(0, new ItemStack("diamond_drill", 1));
            stranger.State.Inventory.SetSlot(1, new ItemStack("wagon_seats", 1));

            // …but nobody steers it, halts it, packs it up or couples onto it.
            server.SetTrainForTest("Stranger", train.Id, speed: 3, halt: 1, autopilot: 0);
            var after = Assert.Single(server.TrainsForTest());
            Assert.Equal(RailRules.PublicTrainSpeed, after.Speed);
            Assert.True(after.Autopilot);
            server.StowTrainForTest("Stranger", train.Id);
            Assert.Single(server.TrainsForTest());
            var tail = server.WagonPoseForTest(train.Id, train.Wagons.Count)!.Value.Pos;
            stranger.State.Position = new Vector3f(tail.X, tail.Y + 1f, tail.Z);
            Assert.False(server.RailWagonForTest("Stranger", "wagon_seats", tail));
            Assert.Equal(RailRules.PublicTrainWagons, Assert.Single(server.TrainsForTest()).Wagons);
            Assert.Equal(1, stranger.State.Inventory.CountOf("wagon_seats"));

            // The linker cannot uncouple (or couple) a generated pylon.
            var a = ic.Pylons[3];
            var b = ic.Pylons[4];
            Assert.False(server.RailLinkerForTest("Stranger", a, b));
            Assert.Contains(b, server.RailLinksForTest(a));

            // The pylons and the stations are protected; an ordinary pylon beside the line is not.
            var pylonId = _content.GetBlock(RailRules.PylonBlockKey)!.NumericId;
            var mid = ic.Pylons[ic.Pylons.Count / 2];
            stranger.State.Position = new Vector3f(mid.X + 1.5f, mid.Y + 1f, mid.Z + 0.5f);
            Assert.True(server.IsIntercityRailBlockForTest(mid));
            server.MineBlock("Stranger", mid.X, mid.Y, mid.Z);
            Assert.Equal(pylonId, server.World.GetBlock(mid));
            var loose = new Vector3i(mid.X + 1, mid.Y + 7, mid.Z);
            server.World.SetBlock(loose, pylonId);
            stranger.State.Position = new Vector3f(loose.X + 0.5f, loose.Y - 1f, loose.Z + 1.5f);
            Assert.False(server.IsIntercityRailBlockForTest(loose));
            server.MineBlock("Stranger", loose.X, loose.Y, loose.Z);
            Assert.True(server.World.GetBlock(loose).IsAir, "an ordinary pylon mines");
            var floor = new Vector3i(ic.Stations[0].Origin.X + 2, ic.Stations[0].Origin.Y, ic.Stations[0].Origin.Z + 2);
            var floorId = server.World.GetBlock(floor);
            Assert.False(floorId.IsAir);
            stranger.State.Position = new Vector3f(floor.X + 0.5f, floor.Y + 1.2f, floor.Z + 0.5f);
            server.MineBlock("Stranger", floor.X, floor.Y, floor.Z);
            Assert.Equal(floorId, server.World.GetBlock(floor));

            // Board the passenger wagon (a seat) where it stands.
            var wagon = server.WagonPoseForTest(train.Id, 1)!.Value.Pos;
            stranger.State.Position = new Vector3f(wagon.X, wagon.Y + 0.5f, wagon.Z);
            server.EnterTrainForTest("Stranger", train.Id, 1, seat: 0);
            Assert.Contains("Stranger", Assert.Single(server.TrainsForTest()).Riders);
            var boarded = stranger.State.Position;

            // Ride: the train runs from station to station and halts at each one for the public halt time.
            var stops = ic.Stations.Select(s => s.Stop).ToHashSet();
            var halts = new List<(Vector3i Stop, double Start, double End)>();
            bool halted = false;
            double now = 0, start = 0;
            Vector3i? at = null;
            float farthest = 0f;
            for (int step = 0; step < 1200 && halts.Count < 3; step++)
            {
                server.TickForTest(0.5);
                now += 0.5;
                var h = server.TrainHaltForTest(train.Id);
                if (h.Halted && !halted)
                {
                    halted = true;
                    start = now;
                    at = h.Stop;
                    var cab = server.WagonPoseForTest(train.Id, 0)!.Value.Pos;
                    Assert.True(h.Stop.HasValue && stops.Contains(h.Stop.Value), "the train halts at a station's stop");
                    Assert.True(Distance(cab, h.Stop!.Value) < 5f, "the cab stands at the stop");
                }
                else if (!h.Halted && halted)
                {
                    halted = false;
                    halts.Add((at!.Value, start, now));
                }

                float dx = (float)WorldConstants.WrapDeltaX((double)(stranger.State.Position.X - boarded.X), server.World.Circumference), dz = stranger.State.Position.Z - boarded.Z;
                farthest = Math.Max(farthest, (float)Math.Sqrt(dx * dx + dz * dz));
            }

            Assert.True(halts.Count >= 3, $"three halts in ten minutes (saw {halts.Count})");
            Assert.InRange(halts[0].End - halts[0].Start, 0.0, RailRules.PublicStopHaltSeconds + 0.6); // the first halt began at load
            for (int i = 1; i < halts.Count; i++)
            {
                Assert.InRange(halts[i].End - halts[i].Start, RailRules.PublicStopHaltSeconds - 0.6, RailRules.PublicStopHaltSeconds + 0.6);
                Assert.NotEqual(halts[i - 1].Stop, halts[i].Stop); // it shuttles: the other station every time
            }

            Assert.Contains("Stranger", Assert.Single(server.TrainsForTest()).Riders);
            Assert.True(farthest > 60f, "the rider travelled with the train");
        }
    }

    [Fact]
    public void TheLine_IsTheSameForTheSameSeed_AndItsGraphStopsAndTrainSurviveARestart()
    {
        var first = StartWithLine("seedA", out var repoA, out long seed);
        var plan = first.IntercityRailForTest()!.Value;
        string trainId = Assert.Single(first.TrainsForTest()).Id;
        var links = first.RailLinksForTest(plan.Pylons[2]).ToHashSet();
        first.Stop();
        repoA.Dispose();

        // The same seed, a fresh save: the same line, the same stations.
        var twin = Start("seedB" + seed, seed, out var repoB);
        using (repoB)
        {
            var again = twin.IntercityRailForTest();
            Assert.NotNull(again);
            Assert.Equal(plan.Pylons, again!.Value.Pylons);
            Assert.Equal(plan.Stations, again.Value.Stations);
            twin.Stop();
        }

        // The first save again: everything comes back from the records and the rail metadata.
        var reloaded = Start("seedA" + seed, seed, out var repoC);
        using (repoC)
        {
            var back = reloaded.IntercityRailForTest();
            Assert.NotNull(back);
            Assert.Equal(plan.Pylons, back!.Value.Pylons);
            Assert.Equal(plan.Stations, back.Value.Stations);
            var line = Assert.Single(reloaded.RailLinesForTest());
            Assert.Equal(2, line.Stops);
            Assert.Equal(links, reloaded.RailLinksForTest(plan.Pylons[2]).ToHashSet());
            var train = Assert.Single(reloaded.TrainsForTest());
            Assert.Equal(trainId, train.Id);
            Assert.True(RailRules.IsPublic(train.OwnerId));
            Assert.Equal(RailRules.PublicTrainWagons, train.Wagons);
            Assert.Equal(line.Id, train.LineId);
            foreach (var st in back.Value.Stations)
            {
                Assert.NotNull(reloaded.CrystalDeviceOutput(st.Stop));
            }
        }
    }

    [Fact]
    public void NoLine_OnAnOlderGeneration_OrWhenTheChanceSaysNo()
    {
        // The same seed on generation 18: the towns are there (the terrain is the same), the line never is.
        var older = Start("gen18", KnownSeed, out var repo18, configure: c => c.World.TerrainGeneration = 18);
        using (repo18)
        {
            var tiers = older.SettlementTiersForTest;
            var boxes = older.SettlementBoxesForTest;
            Assert.True(Enumerable.Range(0, tiers.Count).Count(i => tiers[i] is "town" or "city" && !boxes[i].Ruined) >= 2,
                "the generation-18 world has the towns a line would join");
            Assert.Null(older.IntercityRailForTest());
            Assert.Empty(older.RailLinesForTest());
            Assert.Empty(older.TrainsForTest());
            Assert.DoesNotContain(older.PlacementRecordsForTest, r => r.Kind is "rail_line" or "rail_station");
            older.Stop();
        }

        // The chance on the line's own lane: at 0 the world decides "no line" and pins it.
        var unlucky = Start("nochance", KnownSeed, out var repo0, configure: c => c.IntercityRailChance = 0.0);
        using (repo0)
        {
            Assert.Null(unlucky.IntercityRailForTest());
            Assert.Contains(unlucky.PlacementRecordsForTest, r => r.Kind == "rail_line" && !r.Placed);
            Assert.Empty(unlucky.TrainsForTest());
            unlucky.Stop();
        }
    }

    [Fact]
    public void NoLine_OnAnAirlessARestrictedOrTheCityWorld()
    {
        // Airless (no settlements), restricted (Titas: a structure whitelist), the city world (one composed city).
        foreach (var (planet, seed) in new[] { ("asteroid", 3L), ("titas", 77L), ("gds_desert", 20260912L) })
        {
            var server = Start("none_" + planet, seed, out var repo, planet);
            using (repo)
            {
                Assert.Null(server.IntercityRailForTest());
                Assert.Empty(server.TrainsForTest());
                server.Stop();
            }
        }
    }

    [Fact]
    public void TheStation_KeepsTheCorridorFree_AndCarriesItsPylonsStopBenchesAndRoof()
    {
        var pylon = _content.GetBlock(RailRules.PylonBlockKey)!.NumericId.Value;
        var stop = _content.GetBlock(RailRules.StopBlockKey)!.NumericId.Value;
        for (int heading = 0; heading < 4; heading++)
        {
            var s = RailStationGenerator.Generate(heading, _content);
            var (w, l) = RailStationGenerator.Footprint(heading);
            Assert.Equal((w, l), (s.Width, s.Length));
            Assert.Equal(RailStationGenerator.Height, s.Height);

            (int X, int Z) Cell(int u, int v) => RailStationGenerator.ToStructure(heading, u, v);
            var end = Cell(RailStationGenerator.EndPylonU, RailStationGenerator.TrackV);
            var exit = Cell(RailStationGenerator.ExitPylonU, RailStationGenerator.TrackV);
            var st = Cell(RailStationGenerator.StopU, RailStationGenerator.StopV);
            Assert.Equal(pylon, s.Get(end.X, 0, end.Z));
            Assert.Equal(pylon, s.Get(exit.X, 0, exit.Z));
            Assert.Equal(stop, s.Get(st.X, 0, st.Z));
            Assert.Contains(s.Markers, m => m.Type == RailStationGenerator.StopMarker && m.LocalPos == new Vector3i(st.X, 0, st.Z));

            // The wagon's box (three cells across the track, four rows over the floor) is air the whole length of the hall,
            // and the roof closes over it.
            for (int u = 0; u < RailStationGenerator.Length; u++)
            {
                for (int dv = -1; dv <= 1; dv++)
                {
                    var c = Cell(u, RailStationGenerator.TrackV + dv);
                    for (int y = 1; y <= 5; y++)
                    {
                        Assert.Equal(0, s.Get(c.X, y, c.Z));
                    }

                    Assert.NotEqual(0, s.Get(c.X, RailStationGenerator.Height - 1, c.Z));
                }
            }

            // Benches: seat shapes on both platforms.
            int seats = 0;
            for (int x = 0; x < s.Width; x++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    if (FurnitureShapes.IsSeat(ShapeCode.ShapeOf(s.GetShape(x, 1, z))))
                    {
                        seats++;
                    }
                }
            }

            Assert.Equal(12, seats);
        }
    }

    [Fact]
    public void TheChanceRoll_IsUniformAcrossSmallWorldSeeds()
    {
        // The roll decides 60 % of the worlds that have two towns — for the seeds people and tests actually use (1, 2, 3 …)
        // as much as for random ones.
        foreach (string body in new[] { "sys0-p0", "sys3-p1" })
        {
            int below = Enumerable.Range(1, 4000).Count(s => SvGameServer.IntercityRollForTest(s, body) < 0.6);
            Assert.InRange(below / 4000.0, 0.56, 0.64);
            Assert.Equal(SvGameServer.IntercityRollForTest(7, body), SvGameServer.IntercityRollForTest(7, body));
        }
    }

    private static float Distance(Vector3f p, Vector3i cell)
    {
        float dx = p.X - (cell.X + 0.5f), dz = p.Z - (cell.Z + 0.5f);
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
