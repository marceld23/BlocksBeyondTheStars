// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Terrain generation 3, part 1 — the foundation and its reference families. Each structural extension is
/// proven by the feature that consumes it: the paint fill by the glacier tongue, the sea-relative landmark
/// rows by the seamounts, the material bands by the icebergs, the sub-surface fluid spans by the underground
/// river reaches. Plus the two invariants the package rests on: the sea percentile never sees a sea-relative
/// row, and a generation-3 world without any active family is the generation-1 world, cell for cell.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // the cost guard below is a wall-clock ratio (#1735)
public sealed class LandformGen3Tests
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

    /// <summary>A world cell through the generator, with the chunks it touched memoised for the test.</summary>
    private sealed class World
    {
        private readonly WorldGenerator _gen;
        private readonly PlanetType _planet;
        private readonly Dictionary<ChunkCoord, ChunkData> _chunks = new();

        public World(WorldGenerator gen, PlanetType planet)
        {
            _gen = gen;
            _planet = planet;
        }

        public BlockId Cell(int x, int y, int z)
        {
            var coord = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                chunk = _gen.Generate(_planet, coord);
                _chunks[coord] = chunk;
            }

            const int cs = WorldConstants.ChunkSize;
            return chunk.Get(((x % cs) + cs) % cs, ((y % cs) + cs) % cs, ((z % cs) + cs) % cs);
        }
    }

    private static IEnumerable<(int X, int Z)> Grid(int stepX, int stepZ)
    {
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        for (int z = -period / 2; z < period / 2; z += stepZ)
            for (int x = 0; x < circ; x += stepX)
                yield return (x, z);
    }

    // ---------- S2: sea-relative rows / seamounts ----------

    [Fact]
    public void Seamounts_NeverChangeTheLandSeaPartition_TheCalibrationSaw()
    {
        var planet = Content.Planets["ocean"];
        var gen = Gen(424242, 3);
        int sea = gen.SeaLevel(planet);
        Assert.NotEqual(int.MinValue, sea);

        var sample = Grid(53, 47).ToArray();
        var withRows = sample.Select(c => gen.SurfaceHeight(planet, c.X, c.Z)).ToArray();
        int[] withoutRows;
        WorldGenerator.DisableSeaRowsForTest = true;
        try
        {
            gen.SetTerrainGeneration(3); // drops the column memos, so the heights are recomputed without the rows
            withoutRows = sample.Select(c => gen.SurfaceHeight(planet, c.X, c.Z)).ToArray();
        }
        finally
        {
            WorldGenerator.DisableSeaRowsForTest = false;
            gen.SetTerrainGeneration(3);
        }

        // The rows are invisible to the calibration by construction — and the SortedHeights they are not part of
        // is what the sea level came from. The partition test below is the one that could actually fail.
        Assert.Equal(sea, gen.SeaLevel(planet));

        int lifted = 0;
        for (int i = 0; i < withRows.Length; i++)
        {
            // Sea stays sea: a sea-relative row never makes land the calibration did not count (a seamount stays
            // under) — except the three that exist to do exactly that, on an allow-list (part 6: a causeway islet,
            // an atoll islet, an arch's stem). Land MAY become sea — that is a ria (part 5), a drowned valley.
            if (withoutRows[i] < sea && withRows[i] >= sea)
            {
                var (x, z) = sample[i];
                Assert.True(gen.SeaRowMakesLandForTest(planet, x, z), $"a sea-relative row made new land at ({x},{z}) that no island family claims");
                continue;
            }

            if (withRows[i] != withoutRows[i])
            {
                lifted++;
                if (withRows[i] < withoutRows[i])
                {
                    // A cut: a ria (part 5) drowning coast land, or a blue hole / lagoon bowl / canyon / trench
                    // (part 6) deepening the sea floor. Never below the lava-table safety line.
                    Assert.True(withRows[i] >= planet.BaseHeight - 150, "a sea-relative row cut below the floor cap");
                    continue; // the lift rules below are not its rules
                }

                // A lifted sea column never reaches the sea line: a seamount keeps three of clearance, a reef rim,
                // a causeway bar or a blue hole's lip stops at one below (part 6).
                Assert.True(withRows[i] <= sea - 1, $"a sea-relative row lifted a column to {withRows[i]} at sea {sea}");
                Assert.True(withoutRows[i] <= sea - 2, "a sea-relative row rose where the sea did not own the column");
            }
        }

        Assert.True(lifted > 0, "no seamount was found in the sample — the family never fired on an ocean world");
    }

    [Fact]
    public void Seamounts_AreAbsent_BelowGenerationThree_AndOnDryWorlds()
    {
        foreach (var (key, generation) in new[] { ("ocean", 1), ("ocean", 2), ("desert", 3), ("lava", 3) })
        {
            var planet = Content.Planets[key];
            var gen = Gen(424242, generation);
            foreach (var (x, z) in Grid(97, 89))
            {
                Assert.False(gen.SeamountCellForTest(planet, x, z).Has, $"a seamount on {key} at generation {generation}");
            }
        }
    }

    // ---------- S3: material bands / icebergs ----------

    [Theory]
    [InlineData("frozen_ocean", 20260903)]
    [InlineData("tundra", 424242)]
    public void Icebergs_StandInColdDeepWater_AsIceBands(string key, long seed)
    {
        var planet = Content.Planets[key];
        var gen = Gen(seed, 3);
        int sea = gen.SeaLevel(planet);
        var ice = Content.GetBlock("ice")!.NumericId;
        var world = new World(gen, planet);
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];

        int found = 0;
        foreach (var (x, z) in Grid(23, 19))
        {
            int n = gen.GetExtraBands(planet, x, z, bands);
            for (int b = 0; b < n; b++)
            {
                if (bands[b].Kind != WorldGenerator.BandKind.Ice)
                {
                    continue;
                }

                found++;
                int ground = gen.SurfaceHeight(planet, x, z);
                Assert.True(bands[b].Bottom >= ground + 2, "a berg is grounded — the sea must stay below it");
                Assert.True(bands[b].Top <= sea + 8, "a berg towers more than eight over the waterline");
                Assert.True(ground <= sea - 3, "a berg stands in shallow water");
                if (found <= 12)
                {
                    for (int y = bands[b].Bottom; y <= bands[b].Top; y++)
                    {
                        var cell = world.Cell(x, y, z);
                        Assert.True(cell == ice, $"berg cell ({x},{y},{z}) is {cell}, band [{bands[b].Bottom},{bands[b].Top}], sea {sea}, ground {ground}");
                    }

                    var above = world.Cell(x, bands[b].Top + 1, z);
                    Assert.True(above == BlockId.Air, $"above the berg at ({x},{bands[b].Top + 1},{z}) is {above}");
                    var below = world.Cell(x, bands[b].Bottom - 1, z);
                    Assert.True(below != BlockId.Air, $"under the berg at ({x},{bands[b].Bottom - 1},{z}) is air"); // the sea (or its ice lid)
                }

                // The band never moves the sea's own answer: the column is still a water column underneath.
                Assert.True(gen.IsSurfaceWater(planet, x, z) || gen.SurfaceIceThickness(planet, x, z) > 0);
            }
        }

        Assert.True(found > 0, $"no iceberg band on {key}");
    }

    [Fact]
    public void Icebergs_AreAbsent_OnWarmWorlds_AndBelowGenerationThree()
    {
        Span<WorldGenerator.ColumnBand> bands = stackalloc WorldGenerator.ColumnBand[WorldGenerator.MaxColumnBands];
        foreach (var (key, generation) in new[] { ("ocean", 3), ("frozen_ocean", 1), ("frozen_ocean", 2) })
        {
            var planet = Content.Planets[key];
            var gen = Gen(20260903, generation);
            foreach (var (x, z) in Grid(41, 37))
            {
                int n = gen.GetExtraBands(planet, x, z, bands);
                for (int b = 0; b < n; b++)
                {
                    Assert.NotEqual(WorldGenerator.BandKind.Ice, bands[b].Kind);
                    Assert.NotEqual(WorldGenerator.BandKind.Fluid, bands[b].Kind);
                }
            }
        }
    }

    // ---------- S1: paint fill / glacier tongue ----------

    [Fact]
    public void GlacierTongue_OnGenerationThree_IsIceSixDeep()
    {
        // Massifs are a rare per-body roll (like LandscapeLandmarksTests, walk seeds until a tongue exists).
        var planet = Content.Planets["ice"];
        var ice = Content.GetBlock("ice")!.NumericId;
        for (long s = 1; s <= 120; s++)
        {
            long seed = s * 6151 + 3;
            var gen3 = Gen(seed, 3);
            if (!gen3.LandmarkOrderForTest(planet).Contains("glacier-tongue"))
            {
                continue;
            }

            var gen1 = Gen(seed, 1);
            var world = new World(gen3, planet);
            int checkedColumns = 0;
            foreach (var (x, z) in Grid(12, 12))
            {
                var painted = gen3.LandmarkPaintForTest("glacier-tongue", planet, x, z, out int fill);
                if (painted is null)
                {
                    continue;
                }

                int surface = gen3.SurfaceHeight(planet, x, z);
                Assert.Equal(ice, painted.Value);
                Assert.Equal(surface - 6, fill);

                // Generation 1 paints the same tongue, but only the topsoil.
                Assert.Equal(ice, gen1.LandmarkPaintForTest("glacier-tongue", planet, x, z, out int fill1));
                Assert.Equal(int.MinValue, fill1);

                if (checkedColumns++ < 24)
                {
                    for (int y = surface; y >= surface - 6; y--)
                    {
                        var cell = world.Cell(x, y, z);
                        // Solid cells of the fill are ice; a cave or tunnel may still have carved one away.
                        Assert.True(cell == ice || cell == BlockId.Air, $"{cell} inside the glacier fill at ({x},{y},{z})");
                    }
                }
            }

            if (checkedColumns > 0)
            {
                return;
            }
        }

        Assert.Fail("no glacier tongue found on an ice world in 120 seeds");
    }

    // ---------- S4/S5: sub-surface fluid spans / underground rivers ----------

    [Fact]
    public void UndergroundRiver_IsSealed_WalkableBesideTheWater_AndInvisibleToTheSurfaceQueries()
    {
        var planet = Content.Planets["karst"];
        var gen = Gen(20260903, 3);
        var gen1 = Gen(20260903, 1);
        var water = Content.GetBlock("water")!.NumericId;
        var world = new World(gen, planet);

        var field = gen.RiverFieldFor(planet);
        var sunk = field.ColumnsByPosition.Where(kv => kv.Value.Underground).ToList();
        Assert.True(sunk.Count > 0, "no underground reach on a generation-3 karst world");
        Assert.DoesNotContain(gen1.RiverFieldFor(planet).ColumnsByPosition, kv => kv.Value.Underground);

        int inspected = 0;
        foreach (var (pos, col) in sunk)
        {
            // The surface is untouched and the surface queries know nothing of the reach: a column reads as
            // water only when a surface body of its own (the sea, a pond, a generation-1 sheet) sits there.
            int surfaceY = gen.SurfaceHeight(planet, pos.X, pos.Z);
            bool claimed = surfaceY <= gen.SeaLevel(planet) || gen.SurfacePondDepth(planet, pos.X, pos.Z) > 0; // the column phase's precedence
            bool classicBody = surfaceY + 1 <= gen.SeaLevel(planet) || gen.SurfacePondDepth(planet, pos.X, pos.Z) > 0;
            bool anyBody = classicBody || gen.SurfaceGen1WaterDepth(planet, pos.X, pos.Z) > 0;
            // IsSurfaceWater (the landing gate) has never counted the generation-1 sheets; TryGetWaterSurface does.
            Assert.True(gen.IsSurfaceWater(planet, pos.X, pos.Z) == classicBody, $"IsSurfaceWater at {pos} disagrees with the surface bodies");
            Assert.True(gen.TryGetWaterSurface(planet, pos.X, pos.Z, out _, out _) == anyBody, $"TryGetWaterSurface at {pos} disagrees with the surface bodies");
            Assert.Equal(0, gen.SurfaceRiverDepth(planet, pos.X, pos.Z));

            bool reported = gen.TryGetUndergroundRiver(planet, pos.X, pos.Z, out int top, out int bed, out int roof);
            Assert.True(reported == (!col.IsBank && !claimed), $"TryGetUndergroundRiver at {pos}: reported={reported} bank={col.IsBank} claimed={claimed}");
            if (!reported || col.Mouth || inspected >= 40)
            {
                continue;
            }

            inspected++;
            int surface = gen.SurfaceHeight(planet, pos.X, pos.Z);
            Assert.True(roof < surface, "a non-mouth passage breaks the surface");
            Assert.NotEqual(BlockId.Air, world.Cell(pos.X, surface, pos.Z)); // the ground over it is intact
            Assert.True(top < roof,
                $"no headroom above the water at {pos}: top {top} bed {bed} roof {roof} surface {surface}; column water {col.WaterSurfaceY} bed {col.BedY} roof {col.RoofY} mouth {col.Mouth}; "
                + $"gen-1 surface {gen1.SurfaceHeight(planet, pos.X, pos.Z)}, floodplain {field.IsFloodplain(pos.X, pos.Z)}, pooled {field.TryGetPooled(pos.X, pos.Z, out _)}");
            for (int y = bed + 1; y <= top; y++)
            {
                var cell = world.Cell(pos.X, y, pos.Z);
                Assert.True(cell == water, $"passage water at ({pos.X},{y},{pos.Z}) is {cell}");
                // Sealed sideways: what stands beside the water at its own height is water or rock — air only
                // where the neighbour is the passage itself one step further down (a river steps downhill,
                // exactly as a surface reach does beside its lower neighbour).
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var side = world.Cell(pos.X + dx, y, pos.Z + dz);
                    if (side == BlockId.Air)
                    {
                        Assert.True(field.TryGet(pos.X + dx, pos.Z + dz, out var next) && next.Underground,
                            $"air beside the water at ({pos.X + dx},{y},{pos.Z + dz}) outside the passage");
                    }
                }
            }

            var bedCell = world.Cell(pos.X, bed, pos.Z);
            Assert.True(bedCell != BlockId.Air && bedCell != water, $"the bed at ({pos.X},{bed},{pos.Z}) is {bedCell}");
            for (int y = top + 1; y <= roof; y++)
            {
                var cell = world.Cell(pos.X, y, pos.Z);
                Assert.True(cell == BlockId.Air, $"passage air at ({pos.X},{y},{pos.Z}) is {cell}"); // the passage
            }
        }

        Assert.True(inspected > 0, "every underground column was a bank or a mouth");
    }

    // The "generation 3 without an active family IS generation 1" control lives in LandformGen3RockTests —
    // it searches for a world on which every generation-3 gate is false instead of assuming one (highland
    // grows arêtes since part 2).

    // ---------- the cost guard ----------

    /// <summary>No generation-time budget existed before this package; the client bakes up to 32 768 columns
    /// synchronously on its main thread, so a chunk must not get meaningfully dearer. Slow tier: main only.
    ///
    /// <para>A wall-clock ratio measures the scheduler as much as the code, and this one measured the
    /// scheduler on the v2026.9.5 release gate: it read "generation 3 took 2688 ms against 144 ms" and took
    /// the release run down, while the SAME commit passed on main with the whole test billed at 0.96 s. CPU
    /// contention from the parallel suite, not a regression — the guard is tight in absolute terms because
    /// generation 1 is only ~100 ms, so the 250 ms constant is the entire cushion and one preemption spends
    /// it. Two defences, both needed: the class runs in <see cref="RealTimeSensitiveCollection"/> so no other
    /// collection competes for the cores, and each generation is measured three times with the FASTEST run
    /// kept. A minimum is the sample least contaminated by preemption — a stall can only inflate a run,
    /// never shorten one — whereas a single measurement is poisoned by the first stall that lands on it.
    /// Keep both: the sequential island alone still leaves the runner's own noise inside a 250 ms budget.</para></summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void GenerationThree_ChunkCost_StaysNearGenerationOne()
    {
        var planet = Content.Planets["ocean"];
        long Measure(int generation)
        {
            var gen = Gen(424242, generation);
            gen.Generate(planet, new ChunkCoord(0, WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, 0, 0)), 0)); // warm-up
            var sw = Stopwatch.StartNew();
            for (int cx = 0; cx < 4; cx++)
                for (int cz = 0; cz < 4; cz++)
                {
                    int cy = WorldConstants.WorldToChunk(gen.SurfaceHeight(planet, cx * 16, cz * 16));
                    gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                    gen.Generate(planet, new ChunkCoord(cx, cy - 1, cz));
                }

            return sw.ElapsedMilliseconds;
        }

        // Interleaved so a slow patch of the runner cannot land on one generation alone, and each round
        // builds a fresh generator, so no round is cheapened by the previous one's memos.
        long t1 = long.MaxValue, t3 = long.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            t1 = Math.Min(t1, Measure(1));
            t3 = Math.Min(t3, Measure(3));
        }

        Assert.True(t3 <= 1.3 * t1 + 250,
            $"generation 3 took {t3} ms against {t1} ms for generation 1 (fastest of 3 rounds each)");
    }
}
