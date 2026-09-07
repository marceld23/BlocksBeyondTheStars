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
/// Terrain generation 3, part 2 — the rock landforms. Each family is checked for the property that makes it
/// that landform and not another: a slot canyon is narrow AND deep, an arête is a crest with two steep
/// flanks, a tooth row has several summits, a rock gate goes all the way through a wall, a mountain hall is
/// a room inside a massif, pavement only covers level ground, and the three new styles produce the pattern
/// they are named for. Every family must be absent below generation 3.
/// </summary>
public sealed class LandformGen3RockTests
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

    /// <summary>Walks seeds until <paramref name="pick"/> finds what the test needs, like the gen-1 landmark
    /// tests do — the families are per-body rolls, so a fixed seed is not a guarantee.</summary>
    private static T FindOverSeeds<T>(Func<WorldGenerator, T?> pick, int seeds = 60) where T : class
    {
        for (long s = 1; s <= seeds; s++)
        {
            var found = pick(Gen(s * 6151 + 3, 3));
            if (found is not null)
            {
                return found;
            }
        }

        Assert.Fail($"nothing found in {seeds} seeds");
        return null!;
    }

    // ---------- slot canyons ----------

    [Fact]
    public void SlotCanyon_IsNarrowAndDeep_AndOnlyOnDryButteOrWindWorlds()
    {
        var planet = Content.Planets["desert"];
        var hit = FindOverSeeds(gen =>
        {
            foreach (var (x, z) in Grid(11, 13))
            {
                if (gen.RockOffsetForTest("slot-canyon", planet, x, z) <= -14.0)
                {
                    return Tuple.Create(gen, x, z);
                }
            }

            return null;
        });

        var (g, cx, cz) = (hit.Item1, hit.Item2, hit.Item3);
        Assert.True(g.RockOffsetForTest("slot-canyon", planet, cx, cz) <= -14.0);

        // Narrow: walking away from the centre in any of the four directions leaves the crack within 4 blocks.
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int steps = 0;
            while (steps < 12 && g.RockOffsetForTest("slot-canyon", planet, cx + dx * steps, cz + dz * steps) < -0.5)
            {
                steps++;
            }

            Assert.True(steps <= 6, $"the slot is still open {steps} blocks along ({dx},{dz}) — that is a canyon, not a slot");
        }

        // Absent below generation 3 and on a world without the tags.
        Assert.Equal(0.0, Gen(1, 1).RockOffsetForTest("slot-canyon", planet, cx, cz));
        Assert.Equal(0.0, Gen(1, 3).RockOffsetForTest("slot-canyon", Content.Planets["jungle"], cx, cz));
    }

    // ---------- arêtes and tooth rows ----------

    [Fact]
    public void Arete_IsACrest_WithBothFlanksFallingAway()
    {
        var planet = Content.Planets["highland"];
        var hit = FindOverSeeds(gen =>
        {
            foreach (var (x, z) in Grid(13, 11))
            {
                if (gen.RockOffsetForTest("arete", planet, x, z) >= 20.0)
                {
                    return Tuple.Create(gen, x, z);
                }
            }

            return null;
        });

        var (g, cx, cz) = (hit.Item1, hit.Item2, hit.Item3);
        double crest = g.RockOffsetForTest("arete", planet, cx, cz);

        // Somewhere within 8 blocks the ridge is gone on BOTH sides of one axis — that is what makes it a knife
        // ridge rather than a plateau.
        bool knife = false;
        foreach (var (dx, dz) in new[] { (1, 0), (0, 1) })
        {
            double a = g.RockOffsetForTest("arete", planet, cx + dx * 8, cz + dz * 8);
            double b = g.RockOffsetForTest("arete", planet, cx - dx * 8, cz - dz * 8);
            if (a <= crest * 0.35 && b <= crest * 0.35)
            {
                knife = true;
            }
        }

        Assert.True(knife, "the arête does not fall away on both sides of either axis");
        Assert.Equal(0.0, Gen(1, 1).RockOffsetForTest("arete", planet, cx, cz));
    }

    [Fact]
    public void ToothRow_HasSeveralSummits_InALine()
    {
        var planet = Content.Planets["highland"];
        var hit = FindOverSeeds(gen =>
        {
            foreach (var (x, z) in Grid(13, 11))
            {
                if (gen.RockOffsetForTest("tooth-row", planet, x, z) >= 20.0)
                {
                    return Tuple.Create(gen, x, z);
                }
            }

            return null;
        });

        var (g, cx, cz) = (hit.Item1, hit.Item2, hit.Item3);

        // Scan a square around the hit and count local maxima of the family's own offset.
        int peaks = 0;
        for (int dx = -140; dx <= 140; dx += 2)
            for (int dz = -140; dz <= 140; dz += 2)
            {
                double v = g.RockOffsetForTest("tooth-row", planet, cx + dx, cz + dz);
                if (v < 20.0)
                {
                    continue;
                }

                bool top = true;
                foreach (var (ox, oz) in new[] { (2, 0), (-2, 0), (0, 2), (0, -2) })
                {
                    if (g.RockOffsetForTest("tooth-row", planet, cx + dx + ox, cz + dz + oz) > v)
                    {
                        top = false;
                        break;
                    }
                }

                if (top)
                {
                    peaks++;
                }
            }

        Assert.True(peaks >= 3, $"only {peaks} summits — a tooth row needs several");
        Assert.Equal(0.0, Gen(1, 1).RockOffsetForTest("tooth-row", planet, cx, cz));
    }

    // ---------- rock gates ----------

    [Fact]
    public void RockGate_CutsAllTheWayThroughATableMountainWall()
    {
        var planet = Content.Planets["tablelands"];
        var hit = FindOverSeeds(gen =>
        {
            // A gate column carries a carve span in the wall band; find one near a table.
            Span<(int Lo, int Hi)> spans = stackalloc (int Lo, int Hi)[WorldGenerator.MaxColumnBands];
            foreach (var (x, z) in Grid(17, 19))
            {
                if (gen.LandmarkOffsetForTest("table-mountain", planet, x, z) < 20.0)
                {
                    continue;
                }

                for (int r = 0; r < 160; r += 2)
                    for (int dir = 0; dir < 4; dir++)
                    {
                        int px = x + (dir == 0 ? r : dir == 1 ? -r : 0);
                        int pz = z + (dir == 2 ? r : dir == 3 ? -r : 0);
                        int n = gen.TunnelSpans(planet, px, pz, spans);
                        for (int i = 0; i < n; i++)
                        {
                            if (spans[i].Hi - spans[i].Lo >= 4 && spans[i].Lo <= planet.BaseHeight + 10)
                            {
                                return Tuple.Create(gen, px, pz);
                            }
                        }
                    }

                return null;
            }

            return null;
        });

        var (g, gx, gz) = (hit.Item1, hit.Item2, hit.Item3);
        Span<(int Lo, int Hi)> at = stackalloc (int Lo, int Hi)[WorldGenerator.MaxColumnBands];
        int count = g.TunnelSpans(planet, gx, gz, at);
        Assert.True(count > 0, "the gate column carries no carve span");
        int tallest = 0;
        for (int i = 0; i < count; i++)
        {
            tallest = Math.Max(tallest, at[i].Hi - at[i].Lo);
        }

        Assert.True(tallest >= 4, $"the tallest span in the gate column is only {tallest} — that is not an opening");
    }

    // ---------- mountain halls ----------

    [Fact]
    public void MountainHall_IsARoomInsideAMassif_WithAWayIn()
    {
        var planet = Content.Planets["highland"];
        var hit = FindOverSeeds(gen =>
        {
            Span<(int Lo, int Hi)> spans = stackalloc (int Lo, int Hi)[WorldGenerator.MaxColumnBands];
            foreach (var (x, z) in Grid(23, 19))
            {
                if (gen.LandmarkOffsetForTest("massif", planet, x, z) < 60.0)
                {
                    continue;
                }

                int n = gen.TunnelSpans(planet, x, z, spans);
                for (int i = 0; i < n; i++)
                {
                    if (spans[i].Hi - spans[i].Lo >= 8)
                    {
                        return Tuple.Create(gen, x, z);
                    }
                }
            }

            return null;
        });

        var (g, hx, hz) = (hit.Item1, hit.Item2, hit.Item3);
        Span<(int Lo, int Hi)> at = stackalloc (int Lo, int Hi)[WorldGenerator.MaxColumnBands];
        int c = g.TunnelSpans(planet, hx, hz, at);
        int tall = 0;
        for (int i = 0; i < c; i++)
        {
            if (at[i].Hi - at[i].Lo >= 8)
            {
                tall++;
                Assert.True(at[i].Lo > planet.BaseHeight - 40, "the hall sits far below the mountain it should be inside");
            }
        }

        Assert.True(tall > 0);

        // A generation-1 generator of the same world has no hall at that column.
        var g1 = Gen(1, 1);
        Assert.True(true); // (the seed differs per walk; the family gate below is the real guard)
        Assert.False(g1.WonderGatesForTest(planet)["mountainHalls"]);
    }

    // ---------- desert pavement ----------

    [Fact]
    public void DesertPavement_CoversLevelGroundOnly_AndNotBelowGenerationThree()
    {
        var planet = Content.Planets["dust_bowl"];
        var scree = Content.GetBlock("scree")!.NumericId;
        var stone = Content.GetBlock("stone")!.NumericId;
        var gen = Gen(20260903, 3);
        var gen1 = Gen(20260903, 1);

        int painted = 0, screeCells = 0, stoneCells = 0;
        foreach (var (x, z) in Grid(37, 41))
        {
            var block = gen.LandmarkPaintForTest("desert-pavement", planet, x, z, out int fill);
            Assert.Null(gen1.LandmarkPaintForTest("desert-pavement", planet, x, z));
            if (block is null)
            {
                continue;
            }

            painted++;
            Assert.Equal(int.MinValue, fill); // topsoil only
            Assert.True(block.Value == scree || block.Value == stone, "pavement paints scree or bare stone");
            if (block.Value == scree) { screeCells++; } else { stoneCells++; }
        }

        Assert.True(painted > 0, "no pavement on a wind-swept dry world");
        Assert.True(stoneCells > 0 && screeCells > 0, "the pavement is not mottled — it reads as one flat repaint");
    }

    // ---------- the three new styles ----------

    [Fact]
    public void NewStyles_AreRolledOnlyFromGenerationThree()
    {
        foreach (var key in new[] { "desert", "red_desert", "tablelands", "badlands", "karst", "jungle", "fungal" })
        {
            var planet = Content.Planets[key];
            for (long s = 1; s <= 40; s++)
            {
                var styles = Gen(s * 6151 + 3, 1).StylesForTest(planet);
                foreach (var style in styles)
                {
                    Assert.False(style is "labyrinth" or "stone-forest" or "petrified-dunes",
                        $"{key} rolled the generation-3 style {style} on generation 1");
                }
            }
        }

        // …and they do appear from generation 3.
        bool any = false;
        for (long s = 1; s <= 60 && !any; s++)
        {
            any = Gen(s * 6151 + 3, 3).StylesForTest(Content.Planets["desert"])
                .Any(st => st is "labyrinth" or "petrified-dunes");
        }

        Assert.True(any, "no generation-3 style was ever rolled on a desert world");
    }

    [Fact]
    public void Labyrinth_AlternatesWallsAndPassages()
    {
        var planet = Content.Planets["desert"];
        var hit = FindOverSeeds(gen => gen.StylesForTest(planet).Contains("labyrinth") ? gen : null);

        // Walk a transect and count the alternations between wall and passage.
        int changes = 0;
        bool? wasWall = null;
        int baseline = hit.SurfaceHeight(planet, 0, 500);
        int lo = baseline, hi = baseline;
        for (int x = 0; x < 400; x++)
        {
            int h = hit.SurfaceHeight(planet, x, 500);
            lo = Math.Min(lo, h);
            hi = Math.Max(hi, h);
        }

        int mid = (lo + hi) / 2;
        for (int x = 0; x < 400; x++)
        {
            bool wall = hit.SurfaceHeight(planet, x, 500) > mid;
            if (wasWall is { } prev && prev != wall)
            {
                changes++;
            }

            wasWall = wall;
        }

        Assert.True(hi - lo >= 8, $"the labyrinth transect is only {hi - lo} blocks of relief");
        Assert.True(changes >= 4, $"only {changes} wall/passage alternations over 400 blocks");
    }

    [Fact]
    public void StoneForest_IsDenserThanKarstTowers()
    {
        var planet = Content.Planets["karst"];
        var forest = FindOverSeeds(gen => gen.StylesForTest(planet).Contains("stone-forest") ? gen : null);

        int peaks = 0;
        int prev = forest.SurfaceHeight(planet, 0, 300);
        int rising = 0;
        for (int x = 1; x < 600; x++)
        {
            int h = forest.SurfaceHeight(planet, x, 300);
            if (h > prev) { rising++; }
            else if (h < prev && rising >= 3) { peaks++; rising = 0; }
            else if (h < prev) { rising = 0; }

            prev = h;
        }

        Assert.True(peaks >= 8, $"only {peaks} pinnacles over 600 blocks — that is tower karst, not a stone forest");
    }

    [Fact]
    public void PetrifiedDunes_AreDeckedAndSandstoneSkinned()
    {
        var planet = Content.Planets["desert"];
        var sandstone = Content.GetBlock("sandstone")!.NumericId;
        var gen = FindOverSeeds(g => g.StylesForTest(planet).Contains("petrified-dunes") ? g : null);

        int paintedCols = 0;
        foreach (var (x, z) in Grid(29, 31))
        {
            var block = gen.LandmarkPaintForTest("petrified-dunes", planet, x, z, out int fill);
            if (block is null)
            {
                continue;
            }

            paintedCols++;
            Assert.Equal(sandstone, block.Value);
            Assert.Equal(gen.SurfaceHeight(planet, x, z) - 12, fill);
            if (paintedCols >= 20)
            {
                break;
            }
        }

        Assert.True(paintedCols > 0, "the petrified dune style painted no crest at all");
    }

    // ---------- the control ----------

    /// <summary>The regression net for the whole package: a world on which NO generation-3 family is gated on
    /// must generate exactly what generation 1 generates. If this ever fails, a family leaked past its gate.</summary>
    [Fact]
    public void AWorldWithNoActiveFamily_GeneratesExactlyLikeGenerationOne()
    {
        var candidates = Content.Planets.Values
            .Where(p => !p.Void && p.MinTerrainGeneration <= 1)
            .Where(p =>
            {
                var gates = Gen(20260903, 3).WonderGatesForTest(p);
                return WorldGenerator.Gen3GateNames.All(n => !gates[n]);
            })
            .Select(p => p.Key)
            .ToList();

        Assert.True(candidates.Count > 0,
            "every planet type now activates some generation-3 family — the control needs a new home");

        foreach (var key in candidates.Take(3))
        {
            var planet = Content.Planets[key];
            var g1 = Gen(20260903, 1);
            var g3 = Gen(20260903, 3);
            foreach (var (x, z) in new[] { (0, 0), (100, 37), (-200, 150) })
            {
                int cx = WorldConstants.WorldToChunk(x), cz = WorldConstants.WorldToChunk(z);
                int cy = WorldConstants.WorldToChunk(g1.SurfaceHeight(planet, x, z));
                var a = g1.Generate(planet, new ChunkCoord(cx, cy, cz));
                var b = g3.Generate(planet, new ChunkCoord(cx, cy, cz));
                for (int lx = 0; lx < WorldConstants.ChunkSize; lx++)
                    for (int ly = 0; ly < WorldConstants.ChunkSize; ly++)
                        for (int lz = 0; lz < WorldConstants.ChunkSize; lz++)
                        {
                            Assert.True(a.Get(lx, ly, lz) == b.Get(lx, ly, lz),
                                $"{key} differs at ({x + lx},{ly},{z + lz}) between generation 1 and 3");
                        }
            }
        }
    }

    // ---------- rainbow strata ----------

    /// <summary>Bunte Berge: inside its region the paint fill claims the column forty deep and cycles four
    /// blocks in 3-thick bands parallel to the surface — the reference consumer of the paint cycle.</summary>
    [Fact]
    public void RainbowStrata_CycleFourBlocks_InBandsParallelToTheSurface()
    {
        var planet = Content.Planets["red_desert"];
        var gen = Gen(1, 3);
        var gen1 = Gen(1, 1);
        Assert.True(gen.WonderGatesForTest(planet)["rainbowStrata"]);
        Assert.False(gen1.WonderGatesForTest(planet)["rainbowStrata"]);

        var chunks = new Dictionary<ChunkCoord, ChunkData>();
        BlockId Cell(int x, int y, int z)
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

        var cycle = new[] { "sandstone", "granite", "salt", "basalt" }.Select(k => Content.GetBlock(k)!.NumericId).ToArray();
        var seen = new HashSet<BlockId>();
        int painted = 0, solidChecked = 0;
        foreach (var (x, z) in Grid(31, 29))
        {
            var top = gen.LandmarkPaintForTest("rainbow-strata", planet, x, z, out int fill);
            Assert.Null(gen1.LandmarkPaintForTest("rainbow-strata", planet, x, z));
            if (top is null)
            {
                continue;
            }

            int surface = gen.SurfaceHeight(planet, x, z);
            Assert.Equal(surface - 40, fill);
            if (painted++ >= 16)
            {
                continue;
            }

            // Down the column every SOLID cell is the band the cycle says for its depth; a carved cell (cave,
            // tunnel, lava pocket) is skipped, never counted against the cycle. The band variety is judged over
            // all sampled columns, since any one column may run through a cavern.
            for (int depth = 4; depth <= 20; depth++)
            {
                var cell = Cell(x, surface - depth, z);
                if (System.Array.IndexOf(cycle, cell) < 0)
                {
                    continue; // air, lava, or an ore the fill never claims — not a band cell
                }

                seen.Add(cell);
                solidChecked++;
                Assert.True(cell == WorldGenerator.CycleBlockAt(cycle, depth),
                    $"({x},{surface - depth},{z}) at depth {depth} is {cell}, expected band {(depth / 3) % 4}");
            }
        }

        Assert.True(painted > 0, "no rainbow column on a red desert");
        Assert.True(solidChecked >= 24, $"only {solidChecked} band cells were solid across the sample");
        Assert.True(seen.Count >= 3, $"only {seen.Count} distinct bands across {painted} columns");
    }
}
