// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Giant overhangs (#2339, terrain generation 21). The <b>wave rock</b> is an oriented ridge (the arête frame — a
/// bearing from the eight, half-length 30–65, half-width 4–8, rise 12–25) whose crest curls over one side: on that
/// side the wall is sheer and a <see cref="BandKind.Rock"/> band reaches 6–10 past it, 3–5 thick, drooping toward
/// its lip, with air under it. The <b>abri</b> (rock shelter) is a worm family riding the table mountain's own
/// cell: a quarter of the tables of arch country carry one short capsule along their wall foot, so the cliff is
/// undercut by four to six cells over twenty to forty — a place to camp under. Trig-free wave; the abri uses the
/// libm angle like the rock gates of its family. (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    // ================= wave rocks =================
    private const double WaveCellSize = 1100.0;
    private const double WaveChance = 0.3;
    private const long WaveSalt = 0x3A5E01;
    private const double WaveMaxHalfLen = 65.0;
    private const double WaveMargin = WaveMaxHalfLen + 8.0 + 10.0 + 4.0;

    private bool HasWaveRocks(PlanetType planet)
        => _terrainGeneration >= WorldDescription.SpectacleGeneration && !planet.Void && !planet.Cratered && !_crateredWorld
           && !planet.FloatingIslands && HasAir(planet) && planet.HasTag(TerrainTag.Arches)
           && (planet.HasTag(TerrainTag.Buttes) || planet.HasTag(TerrainTag.Wind));

    /// <summary>The wave's frame at a column: the along / across coordinates, its rolls, and whether the column lies
    /// within the ridge's length. <paramref name="across"/> is positive on the curl side.</summary>
    private bool TryGetWave(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out double along, out double across, out double halfLen, out double halfWidth, out double rise, out double reach, out int thick)
    {
        along = across = halfLen = halfWidth = rise = reach = 0.0;
        thick = 0;
        var (cell, chance) = SpectacleCell(planet, WaveCellSize, WaveChance);
        if (!TryGetHotspot(w.Seed ^ WaveSalt, cell, chance, WaveMargin, worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        int dir = (int)((h >> 16) & 0x7);
        halfLen = 30.0 + ((h >> 20) & 0x3FF) / 1023.0 * (WaveMaxHalfLen - 30.0);
        halfWidth = 4.0 + ((h >> 30) & 0xFF) / 255.0 * 4.0;
        rise = 12.0 + ((h >> 38) & 0x3FF) / 1023.0 * 13.0;
        reach = 6.0 + ((h >> 48) & 0xF) / 15.0 * 4.0;
        thick = 3 + (int)((h >> 52) & 0x3) % 3;
        AlongAcross(dx, dz, EightDirs[dir], out along, out across);
        return System.Math.Abs(along) <= halfLen;
    }

    private static double WaveTaper(double along, double halfLen)
    {
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        return endT >= 0.2 ? 1.0 : Smooth01(endT / 0.2);
    }

    /// <summary>The ridge's rise at a column (the row): a gentle flank on one side, a sheer wall on the curl side.</summary>
    private double WaveRockOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetWave(planet, w, worldX, worldZ, out double along, out double across, out double halfLen, out double halfWidth, out double rise, out _, out _)
            || System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        double wSpan = System.Math.Abs(across) / halfWidth;
        double flank = across > 0.0
            ? (wSpan <= 0.8 ? 1.0 : Smooth01((1.0 - wSpan) / 0.2))   // the curl side: the crest runs to the edge, then drops sheer
            : (wSpan <= 0.35 ? 1.0 : Smooth01((1.0 - wSpan) / 0.65)); // the back: a gentle flank
        return rise * flank * WaveTaper(along, halfLen);
    }

    /// <summary>The curl: a rock band past the sheer wall at crest height, drooping toward its lip, air below.</summary>
    private int AppendWaveBands(PlanetType planet, WonderProfile w, int worldX, int worldZ, System.Span<ColumnBand> bands, int n)
    {
        if (!TryGetWave(planet, w, worldX, worldZ, out double along, out double across, out double halfLen, out double halfWidth, out double rise, out double reach, out int thick)
            || across <= halfWidth || across > halfWidth + reach)
        {
            return n;
        }

        double taper = WaveTaper(along, halfLen);
        if (taper < 0.5)
        {
            return n; // the curl ends before the ridge does
        }

        int crestTop = RawSurfaceHeight(planet, w, worldX, worldZ) + (int)System.Math.Round(rise * taper);
        int droop = (int)((across - halfWidth) / reach * 3.0);
        int top = crestTop - droop;
        bands[n++] = new ColumnBand { Bottom = top - thick + 1, Top = top, Kind = BandKind.Rock, Material = w.BandMaterial };
        return n;
    }

    // ================= abris (rock shelters) =================

    /// <summary>One capsule along the wall foot of a quarter of the tables: the cliff undercut by its radius.</summary>
    private TunnelSeg[] AbriSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        ulong g = h * 0x9E3779B97F4A7C15UL;
        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ButteMaxRadius - 40.0);
        if (radius < 50.0 || ((g >> 30) & 0x3) != 0)
        {
            return System.Array.Empty<TunnelSeg>();
        }

        double angle = ((g >> 34) & 0x3FF) / 1023.0 * System.Math.PI * 2.0;
        double len = 20.0 + ((g >> 44) & 0xFF) / 255.0 * 20.0;
        double r = 4.0 + ((g >> 52) & 0x3) * 0.67;
        double foot = radius * 0.92;
        double sweep = len / foot;
        double y = RawSurfaceHeight(planet, w, centreX, centreZ) + r + 1.0; // the floor stays: the capsule's bottom is the foot
        double x0 = System.Math.Cos(angle) * foot, z0 = System.Math.Sin(angle) * foot;
        double x1 = System.Math.Cos(angle + sweep) * foot, z1 = System.Math.Sin(angle + sweep) * foot;
        return new[] { new TunnelSeg(x0, y, z0, x1, y, z1, r) };
    }

    // ---------------- test seams ----------------

    /// <summary>The wave rocks of a world (tests): centre, bearing, half-length, half-width, rise, reach.</summary>
    internal System.Collections.Generic.List<(int X, int Z, int Dir, double HalfLen, double HalfWidth, double Rise, double Reach)> WaveRocksForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int, int, int, double, double, double, double)>();
        var w = WonderFor(planet);
        if (!w.WaveRocks)
        {
            return list;
        }

        var (cell, chance) = SpectacleCell(planet, WaveCellSize, WaveChance);
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
                if (!TryGetHotspot(w.Seed ^ WaveSalt, cell, chance, WaveMargin, x, z, out ulong h, out double dx, out double dz))
                {
                    continue;
                }

                list.Add((WorldConstants.WrapX(x - (int)System.Math.Round(dx), _circumference), WorldConstants.WrapZ(z - (int)System.Math.Round(dz), _circumference),
                    (int)((h >> 16) & 0x7), 30.0 + ((h >> 20) & 0x3FF) / 1023.0 * (WaveMaxHalfLen - 30.0), 4.0 + ((h >> 30) & 0xFF) / 255.0 * 4.0,
                    12.0 + ((h >> 38) & 0x3FF) / 1023.0 * 13.0, 6.0 + ((h >> 48) & 0xF) / 15.0 * 4.0));
            }

        return list;
    }
}
