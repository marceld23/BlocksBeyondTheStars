// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 24 (#2516): generated water is enclosed — no sea, pond or lake cell has air beside or below it —
/// and the water-class query the fluid automaton reads (#2515).
/// </summary>
public sealed class WaterSealGenerationTests
{
    private readonly ITestOutputHelper _out;

    public WaterSealGenerationTests(ITestOutputHelper output) => _out = output;

    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A chunk cache over one generator, so a neighbour across a chunk border reads like any other cell.</summary>
    private sealed class Blocks
    {
        private readonly WorldGenerator _gen;
        private readonly PlanetType _planet;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public Blocks(WorldGenerator gen, PlanetType planet)
        {
            _gen = gen;
            _planet = planet;
        }

        public BlockId Get(int x, int y, int z)
        {
            var c = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
            if (!_chunks.TryGetValue(c, out var chunk))
            {
                chunk = _gen.Generate(_planet, c);
                _chunks[c] = chunk;
            }

            var origin = WorldConstants.ChunkOrigin(c);
            return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
        }
    }

    private static WorldGenerator Generator(GameContent content, long seed, int generation)
    {
        var gen = new WorldGenerator(seed, content);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    /// <summary>Generated water cells with air beside or below them in a square around (x0, z0), by class. A river's
    /// surface follows the ground along its flow and a generated fall stands free by design — both are counted apart
    /// (the automaton treats them as endless, and a fall as confined), everything else is a leak.</summary>
    private static Dictionary<string, int> OpenWater(GameContent content, long seed, string planetKey, int generation, int r)
    {
        var planet = content.GetPlanet(planetKey)!;
        var gen = Generator(content, seed, generation);
        var blocks = new Blocks(gen, planet);
        ushort water = content.GetBlock("water")!.NumericId.Value;
        int sea = gen.SeaLevel(planet);
        var river = gen.RiverFieldFor(planet);
        var counts = new Dictionary<string, int>();
        for (int x = -r; x <= r; x++)
        {
            for (int z = -r; z <= r; z++)
            {
                int surface = gen.SurfaceHeight(planet, x, z);
                bool riverColumn = river.TryGet(x, z, out var rc) && !rc.Underground;
                for (int y = Math.Min(sea, surface) - 24; y <= surface + 6; y++)
                {
                    if (blocks.Get(x, y, z).Value != water)
                    {
                        continue;
                    }

                    bool open = blocks.Get(x + 1, y, z).IsAir || blocks.Get(x - 1, y, z).IsAir
                        || blocks.Get(x, y, z + 1).IsAir || blocks.Get(x, y, z - 1).IsAir || blocks.Get(x, y - 1, z).IsAir;
                    if (!open)
                    {
                        continue;
                    }

                    string key = WorldGenerator.IsConfinedFall(gen.WaterKindAt(planet, x, y, z)) ? "fall"
                        : riverColumn ? "river"
                        : y <= sea ? "sea"
                        : gen.SurfacePondDepth(planet, x, z) > 0 ? "pond"
                        : "other";
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }
        }

        return counts;
    }

    private static int Leaks(Dictionary<string, int> counts)
        => counts.Where(kv => kv.Key != "fall" && kv.Key != "river").Sum(kv => kv.Value);

    [Theory]
    [InlineData(1L, "varied")]
    [InlineData(7L, "meadowlands")]
    [InlineData(-6457353509591164307L, "varied")] // the tester's world of #2515 (v2026.10.12, generation 23)
    public void Generation24_LeavesNoGeneratedWaterOpenToTheAir(long seed, string planet)
    {
        var counts = OpenWater(Content(), seed, planet, WorldDescription.WaterSealGeneration, 48);
        _out.WriteLine($"{planet} {seed}: " + string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.Equal(0, Leaks(counts));
    }

    [Fact]
    public void Generation23_StillLeavesWaterOpen_SoTheScanSeesALeak()
    {
        // The scan's own sanity: the generation before the seal leaves the sea open to the caves in the same window.
        var counts = OpenWater(Content(), 1L, "varied", WorldDescription.WaterSealGeneration - 1, 48);
        _out.WriteLine(string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.True(Leaks(counts) > 0, "generation 23 should still leave generated water open to the caves");
    }

    [Fact]
    public void Generation24_FillsEachPondToOneLevel()
    {
        var content = Content();
        var planet = content.GetPlanet("meadowlands")!;
        var gen = Generator(content, 7L, WorldDescription.WaterSealGeneration);
        var seen = new HashSet<(int, int)>();
        int ponds = 0;
        for (int x = -96; x <= 96; x++)
        {
            for (int z = -96; z <= 96; z++)
            {
                if (seen.Contains((x, z)) || gen.SurfacePondDepth(planet, x, z) <= 0)
                {
                    continue;
                }

                // One pond: the 4-connected pond columns around this one — every one of them holds water to one top.
                var tops = new HashSet<int>();
                var queue = new Queue<(int X, int Z)>();
                queue.Enqueue((x, z));
                seen.Add((x, z));
                while (queue.Count > 0)
                {
                    var (cx, cz) = queue.Dequeue();
                    Assert.True(gen.TryGetWaterSurface(planet, cx, cz, out int top, out _));
                    tops.Add(top);
                    foreach (var (nx, nz) in new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
                    {
                        if (!seen.Contains((nx, nz)) && gen.SurfacePondDepth(planet, nx, nz) > 0)
                        {
                            seen.Add((nx, nz));
                            queue.Enqueue((nx, nz));
                        }
                    }
                }

                ponds++;
                Assert.True(tops.Count == 1, $"the pond at ({x},{z}) holds water to {tops.Count} different tops: {string.Join(",", tops)}");
            }
        }

        Assert.True(ponds > 0, "the sample window should hold at least one pond");
    }

    [Fact]
    public void APondTooBigToResolve_IsNotSearchedAgainColumnByColumn()
    {
        // A component over the cap memoises "too big" only for the columns its search reached. A search from a column
        // beyond them used to walk back through the memoised ones up to the cap again; it now stops at the first one it
        // meets and takes over its verdict.
        var content = Content();
        var planet = content.GetPlanet("meadowlands")!;
        var gen = Generator(content, 7L, WorldDescription.WaterSealGeneration);
        gen.PondComponentCapOverride = 16; // every real pond of the sample is "too big" now
        int pondColumns = 0;
        for (int x = -96; x <= 96; x++)
            for (int z = -96; z <= 96; z++)
            {
                if (gen.SurfacePondDepth(planet, x, z) > 0)
                {
                    pondColumns++;
                }
            }

        var (searches, visits) = gen.PondLevelStatsForTest;
        _out.WriteLine($"{pondColumns} pond columns, {searches} searches, {visits} columns examined");
        Assert.True(pondColumns > 50, "the sample should hold ponds");
        // Measured: about 1.1 columns examined per pond column with the fix, 2.2 without it (each search walked back to the cap).
        Assert.True(visits <= pondColumns * 8L / 5, $"{visits} columns examined for {pondColumns} pond columns");
    }

    [Fact]
    public void WaterKindAt_NamesTheSea_ARiverAndItsFall_AndStillWater()
    {
        var content = Content();
        var planet = content.GetPlanet("varied")!;
        for (long seed = 1; seed <= 12; seed++)
        {
            var gen = Generator(content, seed, WorldDescription.WaterSealGeneration);
            int sea = gen.SeaLevel(planet);
            Assert.Equal(GeneratedWaterKind.BelowSeaLevel, gen.WaterKindAt(planet, 0, sea, 0));
            Assert.Equal(GeneratedWaterKind.BelowSeaLevel, gen.WaterKindAt(planet, 0, sea - 20, 0));

            foreach (var (pos, col) in gen.RiverFieldFor(planet).ColumnsByPosition)
            {
                if (col.Underground || col.WaterfallDrop <= 0 || col.WaterSurfaceY <= sea
                    || gen.SurfacePondDepth(planet, pos.X, pos.Z) > 0 || gen.SurfaceHeight(planet, pos.X, pos.Z) <= sea
                    || gen.TryGetVolcanoCrater(planet, pos.X, pos.Z, out _))
                {
                    continue;
                }

                Assert.Equal(GeneratedWaterKind.River, gen.WaterKindAt(planet, pos.X, col.WaterSurfaceY, pos.Z));
                Assert.Equal(GeneratedWaterKind.RiverFall, gen.WaterKindAt(planet, pos.X, col.WaterSurfaceY + 1, pos.Z));
                Assert.True(WorldGenerator.IsConfinedFall(GeneratedWaterKind.RiverFall));
                Assert.True(WorldGenerator.IsEndless(GeneratedWaterKind.River));
                Assert.False(WorldGenerator.IsEndless(GeneratedWaterKind.Still));
                Assert.Equal(GeneratedWaterKind.Still, gen.WaterKindAt(planet, pos.X, col.WaterSurfaceY + col.WaterfallDrop + 1, pos.Z));
                return;
            }
        }

        Assert.Fail("no river waterfall found on twelve varied worlds");
    }

    [Fact]
    public void WaterKindAt_NamesAGeneratedWaterfallBand()
    {
        var content = Content();
        var planet = content.GetPlanet("skylands")!;
        var gen = Generator(content, 1L, WorldDescription.WaterSealGeneration);
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        for (int x = -400; x <= 400; x += 2)
        {
            for (int z = -400; z <= 400; z += 2)
            {
                int n = gen.GetExtraBands(planet, x, z, bands);
                for (int i = 0; i < n; i++)
                {
                    if (bands[i].Kind == WorldGenerator.BandKind.Waterfall && bands[i].Bottom > gen.SeaLevel(planet))
                    {
                        Assert.Equal(GeneratedWaterKind.Waterfall, gen.WaterKindAt(planet, x, bands[i].Bottom, z));
                        Assert.True(WorldGenerator.IsConfinedFall(GeneratedWaterKind.Waterfall));
                        return;
                    }
                }
            }
        }

        Assert.Fail("no waterfall band found on the skylands sample");
    }
}
