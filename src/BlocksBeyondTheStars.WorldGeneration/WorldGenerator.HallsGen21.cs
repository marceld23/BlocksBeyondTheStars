// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Cave portals and daylight halls (#2340, terrain generation 21).
/// <para><b>Portals</b> are a worm family riding the massif's own cell: a flared mouth (radius 7–9, so 14–18 tall
/// and wide — a ship fits) at the flank's foot on a rolled bearing, climbing to the mountain hall's level and
/// narrowing into it, so the hall of #1688 is entered through a gate instead of a crawl.</para>
/// <para><b>Daylight halls</b> are a cell of their own on cave-bearing karst / portal country: an ellipsoid 40–120
/// wide and 20–35 half-tall centred 30–60 under the raw ground, so its roof breaks the surface in a skylight. Under
/// the skylight the column's ground IS the hall floor (the column phase moves the surface down there: flora, trees
/// and props grow on the floor, the pond and river stages work on it, a lake fills the bowl's middle on three in
/// five); the roofed rim is written like a mega-cavern. A short worm family riding the hall's cell cuts a walk-in
/// from outside. The pad planner and the structure placer see the hall through <c>ColumnHasVoidBelow</c>. Never
/// under the sea. (partial of <see cref="WorldGenerator"/>)</para>
/// </summary>
public sealed partial class WorldGenerator
{
    // ================= portals =================
    private const long PortalSalt = 0x9027A1;

    private bool HasPortals(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && HasMountainHalls(planet)
           && (planet.HasTag(TerrainTag.Portals) || planet.HasTag(TerrainTag.Karst));

    /// <summary>The portal into a massif's hall: a wide mouth at the flank's foot, climbing to the hall's level.</summary>
    private TunnelSeg[] PortalSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        double radius = 150.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MassifMaxRadius - 150.0);
        double massifHeight = 120.0 + ((h >> 26) & 0x3FF) / 1023.0 * 100.0;
        massifHeight = System.Math.Min(massifHeight, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);
        double ground = RawSurfaceHeight(planet, w, centreX, centreZ);

        ulong s = (h ^ (ulong)PortalSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        double angle = Next() * System.Math.PI * 2.0;
        double cos = System.Math.Cos(angle), sin = System.Math.Sin(angle);
        double mouthR = 7.0 + Next() * 2.0;
        double outerD = radius * 0.6 + 20.0;
        int mx = centreX + (int)System.Math.Round(cos * outerD), mz = centreZ + (int)System.Math.Round(sin * outerD);
        double mouthY = RawSurfaceHeight(planet, w, mx, mz) + mouthR + 1.0; // opens at the local ground
        double hallY = ground + massifHeight * 0.3;                          // the mountain hall's own level
        double innerD = radius * 0.35;
        return new[]
        {
            new TunnelSeg(cos * outerD, mouthY, sin * outerD, cos * innerD, hallY, sin * innerD, mouthR),
            new TunnelSeg(cos * innerD, hallY, sin * innerD, 0.0, hallY, 0.0, 5.0),
        };
    }

    // ================= daylight halls =================
    private const double HallCellSize = 1700.0;
    private const double HallChance = 0.2;
    private const long HallSalt = 0xDA11A7;
    private const double HallMaxRx = 60.0;
    private const double HallMargin = HallMaxRx * 1.25 + 16.0 + 2.0;

    private bool HasDaylightHalls(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && !planet.Void && !planet.Cratered && !_crateredWorld
           && !planet.FloatingIslands && HasAir(planet) && planet.CaveThreshold > 0.0
           && (planet.HasTag(TerrainTag.Portals) || planet.HasTag(TerrainTag.Karst));

    private sealed class DaylightHall
    {
        public bool Has;
        public double Rx, Rz, Ry;
        public int Cy, LakeY;
    }

    private static readonly CellCache<DaylightHall> _halls = new();

    private bool TryGetDaylightHall(PlanetType planet, WonderProfile w, int worldX, int worldZ, out DaylightHall hall, out double dx, out double dz)
    {
        hall = null!;
        double chance = System.Math.Min(0.95, HallChance * System.Math.Max(0.25, planet.SpectacleDensity));
        if (!TryGetHotspot(w.Seed ^ HallSalt, HallCellSize, chance, HallMargin, worldX, worldZ, out ulong h, out dx, out dz))
        {
            return false;
        }

        if (dx * dx + dz * dz > HallMargin * HallMargin)
        {
            return false;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        hall = _halls.GetOrAdd(w.Seed ^ HallSalt, h, (this, planet, w, cx, cz), static (s, hash) => s.Item1.BuildDaylightHall(s.planet, s.w, hash, s.cx, s.cz));
        return hall.Has;
    }

    private DaylightHall BuildDaylightHall(PlanetType planet, WonderProfile w, ulong h, int cx, int cz)
    {
        ulong s = (h ^ (ulong)HallSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        int raw = RawSurfaceHeight(planet, w, cx, cz);
        int sea = SeaLevel(planet);
        var hall = new DaylightHall { Has = sea == int.MinValue || raw >= sea + 8 };
        hall.Rx = 20.0 + Next() * (HallMaxRx - 20.0);
        hall.Rz = hall.Rx * (0.75 + Next() * 0.5);
        // The floor lies 40–90 under the ground and the ellipsoid's top 6–12 OVER it, so the roof breaks open where
        // the terrain is at or below the cell centre's ground — a skylight about half to two thirds of the hall wide,
        // ragged where the ground rolls. The lake fills the lowest 40 % of the bowl on three halls in five.
        double floor = raw - 40.0 - Next() * 50.0;
        double top = raw + 6.0 + Next() * 6.0;
        hall.Ry = (top - floor) * 0.5;
        hall.Cy = (int)System.Math.Round(floor + hall.Ry);
        hall.LakeY = Next() < 0.6 ? (int)System.Math.Round(floor + hall.Ry * 0.4) : int.MinValue;
        return hall;
    }

    /// <summary>The hall's vertical span at a column, the lake surface (MinValue dry) and whether the roof is open
    /// there — the skylight: the ellipsoid's top within two cells of the ground, snapped to it.</summary>
    private bool TryGetDaylightHallSpan(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY,
        out int lo, out int hi, out int lakeY, out bool open)
    {
        lo = hi = 0;
        lakeY = int.MinValue;
        open = false;
        if (!TryGetDaylightHall(planet, w, worldX, worldZ, out var hall, out double dx, out double dz))
        {
            return false;
        }

        double q = 1.0 - (dx / hall.Rx) * (dx / hall.Rx) - (dz / hall.Rz) * (dz / hall.Rz);
        if (q <= 0.0)
        {
            return false;
        }

        double half = hall.Ry * System.Math.Sqrt(q);
        lo = hall.Cy - (int)half;
        hi = hall.Cy + (int)half;
        lakeY = hall.LakeY;
        if (hi >= surfaceY - 2)
        {
            open = true;
            hi = System.Math.Max(hi, surfaceY);
        }

        return true;
    }

    /// <summary>The hall floor under an open skylight column, for the stamps that read the ground.</summary>
    private bool DaylightHallFloorAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int floorY)
    {
        floorY = 0;
        int surface = SurfaceHeight(planet, worldX, worldZ);
        if (!TryGetDaylightHallSpan(planet, w, worldX, worldZ, surface, out int lo, out _, out _, out bool open) || !open)
        {
            return false;
        }

        floorY = lo - 1;
        return true;
    }

    /// <summary>The walk-in: one capsule from outside the hall's rim down to its floor on a rolled bearing.</summary>
    private TunnelSeg[] HallPortalSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        if (!TryGetDaylightHall(planet, w, centreX, centreZ, out var hall, out _, out _))
        {
            return System.Array.Empty<TunnelSeg>();
        }

        ulong g = h * 0x9E3779B97F4A7C15UL;
        var dir = EightDirs[(int)((g >> 8) & 0x7)];
        double outer = System.Math.Max(hall.Rx, hall.Rz) + 14.0;
        int ox = centreX + (int)System.Math.Round(dir.X * outer), oz = centreZ + (int)System.Math.Round(dir.Z * outer);
        double outerY = RawSurfaceHeight(planet, w, ox, oz) + 5.0;
        double inner = System.Math.Min(hall.Rx, hall.Rz) * 0.6;
        double innerY = hall.Cy - hall.Ry * 0.6 + 5.0;
        return new[] { new TunnelSeg(dir.X * outer, outerY, dir.Z * outer, dir.X * inner, innerY, dir.Z * inner, 4.5) };
    }

    // ---------------- test seams ----------------

    /// <summary>The daylight halls of a world (tests): centre, half-axes, centre Y, lake Y (MinValue dry).</summary>
    internal System.Collections.Generic.List<(int X, int Z, double Rx, double Rz, double Ry, int Cy, int LakeY)> DaylightHallsForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int, int, double, double, double, int, int)>();
        var w = WonderFor(planet);
        if (!w.DaylightHalls)
        {
            return list;
        }

        double chance = System.Math.Min(0.95, HallChance * System.Math.Max(0.25, planet.SpectacleDensity));
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / HallCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / HallCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        for (int czI = 0; czI < nz; czI++)
            for (int cxI = 0; cxI < nx; cxI++)
            {
                int x = (int)(cxI * cw + cw * 0.5);
                int z = (int)(czI * ch + ch * 0.5) - period / 2;
                if (!TryGetHotspot(w.Seed ^ HallSalt, HallCellSize, chance, HallMargin, x, z, out _, out double dx, out double dz))
                {
                    continue;
                }

                int cx = WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference);
                int cz = WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference);
                if (TryGetDaylightHall(planet, w, cx, cz, out var hall, out _, out _))
                {
                    list.Add((cx, cz, hall.Rx, hall.Rz, hall.Ry, hall.Cy, hall.LakeY));
                }
            }

        return list;
    }
}
