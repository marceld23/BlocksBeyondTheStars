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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The sea giant (#2111, Justus' idea, generation 18): a leviathan lives under the deep sea of a living water world and
/// nowhere else (never on an older save), appears by itself near a player on the water, hears a boat and a swimmer through
/// the water but not a walker on a pier, comes for the shaking, breaches (hittable only above the water) and cracks the
/// hull of the boat it strikes.
/// </summary>
public sealed class LeviathanServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_leviathan_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    private SvGameServer Started(string planet, out SqliteWorldRepository repo, long seed = 2026, int generation = WorldDescription.LeviathanGeneration)
    {
        string world = "leviathan_" + planet + "_" + (++_worlds);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = generation },
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Leviathan_IsHostedOnDeepSeaWorlds_OfGenerationEighteen_AndNowhereElse()
    {
        var ocean = Started("ocean", out var repo);
        using (repo)
        {
            var (sp, count) = ocean.LeviathanSpeciesForTest();
            Assert.NotNull(sp);
            Assert.True(count >= 1);
            Assert.Equal(CreatureBodyPlan.Leviathan, sp!.BodyPlan);
            Assert.Null(ocean.GiantSpeciesForTest().Sandworm); // a water world has no sand sea
        }

        var older = Started("ocean", out var repo2, generation: WorldDescription.LeviathanGeneration - 1);
        using (repo2)
        {
            Assert.Null(older.LeviathanSpeciesForTest().Leviathan); // an older save is unchanged
        }

        var dry = Started("desert", out var repo3);
        using (repo3)
        {
            Assert.Null(dry.LeviathanSpeciesForTest().Leviathan);
        }

        var sand = Started("sand_sea", out var repo4);
        using (repo4)
        {
            Assert.Null(sand.LeviathanSpeciesForTest().Leviathan); // the sandworm's world: ponds, no deep sea
            Assert.NotNull(sand.GiantSpeciesForTest().Sandworm);
        }
    }

    [Fact]
    public void Leviathan_SpawnsByItself_UnderTheDeepSea_NearAPlayerOnTheWater()
    {
        var server = Started("ocean", out var repo);
        using (repo)
        {
            var spot = FindDeepSea(server, run: false);
            var (top, _) = server.DeepSeaAtForTest(spot.X, spot.Z)!.Value;
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(spot.X + 0.5f, top + 1.3f, spot.Z + 0.5f); // afloat on the surface
            Assert.Empty(server.GiantsForTest());

            for (int i = 0; i < 60; i++)
            {
                server.TickForTest(0.1);
            }

            var giants = server.GiantsForTest().Where(g => g.Kind == CreatureBodyPlan.Leviathan).ToList();
            Assert.NotEmpty(giants);
            Assert.True(giants.Count <= server.LeviathanSpeciesForTest().Count);
            foreach (var g in giants)
            {
                Assert.Equal("hidden", g.Phase);
                double dist = Math.Sqrt(WorldConstants.WrapDistanceSquared(g.Position, p.State.Position, server.World.Circumference));
                Assert.InRange(dist, 60.0, 190.0);
                int gx = (int)Math.Floor(g.Position.X), gz = (int)Math.Floor(g.Position.Z);
                Assert.True(server.IsDeepSeaAtForTest(gx, gz), "a leviathan lives under the deep sea, never under the shelf");
                var (wtop, wbed) = server.DeepSeaAtForTest(gx, gz)!.Value;
                Assert.InRange(g.Position.Y, wbed + 1f, wtop + 1f); // hidden in the water column
            }
        }
    }

    [Fact]
    public void Leviathan_HearsABoatAndASwimmer_ButNotAWalkerOnAPier_NorAPickOnTheShore()
    {
        var (server, repo, p, id) = DeepSeaScene(CreatureTemperament.Aggressive);
        using (repo)
        {
            var at = p.State.Position;
            server.EmitVibrationForTest(at, VibrationSource.Boat);
            float heard = server.GiantsForTest().Single(g => g.Id == id).Attention;
            Assert.True(heard > 0f, "a boat on the deep sea was not heard");

            server.EmitVibrationForTest(at, VibrationSource.Swim);
            float swim = server.GiantsForTest().Single(g => g.Id == id).Attention;
            Assert.True(swim > heard, "a swimmer on the deep sea was not heard");

            // Mining is the sand's sound, not the sea's.
            server.EmitVibrationForTest(at, VibrationSource.Mining);
            Assert.Equal(swim, server.GiantsForTest().Single(g => g.Id == id).Attention);

            // A pier: a stone raft laid on the water; a step on it reaches nothing (the water's first block under the source is stone).
            int x = (int)Math.Floor(at.X), z = (int)Math.Floor(at.Z), y = (int)Math.Floor(at.Y);
            server.World.SetBlock(new Vector3i(x, y, z), _content.GetBlock("stone")!.NumericId);
            server.EmitVibrationForTest(new Vector3f(at.X, y + 1.1f, at.Z), VibrationSource.Step);
            Assert.Equal(swim, server.GiantsForTest().Single(g => g.Id == id).Attention);
        }
    }

    [Fact]
    public void Leviathan_ComesForTheShaking_BreachesAboveTheWater_AndCracksTheBoatsHull()
    {
        var (server, repo, p, id) = DeepSeaScene(CreatureTemperament.Aggressive);
        using (repo)
        {
            p.State.Inventory.Add("boat", 1, 1);
            string boat = server.DeployVehicleForTest("Justus", "boat");
            Assert.False(string.IsNullOrEmpty(boat), "the boat found no water to launch into");
            server.EnterSpeederForTest("Justus", boat);
            var snap = server.SpeederSnapshots.Single(s => s.Id == boat);
            var at = snap.Pos;
            p.State.Position = at;
            float hullBefore = snap.Hull;

            // Under the water: nothing to hit.
            Assert.False(server.GiantHitForTest(id, at).Hittable);

            for (int i = 0; i < 6; i++)
            {
                server.EmitVibrationForTest(at, VibrationSource.Boat);
            }

            float healthBefore = p.State.Health;
            bool rose = false, exposed = false, struck = false;
            for (int i = 0; i < 200 && !(rose && exposed && struck); i++)
            {
                p.State.Position = at;
                server.TickForTest(0.25);
                var g = server.GiantsForTest().Single(x => x.Id == id);
                rose |= g.Phase is "rear" or "breach";
                exposed |= server.GiantHitForTest(id, at).Hittable;
                struck |= server.SpeederSnapshots.Single(s => s.Id == boat).Hull < hullBefore || p.State.Health < healthBefore;
            }

            Assert.True(rose, "the leviathan never came up");
            Assert.True(exposed, "the leviathan was never above the water");
            Assert.True(struck, "the strike on the boat neither cracked the hull nor hurt the sailor");
        }
    }

    // ---------------- helpers ----------------

    /// <summary>A deep-sea column near the spawn; with <paramref name="run"/>, one with deep sea all along 30 blocks west to 60 east
    /// (the giant's way in, and where it may rear).</summary>
    private static (int X, int Z) FindDeepSea(SvGameServer server, bool run)
    {
        for (int r = 0; r < 1400; r += 17)
            for (int a = 0; a < 16; a++)
            {
                int x = (int)(Math.Cos(a * 0.3927) * r), z = (int)(Math.Sin(a * 0.3927) * r);
                if (server.IsDeepSeaAtForTest(x, z) && (!run || DeepRun(server, x, z)))
                {
                    return (x, z);
                }
            }

        Assert.Fail("no deep sea within 1400 blocks of the spawn — an ocean world should be mostly deep water");
        return default;
    }

    private static bool DeepRun(SvGameServer server, int x, int z)
    {
        for (int dx = -30; dx <= 60; dx += 3)
        {
            if (!server.IsDeepSeaAtForTest(x + dx, z))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>An ocean world with the player afloat on a deep-sea column and the leviathan moved hidden 30 blocks east.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, BlocksBeyondTheStars.GameServer.PlayerSession Player, string Giant)
        DeepSeaScene(CreatureTemperament temper)
    {
        var server = Started("ocean", out var repo);
        var spot = FindDeepSea(server, run: true);
        var (top, _) = server.DeepSeaAtForTest(spot.X, spot.Z)!.Value;
        var p = server.AddLocalPlayer("Justus");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(spot.X + 0.5f, top + 1.3f, spot.Z + 0.5f);
        var (sp, count) = server.LeviathanSpeciesForTest();
        Assert.NotNull(sp);
        Assert.True(count >= 1);
        sp!.Temperament = temper;
        server.SummonGiantForTest("Justus", "leviathan");
        var g = Assert.Single(server.GiantsForTest(), x => x.Kind == CreatureBodyPlan.Leviathan);
        int wx = spot.X + 30, wz = spot.Z;
        var (wtop, _) = server.DeepSeaAtForTest(wx, wz)!.Value;
        server.SetGiantForTest(g.Id, new Vector3f(wx + 0.5f, wtop - 9f, wz + 0.5f), (float)Math.PI);
        return (server, repo, p, g.Id);
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
        catch
        {
            // best effort — a locked SQLite file must not fail the test run
        }
    }
}
