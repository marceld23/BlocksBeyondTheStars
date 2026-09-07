// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 6 — the coast and the sea floor. Every family here is SEA-RELATIVE: it
/// needs the sea level, so it runs as a sea-relative landmark row (never read while calibrating) or as a band /
/// column feature resolved after calibration. Sea arches (a rock bar from a cliff to a stem out in the water),
/// blowholes (a vent on a cliff top over a sealed water shaft), causeway islands (an islet joined to the coast
/// by a wadable sandbar — the tidal island at permanent low tide), lagoons and atolls (reef rings of coral rock,
/// the atoll with sand islets), reef fields (bumpy coral-rock shallows), blue holes (a shaft in the shallows),
/// submarine canyons (a V cut seaward from the shelf) and trenches (one great gash in the deep sea). All
/// geometry is trig-free.</summary>
public sealed partial class WorldGenerator
{
    // ================= shared helpers =================
    private const double Diag = 0.7071067811865476; // 1/√2, a compile-time constant — no libm at runtime
    private static readonly (double X, double Z)[] EightDirs =
    {
        (1.0, 0.0), (Diag, Diag), (0.0, 1.0), (-Diag, Diag), (-1.0, 0.0), (-Diag, -Diag), (0.0, -1.0), (Diag, -Diag),
    };

    /// <summary>The direction of steepest descent of the RAW ground around (cx, cz), probed at eight points
    /// <paramref name="step"/> away. Returns the index into <see cref="EightDirs"/> and the lowest probe.</summary>
    private int SteepestDescent(PlanetType planet, WonderProfile w, int cx, int cz, int step, out int lowest)
    {
        int best = 0;
        lowest = int.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            int px = cx + (int)System.Math.Round(EightDirs[i].X * step);
            int pz = cz + (int)System.Math.Round(EightDirs[i].Z * step);
            int h = RawSurfaceHeight(planet, w, px, pz);
            if (h < lowest)
            {
                lowest = h;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The direction of steepest ASCENT (the nearest high ground), same probing.</summary>
    private int SteepestAscent(PlanetType planet, WonderProfile w, int cx, int cz, int step, out int highest)
    {
        int best = 0;
        highest = int.MinValue;
        for (int i = 0; i < 8; i++)
        {
            int px = cx + (int)System.Math.Round(EightDirs[i].X * step);
            int pz = cz + (int)System.Math.Round(EightDirs[i].Z * step);
            int h = RawSurfaceHeight(planet, w, px, pz);
            if (h > highest)
            {
                highest = h;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Along / across coordinates of (dx, dz) relative to a unit direction.</summary>
    private static void AlongAcross(double dx, double dz, (double X, double Z) dir, out double along, out double across)
    {
        along = dx * dir.X + dz * dir.Z;
        across = -dx * dir.Z + dz * dir.X;
    }

    private bool WetLandWorld(PlanetType planet, double minWater)
        => HasMassifs(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= minWater && (planet.LavaAbundance ?? 0.0) <= 0.0;

    // ================= gates =================
    private bool HasSeaArches(PlanetType planet) => HasSeaStacks(planet);
    private bool HasBlowholes(PlanetType planet) => HasSeaStacks(planet);
    private bool HasCausewayIslands(PlanetType planet) => WetLandWorld(planet, 0.6);
    private bool HasReefRings(PlanetType planet) => WetLandWorld(planet, 0.4) && planet.HasTag(TerrainTag.Reef) && planet.BaseTemperature >= 18.0;
    private bool HasReefFields(PlanetType planet) => HasReefRings(planet);
    private bool HasBlueHoles(PlanetType planet) => WetLandWorld(planet, 0.5) && (planet.HasTag(TerrainTag.Reef) || planet.HasTag(TerrainTag.Karst));
    private bool HasSubmarineCanyons(PlanetType planet) => WetLandWorld(planet, 0.6);
    private bool HasTrenches(PlanetType planet) => WetLandWorld(planet, 0.8);

    // ================= sea arches =================
    private const double SeaArchCellSize = 520.0;
    private const double SeaArchChance = 0.35;
    private const long SeaArchSalt = 0xA2C401;
    private const double ArchStemR = 2.5;

    /// <summary>The arch a column belongs to, if any: the cliff-top anchor, the bar's thickness, the bar's
    /// length and direction, and the column's along / across coordinates from the cliff foot. Rolled once per
    /// hotspot cell (anchor + direction + length memoised in the sea-cell memo, never while calibrating).</summary>
    private bool TryGetSeaArch(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out int anchor, out int thick, out double len, out (double X, double Z) dir, out double along, out double across)
    {
        anchor = thick = 0;
        len = along = across = 0.0;
        dir = EightDirs[0];
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || !TryGetHotspot(w.Seed ^ SeaArchSalt, SeaArchCellSize, SeaArchChance, 24.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        if (dx * dx + dz * dz > 24.0 * 24.0)
        {
            return false; // far from the arch: no probes
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, SeaArchSalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int raw = RawSurfaceHeight(planet, w, cx, cz);
            bool has = raw >= sea + 6 && raw <= sea + 22;
            int dirIdx = 0;
            double l = 9.0 + ((h >> 16) & 0xFF) / 255.0 * 5.0; // 9..14
            if (has)
            {
                dirIdx = SteepestDescent(planet, w, cx, cz, 12, out int lowest);
                int sx = cx + (int)System.Math.Round(EightDirs[dirIdx].X * l);
                int sz = cz + (int)System.Math.Round(EightDirs[dirIdx].Z * l);
                has = lowest <= sea - 1 && RawSurfaceHeight(planet, w, sx, sz) <= sea - 1;
            }

            cell = (has, raw, dirIdx * 100.0 + l);
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return false;
        }

        anchor = (int)cell.A;
        int di = (int)(cell.B / 100.0);
        len = cell.B - di * 100.0;
        dir = EightDirs[di];
        thick = 3 + (int)((h >> 24) & 0x3) % 3; // 3..5
        AlongAcross(dx, dz, dir, out along, out across);
        return true;
    }

    /// <summary>The bar: a rock slab of the arch's thickness from the cliff foot out to the stem.</summary>
    private bool TryGetSeaArchBand(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (_calibrating || !TryGetSeaArch(planet, w, worldX, worldZ, out int anchor, out int thick, out double len, out _, out double along, out double across)
            || along < 0.0 || along > len + ArchStemR || System.Math.Abs(across) > 2.0)
        {
            return false;
        }

        top = anchor + 1;
        bottom = top - thick + 1;
        return true;
    }

    /// <summary>The stem out in the water: a pillar from the sea floor up to the bar's underside (a sea-relative row).</summary>
    private double SeaArchOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetSeaArch(planet, w, worldX, worldZ, out int anchor, out int thick, out double len, out _, out double along, out double across))
        {
            return 0.0;
        }

        double ex = along - len, ez = across;
        if (ex * ex + ez * ez > ArchStemR * ArchStemR)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        int target = anchor + 1 - thick; // the cell under the bar's underside is the pillar top
        return target > raw ? target - raw : 0.0;
    }

    // ================= blowholes =================
    private const double BlowholeCellSize = 300.0;
    private const double BlowholeChance = 0.30;
    private const long BlowholeSalt = 0xB10B01;

    /// <summary>The blowhole's vent column: a cliff-top hotspot centre (raw ground 5–20 above the sea, the sea
    /// within eight blocks). The vent block is the surface cell; the shaft below it is water down to the sea line.</summary>
    private bool BlowholeAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || _calibrating || !TryGetHotspot(w.Seed ^ BlowholeSalt, BlowholeCellSize, BlowholeChance, 10.0,
                worldX, worldZ, out _, out double dx, out double dz))
        {
            return false;
        }

        if ((int)System.Math.Round(dx) != 0 || (int)System.Math.Round(dz) != 0)
        {
            return false; // the vent is the centre column alone
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw < sea + 5 || raw > sea + 20)
        {
            return false;
        }

        SteepestDescent(planet, w, worldX, worldZ, 8, out int lowest);
        return lowest <= sea - 1;
    }

    // ================= causeway islands =================
    private const double CausewayCellSize = 900.0;
    private const double CausewayChance = 0.30;
    private const long CausewaySalt = 0xCA05E1;
    private const int CausewayReach = 60;

    /// <summary>The islet + sandbar covering a column. The islet stands 40–90 out from a coast (the centre must
    /// be shallow sea with land within reach in some direction); its top is 2–4 above the sea and its sandbar
    /// runs to the coast at one below the sea — wadable, the tidal island at permanent low tide.</summary>
    private double CausewayIslandOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ, out bool land)
    {
        land = false;
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || !TryGetHotspot(w.Seed ^ CausewaySalt, CausewayCellSize, CausewayChance, CausewayReach + 36.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double reach = CausewayReach + 32.0;
        if (dx * dx + dz * dz > reach * reach)
        {
            return 0.0;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, CausewaySalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int raw = RawSurfaceHeight(planet, w, cx, cz);
            bool has = raw <= sea - 3 && raw >= sea - 12;
            int dirIdx = 0;
            if (has)
            {
                dirIdx = SteepestAscent(planet, w, cx, cz, CausewayReach, out int highest);
                has = highest >= sea + 1;
            }

            cell = (has, dirIdx, 0.0);
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return 0.0;
        }

        double radius = 12.0 + ((h >> 16) & 0x3FF) / 1023.0 * 18.0; // 12..30
        int hTop = 2 + (int)((h >> 26) & 0x3) % 3;                    // 2..4
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        int rawHere = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (dist < radius)
        {
            double u = dist / radius;
            double target = sea - 1 + (hTop + 1) * (1.0 - u * u);
            double rise = target > rawHere ? target - rawHere : 0.0;
            land = rawHere + System.Math.Round(rise) >= sea; // what the integer height will be, not the real target
            return rise;
        }

        // The sandbar: a capsule from the islet's edge to the coast, one below the sea.
        var dir = EightDirs[(int)cell.A];
        AlongAcross(dx, dz, dir, out double along, out double across);
        double halfWidth = 2.0 + ((h >> 30) & 0x1);
        if (along < radius * 0.8 || along > CausewayReach || System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        int bar = sea - 1;
        return bar > rawHere ? bar - rawHere : 0.0;
    }

    // ================= lagoons and atolls =================
    private const double ReefRingCellSize = 1000.0;
    private const double ReefRingChance = 0.35;
    private const long ReefRingSalt = 0x2EEF01;
    private const double ReefRingMaxRadius = 110.0;
    private const double ReefRimHalf = 3.0;

    /// <summary>The reef ring covering a column: a lagoon (a ring in the shallows, its rim at one below the sea,
    /// its interior deepened to 3–6 below) or an atoll (the same ring in deep water with 3–6 sand islets on the
    /// rim and the interior 8–15 below). One or two gaps in the rim are the passes. Outputs the kind and what
    /// this column is: rim, islet (with its target height), or interior.</summary>
    private bool TryGetReefRing(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out bool atoll, out bool rim, out bool islet, out double target)
    {
        atoll = rim = islet = false;
        target = 0.0;
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || !TryGetHotspot(w.Seed ^ ReefRingSalt, ReefRingCellSize, ReefRingChance, ReefRingMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ReefRingMaxRadius - 40.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius + ReefRimHalf + 10.0)
        {
            return false;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, ReefRingSalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int raw = RawSurfaceHeight(planet, w, cx, cz);
            bool has = raw <= sea - 2 && raw >= sea - 40;
            cell = (has, raw <= sea - 12 ? 1.0 : 0.0, 0.0);
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return false;
        }

        atoll = cell.A > 0.5;
        double u = dist / radius;

        // Passes: one or two gap directions, a ~14° cone each (dot > 0.97), trig-free.
        int gaps = 1 + (int)((h >> 26) & 0x1);
        bool inGap = false;
        for (int g = 0; g < gaps && !inGap && dist > 0.0; g++)
        {
            var gd = DirFromBits(h >> (28 + g * 7));
            double dot = (dx * gd.X + dz * gd.Z) / dist;
            inGap = dot > 0.97;
        }

        if (System.Math.Abs(dist - radius) <= ReefRimHalf)
        {
            if (inGap)
            {
                return false; // the pass: the sea floor stays what it was
            }

            rim = true;
            target = sea - 1;
            if (atoll)
            {
                // Islets on the rim: 3–6 of them at hash-drawn bearings, sand domes 2–5 above the sea.
                int count = 3 + (int)((h >> 42) & 0x3);
                for (int k = 0; k < count; k++)
                {
                    var bd = DirFromBits(h >> (44 + k * 3));
                    double ix = bd.X * radius, iz = bd.Z * radius;
                    double ex = dx - ix, ez = dz - iz;
                    double ir = 6.0 + ((h >> (50 + k)) & 0x3);
                    double q = (ex * ex + ez * ez) / (ir * ir);
                    if (q < 1.0)
                    {
                        islet = true;
                        rim = false;
                        int hTop = 2 + (int)((h >> (52 + k)) & 0x3);
                        target = sea - 1 + (hTop + 1) * (1.0 - q);
                        break;
                    }
                }
            }

            return true;
        }

        if (dist < radius - ReefRimHalf)
        {
            // The interior: a bowl deepening toward the centre.
            double inner = 1.0 - u;
            target = atoll ? sea - 8 - 7 * inner : sea - 3 - 3 * inner;
            return true;
        }

        return false;
    }

    private double ReefRingOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ, out bool land)
    {
        land = false;
        if (!TryGetReefRing(planet, w, worldX, worldZ, out _, out bool rim, out bool islet, out double target))
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (rim || islet)
        {
            double offset = target - raw; // the rim is raised OR cut to one below the sea; an islet rises
            land = islet && raw + System.Math.Round(offset) >= SeaLevel(planet); // the integer height, not the real target
            return offset;
        }

        return target < raw ? target - raw : 0.0; // the interior only ever deepens
    }

    /// <summary>Coral rock on the rim, sand on an islet.</summary>
    private BlockId? ReefRingPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!TryGetReefRing(planet, w, worldX, worldZ, out _, out bool rim, out bool islet, out _))
        {
            return null;
        }

        if (rim)
        {
            var coral = _content.GetBlock("coral_rock")?.NumericId ?? BlockId.Air;
            if (!coral.IsAir)
            {
                fillToY = surfaceY - 2;
                return coral;
            }

            return null;
        }

        if (islet)
        {
            var sand = _content.GetBlock("sand")?.NumericId ?? BlockId.Air;
            return sand.IsAir ? null : sand;
        }

        return null;
    }

    // ================= reef fields =================
    private const long ReefFieldSalt = 0x2EEF02;
    private const long ReefBumpSalt = 0x2EEF03;

    private bool ReefRegionAt(WonderProfile w, int worldX, int worldZ)
        => FbmT(w.Seed + ReefFieldSalt, worldX, worldZ, 260.0, octaves: 2) > 0.58;

    /// <summary>The bumpy coral shallows: inside the region, under shallow sea, the floor rises up to four in a
    /// ridged field but never above two below the sea.</summary>
    private double ReefFieldOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw > sea - 2 || raw < sea - 14 || !ReefRegionAt(w, worldX, worldZ))
        {
            return 0.0;
        }

        double ridged = 1.0 - System.Math.Abs(2.0 * FbmT(w.Seed + ReefBumpSalt, worldX, worldZ, 9.0, octaves: 2) - 1.0);
        int target = System.Math.Min(sea - 2, raw + (int)System.Math.Round(4.0 * ridged));
        return target > raw ? target - raw : 0.0;
    }

    /// <summary>Coral rock three deep across the reef field's floor.</summary>
    private BlockId? ReefFieldPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || surfaceY > sea - 2 || surfaceY < sea - 16 || !ReefRegionAt(w, worldX, worldZ))
        {
            return null;
        }

        var coral = _content.GetBlock("coral_rock")?.NumericId ?? BlockId.Air;
        if (coral.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - 3;
        return coral;
    }

    // ================= blue holes =================
    private const double BlueHoleCellSize = 600.0;
    private const double BlueHoleChance = 0.30;
    private const long BlueHoleSalt = 0xB10E01;

    /// <summary>A shaft in the shallows: 8–18 across, its floor 40–70 below the sea with near-vertical walls,
    /// and a lip ring raised to one below the sea around the mouth.</summary>
    private double BlueHoleOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || !TryGetHotspot(w.Seed ^ BlueHoleSalt, BlueHoleCellSize, BlueHoleChance, 22.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 8.0 + ((h >> 16) & 0x3FF) / 1023.0 * 10.0;
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius + 2.0)
        {
            return 0.0;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, BlueHoleSalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int rawC = RawSurfaceHeight(planet, w, cx, cz);
            cell = (rawC <= sea - 2 && rawC >= sea - 12, 0.0, 0.0);
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw > sea - 2)
        {
            return 0.0; // the hole and its lip live in the sea alone — a coast column keeps its ground
        }

        if (dist > radius)
        {
            int lip = sea - 1;
            return lip > raw ? lip - raw : 0.0;
        }

        int floor = System.Math.Max(planet.BaseHeight - 150, sea - 40 - (int)(((h >> 26) & 0x3FF) / 1023.0 * 30.0));
        double u = dist / radius;
        double target = floor + (raw - floor) * u * u * u * u; // steep walls, a flat floor
        return target < raw ? target - raw : 0.0;
    }

    // ================= submarine canyons =================
    private const double CanyonCellSize = 1400.0;
    private const double CanyonChance = 0.30;
    private const long CanyonSalt = 0x5CA401;
    private const double CanyonMaxLen = 400.0;
    private const double CanyonMaxHalf = 40.0;

    /// <summary>A V cut seaward from the shelf: 200–400 long from a shelf hotspot down the steepest descent,
    /// 40–80 wide, 30–60 deep, only ever under the sea (a column of land keeps its ground).</summary>
    private double SubmarineCanyonOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || !TryGetHotspot(w.Seed ^ CanyonSalt, CanyonCellSize, CanyonChance, CanyonMaxLen + CanyonMaxHalf + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double reach = CanyonMaxLen + CanyonMaxHalf;
        if (dx * dx + dz * dz > reach * reach)
        {
            return 0.0;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, CanyonSalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int rawC = RawSurfaceHeight(planet, w, cx, cz);
            bool has = rawC <= sea - 2 && rawC >= sea - 14;
            int dirIdx = 0;
            if (has)
            {
                dirIdx = SteepestDescent(planet, w, cx, cz, 24, out int lowest);
                has = lowest <= rawC - 2;
            }

            cell = (has, dirIdx, 0.0);
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return 0.0;
        }

        AlongAcross(dx, dz, EightDirs[(int)cell.A], out double along, out double across);
        double len = 200.0 + ((h >> 16) & 0x3FF) / 1023.0 * (CanyonMaxLen - 200.0);
        double half = 20.0 + ((h >> 26) & 0x3FF) / 1023.0 * (CanyonMaxHalf - 20.0);
        if (along < 0.0 || along > len || System.Math.Abs(across) > half)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw > sea - 2)
        {
            return 0.0; // never touches land
        }

        double depth = 30.0 + ((h >> 36) & 0x3FF) / 1023.0 * 30.0;
        double v = 1.0 - System.Math.Abs(across) / half;
        double endT = along < 30.0 ? Smooth01(along / 30.0) : along > len - 40.0 ? Smooth01((len - along) / 40.0) : 1.0;
        double cut = depth * v * endT;
        int floorCap = planet.BaseHeight - 150;
        if (raw - cut < floorCap)
        {
            cut = raw - floorCap;
        }

        return cut > 0.0 ? -cut : 0.0;
    }

    // ================= trenches =================
    private const double TrenchCellSize = 3000.0;
    private const double TrenchChance = 0.50;
    private const long TrenchSalt = 0x7E2C01;
    private const double TrenchMaxLen = 900.0;
    private const double TrenchMaxHalf = 35.0;

    /// <summary>One great gash in the deep sea: 400–900 long, 40–70 wide, 60–120 below the surrounding floor,
    /// only where the raw floor already lies 20 below the sea, and never below the lava table's safety line.</summary>
    private double TrenchOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || !TryGetHotspot(w.Seed ^ TrenchSalt, TrenchCellSize, TrenchChance, TrenchMaxLen / 2.0 + TrenchMaxHalf + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double reach = TrenchMaxLen / 2.0 + TrenchMaxHalf;
        if (dx * dx + dz * dz > reach * reach)
        {
            return 0.0;
        }

        AlongAcross(dx, dz, DirFromBits(h >> 16), out double along, out double across);
        double halfLen = (400.0 + ((h >> 24) & 0x3FF) / 1023.0 * (TrenchMaxLen - 400.0)) / 2.0;
        double half = 20.0 + ((h >> 34) & 0x3FF) / 1023.0 * (TrenchMaxHalf - 20.0);
        if (System.Math.Abs(along) > halfLen || System.Math.Abs(across) > half)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw > sea - 20)
        {
            return 0.0;
        }

        double depth = 60.0 + ((h >> 44) & 0x3FF) / 1023.0 * 60.0;
        double wall = System.Math.Abs(across) / half;
        double profile = wall < 0.6 ? 1.0 : Smooth01((1.0 - wall) / 0.4);
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        double cut = depth * profile * (endT < 0.15 ? Smooth01(endT / 0.15) : 1.0);
        int floorCap = planet.BaseHeight - 150;
        if (raw - cut < floorCap)
        {
            cut = raw - floorCap;
        }

        return cut > 0.0 ? -cut : 0.0;
    }

    // ================= test hooks =================

    /// <summary>The part-6 sea-relative rows by name (tests).</summary>
    internal double CoastOffsetForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return name switch
        {
            "sea-arch" => w.SeaArches ? SeaArchOffset(planet, w, worldX, worldZ) : 0.0,
            "causeway-island" => w.CausewayIslands ? CausewayIslandOffset(planet, w, worldX, worldZ, out _) : 0.0,
            "reef-ring" => w.ReefRings ? ReefRingOffset(planet, w, worldX, worldZ, out _) : 0.0,
            "reef-field" => w.ReefFields ? ReefFieldOffset(planet, w, worldX, worldZ) : 0.0,
            "blue-hole" => w.BlueHoles ? BlueHoleOffset(planet, w, worldX, worldZ) : 0.0,
            "submarine-canyon" => w.SubmarineCanyons ? SubmarineCanyonOffset(planet, w, worldX, worldZ) : 0.0,
            "trench" => w.Trenches ? TrenchOffset(planet, w, worldX, worldZ) : 0.0,
            _ => throw new System.ArgumentException(name, nameof(name)),
        };
    }

    /// <summary>True where a sea-relative row of this part deliberately makes LAND out of what the calibration saw
    /// as sea: a causeway islet, an atoll islet, an arch's stem (tests — the partition test's allow-list).</summary>
    internal bool SeaRowMakesLandForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (w.CausewayIslands && CausewayIslandOffset(planet, w, worldX, worldZ, out bool cl) > 0.0 && cl)
        {
            return true;
        }

        if (w.ReefRings && ReefRingOffset(planet, w, worldX, worldZ, out bool al) != 0.0 && al)
        {
            return true;
        }

        return w.SeaArches && SeaArchOffset(planet, w, worldX, worldZ) > 0.0;
    }

    /// <summary>The ground before any landmark row (tests): a sea-relative row's offset applies on top of this,
    /// and only where no classic row owns the column.</summary>
    internal int RawSurfaceHeightForTest(PlanetType planet, int worldX, int worldZ)
        => RawSurfaceHeight(planet, WonderFor(planet), worldX, worldZ);

    /// <summary>The reef ring at a column (tests): (atoll, rim, islet), or null.</summary>
    internal (bool Atoll, bool Rim, bool Islet)? ReefRingForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.ReefRings && TryGetReefRing(planet, w, worldX, worldZ, out bool atoll, out bool rim, out bool islet, out _)
            ? (atoll, rim, islet) : null;
    }

    /// <summary>The arch bar at a column (tests): (bottom, top), or null.</summary>
    internal (int Bottom, int Top)? SeaArchBandForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.SeaArches && TryGetSeaArchBand(planet, w, worldX, worldZ, out int b, out int t) ? (b, t) : null;
    }

    /// <summary>Whether a column is a blowhole vent (tests).</summary>
    internal bool BlowholeForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.Blowholes && BlowholeAt(planet, w, worldX, worldZ);
    }
}
