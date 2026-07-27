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
/// Landable asteroids: special body types — no life, airless (drains oxygen) and a permanent space sky,
/// excluded from the random universe planet pool. Since #515 there are several FAMILIES (stony, metallic,
/// icy, carbonaceous, crystalline), one rolled per asteroid body, so a system's rocks differ in surface,
/// temperature and what they are worth mining.
/// </summary>
public sealed class LandableAsteroidTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    /// <summary>Every asteroid family in the shipped content (the generator draws from exactly these).</summary>
    private string[] Families => _content.Planets.Keys
        .Where(k => WorldConstants.IsAsteroidType(k))
        .OrderBy(k => k, StringComparer.Ordinal)
        .ToArray();

    public LandableAsteroidTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_ast_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void EveryAsteroidFamily_IsAirless_NonSelectable_SpaceSky()
    {
        Assert.True(Families.Length >= 2, "the point of #515 is that there is more than one family");
        foreach (var key in Families)
        {
            var ast = _content.GetPlanet(key)!;
            Assert.Equal("none", ast.Atmosphere);
            Assert.True(ast.SpaceSky, key);
            Assert.False(ast.Selectable, key);
            Assert.Equal("none", ast.CreatureAbundance);
            Assert.Equal(0.0, ast.FloraDensity);
            Assert.True(ast.Cratered, key);
            Assert.True(ast.SpawnWeight > 0, $"{key} needs a draw weight or it can never appear");
        }
    }

    [Fact]
    public void AsteroidFamilies_HaveDistinctSurfaces_AndNoFlora()
    {
        // The bug behind #515: every asteroid was the same crystal rock. Each family must now put its OWN
        // block on top — and none of them grows anything.
        var gen = new WorldGenerator(2026, _content);
        ushort floraPlant = _content.GetBlock("flora_plant")!.NumericId.Value;
        ushort floraCrystal = _content.GetBlock("flora_crystal")!.NumericId.Value;

        var surfaces = new Dictionary<string, HashSet<ushort>>();
        foreach (var key in Families)
        {
            var ast = _content.GetPlanet(key)!;
            var seen = new HashSet<ushort>();
            // Sample WIDE rather than dense: biome regions are far larger than a chunk, so a tight 24×24
            // window can sit entirely inside a single one and see only that biome's block.
            for (int x = 0; x < 192; x += 12)
                for (int z = 0; z < 192; z += 12)
                {
                    int y = gen.SurfaceHeight(ast, x, z);
                    var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
                    var origin = WorldConstants.ChunkOrigin(coord);
                    var chunk = gen.Generate(ast, coord);
                    seen.Add(chunk.Get(x - origin.X, y - origin.Y, z - origin.Z).Value);

                    int ay = y + 1;
                    if (ay - origin.Y is >= 0 and < WorldConstants.ChunkSize)
                    {
                        ushort above = chunk.Get(x - origin.X, ay - origin.Y, z - origin.Z).Value;
                        Assert.NotEqual(floraPlant, above);   // no flora on a barren asteroid
                        Assert.NotEqual(floraCrystal, above);
                    }
                }

            Assert.NotEmpty(seen);
            surfaces[key] = seen;
        }

        // Crystal is now the RARE family's signature, not what every rock is made of.
        ushort crystal = _content.GetBlock("crystal")!.NumericId.Value;
        Assert.Contains(crystal, surfaces["asteroid_crystal"]);
        Assert.DoesNotContain(crystal, surfaces["asteroid"]);

        // …and each family shows its own material. Which of a family's biomes a given body uses is seeded,
        // so assert against the family's declared biome pool rather than one specific block — plus the rare
        // metals that item 33 deliberately exposes on deep crater floors, which are also a legal surface.
        var craterMetals = new[] { "titanium_ore", "gold_ore", "platinum_ore", "cobalt_ore", "uranium_ore", "tungsten_ore", "neodymium_ore" }
            .Select(k => _content.GetBlock(k)!.NumericId.Value)
            .ToHashSet();
        foreach (var key in Families)
        {
            var allowed = _content.GetPlanet(key)!.Biomes
                .Select(b => _content.GetBlock(b.SurfaceBlock)!.NumericId.Value)
                .ToHashSet();
            Assert.NotEmpty(allowed);
            allowed.UnionWith(craterMetals);
            Assert.All(surfaces[key], s => Assert.Contains(s, allowed));
        }

        // The icy rock is frozen over — ice or its snow cover, never bare crystal or carbon.
        var icy = surfaces["asteroid_icy"];
        Assert.Contains(icy, s => s == _content.GetBlock("ice")!.NumericId.Value
                                  || s == _content.GetBlock("snow")!.NumericId.Value);
    }

    [Fact]
    public void Asteroid_HasNoCreatures()
    {
        foreach (var key in Families)
        {
            Assert.Empty(CreatureGenerator.GenerateRoster(_content.GetPlanet(key)!, 2026));
        }
    }

    [Fact]
    public void Asteroid_NotInRandomUniversePool()
    {
        // A default galaxy (no frequency overrides) must never place ANY asteroid family as a system planet.
        var galaxy = new UniverseGenerator(123, new WorldDescription(), _content).Generate();
        var planetTypes = galaxy.AllBodies()
            .Where(b => b.Kind == CelestialKind.Planet)
            .Select(b => b.PlanetType)
            .ToHashSet();

        Assert.DoesNotContain(planetTypes, t => WorldConstants.IsAsteroidType(t));
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
            // → travel/land loads a walkable asteroid world of that family…
            Assert.Contains(a.PlanetType, Families);
            // …and every family sizes as an asteroid. A family the size lookup did not recognise would wrap
            // its coordinates at a planet's circumference — the old "cannot mine any block" bug.
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
    public void AsteroidFamilies_VaryAcrossTheGalaxy_AndAreDeterministic()
    {
        // #515: the whole point — a galaxy's rocks are not all the same type, the common stony one dominates,
        // and the same seed always produces the same rock in the same place.
        var desc = new WorldDescription { StarSystemCount = 40 };
        var galaxy = new UniverseGenerator(4711, desc, _content).Generate();
        var types = galaxy.AllBodies()
            .Where(b => b.Kind == CelestialKind.AsteroidField)
            .Select(b => b.PlanetType!)
            .ToList();

        Assert.True(types.Count > 60, "expected plenty of asteroids across 40 systems");
        Assert.True(types.Distinct().Count() >= 4, "a galaxy should show several asteroid families");

        int stony = types.Count(t => t == "asteroid");
        int crystalline = types.Count(t => t == "asteroid_crystal");
        Assert.True(stony > crystalline, "the common stony rock must outnumber the rare crystal one");

        var again = new UniverseGenerator(4711, desc, _content).Generate()
            .AllBodies().Where(b => b.Kind == CelestialKind.AsteroidField).Select(b => b.PlanetType!).ToList();
        Assert.Equal(types, again);
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
