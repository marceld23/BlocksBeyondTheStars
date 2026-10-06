// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Fossils in the terrain (#2341, terrain generation 21). A <b>fossil ridge</b> is a skeleton surfacing from the
/// ground: a low spine mound along a bearing with a bone line painted down its crest, a skull dome at the head end
/// painted bone four deep, and ribs — <see cref="BandKind.Rock"/> bands of bone — standing as a vault of arcs over
/// the spine with air under them, rooted at the ground on both sides. One cell in seven grows a <b>giant</b>: a
/// skull dome twenty-five across, a spine two hundred long, ribs forty tall — a find seen from the ship. The
/// <b>bone stratum</b> is one bone layer in every third period of the sediment strata of a fossil world, so cliffs
/// and mined shafts show it. Trig-free; the fossil monuments (sauropod, skull, serpent) live in
/// <see cref="MonumentGenerator"/>. (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    private const double FossilCellSize = 1500.0;
    private const double FossilChance = 0.3;
    private const long FossilSalt = 0xF0551A;
    private const double FossilMargin = 100.0 + 50.0 + 24.0 + 12.0; // a giant's half-length, skull, rib reach and slack

    private bool HasFossilRidges(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && !planet.Void && !planet.Cratered && !_crateredWorld
           && !planet.FloatingIslands && HasAir(planet) && planet.HasTag(TerrainTag.Fossil);

    private bool HasBoneStrata(PlanetType planet) => HasFossilRidges(planet) && HasStrata(planet);

    /// <summary>One bone layer in every third strata period (the strata are 2 of every 7 cells).</summary>
    private static bool StrataBoneBandAt(int worldY, int shift) => ((worldY + shift) % 21 + 21) % 21 == 3;

    private sealed class FossilRidge
    {
        public bool Giant;
        public int Dir;
        public double HalfLen, HalfWidth, MoundRise, SkullR, SkullRise, PaintHalf;
        public int RibCount, RibThick;
        public double RibReach, RibHeight, RibTol;
    }

    private static readonly CellCache<FossilRidge> _fossils = new();

    private bool TryGetFossil(PlanetType planet, WonderProfile w, int worldX, int worldZ, out FossilRidge fossil, out double dx, out double dz)
    {
        fossil = null!;
        var (cell, chance) = SpectacleCell(planet, FossilCellSize, FossilChance);
        if (!TryGetHotspot(w.Seed ^ FossilSalt, cell, chance, FossilMargin, worldX, worldZ, out ulong h, out dx, out dz))
        {
            return false;
        }

        if (dx * dx + dz * dz > FossilMargin * FossilMargin)
        {
            return false;
        }

        fossil = _fossils.GetOrAdd(w.Seed ^ FossilSalt, h, 0, static (_, hash) => BuildFossil(hash));
        return true;
    }

    private static FossilRidge BuildFossil(ulong h)
    {
        ulong s = (h ^ (ulong)FossilSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        bool giant = Next() < 0.15;
        var f = new FossilRidge { Giant = giant, Dir = (int)(Next() * 8.0) & 7 };
        if (giant)
        {
            f.HalfLen = 60.0 + Next() * 40.0;
            f.HalfWidth = 6.0 + Next() * 4.0;
            f.MoundRise = 4.0 + Next() * 4.0;
            f.SkullR = 15.0 + Next() * 10.0;
            f.SkullRise = 10.0 + Next() * 6.0;
            f.PaintHalf = 2.5;
            f.RibCount = 8 + (int)(Next() * 5.0);
            f.RibReach = 14.0 + Next() * 10.0;
            f.RibHeight = 20.0 + Next() * 20.0;
            f.RibThick = 2 + (int)(Next() * 2.0);
            f.RibTol = 1.2;
        }
        else
        {
            f.HalfLen = 15.0 + Next() * 15.0;
            f.HalfWidth = 3.0 + Next() * 2.0;
            f.MoundRise = 3.0 + Next() * 3.0;
            f.SkullR = 5.0 + Next() * 3.0;
            f.SkullRise = 4.0 + Next() * 3.0;
            f.PaintHalf = 1.5;
            f.RibCount = 3 + (int)(Next() * 5.0);
            f.RibReach = 6.0 + Next() * 8.0;
            f.RibHeight = 6.0 + Next() * 8.0;
            f.RibThick = 1 + (int)(Next() * 2.0);
            f.RibTol = 0.6;
        }

        return f;
    }

    /// <summary>The spine's frame at a column: along (the head end is negative), across, and the distance to the
    /// skull centre, which sits past the head end of the spine.</summary>
    private static void FossilFrame(FossilRidge f, double dx, double dz, out double along, out double across, out double skullDist)
    {
        var dir = EightDirs[f.Dir];
        AlongAcross(dx, dz, dir, out along, out across);
        double skullAlong = -f.HalfLen - f.SkullR * 0.8;
        double ex = along - skullAlong;
        skullDist = System.Math.Sqrt(ex * ex + across * across);
    }

    private static double FossilTaper(FossilRidge f, double along)
    {
        double endT = 1.0 - System.Math.Abs(along) / f.HalfLen;
        return endT >= 0.25 ? 1.0 : Smooth01(endT / 0.25);
    }

    /// <summary>The mound and the skull dome (the landmark row).</summary>
    private double FossilRidgeOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetFossil(planet, w, worldX, worldZ, out var f, out double dx, out double dz))
        {
            return 0.0;
        }

        FossilFrame(f, dx, dz, out double along, out double across, out double skullDist);
        double rise = 0.0;
        if (skullDist < f.SkullR)
        {
            double u = skullDist / f.SkullR;
            rise = f.SkullRise * System.Math.Sqrt(1.0 - u * u);
        }

        if (System.Math.Abs(along) <= f.HalfLen && System.Math.Abs(across) <= f.HalfWidth)
        {
            double v = across / f.HalfWidth;
            rise = System.Math.Max(rise, f.MoundRise * (1.0 - v * v) * FossilTaper(f, along));
        }

        return rise;
    }

    /// <summary>The bone paint: the spine's crest line two deep, the skull dome four deep.</summary>
    private BlockId? FossilRidgePaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!TryGetFossil(planet, w, worldX, worldZ, out var f, out double dx, out double dz))
        {
            return null;
        }

        var bone = _content.GetBlock("bone")?.NumericId ?? BlockId.Air;
        if (bone.IsAir)
        {
            return null;
        }

        FossilFrame(f, dx, dz, out double along, out double across, out double skullDist);
        if (skullDist < f.SkullR * 0.8)
        {
            fillToY = surfaceY - 4;
            return bone;
        }

        if (System.Math.Abs(along) <= f.HalfLen && System.Math.Abs(across) <= f.PaintHalf)
        {
            fillToY = surfaceY - 2;
            return bone;
        }

        return null;
    }

    /// <summary>The ribs: a vault of arcs over the spine, each rooted at the ground on both sides.</summary>
    private int AppendFossilBands(PlanetType planet, WonderProfile w, int worldX, int worldZ, System.Span<ColumnBand> bands, int n)
    {
        if (!TryGetFossil(planet, w, worldX, worldZ, out var f, out double dx, out double dz))
        {
            return n;
        }

        FossilFrame(f, dx, dz, out double along, out double across, out _);
        if (System.Math.Abs(across) > f.RibReach || along < -f.HalfLen || along > f.HalfLen)
        {
            return n;
        }

        double spacing = 2.0 * f.HalfLen / (f.RibCount + 1);
        double k = System.Math.Round((along + f.HalfLen) / spacing);
        if (k < 1 || k > f.RibCount || System.Math.Abs(along + f.HalfLen - k * spacing) > f.RibTol)
        {
            return n;
        }

        var bone = _content.GetBlock("bone")?.NumericId ?? BlockId.Air;
        double u = across / f.RibReach;
        double arc = f.RibHeight * System.Math.Sqrt(System.Math.Max(0.0, 1.0 - u * u));
        int top = RawSurfaceHeight(planet, w, worldX, worldZ) + (int)System.Math.Round(f.MoundRise * FossilTaper(f, along) + arc);
        bands[n++] = new ColumnBand { Bottom = top - f.RibThick + 1, Top = top, Kind = BandKind.Rock, Material = bone };
        return n;
    }

    // ---------------- test seams ----------------

    /// <summary>The fossil ridges of a world (tests): spine centre, bearing, half-length, skull centre and radius,
    /// whether it is a giant, rib count, reach and height.</summary>
    internal System.Collections.Generic.List<(int X, int Z, int Dir, double HalfLen, int SkullX, int SkullZ, double SkullR, bool Giant, int RibCount, double RibReach, double RibHeight)>
        FossilRidgesForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int, int, int, double, int, int, double, bool, int, double, double)>();
        var w = WonderFor(planet);
        if (!w.FossilRidges)
        {
            return list;
        }

        var (cell, chance) = SpectacleCell(planet, FossilCellSize, FossilChance);
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / cell));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / cell));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        for (int czI = 0; czI < nz; czI++)
            for (int cxI = 0; cxI < nx; cxI++)
            {
                int x = (int)(cxI * cw + cw * 0.5);
                int z = (int)(czI * ch + ch * 0.5) - period / 2;
                if (!TryGetHotspot(w.Seed ^ FossilSalt, cell, chance, FossilMargin, x, z, out _, out double dx, out double dz))
                {
                    continue;
                }

                int cx = WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference);
                int cz = WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference);
                if (!TryGetFossil(planet, w, cx, cz, out var f, out _, out _))
                {
                    continue;
                }

                var dir = EightDirs[f.Dir];
                double skullAlong = -f.HalfLen - f.SkullR * 0.8;
                list.Add((cx, cz, f.Dir, f.HalfLen,
                    WorldConstants.WrapX(cx + (int)System.Math.Round(dir.X * skullAlong), _circumference), cz + (int)System.Math.Round(dir.Z * skullAlong),
                    f.SkullR, f.Giant, f.RibCount, f.RibReach, f.RibHeight));
            }

        return list;
    }

    /// <summary>Whether a column lies in a sediment-strata region and the world lays a bone stratum (tests).</summary>
    internal bool BoneStrataRegionForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.BoneStrata && StrataShiftAt(w.Seed, worldX, worldZ) != int.MinValue;
    }
}
