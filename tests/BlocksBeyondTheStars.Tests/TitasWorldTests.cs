// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Titas (2026-09, Justus' player report): the frozen planet that exists at most once per galaxy — ten blocks of snow
/// over sulfur stone, toxic yellow water under five blocks of ice, volcanic hot zones, leafless dead forests. The
/// type's fields are read on generation-8 worlds only.
/// </summary>
public sealed class TitasWorldTests
{
    private const string Key = "titas";
    private const long Seed = 20260916;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlanetType Titas => Content.GetPlanet(Key)!;

    private static WorldGenerator Generator()
    {
        var gen = new WorldGenerator(Seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(WorldDescription.ExtremePlanetsGeneration);
        return gen;
    }

    /// <summary>Reads generated blocks by world position, one cached chunk per coordinate.</summary>
    private sealed class Probe
    {
        private readonly WorldGenerator _gen;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public Probe(WorldGenerator gen) => _gen = gen;

        public IEnumerable<ChunkData> Chunks => _chunks.Values;

        public BlockId At(int x, int y, int z)
        {
            var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                chunk = _gen.Generate(Titas, coord);
                _chunks[coord] = chunk;
            }

            var origin = WorldConstants.ChunkOrigin(coord);
            return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
        }
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    /// <summary>A grid of sample columns over a stretch of the world.</summary>
    private static IEnumerable<(int X, int Z)> Columns(int step = 23, int span = 1400)
    {
        for (int x = 0; x < span; x += step)
            for (int z = -span / 2; z < span / 2; z += step)
                yield return (x, z);
    }

    // ---------------- the planet type ----------------

    [Fact]
    public void Titas_IsDataComplete_AndGatedToGenerationEight()
    {
        var p = Titas;
        Assert.NotNull(p);
        Assert.True(p.Exotic);
        Assert.Equal(WorldDescription.ExtremePlanetsGeneration, p.MinTerrainGeneration);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= WorldDescription.ExtremePlanetsGeneration);
        Assert.True(p.OncePerGalaxy);
        Assert.Equal("Titas", p.FixedName);
        Assert.Equal("breathable", p.Atmosphere);
        Assert.Equal(-100.0, p.BaseTemperature);
        Assert.Equal(10, p.SnowCoverDepth);
        Assert.Equal(5, p.IceSheetDepth);
        Assert.True(p.DeadForests);
        Assert.Equal(1, p.MaxAquaticSpecies);
        Assert.Equal(2.0, p.WaterDamagePerSecond);
        Assert.Equal(40.0, p.ExposureMinutesCold);
        Assert.Equal(30.0, p.ExposureMinutesHot);
        Assert.True(p.RestrictStructures);
        Assert.Equal(new[] { "net_fragments", "sps_labs" }, p.AllowedStructures.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Single(p.Biomes, b => b.HotZone && b.Temperature == 100.0);
        Assert.InRange(p.SpawnWeight, 1, 2);

        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.DataDir(), "locales"), "*.json"))
        {
            Assert.Contains("\"planet.titas.name\": \"Titas\"", File.ReadAllText(file)); // the name is never translated
        }

        foreach (var code in new[] { "en", "de" })
        {
            string text = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json"));
            Assert.Contains("\"planet.titas.desc\"", text);
            Assert.Contains("\"block.sulfur_stone.name\"", text);
        }

        var sulfur = Content.GetBlock("sulfur_stone");
        Assert.NotNull(sulfur);
        Assert.Contains(sulfur!.Drops, d => d.Item == "stone");
        Assert.Contains(sulfur.RandomDrops!, d => d.Item == "sulfur_ore");
    }

    [Fact]
    public void Titas_AppearsAtMostOncePerGalaxy_NeverInTheStartSystem_AndIsNamedTitas()
    {
        var older = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.ExtremePlanetsGeneration - 1 };
        var current = new WorldDescription { StarSystemCount = 12, TerrainGeneration = WorldDescription.ExtremePlanetsGeneration };
        older.PlanetTypeFrequencies[Key] = Frequency.Frequent;
        current.PlanetTypeFrequencies[Key] = Frequency.Frequent; // deliberately rare — weighted up so 40 seeds are enough
        int galaxiesWithTitas = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            Assert.DoesNotContain(new UniverseGenerator(seed, older, Content).Generate().Systems.SelectMany(s => s.Bodies), b => b.PlanetType == Key);

            var galaxy = new UniverseGenerator(seed, current, Content).Generate();
            UniverseGenerator.ApplyFixedNames(galaxy, Content);
            var found = galaxy.Systems.SelectMany((s, i) => s.Bodies.Select(b => (System: i, Body: b))).Where(e => e.Body.PlanetType == Key).ToList();
            Assert.True(found.Count <= 1, $"seed {seed}: {found.Count} bodies of type titas");
            if (found.Count == 1)
            {
                galaxiesWithTitas++;
                Assert.NotEqual(0, found[0].System);
                Assert.Equal(CelestialKind.Planet, found[0].Body.Kind);
                Assert.Equal("Titas", found[0].Body.Name);
            }
        }

        Assert.True(galaxiesWithTitas > 0, "no generation-8 galaxy rolled Titas in 40 seeds");
    }

    // ---------------- the terrain ----------------

    [Fact]
    public void Titas_DryLand_WearsTenBlocksOfSnowOverSulfurStone_AndNoIceGround()
    {
        var gen = Generator();
        var probe = new Probe(gen);
        var snow = Block("snow");
        var sulfur = Block("sulfur_stone");
        var ice = Block("ice");
        int sea = gen.SeaLevel(Titas);
        int checkedColumns = 0, blanketed = 0;
        var diag = new List<string>();
        foreach (var (x, z) in Columns(step: 61))
        {
            int sy = gen.SurfaceHeight(Titas, x, z);
            if (sy <= sea + 1 || gen.IsHotZoneAt(Titas, x, z) || gen.IsSurfaceWater(Titas, x, z)
                || gen.SurfacePondDepth(Titas, x, z) > 0 || gen.SurfaceRiverDepth(Titas, x, z) > 0 || gen.SurfaceGen1WaterDepth(Titas, x, z) > 0)
            {
                continue;
            }

            checkedColumns++;
            bool ok = probe.At(x, sy, z) == snow;
            for (int d = 0; d < 10 && ok; d++)
            {
                var b = probe.At(x, sy - d, z);
                ok = b == snow || b.IsAir; // a cave may open into the blanket
            }

            var below = probe.At(x, sy - 10, z);
            ok &= below == sulfur || below.IsAir;
            if (ok)
            {
                blanketed++;
            }
            else if (diag.Count < 12)
            {
                diag.Add($"({x},{sy},{z}): " + string.Join(",", Enumerable.Range(0, 12).Select(d => Content.BlockById(probe.At(x, sy - d, z))?.Key ?? "air")));
            }

            Assert.NotEqual(ice, probe.At(x, sy, z));
        }

        Assert.True(checkedColumns >= 40, $"only {checkedColumns} dry cool columns sampled");
        Assert.True(blanketed >= checkedColumns * 0.85, $"{blanketed}/{checkedColumns} dry columns carry the ten-block snow blanket; " + string.Join(" | ", diag));
    }

    [Fact]
    public void Titas_WaterLiesUnderFiveBlocksOfIce_AndCountsAsLand()
    {
        var gen = Generator();
        var probe = new Probe(gen);
        var ice = Block("ice");
        var water = Block("water");
        int sea = gen.SeaLevel(Titas);
        int deepColumns = 0;
        foreach (var (x, z) in Columns(step: 47))
        {
            int sy = gen.SurfaceHeight(Titas, x, z);
            if (sea - sy < 8 || gen.IsHotZoneAt(Titas, x, z))
            {
                continue;
            }

            deepColumns++;
            Assert.Equal(5, gen.SurfaceIceThickness(Titas, x, z));
            Assert.True(gen.TryGetWaterSurface(Titas, x, z, out int liquidTop, out _));
            Assert.Equal(sea - 5, liquidTop);
            Assert.False(gen.IsSurfaceWater(Titas, x, z)); // a five-block sheet is land a ship may set down on
            for (int d = 0; d < 5; d++)
            {
                Assert.Equal(ice, probe.At(x, sea - d, z));
            }

            Assert.Equal(water, probe.At(x, sea - 5, z));
            if (deepColumns >= 12)
            {
                break;
            }
        }

        Assert.True(deepColumns > 0, "no deep sea column sampled");
    }

    [Fact]
    public void Titas_HotZones_CoverAboutFifteenPercent_BareBasalt_AndTheirPondsAreLava()
    {
        var gen = Generator();
        var probe = new Probe(gen);
        var snow = Block("snow");
        var lava = Block("lava");
        int sea = gen.SeaLevel(Titas);
        int total = 0, hot = 0, hotDry = 0, bare = 0;
        foreach (var (x, z) in Columns(step: 31, span: 2400))
        {
            total++;
            if (!gen.IsHotZoneAt(Titas, x, z))
            {
                continue;
            }

            hot++;
            int sy = gen.SurfaceHeight(Titas, x, z);
            if (sy <= sea + 1 || hotDry >= 30 || gen.SurfacePondDepth(Titas, x, z) > 0 || gen.SurfaceRiverDepth(Titas, x, z) > 0)
            {
                continue;
            }

            hotDry++;
            if (probe.At(x, sy, z) != snow)
            {
                bare++;
            }
        }

        double share = hot / (double)total;
        Assert.InRange(share, 0.06, 0.28);
        Assert.True(hotDry > 0 && bare >= hotDry * 0.9, $"{bare}/{hotDry} hot dry columns without snow");

        // A pond inside a hot zone is molten, and every fluid query agrees.
        bool foundPond = false;
        for (int x = 0; x < 6000 && !foundPond; x += 5)
            for (int z = -1500; z < 1500 && !foundPond; z += 5)
            {
                if (!gen.IsHotZoneAt(Titas, x, z))
                {
                    continue;
                }

                int depth = gen.SurfacePondDepth(Titas, x, z);
                if (depth <= 0)
                {
                    continue;
                }

                foundPond = true;
                int sy = gen.SurfaceHeight(Titas, x, z);
                Assert.True(gen.TryGetLavaSurface(Titas, x, z, out int lavaTop, out int bed));
                Assert.Equal(sy, lavaTop);
                Assert.Equal(sy - depth, bed);
                Assert.False(gen.TryGetWaterSurface(Titas, x, z, out _, out _));
                Assert.False(gen.IsSurfaceWater(Titas, x, z));
                Assert.Equal(lava, probe.At(x, sy, z));
            }

        Assert.True(foundPond, "no pond inside a hot zone found");
    }

    [Fact]
    public void Titas_GrowsOnlyLeaflessTrees_AndCapsItsWaterLife()
    {
        var gen = Generator();
        var probe = new Probe(gen);
        int sea = gen.SeaLevel(Titas);
        foreach (var (x, z) in Columns(step: 16, span: 480))
        {
            int sy = gen.SurfaceHeight(Titas, x, z);
            if (sy > sea)
            {
                probe.At(x, sy + 2, z); // the chunk the trees stand in
            }
        }

        var log = Block("wood_log");
        var foliage = new[] { "tree_leaves", "pine_needles", "palm_frond" }.Select(k => Content.GetBlock(k)).Where(b => b != null).Select(b => b!.NumericId).ToHashSet();
        int logs = 0;
        foreach (var chunk in probe.Chunks)
        {
            for (int x = 0; x < WorldConstants.ChunkSize; x++)
                for (int y = 0; y < WorldConstants.ChunkSize; y++)
                    for (int z = 0; z < WorldConstants.ChunkSize; z++)
                    {
                        var b = chunk.Get(x, y, z);
                        Assert.False(foliage.Contains(b), "a leafy tree grows on Titas");
                        if (b == log)
                        {
                            logs++;
                        }
                    }
        }

        Assert.True(logs > 0, "no dead trees stand on the sampled stretch of Titas");

        for (long seed = 1; seed <= 12; seed++)
        {
            var roster = CreatureGenerator.GenerateRoster(Titas, seed, WorldDescription.ExtremePlanetsGeneration);
            Assert.True(roster.Count(s => s.Habitat is CreatureHabitat.Water or CreatureHabitat.Amphibian) <= 1);
        }
    }

    [Fact]
    public void ExtremeFields_AreReadOnGenerationEightOnly()
    {
        // The same type forced onto a generation-7 generator keeps the classic climate: no blanket, no hot zones.
        var gen = new WorldGenerator(Seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(WorldDescription.ExtremePlanetsGeneration - 1);
        Assert.DoesNotContain(Columns(step: 97), c => gen.IsHotZoneAt(Titas, c.X, c.Z));
    }
}
