// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>One oil pocket as the oil echo (#2372) reads it: where its centre column stands, the oil span there, and how
/// far it lies from the point that asked.</summary>
public sealed class OilPocketSite
{
    /// <summary>The pocket's centre column (canonical X).</summary>
    public int X { get; set; }

    public int Z { get; set; }

    /// <summary>The oil span of the centre column (Lo &gt; Hi when the column only grazes the shell).</summary>
    public int OilLo { get; set; }

    public int OilHi { get; set; }

    /// <summary>The horizontal distance from the asking point, in blocks (longitude and latitude wrap).</summary>
    public double Distance { get; set; }

    /// <summary>Generation 22: this pocket seeps — a tar chimney leads from it to the ground.</summary>
    public bool Seeps { get; set; }
}

/// <summary>
/// Terrain generation 22 (#2371) — the oil seeps, partial of <see cref="WorldGenerator"/>. About a third of the oil pockets
/// reach the surface: a solid TAR chimney about three blocks wide from the pocket's shell up to the ground (tar, never oil
/// or air — oil cannot be breathed in and an open shaft seventy blocks deep is a fall trap; tar breaks with any tool, so a
/// player follows the black trail straight down), a tar patch round its mouth, and a small oil puddle in the middle where no
/// water stands over it — the first oil pumpable at once. A pure function of the pocket's hash (bits 33–35, which no older
/// roll used), so every column of a seep agrees without asking its neighbours; claimed in the pocket's own branch of the
/// column loop, before the topsoil, the tunnels and the caves. The mega-cavern, an underground river and a geode still win
/// where they cross the chimney. The trees, the plants, the set dressing and the settlements keep off the patch
/// (<see cref="OilSeepNear"/>, <see cref="FootprintTouchesOilSeep"/>). The same pocket grid also answers the terrain
/// scanner's oil echo (#2372, <see cref="FindOilPocketsNear"/>).
/// </summary>
public sealed partial class WorldGenerator
{
    private const int OilSeepEighths = 3;            // 3 of 8 pockets seep — "about a third"
    private const double OilSeepChimneyRadius = 1.5; // the chimney: the centre column and its eight neighbours
    private const double OilSeepPadRadius = 3.2;     // the tar patch round the mouth
    private const double OilSeepPuddleRadius = 1.0;  // the oil puddle: the centre and its four axis neighbours
    private const int OilSeepPadDepth = 2;           // the patch is two cells of tar deep
    private const double OilSeepKeepClear = OilSeepPadRadius + 1.5; // trees, props, plants stand off this far

    /// <summary>Whether the pocket with this hash seeps (generation 22).</summary>
    private static bool IsSeepingPocket(ulong h) => ((h >> 33) & 0x7) < OilSeepEighths;

    /// <summary>The seep's cells on one column of a seeping pocket: the chimney columns from the shell's top
    /// (<paramref name="shellTop"/> + 1) to the ground, the patch columns their top <see cref="OilSeepPadDepth"/> cells;
    /// int.MaxValue elsewhere. <paramref name="puddle"/> marks the puddle columns (the caller drops it under water).</summary>
    private static void OilSeepSpan(ulong h, double dx, double dz, int shellTop, int seabedY, out int seepLo, out bool puddle)
    {
        seepLo = int.MaxValue;
        puddle = false;
        double d2 = dx * dx + dz * dz;
        if (!IsSeepingPocket(h) || d2 > OilSeepPadRadius * OilSeepPadRadius)
        {
            return;
        }

        seepLo = d2 <= OilSeepChimneyRadius * OilSeepChimneyRadius ? shellTop + 1 : seabedY - OilSeepPadDepth + 1;
        puddle = d2 <= OilSeepPuddleRadius * OilSeepPuddleRadius;
    }

    /// <summary>True when this column lies on or right beside an oil seep's tar patch on a generation-22 world — the
    /// stampers (trees, giant flora, Fifi plants, geysers, set dressing) skip it, so nothing grows out of the tar.</summary>
    public bool OilSeepNear(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (!w.OilSeeps)
        {
            return false;
        }

        return TryGetHotspot(w.Seed ^ OilHotspotSalt, OilCellSize, OilChance, OilMaxRadius + 4.0, worldX, worldZ,
                   out ulong h, out double dx, out double dz)
               && IsSeepingPocket(h)
               && dx * dx + dz * dz <= OilSeepKeepClear * OilSeepKeepClear;
    }

    /// <summary>True when an oil seep's patch lies inside (or right beside) the <paramref name="w"/> × <paramref name="l"/>
    /// footprint at <paramref name="ox"/>, <paramref name="oz"/> on a generation-22 world. A footprint narrower than a
    /// pocket cell (600) touches at most four cells, and its corners lie in every one of them, so asking the corners
    /// finds every pocket whose seep could reach it.</summary>
    public bool FootprintTouchesOilSeep(PlanetType planet, int ox, int oz, int w, int l)
    {
        var wonder = WonderFor(planet);
        if (!wonder.OilSeeps)
        {
            return false;
        }

        int x1 = ox + System.Math.Max(0, w - 1), z1 = oz + System.Math.Max(0, l - 1);
        return Touches(ox, oz) || Touches(x1, oz) || Touches(ox, z1) || Touches(x1, z1);

        bool Touches(int x, int z)
        {
            if (!TryGetHotspot(wonder.Seed ^ OilHotspotSalt, OilCellSize, OilChance, OilMaxRadius + 4.0, x, z,
                    out ulong h, out double dx, out double dz) || !IsSeepingPocket(h))
            {
                return false;
            }

            // The seep's centre, relative to the footprint's origin corner (longitude wraps, latitude is the probe's own).
            double sx = WorldConstants.WrapDeltaX((x - dx) - ox, _circumference);
            double sz = (z - dz) - oz;
            double nearX = System.Math.Clamp(sx, 0.0, x1 - ox), nearZ = System.Math.Clamp(sz, 0.0, z1 - oz);
            double ex = sx - nearX, ez = sz - nearZ;
            return ex * ex + ez * ez <= OilSeepKeepClear * OilSeepKeepClear;
        }
    }

    /// <summary>Whether this world really holds oil pockets: a living world of generation 18 or newer with a real
    /// underground — never a gas giant, whose pockets would lie in the gas sea (#2375).</summary>
    public bool CarriesOilPockets(PlanetType planet)
        => !planet.IsGasWorld && HasOilPockets(planet) && WonderFor(planet).OilPockets;

    /// <summary>The oil echo's query (#2372): every pocket whose centre lies within <paramref name="radius"/> blocks
    /// (horizontal) of the point, nearest first, read from the pocket function itself — a handful of hotspot cells, no
    /// chunk. The oil span is the generated one of the centre column; the caller checks the live world for what a pump
    /// has taken since. Empty on a world without oil pockets.</summary>
    public List<OilPocketSite> FindOilPocketsNear(PlanetType planet, int worldX, int worldZ, int radius)
    {
        var found = new List<OilPocketSite>();
        if (!CarriesOilPockets(planet) || radius <= 0)
        {
            return found;
        }

        var w = WonderFor(planet);
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / OilCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / OilCellSize));
        double cw = _circumference / (double)nx, ch = period / (double)nz;
        int qx = WorldConstants.WrapX(worldX, _circumference);
        int qzc = ((worldZ + period / 2) % period + period) % period; // canonical [0, period), as the hotspot grid reads it
        int cxI = System.Math.Min(nx - 1, (int)(qx / cw)), czI = System.Math.Min(nz - 1, (int)(qzc / ch));
        int kx = System.Math.Min(nx / 2, (int)System.Math.Ceiling(radius / cw) + 1);
        int kz = System.Math.Min(nz / 2, (int)System.Math.Ceiling(radius / ch) + 1);
        var seen = new HashSet<(int, int)>();
        for (int i = -kx; i <= kx; i++)
            for (int j = -kz; j <= kz; j++)
            {
                int ci = ((cxI + i) % nx + nx) % nx, cj = ((czI + j) % nz + nz) % nz;
                if (!seen.Add((ci, cj)))
                {
                    continue; // a small world: the ring wrapped onto a cell already asked
                }

                // Ask the cell's middle: TryGetHotspot answers for the cell the probe lies in, with the offset to its pocket.
                int px = (int)(ci * cw + cw / 2.0);
                int pz = (int)(cj * ch + ch / 2.0) - period / 2;
                if (!TryGetHotspot(w.Seed ^ OilHotspotSalt, OilCellSize, OilChance, OilMaxRadius + 4.0, px, pz,
                        out ulong h, out double dx, out double dz))
                {
                    continue;
                }

                int sx = WorldConstants.WrapX(px - (int)dx, _circumference);
                int sz = pz - (int)dz;
                double ddx = WorldConstants.WrapDeltaX((double)sx - qx, _circumference);
                double ddz = WrapLatDelta(((sz + period / 2) % period + period) % period - qzc, period);
                double dist = System.Math.Sqrt(ddx * ddx + ddz * ddz);
                if (dist > radius)
                {
                    continue;
                }

                if (!TryGetOilPocketSpan(planet, w, sx, sz, SurfaceHeight(planet, sx, sz),
                        out _, out _, out int inLo, out int inHi) || inHi < inLo)
                {
                    continue; // the ground dips through the pocket here — nothing sealed to point at
                }

                found.Add(new OilPocketSite
                {
                    X = sx,
                    Z = WorldConstants.WrapZ(sz, _circumference),
                    OilLo = inLo,
                    OilHi = inHi,
                    Distance = dist,
                    Seeps = w.OilSeeps && IsSeepingPocket(h),
                });
            }

        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return found;
    }

    private static double WrapLatDelta(double dz, int period)
    {
        if (dz > period / 2.0)
        {
            dz -= period;
        }

        if (dz < -period / 2.0)
        {
            dz += period;
        }

        return dz;
    }
}
