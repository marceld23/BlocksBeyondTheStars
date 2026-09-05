// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Generation-1 set dressing (#1648, landscape variety 5/6): the new prop rows and micro-ruins, the
/// seven new tree kinds and the giant-flora table (partial of <see cref="WorldGenerator"/>). Prop rows gate
/// on the world's generation plus a per-row world gate; the classic five rows keep their salts, order and
/// materials so every generation-0 world is byte-identical.</summary>
public sealed partial class WorldGenerator
{
    // ---------------- prop gates (generation ≥ 1; each also needs the row's material to exist) ----------------

    private static bool PropSolidGround(WonderProfile w, PlanetType p) => w.Generation >= 1 && !p.Void && !p.FloatingIslands;

    private static bool PropWooded(WonderProfile w, PlanetType p)
        => PropSolidGround(w, p) && !p.Cratered && HasAir(p) && p.FloraDensity > 0 && (p.TreeDensity ?? 0.012) > 0.0;

    private static bool PropSavanna(WonderProfile w, PlanetType p)
        => PropSolidGround(w, p) && string.Equals(p.FloraTheme, "savanna", System.StringComparison.OrdinalIgnoreCase);

    private static bool PropCold(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && HasAir(p) && p.BaseTemperature <= 5.0;

    private static bool PropFrozen(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && HasAir(p) && p.BaseTemperature <= -5.0;

    private static bool PropDry(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && HasAir(p) && WaterAbundanceOf(p) <= 0.3;

    private static bool PropVolcanic(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && p.HasTag(TerrainTag.Volcanic);

    private static bool PropCoast(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && HasAir(p) && WaterAbundanceOf(p) >= 0.5;

    private static bool PropCrystal(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && p.HasTag(TerrainTag.Crystal);

    private static bool PropAirless(WonderProfile w, PlanetType p) => w.Generation >= 1 && !p.Void && p.IsAirless;

    private static bool PropTarFlats(WonderProfile w, PlanetType p)
        => PropDry(w, p) && (p.HasTag(TerrainTag.Buttes) || p.HasTag(TerrainTag.Wind));

    private static bool PropRuins(WonderProfile w, PlanetType p) => PropSolidGround(w, p) && !p.Cratered && HasAir(p);

    // ---------------- prop shapes (fill air only, never carve) ----------------

    /// <summary>Two logs are stamped: a horizontal 3–5-long trunk along X or Z one cell above the ground of each column.</summary>
    private static void StampFallenLog(PropStamp s)
    {
        int len = 3 + s.ShapeHash % 3;
        bool alongX = (s.ShapeHash & 8) == 0;
        for (int i = 0; i < len; i++)
        {
            int px = s.Wx + (alongX ? i : 0), pz = s.Wz + (alongX ? 0 : i);
            int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
            s.Set(px, py + 1, pz, s.Material);
        }
    }

    /// <summary>A termite mound: a 2–3-tall spire on a 3-cell base.</summary>
    private static void StampTermiteMound(PropStamp s)
    {
        int h = 2 + s.ShapeHash % 2;
        for (int dy = 1; dy <= h; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, s.Material);
        }

        s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx, s.Sy + 1, s.Wz + ((s.ShapeHash & 4) == 0 ? 1 : -1), s.Material);
    }

    /// <summary>A cairn: a 3–4 stone stack with two side stones at the foot.</summary>
    private static void StampCairn(PropStamp s)
    {
        int h = 3 + s.ShapeHash % 2;
        for (int dy = 1; dy <= h; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, s.Material);
        }

        s.Set(s.Wx - 1, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx, s.Sy + 1, s.Wz + 1, s.Material);
    }

    /// <summary>A bone pile: a 2×2 scatter with one two-tall heap.</summary>
    private static void StampBonePile(PropStamp s)
    {
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Material);
        if ((s.ShapeHash & 1) == 0) s.Set(s.Wx, s.Sy + 1, s.Wz + 1, s.Material);
        if ((s.ShapeHash & 2) == 0) s.Set(s.Wx + 1, s.Sy + 1, s.Wz + 1, s.Material);
        s.Set(s.Wx, s.Sy + 2, s.Wz, s.Material);
    }

    /// <summary>A rib cage: seven arches of bone along one axis — the widest prop (7 across), which sets the
    /// set-dressing scan margin of 8.</summary>
    private static void StampRibCage(PropStamp s)
    {
        bool alongX = (s.ShapeHash & 8) == 0;
        for (int k = -3; k <= 3; k++)
        {
            int ax = s.Wx + (alongX ? k : 0), az = s.Wz + (alongX ? 0 : k);
            int height = System.Math.Abs(k) >= 3 ? 1 : System.Math.Abs(k) == 2 ? 2 : 3;
            for (int side = -1; side <= 1; side += 2)
            {
                int px = ax + (alongX ? 0 : side * 2), pz = az + (alongX ? side * 2 : 0);
                int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
                for (int dy = 1; dy <= height; dy++)
                {
                    s.Set(px, py + dy, pz, s.Material);
                }
            }

            if (height == 3)
            {
                // the arch top joins the two ribs
                for (int t = -1; t <= 1; t++)
                {
                    s.Set(ax + (alongX ? 0 : t), s.Sy + 4, az + (alongX ? t : 0), s.Material);
                }
            }
        }

        // the spine
        for (int k = -3; k <= 3; k++)
        {
            s.Set(s.Wx + (alongX ? k : 0), s.Sy + 1, s.Wz + (alongX ? 0 : k), s.Material);
        }
    }

    /// <summary>A 3×3 crystal cluster with 1–4-tall spikes.</summary>
    private static void StampCrystalCluster(PropStamp s)
    {
        int h = s.ShapeHash;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int bit = (dx + 1) * 3 + (dz + 1);
                int height = dx == 0 && dz == 0 ? 4 : 1 + ((h >> bit) & 0x3);
                if (dx != 0 && dz != 0 && ((h >> (bit + 2)) & 1) == 0)
                {
                    continue; // the odd empty corner
                }

                int px = s.Wx + dx, pz = s.Wz + dz;
                int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
                for (int dy = 1; dy <= height; dy++)
                {
                    s.Set(px, py + dy, pz, s.Material);
                }
            }
    }

    /// <summary>A coral outcrop on the dry strip above the waterline: one or two coral cells.</summary>
    private static void StampCoralOutcrop(PropStamp s)
    {
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        if ((s.ShapeHash & 1) == 0)
        {
            s.Set(s.Wx + ((s.ShapeHash & 2) == 0 ? 1 : -1), s.Sy + 1, s.Wz, s.Material);
        }
    }

    /// <summary>A meteorite: a 2–3-cell lump of iron ore resting on the regolith.</summary>
    private static void StampMeteorite(PropStamp s)
    {
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx + ((s.ShapeHash & 1) == 0 ? 1 : -1), s.Sy + 1, s.Wz, s.Material);
        if ((s.ShapeHash & 6) == 0)
        {
            s.Set(s.Wx, s.Sy + 2, s.Wz, s.Material);
        }
    }

    /// <summary>A tar pool: a 3×3 (corners rolled) tar pad flush on the flat ground.</summary>
    private static void StampTarPit(PropStamp s)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx != 0 && dz != 0 && ((s.ShapeHash >> ((dx + 1) + (dz + 1) * 3)) & 1) == 0)
                {
                    continue;
                }

                int px = s.Wx + dx, pz = s.Wz + dz;
                int py = s.Generator.SurfaceHeight(s.Planet, px, pz);
                s.Set(px, py + 1, pz, s.Material);
            }
    }

    // --- micro-ruins: a data cache rolls in with some of them ---

    /// <summary>An L-shaped wall fragment, 3 + 2 long, 2 tall (one course crumbled), a cache in the corner 30 % of the time.</summary>
    private static void StampWallFragment(PropStamp s)
    {
        for (int i = 0; i < 3; i++)
        {
            int px = s.Wx + i;
            int py = s.Generator.SurfaceHeight(s.Planet, px, s.Wz);
            int h = i == 2 && (s.ShapeHash & 1) == 0 ? 1 : 2;
            for (int dy = 1; dy <= h; dy++)
            {
                s.Set(px, py + dy, s.Wz, s.Material);
            }
        }

        for (int j = 1; j <= 2; j++)
        {
            int pz = s.Wz + j;
            int py = s.Generator.SurfaceHeight(s.Planet, s.Wx, pz);
            int h = j == 2 && (s.ShapeHash & 2) == 0 ? 1 : 2;
            for (int dy = 1; dy <= h; dy++)
            {
                s.Set(s.Wx, py + dy, pz, s.Material);
            }
        }

        if (!s.Cache.IsAir && s.ShapeHash % 10 < 3)
        {
            s.Set(s.Wx + 1, s.Sy + 1, s.Wz + 1, s.Cache);
        }
    }

    /// <summary>A buried pillar: 2–4 courses of ancient brick with rubble at the foot.</summary>
    private static void StampBuriedPillar(PropStamp s)
    {
        int h = 2 + s.ShapeHash % 3;
        for (int dy = 1; dy <= h; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, s.Material);
        }

        s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx - 1, s.Sy + 1, s.Wz + 1, s.Material);
    }

    /// <summary>A crashed probe: a 2×2 hull with a glass eye, debris around it, a cache under the hull half the time.</summary>
    private static void StampCrashedProbe(PropStamp s)
    {
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx, s.Sy + 1, s.Wz + 1, s.Material);
        s.Set(s.Wx + 1, s.Sy + 1, s.Wz + 1, s.Secondary.IsAir ? s.Material : s.Secondary);
        s.Set(s.Wx, s.Sy + 2, s.Wz, s.Material);
        s.Set(s.Wx + ((s.ShapeHash & 1) == 0 ? 3 : -2), s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx, s.Sy + 1, s.Wz + ((s.ShapeHash & 2) == 0 ? 3 : -2), s.Material);
        if (!s.Cache.IsAir && (s.ShapeHash & 4) == 0)
        {
            s.Set(s.Wx + 1, s.Sy + 2, s.Wz + 1, s.Cache);
        }
    }

    /// <summary>An abandoned mining rig: a 2×2 machine housing with a 3-tall pipe stack.</summary>
    private static void StampMiningRig(PropStamp s)
    {
        for (int dx = 0; dx <= 1; dx++)
            for (int dz = 0; dz <= 1; dz++)
            {
                s.Set(s.Wx + dx, s.Sy + 1, s.Wz + dz, s.Material);
            }

        var pipe = s.Secondary.IsAir ? s.Material : s.Secondary;
        for (int dy = 2; dy <= 4; dy++)
        {
            s.Set(s.Wx, s.Sy + dy, s.Wz, pipe);
        }

        s.Set(s.Wx + 1, s.Sy + 2, s.Wz + 1, pipe);
        if (!s.Cache.IsAir && s.ShapeHash % 10 < 4)
        {
            s.Set(s.Wx + 2, s.Sy + 1, s.Wz, s.Cache);
        }
    }

    /// <summary>A lone rune stone, 2 tall, a cache at its foot 40 % of the time.</summary>
    private static void StampRuneStone(PropStamp s)
    {
        s.Set(s.Wx, s.Sy + 1, s.Wz, s.Material);
        s.Set(s.Wx, s.Sy + 2, s.Wz, s.Material);
        if (!s.Cache.IsAir && s.ShapeHash % 10 < 4)
        {
            s.Set(s.Wx + 1, s.Sy + 1, s.Wz, s.Cache);
        }
    }

    // ---------------- trees (generation-1 kinds) ----------------

    /// <summary>A baobab: a thick 2×2 trunk 5–8 tall under a flat, wide crown.</summary>
    private static void BuildBaobab(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(6.0 * sizeF * hJit), 5, 8);
        int crownR = System.Math.Clamp((int)System.Math.Round(3.0 * sizeF * cJit), 2, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
            set(wx + 1, ty, wz, logId, true);
            set(wx, ty, wz + 1, logId, true);
            set(wx + 1, ty, wz + 1, logId, true);
        }

        for (int dy = 0; dy <= 1; dy++)
        {
            int r = crownR - dy;
            for (int dx = -r; dx <= r + 1; dx++)
                for (int dz = -r; dz <= r + 1; dz++)
                {
                    if ((dx - 0.5) * (dx - 0.5) + (dz - 0.5) * (dz - 0.5) <= r * r + 1.5)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                    }
                }
        }
    }

    /// <summary>A mangrove: four stilt roots rising diagonally to a trunk that starts three cells up, a rounded crown.</summary>
    private static void BuildMangrove(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(6.0 * sizeF * hJit), 5, 9);
        int crownR = System.Math.Clamp((int)System.Math.Round(2.0 * sizeF * cJit), 1, 2);
        int[,] roots = { { 2, 0 }, { -2, 0 }, { 0, 2 }, { 0, -2 } };
        for (int r = 0; r < 4; r++)
        {
            set(wx + roots[r, 0], sy + 1, wz + roots[r, 1], logId, true);
            set(wx + roots[r, 0] / 2, sy + 2, wz + roots[r, 1] / 2, logId, true);
        }

        int topY = sy + height;
        for (int ty = sy + 3; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        for (int dy = -1; dy <= crownR; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    if (dx * dx + dz * dz + dy * dy <= crownR * crownR + 1)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                    }
                }
    }

    /// <summary>A bamboo grove: 3–6 one-wide stems 8–12 tall within radius 2, two leaf cells at each top.</summary>
    private static void BuildBamboo(int wx, int sy, int wz, double sizeF, double hJit, int shapeHash,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int stems = 3 + shapeHash % 4;
        (int X, int Z)[] spots = { (0, 0), (1, 1), (-1, 1), (2, -1), (-2, 0), (0, -2), (1, -1), (-1, -2) };
        for (int i = 0; i < stems && i < spots.Length; i++)
        {
            int height = System.Math.Clamp((int)System.Math.Round(10.0 * sizeF * hJit) + (shapeHash >> i) % 3 - 1, 8, 12);
            int px = wx + spots[i].X, pz = wz + spots[i].Z;
            for (int ty = sy + 1; ty <= sy + height; ty++)
            {
                set(px, ty, pz, logId, true);
            }

            set(px, sy + height + 1, pz, leafId, false);
            set(px + ((shapeHash >> i) & 1) * 2 - 1, sy + height, pz, leafId, false);
        }
    }

    /// <summary>A saguaro: a 4–7 column with two up-turned arms — the cactus is built from the leaf block (green).</summary>
    private static void BuildSaguaro(int wx, int sy, int wz, double sizeF, double hJit, int shapeHash,
        BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(5.5 * sizeF * hJit), 4, 7);
        for (int ty = sy + 1; ty <= sy + height; ty++)
        {
            set(wx, ty, wz, leafId, true);
        }

        int armY = sy + System.Math.Max(2, height / 2);
        int ax = (shapeHash & 1) == 0 ? 1 : -1;
        set(wx + ax, armY, wz, leafId, true);
        set(wx + ax, armY + 1, wz, leafId, true);
        set(wx + ax, armY + 2, wz, leafId, true);
        if ((shapeHash & 2) == 0)
        {
            int az = (shapeHash & 4) == 0 ? 1 : -1;
            set(wx, armY + 1, wz + az, leafId, true);
            set(wx, armY + 2, wz + az, leafId, true);
        }
    }

    /// <summary>A willow: a 4–6 trunk under a broad crown whose rim drips 3–4-cell leaf strands.</summary>
    private static void BuildWillow(int wx, int sy, int wz, double sizeF, double hJit, double cJit,
        BlockId logId, BlockId leafId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(5.0 * sizeF * hJit), 4, 6);
        int crownR = System.Math.Clamp((int)System.Math.Round(2.5 * sizeF * cJit), 2, 3);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, logId, true);
        }

        for (int dy = 0; dy <= 1; dy++)
            for (int dx = -crownR; dx <= crownR; dx++)
                for (int dz = -crownR; dz <= crownR; dz++)
                {
                    int d2 = dx * dx + dz * dz;
                    if (d2 <= crownR * crownR + 1)
                    {
                        set(wx + dx, topY + dy, wz + dz, leafId, false);
                        if (dy == 0 && d2 >= (crownR - 1) * (crownR - 1) + 1 && ((dx + dz) & 1) == 0)
                        {
                            for (int s = 1; s <= 3; s++)
                            {
                                set(wx + dx, topY - s, wz + dz, leafId, false); // the drooping strands
                            }
                        }
                    }
                }
    }

    /// <summary>An alien mushroom tree: a 4–6 stem under a flat cap disc of radius 2.</summary>
    private static void BuildMushroomTree(int wx, int sy, int wz, double sizeF, double hJit,
        BlockId stemId, BlockId capId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(5.0 * sizeF * hJit), 4, 6);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, stemId, true);
        }

        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx * dx + dz * dz <= 5)
                {
                    set(wx + dx, topY + 1, wz + dz, capId, false);
                }
            }

        set(wx, topY + 2, wz, capId, false);
    }

    /// <summary>A crystal tree: a 3–5 crystal shaft crowned by a cross of crystal arms.</summary>
    private static void BuildCrystalTree(int wx, int sy, int wz, double sizeF, double hJit,
        BlockId crystalId, System.Action<int, int, int, BlockId, bool> set)
    {
        int height = System.Math.Clamp((int)System.Math.Round(4.0 * sizeF * hJit), 3, 5);
        int topY = sy + height;
        for (int ty = sy + 1; ty <= topY; ty++)
        {
            set(wx, ty, wz, crystalId, true);
        }

        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        for (int i = 0; i < 4; i++)
        {
            set(wx + dirs[i, 0], topY, wz + dirs[i, 1], crystalId, false);
            set(wx + dirs[i, 0] * 2, topY + 1, wz + dirs[i, 1] * 2, crystalId, false);
        }

        set(wx, topY + 1, wz, crystalId, false);
        set(wx, topY + 2, wz, crystalId, false);
    }

    /// <summary>Builds one generation-1 tree kind into a cell list relative to its column (tests): (dx, dy, dz).</summary>
    internal static List<(int Dx, int Dy, int Dz)> BuildTreeForTest(TreeKind kind, double sizeF, double hJit, double cJit, int shapeHash)
    {
        var cells = new List<(int, int, int)>();
        var a = new BlockId(1);
        var b = new BlockId(2);
        void Set(int x, int y, int z, BlockId _, bool __) => cells.Add((x, y, z));
        switch (kind)
        {
            case TreeKind.Baobab: BuildBaobab(0, 0, 0, sizeF, hJit, cJit, a, b, Set); break;
            case TreeKind.Mangrove: BuildMangrove(0, 0, 0, sizeF, hJit, cJit, a, b, Set); break;
            case TreeKind.Bamboo: BuildBamboo(0, 0, 0, sizeF, hJit, shapeHash, a, b, Set); break;
            case TreeKind.Saguaro: BuildSaguaro(0, 0, 0, sizeF, hJit, shapeHash, b, Set); break;
            case TreeKind.Willow: BuildWillow(0, 0, 0, sizeF, hJit, cJit, a, b, Set); break;
            case TreeKind.MushroomTree: BuildMushroomTree(0, 0, 0, sizeF, hJit, a, b, Set); break;
            case TreeKind.CrystalTree: BuildCrystalTree(0, 0, 0, sizeF, hJit, b, Set); break;
            default: BuildBroadleaf(0, 0, 0, sizeF, hJit, cJit, a, b, Set); break;
        }

        return cells;
    }

    /// <summary>True when water lies within four cells of the column (a mangrove's place). Four memoised lookups.</summary>
    private bool NearWater(PlanetType planet, int wx, int wz, int seaLevel)
    {
        for (int d = 0; d < 4; d++)
        {
            int px = wx + (d == 0 ? 4 : d == 1 ? -4 : 0), pz = wz + (d == 2 ? 4 : d == 3 ? -4 : 0);
            if (SurfaceHeight(planet, px, pz) < seaLevel || SurfacePondDepth(planet, px, pz) > 0 || SurfaceGen1WaterDepth(planet, px, pz) > 0)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- giant flora table ----------------

    /// <summary>What grows huge on which ground (#1648): the classic giant mushroom on mycelium (its roll and
    /// sizes untouched), and from generation 1 the giant fern on jungle mud, the giant crystal on crystal
    /// ground and the giant cactus on sand.</summary>
    private readonly struct GiantFloraKind
    {
        public GiantFloraKind(string name, string host, string stem, string cap, long salt, double density, int gen)
        {
            Name = name;
            Host = host;
            Stem = stem;
            Cap = cap;
            Salt = salt;
            Density = density;
            Generation = gen;
        }

        public readonly string Name, Host, Stem, Cap;
        public readonly long Salt;
        public readonly double Density;
        public readonly int Generation; // the first generation the row grows on
    }

    private static readonly GiantFloraKind[] GiantFloraKinds =
    {
        new("giant-fern", "mud", "wood_log", "tree_leaves", 0x6F3A0, 0.006, 1),
        new("giant-crystal", "crystal", "crystal", "crystal", 0x6C570, 0.004, 1),
        new("giant-cactus", "sand", "tree_leaves", "tree_leaves", 0x6CAC0, 0.0025, 1),
    };

    /// <summary>Stamps the generation-1 giant flora (#1648): the giant-mushroom recipe (per-column roll on the host
    /// ground, tree line, dry ground, a bell-sized stem) with each row's own salt and shape — a fan-crowned fern
    /// on mud, a spiked crystal on crystal ground, an armed cactus on sand. Never touches the mycelium path.</summary>
    private void StampGiantFloraGen1(PlanetType planet, long seed, ChunkData chunk, ChunkCoord coord,
        List<BiomeResolved> biomes, int fluidLevel)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        int cs = WorldConstants.ChunkSize;
        const int maxR = 3;

        void SetCell(int wx, int wy, int wz, BlockId block, bool overwrite)
        {
            int lx = wx - origin.X, ly = wy - origin.Y, lz = wz - origin.Z;
            if (lx < 0 || lx >= cs || ly < 0 || ly >= cs || lz < 0 || lz >= cs)
            {
                return;
            }

            if (!overwrite && !chunk.Get(lx, ly, lz).IsAir)
            {
                return;
            }

            chunk.Set(lx, ly, lz, block);
        }

        var calib = CalibFor(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        foreach (var row in GiantFloraKinds)
        {
            var hostId = _content.GetBlock(row.Host)?.NumericId ?? BlockId.Air;
            var stemId = _content.GetBlock(row.Stem)?.NumericId ?? BlockId.Air;
            var capId = _content.GetBlock(row.Cap)?.NumericId ?? BlockId.Air;
            if (hostId.IsAir || stemId.IsAir || capId.IsAir || !biomes.Exists(b => b.Surface == hostId))
            {
                continue; // this world has no such ground
            }

            for (int wx = origin.X - maxR; wx < origin.X + cs + maxR; wx++)
                for (int wz = origin.Z - maxR; wz < origin.Z + cs + maxR; wz++)
                {
                    int cx = WorldConstants.WrapX(wx, _circumference);
                    if (Noise.Value01(seed + row.Salt, cx, 17, Wz(wz)) >= row.Density)
                    {
                        continue;
                    }

                    int sy = SurfaceHeight(planet, wx, wz);
                    if (sy + 1 > origin.Y + cs - 1 || sy + MaxStampRise < origin.Y)
                    {
                        continue;
                    }

                    var surf = biomes[biomes.Count <= 1 ? 0 : BiomeIndex(calib, seed, wx, wz, biomes.Count, sy)].Surface;
                    if (surf != hostId || TempAt(calib, sy) < TreeLineC)
                    {
                        continue;
                    }

                    if (sy + 1 <= fluidLevel || SurfacePondDepth(planet, wx, wz) > 0 || SurfaceRiverDepth(planet, wx, wz) > 0
                        || SurfaceGen1WaterDepth(planet, wx, wz) > 0
                        || DryBeachAt(planet, calib, seed, RiverFieldFor(planet), waterId, wx, wz, sy))
                    {
                        continue;
                    }

                    double sizeF = SizeFactor(seed + row.Salt + 1, wx, wz, 0.30);
                    double hJit = SizeFactor(seed + row.Salt + 2, wx, wz, 0.12);
                    int shapeHash = (int)(Noise.Value01(seed + row.Salt + 3, cx, 41, Wz(wz)) * 997);
                    switch (row.Name)
                    {
                        case "giant-fern":
                            {
                                int height = System.Math.Clamp((int)System.Math.Round(5.0 * sizeF * hJit), 4, 7);
                                int topY = sy + height;
                                for (int ty = sy + 1; ty <= topY; ty++)
                                {
                                    SetCell(wx, ty, wz, stemId, true);
                                }

                                // a flat fan of fronds, the tips drooping one cell
                                int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 }, { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 } };
                                for (int i = 0; i < 8; i++)
                                {
                                    SetCell(wx + dirs[i, 0], topY, wz + dirs[i, 1], capId, false);
                                    SetCell(wx + dirs[i, 0] * 2, topY, wz + dirs[i, 1] * 2, capId, false);
                                    SetCell(wx + dirs[i, 0] * 3, topY - 1, wz + dirs[i, 1] * 3, capId, false);
                                }

                                SetCell(wx, topY + 1, wz, capId, false);
                                break;
                            }

                        case "giant-crystal":
                            {
                                int height = System.Math.Clamp((int)System.Math.Round(6.0 * sizeF * hJit), 4, 9);
                                for (int ty = sy + 1; ty <= sy + height; ty++)
                                {
                                    SetCell(wx, ty, wz, stemId, true);
                                    if (ty <= sy + 2)
                                    {
                                        SetCell(wx + 1, ty, wz, stemId, true);
                                        SetCell(wx, ty, wz + 1, stemId, true);
                                    }
                                }

                                // satellite spikes
                                SetCell(wx + 2, sy + 1, wz, capId, false);
                                SetCell(wx + 2, sy + 2, wz, capId, false);
                                SetCell(wx - 1, sy + 1, wz - 1, capId, false);
                                if ((shapeHash & 1) == 0)
                                {
                                    SetCell(wx - 1, sy + 2, wz - 1, capId, false);
                                }

                                break;
                            }

                        default: // giant-cactus
                            {
                                int height = System.Math.Clamp((int)System.Math.Round(6.0 * sizeF * hJit), 4, 9);
                                for (int ty = sy + 1; ty <= sy + height; ty++)
                                {
                                    SetCell(wx, ty, wz, stemId, true);
                                }

                                int armY = sy + System.Math.Max(2, height / 2);
                                for (int i = 0; i < 2; i++)
                                {
                                    int ax = i == 0 ? 1 : -1;
                                    int len = 2 + ((shapeHash >> i) & 1);
                                    SetCell(wx + ax, armY + i, wz, stemId, true);
                                    for (int k = 1; k <= len; k++)
                                    {
                                        SetCell(wx + ax, armY + i + k, wz, stemId, true);
                                    }
                                }

                                break;
                            }
                    }
                }
        }
    }

    /// <summary>The giant-flora rows active on this world (tests).</summary>
    internal string[] GiantFloraForTest(PlanetType planet)
    {
        var w = WonderFor(planet);
        var names = new List<string>();
        foreach (var k in GiantFloraKinds)
        {
            if (w.Generation >= k.Generation && !(_content.GetBlock(k.Host)?.NumericId ?? BlockId.Air).IsAir)
            {
                names.Add(k.Name);
            }
        }

        return names.ToArray();
    }
}
