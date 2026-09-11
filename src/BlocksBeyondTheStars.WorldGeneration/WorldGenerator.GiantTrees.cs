// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #1783 (generation 6): the giant trees — rare landmarks three to five times the size of an ordinary tree,
/// trunks three to five cells thick, rooted into the slope, with real branches and a hollow crown, on their
/// own block pair (<see cref="GiantLogKey"/> + <see cref="GiantLeavesKey"/>) so the scanner names them as
/// their own species. The shape follows the biome theme's own trees: a giant broadleaf where the woods are
/// broadleaf, a giant conifer where they are conifer, a giant jungle tree in the tropics.
///
/// <para>A separate pass, deliberately NOT a <see cref="TreeKind"/> inside <see cref="StampTrees"/>: the ordinary
/// tree scan is bounded by a 4-cell margin and an 18-cell rise (<see cref="MaxStampRise"/>) that every stacked
/// chunk relies on for its skip test; a 15-radius crown would make that scan four times larger for every
/// chunk. This pass has its own margin and rise, and its per-column roll rejects all but ~0.07 % of columns
/// before anything expensive runs (the #1527 first-reject). Deterministic: every input is a pure function of
/// the column (wrapped-X noise rolls, the memoised forest mask, surface heights), so a tree straddling a chunk
/// edge generates identically from every chunk it touches.</para>
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The giant trunk block key.</summary>
    public const string GiantLogKey = "giant_log";

    /// <summary>The giant crown block key.</summary>
    public const string GiantLeavesKey = "giant_leaves";

    /// <summary>The shape a giant tree takes, from the biome theme's own tree palette.</summary>
    internal enum GiantTreeShape
    {
        None,
        Broadleaf, // a domed hollow crown on radial branches (temperate / swamp / savanna woods)
        Conifer,   // a tall hollow cone in tiers (tundra / alpine)
        Jungle,    // a very tall trunk with buttress roots under a wide flat canopy (tropical)
    }

    private const int GiantTreeMargin = 16;              // the widest crown + branch reach — the chunk-edge scan margin
    private const int GiantTreeRise = 64;                // the highest cell a giant tree writes above its column's surface
    private const double GiantTreeDensity = 0.0007;      // per forest column → about one tree per 38×38 inside a wood
    private const int GiantTreeFootprintRelief = 4;      // the trunk footprint may not step more than this
    private const double GiantTreeForestMask = 0.62;     // the same threshold StampTrees calls "inside a forest"

    /// <summary>The giant shape a theme's palette implies: conifer woods grow a giant conifer, jungle woods a giant
    /// jungle tree, broadleaf woods a giant broadleaf; palettes without any of the three (desert, ashen, fungal,
    /// crystal, floral) grow none.</summary>
    internal static GiantTreeShape GiantShapeFor(TreeKind[] palette)
    {
        bool conifer = false, jungle = false, broadleaf = false;
        foreach (var k in palette)
        {
            if (k == TreeKind.Conifer) conifer = true;
            else if (k == TreeKind.Jungle) jungle = true;
            else if (k == TreeKind.Broadleaf) broadleaf = true;
        }

        return conifer ? GiantTreeShape.Conifer : jungle ? GiantTreeShape.Jungle : broadleaf ? GiantTreeShape.Broadleaf : GiantTreeShape.None;
    }

    private void StampGiantTrees(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, int fluidLevel)
    {
        var logId = _content.GetBlock(GiantLogKey)?.NumericId ?? BlockId.Air;
        var leafId = _content.GetBlock(GiantLeavesKey)?.NumericId ?? BlockId.Air;
        if (logId.IsAir || leafId.IsAir)
        {
            return; // this content has no giant blocks
        }

        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return; // outside this chunk (a neighbour chunk stamps that part of the tree)
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return; // leaves and branches only fill air, never carve the trunk or terrain
            }

            chunk.Set(lx, ly, lz, block);
        }

        var calib = CalibFor(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        var riverField = RiverFieldFor(planet);
        var wonder = WonderFor(planet);
        var grassId = _content.GetBlock("grass")?.NumericId ?? BlockId.Air;
        var dirtId = _content.GetBlock("dirt")?.NumericId ?? BlockId.Air;
        var mudId = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;

        for (int wx = origin.X - GiantTreeMargin; wx < origin.X + cs + GiantTreeMargin; wx++)
            for (int wz = origin.Z - GiantTreeMargin; wz < origin.Z + cs + GiantTreeMargin; wz++)
            {
                // The cheap first-reject: one noise read decides for 99.9 % of the margin columns.
                int cx = WorldConstants.WrapX(wx, _circumference);
                if (Noise.Value01(seed + 0x61A47, cx, 29, Wz(wz)) >= GiantTreeDensity)
                {
                    continue;
                }

                if (ForestMaskAt(planet, seed, wx, wz) <= GiantTreeForestMask)
                {
                    continue; // landmarks of the deep woods, never of the open land
                }

                int sy = SurfaceHeight(planet, wx, wz);
                if (sy + GiantTreeRise < origin.Y || sy - GiantTreeFootprintRelief + 1 > origin.Y + cs - 1)
                {
                    continue; // every cell this tree could write lies outside this chunk
                }

                var biome = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, sy)];
                if (biome.TreeMul * biome.Theme.TreeMul <= 0.0)
                {
                    continue; // a treeless biome grows no giant either
                }

                var shape = GiantShapeFor(biome.Theme.PaletteFor(wonder.Generation));
                if (shape == GiantTreeShape.None)
                {
                    continue;
                }

                if (TempAt(calib, sy) < TreeLineC || sy + 1 <= fluidLevel)
                {
                    continue; // above the tree line, or in the sea
                }

                if (SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
                    || SurfaceGen1WaterDepth(planet, wx, wz) > 0
                    || DryBeachAt(planet, calib, seed, riverField, waterId, wx, wz, sy))
                {
                    continue; // a pond, a river or a beach — no giant stands in the water or in the sand
                }

                var surf = biome.Surface;
                if (surf != grassId && surf != dirtId && surf != mudId)
                {
                    continue; // earthy ground only
                }

                // Size 3..5 (bell-shaped like the ordinary trees' SizeFactor), the trunk thickness rounds from it,
                // and the whole footprint must be reasonably level — the trunk roots from its LOWEST surface, so a
                // steep site would leave a wall of logs on the uphill side.
                double sizeF = 3.0 + (Noise.Value01(seed + 0x61A48, cx, 31, Wz(wz)) + Noise.Value01(seed + 0x61A4A, cx, 43, Wz(wz))) * 1.0;
                int trunk = System.Math.Clamp((int)System.Math.Round(sizeF), 3, 5);
                int half = trunk / 2;
                int minSy = sy, maxSy = sy;
                for (int dx = 0; dx < trunk; dx++)
                    for (int dz = 0; dz < trunk; dz++)
                    {
                        int h = SurfaceHeight(planet, wx - half + dx, wz - half + dz);
                        if (h < minSy) minSy = h;
                        if (h > maxSy) maxSy = h;
                    }

                if (maxSy - minSy > GiantTreeFootprintRelief)
                {
                    continue;
                }

                int shapeHash = (int)(Noise.Value01(seed + 0x61A49, cx, 37, Wz(wz)) * 997);
                BuildGiantTree(shape, wx, minSy, wz, sizeF, shapeHash, logId, leafId, SetCell);
            }
    }

    /// <summary>Stamps one giant tree with its root cell row at <paramref name="baseY"/> + 1 (the lowest surface of
    /// the footprint): a <c>trunk×trunk</c> column of logs (overwriting — a trunk is solid), radial branches and a
    /// hollow crown that only fill air. All three shapes stay inside <see cref="GiantTreeMargin"/> and
    /// <see cref="GiantTreeRise"/> (a test holds them there).</summary>
    private static void BuildGiantTree(GiantTreeShape shape, int wx, int baseY, int wz, double sizeF, int shapeHash,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        switch (shape)
        {
            case GiantTreeShape.Conifer: BuildGiantConifer(wx, baseY, wz, sizeF, shapeHash, logId, leafId, set); break;
            case GiantTreeShape.Jungle: BuildGiantJungle(wx, baseY, wz, sizeF, shapeHash, logId, leafId, set); break;
            default: BuildGiantBroadleaf(wx, baseY, wz, sizeF, shapeHash, logId, leafId, set); break;
        }
    }

    /// <summary>The thick trunk: a trunk×trunk square of logs from the root row to <paramref name="topY"/>, centred
    /// on the column (an even thickness sits one cell off-centre, like the baobab's 2×2).</summary>
    private static void GiantTrunk(int wx, int baseY, int wz, int trunk, int topY, BlockId logId,
        System.Action<int, int, int, BlockId, bool> set)
    {
        int half = trunk / 2;
        for (int ty = baseY + 1; ty <= topY; ty++)
            for (int dx = 0; dx < trunk; dx++)
                for (int dz = 0; dz < trunk; dz++)
                {
                    set(wx - half + dx, ty, wz - half + dz, logId, true);
                }
    }

    /// <summary>A radial branch: logs stepping outward (and gently upward) from the trunk face, ending in a ball
    /// of leaves. Branches only fill air, so one never cuts the trunk or the hillside.</summary>
    private static void GiantBranch(int wx, int y, int wz, double angle, int length, int rise, int ballR,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        double cos = System.Math.Cos(angle), sin = System.Math.Sin(angle);
        int tipX = wx, tipY = y, tipZ = wz;
        for (int k = 1; k <= length; k++)
        {
            tipX = wx + (int)System.Math.Round(cos * k);
            tipZ = wz + (int)System.Math.Round(sin * k);
            tipY = y + (k * rise) / System.Math.Max(1, length);
            set(tipX, tipY, tipZ, logId, false);
        }

        for (int dy = -ballR; dy <= ballR; dy++)
            for (int dx = -ballR; dx <= ballR; dx++)
                for (int dz = -ballR; dz <= ballR; dz++)
                {
                    if (dx * dx + dy * dy + dz * dz <= ballR * ballR + 1)
                    {
                        set(tipX + dx, tipY + dy, tipZ + dz, leafId, false);
                    }
                }
    }

    /// <summary>A hollow ellipsoid of leaves: only the outer shell is filled, so the crown reads as a canopy from
    /// below and from inside, the grove floor is not black, and the mesher does not carry ten thousand hidden cubes.</summary>
    private static void GiantCrownShell(int cx, int cy, int cz, int rx, int ry, double innerNorm, BlockId leafId,
        System.Action<int, int, int, BlockId, bool> set)
    {
        double rx2 = (double)rx * rx, ry2 = (double)ry * ry;
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
                for (int dz = -rx; dz <= rx; dz++)
                {
                    double n = (dx * dx + dz * dz) / rx2 + dy * dy / ry2;
                    if (n <= 1.0 && n >= innerNorm)
                    {
                        set(cx + dx, cy + dy, cz + dz, leafId, false);
                    }
                }
    }

    /// <summary>The giant broadleaf: trunk 21–35 high, four to six radial branches at half height and above, each
    /// ending in a leaf ball, and a domed hollow crown radius 8–13 on top.</summary>
    private static void BuildGiantBroadleaf(int wx, int baseY, int wz, double sizeF, int shapeHash,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int trunk = System.Math.Clamp((int)System.Math.Round(sizeF), 3, 5);
        int height = System.Math.Clamp((int)System.Math.Round(7.0 * sizeF), 21, 35);
        int crownR = System.Math.Clamp((int)System.Math.Round(2.6 * sizeF), 8, 13);
        int crownRy = System.Math.Max(4, (int)System.Math.Round(crownR * 0.7));
        int topY = baseY + height;
        GiantTrunk(wx, baseY, wz, trunk, topY, logId, set);

        int branches = 4 + shapeHash % 3;
        double spin = (shapeHash >> 3 & 7) / 8.0 * (System.Math.PI * 2.0 / branches);
        int length = System.Math.Min(crownR - 2, 11);
        int ballR = System.Math.Clamp((int)System.Math.Round(sizeF * 0.8), 2, 4);
        for (int i = 0; i < branches; i++)
        {
            double angle = spin + i * System.Math.PI * 2.0 / branches;
            int by = baseY + (int)System.Math.Round(height * (0.55 + 0.07 * (i % 4)));
            GiantBranch(wx, by, wz, angle, length, length / 3, ballR, logId, leafId, set);
        }

        GiantCrownShell(wx, topY - 1, wz, crownR, crownRy, 0.55, leafId, set);
    }

    /// <summary>The giant conifer: trunk 27–45 high under a hollow cone of tiers, radius 7–11 at the base of the
    /// crown tapering to a spike, with a few short branches poking out of the lower tiers.</summary>
    private static void BuildGiantConifer(int wx, int baseY, int wz, double sizeF, int shapeHash,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int trunk = System.Math.Clamp((int)System.Math.Round(sizeF), 3, 5);
        int height = System.Math.Clamp((int)System.Math.Round(9.0 * sizeF), 27, 45);
        int crownR = System.Math.Clamp((int)System.Math.Round(2.2 * sizeF), 7, 11);
        int topY = baseY + height;
        GiantTrunk(wx, baseY, wz, trunk, topY, logId, set);

        int crownBase = baseY + (int)System.Math.Round(height * 0.25);
        int crownH = System.Math.Max(1, topY - crownBase);
        for (int y = crownBase; y <= topY; y++)
        {
            double r = 1.0 + (crownR - 1.0) * (topY - y) / crownH;
            int tier = (y - crownBase) % 3; // every third layer is a full tier, the two between are the hollow shell
            double inner = tier == 0 ? 0.0 : r - 1.6;
            int ir = (int)System.Math.Ceiling(r);
            for (int dx = -ir; dx <= ir; dx++)
                for (int dz = -ir; dz <= ir; dz++)
                {
                    double d = System.Math.Sqrt(dx * dx + dz * dz);
                    if (d <= r + 0.4 && d >= inner)
                    {
                        set(wx + dx, y, wz + dz, leafId, false);
                    }
                }
        }

        set(wx, topY + 1, wz, leafId, false);
        set(wx, topY + 2, wz, leafId, false);

        int branches = 3 + shapeHash % 3;
        double spin = (shapeHash >> 3 & 7) / 8.0 * (System.Math.PI * 2.0 / branches);
        for (int i = 0; i < branches; i++)
        {
            double angle = spin + i * System.Math.PI * 2.0 / branches;
            int by = crownBase + 1 + 3 * (i % 3);
            GiantBranch(wx, by, wz, angle, crownR - 1, 0, 1, logId, leafId, set);
        }
    }

    /// <summary>The giant jungle tree: trunk 30–50 high on buttress roots, five or six long branches high up, each
    /// with a leaf ball, under a very wide, flat hollow canopy radius 9–15.</summary>
    private static void BuildGiantJungle(int wx, int baseY, int wz, double sizeF, int shapeHash,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int trunk = System.Math.Clamp((int)System.Math.Round(sizeF), 3, 5);
        int height = System.Math.Clamp((int)System.Math.Round(10.0 * sizeF), 30, 50);
        int crownR = System.Math.Clamp((int)System.Math.Round(3.0 * sizeF), 9, 15);
        int crownRy = System.Math.Clamp((int)System.Math.Round(crownR * 0.45), 4, 7);
        int topY = baseY + height;
        GiantTrunk(wx, baseY, wz, trunk, topY, logId, set);

        // Buttress roots: one log column against each trunk face, three cells high, filling air only.
        int half = trunk / 2;
        int lo = -half - 1, hi = -half + trunk;
        for (int k = 0; k < trunk; k++)
        {
            for (int ty = baseY + 1; ty <= baseY + 3; ty++)
            {
                set(wx + lo, ty, wz - half + k, logId, false);
                set(wx + hi, ty, wz - half + k, logId, false);
                set(wx - half + k, ty, wz + lo, logId, false);
                set(wx - half + k, ty, wz + hi, logId, false);
            }
        }

        int branches = 5 + shapeHash % 2;
        double spin = (shapeHash >> 3 & 7) / 8.0 * (System.Math.PI * 2.0 / branches);
        int length = System.Math.Min(crownR - 4, 11);
        int ballR = System.Math.Clamp((int)System.Math.Round(sizeF * 0.8), 2, 4);
        for (int i = 0; i < branches; i++)
        {
            double angle = spin + i * System.Math.PI * 2.0 / branches;
            int by = baseY + (int)System.Math.Round(height * (0.6 + 0.05 * (i % 5)));
            GiantBranch(wx, by, wz, angle, length, length / 4, ballR, logId, leafId, set);
        }

        GiantCrownShell(wx, topY - 1, wz, crownR, crownRy, 0.5, leafId, set);
    }

    /// <summary>Builds one giant shape into a cell list relative to its root column (tests): (dx, dy, dz), dy
    /// counted from the root row (the lowest surface of the footprint).</summary>
    internal static List<(int Dx, int Dy, int Dz, bool Log)> BuildGiantTreeForTest(GiantTreeShape shape, double sizeF, int shapeHash)
    {
        var cells = new List<(int, int, int, bool)>();
        var log = new BlockId(1);
        var leaf = new BlockId(2);
        void Set(int x, int y, int z, BlockId id, bool _) => cells.Add((x, y, z, id == log));
        BuildGiantTree(shape, 0, 0, 0, sizeF, shapeHash, log, leaf, Set);
        return cells;
    }

    /// <summary>The scan margin and rise the giant pass promises (tests).</summary>
    internal static (int Margin, int Rise) GiantTreeEnvelopeForTest => (GiantTreeMargin, GiantTreeRise);
}
