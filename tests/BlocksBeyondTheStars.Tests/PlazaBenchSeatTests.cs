// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — evenings on the plaza: once the tavern's chairs are taken, the next residents without a post get
/// a bench of the plaza as their evening seat (never one somebody else has). They sit there in dry weather only: rain
/// sends them home, and they come back when it stops.
/// </summary>
public sealed class PlazaBenchSeatTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Benchford";
    private const double Evening = 0.72;

    [Fact]
    public void ABenchSitter_SitsOutTheDryEvening_GoesHomeWhenItRains_AndComesBackAfterwards()
    {
        using var w = new NpcLifeWorld(Content.Value, "bench_rain");
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetWeatherForTest("clear");
        var home = new Vector3f(NpcLifeWorld.Cx - 7.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        var bench = new Vector3i(NpcLifeWorld.Cx + 5, w.Feet, NpcLifeWorld.Cz + 3);
        w.Set(bench.X, bench.Y, bench.Z, "stone", ShapeCode.Pack((int)BlockShape.Bench, 0));
        int id = w.Server.SpawnVillagerForTest(Village, home);
        w.Server.GiveBenchSeatForTest(id, bench);

        // A dry evening: over to the bench, and seated on it.
        w.TickAt(Evening, 90, until: () => w.Server.NpcRoutineForTest(id).Pose == 1);
        var seated = w.Server.NpcRoutineForTest(id);
        Assert.Equal(1, seated.Pose);
        Assert.True(Math.Abs(seated.Pos.X - (bench.X + 0.5f)) < 0.01f && Math.Abs(seated.Pos.Z - (bench.Z + 0.5f)) < 0.01f,
            $"sits at {seated.Pos}, not on the bench {bench}");

        // Rain: up from the bench and home, resting there.
        w.Server.SetWeatherForTest("storm");
        w.TickAt(Evening, 90, until: () => w.Server.NpcRoutineForTest(id).Pose == 0 && Dist(w.Server.NpcRoutineForTest(id).Pos, home) < 1.5);
        var sheltering = w.Server.NpcRoutineForTest(id);
        Assert.Equal(0, sheltering.Pose);
        Assert.True(Dist(sheltering.Pos, home) < 1.5, $"waits at {sheltering.Pos}, not at home {home}");
        Assert.Equal("npc.activity.resting", sheltering.Activity);

        // … and stays there for as long as it rains (the evening is decided once per change of the weather).
        bool satInTheRain = false;
        w.TickAt(Evening, 12, every: () => satInTheRain |= w.Server.NpcRoutineForTest(id).Pose == 1);
        Assert.False(satInTheRain, "sat down on the plaza in the rain");

        // The rain stops: back to the bench.
        w.Server.SetWeatherForTest("clear");
        w.TickAt(Evening, 90, until: () => w.Server.NpcRoutineForTest(id).Pose == 1);
        Assert.Equal(1, w.Server.NpcRoutineForTest(id).Pose);
        Assert.Single(w.Server.BenchSittersForTest); // still its bench
    }

    [Fact]
    public void ARainyEvening_BeginsAtHome_AndABrokenBenchIsGivenUp()
    {
        using var w = new NpcLifeWorld(Content.Value, "bench_wet_start");
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        var home = new Vector3f(NpcLifeWorld.Cx - 7.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        var bench = new Vector3i(NpcLifeWorld.Cx + 5, w.Feet, NpcLifeWorld.Cz + 3);
        w.Set(bench.X, bench.Y, bench.Z, "stone", ShapeCode.Pack((int)BlockShape.Bench, 0));
        int id = w.Server.SpawnVillagerForTest(Village, home);
        w.Server.GiveBenchSeatForTest(id, bench);

        w.Server.SetWeatherForTest("storm");
        bool everLeftHome = false;
        w.TickAt(Evening, 30, every: () => everLeftHome |= Dist(w.Server.NpcRoutineForTest(id).Pos, home) > 3);
        Assert.False(everLeftHome, "set out for the plaza in the rain");
        Assert.Equal("npc.activity.resting", w.Server.NpcRoutineForTest(id).Activity);

        // Somebody takes the bench away: the seat is given up, and with it the "bench" of it.
        w.Server.World.SetBlock(bench, default, 0, 0, 0, NpcLifeWorld.Owner);
        w.Server.SetWeatherForTest("clear");
        w.TickAt(Evening, 20);
        Assert.Empty(w.Server.BenchSittersForTest);
        Assert.DoesNotContain(w.Server.NpcSeatsForTest, s => s.Id == id);
    }

    [Fact]
    public void InAGeneratedVillage_TheOnesWithoutATavernSeat_GetABenchOfThePlaza_EachTheirOwn()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_benches_" + Guid.NewGuid().ToString("N"));
        try
        {
            int worldsWithBenchSitters = 0;
            for (long seed = 1; seed <= 16 && worldsWithBenchSitters < 2; seed++)
            {
                var repo = new SqliteWorldRepository(new SaveGamePaths(root, "benches_" + seed));
                using (repo)
                {
                    var config = new ServerConfig
                    {
                        WorldName = "benches_" + seed,
                        Seed = seed,
                        StartPlanet = "jungle",
                        AutoSaveIntervalMinutes = 9999,
                        PlaceStarterShip = false,
                        PlaceSettlements = true,
                        PlaceWrecks = false,
                        AiLevel = AiLevel.Off,
                    };
                    var server = new SvGameServer(config, Content.Value, new LoopbackServerTransport(new LoopbackLink()), repo);
                    server.Start();
                    int circ = server.World.Circumference;
                    var sitters = server.BenchSittersForTest;
                    Assert.All(sitters, s =>
                    {
                        Assert.True(s.IsBench, $"{s.Settlement}: the seat {s.Seat} is no bench");
                        Assert.True(s.ByABenchMarker, $"{s.Settlement}: no bench marker beside {s.Seat}");
                    });

                    // Nobody shares a seat — bench, tavern chair or the chair at home.
                    var claims = server.NpcSeatsForTest.Select(s => WorldConstants.CanonicalBlock(s.Seat, circ)).ToList();
                    Assert.Equal(claims.Count, claims.Distinct().Count());

                    // A bench sitter is a resident without a post (the ones with a post rest near their work).
                    var residents = server.SettlementResidentsForTest;
                    foreach (var s in sitters)
                    {
                        var place = residents.Single(r => r.Name == s.Settlement);
                        Assert.Contains(place.Residents, r => r.Role == "settler" && r.Job.Length == 0);
                    }

                    if (sitters.Count > 0)
                    {
                        worldsWithBenchSitters++;
                    }

                    server.Stop();
                }
            }

            Assert.True(worldsWithBenchSitters > 0, "no generated village seated anybody on a plaza bench in the searched seeds");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // a locked temp file must not fail the run
            }
        }
    }

    private static double Dist(Vector3f a, Vector3f b)
    {
        double dx = a.X - b.X, dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
