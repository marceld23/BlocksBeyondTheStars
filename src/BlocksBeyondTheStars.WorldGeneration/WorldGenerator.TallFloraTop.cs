// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3859: how high the flora of a chunk column can reach — what the server's chunk streaming needs so a tall tree is not
/// cut off flat at the top. A far column streams only a thin band around its terrain surface (one chunk above it), which
/// leaves 16–31 blocks over the ground: an ordinary tree (up to <see cref="MaxStampRise"/>) over a surface high in its
/// chunk, and every giant tree (up to <see cref="GiantTreeRise"/>), stood out of it and ended in a flat lid, because the
/// client draws a chunk it was never sent as air.
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The per-world flora richness multiplier (2026-06-10, "belebte Planeten"): 0.8..1.6 on top of the planet
    /// type's flora and tree density, a pure function of the planet seed.</summary>
    private double FloraMulFor(long seed) => (0.8 + 0.8 * Noise.Value01(seed + 0xF10A, 11, 23, 37)) * _floraFactor;

    /// <summary>The tree density a planet grows (a small automatic one on a flora world without its own value).</summary>
    private static double TreeDensityFor(PlanetType planet, bool flora, double floraMul)
        => (planet.TreeDensity ?? (flora ? 0.012 : 0.0)) * floraMul;

    /// <summary>
    /// The highest cell the tall flora of a <paramref name="size"/>-wide square of columns can reach, or
    /// <see cref="int.MinValue"/> when nothing tall can grow there.
    /// <list type="bullet">
    /// <item>Giant trees are replayed exactly: the same site test the chunk stamp runs, then the tree's own builder, keeping
    /// the highest cell that lands inside the square (a crown reaching in from a neighbouring column counts).</item>
    /// <item>Ordinary trees, giant mushrooms and giant flora are bounded by their common rise: the highest of five surface
    /// samples (the centre and the corners, one in) plus <see cref="MaxStampRise"/> — conservative, so a tree on a hill
    /// above the column's centre keeps its crown too.</item>
    /// </list>
    /// Pure and deterministic; the server caches the answer per column.
    /// </summary>
    public int TallFloraTopInSquare(PlanetType planet, int x0, int z0, int size)
    {
        long seed = PlanetSeed(planet);
        bool flora = planet.FloraDensity > 0;
        var biomes = ResolveBiomes(planet);
        var wonder = WonderFor(planet);
        var logId = _content.GetBlock("wood_log")?.NumericId ?? BlockId.Air;
        bool trees = TreeDensityFor(planet, flora, FloraMulFor(seed)) > 0.0 && !logId.IsAir;
        var myceliumId = _content.GetBlock("mycelium")?.NumericId ?? BlockId.Air;
        bool mushrooms = !myceliumId.IsAir && biomes.Exists(b => b.Surface == myceliumId);
        bool giantFlora = flora && wonder.Generation >= 1;
        if (!trees && !mushrooms && !giantFlora)
        {
            return int.MinValue; // a barren world grows nothing tall
        }

        int fluidLevel = ResolveSeaFluid(planet).Level;
        int top = int.MinValue;
        int groundTop = HighestSurfaceInSquare(planet, x0, z0, size);
        if (groundTop + 1 > fluidLevel)
        {
            top = groundTop + MaxStampRise;
        }

        if (trees && wonder.Generation >= WorldDescription.NewKindsGeneration)
        {
            int giant = GiantTreeTopInSquare(planet, seed, biomes, fluidLevel, x0, z0, size);
            if (giant > top)
            {
                top = giant;
            }
        }

        return top;
    }

    /// <summary>The highest of five surface samples over the square: the centre and the four corners, one cell in (the
    /// same probe pattern as <see cref="HighestBandTopInSquare"/>).</summary>
    private int HighestSurfaceInSquare(PlanetType planet, int x0, int z0, int size)
    {
        int half = size / 2;
        int far = size - 2;
        int top = SurfaceHeight(planet, x0 + half, z0 + half);
        top = System.Math.Max(top, SurfaceHeight(planet, x0 + 1, z0 + 1));
        top = System.Math.Max(top, SurfaceHeight(planet, x0 + far, z0 + 1));
        top = System.Math.Max(top, SurfaceHeight(planet, x0 + 1, z0 + far));
        return System.Math.Max(top, SurfaceHeight(planet, x0 + far, z0 + far));
    }

    /// <summary>The highest giant-tree cell inside the square, or <see cref="int.MinValue"/>: every column within
    /// <see cref="GiantTreeMargin"/> of the square runs the stamp's own site test, and a tree that stands there is built
    /// into a probe that only records heights.</summary>
    private int GiantTreeTopInSquare(PlanetType planet, long seed, System.Collections.Generic.List<BiomeResolved> biomes,
        int fluidLevel, int x0, int z0, int size)
    {
        if (_content.GetBlock(GiantLogKey) is null || _content.GetBlock(GiantLeavesKey) is null)
        {
            return int.MinValue; // this content has no giant blocks
        }

        GiantTreeSiteInputs? sites = null;
        int top = int.MinValue;
        for (int wx = x0 - GiantTreeMargin; wx < x0 + size + GiantTreeMargin; wx++)
            for (int wz = z0 - GiantTreeMargin; wz < z0 + size + GiantTreeMargin; wz++)
            {
                if (!GiantTreeRollPasses(planet, seed, wx, wz))
                {
                    continue;
                }

                sites ??= GiantTreeSiteInputsFor(planet, seed, biomes, fluidLevel);
                if (!TryGiantTreeSite(sites, wx, wz, SurfaceHeight(planet, wx, wz), out var site))
                {
                    continue;
                }

                BuildGiantTree(site.Shape, wx, site.BaseY, wz, site.SizeF, site.ShapeHash, BlockId.Air, BlockId.Air,
                    (x, y, z, _, _) =>
                    {
                        if (y > top && x >= x0 && x < x0 + size && z >= z0 && z < z0 + size)
                        {
                            top = y;
                        }
                    });
            }

        return top;
    }
}
