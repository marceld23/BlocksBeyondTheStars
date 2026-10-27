// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The trees a sapling grows into (#1774, #2977–#2980) — pure functions of the shape, a per-cell variant and the tree hash,
/// shared by the server (<c>GameServerSaplings</c>) and the tests. Every cell is relative to the sapling's cell. The server
/// writes the trunk (which must be free — the sapling's own cell excepted), then the crown and the blossoms into air only,
/// then the fruit.
/// </summary>
public static class SaplingTrees
{
    /// <summary>One grown tree's cells.</summary>
    public sealed class Shape
    {
        /// <summary>The trunk, bottom up. Every cell but the sapling's own must be free for the tree to grow; cells at
        /// height 0 beside the sapling (the young giant's 2×2 trunk) need ground of their own.</summary>
        public readonly List<(int X, int Y, int Z)> Trunk = new();

        /// <summary>The crown in builder order (air only) — the cells the fruit hangs under.</summary>
        public readonly List<(int X, int Y, int Z)> Crown = new();

        /// <summary>The blossoms (air only) — only the Fifi plant has any.</summary>
        public readonly List<(int X, int Y, int Z)> Blossoms = new();

        /// <summary>The authored berry cells (air only, under a leaf) — only the Fifi plant has any; the fruit trees pick
        /// theirs from the crown with <see cref="FruitRules.PickFruitCells"/>.</summary>
        public readonly List<(int X, int Y, int Z)> Berries = new();
    }

    /// <summary>The size factor of a sapling-grown Fifi plant: the wild plant's smaller shape (crown radius 2).</summary>
    public const double FifiSizeFactor = 0.8;

    /// <summary>The tree of <paramref name="shape"/>. <paramref name="variant"/> is the server's fixed per-cell number
    /// (heights vary with it), <paramref name="treeHash"/> the tree hash (the Fifi plant's blossoms and berries).</summary>
    public static Shape For(SaplingShape shape, int variant, ulong treeHash)
    {
        variant = System.Math.Abs(variant);
        return shape switch
        {
            SaplingShape.Conifer => Conifer(5 + variant % 2),
            SaplingShape.Palm => Palm(5 + variant % 2),
            SaplingShape.Fifi => Fifi(treeHash),
            SaplingShape.YoungGiant => YoungGiant(9 + variant % 3),
            _ => Broadleaf(4 + variant % 2),
        };
    }

    /// <summary>The classic sapling tree (#1774): a trunk of 4–5 logs, two full rings around its top two cells (corners
    /// clipped), a smaller ring above and a cap.</summary>
    private static Shape Broadleaf(int height)
    {
        var s = new Shape();
        AddTrunk(s, height);
        int top = height - 1;
        for (int dy = -1; dy <= 2; dy++)
        {
            int r = dy <= 0 ? 2 : dy == 1 ? 1 : 0;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (r == 2 && System.Math.Abs(dx) == 2 && System.Math.Abs(dz) == 2)
                    {
                        continue; // clipped corners keep the crown round
                    }

                    s.Crown.Add((dx, top + dy, dz));
                }
        }

        return s;
    }

    /// <summary>A small fir (#2978): a trunk of 5–6 logs, needle rings from its third cell up — a wide diamond low down, a
    /// 3×3 ring near the top — then a cap and a tip above the trunk.</summary>
    private static Shape Conifer(int height)
    {
        var s = new Shape();
        AddTrunk(s, height);
        int top = height - 1;
        for (int y = 2; y <= top; y++)
        {
            int r = top - y >= 2 ? 2 : 1;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    bool inRing = r == 1 || System.Math.Abs(dx) + System.Math.Abs(dz) <= 2;
                    if (inRing && (dx != 0 || dz != 0))
                    {
                        s.Crown.Add((dx, y, dz));
                    }
                }
        }

        s.Crown.Add((0, top + 1, 0));
        s.Crown.Add((0, top + 2, 0));
        return s;
    }

    /// <summary>A small palm (#2977, the banana sapling): a bare trunk of 5–6 logs, a cap on top and four fronds that
    /// reach out three cells and droop at the end, with short diagonal fronds between them.</summary>
    private static Shape Palm(int height)
    {
        var s = new Shape();
        AddTrunk(s, height);
        int top = height - 1;
        s.Crown.Add((0, top + 1, 0));
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            s.Crown.Add((dx, top + 1, dz));
            s.Crown.Add((2 * dx, top + 1, 2 * dz));
            s.Crown.Add((3 * dx, top, 3 * dz));
        }

        foreach (var (dx, dz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            s.Crown.Add((dx, top + 1, dz));
            s.Crown.Add((2 * dx, top, 2 * dz));
        }

        return s;
    }

    /// <summary>A Fifi plant (#2979): the wild plant's own smaller shape (<see cref="FifiPlantRules.ShapeFor"/>), so a
    /// planted one looks like the ones that grow wild.</summary>
    private static Shape Fifi(ulong treeHash)
    {
        var wild = FifiPlantRules.ShapeFor(treeHash, FifiSizeFactor);
        var s = new Shape();
        s.Trunk.AddRange(wild.Trunk);
        s.Crown.AddRange(wild.Leaves);
        s.Blossoms.AddRange(wild.Blossoms);
        s.Berries.AddRange(wild.Berries);
        return s;
    }

    /// <summary>A young giant mahogany (#2980): a 2×2 trunk of giant logs 9–11 cells high — the sapling is its north-west
    /// corner — under a wide flattened crown (an ellipsoid, 4.2 cells across from the trunk's centre, 2.6 high).</summary>
    private static Shape YoungGiant(int height)
    {
        var s = new Shape();
        var trunk = new HashSet<(int X, int Y, int Z)>();
        for (int y = 0; y < height; y++)
            for (int dx = 0; dx <= 1; dx++)
                for (int dz = 0; dz <= 1; dz++)
                {
                    s.Trunk.Add((dx, y, dz));
                    trunk.Add((dx, y, dz));
                }

        const double rh = 4.2, rv = 2.6;
        for (int dy = -3; dy <= 2; dy++)
            for (int dx = -4; dx <= 5; dx++)
                for (int dz = -4; dz <= 5; dz++)
                {
                    double cx = dx - 0.5, cz = dz - 0.5;
                    double e = (cx * cx + cz * cz) / (rh * rh) + (double)(dy * dy) / (rv * rv);
                    var cell = (dx, height + dy, dz);
                    if (e <= 1.0 && !trunk.Contains(cell))
                    {
                        s.Crown.Add(cell);
                    }
                }

        return s;
    }

    /// <summary>The quarter-turn mirrors a tree may grow in (#2980): the young giant's 2×2 trunk can stand in any of the four
    /// quarters around its sapling, and the server takes the first one with room.</summary>
    public static readonly (int Sx, int Sz)[] Mirrors = { (1, 1), (-1, 1), (1, -1), (-1, -1) };

    /// <summary>The shape mirrored on the X and/or Z axis (<paramref name="sx"/>, <paramref name="sz"/> = ±1).</summary>
    public static Shape Mirrored(Shape s, int sx, int sz)
    {
        if (sx == 1 && sz == 1)
        {
            return s;
        }

        var m = new Shape();
        Mirror(s.Trunk, m.Trunk, sx, sz);
        Mirror(s.Crown, m.Crown, sx, sz);
        Mirror(s.Blossoms, m.Blossoms, sx, sz);
        Mirror(s.Berries, m.Berries, sx, sz);
        return m;
    }

    private static void Mirror(List<(int X, int Y, int Z)> from, List<(int X, int Y, int Z)> to, int sx, int sz)
    {
        foreach (var (x, y, z) in from)
        {
            to.Add((x * sx, y, z * sz));
        }
    }

    private static void AddTrunk(Shape s, int height)
    {
        for (int y = 0; y < height; y++)
        {
            s.Trunk.Add((0, y, 0));
        }
    }
}
