// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Generation-1 landmark families, overhang bands and underground finds (#1646, landscape variety 3/6):
/// shield volcanoes, impact basins, glacial troughs, yardang and drumlin fields, inselbergs, star dunes,
/// mud-volcano fields, sinkhole chains, maars, mushroom rocks, glacier tongues; natural bridges, wave-cut
/// coastal overhangs, ice cornices; crystal geodes and sediment strata (partial of <see cref="WorldGenerator"/>).
/// Every family is one hotspot-cell feature on the #477 recipe (one deterministic candidate per cell, the
/// centre kept a full extent inside its cell) and gates on the world's generation through its profile flag,
/// so no generation-0 world ever sees one.</summary>
public sealed partial class WorldGenerator
{
    private static double Smooth01(double t) => t <= 0.0 ? 0.0 : t >= 1.0 ? 1.0 : t * t * (3.0 - 2.0 * t);

    private static bool HasAir(PlanetType planet)
        => !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);

    private static double WaterAbundanceOf(PlanetType planet) => planet.WaterAbundance ?? (HasAir(planet) ? 0.55 : 0.0);

    private static bool SandSurface(PlanetType planet)
    {
        if (string.Equals(planet.SurfaceBlock, "sand", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var b in planet.Biomes)
        {
            if (string.Equals(b.SurfaceBlock, "sand", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- gates (resolved once per world in WonderFor, generation ≥ 1 only) ----------------

    /// <summary>Shield volcanoes: every volcanic-tagged world, plus a quarter of the other volcano worlds.</summary>
    private bool HasShieldVolcanoes(PlanetType planet, long seed)
        => HasVolcanoes(planet) && (planet.HasTag(TerrainTag.Volcanic) || (Noise.Hash(seed ^ 0x5A1E1D01, 1, 2, 3) & 0xFF) < 64);

    private bool HasImpactBasins(PlanetType planet) => HasMassifs(planet);

    private bool HasInselbergs(PlanetType planet) => HasMassifs(planet) && planet.HasTag(TerrainTag.Inselbergs);

    private bool HasGlacialTroughs(PlanetType planet)
        => HasMassifs(planet) && (planet.HasTag(TerrainTag.Glacial) || planet.BaseTemperature <= -5.0);

    private bool HasGlacierTongues(PlanetType planet) => HasMassifs(planet) && planet.BaseTemperature <= -5.0;

    private bool HasYardangs(PlanetType planet) => HasMassifs(planet) && planet.HasTag(TerrainTag.Wind);

    /// <summary>Drumlin fields as a landmark region — unless the world already rolled the drumlins STYLE.</summary>
    private bool HasDrumlinFields(PlanetType planet, string[] styles)
        => HasMassifs(planet) && planet.HasTag(TerrainTag.Glacial) && System.Array.IndexOf(styles, "drumlins") < 0;

    private bool HasStarDunes(PlanetType planet) => HasMassifs(planet) && planet.HasTag(TerrainTag.Wind) && SandSurface(planet);

    private bool HasMudVolcanoes(PlanetType planet) => HasVolcanoes(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.4;

    private bool HasSinkholeChains(PlanetType planet) => HasCenotes(planet);

    private bool HasMaars(PlanetType planet) => HasMassifs(planet) && WaterAbundanceOf(planet) > 0.3;

    private bool HasMushroomRocks(PlanetType planet) => HasTableMountains(planet);

    private bool HasNaturalBridges(PlanetType planet) => HasRifts(planet);

    private bool HasCoastalOverhangs(PlanetType planet) => HasSeaStacks(planet);

    private bool HasIceCornices(PlanetType planet) => HasPenitentes(planet);

    private bool HasGeodes(PlanetType planet) => HasCaverns(planet) && !planet.Cratered && !_crateredWorld;

    private bool HasStrata(PlanetType planet) => HasMassifs(planet);

    // ---------------- shield volcano ----------------
    private const double ShieldCellSize = 3000.0;
    private const double ShieldChance = 0.12;
    private const double ShieldMaxRadius = 400.0;

    /// <summary>A very broad, low dome (r 200–400, h 20–40) with a shallow summit bowl — the lava lake that
    /// fills the bowl is part 4's fluid work; until then the summit is a wide crater.</summary>
    private double ShieldVolcanoOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x5A1E1D02, ShieldCellSize, ShieldChance, ShieldMaxRadius + 20.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 200.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ShieldMaxRadius - 200.0); // 200..400
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return 0.0;
        }

        double height = 20.0 + ((h >> 26) & 0x3FF) / 1023.0 * 20.0; // 20..40
        height = System.Math.Min(height, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);
        double u = dist / radius;
        double dome = height * System.Math.Pow(1.0 - u * u, 1.2);
        double bowlR = radius * 0.22;
        if (dist < bowlR)
        {
            dome -= 6.0 * Smooth01(1.0 - dist / bowlR);
        }

        return dome;
    }

    // ---------------- flooded impact basin ----------------
    private const double BasinCellSize = 2600.0;
    private const double BasinChance = 0.08;
    private const double BasinMaxRadius = 250.0;

    /// <summary>A rare broad bowl (r 120–250, 20–35 deep) with a raised rim; wherever the floor dips under
    /// the sea percentile it floods into a round lake for free.</summary>
    private double ImpactBasinOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x1B0A51, BasinCellSize, BasinChance, BasinMaxRadius + 20.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 120.0 + ((h >> 16) & 0x3FF) / 1023.0 * (BasinMaxRadius - 120.0); // 120..250
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return 0.0;
        }

        double depth = 20.0 + ((h >> 26) & 0x3FF) / 1023.0 * 15.0; // 20..35
        double u = dist / radius;
        double bowl = u < 0.85 ? -depth * (1.0 - (u / 0.85) * (u / 0.85)) : 0.0;
        double rim = 6.0 * System.Math.Exp(-((u - 0.92) / 0.05) * ((u - 0.92) / 0.05)) * Smooth01((1.0 - u) / 0.04);
        return bowl + rim;
    }

    // ---------------- glacial trough ----------------
    private const double TroughCellSize = 2200.0;
    private const double TroughChance = 0.25;
    private const double TroughMaxHalfLen = 400.0;
    private const double TroughMaxHalfWidth = 45.0;

    /// <summary>A U-shaped valley (25–45 deep) whose floor slopes toward one end — the deep end takes the
    /// terminal tarn (part 4) or floods where it dips under the sea.</summary>
    private double GlacialTroughOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x61AC1A1, TroughCellSize, TroughChance, TroughMaxHalfLen + TroughMaxHalfWidth + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 200.0 + ((h >> 26) & 0x3FF) / 1023.0 * (TroughMaxHalfLen - 200.0);   // 200..400
        double halfWidth = 25.0 + ((h >> 56) & 0xFF) / 255.0 * (TroughMaxHalfWidth - 25.0);   // 25..45
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        double across = -dx * sin + dz * cos;
        if (System.Math.Abs(along) > halfLen || System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        ulong h2 = h * 0x9E3779B97F4A7C15UL;
        double depth = 25.0 + ((h2 >> 20) & 0x3FF) / 1023.0 * 20.0; // 25..45
        double u = across / halfWidth;
        double wall = 1.0 - u * u;                                   // the U
        double slope = 0.55 + 0.45 * (along / halfLen * 0.5 + 0.5);  // deeper toward the +along end
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        double taper = endT >= 0.15 ? 1.0 : Smooth01(endT / 0.15);
        return -depth * wall * slope * taper;
    }

    // ---------------- yardang field ----------------
    private const double YardangCellSize = 1400.0;
    private const double YardangChance = 0.35;
    private const double YardangMaxRadius = 260.0;

    /// <summary>Parallel wind-carved rock ridges (6–12 tall, pitch 18–30) along the world's grain inside a
    /// round field; the field edge fades over its outer quarter.</summary>
    private double YardangOffset(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ 0x9A2DA46, YardangCellSize, YardangChance, YardangMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 120.0 + ((h >> 16) & 0x3FF) / 1023.0 * (YardangMaxRadius - 120.0); // 120..260
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return 0.0;
        }

        double amp = 6.0 + ((h >> 26) & 0x3FF) / 1023.0 * 6.0;    // 6..12
        double pitch = 18.0 + ((h >> 36) & 0x3FF) / 1023.0 * 12.0; // 18..30
        double ridge = System.Math.Max(0.0, OrientedRidge(w.Seed + 0x9A2DA47, w.Grain, worldX, worldZ, pitch));
        double fall = Smooth01((1.0 - dist / radius) / 0.25);
        return amp * System.Math.Pow(ridge, 1.5) * fall;
    }

    // ---------------- drumlin field ----------------
    private const double DrumlinCellSize = 1400.0;
    private const double DrumlinChance = 0.35;
    private const double DrumlinMaxRadius = 260.0;

    /// <summary>Rounded whaleback ridges (6–10 tall) elongated along the grain inside a round field.</summary>
    private double DrumlinFieldOffset(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ 0xD2A1B, DrumlinCellSize, DrumlinChance, DrumlinMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 120.0 + ((h >> 16) & 0x3FF) / 1023.0 * (DrumlinMaxRadius - 120.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return 0.0;
        }

        double amp = 6.0 + ((h >> 26) & 0x3FF) / 1023.0 * 4.0; // 6..10
        double d = GrainFbm(w.Seed + 0xD2A1C, w.Grain, worldX, worldZ, 40.0, octaves: 2);
        double ridged = Smooth01(1.0 - System.Math.Abs(d * 2.0 - 1.0));
        return amp * ridged * Smooth01((1.0 - dist / radius) / 0.25);
    }

    // ---------------- inselberg ----------------
    private const double InselbergCellSize = 1500.0;
    private const double InselbergChance = 0.35;
    private const double InselbergMaxRadius = 150.0;

    private bool TryGetInselberg(PlanetType planet, long seed, int worldX, int worldZ, out double rise, out double radius, out double dist)
    {
        rise = radius = dist = 0.0;
        if (!TryGetHotspot(seed ^ 0x1A5E1B, InselbergCellSize, InselbergChance, InselbergMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        radius = 60.0 + ((h >> 16) & 0x3FF) / 1023.0 * (InselbergMaxRadius - 60.0); // 60..150
        dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return false;
        }

        double height = 40.0 + ((h >> 26) & 0x3FF) / 1023.0 * 50.0; // 40..90
        height = System.Math.Min(height, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);
        double u = dist / radius;
        rise = height * System.Math.Pow(1.0 - u * u, 0.8); // steep flanks, rounded crown
        return true;
    }

    /// <summary>A lone smooth granite dome rising from flat ground (r 60–150, h 40–90).</summary>
    private double InselbergOffset(PlanetType planet, long seed, int worldX, int worldZ)
        => TryGetInselberg(planet, seed, worldX, worldZ, out double rise, out _, out _) ? rise : 0.0;

    /// <summary>The dome's bare granite skin (the paint delegate of the inselberg row).</summary>
    private BlockId? InselbergPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetInselberg(planet, w.Seed, worldX, worldZ, out double rise, out _, out _) || rise < 2.5)
        {
            return null;
        }

        var granite = _content.GetBlock("granite")?.NumericId ?? BlockId.Air;
        return granite.IsAir ? null : granite;
    }

    // ---------------- star dunes ----------------
    private const double StarDuneCellSize = 700.0;
    private const double StarDuneChance = 0.30;
    private const double StarDuneMaxRadius = 120.0;

    /// <summary>A pyramidal sand mountain (15–30 tall) with 3–5 radial arms.</summary>
    private double StarDuneOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x57A2D0, StarDuneCellSize, StarDuneChance, StarDuneMaxRadius + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 60.0 + ((h >> 16) & 0x3FF) / 1023.0 * (StarDuneMaxRadius - 60.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return 0.0;
        }

        double height = 15.0 + ((h >> 26) & 0x3FF) / 1023.0 * 15.0; // 15..30
        int arms = 3 + (int)((h >> 36) & 0x3) % 3;                    // 3..5
        double phase = ((h >> 40) & 0x3FF) / 1023.0 * System.Math.PI * 2.0;
        double theta = System.Math.Atan2(dz, dx);
        double arm = System.Math.Max(0.0, System.Math.Cos(arms * (theta - phase)));
        double core = System.Math.Pow(1.0 - dist / radius, 1.5);
        return height * core * (0.35 + 0.65 * arm * arm);
    }

    // ---------------- mud-volcano / fumarole field ----------------
    private const double MudFieldCellSize = 900.0;
    private const double MudFieldChance = 0.12;
    private const double MudFieldMaxRadius = 120.0;

    /// <summary>A round field of small cones (3–6 tall) with a dip at each vent — wet volcano worlds.</summary>
    private double MudVolcanoOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x3DD0F1, MudFieldCellSize, MudFieldChance, MudFieldMaxRadius + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 60.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MudFieldMaxRadius - 60.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return 0.0;
        }

        double c = FbmT(seed + 0x3DD0F2, worldX, worldZ, 9.0, octaves: 2);
        if (c <= 0.72)
        {
            return 0.0;
        }

        double coneH = 3.0 + ((h >> 26) & 0x3FF) / 1023.0 * 3.0; // 3..6
        double rise = System.Math.Pow((c - 0.72) / 0.28, 1.3) * coneH;
        if (c > 0.95)
        {
            rise *= 0.4; // the vent dip
        }

        return rise * Smooth01((1.0 - dist / radius) / 0.3);
    }

    // ---------------- sinkhole chain ----------------
    private const double SinkholeCellSize = 900.0;
    private const double SinkholeChance = 0.25;
    private const double SinkholeMaxHalfLen = 90.0;

    /// <summary>3–5 sheer shafts (r 3–5, 12–20 deep) strung along a straight line.</summary>
    private double SinkholeChainOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x51AC40, SinkholeCellSize, SinkholeChance, SinkholeMaxHalfLen + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 40.0 + ((h >> 26) & 0x3FF) / 1023.0 * (SinkholeMaxHalfLen - 40.0);
        int count = 3 + (int)((h >> 36) & 0x3) % 3;                   // 3..5
        double radius = 3.0 + ((h >> 40) & 0x3FF) / 1023.0 * 2.0;      // 3..5
        double depth = 12.0 + ((h >> 50) & 0x3FF) / 1023.0 * 8.0;      // 12..20
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        double across = -dx * sin + dz * cos;
        if (System.Math.Abs(across) > radius || System.Math.Abs(along) > halfLen + radius)
        {
            return 0.0;
        }

        for (int k = 0; k < count; k++)
        {
            double pos = -halfLen + 2.0 * halfLen * (k + 0.5) / count;
            double dd = System.Math.Sqrt((along - pos) * (along - pos) + across * across);
            if (dd < radius)
            {
                return -depth * Smooth01((1.0 - dd / radius) / 0.3);
            }
        }

        return 0.0;
    }

    // ---------------- maar ----------------
    private const double MaarCellSize = 1100.0;
    private const double MaarChance = 0.25;
    private const double MaarMaxRadius = 40.0;

    /// <summary>A small round explosion bowl (r 20–40, 8–14 deep) with a low rim — the crater lake is part 4.</summary>
    private double MaarOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x3AA201, MaarCellSize, MaarChance, MaarMaxRadius * 1.15 + 6.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 20.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MaarMaxRadius - 20.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        double u = dist / radius;
        if (u >= 1.15)
        {
            return 0.0;
        }

        double depth = 8.0 + ((h >> 26) & 0x3FF) / 1023.0 * 6.0; // 8..14
        double bowl = u < 1.0 ? -depth * (1.0 - u * u) : 0.0;
        double rim = 2.0 * System.Math.Exp(-((u - 1.0) / 0.08) * ((u - 1.0) / 0.08));
        if (u > 1.0)
        {
            rim *= Smooth01((1.15 - u) / 0.15);
        }

        return bowl + rim;
    }

    // ---------------- mushroom rock ----------------
    private const double MushroomCellSize = 260.0;
    private const double MushroomChance = 0.25;

    private bool TryGetMushroomRock(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out double dist, out double rise, out double capR, out int anchor)
    {
        dist = rise = capR = 0.0;
        anchor = 0;
        if (!TryGetHotspot(w.Seed ^ 0x3B5D00, MushroomCellSize, MushroomChance, 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        dist = System.Math.Sqrt(dx * dx + dz * dz);
        capR = 3.5 + ((h >> 16) & 0x3) * 0.6;   // 3.5..5.3
        if (dist > capR || FbmT(w.Seed + 0x3B5D01, worldX, worldZ, w.Scale * 1.6, octaves: 2) <= 0.58)
        {
            return false; // outside the cap, or outside a mushroom-rock region
        }

        rise = 5.0 + ((h >> 20) & 0x3);         // 5..8
        anchor = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        return true;
    }

    /// <summary>The mushroom rock's stem (the landmark row): a sheer pillar 5–8 tall under the cap.</summary>
    private double MushroomStemOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetMushroomRock(planet, w, worldX, worldZ, out double dist, out double rise, out _, out _) || dist > 1.6)
        {
            return 0.0;
        }

        return rise * System.Math.Pow(1.0 - dist / 1.6, 0.2);
    }

    /// <summary>The mushroom rock's wide cap band: a 2-thick rock disc on the stem top.</summary>
    private bool TryGetMushroomCap(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetMushroomRock(planet, w, worldX, worldZ, out _, out double rise, out _, out int anchor))
        {
            return false;
        }

        top = anchor + (int)System.Math.Round(rise) + 1;
        bottom = top - 1;
        return true;
    }

    // ---------------- glacier tongue (paint only) ----------------

    /// <summary>Ice paint on one flank sector of a massif on a cold world — a glacier tongue running down
    /// from the summit region (the crevasse fields of #709 already slit the ice).</summary>
    private BlockId? GlacierTonguePaint(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ 0x3A551F, MassifCellSize, MassifChance, MassifMaxRadius + 20.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return null;
        }

        double radius = 150.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MassifMaxRadius - 150.0); // the massif's own roll
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist < radius * 0.12 || dist > radius * 0.85)
        {
            return null;
        }

        double phi = ((h >> 40) & 0x3FF) / 1023.0 * System.Math.PI * 2.0;
        double half = 0.35 + ((h >> 50) & 0xFF) / 255.0 * 0.25;
        double theta = System.Math.Atan2(dz, dx);
        double diff = System.Math.Abs(System.Math.IEEERemainder(theta - phi, System.Math.PI * 2.0));
        if (diff > half)
        {
            return null;
        }

        var ice = _content.GetBlock("ice")?.NumericId ?? BlockId.Air;
        return ice.IsAir ? null : ice;
    }

    // ---------------- natural bridge over a rift ----------------

    /// <summary>The rift's straight-gorge geometry, the same rolls <see cref="RiftOffset"/> makes (kept
    /// separate so the classic offset stays byte-identical).</summary>
    private bool TryGetRiftGeometry(long seed, int worldX, int worldZ,
        out double along, out double across, out double halfLen, out double halfWidth, out ulong hash)
    {
        along = across = halfLen = halfWidth = 0.0;
        if (!TryGetHotspot(seed ^ 0x21F7A9, RiftCellSize, RiftChance,
                RiftMaxHalfLen + RiftMaxHalfWidth + 16.0, worldX, worldZ,
                out hash, out double dx, out double dz))
        {
            return false;
        }

        double angle = ((hash >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        halfLen = 260.0 + ((hash >> 26) & 0x3FF) / 1023.0 * (RiftMaxHalfLen - 260.0);
        halfWidth = 14.0 + ((hash >> 56) & 0xFF) / 255.0 * (RiftMaxHalfWidth - 14.0);
        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        along = dx * cos + dz * sin;
        across = -dx * sin + dz * cos;
        return true;
    }

    /// <summary>A rock bridge spanning the gorge at 1–2 rolled points: a 3-thick deck at the rim ground, 7 wide.</summary>
    private bool TryGetNaturalBridge(PlanetType planet, long seed, int worldX, int worldZ, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetRiftGeometry(seed, worldX, worldZ, out double along, out double across, out double halfLen, out double halfWidth, out ulong h)
            || System.Math.Abs(across) > halfWidth + 1.0 || System.Math.Abs(along) > halfLen * 0.8)
        {
            return false;
        }

        ulong h2 = h * 0xD1B54A32D192ED03UL;
        int bridges = 1 + (int)((h2 >> 4) & 1UL);
        for (int k = 0; k < bridges; k++)
        {
            double pos = (-0.55 + 1.1 * ((h2 >> (8 + 10 * k)) & 0x3FF) / 1023.0) * halfLen * 0.8;
            if (System.Math.Abs(along - pos) <= 3.0)
            {
                int anchor = RawSurfaceHeight(planet, worldX, worldZ); // the pre-rift rim ground
                top = anchor;
                bottom = anchor - 2;
                return true;
            }
        }

        return false;
    }

    // ---------------- wave-cut coastal overhang ----------------

    /// <summary>A rock ledge hanging over the water at the foot of a sea cliff: on a shallow-water column next
    /// to ground that stands 4+ above the sea, a 2-thick band just above the waterline (about half of all
    /// such coasts, by a mask).</summary>
    private bool TryGetCoastalOverhang(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || surfaceY >= sea - 1 || surfaceY < sea - 6)
        {
            return false;
        }

        if (FbmT(seed + 0xC0A57A1, worldX, worldZ, 60.0, octaves: 2) <= 0.5)
        {
            return false;
        }

        bool cliff = SurfaceHeight(planet, worldX + 2, worldZ) >= sea + 4
            || SurfaceHeight(planet, worldX - 2, worldZ) >= sea + 4
            || SurfaceHeight(planet, worldX, worldZ + 2) >= sea + 4
            || SurfaceHeight(planet, worldX, worldZ - 2) >= sea + 4;
        if (!cliff)
        {
            return false;
        }

        bottom = sea + 2;
        top = sea + 3;
        return true;
    }

    // ---------------- ice cornice ----------------
    private const double CorniceCellSize = 700.0;
    private const double CorniceChance = 0.30;
    private const double CorniceMaxRadius = 200.0;

    /// <summary>Snow lips jutting from ridge crests over a steep drop, inside sparse cornice regions of the
    /// coldest worlds: a column 3 from a crest that stands 6+ higher carries a 2-thick band at crest level.</summary>
    private bool TryGetIceCornice(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, out int bottom, out int top)
    {
        bottom = 0;
        top = -1;
        if (!TryGetHotspot(seed ^ 0x1CEC0A1, CorniceCellSize, CorniceChance, CorniceMaxRadius + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 100.0 + ((h >> 16) & 0x3FF) / 1023.0 * (CorniceMaxRadius - 100.0);
        if (dx * dx + dz * dz >= radius * radius)
        {
            return false;
        }

        int crest = System.Math.Max(
            System.Math.Max(SurfaceHeight(planet, worldX + 3, worldZ), SurfaceHeight(planet, worldX - 3, worldZ)),
            System.Math.Max(SurfaceHeight(planet, worldX, worldZ + 3), SurfaceHeight(planet, worldX, worldZ - 3)));
        if (crest < surfaceY + 6)
        {
            return false;
        }

        top = crest;
        bottom = crest - 1;
        return true;
    }

    // ---------------- crystal geode ----------------
    private const double GeodeCellSize = 500.0;
    private const double GeodeChance = 0.30;
    private const double GeodeMaxRadius = 14.0;

    /// <summary>The geode covering this column (r 6–14, 30–80 below base): the full sphere span and the hollow
    /// interior span (empty when the column only grazes the shell). Everything between is crystal shell.</summary>
    private bool TryGetGeodeSpan(PlanetType planet, WonderProfile w, int worldX, int worldZ,
        out int yLo, out int yHi, out int innerLo, out int innerHi)
    {
        yLo = yHi = 0;
        innerLo = 1;
        innerHi = 0;
        if (!TryGetHotspot(w.Seed ^ 0x6E0DE01, GeodeCellSize, GeodeChance, GeodeMaxRadius + 4.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double r = 6.0 + ((h >> 16) & 0x3FF) / 1023.0 * (GeodeMaxRadius - 6.0);
        double q = r * r - dx * dx - dz * dz;
        if (q <= 0.0)
        {
            return false;
        }

        int cy = planet.BaseHeight - 30 - (int)((h >> 26) & 0x3F) % 51; // 30..80 below base
        double half = System.Math.Sqrt(q);
        yLo = cy - (int)half;
        yHi = cy + (int)half;
        double ri = r - 1.6;
        double qi = ri * ri - dx * dx - dz * dz;
        if (qi > 0.0)
        {
            double hi = System.Math.Sqrt(qi);
            innerLo = cy - (int)hi;
            innerHi = cy + (int)hi;
        }

        return true;
    }

    // ---------------- sediment strata ----------------

    /// <summary>The strata region shift for a column (gently tilted bands 7 apart, 2 thick, granite), or
    /// <see cref="int.MinValue"/> where the column lies outside a strata region.</summary>
    private int StrataShiftAt(long seed, int worldX, int worldZ)
    {
        if (FbmT(seed + 0x57A7A01, worldX, worldZ, 420.0, octaves: 2) <= 0.58)
        {
            return int.MinValue;
        }

        return (int)System.Math.Round((FbmT(seed + 0x57A7A02, worldX, worldZ, 300.0, octaves: 2) - 0.5) * 24.0);
    }

    private static bool StrataBandAt(int worldY, int shift) => ((worldY + shift) % 7 + 7) % 7 < 2;

    // ---------------- test hooks ----------------

    /// <summary>A landmark row's offset at a column, 0 when the row is inactive on this world (tests).</summary>
    internal double LandmarkOffsetForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        foreach (var k in LandmarkKinds)
        {
            if (k.Name == name)
            {
                return k.Active(w) ? k.Offset(this, planet, w, worldX, worldZ) : 0.0;
            }
        }

        throw new System.ArgumentException($"unknown landmark row '{name}'", nameof(name));
    }

    /// <summary>A landmark row's paint at a column (tests).</summary>
    internal BlockId? LandmarkPaintForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        foreach (var k in LandmarkKinds)
        {
            if (k.Name == name)
            {
                return k.Active(w) && k.Paint is { } paint ? paint(this, planet, w, worldX, worldZ, SurfaceHeight(planet, worldX, worldZ)) : null;
            }
        }

        throw new System.ArgumentException($"unknown landmark row '{name}'", nameof(name));
    }

    /// <summary>The geode span at a column (tests).</summary>
    internal bool TryGetGeodeSpanForTest(PlanetType planet, int worldX, int worldZ, out int yLo, out int yHi, out int innerLo, out int innerHi)
    {
        var w = WonderFor(planet);
        yLo = yHi = innerLo = innerHi = 0;
        return w.Geodes && TryGetGeodeSpan(planet, w, worldX, worldZ, out yLo, out yHi, out innerLo, out innerHi);
    }

    /// <summary>Whether the strata bands run on this world (tests).</summary>
    internal bool StrataForTest(PlanetType planet) => WonderFor(planet).Strata;
}
