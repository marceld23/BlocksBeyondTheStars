// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 3, part 5 — wetlands and rivers: river morphology (meanders with oxbows, delta fans,
/// floodplains with mud and pools), rias (drowned shelf-coast valleys), floating mats on lake water, peat
/// bogs (the new peat block, six deep, with pools) and thermokarst ponds with polygonal rims. All absent
/// below generation 3.
/// </summary>
public sealed class LandformGen3WetlandTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldGenerator Gen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        if (generation > 0)
        {
            gen.SetTerrainGeneration(generation);
        }

        return gen;
    }

    private static IEnumerable<(int X, int Z)> Grid(int stepX, int stepZ)
    {
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += stepZ)
            for (int x = 0; x < circ; x += stepX)
                yield return (x, z);
    }

    private static BlockId Cell(WorldGenerator gen, PlanetType planet, Dictionary<ChunkCoord, ChunkData> chunks, int x, int y, int z)
    {
        var coord = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
        if (!chunks.TryGetValue(coord, out var chunk))
        {
            chunk = gen.Generate(planet, coord);
            chunks[coord] = chunk;
        }

        const int cs = WorldConstants.ChunkSize;
        return chunk.Get(((x % cs) + cs) % cs, ((y % cs) + cs) % cs, ((z % cs) + cs) % cs);
    }

    private static BlockId Block(string key) => Content.GetBlock(key)!.NumericId;

    // ---------- river morphology ----------

    /// <summary>On a wet world the generation-3 field carries floodplain columns and morphology columns (delta
    /// fans, oxbows) that the generation-1 field of the same seed has none of; a floodplain column is painted
    /// mud, and some of them stand one deep in water.</summary>
    [Fact]
    public void RiverMorphology_FloodplainsMudAndPools_DeltasAndOxbows_OnlyFromGenerationThree()
    {
        var planet = Content.Planets["jungle"];
        var mud = Block("mud");
        var g3 = Gen(20260903, 3);
        var g1 = Gen(20260903, 1);
        var f3 = g3.RiverFieldFor(planet);
        var f1 = g1.RiverFieldFor(planet);
        Assert.Equal(0, f1.FloodplainColumnCount);
        Assert.Equal(0, f1.MorphologyColumnCount);
        Assert.True(f3.FloodplainColumnCount > 0,
            $"no floodplain on a wet generation-3 world (gate {g3.WonderGatesForTest(planet)["riverMorphology"]}, built with morphology {f3.Morphology}, columns {f3.ColumnCount} vs {f1.ColumnCount}, "
            + $"underground {f3.ColumnsByPosition.Count(kv => kv.Value.Underground)}, morphology columns {f3.MorphologyColumnCount}, lake shore {f3.LakeShoreColumnCount}, "
            + $"outlet strokes {f3.OutletStrokeCount}, trunk strokes {f3.TrunkStrokeCount})");
        Assert.True(f3.MorphologyColumnCount > 0, "no delta fan or oxbow on a wet generation-3 world");

        int sea = g3.SeaLevel(planet);
        int painted = 0, pools = 0, checkedCols = 0;
        foreach (var (x, z) in Grid(3, 3))
        {
            if (!f3.IsFloodplain(x, z) || f3.TryGet(x, z, out _) || g3.SurfaceHeight(planet, x, z) <= sea + 1)
            {
                continue;
            }

            checkedCols++;
            if (g3.LandmarkPaintForTest("floodplain", planet, x, z) == mud)
            {
                painted++;
            }

            if (g3.Gen1WaterForTest(planet, x, z) is { Fluid: "water" } body && body.Top == g3.SurfaceHeight(planet, x, z) && body.Bed == body.Top - 1)
            {
                pools++;
            }

            if (checkedCols >= 400)
            {
                break;
            }
        }

        Assert.True(checkedCols > 20, "too few dry floodplain columns on the sample grid");
        Assert.Equal(checkedCols, painted);
        Assert.True(pools > 0 && pools < checkedCols, $"floodplain pools {pools} of {checkedCols}");
    }

    /// <summary>The rasteriser on a synthetic plane: sinuosity bends a low-gradient stroke off its straight
    /// line, a delta fans out of the sea outlet, and the trunk gets a floodplain — and every parameter at its
    /// default reproduces the classic field exactly.</summary>
    [Fact]
    public void Synthetic_Morphology_BendsFansAndFlags_AndDefaultsAreClassic()
    {
        const int w = 160, period = 80, seaLevel = 2, cell = 16;
        int H(int x, int z)
        {
            int wx = ((x % w) + w) % w;
            if (wx < 24) return 0; // the sea at the west edge — a whole coarse cell of it, so the outlet's downstream IS a sea cell
            int zc = WorldConstants.WrapZ(z, w);
            return 3 + wx / 16 + Math.Abs(zc) / 16; // one block per coarse cell: the low gradient that meanders
        }

        var net = RiverNetwork.Build(seed: 77, circumference: w, latitudePeriod: period, seaLevel: seaLevel, height: H, cellSize: cell);
        var classic = RiverField.Build(net, H, circumference: w);
        var again = RiverField.Build(net, H, circumference: w, sinuosity: 0.0, distributaries: 0, floodplainWidth: 0);
        Assert.Equal(classic.ColumnCount, again.ColumnCount);
        Assert.Equal(0, classic.FloodplainColumnCount);
        Assert.Equal(0, classic.MorphologyColumnCount);
        Assert.True(classic.ColumnsByPosition.Keys.ToHashSet().SetEquals(again.ColumnsByPosition.Keys), "the defaults are not the classic field");

        var bent = RiverField.Build(net, H, circumference: w, sinuosity: 1.0);
        Assert.False(bent.ColumnsByPosition.Keys.ToHashSet().SetEquals(classic.ColumnsByPosition.Keys), "sinuosity moved no column off the straight stroke");
        Assert.Equal(bent.ColumnCount, RiverField.Build(net, H, circumference: w, sinuosity: 1.0).ColumnCount); // deterministic

        var delta = RiverField.Build(net, H, circumference: w, distributaries: 4);
        Assert.True(delta.MorphologyColumnCount > 0, "no delta fan at the sea outlet");

        var plain = RiverField.Build(net, H, circumference: w, floodplainWidth: 6);
        Assert.True(plain.FloodplainColumnCount > 0, "no floodplain beside the trunk");
        foreach (var (x, z) in plain.ColumnsByPosition.Keys)
        {
            Assert.False(plain.IsFloodplain(x, z), "a river column is flagged floodplain");
        }
    }

    // ---------- rias ----------

    /// <summary>A ria drowns a shelf-coast valley: the generation-3 ground there lies at least two below the
    /// sea (and below the generation-1 ground of the same seed), so the sea runs up the valley.</summary>
    [Fact]
    public void Rias_DrownShelfCoastValleys_OnlyFromGenerationThree()
    {
        var planet = Content.Planets["ocean"];
        for (long s = 1; s <= 30; s++)
        {
            var g3 = Gen(s * 6151 + 3, 3);
            var g1 = Gen(s * 6151 + 3, 1);
            int sea = g3.SeaLevel(planet);
            foreach (var (x, z) in Grid(7, 5))
            {
                double off = g3.WetlandOffsetForTest("ria", planet, x, z);
                if (off > -2.0)
                {
                    continue;
                }

                Assert.Equal(0.0, g1.WetlandOffsetForTest("ria", planet, x, z));
                int h3 = g3.SurfaceHeight(planet, x, z);
                Assert.True(h3 <= sea - 2, $"ria ground {h3} at ({x},{z}) is not below the sea {sea}");
                Assert.True(h3 < g1.SurfaceHeight(planet, x, z), "the ria did not lower the coast");
                Assert.True(g3.IsSurfaceWater(planet, x, z), "the drowned valley is not sea");
                return;
            }
        }

        Assert.Fail("no ria on the sample grid in 30 seeds");
    }

    // ---------- floating mats ----------

    /// <summary>A mat is one cell of mud at a pooled lake column's water top, water under it, air over it.</summary>
    [Fact]
    public void FloatingMats_SitAtTheLakeTop_MudOverWater()
    {
        var planet = Content.Planets["swamp"];
        var mud = Block("mud");
        var water = Block("water");
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            var field = gen.RiverFieldFor(planet);
            int sea = gen.SeaLevel(planet);
            foreach (var (pos, col) in field.ColumnsByPosition)
            {
                if (!field.TryGetPooled(pos.X, pos.Z, out _) || col.WaterSurfaceY <= sea + 1
                    || gen.MatBandForTest(planet, pos.X, pos.Z) is not { } matY)
                {
                    continue;
                }

                Assert.Equal(col.WaterSurfaceY, matY);
                var chunks = new Dictionary<ChunkCoord, ChunkData>();
                var matCell = Cell(gen, planet, chunks, pos.X, matY, pos.Z);
                Assert.True(matCell == mud, $"the mat at ({pos.X},{matY},{pos.Z}) is {Content.BlockById(matCell)?.Key ?? matCell.ToString()}");
                Assert.Equal(water, Cell(gen, planet, chunks, pos.X, matY - 1, pos.Z));
                Assert.True(Cell(gen, planet, chunks, pos.X, matY + 1, pos.Z).IsAir, "the mat has something on it already");
                Assert.Null(Gen(s * 6151 + 3, 1).MatBandForTest(planet, pos.X, pos.Z));
                return;
            }
        }

        Assert.Fail("no floating mat on a swamp lake in 40 seeds");
    }

    // ---------- peat bogs ----------

    /// <summary>Peat six deep across the bog on flat ground, pools in two fifths of it, nothing of either on
    /// generation 1.</summary>
    [Fact]
    public void PeatBogs_PaintSixDeep_WithPools()
    {
        var planet = Content.Planets["boreal"];
        var peat = Block("peat");
        Assert.False(Gen(20260903, 1).PeatRegionForTest(planet, 10, 10));
        for (long s = 1; s <= 30; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            (int X, int Z)? dry = null;
            int region = 0, pools = 0;
            foreach (var (x, z) in Grid(5, 5))
            {
                if (!gen.PeatRegionForTest(planet, x, z) || gen.SurfaceHeight(planet, x, z) <= sea + 1 || gen.RiverFieldFor(planet).TryGet(x, z, out _))
                {
                    continue;
                }

                region++;
                bool pool = gen.Gen1WaterForTest(planet, x, z) is { Fluid: "water" } body && body.Top == gen.SurfaceHeight(planet, x, z);
                if (pool)
                {
                    pools++;
                }
                else
                {
                    dry ??= (x, z);
                }

                if (region >= 300)
                {
                    break;
                }
            }

            if (region < 50 || dry is null)
            {
                continue;
            }

            Assert.True(pools > region / 10 && pools < region * 7 / 10, $"peat pools {pools} of {region}");
            var (px, pz) = dry.Value;
            Assert.Equal(peat, gen.LandmarkPaintForTest("peat-bog", planet, px, pz, out int fill));
            int surface = gen.SurfaceHeight(planet, px, pz);
            Assert.Equal(surface - 6, fill);
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            for (int y = surface; y >= surface - 6; y--)
            {
                Assert.True(Cell(gen, planet, chunks, px, y, pz) == peat, $"cell ({px},{y},{pz}) in the bog is not peat");
            }

            Assert.NotEqual(peat, Cell(gen, planet, chunks, px, surface - 7, pz));
            return;
        }

        Assert.Fail("no boreal seed with a bog on the sample grid");
    }

    // ---------- thermokarst ----------

    /// <summary>A thaw pond is 2–4 deep with a one-block rim around its polygon; none on generation 1.</summary>
    [Fact]
    public void Thermokarst_PondsAreDeepWithRims_OnlyFromGenerationThree()
    {
        var planet = Content.Planets["tundra"];
        var water = Block("water");
        var ice = Block("ice");
        Assert.Equal((0, false), Gen(20260903, 1).ThermokarstForTest(planet, 10, 10));
        for (long s = 1; s <= 30; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            (int X, int Z, int Depth)? pond = null;
            (int X, int Z)? rim = null;
            foreach (var (x, z) in Grid(4, 4))
            {
                var (depth, isRim) = gen.ThermokarstForTest(planet, x, z);
                if (rim is null && isRim)
                {
                    rim = (x, z);
                }

                if (pond is null && depth > 0 && gen.SurfaceHeight(planet, x, z) > sea + 4
                    && gen.Gen1WaterForTest(planet, x, z) is { Fluid: "water" } body
                    && body.Top == gen.SurfaceHeight(planet, x, z) && body.Bed == body.Top - depth)
                {
                    pond = (x, z, depth);
                }

                if (pond is not null && rim is not null)
                {
                    break;
                }
            }

            if (pond is null || rim is null)
            {
                continue;
            }

            var (px, pz, pd) = pond.Value;
            Assert.InRange(pd, 2, 4);
            int top = gen.SurfaceHeight(planet, px, pz);
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            var topCell = Cell(gen, planet, chunks, px, top, pz);
            Assert.True(topCell == ice || topCell == water, $"the pond top at ({px},{pz}) is {topCell}");
            var bottom = Cell(gen, planet, chunks, px, top - pd + 1, pz);
            Assert.True(bottom == water || bottom == ice, $"the pond bottom cell is {bottom}");
            Assert.False(Cell(gen, planet, chunks, px, top - pd, pz).IsAir, "the pond has no bed");
            Assert.Equal(1.0, gen.WetlandOffsetForTest("thermokarst", planet, rim.Value.X, rim.Value.Z));
            return;
        }

        Assert.Fail("no tundra seed with a thaw pond and a rim on the sample grid");
    }

    // ---------- gates ----------

    [Fact]
    public void Gates_WetlandFamilies_FromGenerationThreeOnly()
    {
        var g3 = Gen(20260903, 3);
        var g1 = Gen(20260903, 1);
        var swamp = g3.WonderGatesForTest(Content.Planets["swamp"]);
        Assert.True(swamp["riverMorphology"] && swamp["floatingMats"] && swamp["peatBogs"] && swamp["rias"], "the swamp has every wet family");
        Assert.False(swamp["thermokarst"], "the swamp is too warm to thaw");
        var tundra = g3.WonderGatesForTest(Content.Planets["tundra"]);
        Assert.True(tundra["thermokarst"] && tundra["peatBogs"], "the tundra thaws and grows peat");
        Assert.False(g3.WonderGatesForTest(Content.Planets["glacier"])["rias"], "fjord country keeps its fjords");
        var highland = g3.WonderGatesForTest(Content.Planets["highland"]);
        Assert.True(highland["riverMorphology"] && highland["rias"], "a wet alpine world has meandering rivers and drowned coastal valleys");
        Assert.False(highland["floatingMats"] || highland["peatBogs"] || highland["thermokarst"], "the highland is neither wetland nor cold enough");
        foreach (var key in new[] { "swamp", "tundra", "ocean" })
        {
            var gates = g1.WonderGatesForTest(Content.Planets[key]);
            Assert.False(gates["riverMorphology"] || gates["rias"] || gates["floatingMats"] || gates["peatBogs"] || gates["thermokarst"], $"{key} on generation 1");
        }
    }
}
