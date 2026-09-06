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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1649 (landscape variety 6/6): the eight data-only planet types compose into worlds players can pick, the
/// six new monument silhouettes build, and a rugged generation-1 world still places every structure it rolled.
/// </summary>
public sealed class LandscapeWorldsTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] NewTypes = { "red_desert", "boreal", "archipelago", "glacier", "meadowlands", "ashen_ocean", "dust_bowl", "frozen_ocean" };
    private static readonly string[] NewMonuments = { "bridge", "watchtower", "tomb", "ziggurat", "colossus", "aqueduct" };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_landscape6_" + Guid.NewGuid().ToString("N"));

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
            // temp dir cleanup is best-effort
        }
    }

    // ---------- content cross-check for every type ----------

    [Fact]
    public void EveryPlanetType_ResolvesItsBlocks_Ores_Locales_Theme_Styles_AndTags()
    {
        var known = WorldGenerator.KnownTerrainStyles.ToHashSet();
        var en = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        var de = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "de.json"));
        Assert.True(Content.Planets.Count >= 36, $"expected 36+ planet types, found {Content.Planets.Count}");
        foreach (var p in Content.Planets.Values)
        {
            Assert.NotNull(Content.GetBlock(p.SurfaceBlock));
            Assert.NotNull(Content.GetBlock(p.SubSurfaceBlock));
            Assert.NotNull(Content.GetBlock(p.DeepBlock));
            foreach (var b in p.Biomes)
            {
                Assert.NotNull(Content.GetBlock(b.SurfaceBlock));
                Assert.NotNull(Content.GetBlock(b.SubSurfaceBlock));
                Assert.InRange(b.ReliefMul, 0.1, 3.0);
            }

            foreach (var ore in p.Ores)
            {
                Assert.NotNull(Content.GetBlock(ore.Block));
            }

            if (!string.IsNullOrEmpty(p.TerrainStyle))
            {
                Assert.Contains(p.TerrainStyle.ToLowerInvariant(), known);
            }

            foreach (var s in p.TerrainStyles)
            {
                Assert.Contains(s.ToLowerInvariant(), known);
            }

            TerrainTags.Parse(p.TerrainTags, out var unknown);
            Assert.Null(unknown);
            Assert.NotEqual(string.Empty, FloraThemes.Resolve(p.FloraTheme).Name);
            if (p.Selectable && !p.Void)
            {
                Assert.Contains($"\"{p.NameKey}\"", en);
                Assert.Contains($"\"{p.NameKey}\"", de);
                Assert.Contains($"\"planet.{p.Key}.desc\"", en);
            }
        }

        foreach (var key in NewTypes)
        {
            var p = Content.GetPlanet(key);
            Assert.NotNull(p);
            Assert.True(p!.Selectable);
            Assert.InRange(p.SpawnWeight, 3, 8); // modest: the classic types keep dominating
            Assert.True(p.TerrainStyles.Count >= 2, $"{key} has no style pool");
            Assert.Equal(1, p.MinTerrainGeneration); // generation-1 galaxies only
        }
    }

    [Fact]
    public void NewTypes_EnterOnlyGenerationOneGalaxies()
    {
        // A classic-generation description rolls the classic planet mix byte for byte (the galaxy-layout
        // regression test pins that); a generation-1 description rolls the new types somewhere.
        var classic = new WorldDescription { StarSystemCount = 12 };
        var gen1 = new WorldDescription { StarSystemCount = 12, TerrainGeneration = 1 };
        int newOnGen1 = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            foreach (var body in new UniverseGenerator(seed, classic, Content).Generate().Systems.SelectMany(s => s.Bodies))
            {
                Assert.DoesNotContain(body.PlanetType, NewTypes);
            }

            newOnGen1 += new UniverseGenerator(seed, gen1, Content).Generate().Systems.SelectMany(s => s.Bodies).Count(b => NewTypes.Contains(b.PlanetType));
        }

        Assert.True(newOnGen1 > 0, "no generation-1 galaxy rolled a new planet type in 40 seeds");
    }

    [Fact]
    public void NewTypes_KeepTheirNamesFlavoured_AndGenerateChunks()
    {
        foreach (var key in NewTypes)
        {
            var planet = Content.Planets[key];
            var gen = new WorldGenerator(77, Content);
            gen.SetLavaCoreVolcanoes(true);
            gen.SetTerrainGeneration(1);
            int h = gen.SurfaceHeight(planet, 100, 37);
            Assert.InRange(h, -400, 288);
            var chunk = gen.Generate(planet, new ChunkCoord(3, WorldConstants.WorldToChunk(h), 1));
            Assert.NotNull(chunk);
            string name = NameGenerator.PlanetProper(new DeterministicRandom(12345 + key.Length), key);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    [Fact]
    public void AshenOcean_RollsBasaltContinentsInALavaSea_OnALargeWorld()
    {
        var planet = Content.Planets["ashen_ocean"];
        var lava = Content.GetBlock("lava")!.NumericId;
        bool sawLavaSea = false, sawLand = false;
        for (long s = 1; s <= 12 && !(sawLavaSea && sawLand); s++)
        {
            var gen = new WorldGenerator(s * 6151 + 3, Content);
            gen.SetWorldMode(6400, false, null, "ashen-ocean-test");
            gen.SetContinentsEnabled(true);
            gen.SetLavaCoreVolcanoes(true);
            gen.SetTerrainGeneration(1);
            int sea = gen.SeaLevel(planet);
            if (sea == int.MinValue)
            {
                continue;
            }

            int circ = 6400;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2; z += 97)
                for (int x = 0; x < circ; x += 131)
                {
                    int h = gen.SurfaceHeight(planet, x, z);
                    if (h + 1 <= sea && gen.TryGetLavaSurface(planet, x, z, out _, out _))
                    {
                        sawLavaSea = true;
                    }
                    else if (h > sea + 4)
                    {
                        sawLand = true;
                    }
                }
        }

        Assert.True(sawLavaSea && sawLand, $"lava sea {sawLavaSea}, land {sawLand}");
    }

    // ---------- monuments ----------

    [Fact]
    public void NewMonumentArchetypes_Build_WithinTheirCanvas_AndCarryRunes()
    {
        var rune = Content.GetBlock("rune_stone")!.NumericId.Value;
        var brick = Content.GetBlock("ancient_brick")!.NumericId.Value;
        foreach (var archetype in NewMonuments)
        {
            for (long seed = 1; seed <= 6; seed++)
            {
                var s = MonumentGenerator.Generate(archetype, seed * 7919, "stone", Content, withCache: seed % 2 == 0);
                Assert.Equal("monument:" + archetype, s.Tier);
                Assert.InRange(s.Width, 5, 24);
                Assert.InRange(s.Length, 5, 24);
                Assert.InRange(s.Height, 6, 20);
                int solid = 0, runes = 0;
                for (int x = 0; x < s.Width; x++)
                    for (int y = 0; y < s.Height; y++)
                        for (int z = 0; z < s.Length; z++)
                        {
                            ushort b = s.Get(x, y, z);
                            if (b != 0)
                            {
                                solid++;
                            }

                            if (b == rune)
                            {
                                runes++;
                            }
                        }

                Assert.True(solid >= 20, $"{archetype}/{seed}: only {solid} cells");
                Assert.True(runes >= 1 || brick == rune, $"{archetype}/{seed}: no rune course");
                if (seed % 2 == 0)
                {
                    Assert.Contains(s.Markers, m => m.Type == "relic_cache");
                }
            }
        }

        Assert.Equal(MonumentGenerator.Archetypes, MonumentGenerator.ArchetypesGen1.Take(5).ToArray());
        Assert.Equal(11, MonumentGenerator.ArchetypesGen1.Length);
    }

    // ---------- placement on rugged generation-1 ground ----------

    [Theory]
    [InlineData("highland", 23)]
    [InlineData("red_desert", 31)]
    [InlineData("glacier", 41)]
    public void FreshGenerationOneWorld_PlacesEverythingTheRollsRequested(string planet, long seed)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "g1_" + planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "g1_" + planet,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
        };
        Assert.Equal(WorldDescription.CurrentTerrainGeneration, config.World.TerrainGeneration); // a NEW world = generation 1
        var server = new SvGameServer(config, Content, st, repo);
        server.Start();
        int settlements = 0;
        foreach (var (kind, requested, placed) in server.StampReportForTest)
        {
            Assert.True(placed == requested, $"{planet}/{seed}: {kind} placed {placed}/{requested} on the generation-1 relief");
            if (kind == "settlement")
            {
                settlements = placed;
            }
        }

        Assert.True(server.SettlementCount >= 1 || settlements == 0, $"{planet}: settlements requested but none stand");
    }
}
