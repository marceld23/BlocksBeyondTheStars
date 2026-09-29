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
/// The gas giant on a live server (#2112): the ship lands on a metal deck hanging over the gas, the gas kills whoever
/// falls in (no armour helps, its own death line), a sky city on an island holds a pocket of air, and the sky giant is
/// hosted, appears by itself over the islands, drifts, calls and can be hit — while no other world hosts one.
/// </summary>
public sealed class GasGiantServerTests : IDisposable
{
    private const string Key = "gas_giant";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_gasgiant_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private int _worlds;

    private SvGameServer Started(string planet, out SqliteWorldRepository repo, long seed = 2026)
    {
        string world = "gasgiant_" + planet + "_" + (++_worlds);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = WorldDescription.GasGiantGeneration },
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static int TopSolidY(SvGameServer server, int x, int z, int from)
    {
        for (int y = from; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return int.MinValue;
    }

    [Fact]
    public void TheHomePad_IsAMetalDeck_ThreeBlocksOverTheGas_WithGasUnderIt()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            Assert.NotEqual(int.MinValue, sea);
            int top = TopSolidY(server, 0, 0, sea + 40);
            Assert.Equal(sea + 3, top); // the islet rise, over the gas instead of over water
            Assert.Equal("steel_floor", _content.BlockById(server.World.GetBlock(new Vector3i(0, top, 0)))?.Key);
            for (int dy = 1; dy <= 3; dy++)
            {
                Assert.Equal("metal_panel", _content.BlockById(server.World.GetBlock(new Vector3i(0, top - dy, 0)))?.Key);
            }

            Assert.Equal("gas", _content.BlockById(server.World.GetBlock(new Vector3i(0, top - 4, 0)))?.Key); // a platform, not a mound
            // #2134: under the deck the gas reaches down to the dense gas, like everywhere on the sea — never rock.
            int denseTop = sea - BlocksBeyondTheStars.WorldGeneration.WorldGenerator.GasSeaDepth;
            Assert.Equal("gas", _content.BlockById(server.World.GetBlock(new Vector3i(0, denseTop + 1, 0)))?.Key);
            Assert.Equal("gas_dense", _content.BlockById(server.World.GetBlock(new Vector3i(0, denseTop, 0)))?.Key);
            Assert.Equal("gas_dense", _content.BlockById(server.World.GetBlock(new Vector3i(0, sea - 40, 0)))?.Key);
        }
    }

    [Fact]
    public void TheGas_Kills_FasterThanLava_WithItsOwnDeathLine_AndNoArmourHelps()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            TestGear.Wear(p.State, "armor_chest"); // worn armour mitigates lava — not the gas
            p.State.Position = new Vector3f(0.5f, sea - 3, 0.5f); // fell off the deck into the gas
            float before = p.State.Health;
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.5);
            }

            float lost = before - p.State.Health;
            Assert.True(lost >= 50f, $"only {lost} health lost in two seconds of gas"); // 30/s, unmitigated
        }
    }

    [Fact]
    public void TheDenseGas_UnderTheSea_BurnsFasterThanTheGas()
    {
        // #2134: whoever sinks through the gas into the dense gas under it is gone faster still — no armour helps.
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            int deep = sea - BlocksBeyondTheStars.WorldGeneration.WorldGenerator.GasSeaDepth - 6;
            Assert.Equal("gas_dense", _content.BlockById(server.World.GetBlock(new Vector3i(0, deep, 0)))?.Key);

            float LostInOneSecond(string name, float y)
            {
                var p = server.AddLocalPlayer(name);
                p.State.AboardShip = false;
                TestGear.Wear(p.State, "armor_chest");
                p.State.Position = new Vector3f(0.5f, y, 0.5f);
                float before = p.State.Health;
                server.TickForTest(0.5);
                server.TickForTest(0.5);
                float lost = before - p.State.Health;
                p.State.Position = new Vector3f(0.5f, sea + 10, 0.5f); // out again before the next probe ticks
                return lost;
            }

            float gas = LostInOneSecond("Floater", sea - 3);
            float dense = LostInOneSecond("Sinker", deep);
            Assert.True(gas >= 25f, $"only {gas} health lost in a second of gas");
            Assert.True(dense >= 40f, $"only {dense} health lost in a second of dense gas");
            Assert.True(dense > gas + 10f, $"the dense gas ({dense}/s) must burn faster than the gas ({gas}/s)");
        }
    }

    [Fact]
    public void ASkyCity_HoldsAPocketOfAir_TheDeckDoesNot()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            int sea = server.SeaLevelForTest();
            Assert.False(server.InSkyCityAirForTest(new Vector3f(0.5f, sea + 4, 0.5f)), "the landing deck is open to the storm");

            var cities = server.SettlementBoxesForTest.Where(s => !s.Ruined).ToList();
            var onIsland = server.SettlementsForTest.Where(s => !s.Ruined && s.OnIsland).ToList();
            Assert.Equal(server.SettlementsForTest.Count, server.SettlementsForTest.Count(s => s.OnIsland)); // nothing ever seats on the gas
            if (cities.Count > 0 && onIsland.Count > 0)
            {
                var c = cities[0];
                var inside = new Vector3f((c.Min.X + c.Max.X) * 0.5f, c.GroundY + 1.5f, (c.Min.Z + c.Max.Z) * 0.5f);
                Assert.True(server.InSkyCityAirForTest(inside), "a sky city breathes");
                Assert.False(server.InSkyCityAirForTest(new Vector3f(inside.X, c.GroundY + 40f, inside.Z)), "…but not the whole sky over it");
            }
        }
    }

    [Fact]
    public void TheSkyGiant_IsHosted_DriftsOverTheIslands_Calls_AndCanBeHit_AndNoOtherWorldHasOne()
    {
        var server = Started(Key, out var repo);
        using (repo)
        {
            var sp = server.SkyGiantSpeciesForTest();
            Assert.NotNull(sp);
            Assert.Equal(CreatureBodyPlan.SkyGiant, sp!.BodyPlan);
            Assert.Null(server.GiantSpeciesForTest().Sandworm);
            Assert.Null(server.LeviathanSpeciesForTest().Leviathan);

            int sea = server.SeaLevelForTest();
            var p = server.AddLocalPlayer("Justus");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, sea + 4, 0.5f); // on the deck
            Assert.Empty(server.GiantsForTest());
            for (int i = 0; i < 60; i++)
            {
                server.TickForTest(0.1);
            }

            var g = Assert.Single(server.GiantsForTest(), x => x.Kind == CreatureBodyPlan.SkyGiant);
            Assert.Equal("drift", g.Phase);
            Assert.True(g.Position.Y >= sea + GiantRules.SkyGiantAltitudeMin - 4f, $"the sky giant hangs at {g.Position.Y} over a sea at {sea}");
            double dist = Math.Sqrt(WorldConstants.WrapDistanceSquared(g.Position, p.State.Position, server.World.Circumference));
            Assert.InRange(dist, 30.0, 340.0); // a lane 120–200 out, 70–130 across

            var first = g.Position;
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.25);
            }

            var later = server.GiantsForTest().Single(x => x.Id == g.Id);
            Assert.True(Math.Sqrt(WorldConstants.WrapDistanceSquared(first, later.Position, server.World.Circumference)) > 5.0, "it does not drift");
            var lane = server.SkyGiantLaneForTest(g.Id);
            Assert.NotNull(lane);
            Assert.True(lane!.Value.Trail > 1, "the body leaves no trail to be hit along");
            var hit = server.GiantHitForTest(g.Id, p.State.Position);
            Assert.True(hit.Hittable, "a sky giant is always in the open");
            Assert.True(hit.AimPoint.Y > sea + 10f, "the aim point lies on the body up in the sky");
        }

        var ocean = Started("ocean", out var repo2);
        using (repo2)
        {
            Assert.Null(ocean.SkyGiantSpeciesForTest());
        }
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
