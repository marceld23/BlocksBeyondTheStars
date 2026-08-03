// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Beaches (#679): sand along the waterline of the sea and of large lakes. Verifies the shared
/// <see cref="WorldGenerator.IsBeachColumn"/> query, that Generate actually paints the beach block on
/// those columns (and on the shallow submerged apron), that dry/lava worlds get none, determinism, and
/// the <see cref="RiverField"/> lake-shore ring on a synthetic basin.
/// </summary>
public class BeachGenerationTests
{
    private readonly ITestOutputHelper _out;
    public BeachGenerationTests(ITestOutputHelper output) => _out = output;

    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>The generated block at an absolute world position (generates the containing chunk).</summary>
    private static BlockId BlockAt(WorldGenerator gen, PlanetType planet, int wx, int wy, int wz)
    {
        int cs = WorldConstants.ChunkSize;
        var coord = new ChunkCoord(FloorDiv(wx, cs), FloorDiv(wy, cs), FloorDiv(wz, cs));
        var chunk = gen.Generate(planet, coord);
        var origin = WorldConstants.ChunkOrigin(coord);
        return chunk.Get(wx - origin.X, wy - origin.Y, wz - origin.Z);
    }

    /// <summary>Sparse whole-world scan for dry columns inside the sea's beach band (sea..sea+3).</summary>
    private static List<(int X, int Z, int SurfaceY)> SeaBandColumns(
        WorldGenerator gen, PlanetType planet, int sea, int step)
    {
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        var band = new List<(int, int, int)>();
        for (int x = 0; x < circ; x += step)
            for (int z = -period / 2; z < period / 2; z += step)
            {
                int sy = gen.SurfaceHeight(planet, x, z);
                if (sy >= sea && sy - sea <= 3)
                {
                    band.Add((x, z, sy));
                }
            }

        return band;
    }

    [Fact]
    public void SeaCoast_GrowsBeaches_AndGeneratePaintsTheBeachBlock()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        Assert.True(sea != int.MinValue, "jungle must have a sea (percentile level, #473)");

        var band = SeaBandColumns(gen, planet, sea, step: 13);
        Assert.True(band.Count > 0, "no columns in the coastal band at all — terrain sampling broken?");

        var beaches = new List<(int X, int Z, int SurfaceY)>();
        foreach (var (x, z, sy) in band)
        {
            if (gen.IsBeachColumn(planet, x, z))
            {
                beaches.Add((x, z, sy));
            }
        }

        _out.WriteLine($"jungle/7: bandColumns={band.Count}, beachColumns={beaches.Count}, sea={sea}");
        Assert.True(beaches.Count > 0, "a watery world's coast produced no beach columns");
        Assert.True(beaches.Count < band.Count,
            "EVERY coastal-band column is beach — the coast-character mask isn't gating anything");

        // Generate must paint the beach block (sand on jungle) on the beach columns it claims.
        var sand = content.GetBlock("sand")!.NumericId;
        int verified = 0;
        foreach (var (x, z, sy) in beaches)
        {
            if (verified >= 6)
            {
                break;
            }

            Assert.Equal(sand, BlockAt(gen, planet, x, sy, z));
            verified++;
        }

        Assert.True(verified > 0);
    }

    [Fact]
    public void SeaApron_ShallowSeabedNearTheShore_ReadsSandy()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var gen = new WorldGenerator(7, content);
        int sea = gen.SeaLevel(planet);
        var sand = content.GetBlock("sand")!.NumericId;

        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int shallow = 0, sandy = 0;
        for (int x = 0; x < circ && sandy == 0; x += 13)
            for (int z = -period / 2; z < period / 2; z += 13)
            {
                int sy = gen.SurfaceHeight(planet, x, z);
                int depth = sea - sy;
                if (depth < 1 || depth > 3)
                {
                    continue; // not the shallow apron band
                }

                shallow++;
                if (BlockAt(gen, planet, x, sy, z) == sand)
                {
                    sandy++;
                    break;
                }

                if (shallow >= 60)
                {
                    break; // the coast mask covers ~55-60 % — 60 shallow samples MUST hit a beach stretch
                }
            }

        _out.WriteLine($"jungle/7: shallowSampled={shallow}, sandySeabed={sandy}");
        Assert.True(sandy > 0, "no sandy seabed apron found in the shallow band near the coast");
    }

    [Fact]
    public void DryAndLavaWorlds_GetNoBeaches()
    {
        var content = Content();
        var gen = new WorldGenerator(7, content);
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);

        foreach (var key in new[] { "desert", "lava", "asteroid" })
        {
            var planet = content.GetPlanet(key)!;
            for (int x = 0; x < circ; x += 97)
                for (int z = -period / 2; z < period / 2; z += 97)
                {
                    Assert.False(gen.IsBeachColumn(planet, x, z),
                        $"{key} ({x},{z}): a world without a water shoreline claims a beach column");
                }
        }
    }

    [Fact]
    public void BeachClassification_IsDeterministic_AcrossGeneratorInstances()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        var genA = new WorldGenerator(7, content);
        var genB = new WorldGenerator(7, content);
        int sea = genA.SeaLevel(planet);

        int checked_ = 0;
        foreach (var (x, z, _) in SeaBandColumns(genA, planet, sea, step: 31))
        {
            Assert.Equal(genA.IsBeachColumn(planet, x, z), genB.IsBeachColumn(planet, x, z));
            if (++checked_ >= 300)
            {
                break;
            }
        }

        Assert.True(checked_ > 0, "no coastal columns compared");
    }

    // A synthetic closed basin above sea level: the priority-flood fills it (fill-and-spill lake), the
    // strokes pool through it, and the field must ring the pooled water with dry shore markers — but only
    // when the lake's visible water meets the size threshold.
    [Fact]
    public void Synthetic_LargeLake_GetsShoreRing_SmallThresholdRespected()
    {
        const int w = 160, period = 80, seaLevel = 5, cell = 4;
        int H(int x, int z)
        {
            int wx = ((x % w) + w) % w;
            if (wx < 3)
            {
                return 0; // sea sink at the west edge
            }

            int zc = WorldConstants.WrapZ(z, w);
            int baseH = wx + Math.Abs(zc) / 4;     // west-draining ramp + V-valley funnel onto z=0
            int dx = wx - 60;
            if (dx * dx + zc * zc <= 100)
            {
                return 45; // flat-bottom bowl around (60,0): a closed depression the flood fills to ~its west rim
            }

            return baseH;
        }

        var net = RiverNetwork.Build(seed: 77, circumference: w, latitudePeriod: period,
            seaLevel: seaLevel, height: H, cellSize: cell);
        var field = RiverField.Build(net, H, circumference: w, minLakeShoreColumns: 8);
        var field2 = RiverField.Build(net, H, circumference: w, minLakeShoreColumns: 8);
        var fieldHuge = RiverField.Build(net, H, circumference: w, minLakeShoreColumns: 100000);

        Assert.True(field.LakeShoreColumnCount > 0, "the filled basin produced no lake-shore ring");
        Assert.Equal(field.LakeShoreColumnCount, field2.LakeShoreColumnCount); // determinism
        Assert.Equal(0, fieldHuge.LakeShoreColumnCount); // size threshold respected

        // Every shore marker is DRY (not a water column), sits just above its lake's waterline, and has
        // pooled/river water nearby (within the ring width of 3).
        int shores = 0;
        for (int x = 0; x < w; x++)
            for (int z = -period / 2; z < period / 2; z++)
            {
                if (!field.TryGetLakeShore(x, z, out int level))
                {
                    continue;
                }

                shores++;
                Assert.False(field.TryGet(x, z, out _), $"shore ({x},{z}) is also a water column");
                int terrain = H(x, z);
                Assert.InRange(terrain, level, level + 3);

                bool waterNearby = false;
                for (int dx = -3; dx <= 3 && !waterNearby; dx++)
                    for (int dz = -3; dz <= 3 && !waterNearby; dz++)
                    {
                        waterNearby = field.TryGet(x + dx, z + dz, out _);
                    }

                Assert.True(waterNearby, $"shore ({x},{z}) has no water within the ring width");
            }

        Assert.Equal(field.LakeShoreColumnCount, shores);
        _out.WriteLine($"synthetic lake: shoreColumns={shores}");
    }
}
