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
/// Terrain generation 3, part 6 — the coast and the sea floor: sea arches, blowholes, causeway islands, lagoons
/// and atolls (reef rings), reef fields, blue holes, submarine canyons and trenches. Every one is sea-relative
/// and absent below generation 3.
/// </summary>
public sealed class LandformGen3CoastTests
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

    /// <summary>True when the sea-relative row's offset is what the column's height shows — i.e. no classic
    /// landmark row (a massif, a cone) owns the column; those always win over a sea-relative row.</summary>
    private static bool RowOwns(WorldGenerator gen, PlanetType planet, int x, int z, double offset)
        => gen.SurfaceHeight(planet, x, z) == gen.RawSurfaceHeightForTest(planet, x, z) + (int)Math.Round(offset);

    // ---------- reef rings: lagoons and atolls ----------

    /// <summary>A lagoon's rim stands at one below the sea and is coral rock; its interior lies deeper than the
    /// rim; an atoll carries sand islets above the sea on its rim.</summary>
    [Fact]
    public void ReefRings_LagoonRimIsCoral_InteriorDeeper_AtollIsletsStandOut()
    {
        var coral = Block("coral_rock");
        var sand = Block("sand");
        bool sawLagoon = false, sawAtoll = false;
        for (long s = 1; s <= 40 && !(sawLagoon && sawAtoll); s++)
        {
            var planet = Content.Planets["archipelago"];
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            foreach (var (x, z) in Grid(5, 5))
            {
                if (gen.ReefRingForTest(planet, x, z) is not { } ring
                    || !RowOwns(gen, planet, x, z, gen.CoastOffsetForTest("reef-ring", planet, x, z)))
                {
                    continue;
                }

                int h = gen.SurfaceHeight(planet, x, z);
                if (ring.Rim)
                {
                    Assert.Equal(sea - 1, h);
                    Assert.Equal(coral, gen.LandmarkPaintForTest("reef-ring", planet, x, z));
                    if (!ring.Atoll)
                    {
                        sawLagoon = true;
                    }
                }
                else if (ring.Islet)
                {
                    // The islet is a sand dome: its edge sits at the rim's one-below, its crown above the sea.
                    Assert.True(h >= sea - 1, $"an atoll islet at ({x},{z}) lies at {h} under sea {sea}");
                    Assert.Equal(sand, gen.LandmarkPaintForTest("reef-ring", planet, x, z));
                    if (h >= sea + 1)
                    {
                        Assert.True(gen.SeaRowMakesLandForTest(planet, x, z), "an islet crown is not on the new-land allow-list");
                        sawAtoll = true;
                    }
                }
                else
                {
                    Assert.True(h <= sea - 3, $"a ring interior at ({x},{z}) is only {sea - h} below the sea");
                }
            }
        }

        Assert.True(sawLagoon, "no lagoon rim on the sample grid");
        Assert.True(sawAtoll, "no atoll islet on the sample grid");
    }

    // ---------- reef fields ----------

    /// <summary>A reef field's floor is coral rock three deep, never higher than two below the sea, and grows
    /// coral flora above it.</summary>
    [Fact]
    public void ReefFields_CoralRockFloor_UnderShallowSea()
    {
        var coral = Block("coral_rock");
        var planet = Content.Planets["archipelago"];
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            foreach (var (x, z) in Grid(7, 7))
            {
                double off = gen.CoastOffsetForTest("reef-field", planet, x, z);
                if (off <= 0.0 || !RowOwns(gen, planet, x, z, off) || gen.LandmarkPaintForTest("reef-field", planet, x, z) != coral)
                {
                    continue;
                }

                int h = gen.SurfaceHeight(planet, x, z);
                Assert.True(h <= sea - 2, "the reef rises above two below the sea");
                var chunks = new Dictionary<ChunkCoord, ChunkData>();
                for (int y = h; y >= h - 3; y--)
                {
                    Assert.True(Cell(gen, planet, chunks, x, y, z) == coral, $"reef cell ({x},{y},{z}) is not coral rock");
                }

                return;
            }
        }

        Assert.Fail("no reef field on the sample grid in 40 seeds");
    }

    // ---------- causeway islands ----------

    /// <summary>A causeway islet stands above the sea, and its sandbar to the coast lies exactly one below the
    /// sea — wadable, never dry.</summary>
    [Fact]
    public void CausewayIslands_IsletAboveTheSea_SandbarOneBelow()
    {
        var planet = Content.Planets["ocean"];
        for (long s = 1; s <= 40; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            bool islet = false, bar = false;
            foreach (var (x, z) in Grid(4, 4))
            {
                double off = gen.CoastOffsetForTest("causeway-island", planet, x, z);
                if (off <= 0.0 || !RowOwns(gen, planet, x, z, off))
                {
                    continue;
                }

                int h = gen.SurfaceHeight(planet, x, z);
                if (h >= sea)
                {
                    islet = true;
                    Assert.True(gen.SeaRowMakesLandForTest(planet, x, z), "an islet column is not on the new-land allow-list");
                }
                else if (h == sea - 1)
                {
                    bar = true;
                }

                Assert.True(h <= sea + 5, $"causeway ground {h} at ({x},{z}) is too high over sea {sea}");
            }

            if (islet && bar)
            {
                return;
            }
        }

        Assert.Fail("no causeway island with both an islet and a sandbar in 40 seeds");
    }

    // ---------- sea arches ----------

    /// <summary>An arch's bar is a rock slab above the water from the cliff to the stem; the stem is rock from
    /// the sea floor up to the bar; the cells under the bar between the two are air or water.</summary>
    [Fact]
    public void SeaArches_BarSpansFromCliffToStem_OverWater()
    {
        var planet = Content.Planets["ocean"];
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            (int X, int Z, int Bottom, int Top)? bar = null;
            foreach (var (x, z) in Grid(3, 3))
            {
                if (gen.SeaArchBandForTest(planet, x, z) is { } b && gen.SurfaceHeight(planet, x, z) < b.Bottom - 2)
                {
                    bar = (x, z, b.Bottom, b.Top);
                    break;
                }
            }

            if (bar is null)
            {
                continue;
            }

            var (bx, bz, bottom, top) = bar.Value;
            Assert.True(bottom > sea, "the bar dips into the sea");
            var chunks = new Dictionary<ChunkCoord, ChunkData>();
            for (int y = bottom; y <= top; y++)
            {
                Assert.False(Cell(gen, planet, chunks, bx, y, bz).IsAir, $"bar cell ({bx},{y},{bz}) is air");
            }

            var under = Cell(gen, planet, chunks, bx, bottom - 1, bz);
            Assert.True(under.IsAir || under == Block("water"), $"under the bar at ({bx},{bottom - 1},{bz}) is {under}");
            return;
        }

        Assert.Fail("no sea arch bar over open water in 60 seeds");
    }

    // ---------- blowholes ----------

    /// <summary>A blowhole is a vent block on a cliff top with a sealed water shaft under it down to the sea line.</summary>
    [Fact]
    public void Blowholes_VentOnTheCliff_WaterShaftBelow()
    {
        var planet = Content.Planets["ocean"];
        var vent = Block("geyser_vent");
        var water = Block("water");
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            int sea = gen.SeaLevel(planet);
            foreach (var (x, z) in Grid(3, 3))
            {
                if (!gen.BlowholeForTest(planet, x, z))
                {
                    continue;
                }

                int h = gen.SurfaceHeight(planet, x, z);
                if (h <= sea + 2 || gen.SurfacePondDepth(planet, x, z) > 0 || gen.RiverFieldFor(planet).TryGet(x, z, out _))
                {
                    continue;
                }

                var chunks = new Dictionary<ChunkCoord, ChunkData>();
                Assert.Equal(vent, Cell(gen, planet, chunks, x, h, z));
                Assert.Equal(water, Cell(gen, planet, chunks, x, h - 1, z));
                Assert.Equal(water, Cell(gen, planet, chunks, x, sea + 1, z));
                Assert.False(Cell(gen, planet, chunks, x, sea, z).IsAir, "the shaft has no floor at the sea line");
                return;
            }
        }

        Assert.Fail("no blowhole vent on the sample grid in 60 seeds");
    }

    // ---------- blue holes, canyons, trenches ----------

    /// <summary>The three cuts only ever deepen the sea floor: a blue hole to 40–70 below the sea inside a lip
    /// ring, a submarine canyon and a trench below the surrounding floor — never a column of land, never
    /// below the lava-table safety line.</summary>
    [Fact]
    public void SeaFloorCuts_OnlyDeepenTheSea_AndRespectTheFloorCap()
    {
        var planet = Content.Planets["ocean"];
        bool hole = false, canyon = false, trench = false;
        for (long s = 1; s <= 40 && !(hole && canyon && trench); s++)
        {
            var gen = Gen(s * 6151 + 3, 3);
            var g1 = Gen(s * 6151 + 3, 1);
            int sea = gen.SeaLevel(planet);
            foreach (var (x, z) in Grid(6, 6))
            {
                foreach (var row in new[] { "blue-hole", "submarine-canyon", "trench" })
                {
                    double off = gen.CoastOffsetForTest(row, planet, x, z);
                    if (off == 0.0 || !RowOwns(gen, planet, x, z, off))
                    {
                        continue;
                    }

                    Assert.Equal(0.0, g1.CoastOffsetForTest(row, planet, x, z));
                    int h = gen.SurfaceHeight(planet, x, z);
                    Assert.True(h < sea, $"{row} at ({x},{z}) left ground at {h} over sea {sea}");
                    Assert.True(h >= planet.BaseHeight - 150, $"{row} cut below the floor cap at ({x},{z})");
                    if (off < 0.0)
                    {
                        switch (row)
                        {
                            case "blue-hole": hole = true; break;
                            case "submarine-canyon": canyon = true; break;
                            default: trench = true; break;
                        }
                    }
                }
            }
        }

        Assert.True(hole, "no blue hole cut in 40 seeds");
        Assert.True(canyon, "no submarine canyon cut in 40 seeds");
        Assert.True(trench, "no trench cut in 40 seeds");
    }

    // ---------- gates ----------

    [Fact]
    public void Gates_CoastFamilies_FromGenerationThreeOnly()
    {
        var g3 = Gen(20260903, 3);
        var g1 = Gen(20260903, 1);
        var arch = g3.WonderGatesForTest(Content.Planets["archipelago"]);
        Assert.True(arch["reefRings"] && arch["reefFields"] && arch["blueHoles"] && arch["causewayIslands"] && arch["seaArches"] && arch["blowholes"], "the archipelago has the warm-coast families");
        var ocean = g3.WonderGatesForTest(Content.Planets["ocean"]);
        Assert.True(ocean["trenches"] && ocean["submarineCanyons"] && ocean["reefRings"], "the ocean world has the deep-sea families and warm reefs");
        var tundra = g3.WonderGatesForTest(Content.Planets["tundra"]);
        Assert.False(tundra["reefRings"] || tundra["reefFields"], "the tundra is too cold for reefs");
        var desert = g3.WonderGatesForTest(Content.Planets["desert"]);
        Assert.False(desert["seaArches"] || desert["causewayIslands"] || desert["trenches"] || desert["blueHoles"], "the desert has no coast");
        foreach (var key in new[] { "archipelago", "ocean" })
        {
            var gates = g1.WonderGatesForTest(Content.Planets[key]);
            Assert.False(gates["seaArches"] || gates["reefRings"] || gates["trenches"] || gates["causewayIslands"], $"{key} on generation 1");
        }
    }
}
