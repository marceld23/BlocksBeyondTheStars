// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Landable asteroids: a special body type — crystalline surface, no life, airless (drains
/// oxygen) and a permanent space sky. Excluded from the random universe planet pool.
/// </summary>
public sealed class LandableAsteroidTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LandableAsteroidTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ast_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void Asteroid_IsAirless_NonSelectable_SpaceSky()
    {
        var ast = _content.GetPlanet("asteroid")!;
        Assert.Equal("none", ast.Atmosphere);
        Assert.True(ast.SpaceSky);
        Assert.False(ast.Selectable);
        Assert.Equal("none", ast.CreatureAbundance);
        Assert.Equal(0.0, ast.FloraDensity);
    }

    [Fact]
    public void Asteroid_Worldgen_HasCrystalSurface_AndNoFlora()
    {
        var ast = _content.GetPlanet("asteroid")!;
        var gen = new WorldGenerator(2026, _content);
        ushort crystal = _content.GetBlock("crystal")!.NumericId.Value;
        ushort floraPlant = _content.GetBlock("flora_plant")!.NumericId.Value;
        ushort floraCrystal = _content.GetBlock("flora_crystal")!.NumericId.Value;

        int crystalSurface = 0;
        for (int x = 0; x < 24; x++)
            for (int z = 0; z < 24; z++)
            {
                int y = gen.SurfaceHeight(ast, x, z);
                var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
                var origin = WorldConstants.ChunkOrigin(coord);
                var chunk = gen.Generate(ast, coord);
                if (chunk.Get(x - origin.X, y - origin.Y, z - origin.Z).Value == crystal) crystalSurface++;

                int ay = y + 1;
                if (ay - origin.Y is >= 0 and < WorldConstants.ChunkSize)
                {
                    ushort above = chunk.Get(x - origin.X, ay - origin.Y, z - origin.Z).Value;
                    Assert.NotEqual(floraPlant, above);   // no flora on a barren asteroid
                    Assert.NotEqual(floraCrystal, above);
                }
            }

        Assert.True(crystalSurface > 0, "Expected a crystalline asteroid surface.");
    }

    [Fact]
    public void Asteroid_HasNoCreatures()
    {
        var ast = _content.GetPlanet("asteroid")!;
        Assert.Empty(CreatureGenerator.GenerateRoster(ast, 2026));
    }

    [Fact]
    public void Asteroid_NotInRandomUniversePool()
    {
        // A default galaxy (no frequency overrides) must never place an asteroid as a system planet.
        var galaxy = new UniverseGenerator(123, new WorldDescription(), _content).Generate();
        var planetTypes = galaxy.AllBodies()
            .Where(b => b.Kind == CelestialKind.Planet)
            .Select(b => b.PlanetType)
            .ToHashSet();

        Assert.DoesNotContain("asteroid", planetTypes);
    }

    [Fact]
    public void Universe_GeneratesLargeLandableAsteroidBodies_PerSystem()
    {
        // Item 24: a few large, landable asteroid *bodies* per system — walkable "asteroid" worlds you can land
        // on (ship or EVA), distinct from the small mineable rocks that spawn as space entities.
        var galaxy = new UniverseGenerator(123, new WorldDescription(), _content).Generate();
        var asteroids = galaxy.AllBodies().Where(b => b.Kind == CelestialKind.AsteroidField).ToList();
        Assert.NotEmpty(asteroids);

        foreach (var a in asteroids)
        {
            Assert.Equal("asteroid", a.PlanetType); // → travel/land loads the walkable asteroid world
            // Asteroid size class → a defined small walkable size AND EvaLandingAllowed permits it (EVA can land).
            Assert.Equal(WorldConstants.WorldSizeClass.Asteroid,
                WorldConstants.SizeClassFor(a.Kind, a.PlanetType ?? string.Empty));
        }

        // Each system carries a small handful (2–3) of them.
        foreach (var bySystem in asteroids.GroupBy(a => a.SystemId))
        {
            Assert.InRange(bySystem.Count(), 2, 3);
        }
    }

    [Fact]
    public void Asteroid_DrainsOxygen_AndReportsSpaceSky()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "ast"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "ast",
            Seed = 7,
            StartPlanet = "asteroid",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        Assert.True(server.SpaceSky);
        Assert.False(server.AtmosphereBreathable);

        var p = server.AddLocalPlayer("Miner");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 64, 0);
        p.State.Oxygen = 50f;

        server.Tick(2.0);
        Assert.True(p.State.Oxygen < 50f, "Airless asteroid should drain oxygen on the surface.");
    }

    [Fact]
    public void WalkableAsteroid_BlockEdits_Persist_AcrossServerRestart()
    {
        // Item 20 durable save (regression): a player edit (mined-out block) on a walkable, landable
        // "asteroid" BODY survives a server restart. These bodies load as a standard ServerWorld keyed by
        // body id, so mine/place already route through ServerWorld.SetBlock -> _repo.SetBlock (per-cell
        // deltas, the same path planets/moons use). This locks that durability in for the asteroid class.
        // The walkable circumference is body-sized (small for an asteroid), so probe the SERVER's actual world
        // for a solid surface cell rather than a standalone generator (which would use a different size).
        Vector3i pos = default;
        {
            using var repo1 = new SqliteWorldRepository(new SaveGamePaths(_root, "ast_persist"));
            var st1 = new LoopbackServerTransport(new LoopbackLink());
            var config1 = new ServerConfig
            {
                WorldName = "ast_persist",
                Seed = 11,
                StartPlanet = "asteroid",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
            };
            var s1 = new SvGameServer(config1, _content, st1, repo1);
            s1.Start();

            var miner = s1.AddLocalPlayer("Miner");
            miner.State.AboardShip = false;

            // Find the topmost solid cell in a column (scan down from well above any terrain).
            int wx = 6, wz = 6;
            for (int y = 160; y > 0; y--)
            {
                if (!s1.World.GetBlock(new Vector3i(wx, y, wz)).IsAir)
                {
                    pos = new Vector3i(wx, y, wz);
                    break;
                }
            }

            Assert.True(pos.Y > 0, "the asteroid column should have a solid surface cell");

            miner.State.Position = new Vector3f(wx + 0.5f, pos.Y + 1f, wz + 0.5f); // stand on the cell, within reach
            miner.State.SelectedHotbarSlot = 0;
            miner.State.Inventory.SetSlot(0, new Shared.State.ItemStack("titanium_drill", 1)); // strong enough for crystal

            s1.MineBlock("Miner", pos.X, pos.Y, pos.Z);
            Assert.True(s1.World.GetBlock(pos).IsAir, "mining should clear the surface cell");
            repo1.Flush();
        }

        // Reopen the same world → the mined-out cell is still air (edit restored from the per-cell delta).
        using var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "ast_persist"));
        var st2 = new LoopbackServerTransport(new LoopbackLink());
        var config2 = new ServerConfig
        {
            WorldName = "ast_persist",
            Seed = 11,
            StartPlanet = "asteroid",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var s2 = new SvGameServer(config2, _content, st2, repo2);
        s2.Start();
        s2.AddLocalPlayer("Miner");
        Assert.True(s2.World.GetBlock(pos).IsAir, "the mined-out asteroid cell must survive a server restart");
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
