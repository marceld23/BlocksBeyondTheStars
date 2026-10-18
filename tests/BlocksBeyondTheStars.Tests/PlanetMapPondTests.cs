// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The planet-map preview's ponds (#2758) and the raw-height slot (#2759). Since terrain generation 24 a pond's water
/// sits at one level found by a search over the whole pond; map pixels lie dozens of blocks apart and never share a
/// pond, so a bake that asked <see cref="WorldGenerator.SurfacePondDepth"/> per pixel paid a full search for every
/// water candidate — seconds per landing map. The map takes <see cref="SurfaceSummary.Pond"/> instead.
/// </summary>
public sealed class PlanetMapPondTests
{
    // The playtest save of 2026-10-18: a generation-26 karst planet whose landing map took 13.7 s on .NET.
    private const long PlaytestSeed = -7848507700092313449L;
    private const string PlaytestBody = "sys3-p0";
    private const int PlaytestCircumference = 9184;

    private readonly ITestOutputHelper _out;

    public PlanetMapPondTests(ITestOutputHelper output) => _out = output;

    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Generator(GameContent content, int generation, int circumference = PlaytestCircumference,
        string body = PlaytestBody)
    {
        var gen = new WorldGenerator(PlaytestSeed, content);
        gen.SetWorldMode(circumference, false, landingPads: null, body);
        gen.SetContinentsEnabled(true);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    /// <summary>The pixel centres of a map of <paramref name="w"/>×<paramref name="h"/> — the bake's own sampling.</summary>
    private static IEnumerable<(int X, int Z)> MapColumns(int circumference, int w, int h)
    {
        int lat = WorldConstants.LatitudePeriodFor(circumference);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                yield return ((int)((x + 0.5) / w * circumference), (int)(((y + 0.5) / h - 0.5) * lat));
            }
        }
    }

    [Fact]
    public void MapSummary_NeverRunsAPondLevelSearch_AndStillFindsThePonds()
    {
        var content = Content();
        var planet = content.GetPlanet("karst")!;
        var gen = Generator(content, WorldDescription.CurrentTerrainGeneration);
        int sea = gen.SeaLevel(planet);
        int ponds = 0;
        foreach (var (x, z) in MapColumns(PlaytestCircumference, 96, 48))
        {
            var col = gen.SummarizeSurface(planet, x, z);
            if (col.Pond)
            {
                Assert.True(col.Height > sea, "a map pond never sits at or below the sea");
                ponds++;
            }
        }

        var (searches, visits) = gen.PondLevelStatsForTest;
        _out.WriteLine($"{ponds} map ponds, {searches} pond-level searches, {visits} columns examined");
        Assert.True(ponds > 20, $"the playtest planet shows ponds on its map, got {ponds}");
        Assert.Equal(0, searches); // the whole point of #2758: a bake pays no per-pond search
    }

    [Fact]
    public void MapPond_CoversEveryRealPond_AndIsExactBeforeTheSealedWater()
    {
        var content = Content();
        var planet = content.GetPlanet("karst")!;

        // Generation 26: the map flag is a superset of the sealed ponds (a pond the sealed level leaves dry still reads
        // as water at map scale). Two generators, so the map side's statistics stay clean.
        var map = Generator(content, WorldDescription.CurrentTerrainGeneration);
        var exact = Generator(content, WorldDescription.CurrentTerrainGeneration);
        int real = 0, extra = 0;
        foreach (var (x, z) in MapColumns(PlaytestCircumference, 48, 24))
        {
            bool pond = map.SummarizeSurface(planet, x, z).Pond;
            if (exact.SurfacePondDepth(planet, x, z) > 0)
            {
                real++;
                Assert.True(pond, $"({x}, {z}) holds a pond the map does not show");
            }
            else if (pond)
            {
                extra++;
            }
        }

        _out.WriteLine($"generation 26: {real} real ponds, {extra} map-only");
        Assert.True(real > 3, $"the sample should hold real ponds, got {real}");
        Assert.Equal(0, map.PondLevelStatsForTest.Searches);

        // Below generation 24 there is no sealed level: the map flag IS the pond query.
        var old = Generator(content, WorldDescription.WaterSealGeneration - 1);
        int oldPonds = 0;
        foreach (var (x, z) in MapColumns(PlaytestCircumference, 48, 24))
        {
            bool pond = old.SummarizeSurface(planet, x, z).Pond;
            Assert.Equal(old.SurfacePondDepth(planet, x, z) > 0, pond);
            oldPonds += pond ? 1 : 0;
        }

        Assert.True(oldPonds > 3, $"the generation-23 sample should hold ponds, got {oldPonds}");
    }

    [Fact]
    public void RawHeightSlot_NeverLeaksBetweenGenerators_OrThreads()
    {
        // #2759: the raw relief of the column being resolved is kept in a thread-static slot so the sea-relative rows
        // (the ria, the coast families) do not compute it again. Two generators of different world modes interleaved on
        // one thread, and one generator shared by several threads, must each give exactly what a lone generator gives.
        var content = Content();
        var planet = content.GetPlanet("ocean")!; // rias and the coast families are active on an ocean world
        var columns = MapColumns(PlaytestCircumference, 40, 20).ToArray();

        int[] Alone(int circumference, string body)
        {
            var gen = Generator(content, WorldDescription.CurrentTerrainGeneration, circumference, body);
            return columns.Select(c => gen.SurfaceHeightUncached(planet, c.X, c.Z)).ToArray();
        }

        int[] expectA = Alone(PlaytestCircumference, PlaytestBody);
        int[] expectB = Alone(5472, "sys0-p1");

        var a = Generator(content, WorldDescription.CurrentTerrainGeneration);
        var b = Generator(content, WorldDescription.CurrentTerrainGeneration, 5472, "sys0-p1");
        for (int i = 0; i < columns.Length; i++)
        {
            Assert.Equal(expectA[i], a.SurfaceHeightUncached(planet, columns[i].X, columns[i].Z));
            Assert.Equal(expectB[i], b.SurfaceHeightUncached(planet, columns[i].X, columns[i].Z));
        }

        var shared = Generator(content, WorldDescription.CurrentTerrainGeneration);
        var got = new int[columns.Length];
        Parallel.For(0, columns.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 },
            i => got[i] = shared.SurfaceHeightUncached(planet, columns[i].X, columns[i].Z));
        Assert.Equal(expectA, got);
    }
}
