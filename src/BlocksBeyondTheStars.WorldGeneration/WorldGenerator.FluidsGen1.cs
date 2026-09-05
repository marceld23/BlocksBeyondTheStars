// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Generation-1 water and lava bodies and surface paints (#1647, landscape variety 4/6): marsh sheets,
/// oases, hot springs, caldera / shield / maar lakes, playas, tarns; scree slopes, soil patches, ash fall,
/// dry riverbeds, deck banding, moss stone (partial of <see cref="WorldGenerator"/>).
/// <see cref="TryGetGen1Water"/> is the ONE source of truth for the new bodies: the column phase fills what it
/// says, and the surface-water helper queries (<c>TryGetRawWaterColumn</c>, <c>TryGetLavaSurface</c>,
/// <c>SurfaceGen1WaterDepth</c>) read the same function, so placement and fauna can never disagree with
/// the generated blocks. Everything gates on the world's generation through its profile flags.</summary>
public sealed partial class WorldGenerator
{
    // ---------------- gates (WonderFor, generation ≥ 1) ----------------

    private bool HasMarshes(PlanetType planet)
        => HasMassifs(planet) && planet.HasTag(TerrainTag.Wetland) && WaterAbundanceOf(planet) >= 0.4;

    /// <summary>Oases: dry sand worlds with an atmosphere (the desert family).</summary>
    private bool HasOases(PlanetType planet)
        => HasMassifs(planet) && SandSurface(planet) && WaterAbundanceOf(planet) <= 0.15;

    private bool HasHotSprings(PlanetType planet)
        => HasVolcanoes(planet) && HasAir(planet) && WaterAbundanceOf(planet) >= 0.3;

    private bool HasCalderaLakes(PlanetType planet)
        => HasCalderas(planet) && (WaterAbundanceOf(planet) >= 0.3 || planet.HasTag(TerrainTag.Volcanic));

    private bool HasPlayas(PlanetType planet)
        => HasMassifs(planet) && SandSurface(planet) && WaterAbundanceOf(planet) <= 0.15;

    /// <summary>Whether the world's rolled styles include a deck style (mesa / tablelands / terraces).</summary>
    private static bool DeckStyleWorld(string[] styles)
        => System.Array.IndexOf(styles, "mesa") >= 0 || System.Array.IndexOf(styles, "tablelands") >= 0
           || System.Array.IndexOf(styles, "terraces") >= 0;

    private static bool MossWorld(PlanetType planet)
        => HasAir(planet) && planet.BaseTemperature >= 5.0 && planet.BaseTemperature <= 25.0 && WaterAbundanceOf(planet) >= 0.5;

    private static bool DryBedWorld(PlanetType planet)
        => HasAir(planet) && WaterAbundanceOf(planet) <= 0.3;

    // ---------------- the generation-1 water / lava bodies ----------------

    /// <summary>The generation-1 body covering a surface column that no classic body (sea, pond, crater,
    /// river, travertine, cenote) claimed: outputs the fluid's top cell, the bed and the fluid block. Fixed
    /// order — marsh, oasis, hot spring, caldera lake, shield lake, maar lake, playa, tarn — first hit wins.</summary>
    private bool TryGetGen1Water(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY,
        out int top, out int bed, out BlockId fluid)
    {
        top = bed = 0;
        fluid = BlockId.Air;
        var water = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        var lava = _content.GetBlock("lava")?.NumericId ?? BlockId.Air;
        if (water.IsAir)
        {
            return false;
        }

        long seed = w.Seed;
        if (w.Marshes && TryGetMarshSheet(planet, w, worldX, worldZ, surfaceY))
        {
            top = surfaceY;
            bed = surfaceY - 1;
            fluid = water;
            return true;
        }

        if (w.Oases && TryGetOasis(planet, seed, worldX, worldZ, out double dist, out double radius, out int anchor) && dist < radius)
        {
            double u = dist / radius;
            int depth = System.Math.Max(1, (int)System.Math.Round(3.0 * (1.0 - u * u)));
            top = anchor;
            bed = System.Math.Min(surfaceY, anchor) - depth;
            fluid = water;
            return bed < top;
        }

        if (w.HotSprings && TryGetHotSpringPool(seed, worldX, worldZ, out int poolDepth))
        {
            top = surfaceY;
            bed = surfaceY - poolDepth;
            fluid = water;
            return true;
        }

        if (w.CalderaLakes && TryGetCalderaLake(planet, seed, worldX, worldZ, surfaceY, out int lakeTop))
        {
            top = lakeTop;
            bed = surfaceY;
            fluid = planet.HasTag(TerrainTag.Volcanic) && !lava.IsAir && WaterAbundanceOf(planet) < 0.3 ? lava : water;
            return true;
        }

        if (w.ShieldVolcanoes && TryGetShieldLake(planet, seed, worldX, worldZ, surfaceY, out int shieldTop))
        {
            top = shieldTop;
            bed = surfaceY;
            fluid = !lava.IsAir ? lava : water;
            return true;
        }

        if (w.Maars && TryGetMaarLake(planet, seed, worldX, worldZ, surfaceY, out int maarTop))
        {
            top = maarTop;
            bed = surfaceY;
            fluid = water;
            return true;
        }

        if (w.Playas && TryGetPlaya(planet, seed, worldX, worldZ, out bool playaWet) && playaWet)
        {
            top = surfaceY;
            bed = surfaceY - 1;
            fluid = water;
            return true;
        }

        if (w.GlacialTroughs && TryGetTarn(planet, seed, worldX, worldZ, surfaceY, out int tarnTop))
        {
            top = tarnTop;
            bed = surfaceY;
            fluid = water;
            return true;
        }

        return false;
    }

    /// <summary>Water depth (0 = none) of a generation-1 WATER body at a surface column — for placement
    /// (trees, props, landings) that must stay out of marsh sheets, oases and tarns like out of ponds.</summary>
    public int SurfaceGen1WaterDepth(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        if (w.Generation < 1)
        {
            return 0;
        }

        int surfaceY = SurfaceHeight(planet, worldX, worldZ);
        if (surfaceY <= ResolveSeaFluid(planet).Level || SurfacePondDepth(planet, worldX, worldZ) > 0
            || SurfaceRiverDepth(planet, worldX, worldZ) > 0 || TryGetVolcanoCrater(planet, worldX, worldZ, out _))
        {
            return 0; // a classic body owns the column
        }

        var water = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        return TryGetGen1Water(planet, w, worldX, worldZ, surfaceY, out int top, out int bed, out var fluid) && fluid == water
            ? System.Math.Max(1, top - bed) : 0;
    }

    // --- marsh sheets: on wetland worlds a broad marsh region alternates 1-deep water sheets with mud ---
    private bool MarshRegionAt(WonderProfile w, int worldX, int worldZ)
        => FbmT(w.Seed + 0x3A2501, worldX, worldZ, w.Scale * 3.0, octaves: 2) > 0.64;

    private bool TryGetMarshSheet(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY)
    {
        if (!MarshRegionAt(w, worldX, worldZ) || SurfaceSlope(planet, worldX, worldZ) > 2)
        {
            return false;
        }

        return FbmT(w.Seed + 0x3A2502, worldX, worldZ, 9.0, octaves: 2) > 0.47;
    }

    /// <summary>True on the dry mud of a marsh region (the reed host between the sheets).</summary>
    private bool MarshMudAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => MarshRegionAt(w, worldX, worldZ) && SurfaceSlope(planet, worldX, worldZ) <= 3;

    // --- oases: a round pond in the desert with a grass ring and palms ---
    private const double OasisCellSize = 900.0;
    private const double OasisChance = 0.35;
    private const double OasisMaxRadius = 14.0;
    private const double OasisRing = 6.0;

    private bool TryGetOasis(PlanetType planet, long seed, int worldX, int worldZ, out double dist, out double radius, out int anchor)
    {
        dist = radius = 0.0;
        anchor = 0;
        if (!TryGetHotspot(seed ^ 0x0A515, OasisCellSize, OasisChance, OasisMaxRadius + OasisRing + 10.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        radius = 6.0 + ((h >> 16) & 0x3FF) / 1023.0 * (OasisMaxRadius - 6.0);
        dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius + OasisRing + 8.0)
        {
            return false;
        }

        anchor = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        return true;
    }

    /// <summary>The oasis grass ring at a column (paint) — between the pond edge and +6.</summary>
    private bool OasisRingAt(PlanetType planet, long seed, int worldX, int worldZ)
        => TryGetOasis(planet, seed, worldX, worldZ, out double dist, out double radius, out _) && dist >= radius && dist < radius + OasisRing;

    /// <summary>Whether a tree at this column stands in an oasis's palm fringe (ring + 2), for StampTrees.</summary>
    internal bool OasisPalmFringeAt(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.Oases && TryGetOasis(planet, w.Seed, worldX, worldZ, out double dist, out double radius, out _)
            && dist >= radius && dist < radius + OasisRing + 2.0;
    }

    // --- hot springs: 1–3 small steaming pools near a hotspot centre, mineral crust around them ---
    private const double HotSpringCellSize = 700.0;
    private const double HotSpringChance = 0.30;

    private bool TryGetHotSpring(long seed, int worldX, int worldZ, out double nearest, out double poolR, out ulong hash)
    {
        nearest = double.MaxValue;
        poolR = 0.0;
        if (!TryGetHotspot(seed ^ 0x407591, HotSpringCellSize, HotSpringChance, 20.0,
                worldX, worldZ, out hash, out double dx, out double dz))
        {
            return false;
        }

        int pools = 1 + (int)((hash >> 16) & 0x3) % 3; // 1..3
        for (int k = 0; k < pools; k++)
        {
            double ox = (((hash >> (20 + 8 * k)) & 0xFF) / 255.0 - 0.5) * 20.0;
            double oz = (((hash >> (44 + 6 * k)) & 0x3F) / 63.0 - 0.5) * 20.0;
            double d = System.Math.Sqrt((dx - ox) * (dx - ox) + (dz - oz) * (dz - oz));
            if (d < nearest)
            {
                nearest = d;
                poolR = 2.0 + ((hash >> (10 + 2 * k)) & 0x3) * 0.6; // 2..3.8
            }
        }

        return true;
    }

    private bool TryGetHotSpringPool(long seed, int worldX, int worldZ, out int depth)
    {
        depth = 0;
        if (!TryGetHotSpring(seed, worldX, worldZ, out double d, out double r, out _) || d >= r)
        {
            return false;
        }

        depth = d < r * 0.5 ? 2 : 1;
        return true;
    }

    /// <summary>The mineral crust ring around a hot spring (paint).</summary>
    private bool HotSpringCrustAt(long seed, int worldX, int worldZ)
        => TryGetHotSpring(seed, worldX, worldZ, out double d, out double r, out _) && d >= r && d < r + 2.0;

    // --- caldera lake: the ring caldera's interior fills to 60 % of its basin depth ---
    private bool TryGetCalderaLake(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, out int lakeTop)
    {
        lakeTop = 0;
        if (!TryGetHotspot(seed ^ 0x0CA1DE7A, CalderaCellSize, CalderaChance, CalderaMaxRadius + 30.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 200.0 + ((h >> 16) & 0x3FF) / 1023.0 * (CalderaMaxRadius - 200.0); // the caldera's own rolls
        double basin = 20.0 + ((h >> 56) & 0xFF) / 255.0 * 20.0;
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist / radius >= 0.70)
        {
            return false;
        }

        int centreRaw = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        lakeTop = centreRaw - (int)System.Math.Round(basin * 0.4);
        return surfaceY < lakeTop;
    }

    // --- shield lake: the shield volcano's summit bowl holds lava (or water) 3 deep ---
    private bool TryGetShieldLake(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, out int lakeTop)
    {
        lakeTop = 0;
        if (!TryGetHotspot(seed ^ 0x5A1E1D02, ShieldCellSize, ShieldChance, ShieldMaxRadius + 20.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 200.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ShieldMaxRadius - 200.0);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius * 0.22)
        {
            return false;
        }

        int centreY = SurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        lakeTop = centreY + 3; // the bowl centre is 6 under the rim: fill the lower half
        return surfaceY < lakeTop;
    }

    // --- maar lake: the explosion bowl fills to 60 % of its depth ---
    private bool TryGetMaarLake(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, out int lakeTop)
    {
        lakeTop = 0;
        if (!TryGetHotspot(seed ^ 0x3AA201, MaarCellSize, MaarChance, MaarMaxRadius * 1.15 + 6.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 20.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MaarMaxRadius - 20.0);
        double depth = 8.0 + ((h >> 26) & 0x3FF) / 1023.0 * 6.0;
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius)
        {
            return false;
        }

        int centreRaw = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        lakeTop = centreRaw - (int)System.Math.Round(depth * 0.4);
        return surfaceY < lakeTop;
    }

    // --- playa / salt lake: a flat salt pan on desert flats, its wettest tenth a film of water ---
    private const double PlayaCellSize = 1500.0;
    private const double PlayaChance = 0.30;
    private const double PlayaMaxRadius = 90.0;

    private bool TryGetPlaya(PlanetType planet, long seed, int worldX, int worldZ, out bool wet)
    {
        wet = false;
        if (!TryGetHotspot(seed ^ 0x91A7A, PlayaCellSize, PlayaChance, PlayaMaxRadius + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (PlayaMaxRadius - 40.0);
        if (dx * dx + dz * dz >= radius * radius || SurfaceSlope(planet, worldX, worldZ) > 2)
        {
            return false;
        }

        wet = FbmT(seed + 0x91A7B, worldX, worldZ, 20.0, octaves: 2) > 0.90;
        return true;
    }

    // --- tarn: the glacial trough's deep end holds a lake ---
    private bool TryGetTarn(PlanetType planet, long seed, int worldX, int worldZ, int surfaceY, out int tarnTop)
    {
        tarnTop = 0;
        if (!TryGetHotspot(seed ^ 0x61AC1A1, TroughCellSize, TroughChance, TroughMaxHalfLen + TroughMaxHalfWidth + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 200.0 + ((h >> 26) & 0x3FF) / 1023.0 * (TroughMaxHalfLen - 200.0);
        double halfWidth = 25.0 + ((h >> 56) & 0xFF) / 255.0 * (TroughMaxHalfWidth - 25.0);
        double along = dx * System.Math.Cos(angle) + dz * System.Math.Sin(angle);
        double across = -dx * System.Math.Sin(angle) + dz * System.Math.Cos(angle);
        if (along < halfLen * 0.5 || System.Math.Abs(along) > halfLen || System.Math.Abs(across) > halfWidth * 0.8)
        {
            return false;
        }

        ulong h2 = h * 0x9E3779B97F4A7C15UL;
        double depth = 25.0 + ((h2 >> 20) & 0x3FF) / 1023.0 * 20.0;
        int centreRaw = RawSurfaceHeight(planet, worldX - (int)System.Math.Round(dx), worldZ - (int)System.Math.Round(dz));
        tarnTop = centreRaw - (int)System.Math.Round(depth * 0.55);
        return surfaceY < tarnTop;
    }

    // ---------------- surface paints (generation ≥ 1) ----------------

    /// <summary>The generation-1 surface repaint for a dry land column (null = keep), run after the classic
    /// beach / snow paints: marsh mud, oasis grass ring, hot-spring crust, playa salt, then scree on steep
    /// slopes, ash fall around volcano cones, dry riverbeds, deck banding, soil patches, moss stone.</summary>
    private BlockId? Gen1SurfacePaint(PlanetType planet, WonderProfile w, ColumnContext c, int worldX, int worldZ,
        int surfaceY, BlockId biomeSurface, out BlockId? subSurface)
    {
        subSurface = null;
        long seed = w.Seed;

        if (w.Marshes && MarshMudAt(planet, w, worldX, worldZ) && !c.MudId.IsAir)
        {
            subSurface = c.MudId;
            return c.MudId;
        }

        if (w.Oases && OasisRingAt(planet, seed, worldX, worldZ) && !c.GrassId.IsAir)
        {
            subSurface = c.DirtId.IsAir ? c.GrassId : c.DirtId;
            return c.GrassId;
        }

        if (w.HotSprings && HotSpringCrustAt(seed, worldX, worldZ) && !c.BasaltId.IsAir)
        {
            return c.BasaltId;
        }

        if (w.Playas && TryGetPlaya(planet, seed, worldX, worldZ, out _) && !c.SaltBlockId.IsAir)
        {
            subSurface = c.SandId.IsAir ? c.SaltBlockId : c.SandId;
            return c.SaltBlockId;
        }

        // Scree and bare rock on steep slopes (the slope of the final heights, four memoised neighbours).
        int slope = SurfaceSlope(planet, worldX, worldZ);
        if (slope > 10)
        {
            subSurface = c.DeepId;
            return c.DeepId;
        }

        if (slope > 6 && !c.ScreeId.IsAir)
        {
            subSurface = c.ScreeId;
            return c.ScreeId;
        }

        // Ash fall around volcano cones: a fading skirt from the cone foot out to 1.5 radii.
        if (c.VolcanoWorld && !c.AshId.IsAir && AshFallAt(planet, seed, worldX, worldZ))
        {
            return c.AshId;
        }

        // Dry riverbeds on dry worlds: winding channels of scree (rock ground) or sand.
        if (w.DryBeds && DryBedAt(w, worldX, worldZ))
        {
            var bed = biomeSurface == c.StoneId || biomeSurface == c.GraniteId ? c.ScreeId : c.SandId;
            if (!bed.IsAir)
            {
                subSurface = bed;
                return bed;
            }
        }

        // Deck banding on mesa / tablelands / terraces worlds: every third deck reads granite or sandstone.
        if (w.DeckBands)
        {
            int deck = (int)System.Math.Floor((surfaceY - planet.BaseHeight) / 6.0);
            int band = ((deck % 3) + 3) % 3;
            if (band == 1 && !c.SandstoneId.IsAir)
            {
                subSurface = c.SandstoneId;
                return c.SandstoneId;
            }

            if (band == 2 && !c.GraniteId.IsAir && biomeSurface != c.SandId)
            {
                subSurface = c.GraniteId;
                return c.GraniteId;
            }
        }

        // Soil patches: bare dirt patches break up grass biomes.
        if (biomeSurface == c.GrassId && !c.DirtId.IsAir && FbmT(seed + 0x5011, worldX, worldZ, 28.0, octaves: 2) > 0.68)
        {
            return c.DirtId;
        }

        // Moss stone on the rock of temperate wet worlds.
        if (w.Moss && !c.MossStoneId.IsAir && (biomeSurface == c.StoneId || biomeSurface == c.GraniteId)
            && FbmT(seed + 0x3055, worldX, worldZ, 16.0, octaves: 2) > 0.5)
        {
            return c.MossStoneId;
        }

        return null;
    }

    private bool AshFallAt(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetVolcanoSkirt(planet, seed, worldX, worldZ, out double t))
        {
            return false;
        }

        return t > FbmT(seed + 0xA5F, worldX, worldZ, 11.0, octaves: 2);
    }

    /// <summary>1 at the cone foot fading to 0 at 1.5 radii — the cone itself (dist &lt; radius) is not a skirt.</summary>
    private bool TryGetVolcanoSkirt(PlanetType planet, long seed, int worldX, int worldZ, out double t)
    {
        t = 0.0;
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / VolcanoCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / VolcanoCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period;
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));
        if (!TryGetVolcanoInCell(planet, seed, cxI, czI, cw, ch, period, out var cone))
        {
            return false;
        }

        double dx = WorldConstants.WrapDeltaX(wx - cone.CenterX, _circumference);
        double dz = zc - (cone.CenterZ + period / 2);
        if (dz > period / 2.0) dz -= period;
        if (dz < -period / 2.0) dz += period;
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist < cone.Radius || dist >= cone.Radius * 1.5)
        {
            return false;
        }

        t = 1.0 - (dist - cone.Radius) / (cone.Radius * 0.5);
        return true;
    }

    private bool DryBedAt(WonderProfile w, int worldX, int worldZ)
    {
        double n = FbmT(w.Seed + 0xD2B01, worldX, worldZ, w.Scale * 1.2, octaves: 3);
        return 1.0 - System.Math.Abs(2.0 * n - 1.0) > 0.93;
    }

    // ---------------- test hooks ----------------

    /// <summary>The generation-1 body at a column, if any (tests): (top, bed, fluid key).</summary>
    internal (int Top, int Bed, string Fluid)? Gen1WaterForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        int surfaceY = SurfaceHeight(planet, worldX, worldZ);
        if (!TryGetGen1Water(planet, w, worldX, worldZ, surfaceY, out int top, out int bed, out var fluid))
        {
            return null;
        }

        string key = _content.GetBlock("lava")?.NumericId == fluid ? "lava" : "water";
        return (top, bed, key);
    }

    /// <summary>Whether a column lies in a marsh region / oasis ring / playa (tests).</summary>
    internal (bool Marsh, bool OasisRing, bool Playa) Gen1PaintRegionsForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return (w.Marshes && MarshRegionAt(w, worldX, worldZ),
            w.Oases && OasisRingAt(planet, w.Seed, worldX, worldZ),
            w.Playas && TryGetPlaya(planet, w.Seed, worldX, worldZ, out _));
    }
}
