// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain from a planet map (#3978): the <c>.bbmap</c> format, the lazy map loading, and the map base form — the same
/// ground on every generator, no step at either torus seam, the fixed sea, biomes that follow the map, no giant
/// landforms. Built on a small synthetic map (never the real Earth file) and planet types made here.
/// </summary>
public sealed class PlanetMapTerrainTests
{
    private const int MapW = 128, MapH = 64;
    private const int Circumference = 4096; // 32 blocks per cell; the latitude period is 2048
    private const long Seed = 20261123;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] BiomeBlocks = { "ice", "snow", "mud", "grass", "dirt", "sand", "grass", "stone" };
    private static readonly string[] GiantRows =
    {
        "volcano", "caldera", "massif", "table-mountain", "rift", "mega-rift", "shield-volcano", "impact-basin", "glacial-trough",
        "pillar-island", "arch-cluster", "mesa-cluster", "wave-rock", "fossil-ridge", "giant-monitor",
    };

    /// <summary>The synthetic world: an ocean at −4,000 m, one continent (cells 32–95 × 8–55) at 200 m with an 8,848 m peak
    /// in its middle, the south edge row a 2,500 m plateau and the north edge row deep sea (so the seam blend has a
    /// cliff to remove), and the biome class = column / 16 — eight vertical bands.</summary>
    private static PlanetMap SyntheticMap()
    {
        var elevation = new short[MapW * MapH];
        var biome = new byte[MapW * MapH];
        for (int row = 0; row < MapH; row++)
            for (int col = 0; col < MapW; col++)
            {
                int e = -4000;
                if (col >= 32 && col < 96 && row >= 8 && row < 56)
                {
                    int d = Math.Max(Math.Abs(col - 64), Math.Abs(row - 32));
                    e = Math.Max(200, 8848 - d * 600);
                }

                if (row == 0)
                {
                    e = 2500;
                }

                elevation[row * MapW + col] = (short)e;
                biome[row * MapW + col] = (byte)(col / 16);
            }

        return new PlanetMap(MapW, MapH, elevation, biome);
    }

    private static PlanetMap RegisteredMap(string key) => Content.RegisterPlanetMap(key, PlanetMapCodec.Encode(SyntheticMap()));

    /// <summary>A map planet type of its own key (the generator's caches key on it), with eight biomes in class order.</summary>
    private static PlanetType MapPlanet(string key, double? waterAbundance = null, string terrainMap = "")
    {
        if (terrainMap.Length == 0)
        {
            RegisteredMap(key);
            terrainMap = key;
        }

        return new PlanetType
        {
            Key = key,
            TerrainMap = terrainMap,
            Atmosphere = "breathable",
            WaterAbundance = waterAbundance,
            SurfaceBlock = "grass",
            SubSurfaceBlock = "dirt",
            Biomes = BiomeBlocks.Select(b => new Biome { SurfaceBlock = b, SubSurfaceBlock = "dirt" }).ToList(),
        };
    }

    private static WorldGenerator Generator(int generation = WorldDescription.CurrentTerrainGeneration)
    {
        var gen = new WorldGenerator(Seed, Content);
        gen.SetTerrainGeneration(generation);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetContinentsEnabled(true);
        gen.SetWorldMode(Circumference, cratered: false, landingPads: null, locationId: "sol-map-test");
        return gen;
    }

    [Fact]
    public void TheFormat_RoundTrips_AndRefusesADamagedFile()
    {
        var map = SyntheticMap();
        byte[] bytes = PlanetMapCodec.Encode(map);
        var back = PlanetMapCodec.Decode(bytes);
        Assert.Equal((MapW, MapH, map.ContentHash), (back.Width, back.Height, back.ContentHash));
        Assert.Equal(map.ElevationAt(64, 32), back.ElevationAt(64, 32));
        Assert.Equal(map.BiomeAt(127, 63), back.BiomeAt(127, 63));
        Assert.Equal("BBMP", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        var damaged = (byte[])bytes.Clone();
        damaged[16] ^= 0xFF; // the content hash
        Assert.Throws<InvalidDataException>(() => PlanetMapCodec.Decode(damaged));
        var futureVersion = (byte[])bytes.Clone();
        futureVersion[4] = 9;
        Assert.Throws<InvalidDataException>(() => PlanetMapCodec.Decode(futureVersion));
        Assert.Throws<InvalidDataException>(() => PlanetMapCodec.Decode(bytes.Take(bytes.Length - 8).ToArray()));
    }

    [Fact]
    public void TheContent_ReadsAMap_OnFirstUse_AndKeepsIt()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        int reads = 0;
        byte[] bytes = PlanetMapCodec.Encode(SyntheticMap());
        content.SetPlanetMapSource(key =>
        {
            reads++;
            return key == "lazy_map" ? bytes : null;
        });

        Assert.Equal(0, reads); // setting the source reads nothing
        Assert.NotNull(content.GetPlanetMap("lazy_map"));
        Assert.Same(content.GetPlanetMap("lazy_map"), content.GetPlanetMap("lazy_map"));
        Assert.Equal(1, reads);
        Assert.Null(content.GetPlanetMap("no_such_map"));
        Assert.Null(content.GetPlanetMap("../escape")); // a key never becomes a path
    }

    [Fact]
    public void TheHeightCurve_PutsEverestJustUnderTheCap_AndTheCoastOnZeroMetres()
    {
        var p = new PlanetType();
        int everest = p.MapSeaLevelY + MapTerrain.HeightBlocks(8848L * MapTerrain.FixedOne, p);
        Assert.InRange(everest, 270, 285); // the cap is 288; the detail adds at most a few blocks
        Assert.Equal(0, MapTerrain.HeightBlocks(0, p));
        Assert.Equal(0, MapTerrain.HeightBlocks(19L * MapTerrain.FixedOne, p)); // low land sits on the waterline, dry
        Assert.Equal(-1, MapTerrain.HeightBlocks(-1, p));                       // anything below 0 m is under water
        Assert.Equal(-p.MapOceanMaxDepth, MapTerrain.HeightBlocks(-11000L * MapTerrain.FixedOne, p));
        Assert.InRange(MapTerrain.HeightBlocks(-4000L * MapTerrain.FixedOne, p), -40, -30);
    }

    [Fact]
    public void ServerAndClient_BuildTheSameGround()
    {
        var planet = MapPlanet("map_test_same");
        var server = Generator();
        var client = Generator();
        for (int x = -300; x < Circumference + 300; x += 37)
            for (int z = -1100; z < 1100; z += 41)
            {
                Assert.Equal(server.NaturalSurfaceHeight(planet, x, z), client.NaturalSurfaceHeight(planet, x, z));
            }

        foreach (var (x, z) in new[] { (2048, 0), (1100, 300), (40, -1000) })
        {
            var coord = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(server.SurfaceHeight(planet, x, z)),
                WorldConstants.WorldToChunk(z));
            Assert.Equal(WorldGenerationGoldenTests.HashChunk(server.Generate(planet, coord)),
                WorldGenerationGoldenTests.HashChunk(client.Generate(planet, coord)));
        }
    }

    [Fact]
    public void TheGround_FollowsTheMap_AndJoinsAtBothSeams()
    {
        var planet = MapPlanet("map_test_seams");
        var gen = Generator();
        int period = WorldConstants.LatitudePeriodFor(Circumference);

        int Raw(int x, int z) => gen.RawSurfaceHeightForTest(planet, x, z); // the base form alone, before the small landforms

        // The peak cell (64, 32) — its centre is world (2064, 16) — is the map's 8,848 m; the open ocean (−4,000 m) is sea
        // floor; the continent's 200 m plain is just dry.
        Assert.InRange(Raw(64 * 32 + 16, 16), 270, 288);
        Assert.InRange(Raw(10 * 32, 200), planet.MapSeaLevelY - 40, planet.MapSeaLevelY - 28);
        Assert.InRange(Raw(36 * 32, 200), planet.MapSeaLevelY + 1, planet.MapSeaLevelY + 20);

        for (int z = -period / 2; z < period / 2; z += 29)
        {
            Assert.Equal(Raw(5, z), Raw(5 + Circumference, z));
            int step = Math.Abs(Raw(Circumference - 1, z) - Raw(0, z));
            Assert.True(step <= 4, $"a {step}-block step across X = 0 at z {z}");
        }

        for (int x = 0; x < Circumference; x += 31)
        {
            Assert.Equal(Raw(x, 7), Raw(x, 7 + period));
            int step = Math.Abs(Raw(x, period / 2 - 1) - Raw(x, -period / 2));
            Assert.True(step <= 4, $"a {step}-block step across the Z seam at x {x}");
        }
    }

    [Fact]
    public void TheSea_IsTheFixedLevel_AndADryMapWorldHasNone()
    {
        var gen = Generator();
        var wet = MapPlanet("map_test_wet");
        Assert.Equal(wet.MapSeaLevelY, gen.SeaLevel(wet));
        var dry = MapPlanet("map_test_dry", waterAbundance: 0.0);
        Assert.Equal(int.MinValue, gen.SeaLevel(dry));
    }

    [Fact]
    public void TheBiomes_FollowTheMapClasses_WithIceAlongTheSeam()
    {
        var planet = MapPlanet("map_test_biomes");
        var gen = Generator();
        Assert.Equal(PlanetMapBiome.Count, gen.BiomeCount(planet)); // the whole list, in order
        for (int band = 0; band < PlanetMapBiome.Count; band++)
        {
            int x = (band * 16 + 8) * 32; // the middle of the band, 256 blocks from either border
            foreach (int z in new[] { -500, 0, 500 })
            {
                Assert.Equal(band, gen.BiomeIndexAt(planet, x, z));
            }
        }

        // Along the Z seam the bands turn to ice (the blend covers the outermost row of this small map; the jitter lets a
        // border wander a few cells, so it is a share, not every column) — and nowhere in the middle of the map.
        int period = WorldConstants.LatitudePeriodFor(Circumference);
        int seamIce = 0, middleIce = 0, samples = 0;
        for (int x = 20 * 32; x < Circumference - 4 * 32; x += 23) // bands 1–7, clear of band 0: none is ice on the map
        {
            samples++;
            seamIce += gen.BiomeIndexAt(planet, x, -period / 2) == PlanetMapBiome.Ice ? 1 : 0;
            middleIce += gen.BiomeIndexAt(planet, x, 0) == PlanetMapBiome.Ice ? 1 : 0;
        }

        Assert.True(seamIce * 100 >= samples * 40, $"only {seamIce} of {samples} seam columns are ice");
        Assert.Equal(0, middleIce);
    }

    [Fact]
    public void GiantLandforms_AreOff_OnAMapWorld()
    {
        var map = MapPlanet("map_test_giants");
        var rolled = MapPlanet("map_test_giants_rolled", terrainMap: "map_test_giants");
        rolled.TerrainMap = string.Empty;
        var gen = Generator();
        Assert.Contains("massif", gen.LandmarkOrderForTest(rolled)); // the same type without its map would carry them
        Assert.Empty(gen.LandmarkOrderForTest(map).Intersect(GiantRows));
    }

    [Fact]
    public void AMissingMap_IsAnError_NeverASilentRolledWorld()
    {
        var planet = MapPlanet("map_test_missing", terrainMap: "map_nobody_has");
        Assert.Throws<InvalidOperationException>(() => Generator().NaturalSurfaceHeight(planet, 0, 0));
    }
}
