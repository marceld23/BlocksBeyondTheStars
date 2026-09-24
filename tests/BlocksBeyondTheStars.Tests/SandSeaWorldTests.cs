// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
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
/// The sand-sea planet class (#2000, terrain generation 9): a calibrated share of the surface is a sea of deep sand with
/// no caves under it that never floods, rock islands stand in it, and the land around it keeps its own procedural relief.
/// </summary>
public sealed class SandSeaWorldTests
{
    private const string Key = "sand_sea";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static PlanetType SandSea => Content.GetPlanet(Key)!;

    private static WorldGenerator Generator(long seed, int generation = WorldDescription.GiantsGeneration)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private sealed class Probe
    {
        private readonly WorldGenerator _gen;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public Probe(WorldGenerator gen) => _gen = gen;

        public BlockId At(int x, int y, int z)
        {
            var coord = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                chunk = _gen.Generate(SandSea, coord);
                _chunks[coord] = chunk;
            }

            var origin = WorldConstants.ChunkOrigin(coord);
            return chunk.Get(x - origin.X, y - origin.Y, z - origin.Z);
        }
    }

    private static List<(int X, int Z)> SeaColumns(WorldGenerator gen, int want, int step = 37)
    {
        var list = new List<(int, int)>();
        for (int x = 0; x < 3000 && list.Count < want; x += step)
            for (int z = -900; z < 900 && list.Count < want; z += step)
                if (gen.IsSandSeaAt(SandSea, x, z))
                {
                    list.Add((x, z));
                }

        return list;
    }

    [Fact]
    public void SandSea_IsDataComplete_AndGatedToGenerationNine()
    {
        var p = SandSea;
        Assert.Equal(WorldDescription.GiantsGeneration, p.MinTerrainGeneration);
        Assert.InRange(p.SandSeaShare, 0.3, 0.7);
        Assert.True(p.SandSeaDepth >= 16);
        Assert.Single(p.Biomes, b => b.SandSea);
        Assert.Contains(p.Biomes, b => !b.SandSea && b.SurfaceBlock != "sand"); // the rock country around it
        Assert.Contains("mountains", p.TerrainStyles);
        Assert.Contains("canyons", p.TerrainStyles);
    }

    [Fact]
    public void Sea_CoversItsShareOfTheSurface()
    {
        foreach (long seed in new long[] { 11, 2026, 90210 })
        {
            double cover = Generator(seed).SandSeaCoverageForTest(SandSea, step: 40);
            // Rock islands and the shore band take a little of the region's share.
            Assert.InRange(cover, SandSea.SandSeaShare * 0.6, SandSea.SandSeaShare * 1.1);
        }
    }

    [Fact]
    public void OlderGenerations_HaveNoSea()
    {
        var gen = Generator(2026, WorldDescription.GiantsGeneration - 1);
        Assert.Equal(0.0, gen.SandSeaCoverageForTest(SandSea, step: 60));
        // And no other type ever has one.
        var desert = Content.GetPlanet("desert")!;
        Assert.False(Generator(2026).IsSandSeaAt(desert, 100, 100));
    }

    [Fact]
    public void SeaColumns_AreDeepSand_WithNothingCarvedUnderThem()
    {
        var gen = Generator(2026);
        var probe = new Probe(gen);
        var sand = Content.GetBlock("sand")!.NumericId;
        var cols = SeaColumns(gen, 24);
        Assert.True(cols.Count >= 20, "no sea found");
        foreach (var (x, z) in cols)
        {
            int top = gen.SurfaceHeight(SandSea, x, z);
            for (int d = 0; d < SandSea.SandSeaDepth - 1; d++)
            {
                var b = probe.At(x, top - d, z);
                Assert.True(b == sand, $"({x},{z}) depth {d}: {Content.BlockById(b)?.Key ?? "?"} instead of sand");
            }
        }
    }

    [Fact]
    public void Sea_NeverFloods()
    {
        var gen = Generator(2026);
        int sea = gen.SeaLevel(SandSea);
        foreach (var (x, z) in SeaColumns(gen, 60))
        {
            int top = gen.SurfaceHeight(SandSea, x, z);
            Assert.True(sea == int.MinValue || top > sea, $"sea column ({x},{z}) at {top} lies under the waterline {sea}");
        }
    }

    [Fact]
    public void AroundTheSea_TheLandKeepsItsRelief()
    {
        // The rock country is not flattened: its columns span a much wider height range than the sea's dunes.
        var gen = Generator(2026);
        var sea = new List<int>();
        var land = new List<int>();
        for (int x = 0; x < 3000; x += 29)
            for (int z = -900; z < 900; z += 29)
            {
                int h = gen.SurfaceHeight(SandSea, x, z);
                (gen.IsSandSeaAt(SandSea, x, z) ? sea : land).Add(h);
            }

        Assert.NotEmpty(land);
        int seaSpan = sea.Max() - sea.Min();
        int landSpan = land.Max() - land.Min();
        Assert.True(landSpan > seaSpan + 10, $"land span {landSpan} vs sea span {seaSpan}");
    }
}
