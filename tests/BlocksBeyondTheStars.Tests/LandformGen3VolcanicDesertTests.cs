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
/// Terrain generation 3, part 4 — the volcanic and desert landforms: obsidian fields (a paint three deep with
/// crystal glints), lava flows (tongues from a cone foot with a basalt skin and glowing pockets), barchans (a
/// field of crescent dunes where no dune sea was rolled) and frost polygons (a stone-ridged net with ice-covered
/// plate ponds on cold wet ground). All absent below generation 3.
/// </summary>
public sealed class LandformGen3VolcanicDesertTests
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

    private static string TunnelsAt(WorldGenerator gen, PlanetType planet, int x, int z)
    {
        var spans = new (int Lo, int Hi)[16];
        int n = gen.TunnelSpans(planet, x, z, spans);
        return string.Join(" ", spans.Take(n).Select(s => $"{s.Lo}..{s.Hi}"));
    }

    // ---------- obsidian fields ----------

    /// <summary>Obsidianfelder: inside a field the paint claims the column three deep and studs the surface with
    /// crystal on a dithered minority of cells; the fourth cell down is not obsidian.</summary>
    [Fact]
    public void ObsidianFields_PaintThreeDeep_WithCrystalGlints()
    {
        var planet = Content.Planets["lava"];
        var obsidian = Block("obsidian");
        var crystal = Block("crystal");
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int obsidianCells = 0, glints = 0;
            (int X, int Z, int Fill)? sample = null;
            foreach (var (x, z) in Grid(7, 9))
            {
                var painted = gen.LandmarkPaintForTest("obsidian-field", planet, x, z, out int fill);
                if (painted == obsidian)
                {
                    obsidianCells++;
                    sample ??= (x, z, fill);
                }
                else if (painted == crystal)
                {
                    glints++;
                }
            }

            if (obsidianCells == 0)
            {
                continue;
            }

            Assert.True(glints > 0 && glints < obsidianCells, $"glints {glints} of {obsidianCells + glints} field cells");
            var (sx, sz, sampleFill) = sample!.Value;
            int surface = gen.SurfaceHeight(planet, sx, sz);
            Assert.Equal(surface - 3, sampleFill);
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            for (int y = surface; y >= surface - 3; y--)
            {
                Assert.True(Cell(gen, planet, chunks, sx, y, sz) == obsidian, $"cell ({sx},{y},{sz}) under the field is not obsidian");
            }

            Assert.NotEqual(obsidian, Cell(gen, planet, chunks, sx, surface - 4, sz));
            return;
        }

        Assert.Fail("no obsidian field on the sample grid in 40 seeds");
    }

    // ---------- lava flows ----------

    /// <summary>Lavafelder: on a tongue's axis the ground rises 1–3 blocks and is basalt three deep; on the
    /// core a cell in about eight is a 1-deep lava pocket that the body chain reports and the column fills.</summary>
    [Fact]
    public void LavaFlows_RiseFromTheConeFoot_WearBasalt_AndKeepGlowingPockets()
    {
        var planet = Content.Planets["lava"];
        var basalt = Block("basalt");
        var lava = Block("lava");
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            (int X, int Z)? axis = null;
            (int X, int Z)? pocket = null;
            int coreCells = 0;
            int sea = gen.SeaLevel(planet);
            foreach (var (x, z) in Grid(5, 5))
            {
                double core = gen.LavaFlowCoreForTest(planet, x, z);
                if (core <= 0.45 || gen.SurfaceHeight(planet, x, z) <= sea + 1)
                {
                    continue; // the body chain is only consulted above the sea (a lava sea on this world)
                }

                coreCells++;
                if (axis is null && core > 0.9)
                {
                    axis = (x, z);
                }

                if (pocket is null && gen.Gen1WaterForTest(planet, x, z) is { Fluid: "lava" } body
                    && body.Top == gen.SurfaceHeight(planet, x, z) && body.Bed == body.Top - 1)
                {
                    pocket = (x, z);
                }

                if (axis is not null && pocket is not null)
                {
                    break;
                }
            }

            if (axis is null)
            {
                continue;
            }

            var (ax, az) = axis.Value;
            double rise = gen.VolcanicOffsetForTest("lava-flow", planet, ax, az);
            Assert.True(rise >= 1.0 && rise <= 3.0, $"flow rise {rise} at ({ax},{az})");
            Assert.Equal(basalt, gen.LandmarkPaintForTest("lava-flow", planet, ax, az, out int fill));
            int surface = gen.SurfaceHeight(planet, ax, az);
            Assert.Equal(surface - 3, fill);
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            var top = Cell(gen, planet, chunks, ax, surface, az);
            Assert.True(top == basalt || top == lava, $"the flow's surface at ({ax},{az}) is {top}");

            Assert.True(pocket.HasValue, $"no lava pocket among {coreCells} core cells");
            var (px, pz) = pocket!.Value;
            int ps = gen.SurfaceHeight(planet, px, pz);
            var pocketCell = Cell(gen, planet, chunks, px, ps, pz);
            Assert.True(pocketCell == lava,
                $"pocket at ({px},{pz}) surface {ps} sea {sea}: cell {pocketCell}, below {Cell(gen, planet, chunks, px, ps - 1, pz)}, above {Cell(gen, planet, chunks, px, ps + 1, pz)}, "
                + $"water surface {(gen.TryGetWaterSurface(planet, px, pz, out int wt, out _) ? wt : int.MinValue)}, pond {gen.SurfacePondDepth(planet, px, pz)}, "
                + $"lava {gen.IsSurfaceLava(planet, px, pz)}, core {gen.LavaFlowCoreForTest(planet, px, pz)}, gen1 {gen.Gen1WaterForTest(planet, px, pz)}");
            var bedCell = Cell(gen, planet, chunks, px, ps - 1, pz);
            Assert.True(bedCell == basalt,
                $"pocket bed at ({px},{ps - 1},{pz}) is {bedCell} (cells {ps - 3}..{ps + 1}: {Cell(gen, planet, chunks, px, ps - 3, pz)} {Cell(gen, planet, chunks, px, ps - 2, pz)} {bedCell} {pocketCell} {Cell(gen, planet, chunks, px, ps + 1, pz)}); "
                + $"water surface {(gen.TryGetWaterSurface(planet, px, pz, out int wt2, out int sb2) ? $"{wt2}/{sb2}" : "none")}, river {gen.SurfaceRiverDepth(planet, px, pz)}, pond {gen.SurfacePondDepth(planet, px, pz)}, "
                + $"crater {gen.TryGetVolcanoCrater(planet, px, pz, out _)}, sea {sea}, paint {gen.LandmarkPaintForTest("lava-flow", planet, px, pz)}, "
                + $"tunnels {TunnelsAt(gen, planet, px, pz)}, cavern {gen.TryGetCavernSpan(planet, px, pz, out int cl, out int chh, out _)} {cl}..{chh}");
            return;
        }

        Assert.Fail("no lava tongue on the sample grid in 40 seeds");
    }

    // ---------- barchans ----------

    /// <summary>Sicheldünen: a desert that rolled no dune sea grows fields of crescents 4–9 high; a desert that
    /// did roll the dunes style has no barchan gate, since its crests would swallow them.</summary>
    [Fact]
    public void Barchans_GrowOnlyWhereNoDuneSeaWasRolled()
    {
        var planet = Content.Planets["desert"];
        bool sawDuneSea = false, sawField = false;
        for (long s = 1; s <= 60 && !(sawDuneSea && sawField); s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            var styles = gen.StylesForTest(planet);
            bool duneSea = styles.Contains("dunes") || styles.Contains("petrified-dunes");
            bool gate = gen.WonderGatesForTest(planet)["barchans"];
            Assert.True(gate == !duneSea, $"barchan gate {gate} with styles [{string.Join(",", styles)}]");
            if (duneSea)
            {
                sawDuneSea = true;
                continue;
            }

            double tallest = 0.0;
            foreach (var (x, z) in Grid(3, 3))
            {
                tallest = Math.Max(tallest, gen.VolcanicOffsetForTest("barchans", planet, x, z));
            }

            if (tallest > 0.0)
            {
                Assert.True(tallest >= 3.0 && tallest <= 9.5, $"tallest barchan {tallest}");
                sawField = true;
            }
        }

        Assert.True(sawField, "no barchan field on any sampled seed");
        Assert.True(sawDuneSea, "no dune-sea desert among the sampled seeds (the negative half of the gate is unproven)");
    }

    // ---------- frost polygons ----------

    /// <summary>Polygonböden: on the tundra the net's ridges stand one block proud with a stone skin, and a
    /// fifth of the plates hold a 1-deep pond the freeze pass covers with ice.</summary>
    [Fact]
    public void FrostPolygons_RaiseStoneRidges_AndFreezePlatePonds()
    {
        var planet = Content.Planets["tundra"];
        var stone = Block("stone");
        var ice = Block("ice");
        var water = Block("water");
        Assert.Equal((false, false), Gen(20260903, 1).FrostPolygonForTest(planet, 10, 10));
        for (long s = 1; s <= 30; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            (int X, int Z)? ridge = null, pond = null;
            foreach (var (x, z) in Grid(5, 5))
            {
                var net = gen.FrostPolygonForTest(planet, x, z);
                if (ridge is null && net.Ridge)
                {
                    ridge = (x, z);
                }

                if (pond is null && net.Pond && gen.Gen1WaterForTest(planet, x, z) is { Fluid: "water" } body
                    && body.Top == gen.SurfaceHeight(planet, x, z) && body.Bed == body.Top - 1
                    && gen.SurfaceHeight(planet, x, z) > gen.SeaLevel(planet) + 2)
                {
                    pond = (x, z);
                }

                if (ridge is not null && pond is not null)
                {
                    break;
                }
            }

            if (ridge is null || pond is null)
            {
                continue;
            }

            var (rx, rz) = ridge.Value;
            Assert.Equal(1.0, gen.VolcanicOffsetForTest("frost-polygons", planet, rx, rz));
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            Assert.Equal(stone, Cell(gen, planet, chunks, rx, gen.SurfaceHeight(planet, rx, rz), rz));

            var (px, pz) = pond.Value;
            int ps = gen.SurfaceHeight(planet, px, pz);
            var cell = Cell(gen, planet, chunks, px, ps, pz);
            Assert.True(cell == ice || cell == water, $"the plate pond at ({px},{pz}) is {cell}");
            Assert.True(gen.SurfaceIceThickness(planet, px, pz) >= 1 == (cell == ice), "ice thickness disagrees with the filled cell");
            return;
        }

        Assert.Fail("no tundra seed with both a ridge and a plate pond on the sample grid");
    }

    // ---------- gates ----------

    [Fact]
    public void Gates_VolcanicAndDesertFamilies_FromGenerationThreeOnly()
    {
        var g3 = Gen(20260903, 3);
        var g1 = Gen(20260903, 1);
        var lava = g3.WonderGatesForTest(Content.Planets["lava"]);
        Assert.True(lava["lavaFlows"] && lava["obsidianFields"], "the lava world flows and glasses");
        Assert.True(g3.WonderGatesForTest(Content.Planets["tundra"])["frostPolygons"], "the tundra patterns");
        Assert.False(g3.WonderGatesForTest(Content.Planets["boreal"])["frostPolygons"], "the boreal world is too warm");
        var highland = g3.WonderGatesForTest(Content.Planets["highland"]);
        Assert.False(highland["lavaFlows"] || highland["obsidianFields"] || highland["barchans"] || highland["frostPolygons"],
            "the highland control world must stay a world without any generation-3 family");
        foreach (var key in new[] { "lava", "tundra", "desert" })
        {
            var gates = g1.WonderGatesForTest(Content.Planets[key]);
            Assert.False(gates["lavaFlows"] || gates["obsidianFields"] || gates["barchans"] || gates["frostPolygons"], $"{key} on generation 1");
        }
    }
}
