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
/// #1648 (landscape variety 5/6): the generation-1 prop rows and micro-ruins, the seven new tree kinds and the
/// giant-flora table. Generation 0 keeps the classic five rows, palettes and the mycelium mushrooms only.
/// </summary>
public sealed class LandscapeStampsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly string[] ClassicRows = { "monolith", "stone-circle", "boulder", "crystal-shard", "dead-tree" };

    private static readonly (string Row, string Planet, double Chance)[] Gen1Rows =
    {
        ("fallen-log", "jungle", 0.0015), ("termite-mound", "savanna", 0.0020), ("cairn", "tundra", 0.0012),
        ("bone-pile", "desert", 0.0008), ("rib-cage", "desert", 0.00015), ("ice-boulder", "ice", 0.0012),
        ("lava-spatter", "lava", 0.0015), ("coral-outcrop", "ocean", 0.0030), ("crystal-cluster", "crystal", 0.0004),
        ("meteorite", "asteroid", 0.0006), ("tar-pit", "desert", 0.0006), ("wall-fragment", "savanna", 0.00012),
        ("buried-pillar", "savanna", 0.00012), ("crashed-probe", "savanna", 0.00008), ("mining-rig", "savanna", 0.00008),
        ("rune-stone", "savanna", 0.00012),
    };

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

    [Fact]
    public void PropTable_KeepsTheClassicRowsFirst_AndAppendsTheNewOnes()
    {
        var order = WorldGenerator.PropOrderForTest();
        Assert.Equal(ClassicRows, order.Take(5).ToArray());
        foreach (var (row, _, _) in Gen1Rows)
        {
            Assert.Contains(row, order.Skip(5));
        }
    }

    [Fact]
    public void GenerationZero_ActivatesNoNewRow_OnAnyWorld()
    {
        foreach (var planet in Content.Planets.Values)
        {
            var active = Gen(4711, 0).PropActiveForTest(planet, crystalWorld: true, dryWorld: true);
            foreach (var (row, _, _) in Gen1Rows)
            {
                Assert.DoesNotContain(row, active);
            }

            Assert.Empty(Gen(4711, 0).GiantFloraForTest(planet));
        }
    }

    [Theory]
    [MemberData(nameof(RowCases))]
    public void Row_RollsAtItsDesignedRate_OnAnEligibleWorld_AndNeverElsewhere(string row, string key, double chance)
    {
        var planet = Content.Planets[key];
        var gen = Gen(31, 1);
        Assert.Contains(row, gen.PropActiveForTest(planet, crystalWorld: true, dryWorld: true));

        int hits = 0, n = 0;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        int step = chance < 0.0005 ? 1 : 3;
        for (int z = -period / 2; z < period / 2; z += step)
            for (int x = 0; x < circ; x += step)
            {
                n++;
                if (gen.PropRollForTest(row, planet, x, z))
                {
                    hits++;
                }
            }

        double rate = hits / (double)n;
        Assert.InRange(rate, chance / 2.0, chance * 2.0);

        // A world the gate rejects never rolls the row.
        var ineligible = Content.Planets[key == "asteroid" ? "jungle" : "orbital_station"];
        for (int x = 0; x < 3000; x += 7)
        {
            Assert.False(gen.PropRollForTest(row, ineligible, x, 11));
        }
    }

    public static IEnumerable<object[]> RowCases() => Gen1Rows.Select(r => new object[] { r.Row, r.Planet, r.Chance });

    [Fact]
    public void RibCage_StraddlingAChunkEdge_GeneratesOnBothSides()
    {
        // The widest prop (7 across) drives the scan margin: a rib cage rolled 2 columns inside one chunk must write
        // bone cells into the neighbouring chunk too — i.e. the neighbour's margin scan reaches the roll column.
        var desert = Content.Planets["desert"];
        var bone = Content.GetBlock("bone")!.NumericId;
        int cs = WorldConstants.ChunkSize;
        for (long s = 1; s <= 60; s++)
        {
            var gen = Gen(s * 6151 + 3, 1);
            int circ = WorldConstants.Circumference;
            int period = WorldConstants.LatitudePeriodFor(circ);
            for (int z = -period / 2; z < period / 2; z++)
                for (int cx = 1; cx < circ / cs - 1; cx++)
                {
                    int x = cx * cs + 1; // one column inside chunk cx: both orientations reach the chunk to the west
                    if (!gen.PropRollForTest("rib-cage", desert, x, z))
                    {
                        continue;
                    }

                    int sy = gen.SurfaceHeight(desert, x, z);
                    if (gen.SurfaceGen1WaterDepth(desert, x, z) > 0 || sy <= gen.SeaLevel(desert))
                    {
                        continue;
                    }

                    // An earlier row may win the column (table precedence), and the west chunk's ground may sit a
                    // little lower — probe two stacked chunks and move on if this candidate stamped nothing.
                    int count = 0;
                    foreach (int cy in new[] { WorldConstants.WorldToChunk(sy + 1), WorldConstants.WorldToChunk(sy + 4) }.Distinct())
                    {
                        var left = gen.Generate(desert, new ChunkCoord(cx - 1, cy, WorldConstants.WorldToChunk(z)));
                        for (int lx = 0; lx < cs; lx++)
                            for (int ly = 0; ly < cs; ly++)
                                for (int lz = 0; lz < cs; lz++)
                                {
                                    if (left.Get(lx, ly, lz) == bone)
                                    {
                                        count++;
                                    }
                                }
                    }

                    if (count > 0)
                    {
                        return; // the west chunk carries the ribs that straddle the edge
                    }
                }
        }

        Assert.Fail("no rib cage straddling a chunk edge left bone in the neighbouring chunk (60 desert worlds)");
    }

    // ---------- trees ----------

    [Fact]
    public void Palettes_OfferTheNewKinds_OnGenerationOneOnly()
    {
        foreach (var name in new[] { "temperate", "tropical", "savanna", "desert", "swamp", "alien" })
        {
            var theme = FloraThemes.Resolve(name);
            var classic = theme.PaletteFor(0);
            var gen1 = theme.PaletteFor(1);
            Assert.Equal(theme.Trees, classic);
            Assert.True(gen1.Length > classic.Length, $"{name}: no new kind on generation 1");
            foreach (var k in classic)
            {
                Assert.Contains(k, gen1);
            }

            foreach (var k in classic)
            {
                Assert.True(k <= TreeKind.Dead, $"{name}: classic palette carries the generation-1 kind {k}");
            }
        }

        Assert.Equal(FloraThemes.Resolve("tundra").Trees, FloraThemes.Resolve("tundra").PaletteFor(1));
    }

    [Fact]
    public void NewTrees_StayInsideTheStampEnvelope()
    {
        // Every tree kind's cells must lie within the scan margin (|dx|,|dz| ≤ 4) and under MaxStampRise (18).
        foreach (var kind in new[] { TreeKind.Baobab, TreeKind.Mangrove, TreeKind.Bamboo, TreeKind.Saguaro, TreeKind.Willow, TreeKind.MushroomTree, TreeKind.CrystalTree })
        {
            for (int variant = 0; variant < 12; variant++)
            {
                var cells = WorldGenerator.BuildTreeForTest(kind, 0.7 + variant * 0.05, 1.0 + (variant % 3) * 0.06, 1.0 + (variant % 4) * 0.05, variant * 83);
                Assert.NotEmpty(cells);
                foreach (var (dx, dy, dz) in cells)
                {
                    Assert.InRange(dx, -4, 4);
                    Assert.InRange(dz, -4, 4);
                    Assert.InRange(dy, 1, 18);
                }
            }
        }
    }

    [Fact]
    public void Trees_OnGenerationOneWorlds_IncludeANewKind_Somewhere()
    {
        // A savanna gen-1 world grows baobabs (2×2 trunks) — count columns with a 2×2 log square above the ground.
        var savanna = Content.Planets["savanna"];
        var log = Content.GetBlock("wood_log")!.NumericId;
        int cs = WorldConstants.ChunkSize;
        for (long s = 1; s <= 8; s++)
        {
            var gen = Gen(s * 977 + 5, 1);
            for (int cx = 0; cx < 14; cx++)
                for (int cz = 0; cz < 10; cz++)
                {
                    int sx = cx * cs * 5, sz = cz * cs * 5 - 700;
                    int sy = gen.SurfaceHeight(savanna, sx, sz);
                    var chunk = gen.Generate(savanna, new ChunkCoord(WorldConstants.WorldToChunk(sx), WorldConstants.WorldToChunk(sy + 3), WorldConstants.WorldToChunk(sz)));
                    for (int x = 0; x < cs - 1; x++)
                        for (int y = 0; y < cs; y++)
                            for (int z = 0; z < cs - 1; z++)
                            {
                                if (chunk.Get(x, y, z) == log && chunk.Get(x + 1, y, z) == log && chunk.Get(x, y, z + 1) == log && chunk.Get(x + 1, y, z + 1) == log)
                                {
                                    return; // a baobab trunk
                                }
                            }
                }
        }

        Assert.Fail("no baobab trunk found on eight generation-1 savanna worlds");
    }

    // ---------- giant flora ----------

    [Fact]
    public void GiantFlora_RowsFollowTheHostGround()
    {
        var gen = Gen(9, 1);
        Assert.Contains("giant-fern", gen.GiantFloraForTest(Content.Planets["jungle"]));
        Assert.Contains("giant-crystal", gen.GiantFloraForTest(Content.Planets["crystal_living"]));
        Assert.Contains("giant-cactus", gen.GiantFloraForTest(Content.Planets["desert"]));
        Assert.Empty(Gen(9, 0).GiantFloraForTest(Content.Planets["jungle"]));
    }

    [Fact]
    public void GiantCactus_StandsOnGenerationOneDesert()
    {
        // A giant cactus is a 4+ tall column of the leaf block with an arm; the desert theme's saguaro also uses the
        // leaf block, so this probe only asserts that leaf-block columns ≥ 4 exist at all on gen 1 and not on gen 0.
        var desert = Content.Planets["desert"];
        var leaf = Content.GetBlock("tree_leaves")!.NumericId;
        int cs = WorldConstants.ChunkSize;
        int Columns(WorldGenerator gen)
        {
            int found = 0;
            for (int cx = 0; cx < 14; cx++)
                for (int cz = 0; cz < 10; cz++)
                {
                    int sx = cx * cs * 5, sz = cz * cs * 5 - 700;
                    int sy = gen.SurfaceHeight(desert, sx, sz);
                    var chunk = gen.Generate(desert, new ChunkCoord(WorldConstants.WorldToChunk(sx), WorldConstants.WorldToChunk(sy + 3), WorldConstants.WorldToChunk(sz)));
                    for (int x = 0; x < cs; x++)
                        for (int z = 0; z < cs; z++)
                        {
                            int run = 0;
                            for (int y = 0; y < cs; y++)
                            {
                                run = chunk.Get(x, y, z) == leaf ? run + 1 : 0;
                                if (run >= 4)
                                {
                                    found++;
                                    break;
                                }
                            }
                        }
                }

            return found;
        }

        Assert.Equal(0, Columns(Gen(77, 0)));
        int gen1 = 0;
        for (long s = 1; s <= 6 && gen1 == 0; s++)
        {
            gen1 = Columns(Gen(s * 977 + 5, 1));
        }

        Assert.True(gen1 > 0, "no cactus column on six generation-1 desert worlds");
    }
}
