// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 7 — ice as a VOLUME. A glacier is a tongue of ice 12–30 thick lying on
/// the ground (an offset row for the rise, the ice paint filling down to the old ground — part 1's paint fill),
/// slit by its own crevasses, stepped into decks where the ground under it drops (an icefall), walled by
/// moraines of scree along its flanks and at its snout, hollowed by ice caves and a glacier gate (two worm
/// families riding the glacier's own hotspot cell). An ice sheet is a broad cap of ice on the coldest glacial
/// worlds through which the massifs poke as nunataks (a matter of row precedence). Hanging valleys are side
/// troughs entering a glacial trough with their floors above its floor. Ice-surface worlds get shallow ice
/// caves of their own. Icebergs (part 1) now keep off the landing pads.</summary>
public sealed partial class WorldGenerator
{
    // ================= gates =================
    private bool HasGlaciers(PlanetType planet)
        => HasMassifs(planet) && (planet.HasTag(TerrainTag.Glacial) || planet.BaseTemperature <= -5.0);

    private bool HasIceSheets(PlanetType planet)
        => HasMassifs(planet) && planet.HasTag(TerrainTag.Glacial) && planet.BaseTemperature <= -20.0;

    private bool HasHangingValleys(PlanetType planet) => HasGlacialTroughs(planet);

    private static bool IceSurface(PlanetType planet)
        => string.Equals(planet.SurfaceBlock, "ice", System.StringComparison.OrdinalIgnoreCase);

    private bool HasIceCaves(PlanetType planet) => HasGlaciers(planet);

    /// <summary>Shallow ice caves through the crust of an ice-surface world (ice, glacier), glacier or not.</summary>
    private bool HasSheetCaves(PlanetType planet) => HasMassifs(planet) && IceSurface(planet);

    /// <summary>True where an ice sheet or a glacier tongue covers the column: the cold ground patterns of parts 4
    /// and 5 (frost polygons, thaw ponds) yield to the ice there — their one-block heave would otherwise fire
    /// first in the table and leave a pit in the ice where the sheet's cap did not.</summary>
    private bool IceCoveredAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => (w.IceSheets && IceSheetOffset(w, worldX, worldZ) > 0.0)
           || (w.Glaciers && GlacierAt(planet, w, worldX, worldZ, out bool ice) > 0.0 && ice);

    // ================= glaciers =================
    private const double GlacierCellSize = 1100.0;
    private const double GlacierChance = 0.35;
    private const long GlacierSalt = 0x1CE601;
    private const long GlacierSlitSalt = 0x1CE602;
    private const long GlacierDirSalt = 0x1CE603;
    private const double GlacierMaxLen = 400.0;
    private const double GlacierMaxHalf = 35.0;
    private const double MoraineWidth = 8.0;
    private const double MoraineSnoutLen = 10.0;

    /// <summary>The glacier a column lies on or beside. The tongue starts at the hotspot centre (its head) and
    /// runs down the steepest descent of the raw ground for 150–400 blocks, 30–70 wide, 12–30 thick at the
    /// crown. Outputs the along / across coordinates, the rolled dimensions and the direction. The direction
    /// (eight probes of the raw ground, forty out) is the one cell roll that costs something, so it is memoised
    /// in the sea-cell memo outside calibration.</summary>
    private bool TryGetGlacier(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out double along, out double across, out double len, out double half, out double thick, out (double X, double Z) dir, out ulong hash)
    {
        along = across = len = half = thick = 0.0;
        dir = EightDirs[0];
        if (!TryGetHotspot(w.Seed ^ GlacierSalt, GlacierCellSize, GlacierChance, GlacierMaxLen + GlacierMaxHalf + MoraineWidth + 8.0,
                worldX, worldZ, out hash, out double dx, out double dz))
        {
            return false;
        }

        len = 150.0 + ((hash >> 16) & 0x3FF) / 1023.0 * (GlacierMaxLen - 150.0);
        half = 15.0 + ((hash >> 26) & 0x3FF) / 1023.0 * (GlacierMaxHalf - 15.0);
        thick = 12.0 + ((hash >> 36) & 0x3FF) / 1023.0 * 18.0;
        double reach = len + MoraineSnoutLen + half + MoraineWidth;
        if (dx * dx + dz * dz > reach * reach)
        {
            return false;
        }

        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, GlacierDirSalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int rawC = RawSurfaceHeight(planet, w, cx, cz);
            int dirIdx = SteepestDescent(planet, w, cx, cz, 40, out int lowest);
            cell = (lowest <= rawC - 4, dirIdx, 0.0); // a glacier needs a slope to flow down
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return false;
        }

        dir = EightDirs[(int)cell.A];
        AlongAcross(dx, dz, dir, out along, out across);
        return true;
    }

    /// <summary>What the glacier does to a column: the ice rise on the tongue (crevassed, decked in an
    /// icefall) or the moraine's rise beside and beyond it. <paramref name="ice"/> says which.</summary>
    private double GlacierAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, out bool ice)
    {
        ice = false;
        if (!TryGetGlacier(planet, w, worldX, worldZ, out double along, out double across, out double len, out double half, out double thick, out var dir, out ulong h))
        {
            return 0.0;
        }

        double a = System.Math.Abs(across);
        if (along >= 0.0 && along <= len && a <= half)
        {
            // The tongue: a lens across, ramped at the head, tapering to the snout.
            double lens = 1.0 - (across / half) * (across / half);
            double headT = along < 40.0 ? Smooth01(along / 40.0) : 1.0;
            double snoutT = along > len - 40.0 ? Smooth01((len - along) / 40.0) : 1.0;
            double t = thick * lens * headT * snoutT;
            if (t < 1.0)
            {
                return 0.0;
            }

            // An icefall: where the ground under the tongue drops fifteen or more over sixteen blocks along it,
            // the ice breaks into three-block decks and the crevasses come three times as dense.
            int ahead = RawSurfaceHeight(planet, w, worldX + (int)System.Math.Round(dir.X * 8.0), worldZ + (int)System.Math.Round(dir.Z * 8.0));
            int behind = RawSurfaceHeight(planet, w, worldX - (int)System.Math.Round(dir.X * 8.0), worldZ - (int)System.Math.Round(dir.Z * 8.0));
            bool icefall = behind - ahead >= 15;
            if (icefall)
            {
                t = System.Math.Max(3.0, System.Math.Floor(t / 3.0) * 3.0);
            }

            // Crevasses: the slits of a ridged field, narrow lines cut a few blocks into the ice.
            double slit = 1.0 - System.Math.Abs(2.0 * FbmT(w.Seed + GlacierSlitSalt, worldX, worldZ, 12.0, octaves: 2) - 1.0);
            double bar = icefall ? 0.80 : 0.90;
            if (slit > bar)
            {
                double cut = System.Math.Min(t - 1.0, 6.0 * (slit - bar) / (1.0 - bar) + 2.0);
                t -= cut;
            }

            ice = true;
            return t;
        }

        // Moraines: a scree ridge 4–10 high along both flanks and across the snout.
        double ridge = 4.0 + ((h >> 46) & 0x3FF) / 1023.0 * 6.0;
        if (along >= 0.0 && along <= len && a > half && a <= half + MoraineWidth)
        {
            double u = (a - half - MoraineWidth / 2.0) / (MoraineWidth / 2.0);
            double headT = along < 40.0 ? Smooth01(along / 40.0) : 1.0;
            return ridge * (1.0 - u * u) * headT;
        }

        if (along > len && along <= len + MoraineSnoutLen && a <= half + MoraineWidth / 2.0)
        {
            double u = (along - len - MoraineSnoutLen / 2.0) / (MoraineSnoutLen / 2.0);
            return ridge * (1.0 - u * u);
        }

        return 0.0;
    }

    private double GlacierOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => GlacierAt(planet, w, worldX, worldZ, out _);

    /// <summary>Ice through the whole tongue down to the old ground; scree on a moraine.</summary>
    private BlockId? GlacierPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        double rise = GlacierAt(planet, w, worldX, worldZ, out bool ice);
        if (rise <= 0.0)
        {
            return null;
        }

        if (ice)
        {
            var iceId = _content.GetBlock("ice")?.NumericId ?? BlockId.Air;
            if (iceId.IsAir)
            {
                return null;
            }

            fillToY = surfaceY - (int)System.Math.Round(rise) + 1; // the ice sits ON the old ground
            return iceId;
        }

        var scree = _content.GetBlock("scree")?.NumericId ?? BlockId.Air;
        if (scree.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - (int)System.Math.Round(rise) + 1;
        return scree;
    }

    // ================= glacier gates and ice caves (worm families on the glacier cell) =================
    private const long GlacierGateSalt = 0x1CE604;
    private const long IceCaveSalt = 0x1CE605;
    private const long SheetCaveSalt = 0x1CE606;
    private const double SheetCaveCellSize = 1000.0;
    private const double SheetCaveChance = 0.40;
    private const double SheetCaveMargin = 220.0;

    private readonly System.Collections.Generic.Dictionary<(long, ulong), TunnelSeg[]> _iceSegCache = new();
    private readonly object _iceSegLock = new();

    /// <summary>The glacier's direction and dimensions for a cell whose centre is known (the worm families): the
    /// same rolls <see cref="TryGetGlacier"/> makes, without the column.</summary>
    private bool GlacierCellFor(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ,
        out double len, out double half, out double thick, out (double X, double Z) dir)
    {
        len = 150.0 + ((h >> 16) & 0x3FF) / 1023.0 * (GlacierMaxLen - 150.0);
        half = 15.0 + ((h >> 26) & 0x3FF) / 1023.0 * (GlacierMaxHalf - 15.0);
        thick = 12.0 + ((h >> 36) & 0x3FF) / 1023.0 * 18.0;
        int rawC = RawSurfaceHeight(planet, w, centreX, centreZ);
        int dirIdx = SteepestDescent(planet, w, centreX, centreZ, 40, out int lowest);
        dir = EightDirs[dirIdx];
        return lowest <= rawC - 4;
    }

    /// <summary>The glacier gate: one horizontal worm 3–5 in radius from just inside the snout 30–60 back into
    /// the ice at the old ground's level — the cave a meltwater river would leave.</summary>
    private TunnelSeg[] GlacierGateSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        var key = (w.Seed ^ GlacierGateSalt, h);
        lock (_iceSegLock)
        {
            if (_iceSegCache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var segs = System.Array.Empty<TunnelSeg>();
        if (GlacierCellFor(planet, w, h, centreX, centreZ, out double len, out double half, out double thick, out var dir))
        {
            double depth = 30.0 + ((h >> 50) & 0x3FF) / 1023.0 * 30.0;
            double r = 3.0 + ((h >> 60) & 0x3);
            double sx = dir.X * (len - 6.0), sz = dir.Z * (len - 6.0);
            double ground = RawSurfaceHeight(planet, w, centreX + (int)System.Math.Round(sx), centreZ + (int)System.Math.Round(sz));
            double y = ground + r + 1.0;
            segs = new[] { new TunnelSeg(sx, y, sz, sx - dir.X * depth, y, sz - dir.Z * depth, r) };
        }

        lock (_iceSegLock)
        {
            _iceSegCache[key] = segs;
        }

        return segs;
    }

    /// <summary>Ice caves inside the tongue: 3–5 worm segments along it at half the ice thickness, radius 2–4.
    /// The walls are ice because the fill is ice.</summary>
    private TunnelSeg[] IceCaveSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        var key = (w.Seed ^ IceCaveSalt, h);
        lock (_iceSegLock)
        {
            if (_iceSegCache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var segs = System.Array.Empty<TunnelSeg>();
        if (GlacierCellFor(planet, w, h, centreX, centreZ, out double len, out double half, out double thick, out var dir))
        {
            ulong s = (h ^ (ulong)IceCaveSalt) | 1UL;
            double Next()
            {
                s ^= s << 13;
                s ^= s >> 7;
                s ^= s << 17;
                return (s & 0xFFFFF) / 1048576.0;
            }

            int count = 3 + (int)(Next() * 3);
            var list = new System.Collections.Generic.List<TunnelSeg>(count);
            double along = 50.0 + Next() * 40.0;
            double across = (Next() - 0.5) * half;
            for (int i = 0; i < count && along + 30.0 < len - 30.0; i++)
            {
                double step = 25.0 + Next() * 25.0;
                double across2 = System.Math.Clamp(across + (Next() - 0.5) * half * 0.6, -half * 0.6, half * 0.6);
                double x0 = dir.X * along - dir.Z * across, z0 = dir.Z * along + dir.X * across;
                double x1 = dir.X * (along + step) - dir.Z * across2, z1 = dir.Z * (along + step) + dir.X * across2;
                double g0 = RawSurfaceHeight(planet, w, centreX + (int)System.Math.Round(x0), centreZ + (int)System.Math.Round(z0));
                double g1 = RawSurfaceHeight(planet, w, centreX + (int)System.Math.Round(x1), centreZ + (int)System.Math.Round(z1));
                double r = 2.0 + Next() * 2.0;
                list.Add(new TunnelSeg(x0, g0 + thick * 0.5, z0, x1, g1 + thick * 0.5, z1, r));
                along += step;
                across = across2;
            }

            segs = list.ToArray();
        }

        lock (_iceSegLock)
        {
            _iceSegCache[key] = segs;
        }

        return segs;
    }

    /// <summary>Shallow ice caves on an ice-surface world: worms 4–6 segments long, radius 2–4, hanging 6–14
    /// under the raw ground of their cell centre instead of off BaseHeight — so they run through the ice crust.</summary>
    private TunnelSeg[] SheetCaveSegments(PlanetType planet, WonderProfile w, ulong h, int centreX, int centreZ)
    {
        var key = (w.Seed ^ SheetCaveSalt, h);
        lock (_iceSegLock)
        {
            if (_iceSegCache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        ulong s = (h ^ (ulong)SheetCaveSalt) | 1UL;
        double Next()
        {
            s ^= s << 13;
            s ^= s >> 7;
            s ^= s << 17;
            return (s & 0xFFFFF) / 1048576.0;
        }

        int count = 4 + (int)(Next() * 3);
        double py = RawSurfaceHeight(planet, w, centreX, centreZ) - 6.0 - Next() * 8.0;
        double px = 0.0, pz = 0.0;
        var d = DirFromBits(h >> 40);
        double vx = d.X, vz = d.Z;
        var list = new System.Collections.Generic.List<TunnelSeg>(count);
        for (int i = 0; i < count; i++)
        {
            double len = 20.0 + Next() * 20.0;
            int k = (int)(Next() * 7.0) - 3;
            double rl = System.Math.Sqrt(64.0 + k * k);
            double rc = 8.0 / rl, rs = k / rl;
            double nvx = vx * rc - vz * rs, nvz = vx * rs + vz * rc;
            vx = nvx;
            vz = nvz;
            double qx = System.Math.Clamp(px + vx * len, -SheetCaveMargin + 24.0, SheetCaveMargin - 24.0);
            double qz = System.Math.Clamp(pz + vz * len, -SheetCaveMargin + 24.0, SheetCaveMargin - 24.0);
            double qy = py + (Next() - 0.5) * 3.0;
            list.Add(new TunnelSeg(px, py, pz, qx, qy, qz, 2.0 + Next() * 2.0));
            px = qx;
            pz = qz;
            py = qy;
        }

        var segs = list.ToArray();
        lock (_iceSegLock)
        {
            _iceSegCache[key] = segs;
        }

        return segs;
    }

    // ================= ice sheets and nunataks =================
    private const long IceSheetSalt = 0x1CE607;

    /// <summary>A broad cap of ice 10–25 thick across a region of the coldest glacial worlds. The massif and
    /// trough rows come first in the table, so a massif inside the region keeps its bare rock — the nunatak.</summary>
    private double IceSheetOffset(WonderProfile w, int worldX, int worldZ)
    {
        double m = FbmT(w.Seed + IceSheetSalt, worldX, worldZ, 900.0, octaves: 2);
        if (m <= 0.60)
        {
            return 0.0;
        }

        return 10.0 + 15.0 * Smooth01((m - 0.60) / 0.15);
    }

    private BlockId? IceSheetPaint(WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        double cap = IceSheetOffset(w, worldX, worldZ);
        if (cap <= 0.0)
        {
            return null;
        }

        var iceId = _content.GetBlock("ice")?.NumericId ?? BlockId.Air;
        if (iceId.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - (int)System.Math.Round(cap) + 1;
        return iceId;
    }

    // ================= hanging valleys =================

    /// <summary>A side trough entering the glacial trough at a right angle: 80–160 long, 30–50 wide, a U 15–25
    /// deep — shallower than the main trough (25–45), so its floor hangs above the main floor and ends at the
    /// trough wall. Rides the trough's own hotspot cell and its rolled angle (the trough uses the libm angle
    /// too, so the two stay aligned); only ever outside the main trough's half-width, where the trough row has
    /// nothing.</summary>
    private double HangingValleyOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x61AC1A1, TroughCellSize, TroughChance, TroughMaxHalfLen + TroughMaxHalfWidth + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 200.0 + ((h >> 26) & 0x3FF) / 1023.0 * (TroughMaxHalfLen - 200.0);
        double halfWidth = 25.0 + ((h >> 56) & 0xFF) / 255.0 * (TroughMaxHalfWidth - 25.0);
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        double across = -dx * sin + dz * cos;

        ulong h3 = h * 0xD1B54A32D192ED03UL;
        double at = (0.3 + ((h3 >> 8) & 0xFF) / 255.0 * 0.3) * halfLen * (((h3 >> 16) & 1UL) == 0 ? 1.0 : -1.0);
        double side = ((h3 >> 17) & 1UL) == 0 ? 1.0 : -1.0;
        double sideLen = 80.0 + ((h3 >> 18) & 0x3FF) / 1023.0 * 80.0;
        double sideHalf = 15.0 + ((h3 >> 28) & 0xFF) / 255.0 * 10.0;
        double depth = 15.0 + ((h3 >> 36) & 0x3FF) / 1023.0 * 10.0;

        double u = (along - at) / sideHalf;             // across the side valley
        double v = (across * side - halfWidth) / sideLen; // along it, 0 at the main trough's wall, 1 at its head
        if (System.Math.Abs(u) > 1.0 || v < 0.0 || v > 1.0)
        {
            return 0.0;
        }

        double wall = 1.0 - u * u;
        double head = v > 0.8 ? Smooth01((1.0 - v) / 0.2) : 1.0;
        return -depth * wall * head;
    }

    // ================= test hooks =================

    /// <summary>The part-7 land rows by name (tests): glacier, ice-sheet, hanging-valley.</summary>
    internal double IceOffsetForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return name switch
        {
            "glacier" => w.Glaciers ? GlacierOffset(planet, w, worldX, worldZ) : 0.0,
            "ice-sheet" => w.IceSheets ? IceSheetOffset(w, worldX, worldZ) : 0.0,
            "hanging-valley" => w.HangingValleys ? HangingValleyOffset(w.Seed, worldX, worldZ) : 0.0,
            _ => throw new System.ArgumentException(name, nameof(name)),
        };
    }

    /// <summary>The glacier at a column (tests): the rise and whether it is ice (else a moraine), or null.</summary>
    internal (double Rise, bool Ice)? GlacierForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (!w.Glaciers)
        {
            return null;
        }

        double rise = GlacierAt(planet, w, worldX, worldZ, out bool ice);
        return rise > 0.0 ? (rise, ice) : null;
    }
}
