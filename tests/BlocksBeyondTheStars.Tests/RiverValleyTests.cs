// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 40 (#3557–#3560): the river valleys. The ground eases down toward every surface river, never rises,
/// never sinks into the sea and never below the water of a river beside it; the stage reaches no older world, no preview
/// and no river routing.
/// </summary>
public sealed class RiverValleyTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Gen40 = WorldDescription.GeographyGeneration;

    private readonly ITestOutputHelper _out;

    public RiverValleyTests(ITestOutputHelper output) => _out = output;

    private static WorldGenerator Gen(long seed, int generation, string body = "sys0-p2", int circumference = WorldConstants.Circumference)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        gen.SetWorldMode(circumference, cratered: false, landingPads: null, body);
        return gen;
    }

    /// <summary>The columns beside a field's surface rivers at a few distances (every seventh river column, both axes).</summary>
    private static IEnumerable<(int X, int Z)> BesideRivers(RiverField field, params int[] distances)
    {
        foreach (var (x, z) in field.ColumnsByPosition.Keys.OrderBy(k => k.X).ThenBy(k => k.Z).Where((_, i) => i % 7 == 0))
        {
            foreach (int d in distances)
            {
                foreach (var (dx, dz) in new[] { (d, 0), (-d, 0), (0, d), (0, -d) })
                {
                    if (!field.TryGet(x + dx, z + dz, out _))
                    {
                        yield return (x + dx, z + dz);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData("highland", 777L)]
    [InlineData("jungle", 31L)]
    [InlineData("varied", 12345L)]
    [InlineData("boreal", 4242L)]
    [InlineData("river_lowlands", 12345L)]
    public void Valleys_LowerTheGroundBesideRivers_OnlyLower_AndNeverIntoTheSea(string type, long seed)
    {
        var planet = Content.Planets[type];
        var gen = Gen(seed, Gen40);
        var field = gen.RiverFieldFor(planet);
        Assert.NotNull(field.Valleys);
        int sea = gen.SeaLevel(planet);
        int n = 0, lowered = 0, deepest = 0;
        foreach (var (x, z) in BesideRivers(field, 2, 4, 8, 16))
        {
            int natural = gen.NaturalSurfaceHeight(planet, x, z);
            int final = gen.SurfaceHeight(planet, x, z);
            Assert.True(final <= natural, $"{type}: ({x},{z}) rose from {natural} to {final}");
            Assert.False(sea != int.MinValue && natural > sea && final <= sea, $"{type}: ({x},{z}) sank from {natural} into the sea at {sea}");
            n++;
            if (final < natural)
            {
                lowered++;
                deepest = Math.Max(deepest, natural - final);
            }
        }

        _out.WriteLine($"{type}: {lowered}/{n} columns beside rivers lowered, deepest {deepest}");
        Assert.True(lowered > n / 5, $"{type}: only {lowered} of {n} columns beside rivers lie in a valley");
        double limit = 28 * (planet.ValleyStrength ?? 1.0); // the trunk class cuts at most 28 at strength 1
        Assert.True(deepest <= limit, $"{type}: a valley cut {deepest} deep (the trunk limit is {limit})");
    }

    [Theory]
    [InlineData("highland", 777L)]
    [InlineData("jungle", 31L)]
    [InlineData("boreal", 4242L)]
    public void Valleys_KeepEveryRiverSealed(string type, long seed)
    {
        var planet = Content.Planets[type];
        var gen = Gen(seed, Gen40);
        var field = gen.RiverFieldFor(planet);
        int checkedSides = 0;
        foreach (var kv in field.ColumnsByPosition)
        {
            if (kv.Value.Underground)
            {
                continue;
            }

            var (x, z) = kv.Key;
            foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (field.TryGet(nx, nz, out _))
                {
                    continue;
                }

                int natural = gen.NaturalSurfaceHeight(planet, nx, nz);
                if (natural < kv.Value.WaterSurfaceY)
                {
                    continue; // open before the valleys: not theirs to seal
                }

                checkedSides++;
                Assert.True(gen.SurfaceHeight(planet, nx, nz) >= kv.Value.WaterSurfaceY,
                    $"{type}: the bank ({nx},{nz}) beside the river column ({x},{z}) sank below its water {kv.Value.WaterSurfaceY}");
            }
        }

        Assert.True(checkedSides > 1000, $"{type}: only {checkedSides} banks checked");
    }

    [Fact]
    public void Valleys_AreTheSameAcrossBothSeams_AndOnAFreshOrAWarmGenerator()
    {
        var planet = Content.Planets["highland"];
        var warm = Gen(777, Gen40);
        var field = warm.RiverFieldFor(planet);
        int circ = WorldConstants.Circumference, period = WorldConstants.LatitudePeriodFor(circ);
        var columns = BesideRivers(field, 3, 9).Where((_, i) => i % 5 == 0).Take(400).ToList();
        var fresh = Gen(777, Gen40);
        foreach (var (x, z) in columns)
        {
            int h = warm.SurfaceHeight(planet, x, z);
            Assert.Equal(h, warm.SurfaceHeightUncached(planet, x, z));
            Assert.Equal(h, fresh.SurfaceHeight(planet, x + circ, z));
            Assert.Equal(h, fresh.SurfaceHeight(planet, x, z - period));
        }
    }

    [Fact]
    public void OlderGenerations_HaveNoValleys()
    {
        var planet = Content.Planets["highland"];
        var gen = Gen(777, WorldDescription.SchoolClubLandmarksGeneration);
        var field = gen.RiverFieldFor(planet);
        Assert.Null(field.Valleys);
        Assert.Null(field.Geography);
        foreach (var (x, z) in BesideRivers(field, 2, 8).Take(500))
        {
            Assert.Equal(gen.NaturalSurfaceHeight(planet, x, z), gen.SurfaceHeight(planet, x, z));
        }
    }

    [Fact]
    public void ADryWorld_OfGenerationForty_GeneratesWhatGenerationThirtyNineDoes()
    {
        var planet = Content.Planets["desert"];
        var old = Gen(1, WorldDescription.SchoolClubLandmarksGeneration);
        var now = Gen(1, Gen40);
        Assert.Null(now.RiverFieldFor(planet).Valleys); // a dry world routes no rivers
        foreach (var (x, z) in new[] { (0, 0), (100, 37), (-200, 150), (1500, -900) })
        {
            int cy = WorldConstants.WorldToChunk(old.SurfaceHeight(planet, x, z));
            foreach (int dy in new[] { 0, -2, 1 })
            {
                var coord = new ChunkCoord(WorldConstants.WorldToChunk(x), cy + dy, WorldConstants.WorldToChunk(z));
                Assert.Equal(WorldGenerationGoldenTests.HashChunk(old.Generate(planet, coord)),
                    WorldGenerationGoldenTests.HashChunk(now.Generate(planet, coord)));
            }
        }
    }

    [Fact]
    public void LavaRivers_CutHalfAsStrongValleys()
    {
        var lava = Content.Planets["ashen"]; // a dry volcanic world: its rivers are lava
        var water = Content.Planets["highland"];
        var gen = Gen(99, Gen40);
        Assert.Equal(50, gen.RiverFieldFor(lava).Valleys!.StrengthPercent);

        Assert.Equal(100, gen.RiverFieldFor(water).Valleys!.StrengthPercent);
    }

    [Fact]
    public void ThePlanetMapSummary_ReadsTheNaturalGround()
    {
        var planet = Content.Planets["highland"];
        var gen = Gen(777, Gen40);
        var field = gen.RiverFieldFor(planet);
        var (x, z) = BesideRivers(field, 4, 8).First(c => gen.StageDepthAt(planet, c.X, c.Z) > 0);
        Assert.Equal(gen.NaturalSurfaceHeight(planet, x, z), gen.SummarizeSurface(planet, x, z).Height);

        // A fresh body's preview bake never builds its river field.
        var other = Gen(424243, Gen40, body: "golden:preview-only-body");
        for (int i = 0; i < 64; i++)
        {
            other.SummarizeSurface(planet, i * 97, (i * 53) - 1500);
        }

        Assert.False(other.RiverFieldCachedForTest(planet));
    }

    [Fact]
    public void TheBiomeBorders_FollowTheNaturalGround()
    {
        var planet = Content.Planets["highland"];
        var gen = Gen(777, Gen40);
        var field = gen.RiverFieldFor(planet);
        var nat = Gen(777, WorldDescription.SchoolClubLandmarksGeneration);
        foreach (var (x, z) in BesideRivers(field, 4, 8).Where(c => gen.StageDepthAt(planet, c.X, c.Z) > 4).Take(200))
        {
            Assert.Equal(nat.BiomeIndexAt(planet, x, z), gen.BiomeIndexAt(planet, x, z));
        }
    }

    [Fact]
    public void TheDistanceLayers_AreZeroOnTheWater_AndGrowAwayFromIt()
    {
        var planet = Content.Planets["highland"];
        var gen = Gen(777, Gen40);
        var field = gen.RiverFieldFor(planet);
        var (rx, rz) = field.ColumnsByPosition.First(kv => !kv.Value.Underground).Key;
        Assert.Equal(0, gen.FreshWaterDistance(planet, rx, rz));
        Assert.True(gen.FreshWaterDistance(planet, rx + 400, rz) >= gen.FreshWaterDistance(planet, rx + 16, rz) - 16);
        Assert.Equal(RiverGeography.None, Gen(1, Gen40).FreshWaterDistance(Content.Planets["desert"], 0, 0));
        Assert.Equal(RiverGeography.None, Gen(777, WorldDescription.SchoolClubLandmarksGeneration).FreshWaterDistance(planet, rx, rz));
        int circ = WorldConstants.Circumference;
        Assert.Equal(gen.CoastDistance(planet, rx, rz), gen.CoastDistance(planet, rx + circ, rz));
    }

    /// <summary>The cost guard (#3560): a generation-40 chunk beside a river must stay near its generation-39 twin (the same
    /// rule as the generation-3 and generation-21 guards: 1.3× + 250 ms, fastest of three interleaved rounds). Slow tier.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void GenerationForty_ChunkCost_StaysNearGenerationThirtyNine()
    {
        var planet = Content.Planets["jungle"];
        var probe = Gen(31, Gen40);
        var (rx, rz) = probe.RiverFieldFor(planet).ColumnsByPosition.Keys.OrderBy(k => k.X).ThenBy(k => k.Z).First();
        long Measure(int generation)
        {
            var gen = Gen(31, generation);
            gen.Generate(planet, new ChunkCoord(0, WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, 0, 0)), 0)); // warm-up
            var sw = Stopwatch.StartNew();
            for (int cx = -2; cx < 2; cx++)
                for (int cz = -2; cz < 2; cz++)
                {
                    int wx = rx + (cx * 16), wz = rz + (cz * 16);
                    var c = new ChunkCoord(WorldConstants.WorldToChunk(wx), WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, wx, wz)),
                        WorldConstants.WorldToChunk(wz));
                    gen.Generate(planet, c);
                    gen.Generate(planet, new ChunkCoord(c.X, c.Y - 1, c.Z));
                }

            return sw.ElapsedMilliseconds;
        }

        long t39 = long.MaxValue, t40 = long.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            t39 = Math.Min(t39, Measure(WorldDescription.SchoolClubLandmarksGeneration));
            t40 = Math.Min(t40, Measure(Gen40));
        }

        Assert.True(t40 <= (1.3 * t39) + 250, $"generation 40 took {t40} ms against {t39} ms for generation 39 (fastest of 3 rounds each)");
    }
}
