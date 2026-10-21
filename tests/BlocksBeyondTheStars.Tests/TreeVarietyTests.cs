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
/// The tree-variety package (#2848–#2854, terrain generation 29): the fair weighted grove pick, the mixed woods, the
/// alien / fungal / crystal trees on their grounds, the glow-level rules of the crystal and lantern trees, the slot blocks
/// every kind is built from, the saguaro block, the ten new shapes and the variants of the classic kinds (all inside the
/// stamp margins, all deterministic), and the per-slot tree species. Older generations are pinned by
/// <see cref="WorldGenerationGoldenTests"/>; here only a few cheap probes check that they keep the classic kinds.
/// </summary>
public sealed class TreeVarietyTests
{
    private const int Gen = WorldDescription.TreeVarietyGeneration;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A theme and a planet that grows it (the planet only supplies the grove field's scale and the glow level).</summary>
    private static readonly (string Theme, string Planet)[] ThemePlanets =
    {
        ("temperate", "meadowlands"), ("tropical", "jungle"), ("savanna", "savanna"), ("desert", "desert"),
        ("swamp", "swamp"), ("tundra", "boreal"), ("alpine", "highland"), ("alien", "corrupted"),
    };

    private static WorldGenerator NewGen(long seed, int generation)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetLavaCoreVolcanoes(true);
        gen.SetTerrainGeneration(generation);
        return gen;
    }

    private static GlowLevel GlowOf(WorldGenerator gen, PlanetType planet) => GlowRules.LevelFor(planet, gen.RosterSeed, gen.TerrainGeneration);

    /// <summary>A spread of column positions far enough apart that their grove values are nearly independent.</summary>
    private static IEnumerable<(int X, int Z)> SpreadColumns(int count, long salt)
    {
        for (int i = 0; i < count; i++)
        {
            int x = (int)((i * 7919L + salt * 104729L) % 3600L) - 1800;
            int z = (int)((i * 6271L + salt * 1299709L) % 1400L) - 700;
            yield return (x, z);
        }
    }

    // ---------- #2848: the fair pick ----------

    [Fact]
    public void GroveFlattening_IsMonotone_Symmetric_AndMakesTheGroveUniform()
    {
        double prev = -1.0;
        for (int i = 0; i <= 1000; i++)
        {
            double g = i / 1000.0;
            double u = WorldGenerator.FlattenGrove(g);
            Assert.InRange(u, 0.0, 1.0);
            Assert.True(u >= prev, $"not monotone at {g}");
            Assert.Equal(1.0, u + WorldGenerator.FlattenGrove(1.0 - g), 6);
            prev = u;
        }

        Assert.Equal(0.5, WorldGenerator.FlattenGrove(0.5), 9);

        // Over real grove values of several planet types, every tenth of the flattened range holds a tenth of the columns.
        var deciles = new int[10];
        int n = 0;
        foreach (var (_, planetKey) in ThemePlanets)
        {
            var planet = Content.Planets[planetKey];
            for (long s = 1; s <= 3; s++)
            {
                var gen = NewGen(s * 7919 + 13, Gen);
                foreach (var (x, z) in SpreadColumns(1500, s))
                {
                    deciles[Math.Min(9, (int)(WorldGenerator.FlattenGrove(gen.GroveValueForTest(planet, x, z)) * 10))]++;
                    n++;
                }
            }
        }

        foreach (int d in deciles)
        {
            Assert.InRange(d / (double)n, 0.085, 0.115);
        }
    }

    [Fact]
    public void WeightedPick_GivesEveryKindItsWeight_AndSkipLeavesOneOut()
    {
        foreach (var (themeName, _) in ThemePlanets)
        {
            var palette = FloraThemes.Resolve(themeName).WeightedPaletteFor(Gen);
            int total = palette.Sum(e => e.Weight);
            var counts = new Dictionary<TreeKind, int>();
            const int steps = 12000;
            for (int i = 0; i < steps; i++)
            {
                var k = WorldGenerator.WeightedTreePick(palette, total, (i + 0.5) / steps);
                counts[k] = counts.GetValueOrDefault(k) + 1;
            }

            foreach (var e in palette)
            {
                Assert.Equal(e.Weight / (double)total, counts.GetValueOrDefault(e.Kind) / (double)steps, 3);
            }

            var skip = palette[0].Kind;
            for (int i = 0; i < 1000; i++)
            {
                Assert.NotEqual(skip, WorldGenerator.WeightedTreePick(palette, total - palette[0].Weight, i / 1000.0, skip));
            }
        }
    }

    [Fact]
    public void GrovePick_FollowsThePaletteWeights_OnEveryTheme()
    {
        foreach (var (themeName, planetKey) in ThemePlanets)
        {
            var theme = FloraThemes.Resolve(themeName);
            var planet = Content.Planets[planetKey];
            var measured = new Dictionary<TreeKind, double>();
            var expected = new Dictionary<TreeKind, double>();
            int leads = 0;
            for (long s = 1; s <= 24; s++)
            {
                var gen = NewGen(s * 4241 + 7, Gen);
                var palette = WorldGenerator.EffectiveTreePalette(theme, Gen, GlowOf(gen, planet));
                int total = palette.Sum(e => e.Weight);
                foreach (var (x, z) in SpreadColumns(160, s))
                {
                    var (_, lead, _) = gen.PickTreeKindGen29ForTest(planet, theme, x, z);
                    measured[lead] = measured.GetValueOrDefault(lead) + 1;
                    foreach (var e in palette)
                    {
                        expected[e.Kind] = expected.GetValueOrDefault(e.Kind) + e.Weight / (double)total;
                    }

                    leads++;
                }
            }

            foreach (var kind in expected.Keys.Union(measured.Keys))
            {
                double want = expected.GetValueOrDefault(kind) / leads;
                double got = measured.GetValueOrDefault(kind) / leads;
                Assert.True(Math.Abs(want - got) <= 0.045, $"{themeName}: {kind} leads {got:P1} of the groves, its weight says {want:P1}");
            }
        }
    }

    [Fact]
    public void TheMeasuredStarvation_IsGone_DesertIsNoLongerDeadSnags_AndTheJungleGrowsJungleTrees()
    {
        // #2848: deserts were 86 % dead snags, the jungle planet's own jungle tree 0.4 %.
        var shares = KindShares("desert", "desert");
        Assert.True(shares.GetValueOrDefault(TreeKind.Dead) < 0.30, $"desert: {shares.GetValueOrDefault(TreeKind.Dead):P1} dead snags");
        Assert.True(shares.GetValueOrDefault(TreeKind.Palm) > 0.15, "desert: too few palms");
        Assert.True(shares.GetValueOrDefault(TreeKind.Saguaro) > 0.15, "desert: too few saguaros");

        shares = KindShares("tropical", "jungle");
        Assert.True(shares.GetValueOrDefault(TreeKind.Jungle) > 0.12, $"jungle: only {shares.GetValueOrDefault(TreeKind.Jungle):P1} jungle trees");
        Assert.True(shares.GetValueOrDefault(TreeKind.Bamboo) > 0.06, "jungle: too little bamboo");
        Assert.True(shares.GetValueOrDefault(TreeKind.Broadleaf) < 0.20, "jungle: the broadleaf still dominates");
    }

    private static Dictionary<TreeKind, double> KindShares(string themeName, string planetKey)
    {
        var theme = FloraThemes.Resolve(themeName);
        var planet = Content.Planets[planetKey];
        var counts = new Dictionary<TreeKind, double>();
        int n = 0;
        for (long s = 1; s <= 12; s++)
        {
            var gen = NewGen(s * 31 + 1, Gen);
            foreach (var (x, z) in SpreadColumns(250, s))
            {
                var kind = gen.PickTreeKindGen29ForTest(planet, theme, x, z).Kind;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                n++;
            }
        }

        foreach (var k in counts.Keys.ToList())
        {
            counts[k] /= n;
        }

        return counts;
    }

    // ---------- #2852: mixed woods ----------

    [Fact]
    public void MixedWoods_AboutThirtyPercent_TakeAnotherKindOfThePalette()
    {
        var theme = FloraThemes.Resolve("temperate");
        var planet = Content.Planets["meadowlands"];
        int mixed = 0, n = 0;
        for (long s = 1; s <= 6; s++)
        {
            var gen = NewGen(s * 13 + 5, Gen);
            var palette = WorldGenerator.EffectiveTreePalette(theme, Gen, GlowOf(gen, planet));
            for (int x = -600; x < 600; x += 7)
                for (int z = -90; z < 90; z += 9)
                {
                    var (kind, lead, isMixed) = gen.PickTreeKindGen29ForTest(planet, theme, x, z);
                    Assert.Contains(palette, e => e.Kind == kind);
                    if (isMixed)
                    {
                        Assert.NotEqual(lead, kind); // a mixed tree is another kind than its grove's
                        mixed++;
                    }
                    else
                    {
                        Assert.Equal(lead, kind);
                    }

                    n++;
                }
        }

        Assert.InRange(mixed / (double)n, 0.27, 0.33);
    }

    // ---------- #2849 / #2857: glow level and palettes ----------

    [Fact]
    public void EffectivePalette_DropsCrystalTreesWithoutGlow_AndAddsLanternsOnLuminousLeafyPalettes()
    {
        var alien = FloraThemes.Resolve("alien");
        Assert.DoesNotContain(WorldGenerator.EffectiveTreePalette(alien, Gen, GlowLevel.None), e => e.Kind == TreeKind.CrystalTree);
        Assert.Contains(WorldGenerator.EffectiveTreePalette(alien, Gen, GlowLevel.Low), e => e.Kind == TreeKind.CrystalTree);

        foreach (var name in new[] { "temperate", "tropical", "savanna", "desert", "swamp", "tundra", "alpine", "alien" })
        {
            var theme = FloraThemes.Resolve(name);
            var luminous = WorldGenerator.EffectiveTreePalette(theme, Gen, GlowLevel.Luminous);
            Assert.Single(luminous, e => e.Kind == TreeKind.Lantern && e.Weight == WorldGenerator.LanternTreeWeight);
            foreach (var level in new[] { GlowLevel.None, GlowLevel.Low, GlowLevel.Normal })
            {
                Assert.DoesNotContain(WorldGenerator.EffectiveTreePalette(theme, Gen, level), e => e.Kind == TreeKind.Lantern);
            }
        }

        // Palettes without a leafy kind never get a lantern tree; the crystal theme keeps its crystal trees (never None).
        Assert.DoesNotContain(WorldGenerator.EffectiveTreePalette(FloraThemes.Resolve("fungal"), Gen, GlowLevel.Luminous), e => e.Kind == TreeKind.Lantern);
        Assert.DoesNotContain(WorldGenerator.EffectiveTreePalette(FloraThemes.Resolve("crystal"), Gen, GlowLevel.Luminous), e => e.Kind == TreeKind.Lantern);
        Assert.Equal(new[] { TreeKind.CrystalTree }, WorldGenerator.EffectiveTreePalette(FloraThemes.Resolve("crystal"), Gen, GlowLevel.Low).Select(e => e.Kind));
    }

    /// <summary>Generates surface chunks spread over the whole world (the biome field is large-scale, so a multi-biome type
    /// needs the spread to show every biome) and collects every tree kind the ground pass builds, until
    /// <paramref name="enough"/> holds or the chunk budget is spent.</summary>
    private static HashSet<TreeKind> StampedKinds(WorldGenerator gen, PlanetType planet, int maxChunks, Func<HashSet<TreeKind>, bool>? enough = null,
        Action<ChunkData>? perChunk = null, string? biomeSurface = null)
    {
        var kinds = new HashSet<TreeKind>();
        var want = biomeSurface == null ? BlockId.Air : Content.GetBlock(biomeSurface)!.NumericId;
        gen.TreeStampedForTest = (k, _, _) => kinds.Add(k);
        for (int i = 0, made = 0; made < maxChunks && i < maxChunks * 40; i++)
        {
            int sx = (i * 733) % 5600 + 5, sz = (i * 467) % 2400 - 1200;
            if (!want.IsAir && gen.TreeBiomeSurfaceForTest(planet, sx, sz) != want)
            {
                continue; // only the chunks of the wanted biome
            }

            made++;
            int sy = gen.SurfaceHeight(planet, sx, sz);
            var chunk = gen.Generate(planet, new ChunkCoord(WorldConstants.WorldToChunk(sx), WorldConstants.WorldToChunk(sy + 3), WorldConstants.WorldToChunk(sz)));
            perChunk?.Invoke(chunk);
            if (enough != null && enough(kinds))
            {
                break;
            }
        }

        gen.TreeStampedForTest = null;
        return kinds;
    }

    /// <summary>A generation-29 world whose glow level passes <paramref name="want"/> and (when given) which uses the biome
    /// with the surface <paramref name="surfaceKey"/> — a multi-biome type draws its biome subset per world.</summary>
    private static WorldGenerator GenWithGlow(PlanetType planet, Func<GlowLevel, bool> want, string? surfaceKey = null)
    {
        var surface = surfaceKey == null ? BlockId.Air : Content.GetBlock(surfaceKey)!.NumericId;
        for (long s = 1; s < 400; s++)
        {
            var gen = NewGen(s * 1543 + 11, Gen);
            if (want(GlowOf(gen, planet)) && (surface.IsAir || gen.ResolveBiomes(planet).Exists(b => b.Surface == surface)))
            {
                return gen;
            }
        }

        throw new InvalidOperationException("no seed with the wanted glow level");
    }

    [Fact]
    public void AlienWorld_GrowsSpiralMushroomAndCrystalTrees_OnGenerationTwentyNine()
    {
        // #2849: 0 of 1 117 measured corrupted trees were mushroom or crystal trees — their ground (alien grass) was never valid.
        var corrupted = Content.Planets["corrupted"];
        var gen = GenWithGlow(corrupted, g => g >= GlowLevel.Low, "alien_grass");
        var kinds = StampedKinds(gen, corrupted, 48,
            k => k.Contains(TreeKind.MushroomTree) && k.Contains(TreeKind.CrystalTree) && k.Contains(TreeKind.Spiral), biomeSurface: "alien_grass");
        Assert.Contains(TreeKind.Spiral, kinds);
        Assert.Contains(TreeKind.MushroomTree, kinds);
        Assert.Contains(TreeKind.CrystalTree, kinds);

        // A corrupted world without glow grows no crystal tree (its stone biome then grows no tree at all).
        var dark = GenWithGlow(corrupted, g => g == GlowLevel.None, "alien_grass");
        Assert.DoesNotContain(TreeKind.CrystalTree, StampedKinds(dark, corrupted, 24));
    }

    [Fact]
    public void FungalAndCrystalWorlds_GrowTheirTrees_OnlyFromGenerationTwentyNine()
    {
        var fungal = Content.Planets["fungal"];
        Assert.Contains(TreeKind.MushroomTree, StampedKinds(NewGen(77, Gen), fungal, 40, k => k.Contains(TreeKind.MushroomTree)));
        Assert.Empty(StampedKinds(NewGen(77, Gen - 1), fungal, 12)); // generation 28: the fungal theme grows no tree

        var crystal = Content.Planets["crystal_living"];
        Assert.Contains(TreeKind.CrystalTree, StampedKinds(NewGen(78, Gen), crystal, 64, k => k.Contains(TreeKind.CrystalTree)));
        Assert.Empty(StampedKinds(NewGen(78, Gen - 1), crystal, 12));
    }

    [Fact]
    public void LanternTrees_GrowOnlyOnLuminousWorlds_AndHangTheirGlowingFruit()
    {
        var meadow = Content.Planets["meadowlands"];
        var lanternFruit = Content.GetBlock(FloraCatalog.LanternFruitKey)!.NumericId;

        var bright = GenWithGlow(meadow, g => g == GlowLevel.Luminous);
        int fruit = 0;
        var kinds = StampedKinds(bright, meadow, 40, k => k.Contains(TreeKind.Lantern) && fruit > 0, chunk => fruit += Count(chunk, lanternFruit));
        Assert.Contains(TreeKind.Lantern, kinds);
        Assert.True(fruit > 0, "a luminous world's lantern trees hung no lantern fruit");

        var normal = GenWithGlow(meadow, g => g != GlowLevel.Luminous);
        int none = 0;
        Assert.DoesNotContain(TreeKind.Lantern, StampedKinds(normal, meadow, 16, null, chunk => none += Count(chunk, lanternFruit)));
        Assert.Equal(0, none);
    }

    private static int Count(ChunkData chunk, BlockId block)
    {
        int cs = WorldConstants.ChunkSize, n = 0;
        for (int x = 0; x < cs; x++)
            for (int y = 0; y < cs; y++)
                for (int z = 0; z < cs; z++)
                {
                    if (chunk.Get(x, y, z) == block)
                    {
                        n++;
                    }
                }

        return n;
    }

    // ---------- #2850: the saguaro block ----------

    [Fact]
    public void Saguaro_IsBuiltFromItsCactusBlock_OnGenerationTwentyNine_AndFromLeavesBefore()
    {
        foreach (var (_, dy, _, block) in WorldGenerator.BuildTreeGen29ForTest(TreeKind.Saguaro, 1.0, 1.0, 1.0, 321))
        {
            Assert.Equal(WorldGenerator.SaguaroKey, WorldGenerator.TestTreeBlockKey(block));
            Assert.InRange(dy, 1, 18);
        }

        var desert = Content.Planets["desert"];
        var saguaro = Content.GetBlock(WorldGenerator.SaguaroKey)!.NumericId;
        var leaves = Content.GetBlock(TreeFoliage.LeavesKey)!.NumericId;
        int newSaguaro = 0;
        StampedKinds(NewGen(4242, Gen), desert, 40, _ => newSaguaro > 0, chunk => newSaguaro += Count(chunk, saguaro));
        Assert.True(newSaguaro > 0, "no saguaro block on a generation-29 desert");

        int oldSaguaro = 0, oldLeaves = 0;
        var oldKinds = StampedKinds(NewGen(4242, Gen - 1), desert, 24, null, chunk =>
        {
            oldSaguaro += Count(chunk, saguaro);
            oldLeaves += Count(chunk, leaves);
        });
        Assert.Equal(0, oldSaguaro);
        Assert.True(oldLeaves > 0, "a generation-28 desert built its saguaros / giant cacti from leaves");
        Assert.All(oldKinds, k => Assert.True(k <= TreeKind.CrystalTree, $"generation 28 grew the generation-29 kind {k}"));
    }

    // ---------- #2851: slot blocks ----------

    [Fact]
    public void EveryKind_IsBuiltFromItsSlotBlocks()
    {
        foreach (var kind in Enum.GetValues<TreeKind>().Where(k => k != TreeKind.None))
        {
            var (leaves, trunk) = WorldGenerator.TreeSlotKeysFor(kind);
            var keys = new HashSet<string?>();
            foreach (var (_, _, _, block) in WorldGenerator.BuildTreeGen29ForTest(kind, 1.0, 1.0, 1.0, 0x5A5A5))
            {
                keys.Add(WorldGenerator.TestTreeBlockKey(block));
            }

            var allowed = new HashSet<string?> { leaves, trunk };
            Assert.True(keys.IsSubsetOf(allowed), $"{kind} used {string.Join(", ", keys)}, its slots are {leaves} / {trunk}");
            if (leaves != null)
            {
                Assert.Contains(leaves, keys);
            }

            if (trunk != null)
            {
                Assert.Contains(trunk, keys);
            }
        }

        // The design table's rows (#2851).
        AssertSlots(TreeKind.Birch, TreeFoliage.LeavesBKey, TreeWood.PaleLogKey);
        AssertSlots(TreeKind.Bottle, TreeFoliage.LeavesBKey, TreeWood.PaleLogKey); // pale trunk + crown B: one species
        AssertSlots(TreeKind.Blossom, TreeFoliage.BlossomKey, TreeWood.LogKey);
        AssertSlots(TreeKind.Oak, TreeFoliage.LeavesKey, TreeWood.LogKey);
        AssertSlots(TreeKind.Willow, TreeFoliage.LeavesBKey, TreeWood.LogKey);
        AssertSlots(TreeKind.Baobab, TreeFoliage.LeavesCKey, TreeWood.LogKey);
        AssertSlots(TreeKind.Lantern, TreeFoliage.LeavesCKey, TreeWood.LogKey);
        AssertSlots(TreeKind.Conifer, TreeFoliage.NeedlesKey, TreeWood.LogKey);
    }

    private static void AssertSlots(TreeKind kind, string leaves, string trunk)
    {
        var slots = WorldGenerator.TreeSlotKeysFor(kind);
        Assert.Equal(leaves, slots.Leaves);
        Assert.Equal(trunk, slots.Trunk);
    }

    [Fact]
    public void AGenerationTwentyNineWood_WearsSeveralCrownBlocks()
    {
        // A temperate wood mixes broadleaf / oak (slot A), birch / willow (slot B), columns (slot C) and blossom trees.
        var meadow = Content.Planets["meadowlands"];
        var crowns = new[] { TreeFoliage.LeavesKey, TreeFoliage.LeavesBKey, TreeFoliage.LeavesCKey, TreeFoliage.BlossomKey }
            .Select(k => Content.GetBlock(k)!.NumericId).ToArray();
        var seen = new HashSet<BlockId>();
        StampedKinds(NewGen(9001, Gen), meadow, 40, _ => seen.Count >= 3, chunk =>
        {
            foreach (var c in crowns)
            {
                if (Count(chunk, c) > 0)
                {
                    seen.Add(c);
                }
            }
        });
        Assert.True(seen.Count >= 3, $"only {seen.Count} crown blocks in a generation-29 temperate wood");
    }

    // ---------- #2853 / #2854: the builders ----------

    [Fact]
    public void EveryGenerationTwentyNineBuilder_StaysInsideTheStampMargins_AndIsDeterministic()
    {
        var sizes = new[] { 0.7, 0.85, 1.0, 1.15, 1.3 };
        var jitters = new[] { 0.88, 1.0, 1.12 };
        foreach (var kind in Enum.GetValues<TreeKind>().Where(k => k != TreeKind.None))
        {
            int built = 0;
            foreach (double sizeF in sizes)
                foreach (double hJit in jitters)
                    foreach (double cJit in jitters)
                        for (int shape = 0; shape < 48; shape++)
                        {
                            int hash = shape * 21841 + 977;
                            var cells = WorldGenerator.BuildTreeGen29ForTest(kind, sizeF, hJit, cJit, hash);
                            Assert.NotEmpty(cells);
                            foreach (var (dx, dy, dz, _) in cells)
                            {
                                Assert.True(Math.Abs(dx) <= 4 && Math.Abs(dz) <= 4 && dy >= 1 && dy <= 18,
                                    $"{kind} (size {sizeF}, h {hJit}, c {cJit}, shape {hash}) wrote ({dx}, {dy}, {dz})");
                            }

                            if (shape < 4)
                            {
                                Assert.Equal(cells, WorldGenerator.BuildTreeGen29ForTest(kind, sizeF, hJit, cJit, hash));
                            }

                            built++;
                        }

            Assert.True(built > 0);
        }
    }

    [Fact]
    public void TheNewShapes_HaveTheirCharacter()
    {
        int TrunkHeight(List<(int Dx, int Dy, int Dz, BlockId Block)> cells, string trunkKey)
            => cells.Where(c => c.Dx == 0 && c.Dz == 0 && WorldGenerator.TestTreeBlockKey(c.Block) == trunkKey).Select(c => c.Dy).DefaultIfEmpty(0).Max();

        // Birch: a tall thin pale trunk (6–11), a narrow crown (never wider than 2 from the trunk).
        var birch = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Birch, 1.0, 1.0, 1.0, 1234);
        Assert.InRange(TrunkHeight(birch, TreeWood.PaleLogKey), 6, 11);
        Assert.All(birch.Where(c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeFoliage.LeavesBKey), c => Assert.InRange(Math.Abs(c.Dx), 0, 2));

        // Oak on a large tree: a 2×2 trunk.
        var oak = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Oak, 1.3, 1.12, 1.0, 1);
        Assert.Contains(oak, c => c.Dx == 1 && c.Dz == 1 && c.Dy == 1 && WorldGenerator.TestTreeBlockKey(c.Block) == TreeWood.LogKey);

        // Column: tall (≥ 8) and narrow (≤ 2 across from the trunk).
        var column = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Column, 1.0, 1.0, 1.0, 5);
        Assert.True(column.Max(c => c.Dy) >= 8);
        Assert.All(column, c => Assert.InRange(Math.Abs(c.Dx) + Math.Abs(c.Dz), 0, 4));

        // Umbrella: the crown is flat (at most two layers) and its centre sits off the root column (the lean).
        var umbrella = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Umbrella, 1.0, 1.0, 1.0, 9);
        var canopy = umbrella.Where(c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeFoliage.LeavesBKey).ToList();
        Assert.InRange(canopy.Select(c => c.Dy).Distinct().Count(), 1, 2);
        Assert.Contains(umbrella, c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeWood.LogKey && (c.Dx != 0 || c.Dz != 0));

        // Ball: the crown is a sphere — symmetric in x and z around the trunk.
        var ball = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Ball, 1.0, 1.0, 1.0, 3);
        var sphere = ball.Where(c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeFoliage.LeavesKey).Select(c => (c.Dx, c.Dy, c.Dz)).ToHashSet();
        Assert.All(sphere, c => Assert.Contains((-c.Dx, c.Dy, -c.Dz), sphere));

        // Spiral: the trunk leaves its root column (it winds), and it is tall.
        var spiral = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Spiral, 1.0, 1.0, 1.0, 77);
        var spiralLogs = spiral.Where(c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeWood.LogKey).ToList();
        Assert.True(spiralLogs.Select(c => (c.Dx, c.Dz)).Distinct().Count() >= 6, "the spiral trunk does not wind");
        Assert.True(spiralLogs.Max(c => c.Dy) >= 7);

        // Tree fern: 4–8 fronds — leaf cells in at least four distinct directions out from the trunk.
        var fern = WorldGenerator.BuildTreeGen29ForTest(TreeKind.TreeFern, 1.0, 1.0, 1.0, 11);
        var dirs = fern.Where(c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeFoliage.LeavesCKey && (c.Dx != 0 || c.Dz != 0))
            .Select(c => (Math.Sign(c.Dx), Math.Sign(c.Dz))).Distinct().Count();
        Assert.InRange(dirs, 4, 8);

        // Bottle: the trunk is wider in its belly than at its neck.
        var bottle = WorldGenerator.BuildTreeGen29ForTest(TreeKind.Bottle, 1.2, 1.1, 1.1, 2);
        int Width(int dy) => bottle.Count(c => c.Dy == dy && WorldGenerator.TestTreeBlockKey(c.Block) == TreeWood.PaleLogKey);
        int top = bottle.Where(c => WorldGenerator.TestTreeBlockKey(c.Block) == TreeWood.PaleLogKey && c.Dx == 0 && c.Dz == 0).Max(c => c.Dy);
        Assert.True(Enumerable.Range(1, top - 1).Max(Width) > Width(top - 1), "the bottle tree has no belly");
    }

    [Fact]
    public void ClassicKinds_VaryInShape_OnGenerationTwentyNine()
    {
        // #2854: the same kind at the same size builds different shapes for different trees.
        foreach (var kind in new[] { TreeKind.Broadleaf, TreeKind.Conifer, TreeKind.Jungle, TreeKind.Willow, TreeKind.Baobab, TreeKind.Palm })
        {
            var shapes = new HashSet<string>();
            for (int s = 0; s < 24; s++)
            {
                var cells = WorldGenerator.BuildTreeGen29ForTest(kind, 1.1, 1.05, 1.05, s * 40503 + 17)
                    .Select(c => (c.Dx, c.Dy, c.Dz)).Distinct().OrderBy(c => c).ToList();
                shapes.Add(string.Join(";", cells));
            }

            Assert.True(shapes.Count >= 3, $"{kind}: only {shapes.Count} distinct shapes over 24 trees");
        }
    }

    [Fact]
    public void AGenerationTwentyNineTreeChunk_IsIdentical_FromAFreshGenerator()
    {
        var jungle = Content.Planets["jungle"];
        int cs = WorldConstants.ChunkSize;
        for (int i = 0; i < 4; i++)
        {
            int sx = i * cs * 3 + 3, sz = 40 - i * 70;
            int sy = NewGen(31337, Gen).SurfaceHeight(jungle, sx, sz);
            var coord = new ChunkCoord(WorldConstants.WorldToChunk(sx), WorldConstants.WorldToChunk(sy + 3), WorldConstants.WorldToChunk(sz));
            var a = NewGen(31337, Gen).Generate(jungle, coord);
            var b = NewGen(31337, Gen).Generate(jungle, coord);
            for (int x = 0; x < cs; x++)
                for (int y = 0; y < cs; y++)
                    for (int z = 0; z < cs; z++)
                    {
                        Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                    }
        }
    }

    // ---------- fruit ----------

    [Fact]
    public void TheNewKinds_BearFruitAsDesigned()
    {
        foreach (var kind in new[] { TreeKind.Oak, TreeKind.Birch, TreeKind.Ball, TreeKind.Bottle, TreeKind.Umbrella })
        {
            Assert.True(FruitRules.BearsFruit(kind), $"{kind} should bear fruit");
        }

        foreach (var kind in new[] { TreeKind.Blossom, TreeKind.TreeFern, TreeKind.Spiral, TreeKind.Column, TreeKind.Lantern })
        {
            Assert.False(FruitRules.BearsFruit(kind), $"{kind} should bear no rolled fruit");
        }
    }

    // ---------- #2851: the slot species ----------

    [Fact]
    public void TreeSlots_CoinDeterministicSpecies_WithDistinctIdsAndNames()
    {
        var jungle = Content.Planets["jungle"];
        for (long seed = 1; seed <= 40; seed++)
        {
            var species = Enumerable.Range(0, TreeGenerator.SlotCount).Select(s => TreeGenerator.GenerateSlot(jungle, seed, s)).ToList();
            Assert.All(species, s => Assert.NotNull(s));
            Assert.Equal(Enumerable.Range(0, TreeGenerator.SlotCount).Select(s => "tr" + s), species.Select(s => s!.Id));
            Assert.Equal(TreeGenerator.SlotCount, species.Select(s => s!.Name).Distinct().Count());
            Assert.Equal(TreeGenerator.Generate(jungle, seed)!.Name, species[TreeGenerator.CommonSlot]!.Name);
            Assert.Equal(TreeGenerator.GenerateGiant(jungle, seed)!.Name, species[TreeGenerator.GiantSlot]!.Name);

            var again = TreeGenerator.GenerateSlot(jungle, seed, TreeGenerator.SaguaroSlot)!;
            Assert.Equal(species[TreeGenerator.SaguaroSlot]!.Name, again.Name);
            Assert.Equal(species[TreeGenerator.SaguaroSlot]!.Toxic, again.Toxic);
        }

        Assert.Null(TreeGenerator.GenerateSlot(Content.Planets["asteroid"], 5, TreeGenerator.LeavesBSlot));

        var keys = Enumerable.Range(0, TreeGenerator.SlotCount).SelectMany(TreeGenerator.SlotBlockKeys).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count()); // every block belongs to one slot
        Assert.Contains(TreeFoliage.LeavesBKey, TreeGenerator.SlotBlockKeys(TreeGenerator.LeavesBSlot));
        Assert.Contains(TreeWood.PaleLogKey, TreeGenerator.SlotBlockKeys(TreeGenerator.LeavesBSlot));
        Assert.Contains(TreeFoliage.LeavesCKey, TreeGenerator.SlotBlockKeys(TreeGenerator.LeavesCSlot));
        Assert.Contains(TreeFoliage.BlossomKey, TreeGenerator.SlotBlockKeys(TreeGenerator.BlossomSlot));
        Assert.Contains(WorldGenerator.SaguaroKey, TreeGenerator.SlotBlockKeys(TreeGenerator.SaguaroSlot));
        foreach (var key in keys)
        {
            Assert.NotNull(Content.GetBlock(key));
        }
    }

    [Fact]
    public void TreeSlots_ShareTheCommonTreesToxicity()
    {
        // Review of #2851: a slot species with its own toxic roll scanned "edible" over a fruit that is toxic exactly when
        // the COMMON tree is (FloraGenerator) — a birch's leaves said edible, its apple was toxic_fruit_round.
        var jungle = Content.Planets["jungle"];
        int toxic = 0;
        for (long seed = 1; seed <= 60; seed++)
        {
            bool common = TreeGenerator.Generate(jungle, seed)!.Toxic;
            toxic += common ? 1 : 0;
            for (int slot = TreeGenerator.LeavesBSlot; slot < TreeGenerator.SlotCount; slot++)
            {
                Assert.Equal(common, TreeGenerator.GenerateSlot(jungle, seed, slot)!.Toxic);
            }
        }

        Assert.InRange(toxic, 1, 59); // both cases were seen
    }

    [Fact]
    public void SlotsGrownOn_NamesOnlyTheSlotsAWorldCanGrow()
    {
        // Review of #2851: the server names only these (a lodge's warning list must not name a tree the world lacks).
        Assert.Empty(TreeGenerator.SlotsGrownOn(Content.Planets["meadowlands"], 7, Gen - 1)); // older generations: none
        Assert.Empty(TreeGenerator.SlotsGrownOn(Content.Planets["asteroid"], 7, Gen));

        var tundra = TreeGenerator.SlotsGrownOn(Content.Planets["tundra"], 7, Gen); // conifer, birch, dead; snow, dirt, stone
        Assert.Contains(TreeGenerator.LeavesBSlot, tundra);
        Assert.DoesNotContain(TreeGenerator.SaguaroSlot, tundra);
        Assert.DoesNotContain(TreeGenerator.BlossomSlot, tundra);

        var desert = TreeGenerator.SlotsGrownOn(Content.Planets["desert"], 7, Gen); // saguaro, bottle, column, palm, dead
        Assert.Contains(TreeGenerator.SaguaroSlot, desert);
        Assert.Contains(TreeGenerator.LeavesBSlot, desert);
        Assert.Contains(TreeGenerator.LeavesCSlot, desert);
        Assert.DoesNotContain(TreeGenerator.BlossomSlot, desert);

        var meadow = TreeGenerator.SlotsGrownOn(Content.Planets["meadowlands"], 7, Gen);
        Assert.Contains(TreeGenerator.BlossomSlot, meadow);
    }

    [Theory]
    [InlineData(TreeKind.Spiral)]
    [InlineData(TreeKind.Oak)]
    [InlineData(TreeKind.Birch)]
    [InlineData(TreeKind.Umbrella)]
    [InlineData(TreeKind.TreeFern)]
    [InlineData(TreeKind.Bottle)]
    [InlineData(TreeKind.Broadleaf)]
    [InlineData(TreeKind.Jungle)]
    [InlineData(TreeKind.Palm)]
    [InlineData(TreeKind.Lantern)]
    public void EveryLogOfATree_IsFaceConnectedToItsFoot(TreeKind kind)
    {
        // Review of #2853: the spiral's first turn and the oak's diagonal branches touched the trunk by an edge only — the
        // woodcutter (6-connected) saw a stump, and a mined foot left the rest floating.
        var logKeys = new HashSet<string> { TreeWood.LogKey, TreeWood.PaleLogKey };
        for (int shape = 0; shape < 64; shape++)
        {
            foreach (double size in new[] { 0.75, 1.0, 1.3 })
            {
                var cells = WorldGenerator.BuildTreeGen29ForTest(kind, size, 1.0, 1.0, shape * 7919);
                var logs = cells.Where(c => logKeys.Contains(WorldGenerator.TestTreeBlockKey(c.Block) ?? string.Empty))
                    .Select(c => (c.Dx, c.Dy, c.Dz)).ToHashSet();
                if (logs.Count == 0)
                {
                    continue;
                }

                var reached = new HashSet<(int, int, int)>();
                var queue = new Queue<(int, int, int)>();
                var foot = logs.OrderBy(c => c.Dy).ThenBy(c => Math.Abs(c.Dx) + Math.Abs(c.Dz)).First();
                reached.Add(foot);
                queue.Enqueue(foot);
                while (queue.Count > 0)
                {
                    var (x, y, z) = queue.Dequeue();
                    foreach (var n in new[] { (x + 1, y, z), (x - 1, y, z), (x, y + 1, z), (x, y - 1, z), (x, y, z + 1), (x, y, z - 1) })
                    {
                        if (logs.Contains(n) && reached.Add(n))
                        {
                            queue.Enqueue(n);
                        }
                    }
                }

                var loose = logs.Except(reached).ToList();
                Assert.True(loose.Count == 0, $"{kind} shape {shape * 7919} size {size}: logs not face-connected to the foot: {string.Join(" ", loose)}");
            }
        }
    }
}
