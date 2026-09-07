// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 1 — the sea-floor and open-water reference families: seamounts (the
/// first sea-relative landmark row) and icebergs (the first ice band). Both read the CALIBRATED sea level,
/// which the classic rows may not (the calibration samples them), so they follow the #1631 sea-mount rule:
/// nothing while <c>_calibrating</c>, memoised per hotspot cell afterwards, dropped with the column memos.
/// (partial of <see cref="WorldGenerator"/>)</summary>
public sealed partial class WorldGenerator
{
    // --- Generic per-cell memo for the sea-relative families. Keyed on the feature's centre (which
    // TryGetHotspot yields as an offset from the queried column), never written while calibrating. ---
    private readonly System.Collections.Generic.Dictionary<(string Planet, long Salt, int Cx, int Cz), (bool Has, double A, double B)> _seaCells = new();
    private readonly object _seaCellLock = new();
    private const int SeaCellCap = 4096;

    /// <summary>Test seam: skips every sea-relative row so a test can prove the calibration (sea level, height
    /// sample) is identical with and without them. Reset it in a <c>finally</c>.</summary>
    internal static bool DisableSeaRowsForTest;

    /// <summary>The calibration's whole-world height sample (tests — the sea-percentile invariance proof).</summary>
    internal int[] SortedHeightsForTest(PlanetType planet) => CalibFor(planet).SortedHeights;

    private bool TryGetSeaCell((string Planet, long Salt, int Cx, int Cz) key, out (bool Has, double A, double B) cell)
    {
        if (_calibrating)
        {
            cell = default;
            return false;
        }

        lock (_seaCellLock)
        {
            return _seaCells.TryGetValue(key, out cell);
        }
    }

    private void PutSeaCell((string Planet, long Salt, int Cx, int Cz) key, (bool Has, double A, double B) cell)
    {
        if (_calibrating)
        {
            return;
        }

        lock (_seaCellLock)
        {
            if (_seaCells.Count >= SeaCellCap)
            {
                _seaCells.Clear();
            }

            _seaCells[key] = cell;
        }
    }

    // ================= Seamounts =================
    // A cone on the sea floor whose summit stays under the surface — never an island (the volcano lift
    // already makes those). The reference consumer of the sea-relative landmark rows.
    private const double SeamountCellSize = 900.0;
    private const double SeamountChance = 0.35;
    private const double SeamountMaxRadius = 90.0;
    private const double SeamountMinRadius = 40.0;
    // The classic seas are shallow (amplitude 14–20 on the ocean types, no continents under 8000 blocks), so
    // the family adapts to the floor it finds: a cone rises as high as the water above its centre allows,
    // minus the clearance, and only counts when that leaves a real mountain. Deep seas (later parts) make
    // them grand; a shallow shelf gets modest knolls rather than nothing.
    private const int SeamountSummitClearance = 3;   // summit at most this far below the sea
    private const int SeamountMinDepthAtCentre = 7;  // the raw floor must be at least this deep
    private const double SeamountMinHeight = 4.0;
    private const long SeamountSalt = 0x5EA307;

    private bool HasSeamounts(PlanetType planet)
        => !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands
           && HasAir(planet) && WaterAbundanceOf(planet) >= 0.6;

    /// <summary>The seamount's rise at a column (generation 3) — 0 on a dry world, 0 wherever the raw ground is
    /// not clearly under the sea, and never enough to bring the ground above one below the sea.</summary>
    private double SeamountOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue)
        {
            return 0.0;
        }

        if (!TryGetHotspot(w.Seed ^ SeamountSalt, SeamountCellSize, SeamountChance, SeamountMaxRadius + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = SeamountMinRadius + ((h >> 16) & 0x3FF) / 1023.0 * (SeamountMaxRadius - SeamountMinRadius);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        double u = dist / radius;
        if (u >= 1.0)
        {
            return 0.0;
        }

        // The cone is anchored to the raw floor under its CENTRE, rolled once per cell and memoised.
        int cx = WorldConstants.WrapX(worldX - (int)System.Math.Round(dx), _circumference);
        int cz = WorldConstants.WrapZ(worldZ - (int)System.Math.Round(dz), _circumference);
        var key = (planet.Key, SeamountSalt, cx, cz);
        if (!TryGetSeaCell(key, out var cell))
        {
            int rawCentre = RawSurfaceHeight(planet, w, cx, cz);
            bool has = rawCentre <= sea - SeamountMinDepthAtCentre;
            double height = 0.0;
            if (has)
            {
                double rolled = 30.0 + ((h >> 26) & 0x3FF) / 1023.0 * 50.0; // 30..80
                height = System.Math.Min(rolled, sea - SeamountSummitClearance - rawCentre);
                has = height >= SeamountMinHeight;
            }

            cell = (has, height, 0.0);
            PutSeaCell(key, cell);
        }

        if (!cell.Has)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw > sea - 2)
        {
            return 0.0; // the sea does not own this column — the partition stays what the calibration saw
        }

        double rise = cell.A * System.Math.Pow(1.0 - u, 1.6);
        double cap = sea - SeamountSummitClearance - raw; // never above the clearance at THIS column either
        return rise > cap ? cap : rise;
    }

    /// <summary>Seamount centres and heights on a world (tests).</summary>
    internal (bool Has, double Height) SeamountCellForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (!w.Seamounts)
        {
            return (false, 0.0); // the table row is gated the same way
        }

        double rise = SeamountOffset(planet, w, worldX, worldZ);
        return (rise > 0.0, rise);
    }

    // ================= Icebergs =================
    // A faceted ice mass standing in cold open water, mostly under the line. The reference consumer of the
    // Ice band kind: written into the water span before the sea fill, so the column keeps its sea below.
    private const double IcebergCellSize = 320.0;
    private const double IcebergChance = 0.30;
    private const double IcebergMaxRadius = 24.0;
    private const double IcebergMinRadius = 8.0;
    private const int IcebergMinDepth = 3; // only over water at least this deep (keeps them off aprons and pads)
    private const long IcebergSalt = 0x1CEBE26;
    private const long IcebergFacetSalt = 0x1CEBE27;

    /// <summary>Cold watery worlds. The freeze itself is a calibration matter (<see cref="CanFreezeWater"/>), and
    /// WonderFor may not call the calibration, so the gate reads the base temperature only.</summary>
    private bool HasIcebergs(PlanetType planet)
        => !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands
           && HasAir(planet) && WaterAbundanceOf(planet) >= 0.5 && planet.BaseTemperature <= 2.0;

    /// <summary>The iceberg band covering a column (generation 3): an irregular dome, seven eighths below the
    /// waterline, standing in water at least <see cref="IcebergMinDepth"/> deep. False elsewhere.</summary>
    private bool TryGetIcebergBand(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = top = 0;
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || _calibrating)
        {
            return false;
        }

        if (!TryGetHotspot(w.Seed ^ IcebergSalt, IcebergCellSize, IcebergChance, IcebergMaxRadius + 4.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = IcebergMinRadius + ((h >> 16) & 0x3FF) / 1023.0 * (IcebergMaxRadius - IcebergMinRadius);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        double u = dist / radius;
        if (u >= 1.0)
        {
            return false;
        }

        int ground = SurfaceHeight(planet, worldX, worldZ);
        if (ground > sea - IcebergMinDepth || PadColumnAt(worldX, worldZ, out _, out _))
        {
            return false; // never over a landing pad's footprint (part 7)
        }

        double f = FbmT(w.Seed + IcebergFacetSalt, worldX, worldZ, 6.0, octaves: 2); // 0..1 facets
        double shape = 1.0 - u * u;
        top = sea + (int)System.Math.Round(shape * (2.0 + 6.0 * f));
        bottom = sea - (int)System.Math.Round(shape * (8.0 + 20.0 * (1.0 - f)));
        if (bottom < ground + 2)
        {
            bottom = ground + 2; // never grounded: the sea stays below the berg
        }

        return top >= bottom;
    }

    // ================= Underground rivers =================
    /// <summary>Wet soluble-rock worlds (the <c>karst</c> tag): the river rasteriser sinks the reaches that cross
    /// a karst belt (<see cref="KarstRegionAt"/>). The reference consumer of the sub-surface fluid spans.</summary>
    private bool HasUndergroundRivers(PlanetType planet)
        => !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands
           && planet.HasTag(TerrainTag.Karst) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4
           && planet.CaveThreshold > 0.0;
}
