// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 5 — wetlands and rivers. The river morphology itself (meanders with
/// oxbows, delta fans, floodplains) lives in <see cref="RiverField.Build"/> behind parameters that are the
/// classic no-op below generation 3; this partial holds the gates and the families around it: the floodplain
/// paint and its pools, rias (drowned coastal valleys, a sea-relative row), floating vegetation mats on lake
/// water (a material band), peat bogs (a paint six deep with pools, the new <c>peat</c> block) and thermokarst
/// ponds (polygon-rimmed pond fields on cold wet ground).</summary>
public sealed partial class WorldGenerator
{
    // ================= gates =================

    /// <summary>The wet water worlds whose rivers gain morphology: the same predicate the water river network
    /// builds on (a water sea, abundance ≥ 0.4), on land that can carry landmarks.</summary>
    private bool HasRiverMorphology(PlanetType planet)
        => HasMassifs(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4 && (planet.LavaAbundance ?? 0.0) <= 0.0;

    /// <summary>Drowned valleys on shelf coasts: wet, air-bearing, not the fjord (glacial) worlds.</summary>
    private bool HasRias(PlanetType planet)
        => HasMassifs(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4 && !planet.HasTag(TerrainTag.Glacial);

    /// <summary>Floating mats on the lakes of wetland worlds.</summary>
    private bool HasFloatingMats(PlanetType planet)
        => HasMassifs(planet) && HasAir(planet) && planet.HasTag(TerrainTag.Wetland) && WaterAbundanceOf(planet) >= 0.4;

    /// <summary>Peat on cool-to-cold wet ground with standing water: wetland or glacial country between −25 and
    /// 24 °C (boreal, tundra, swamp, ocean) — never a salt pan.</summary>
    private bool HasPeatBogs(PlanetType planet)
        => HasMassifs(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4 && !planet.HasTag(TerrainTag.Salt)
           && (planet.HasTag(TerrainTag.Wetland) || planet.HasTag(TerrainTag.Glacial))
           && planet.BaseTemperature <= 24.0 && planet.BaseTemperature >= -25.0;

    /// <summary>Thaw ponds on cold wet ground (≤ −5 °C), like the frost polygons but on plates of their own.</summary>
    private bool HasThermokarst(PlanetType planet)
        => HasMassifs(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4 && !planet.HasTag(TerrainTag.Salt)
           && planet.BaseTemperature <= -5.0;

    // ================= floodplains =================
    private const long FloodplainPoolSalt = 0xF100D01;

    /// <summary>Mud on the floodplain the rasteriser flagged beside a trunk reach or inside a delta fan.</summary>
    private BlockId? FloodplainPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!RiverFieldFor(planet).IsFloodplain(worldX, worldZ))
        {
            return null;
        }

        var mud = _content.GetBlock("mud")?.NumericId ?? BlockId.Air;
        return mud.IsAir ? null : mud;
    }

    /// <summary>A third of the floodplain stands one deep in water (a body of the generation-1 chain).</summary>
    private bool FloodplainPoolAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY)
        => RiverFieldFor(planet).IsFloodplain(worldX, worldZ)
           && Noise.Value01(w.Seed + FloodplainPoolSalt, WorldConstants.WrapX(worldX, _circumference), 0, Wz(worldZ)) < 0.30
           && !CaveMouthNear(planet, worldX, worldZ, surfaceY);

    // ================= rias =================
    private const long RiaSalt = 0x21A5EA;
    private const int RiaShelf = 6;   // the coast strip the sea may run into: raw ground at most this far above the sea

    /// <summary>Rias (drowned valleys): a sea-relative row. Where a valley line of a ridged field crosses the
    /// shelf coast — raw ground within <see cref="RiaShelf"/> above the sea — the ground drops in a V to 2–5
    /// below the sea, so the sea runs up the valley as a branching inlet. The head of the inlet ramps out where
    /// the ground climbs past the shelf. Never read while calibrating (a sea-relative row), so the percentile
    /// the sea level came from never sees it.</summary>
    private double RiaOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue)
        {
            return 0.0;
        }

        int raw = RawSurfaceHeight(planet, w, worldX, worldZ);
        if (raw <= sea || raw > sea + RiaShelf)
        {
            return 0.0;
        }

        if (w.UndergroundRivers && (KarstRegionAt(w, worldX, worldZ)
            || KarstRegionAt(w, worldX + 32, worldZ) || KarstRegionAt(w, worldX - 32, worldZ)
            || KarstRegionAt(w, worldX, worldZ + 32) || KarstRegionAt(w, worldX, worldZ - 32)))
        {
            // The soluble-rock belt keeps its coast, with a coarse cell of margin for the ramps that dive into it:
            // a sunk reach carries the centerline's levels across its cross-section, and a V cut across that
            // cross-section would leave a bank column with its water at the ground.
            return 0.0;
        }

        double valley = 1.0 - System.Math.Abs(2.0 * FbmT(w.Seed + RiaSalt, worldX, worldZ, 140.0, octaves: 3) - 1.0);
        if (valley <= 0.86)
        {
            return 0.0;
        }

        double t = (valley - 0.86) / 0.14;                       // 0 at the valley's edge, 1 on its line
        double head = Smooth01((sea + RiaShelf - raw) / 3.0);   // the inlet's head ramps out
        double target = sea - 2.0 - 3.0 * t;                     // the V: 2 below the sea at the edge, 5 on the line
        return (target - raw) * Smooth01(t / 0.3) * head;
    }

    // ================= floating mats =================
    private const double MatCellSize = 48.0;
    private const double MatChance = 0.30;
    private const double MatMaxRadius = 9.0;
    private const long MatSalt = 0x0F10A7;

    /// <summary>A floating vegetation mat: one cell of mud at the water top of a pooled lake column inside a
    /// 4–9-radius patch (a hotspot per 48-block cell). Nothing floats physically — it reads as a bog island.</summary>
    private bool TryGetMatBand(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int matY)
    {
        matY = 0;
        var field = RiverFieldFor(planet);
        if (!field.TryGetPooled(worldX, worldZ, out _) || !field.TryGet(worldX, worldZ, out var col)
            || col.WaterSurfaceY <= col.BedY)
        {
            return false;
        }

        if (!TryGetHotspot(w.Seed ^ MatSalt, MatCellSize, MatChance, MatMaxRadius + 2.0, worldX, worldZ,
                out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 4.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MatMaxRadius - 4.0);
        if (dx * dx + dz * dz >= radius * radius)
        {
            return false;
        }

        matY = col.WaterSurfaceY;
        return true;
    }

    // ================= peat bogs =================
    private const long PeatSalt = 0x9EA701;
    private const long PeatPoolSalt = 0x9EA702;

    private bool PeatRegionAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => FbmT(w.Seed + PeatSalt, worldX, worldZ, 300.0, octaves: 2) > 0.60 && SurfaceSlope(planet, worldX, worldZ) <= 2;

    /// <summary>Peat six deep across a bog region on flat ground.</summary>
    private BlockId? PeatBogPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!PeatRegionAt(planet, w, worldX, worldZ))
        {
            return null;
        }

        var peat = _content.GetBlock("peat")?.NumericId ?? BlockId.Air;
        if (peat.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - 6;
        return peat;
    }

    /// <summary>Two fifths of a bog stand one deep in water — the marsh-sheet recipe at a finer pitch.</summary>
    private bool PeatPoolAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY)
        => PeatRegionAt(planet, w, worldX, worldZ)
           && FbmT(w.Seed + PeatPoolSalt, worldX, worldZ, 7.0, octaves: 2) > 0.52
           && !CaveMouthNear(planet, worldX, worldZ, surfaceY);

    // ================= thermokarst =================
    private const long ThermoSalt = 0x7E2A01;
    private const long ThermoNetSalt = 0x7E2A02;
    private const double ThermoPitch = 46.0;

    /// <summary>The thaw-pond field at a column: the plate's pond depth (0 = no pond here) and whether the column
    /// lies on the pond's polygonal rim. Three plates in four of a region hold a pond; the pond is the plate's
    /// interior (so its outline is the Voronoi polygon), the rim the band just outside it. The frost-polygon
    /// region is excluded so the two cold patterns never stack.</summary>
    private (int Depth, bool Rim) ThermokarstAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (FbmT(w.Seed + ThermoSalt, worldX, worldZ, 420.0, octaves: 2) <= 0.60 || (w.FrostPolygons && FrostRegionAt(planet, w, worldX, worldZ))
            || IceCoveredAt(planet, w, worldX, worldZ))
        {
            return (0, false);
        }

        PolygonNet(w.Seed + ThermoNetSalt, worldX, worldZ, ThermoPitch, out double d1, out double d2, out ulong plate);
        if (((plate >> 58) & 3UL) == 0)
        {
            return (0, false); // a dry plate
        }

        double gap = d2 - d1;
        int depth = 2 + (int)((plate >> 55) % 3UL); // 2..4
        if (gap > 3.0)
        {
            return (depth, false);
        }

        return (0, gap > 1.5);
    }

    /// <summary>+1 on the rim of a thaw pond.</summary>
    private double ThermokarstOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => ThermokarstAt(planet, w, worldX, worldZ).Rim ? 1.0 : 0.0;

    /// <summary>The pond itself — 2–4 deep on flat ground (a body of the generation-1 chain; the freeze pass
    /// covers it with ice). <paramref name="depth"/> is the pond's depth when true.</summary>
    private bool ThermokarstPondAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int depth)
    {
        depth = ThermokarstAt(planet, w, worldX, worldZ).Depth;
        return depth > 0 && SurfaceSlope(planet, worldX, worldZ) <= 2
               && !CaveMouthNear(planet, worldX, worldZ, surfaceY - depth + 1);
    }

    // ================= test hooks =================

    /// <summary>The part-5 offset rows by name (tests): ria, thermokarst.</summary>
    internal double WetlandOffsetForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return name switch
        {
            "ria" => w.Rias ? RiaOffset(planet, w, worldX, worldZ) : 0.0,
            "thermokarst" => w.Thermokarst ? ThermokarstOffset(planet, w, worldX, worldZ) : 0.0,
            _ => throw new System.ArgumentException(name, nameof(name)),
        };
    }

    /// <summary>The thaw-pond field at a column (tests): depth / rim, both zero off the field or below generation 3.</summary>
    internal (int Depth, bool Rim) ThermokarstForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.Thermokarst ? ThermokarstAt(planet, w, worldX, worldZ) : (0, false);
    }

    /// <summary>Whether a column lies in a peat region (tests).</summary>
    internal bool PeatRegionForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.PeatBogs && PeatRegionAt(planet, w, worldX, worldZ);
    }

    /// <summary>The floating-mat cell at a column (tests): the mat's Y, or null.</summary>
    internal int? MatBandForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.FloatingMats && TryGetMatBand(planet, w, worldX, worldZ, out int y) ? y : null;
    }
}
