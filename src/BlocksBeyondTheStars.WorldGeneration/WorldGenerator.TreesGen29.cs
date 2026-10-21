// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 29 — the tree-variety package (#2848–#2854), partial of <see cref="WorldGenerator"/>:
/// <list type="bullet">
/// <item>#2848 a FAIR grove pick: the grove value is bell-shaped (≈ Normal(0.5, 0.118)), so cutting it into equal slices
/// starved the first and last kinds of every long palette. It is flattened to a uniform value by a piecewise-linear
/// CDF table and then picks from the theme's weighted palette.</item>
/// <item>#2852 mixed woods: about 30 % of the trees take another kind of the palette (a stateless per-tree hash).</item>
/// <item>#2849 the alien, fungal and crystal trees find their ground (alien grass, mycelium, crystal and stone); the crystal
/// tree follows the world's glow level and the lantern tree joins the palettes of a luminous world.</item>
/// <item>#2851 every kind is built from its own leaf slot / bark (the slot table in <see cref="LeavesFor"/>).</item>
/// <item>#2853 ten new shapes and #2854 shape variety within the classic kinds — new builders only; the classic builders
/// are untouched and older generations never reach this file.</item>
/// </list>
/// Every rule here sits behind <c>_terrainGeneration &gt;= WorldDescription.TreeVarietyGeneration</c>, and every new roll is
/// a stateless hash with its own salt, so generations ≤ 28 generate bit-for-bit the same chunks. Every builder stays
/// within the ground pass's 4-cell margin and <see cref="MaxStampRise"/> (a test holds them there), so a tree straddling a
/// chunk edge is complete from either side. Kept in its own file so the stamp methods stay small (#1740).
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The saguaro body block (#2850): the saguaro tree and the giant cactus are built from it from generation 29.</summary>
    public const string SaguaroKey = "saguaro";

    /// <summary>Share of the trees that take a second kind of their palette (#2852, decision 2026-10-20: about 30 %).</summary>
    internal const double MixedWoodsShare = 0.30;

    /// <summary>The pick weight the lantern tree joins a luminous world's leafy palettes with (#2857).</summary>
    internal const int LanternTreeWeight = 2;

    private const long MixedWoodsRollSalt = 0x2852_A1C0; // whether this tree is one of the mixed share (row 47)
    private const long MixedWoodsPickSalt = 0x2852_A2C0; // which other kind it takes (row 53)
    private const long TreeShapeSalt = 0x2853_5E00;      // the per-tree shape hash of the generation-29 builders (row 43)

    /// <summary>The lower half of the grove value's measured CDF (2026-10-20: 825 000 columns over every planet type and six
    /// seeds; mean 0.4995, sd 0.118, symmetric around 0.5): the grove values <see cref="GroveCdfValue"/> at which the share
    /// <see cref="GroveCdfShare"/> of all columns lies below. The upper half mirrors it. Pure table + linear interpolation —
    /// no transcendental function, so the flattening is bit-identical on every runtime.</summary>
    private static readonly double[] GroveCdfValue =
    {
        0.0, 0.1543, 0.1819, 0.2059, 0.2324, 0.2486, 0.2716, 0.3062, 0.3292, 0.3472, 0.3622, 0.3754, 0.3873, 0.3982, 0.4084, 0.4180,
        0.4272, 0.4361, 0.4447, 0.4530, 0.4611, 0.4690, 0.4769, 0.4846, 0.4924, 0.5,
    };

    private static readonly double[] GroveCdfShare =
    {
        0.0, 0.001, 0.0025, 0.005, 0.01, 0.015, 0.025, 0.05, 0.075, 0.10, 0.125, 0.15, 0.175, 0.20, 0.225, 0.25,
        0.275, 0.30, 0.325, 0.35, 0.375, 0.40, 0.425, 0.45, 0.475, 0.5,
    };

    /// <summary>The eight horizontal directions in ring order: consecutive entries differ by one axis step, which the spiral
    /// trunk walks and the frond bursts fan out along.</summary>
    private static readonly (int X, int Z)[] TreeDirs8 =
    {
        (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
    };

    /// <summary>The four orthogonal directions (a lean or a stub stays face-connected to the trunk).</summary>
    private static readonly (int X, int Z)[] TreeDirs4 = { (1, 0), (0, 1), (-1, 0), (0, -1) };

    // ---------------- blocks + slots (#2851) ----------------

    /// <summary>Resolves the blocks a tree can be built from, once per chunk. The classic ones fall back exactly as the
    /// ground pass always did; the generation-29 slots fall back to the common leaf / trunk when the content lacks them.</summary>
    private TreeBlocks ResolveTreeBlocks(BlockId logId, BlockId leafId, BlockId deadLogId)
    {
        BlockId Or(string key, BlockId fallback) => _content.GetBlock(key)?.NumericId ?? fallback;
        return new TreeBlocks(
            logId,
            leafId,
            Or("pine_needles", leafId),
            Or("palm_frond", leafId),
            deadLogId,
            Or("mushroom_stem", logId),
            Or("mushroom_cap", leafId),
            Or("crystal", leafId),
            Or(TreeFoliage.LeavesBKey, leafId),
            Or(TreeFoliage.LeavesCKey, leafId),
            Or(TreeFoliage.BlossomKey, leafId),
            Or(TreeWood.PaleLogKey, logId),
            Or(SaguaroKey, leafId));
    }

    /// <summary>The foliage a tree kind's crown is built from — the block its fruit hangs under. Below generation 29 the
    /// classic rule (needles, fronds, else the common leaf); from generation 29 the slot table of the design (#2851):
    /// slot A for broadleaf / oak / jungle / ball, slot B for willow / mangrove / umbrella / birch, slot C for baobab /
    /// bamboo / column / tree fern / spiral / bottle / lantern, the blossom crown, needles, fronds and the saguaro body.</summary>
    private BlockId LeavesFor(TreeKind kind, in TreeBlocks b)
    {
        if (_terrainGeneration < WorldDescription.TreeVarietyGeneration)
        {
            return kind == TreeKind.Conifer ? b.Pine : kind == TreeKind.Palm ? b.Palm : b.Leaf;
        }

        return LeavesForGen29(kind, b);
    }

    private static BlockId LeavesForGen29(TreeKind kind, in TreeBlocks b) => kind switch
    {
        TreeKind.Willow or TreeKind.Mangrove or TreeKind.Umbrella or TreeKind.Birch or TreeKind.Bottle => b.LeafB,
        TreeKind.Baobab or TreeKind.Bamboo or TreeKind.Column or TreeKind.TreeFern or TreeKind.Spiral
            or TreeKind.Lantern => b.LeafC,
        TreeKind.Blossom => b.Blossom,
        TreeKind.Conifer => b.Pine,
        TreeKind.Palm => b.Palm,
        TreeKind.Saguaro => b.Saguaro,
        TreeKind.MushroomTree => b.Cap,
        TreeKind.CrystalTree => b.Crystal,
        _ => b.Leaf,
    };

    /// <summary>The trunk a generation-29 kind is built from: the pale bark for birch and bottle trees, the dead wood for a
    /// snag, else the common log (#2851).</summary>
    private static BlockId TrunkForGen29(TreeKind kind, in TreeBlocks b) => kind switch
    {
        TreeKind.Birch or TreeKind.Bottle => b.PaleLog,
        TreeKind.Dead => b.DeadLog,
        TreeKind.MushroomTree => b.Stem,
        TreeKind.CrystalTree => b.Crystal,
        TreeKind.Saguaro => b.Saguaro,
        _ => b.Log,
    };

    /// <summary>The block keys of a kind's crown and trunk on a generation-29 world (tests, and the slot table in one
    /// readable place): null where the kind has none.</summary>
    internal static (string? Leaves, string? Trunk) TreeSlotKeysFor(TreeKind kind) => kind switch
    {
        TreeKind.Broadleaf or TreeKind.Oak or TreeKind.Jungle or TreeKind.Ball => (TreeFoliage.LeavesKey, TreeWood.LogKey),
        TreeKind.Willow or TreeKind.Mangrove or TreeKind.Umbrella => (TreeFoliage.LeavesBKey, TreeWood.LogKey),
        TreeKind.Birch => (TreeFoliage.LeavesBKey, TreeWood.PaleLogKey),
        TreeKind.Bottle => (TreeFoliage.LeavesBKey, TreeWood.PaleLogKey), // pale trunk + crown B: one species (tr2)
        TreeKind.Baobab or TreeKind.Bamboo or TreeKind.Column or TreeKind.TreeFern or TreeKind.Spiral or TreeKind.Lantern
            => (TreeFoliage.LeavesCKey, TreeWood.LogKey),
        TreeKind.Blossom => (TreeFoliage.BlossomKey, TreeWood.LogKey),
        TreeKind.Conifer => (TreeFoliage.NeedlesKey, TreeWood.LogKey),
        TreeKind.Palm => (TreeFoliage.FrondKey, TreeWood.LogKey),
        TreeKind.Saguaro => (SaguaroKey, null),
        TreeKind.Dead => (null, TreeWood.LogKey),
        TreeKind.MushroomTree => ("mushroom_cap", "mushroom_stem"),
        TreeKind.CrystalTree => ("crystal", "crystal"),
        _ => (null, null),
    };

    /// <summary>True for a kind with a leafy crown — a palette holding one gains the lantern tree on a luminous world.</summary>
    private static bool IsLeafyTreeKind(TreeKind kind) => kind is not (TreeKind.None or TreeKind.Dead or TreeKind.Saguaro
        or TreeKind.MushroomTree or TreeKind.CrystalTree);

    // ---------------- the world's glow level (#2857), memoised ----------------

    private sealed class TreeGlowMemo
    {
        public TreeGlowMemo(string planetKey, long rosterSeed, int generation, GlowLevel level)
        {
            PlanetKey = planetKey;
            RosterSeed = rosterSeed;
            Generation = generation;
            Level = level;
        }

        public string PlanetKey { get; }

        public long RosterSeed { get; }

        public int Generation { get; }

        public GlowLevel Level { get; }
    }

    // One immutable memo, swapped whole — a chunk on another thread reads either the old or the new one, never a mix.
    private TreeGlowMemo? _treeGlowMemo;

    /// <summary>This world's glow level for the trees (memoised per planet, body and generation like ResolveFlora): the
    /// crystal tree needs at least <see cref="GlowLevel.Low"/>, the lantern tree <see cref="GlowLevel.Luminous"/>.</summary>
    private GlowLevel TreeGlowLevel(PlanetType planet)
    {
        long roster = RosterSeed;
        var memo = _treeGlowMemo;
        if (memo != null && memo.PlanetKey == planet.Key && memo.RosterSeed == roster && memo.Generation == _terrainGeneration)
        {
            return memo.Level;
        }

        var level = GlowRules.LevelFor(planet, roster, _terrainGeneration);
        _treeGlowMemo = new TreeGlowMemo(planet.Key, roster, _terrainGeneration, level);
        return level;
    }

    // ---------------- per-chunk state of the generation-29 pick ----------------

    /// <summary>What the generation-29 tree pick needs per chunk: every biome's effective weighted palette (crystal trees
    /// dropped on a world without glow, the lantern tree added on a luminous one), its total weight, whether it is an alien
    /// palette, and the extra grounds of #2849.</summary>
    private sealed class TreeVarietyChunk
    {
        public TreeVarietyChunk(TreeWeight[][] palettes, int[] totals, bool[] alien, BlockId sand, BlockId alienGrass,
            BlockId mycelium, BlockId crystal, BlockId stone)
        {
            Palettes = palettes;
            Totals = totals;
            Alien = alien;
            Sand = sand;
            AlienGrass = alienGrass;
            Mycelium = mycelium;
            Crystal = crystal;
            Stone = stone;
        }

        public TreeWeight[][] Palettes { get; }

        public int[] Totals { get; }

        public bool[] Alien { get; }

        public BlockId Sand { get; }

        public BlockId AlienGrass { get; }

        public BlockId Mycelium { get; }

        public BlockId Crystal { get; }

        public BlockId Stone { get; }

        /// <summary>The generation-29 grounds a kind roots in ON TOP of the classic earthy / sandy rule (#2849): sand also
        /// for bottle, column and umbrella trees; alien grass for spiral, mushroom and crystal trees (and for the broadleaf,
        /// the snag and the lantern tree of an alien palette); mycelium for mushroom trees; crystal and stone for crystal
        /// trees.</summary>
        public bool ExtraGroundOk(TreeKind kind, BlockId surf, int biome)
        {
            if (surf.IsAir)
            {
                return false;
            }

            if (surf == Sand)
            {
                return kind is TreeKind.Bottle or TreeKind.Column or TreeKind.Umbrella;
            }

            if (surf == AlienGrass)
            {
                return kind is TreeKind.Spiral or TreeKind.MushroomTree or TreeKind.CrystalTree
                    || (Alien[biome] && kind is TreeKind.Broadleaf or TreeKind.Dead or TreeKind.Lantern);
            }

            if (surf == Mycelium)
            {
                return kind == TreeKind.MushroomTree;
            }

            return (surf == Crystal || surf == Stone) && kind == TreeKind.CrystalTree;
        }
    }

    /// <summary>The generation-29 pick state of this chunk, or null below generation 29 (the classic pick runs there).</summary>
    private TreeVarietyChunk? TreeVarietyFor(PlanetType planet, List<BiomeResolved> biomes)
    {
        if (_terrainGeneration < WorldDescription.TreeVarietyGeneration)
        {
            return null;
        }

        var glow = TreeGlowLevel(planet);
        var palettes = new TreeWeight[biomes.Count][];
        var totals = new int[biomes.Count];
        var alien = new bool[biomes.Count];
        for (int i = 0; i < biomes.Count; i++)
        {
            palettes[i] = EffectiveTreePalette(biomes[i].Theme, _terrainGeneration, glow);
            totals[i] = TotalWeight(palettes[i]);
            alien[i] = (biomes[i].Theme.Preferred & FloraTag.Alien) != 0;
        }

        BlockId Id(string key) => _content.GetBlock(key)?.NumericId ?? BlockId.Air;
        return new TreeVarietyChunk(palettes, totals, alien, Id("sand"), Id("alien_grass"), Id("mycelium"), Id("crystal"), Id("stone"));
    }

    /// <summary>A theme's weighted palette as it grows on a world of this glow level (#2849, #2857): no crystal tree on a
    /// world without glow, and the lantern tree (weight <see cref="LanternTreeWeight"/>) joins every palette with a leafy
    /// kind on a luminous world. Entries of weight 0 and <see cref="TreeKind.None"/> drop out.</summary>
    internal static TreeWeight[] EffectiveTreePalette(FloraThemes.Theme theme, int generation, GlowLevel glow)
    {
        var source = theme.WeightedPaletteFor(generation);
        var list = new List<TreeWeight>(source.Length + 1);
        bool leafy = false, lantern = false;
        foreach (var e in source)
        {
            if (e.Kind == TreeKind.None || e.Weight <= 0 || (e.Kind == TreeKind.CrystalTree && glow == GlowLevel.None))
            {
                continue;
            }

            leafy |= IsLeafyTreeKind(e.Kind);
            lantern |= e.Kind == TreeKind.Lantern;
            list.Add(e);
        }

        if (glow == GlowLevel.Luminous && leafy && !lantern)
        {
            list.Add(new TreeWeight(TreeKind.Lantern, LanternTreeWeight));
        }

        return list.ToArray();
    }

    private static int TotalWeight(TreeWeight[] palette)
    {
        int total = 0;
        foreach (var e in palette)
        {
            total += e.Weight;
        }

        return total;
    }

    // ---------------- the fair pick (#2848) + mixed woods (#2852) ----------------

    /// <summary>Flattens a grove value to a uniform share in [0, 1): the share of all columns whose grove value lies below
    /// <paramref name="grove"/>, interpolated linearly in the measured CDF table (mirrored around 0.5).</summary>
    internal static double FlattenGrove(double grove)
    {
        if (!(grove > 0.0))
        {
            return 0.0; // also NaN
        }

        if (grove >= 1.0)
        {
            return 1.0 - 1e-12;
        }

        bool upper = grove > 0.5;
        double g = upper ? 1.0 - grove : grove;
        int i = 1;
        while (i < GroveCdfValue.Length - 1 && GroveCdfValue[i] < g)
        {
            i++;
        }

        double x0 = GroveCdfValue[i - 1], x1 = GroveCdfValue[i];
        double t = x1 > x0 ? (g - x0) / (x1 - x0) : 0.0;
        double share = GroveCdfShare[i - 1] + (GroveCdfShare[i] - GroveCdfShare[i - 1]) * t;
        double u = upper ? 1.0 - share : share;
        return u >= 1.0 ? 1.0 - 1e-12 : u < 0.0 ? 0.0 : u;
    }

    /// <summary>Picks a kind from a weighted palette by a uniform value in [0, 1) — kind <c>i</c> owns the slice of width
    /// <c>weight_i / total</c>. <paramref name="skip"/> leaves one kind out (the mixed pick never re-picks the grove's lead
    /// kind); <paramref name="total"/> must then be the total without it.</summary>
    internal static TreeKind WeightedTreePick(TreeWeight[] palette, int total, double u, TreeKind skip = TreeKind.None)
    {
        if (total <= 0)
        {
            return TreeKind.None;
        }

        int target = (int)(u * total);
        if (target >= total)
        {
            target = total - 1;
        }

        if (target < 0)
        {
            target = 0;
        }

        var last = TreeKind.None;
        foreach (var e in palette)
        {
            if (e.Kind == skip && skip != TreeKind.None)
            {
                continue;
            }

            last = e.Kind;
            if (target < e.Weight)
            {
                return e.Kind;
            }

            target -= e.Weight;
        }

        return last;
    }

    private static int WeightOf(TreeWeight[] palette, TreeKind kind)
    {
        foreach (var e in palette)
        {
            if (e.Kind == kind)
            {
                return e.Weight;
            }
        }

        return 0;
    }

    /// <summary>The generation-29 pick of one column: the grove's lead kind from the flattened grove value, and for
    /// <see cref="MixedWoodsShare"/> of the trees another kind of the same palette by a stateless hash.</summary>
    private TreeKind PickTreeKindGen29(TreeWeight[] palette, int total, long seed, int wx, int wz, double terrainScale)
        => PickTreeKindGen29(palette, total, seed, wx, wz, terrainScale, out _, out _);

    private TreeKind PickTreeKindGen29(TreeWeight[] palette, int total, long seed, int wx, int wz, double terrainScale,
        out TreeKind lead, out bool mixed)
    {
        mixed = false;
        if (palette.Length == 0)
        {
            lead = TreeKind.None;
            return lead;
        }

        if (palette.Length == 1)
        {
            lead = palette[0].Kind;
            return lead;
        }

        double grove = FbmT(seed + 0x70EE17, wx, wz, terrainScale * 3.0, octaves: 2); // the classic grove field (#1648)
        lead = WeightedTreePick(palette, total, FlattenGrove(grove));
        int cx = WorldConstants.WrapX(wx, _circumference), cz = Wz(wz);
        if (Noise.Value01(seed + MixedWoodsRollSalt, cx, 47, cz) >= MixedWoodsShare)
        {
            return lead;
        }

        mixed = true;
        return WeightedTreePick(palette, total - WeightOf(palette, lead), Noise.Value01(seed + MixedWoodsPickSalt, cx, 53, cz), lead);
    }

    // ---------------- the generation-29 builders ----------------

    /// <summary>One tree's root column and its per-tree rolls: the size factors of the classic builders and a 20-bit shape
    /// hash the generation-29 builders read their variations from.</summary>
    private readonly struct TreeSite
    {
        public TreeSite(int wx, int sy, int wz, double sizeF, double hJit, double cJit, int shape)
        {
            Wx = wx;
            Sy = sy;
            Wz = wz;
            SizeF = sizeF;
            HJit = hJit;
            CJit = cJit;
            Shape = shape;
        }

        public readonly int Wx, Sy, Wz;
        public readonly double SizeF, HJit, CJit;
        public readonly int Shape;

        /// <summary><paramref name="count"/> bits of the shape hash from <paramref name="shift"/> on.</summary>
        public int Bits(int shift, int count) => (Shape >> shift) & ((1 << count) - 1);
    }

    /// <summary>Builds one tree of a kind on a generation-29 world: the ten new shapes (#2853), the shape variants of the
    /// classic kinds (#2854), and every kind from its slot blocks (#2851).</summary>
    private void BuildTreeOfKindGen29(TreeKind kind, int wx, int sy, int wz, double sizeF, double hJit, double cJit, long seed,
        in TreeBlocks b, System.Action<int, int, int, BlockId, bool> set)
    {
        int cx = WorldConstants.WrapX(wx, _circumference), cz = Wz(wz);
        int shape = (int)(Noise.Value01(seed + TreeShapeSalt, cx, 43, cz) * (1 << 20));
        switch (kind)
        {
            case TreeKind.Bamboo:
                BuildBamboo(wx, sy, wz, sizeF, hJit, (int)(Noise.Value01(seed + 0xBA3B0, cx, 41, cz) * 997), b.Log, b.LeafC, set);
                return;
            case TreeKind.Saguaro:
                BuildSaguaro(wx, sy, wz, sizeF, hJit, (int)(Noise.Value01(seed + 0x5A6A0, cx, 41, cz) * 997), b.Saguaro, set);
                return;
        }

        BuildGen29(kind, new TreeSite(wx, sy, wz, sizeF, hJit, cJit, shape), b, set);
    }

    /// <summary>The generation-29 dispatch for a site (shared by the stamp and the test seam).</summary>
    private static void BuildGen29(TreeKind kind, in TreeSite t, in TreeBlocks b, System.Action<int, int, int, BlockId, bool> set)
    {
        var leaf = LeavesForGen29(kind, b);
        var log = TrunkForGen29(kind, b);
        switch (kind)
        {
            // #2853: the ten new shapes
            case TreeKind.Birch: BuildBirch(t, log, leaf, set); break;
            case TreeKind.Oak: BuildOak(t, log, leaf, set); break;
            case TreeKind.Column: BuildColumn(t, log, leaf, set); break;
            case TreeKind.Umbrella: BuildUmbrella(t, log, leaf, set); break;
            case TreeKind.Blossom: BuildBlossom(t, log, leaf, set); break;
            case TreeKind.TreeFern: BuildTreeFern(t, log, leaf, set); break;
            case TreeKind.Bottle: BuildBottle(t, log, leaf, set); break;
            case TreeKind.Spiral: BuildSpiral(t, log, leaf, set); break;
            case TreeKind.Ball: BuildBall(t, log, leaf, set); break;
            case TreeKind.Lantern: BuildLantern(t, log, leaf, set); break;

            // #2854: shape variety within the classic kinds
            case TreeKind.Conifer: BuildConiferGen29(t, log, leaf, set); break;
            case TreeKind.Palm: BuildPalmGen29(t, log, leaf, set); break;
            case TreeKind.Jungle: BuildJungleGen29(t, log, leaf, set); break;
            case TreeKind.Willow: BuildWillowGen29(t, log, leaf, set); break;
            case TreeKind.Baobab: BuildBaobabGen29(t, log, leaf, set); break;

            // the classic shapes, from their slot blocks
            case TreeKind.Dead: BuildDead(t.Wx, t.Sy, t.Wz, t.SizeF, t.HJit, log, set); break;
            case TreeKind.Mangrove: BuildMangrove(t.Wx, t.Sy, t.Wz, t.SizeF, t.HJit, t.CJit, log, leaf, set); break;
            case TreeKind.Bamboo: BuildBamboo(t.Wx, t.Sy, t.Wz, t.SizeF, t.HJit, t.Shape % 997, log, leaf, set); break;
            case TreeKind.Saguaro: BuildSaguaro(t.Wx, t.Sy, t.Wz, t.SizeF, t.HJit, t.Shape % 997, leaf, set); break;
            case TreeKind.MushroomTree: BuildMushroomTree(t.Wx, t.Sy, t.Wz, t.SizeF, t.HJit, b.Stem, b.Cap, set); break;
            case TreeKind.CrystalTree: BuildCrystalTree(t.Wx, t.Sy, t.Wz, t.SizeF, t.HJit, b.Crystal, set); break;
            default: BuildBroadleafGen29(t, log, leaf, set); break;
        }
    }

    private static int Clamp(double v, int min, int max) => System.Math.Clamp((int)System.Math.Round(v), min, max);

    /// <summary>A per-cell hash in 0..2^31 for ragged rims and gaps — integer arithmetic only.</summary>
    private static int TreeCellHash(int shape, int dx, int dy, int dz)
    {
        unchecked
        {
            uint h = (uint)shape * 0x9E3779B1u ^ (uint)(dx + 16) * 0x85EBCA77u ^ (uint)(dy + 64) * 0xC2B2AE3Du ^ (uint)(dz + 16) * 0x27D4EB2Fu;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (int)(h & 0x7FFFFFFFu);
        }
    }

    /// <summary>A straight trunk from the ground cell above <paramref name="sy"/> up to <paramref name="topY"/> (a trunk
    /// is solid: it overwrites, like every classic trunk).</summary>
    private static void TreeTrunk(int wx, int sy, int wz, int topY, BlockId log, System.Action<int, int, int, BlockId, bool> set)
    {
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, log, true);
        }
    }

    /// <summary>A round leaf blob (dx² + dy² + dz² ≤ r² + slack) that only fills air, never lower than
    /// <paramref name="floorY"/>.</summary>
    private static void TreeBlob(int cx, int cy, int cz, int r, int slack, int floorY, BlockId leaf,
        System.Action<int, int, int, BlockId, bool> set)
    {
        for (int dy = -r; dy <= r; dy++)
        {
            if (cy + dy < floorY)
            {
                continue;
            }

            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dy * dy + dz * dz <= r * r + slack)
                    {
                        set(cx + dx, cy + dy, cz + dz, leaf, false);
                    }
                }
        }
    }

    /// <summary>A trunk that bends <paramref name="lean"/> cells towards <paramref name="dir"/> by its top, the bend growing
    /// with the square of the height (a curved palm, a leaning acacia). Each step aside also fills the cell beside it, so
    /// the trunk stays face-connected. Returns the top cell's horizontal offset.</summary>
    private static (int X, int Z) LeaningTrunk(int wx, int sy, int wz, int height, int lean, (int X, int Z) dir, BlockId log,
        System.Action<int, int, int, BlockId, bool> set)
    {
        int h2 = height * height;
        int prev = 0;
        for (int k = 1; k <= height; k++)
        {
            int off = (lean * k * k + h2 / 2) / h2; // never steps more than one cell at a time for lean ≤ 2, height ≥ 4
            if (off > prev)
            {
                set(wx + dir.X * prev, sy + k, wz + dir.Z * prev, log, false); // the step aside, face to face
                set(wx + dir.X * off, sy + k, wz + dir.Z * prev, log, false);  // and across a diagonal lean's corner
            }

            set(wx + dir.X * off, sy + k, wz + dir.Z * off, log, off == 0);
            prev = off;
        }

        return (dir.X * prev, dir.Z * prev);
    }

    // ---------- #2853: the ten new shapes ----------

    /// <summary>A birch: a thin pale trunk 6–11 tall under a loose, narrow, upright crown whose rim shows gaps, and the odd
    /// twig with a tuft.</summary>
    private static void BuildBirch(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(8.0 * t.SizeF * t.HJit, 6, 11);
        int rx = Clamp(1.6 * t.SizeF * t.CJit, 1, 2);
        int ry = rx + 2;
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);

        if (t.Bits(0, 2) != 0)
        {
            var d = TreeDirs4[t.Bits(2, 2)];
            int ty = t.Sy + height / 2;
            set(t.Wx + d.X, ty, t.Wz + d.Z, log, false); // a twig
            set(t.Wx + d.X * 2, ty + 1, t.Wz + d.Z * 2, leaf, false); // and its tuft
        }

        int cy = topY - 1;
        int rx2 = rx * rx, ry2 = ry * ry;
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
                for (int dz = -rx; dz <= rx; dz++)
                {
                    int d2 = dx * dx + dz * dz;
                    if (d2 * ry2 + dy * dy * rx2 > rx2 * ry2 + ry2)
                    {
                        continue;
                    }

                    bool rim = d2 >= rx2 || System.Math.Abs(dy) >= ry - 1;
                    if (rim && TreeCellHash(t.Shape, dx, dy, dz) % 3 == 0)
                    {
                        continue; // the loose crown: a third of the rim stays open
                    }

                    set(t.Wx + dx, cy + dy, t.Wz + dz, leaf, false);
                }
    }

    /// <summary>An oak: a stout trunk 4–7 tall (2×2 when the tree is large) under a wide crown of two or three overlapping
    /// blobs, each side blob carried by a short branch.</summary>
    private static void BuildOak(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.0 * t.SizeF * t.HJit, 4, 7);
        int r = Clamp(2.4 * t.SizeF * t.CJit, 2, 3);
        bool thick = t.SizeF * t.HJit >= 1.06 || (t.SizeF >= 1.0 && t.Bits(0, 2) == 0);
        int topY = t.Sy + height;
        for (int ty = t.Sy + 1; ty <= topY; ty++)
        {
            set(t.Wx, ty, t.Wz, log, true);
            if (thick)
            {
                set(t.Wx + 1, ty, t.Wz, log, false); // like the baobab, the offset columns fill air only
                set(t.Wx, ty, t.Wz + 1, log, false);
                set(t.Wx + 1, ty, t.Wz + 1, log, false);
            }
        }

        int extra = 1 + t.Bits(2, 1);
        int baseDir = t.Bits(3, 3);
        int rb = 2;
        int off = 4 - rb; // a side blob's centre + its radius stays inside the 4-cell margin
        for (int i = 0; i < extra; i++)
        {
            var d = TreeDirs8[(baseDir + i * 3) & 7];
            int by = topY + t.Bits(6 + i, 1) - 1;
            // The branch, face to face from the trunk to its tip: a diagonal one turns its corner, a raised tip stands on
            // the cell below it (the woodcutter follows the wood 6-connected, and a mined trunk must not leave a branch
            // floating).
            if (d.X != 0 && d.Z != 0)
            {
                set(t.Wx + d.X, topY - 1, t.Wz, log, false);
                set(t.Wx + d.X * 2, topY - 1, t.Wz + d.Z, log, false);
            }

            set(t.Wx + d.X, topY - 1, t.Wz + d.Z, log, false);
            set(t.Wx + d.X * 2, topY - 1, t.Wz + d.Z * 2, log, false);
            set(t.Wx + d.X * 2, by, t.Wz + d.Z * 2, log, false);
            TreeBlob(t.Wx + d.X * off, by + 1, t.Wz + d.Z * off, rb, 1, t.Sy + 2, leaf, set);
        }

        TreeBlob(t.Wx, topY + 1, t.Wz, r, 1, t.Sy + 2, leaf, set);
    }

    /// <summary>A column tree (cypress): a narrow, tall green spindle 8–14 high — three cells across (five on a large one),
    /// tapering to a single-cell tip, the trunk hidden inside.</summary>
    private static void BuildColumn(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int total = Clamp(11.0 * t.SizeF * t.HJit, 8, 14);
        int rMax = t.SizeF * t.CJit >= 1.12 ? 2 : 1;
        int top = t.Sy + total;
        TreeTrunk(t.Wx, t.Sy, t.Wz, top - 2, log, set);
        int start = t.Sy + 2;
        int len = top - start;
        for (int y = start; y <= top; y++)
        {
            int k = y - start;
            int r = k >= len - 1 ? 0 : k >= len - 3 || k == 0 ? 1 : rMax;
            int slack = r == 1 ? (y + t.Shape) & 1 : r; // r 1 alternates a plus and a full 3×3, r 2 is a rounded 5×5
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz <= r * r + slack)
                    {
                        set(t.Wx + dx, y, t.Wz + dz, leaf, false);
                    }
                }
        }
    }

    /// <summary>An umbrella tree (acacia): a trunk 4–7 tall leaning 1–2 cells aside under a flat, wide disc crown one or
    /// two cells thick; the lean plus the crown radius never exceed the 4-cell margin.</summary>
    private static void BuildUmbrella(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.5 * t.SizeF * t.HJit, 4, 7);
        var dir = TreeDirs8[t.Bits(0, 3)];
        int lean = t.Bits(3, 2) == 0 ? 2 : 1; // a strong lean on a quarter of them — it costs crown width (margin)
        int r = Clamp(3.2 * t.SizeF * t.CJit, 2, 4 - lean);
        var top = LeaningTrunk(t.Wx, t.Sy, t.Wz, height, lean, dir, log, set);
        int cx = t.Wx + top.X, cz = t.Wz + top.Z;
        int y0 = t.Sy + height + 1;
        bool thick = t.Bits(5, 1) == 0;
        for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                int d2 = dx * dx + dz * dz;
                if (d2 <= r * r + 1)
                {
                    set(cx + dx, y0, cz + dz, leaf, false);
                }

                if (thick && d2 <= (r - 1) * (r - 1) + 1)
                {
                    set(cx + dx, y0 + 1, cz + dz, leaf, false);
                }
            }
    }

    /// <summary>A blossom tree: a short trunk 3–6 tall forking into two stubs under a round crown of blossoms.</summary>
    private static void BuildBlossom(in TreeSite t, BlockId log, BlockId blossom, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(4.5 * t.SizeF * t.HJit, 3, 6);
        int r = Clamp(2.2 * t.SizeF * t.CJit, 2, 3);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        var d = TreeDirs4[t.Bits(0, 2)];
        set(t.Wx + d.X, topY, t.Wz + d.Z, log, false); // the fork
        set(t.Wx - d.X, topY, t.Wz - d.Z, log, false);
        TreeBlob(t.Wx, topY + 1, t.Wz, r, 1, topY, blossom, set);
    }

    /// <summary>A tree fern: a thin trunk 3–6 tall under a crown of 4–8 long fronds that arch out and droop at the tips.</summary>
    private static void BuildTreeFern(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(4.5 * t.SizeF * t.HJit, 3, 6);
        int fronds = 4 + t.Shape % 5;
        int len = Clamp(3.0 * t.SizeF * t.CJit, 2, 4);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        set(t.Wx, topY + 1, t.Wz, leaf, false); // the crown's heart
        int start = t.Bits(3, 3);
        for (int i = 0; i < fronds; i++)
        {
            var d = TreeDirs8[(start + i * 8 / fronds) & 7];
            for (int s = 1; s <= len; s++)
            {
                int y = topY + 1 - System.Math.Max(0, s - 2); // level for two cells, then down a cell per step
                set(t.Wx + d.X * s, y, t.Wz + d.Z * s, leaf, false);
                if (s == len && len >= 3)
                {
                    set(t.Wx + d.X * s, y - 1, t.Wz + d.Z * s, leaf, false); // the drooping tip
                }
            }
        }
    }

    /// <summary>A bottle tree: a pale trunk 4–8 tall that bulges in its lower half (a ring of bark, on a large tree a full
    /// 3×3), a short neck and a small crown on a few stubby branches.</summary>
    private static void BuildBottle(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.5 * t.SizeF * t.HJit, 4, 8);
        bool fat = t.SizeF * t.CJit >= 1.05;
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        int lo = t.Sy + (height >= 6 ? 2 : 1); // a taller tree stands on a narrow foot under its belly
        int hi = t.Sy + System.Math.Max(2, height * 2 / 3);
        int mid = (lo + hi) / 2;
        for (int y = lo; y <= hi; y++)
        {
            foreach (var d in TreeDirs4)
            {
                set(t.Wx + d.X, y, t.Wz + d.Z, log, false);
            }

            if (fat && (y == mid || y == mid + 1) && y < hi)
            {
                for (int i = 1; i < 8; i += 2)
                {
                    set(t.Wx + TreeDirs8[i].X, y, t.Wz + TreeDirs8[i].Z, log, false); // the belly's corners
                }
            }
        }

        foreach (var d in TreeDirs4)
        {
            set(t.Wx + d.X, topY, t.Wz + d.Z, log, false); // stubby branches
        }

        int rc = 1 + t.Bits(0, 1);
        for (int dx = -rc; dx <= rc; dx++)
            for (int dz = -rc; dz <= rc; dz++)
            {
                int d2 = dx * dx + dz * dz;
                if (d2 <= rc * rc + 1)
                {
                    set(t.Wx + dx, topY + 1, t.Wz + dz, leaf, false);
                }

                if (d2 <= (rc - 1) * (rc - 1) + 1)
                {
                    set(t.Wx + dx, topY + 2, t.Wz + dz, leaf, false);
                }
            }
    }

    /// <summary>A spiral tree: a two-cell foot, then a trunk that winds around its column like a corkscrew (one ring step
    /// per cell of height, 7–13 tall), a leaf clump on the tip and two short twigs with tufts higher up.</summary>
    private static void BuildSpiral(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(9.0 * t.SizeF * t.HJit, 7, 13);
        bool clockwise = t.Bits(0, 1) == 0;
        int phase = t.Bits(1, 3);
        set(t.Wx, t.Sy + 1, t.Wz, log, true);
        set(t.Wx, t.Sy + 2, t.Wz, log, true);
        int px = 0, pz = 0;
        int twigA = t.Sy + height * 3 / 5, twigB = t.Sy + height * 4 / 5;
        for (int k = 3; k <= height; k++)
        {
            int y = t.Sy + k;
            var p = TreeDirs8[(clockwise ? phase - k : phase + k) & 7];
            set(t.Wx + px, y, t.Wz + pz, log, false); // up from the previous turn, face to face
            if (System.Math.Abs(p.X - px) + System.Math.Abs(p.Z - pz) == 2)
            {
                set(t.Wx + p.X, y, t.Wz + pz, log, false); // a diagonal step (the first turn): the corner joins it face to face
            }

            set(t.Wx + p.X, y, t.Wz + p.Z, log, false);
            px = p.X;
            pz = p.Z;
            if (y == twigA || y == twigB)
            {
                if (p.X != 0 && p.Z != 0)
                {
                    set(t.Wx + p.X * 2, y, t.Wz + p.Z, log, false); // a diagonal twig: its corner, face to face
                }

                set(t.Wx + p.X * 2, y, t.Wz + p.Z * 2, log, false); // a twig outward
                TreeBlob(t.Wx + p.X * 3, y, t.Wz + p.Z * 3, 1, 0, t.Sy + 1, leaf, set);
            }
        }

        int rc = Clamp(1.8 * t.SizeF * t.CJit, 1, 2);
        TreeBlob(t.Wx + px, t.Sy + height + 1, t.Wz + pz, rc, 1, t.Sy + 1, leaf, set);
    }

    /// <summary>A ball tree ("lollipop"): a straight trunk 4–7 tall under a perfectly round, filled sphere of radius 2–3.</summary>
    private static void BuildBall(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.0 * t.SizeF * t.HJit, 4, 7);
        int r = Clamp(2.5 * t.SizeF * t.CJit, 2, 3);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        TreeBlob(t.Wx, topY + r - 1, t.Wz, r, r, t.Sy + 1, leaf, set); // r² + r: the roundest voxel sphere of that radius
    }

    /// <summary>A lantern tree: a trunk 4–7 tall under a low dome whose flat rim is where its glowing fruit hangs (the fruit
    /// itself is hung by the fruit recorder, <c>WorldGenerator.FruitTreesGen14.cs</c>).</summary>
    private static void BuildLantern(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.0 * t.SizeF * t.HJit, 4, 7);
        int r = Clamp(2.6 * t.SizeF * t.CJit, 2, 3);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        for (int dy = 0; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz + 2 * dy * dy <= r * r + 1)
                    {
                        set(t.Wx + dx, topY + dy, t.Wz + dz, leaf, false);
                    }
                }
    }

    // ---------- #2854: shape variety within the classic kinds ----------

    /// <summary>The generation-29 broadleaf: the classic sizes, a crown of one to three overlapping blobs, a trunk that
    /// leans a cell in a quarter of the trees, and the odd branch stub.</summary>
    private static void BuildBroadleafGen29(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.5 * t.SizeF * t.HJit, 3, 10);
        int r = Clamp(2.0 * t.SizeF * t.CJit, 1, 3);
        int lean = height >= 5 && t.Bits(0, 2) == 0 ? 1 : 0;
        var leanDir = TreeDirs4[t.Bits(2, 2)];
        int bend = t.Sy + height * 2 / 3;
        for (int ty = t.Sy + 1; ty <= t.Sy + height; ty++)
        {
            int off = ty > bend ? lean : 0;
            if (off > 0 && ty == bend + 1)
            {
                set(t.Wx, ty, t.Wz, log, false); // the bend, face to face
            }

            set(t.Wx + leanDir.X * off, ty, t.Wz + leanDir.Z * off, log, off == 0);
        }

        int cx = t.Wx + leanDir.X * lean, cz = t.Wz + leanDir.Z * lean;
        int topY = t.Sy + height;
        if (height >= 5 && t.Bits(4, 2) == 0)
        {
            var s = TreeDirs4[t.Bits(6, 2)];
            set(t.Wx + s.X, t.Sy + height / 2, t.Wz + s.Z, log, false); // a branch stub
        }

        int extra = height >= 5 ? t.Bits(8, 2) % 3 : 0;
        int rb = System.Math.Max(1, r - 1);
        int off2 = System.Math.Min(2, 4 - lean - rb);
        for (int i = 0; i < extra; i++)
        {
            var d = TreeDirs8[(t.Bits(10, 3) + i * 3) & 7];
            TreeBlob(cx + d.X * off2, topY - 1 + t.Bits(13 + i, 1), cz + d.Z * off2, rb, 1, t.Sy + 2, leaf, set);
        }

        // The main crown — the classic broadleaf's sphere (dy from -1).
        for (int dy = -1; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= r * r + 1)
                    {
                        set(cx + dx, topY + dy, cz + dz, leaf, false);
                    }
                }
    }

    /// <summary>The generation-29 conifer: the classic cone, but in uneven tiers (every third layer pulled in a cell), with
    /// ragged rims, the odd missing layer and now and then a twin top.</summary>
    private static void BuildConiferGen29(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(7.0 * t.SizeF * t.HJit, 5, 13);
        int baseR = Clamp(2.0 * t.SizeF * t.CJit, 1, 3);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        int crownStart = t.Sy + System.Math.Max(2, height / 3);
        int tip = topY + 1;
        int layers = topY - crownStart + 1;
        int gap = layers >= 6 && t.Bits(0, 2) == 0 ? crownStart + 1 + t.Bits(2, 4) % (layers - 4) : int.MinValue;
        int phase = t.Bits(6, 2) % 3;
        for (int y = crownStart; y <= topY; y++)
        {
            if (y == gap)
            {
                continue; // the odd missing layer: the trunk shows through
            }

            double f = (double)(tip - y) / (tip - crownStart);
            int r = System.Math.Clamp((int)System.Math.Round(baseR * f), 0, baseR);
            if (r > 0 && (y - crownStart + phase) % 3 == 1)
            {
                r--; // the tiers
            }

            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    int d2 = dx * dx + dz * dz;
                    if (d2 > r * r + 1 || (r >= 2 && d2 >= r * r && TreeCellHash(t.Shape, dx, y - t.Sy, dz) % 4 == 0))
                    {
                        continue; // outside, or a ragged rim cell
                    }

                    set(t.Wx + dx, y, t.Wz + dz, leaf, false);
                }
        }

        set(t.Wx, tip, t.Wz, leaf, false);
        if (height >= 7 && t.Bits(8, 3) == 0)
        {
            var d = TreeDirs4[t.Bits(11, 2)];
            set(t.Wx + d.X, topY, t.Wz + d.Z, leaf, false); // a twin top beside the leader
            set(t.Wx + d.X, tip, t.Wz + d.Z, leaf, false);
        }
    }

    /// <summary>The generation-29 jungle tree: the classic trunk and canopy on two to four buttress roots, with a second,
    /// lower canopy layer on the tall ones.</summary>
    private static void BuildJungleGen29(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(8.0 * t.SizeF * t.HJit, 7, 14);
        int crownR = Clamp(3.0 * t.SizeF * t.CJit, 2, 4);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);

        // Buttress roots: two to four faces, two cells high, a third cell out at the foot on some.
        int roots = t.Bits(0, 4) | 0b0101; // at least two opposite faces
        for (int i = 0; i < 4; i++)
        {
            if (((roots >> i) & 1) == 0)
            {
                continue;
            }

            var d = TreeDirs4[i];
            set(t.Wx + d.X, t.Sy + 1, t.Wz + d.Z, log, false);
            set(t.Wx + d.X, t.Sy + 2, t.Wz + d.Z, log, false);
            if (t.Bits(4 + i, 1) == 0)
            {
                set(t.Wx + d.X * 2, t.Sy + 1, t.Wz + d.Z * 2, log, false);
            }
        }

        if (height >= 9)
        {
            // The second canopy layer: a ragged disc of radius 2 on two short branches, well under the crown.
            int y2 = t.Sy + height * 3 / 5;
            var b1 = TreeDirs4[t.Bits(8, 2)];
            set(t.Wx + b1.X, y2, t.Wz + b1.Z, log, false);
            set(t.Wx - b1.X, y2, t.Wz - b1.Z, log, false);
            for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int d2 = dx * dx + dz * dz;
                    if (d2 <= 5 && !(d2 >= 4 && TreeCellHash(t.Shape, dx, y2 - t.Sy, dz) % 3 == 0))
                    {
                        set(t.Wx + dx, y2, t.Wz + dz, leaf, false);
                    }
                }
        }

        for (int dy = -2; dy <= crownR; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= crownR * crownR + 2)
                    {
                        set(t.Wx + dx, topY + dy, t.Wz + dz, leaf, false);
                    }
                }
    }

    /// <summary>The generation-29 willow: the classic crown, sometimes domed by a third layer, and strands of 2–4 cells that
    /// differ from strand to strand (a few missing).</summary>
    private static void BuildWillowGen29(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(5.0 * t.SizeF * t.HJit, 4, 6);
        int crownR = Clamp(2.5 * t.SizeF * t.CJit, 2, 3);
        int topY = t.Sy + height;
        TreeTrunk(t.Wx, t.Sy, t.Wz, topY, log, set);
        int layers = t.Bits(0, 1) == 0 ? 2 : 1;
        for (int dy = 0; dy <= layers; dy++)
        {
            int r = dy == 2 ? crownR - 1 : crownR;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    int d2 = dx * dx + dz * dz;
                    if (d2 > r * r + 1)
                    {
                        continue;
                    }

                    set(t.Wx + dx, topY + dy, t.Wz + dz, leaf, false);
                    if (dy == 0 && d2 >= (crownR - 1) * (crownR - 1) + 1 && ((dx + dz) & 1) == 0)
                    {
                        int h = TreeCellHash(t.Shape, dx, 0, dz);
                        if (h % 5 == 0)
                        {
                            continue; // a missing strand
                        }

                        int len = System.Math.Min(2 + h % 3, height - 1);
                        for (int s = 1; s <= len; s++)
                        {
                            set(t.Wx + dx, topY - s, t.Wz + dz, leaf, false);
                        }
                    }
                }
        }
    }

    /// <summary>The generation-29 baobab: the classic 2×2 trunk and flat crown, the trunk bulging at mid height on some,
    /// stubby arms under the crown, and on the large ones a third, smaller crown layer.</summary>
    private static void BuildBaobabGen29(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(6.0 * t.SizeF * t.HJit, 5, 8);
        int crownR = Clamp(3.0 * t.SizeF * t.CJit, 2, 3);
        int topY = t.Sy + height;
        for (int ty = t.Sy + 1; ty <= topY; ty++)
        {
            set(t.Wx, ty, t.Wz, log, true);
            set(t.Wx + 1, ty, t.Wz, log, false);
            set(t.Wx, ty, t.Wz + 1, log, false);
            set(t.Wx + 1, ty, t.Wz + 1, log, false);
        }

        if (t.Bits(0, 1) == 0)
        {
            int by = t.Sy + height / 2; // the bulge: a ring around the 2×2 at mid height
            for (int k = 0; k <= 1; k++)
            {
                set(t.Wx - 1, by, t.Wz + k, log, false);
                set(t.Wx + 2, by, t.Wz + k, log, false);
                set(t.Wx + k, by, t.Wz - 1, log, false);
                set(t.Wx + k, by, t.Wz + 2, log, false);
            }
        }

        int arms = 2 + t.Bits(1, 2) % 3;
        int start = t.Bits(3, 2);
        for (int i = 0; i < arms; i++)
        {
            var d = TreeDirs4[(start + i) & 3];
            int ax = d.X > 0 ? 2 : d.X < 0 ? -1 : t.Bits(5 + i, 1);
            int az = d.Z > 0 ? 2 : d.Z < 0 ? -1 : t.Bits(5 + i, 1);
            set(t.Wx + ax, topY, t.Wz + az, log, false); // an arm out of the trunk's top
        }

        int layers = crownR >= 3 && t.Bits(9, 1) == 0 ? 2 : 1;
        for (int dy = 0; dy <= layers; dy++)
        {
            int r = crownR - dy;
            for (int dx = -r; dx <= r + 1; dx++)
                for (int dz = -r; dz <= r + 1; dz++)
                {
                    if ((dx - 0.5) * (dx - 0.5) + (dz - 0.5) * (dz - 0.5) <= r * r + 1.5)
                    {
                        set(t.Wx + dx, topY + 1 + dy, t.Wz + dz, leaf, false);
                    }
                }
        }
    }

    /// <summary>The generation-29 palm: most trunks curve 1–2 cells aside towards their crown, and the burst has six or
    /// eight fronds of a length that keeps curve + frond inside the 4-cell margin.</summary>
    private static void BuildPalmGen29(in TreeSite t, BlockId log, BlockId leaf, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = Clamp(6.0 * t.SizeF * t.HJit, 5, 11);
        int lean = t.Bits(0, 2) == 0 ? 0 : 1 + t.Bits(2, 1);
        int fr = System.Math.Min(Clamp(2.0 * t.CJit * t.SizeF + 0.4, 2, 3), 4 - lean);
        var top = LeaningTrunk(t.Wx, t.Sy, t.Wz, height, lean, TreeDirs4[t.Bits(3, 2)], log, set);
        int cx = t.Wx + top.X, cz = t.Wz + top.Z;
        int topY = t.Sy + height;
        set(cx, topY + 1, cz, leaf, false); // crown core
        bool six = t.Bits(5, 1) == 0;
        int skip = t.Bits(6, 2) * 2 + 1; // a six-frond burst leaves out one diagonal pair
        for (int i = 0; i < 8; i++)
        {
            if (six && (i == skip || i == ((skip + 4) & 7)))
            {
                continue;
            }

            var d = TreeDirs8[i];
            for (int s = 1; s <= fr; s++)
            {
                int y = topY - (s == fr ? 1 : 0); // the frond tips droop one cell, as on the classic palm
                set(cx + d.X * s, y, cz + d.Z * s, leaf, false);
            }
        }
    }

    // ---------------- test seams ----------------

    /// <summary>Builds one generation-29 tree into a cell list relative to its column (tests): (dx, dy, dz, block), every
    /// slot block a distinct id — log 1, leaf A 2, needles 3, fronds 4, dead log 5, stem 6, cap 7, crystal 8, leaf B 9,
    /// leaf C 10, blossom 11, pale log 12, saguaro 13.</summary>
    internal static List<(int Dx, int Dy, int Dz, BlockId Block)> BuildTreeGen29ForTest(TreeKind kind, double sizeF, double hJit,
        double cJit, int shape)
    {
        var cells = new List<(int, int, int, BlockId)>();
        void Set(int x, int y, int z, BlockId id, bool _) => cells.Add((x, y, z, id));
        BuildGen29(kind, new TreeSite(0, 0, 0, sizeF, hJit, cJit, shape & ((1 << 20) - 1)), TestTreeBlocks, Set);
        return cells;
    }

    private static readonly TreeBlocks TestTreeBlocks = new(new BlockId(1), new BlockId(2), new BlockId(3), new BlockId(4),
        new BlockId(5), new BlockId(6), new BlockId(7), new BlockId(8), new BlockId(9), new BlockId(10), new BlockId(11),
        new BlockId(12), new BlockId(13));

    /// <summary>The block key an id of <see cref="BuildTreeGen29ForTest"/> stands for (tests; the dead log reads as the
    /// common log, which it is unless the type names its own).</summary>
    internal static string? TestTreeBlockKey(BlockId id) => id.Value switch
    {
        1 or 5 => TreeWood.LogKey,
        2 => TreeFoliage.LeavesKey,
        3 => TreeFoliage.NeedlesKey,
        4 => TreeFoliage.FrondKey,
        6 => "mushroom_stem",
        7 => "mushroom_cap",
        8 => "crystal",
        9 => TreeFoliage.LeavesBKey,
        10 => TreeFoliage.LeavesCKey,
        11 => TreeFoliage.BlossomKey,
        12 => TreeWood.PaleLogKey,
        13 => SaguaroKey,
        _ => null,
    };

    /// <summary>The generation-29 pick at a column for a theme on this (generation-29) world (tests): the kind, the grove's
    /// lead kind, and whether the tree is one of the mixed share. Uses the world's own glow level.</summary>
    internal (TreeKind Kind, TreeKind Lead, bool Mixed) PickTreeKindGen29ForTest(PlanetType planet, FloraThemes.Theme theme, int wx, int wz)
    {
        var palette = EffectiveTreePalette(theme, _terrainGeneration, TreeGlowLevel(planet));
        var kind = PickTreeKindGen29(palette, TotalWeight(palette), PlanetSeed(planet), wx, wz, planet.TerrainScale, out var lead, out bool mixed);
        return (kind, lead, mixed);
    }

    /// <summary>The grove value of a column (tests: the distribution the flattening table was measured from).</summary>
    internal double GroveValueForTest(PlanetType planet, int wx, int wz)
        => FbmT(PlanetSeed(planet) + 0x70EE17, wx, wz, planet.TerrainScale * 3.0, octaves: 2);

    /// <summary>Called with every tree the ground pass builds (kind, root column) — a test hook, null in the game.</summary>
    internal System.Action<TreeKind, int, int>? TreeStampedForTest { get; set; }

    /// <summary>The surface block of the biome a column belongs to, as the tree pass reads it (tests: a multi-biome type's
    /// biome field is large-scale, so a test looks for the columns of the biome it wants).</summary>
    internal BlockId TreeBiomeSurfaceForTest(PlanetType planet, int wx, int wz)
    {
        var biomes = ResolveBiomes(planet);
        int sy = SurfaceHeight(planet, wx, wz);
        return biomes[biomes.Count <= 1 ? 0 : BiomeIndex(CalibFor(planet), PlanetSeed(planet), wx, wz, biomes.Count, sy)].Surface;
    }
}
