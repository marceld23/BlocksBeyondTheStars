// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>hotspot-cell landforms: volcanoes, massifs, table mountains, rifts, calderas, escarpments, exotic accents (partial of <see cref="WorldGenerator"/>, split from the single file by seam).</summary>
public sealed partial class WorldGenerator
{
    // --- Volcanoes (#477, decision #6): watery worlds grow sparse basalt cones with a molten summit
    // crater — lava on worlds whose seas are water. The cone lives INSIDE SurfaceHeight so every consumer
    // sees the same mountain; the crater's lava pool is a per-column fluid override in Generate, the same
    // mechanism ponds and rivers already use. Seam-safe by construction: one hotspot cell grid over the
    // torus, centres kept a full cone radius inside their cell so no cone ever straddles a wrap seam. ---
    private const double VolcanoCellSize = 1280.0; // hotspot grid pitch (≈2–5 candidate cells on a default world)
    private const double VolcanoChance = 0.55;     // fraction of hotspot cells that actually grow a cone

    /// <summary>Volcanoes grow on every world with a lava core (#1631, decision 2026-09-05): any body whose
    /// world floor ends in the molten band — that is every non-cratered body (the cratered airless moons
    /// and asteroids bottom out in basalt and are geologically dead). Void worlds (ship interiors, station
    /// decks) and skylands stay out. Until #1631 only watery, breathable worlds qualified (#477).</summary>
    private bool HasVolcanoes(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        if (_lavaCoreVolcanoes)
        {
            return true; // #1631: every body with a molten core
        }

        // Legacy rule (#477) for saves created before #1631: watery, breathable-atmosphere worlds only.
        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return hasAir && waterAb > 0.0;
    }

    /// <summary>Sea-mount volcanoes (#1631): a cone whose centre lies under the sea is lifted so its crater
    /// rim clears the water by this much (seeded 12–36 blocks) — a volcanic island instead of a drowned
    /// bump. The rolled radius may grow by up to <see cref="SeaMountRadiusGrow"/>, which is exactly the
    /// placement margin every centre keeps from its cell border, so seam safety is untouched.</summary>
    private const double SeaMountClearanceMin = 12.0;
    private const double SeaMountClearanceRange = 24.0;
    private const double SeaMountRadiusGrow = 24.0;

    /// <summary>The cone profile's share of its height at the crater rim: t = 1 − CraterR/Radius = 0.84 for
    /// every radius ≥ 25, and 0.84^1.6 (see <see cref="ConeOffsetOf"/>).</summary>
    private const double ConeRimShare = 0.7566;

    /// <summary>True while <see cref="BuildCalibration"/> samples the world on this thread: the sea level is
    /// not known yet, so cones keep their rolled height for the sample (the sea percentile barely notices a
    /// few cones) and the column memos filled meanwhile are dropped once the calibration exists.</summary>
    [System.ThreadStatic]
    private static bool _calibrating;

    /// <summary>Cones memoised per hotspot cell (#1631): the sea-mount lift costs a sea-level lookup and one
    /// raw-height sample per cell, which must not be paid per column. Never filled while calibrating.</summary>
    private readonly System.Collections.Generic.Dictionary<(string, long, int, int, int), (bool Has, VolcanoCone Cone)> _volcanoCells = new();
    private readonly object _volcanoLock = new();

    private readonly struct VolcanoCone
    {
        public readonly int CenterX;
        public readonly int CenterZ;
        public readonly double Radius;
        public readonly double Height;
        public readonly double CraterR;
        public readonly double CraterDepth;

        public VolcanoCone(int cx, int cz, double radius, double height)
        {
            CenterX = cx;
            CenterZ = cz;
            Radius = radius;
            Height = height;
            CraterR = System.Math.Max(4.0, radius * 0.16);
            CraterDepth = height * 0.55 + 4.0;
        }
    }

    /// <summary>The volcano cone covering (worldX, worldZ), if any — with the distance to its centre.
    /// Deterministic hotspot-cell lookup; a centre never sits closer than its radius to a cell border, so
    /// checking the containing cell alone is complete (and the cone can never straddle a wrap seam).</summary>
    private bool TryGetVolcano(PlanetType planet, long seed, int worldX, int worldZ,
        out VolcanoCone cone, out double dist)
    {
        cone = default;
        dist = 0.0;
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / VolcanoCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / VolcanoCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;

        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period; // canonical [0, period)
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));

        if (!TryGetVolcanoInCell(planet, seed, cxI, czI, cw, ch, period, out cone))
        {
            return false; // this hotspot cell grew no volcano
        }

        double dx = WorldConstants.WrapDeltaX(wx - cone.CenterX, _circumference);
        double dz = zc - (cone.CenterZ + period / 2);
        if (dz > period / 2.0) dz -= period;
        if (dz < -period / 2.0) dz += period;
        dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > cone.Radius)
        {
            cone = default;
            return false;
        }

        return true;
    }

    /// <summary>The cone a hotspot cell grew, if any — position, radius and height from the cell hash, plus
    /// the sea-mount lift (#1631) when the centre lies under the sea. Memoised per cell outside calibration.</summary>
    private bool TryGetVolcanoInCell(PlanetType planet, long seed, int cxI, int czI, double cw, double ch, int period, out VolcanoCone cone)
    {
        var key = (planet.Key, _locationSalt, _circumference, cxI, czI); // the flags reset the memo via InvalidateColumnCaches
        bool memo = !_calibrating;
        if (memo)
        {
            lock (_volcanoLock)
            {
                if (_volcanoCells.TryGetValue(key, out var hit))
                {
                    cone = hit.Cone;
                    return hit.Has;
                }
            }
        }

        cone = default;
        bool has = false;
        ulong h = Noise.Hash(seed ^ 0x70C4A0, cxI, 0, czI);
        if ((h & 0xFFFF) / 65536.0 < VolcanoChance)
        {
            double radius = 34.0 + ((h >> 16) & 0x3FF) / 1023.0 * 26.0; // 34..60
            double height = 24.0 + ((h >> 26) & 0x3FF) / 1023.0 * 22.0; // 24..46
            double margin = radius + SeaMountRadiusGrow;
            double ox = margin + ((h >> 36) & 0x3FF) / 1023.0 * System.Math.Max(1.0, cw - 2.0 * margin);
            double oz = margin + ((h >> 46) & 0x3FF) / 1023.0 * System.Math.Max(1.0, ch - 2.0 * margin);
            int centerX = (int)(cxI * cw + ox);
            int centerZ = (int)(czI * ch + oz) - period / 2;

            // Sea-mount (#1631): a centre under the sea lifts the cone until the crater rim clears the water,
            // and widens the base (never beyond the placement margin) so the island is a mountain, not a spike.
            if (_lavaCoreVolcanoes && !_calibrating)
            {
                int sea = SeaLevel(planet);
                if (sea != int.MinValue)
                {
                    int raw = RawSurfaceHeight(planet, centerX, centerZ);
                    if (raw < sea)
                    {
                        double clear = SeaMountClearanceMin + ((h >> 56) & 0xFF) / 255.0 * SeaMountClearanceRange;
                        double need = (sea - raw + clear) / ConeRimShare;
                        if (need > height)
                        {
                            height = need;
                            radius = System.Math.Min(radius + SeaMountRadiusGrow, System.Math.Max(radius, height * 0.9));
                        }
                    }
                }
            }

            cone = new VolcanoCone(centerX, centerZ, radius, height);
            has = true;
        }

        if (memo)
        {
            lock (_volcanoLock)
            {
                if (_volcanoCells.Count >= 4096)
                {
                    _volcanoCells.Clear();
                }

                _volcanoCells[key] = (has, cone);
            }
        }

        return has;
    }

    /// <summary>Test hook (#1631): every volcano of the configured body — centre, radius, height, the crater
    /// rim's surface height and whether the cone was lifted out of a sea.</summary>
    public System.Collections.Generic.List<(int X, int Z, double Radius, double Height, int RimY, bool SeaMount)> VolcanoesForTest(PlanetType planet)
    {
        var list = new System.Collections.Generic.List<(int, int, double, double, int, bool)>();
        if (!HasVolcanoes(planet))
        {
            return list;
        }

        long seed = PlanetSeed(planet);
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / VolcanoCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / VolcanoCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        int sea = SeaLevel(planet);
        for (int cz = 0; cz < nz; cz++)
            for (int cx = 0; cx < nx; cx++)
            {
                if (TryGetVolcanoInCell(planet, seed, cx, cz, cw, ch, period, out var v))
                {
                    int rimY = SurfaceHeight(planet, v.CenterX + (int)System.Math.Round(v.CraterR), v.CenterZ);
                    bool seaMount = sea != int.MinValue && RawSurfaceHeight(planet, v.CenterX, v.CenterZ) < sea;
                    list.Add((v.CenterX, v.CenterZ, v.Radius, v.Height, rimY, seaMount));
                }
            }

        return list;
    }

    /// <summary>The cone's height contribution at a distance from its centre: a smooth basalt slope rising
    /// to the rim, with the summit crater carved back down toward the vent.</summary>
    private static double ConeOffsetOf(in VolcanoCone v, double dist)
    {
        double t = 1.0 - dist / v.Radius;
        double cone = v.Height * System.Math.Pow(t, 1.6);
        if (dist < v.CraterR)
        {
            double bt = (v.CraterR - dist) / v.CraterR;
            cone -= v.CraterDepth * (bt * bt * (3.0 - 2.0 * bt)); // smoothstep bowl down to the vent
        }

        return cone;
    }

    private double VolcanoOffset(PlanetType planet, long seed, int worldX, int worldZ)
        => TryGetVolcano(planet, seed, worldX, worldZ, out var v, out double dist) ? ConeOffsetOf(v, dist) : 0.0;

    /// <summary>The Y of the crater pool's topmost lava cell — anchored to the pre-cone ground under the
    /// cone's centre so the pool is flat regardless of how the base terrain undulates below the flanks.</summary>
    private int CraterLavaTop(PlanetType planet, in VolcanoCone v)
        => RawSurfaceHeight(planet, v.CenterX, v.CenterZ)
           + (int)System.Math.Round(v.Height - v.CraterDepth + v.CraterDepth * 0.45);

    /// <summary>True when this column lies inside a volcano's summit crater; outputs the molten pool's top
    /// cell Y. Shared by Generate and the placement/water helpers so they can never disagree (#477).</summary>
    public bool TryGetVolcanoCrater(PlanetType planet, int worldX, int worldZ, out int lavaTopY)
    {
        lavaTopY = 0;
        if (!HasVolcanoes(planet))
        {
            return false;
        }

        long seed = PlanetSeed(planet);
        if (!TryGetVolcano(planet, seed, worldX, worldZ, out var v, out double dist) || dist >= v.CraterR - 0.5)
        {
            return false;
        }

        lavaTopY = CraterLavaTop(planet, v);
        return true;
    }

    // --- Landmark landforms (#577/#578): table mountains, massifs and rift chasms — sparse discrete
    // features on the #477 volcano hotspot-cell recipe: one deterministic candidate per cell, the centre
    // kept a full feature extent inside its cell, so no landmark ever straddles a wrap seam and checking
    // the containing cell alone is complete. All are pure functions of the body seed → O(1) per column. ---

    /// <summary>Shared hotspot-cell lookup for the landmark landforms: resolves whether the cell containing
    /// (worldX, worldZ) hosts a feature centre and, if so, outputs the per-cell hash (feature rolls come
    /// from its bits) plus the torus-wrapped offset (dx, dz) from the centre to the queried column. The
    /// centre never sits closer than <paramref name="margin"/> to a cell border — pass the WORST-CASE
    /// feature extent so the seam-safety argument holds for every roll.</summary>
    private bool TryGetHotspot(long salt, double cellSize, double chance, double margin,
        int worldX, int worldZ, out ulong hash, out double dx, out double dz)
    {
        dx = 0.0;
        dz = 0.0;
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / cellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / cellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;

        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period; // canonical [0, period)
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));

        hash = Noise.Hash(salt, cxI, 0, czI);
        if ((hash & 0xFFFF) / 65536.0 >= chance)
        {
            return false; // this cell grew no feature
        }

        double ox = margin + ((hash >> 36) & 0x3FF) / 1023.0 * System.Math.Max(1.0, cw - 2.0 * margin);
        double oz = margin + ((hash >> 46) & 0x3FF) / 1023.0 * System.Math.Max(1.0, ch - 2.0 * margin);
        dx = WorldConstants.WrapDeltaX(wx - (int)(cxI * cw + ox), _circumference);
        dz = zc - (int)(czI * ch + oz);
        if (dz > period / 2.0)
        {
            dz -= period;
        }

        if (dz < -period / 2.0)
        {
            dz += period;
        }

        return true;
    }

    private const double ButteCellSize = 1600.0;  // hotspot pitch (≈8 candidate cells on a default world)
    private const double ButteChance = 0.40;      // fraction of cells that grow a table mountain
    private const double ButteMaxRadius = 120.0;

    /// <summary>Table mountains grow on dry, rocky-reading worlds (#577) — dune/mesa/canyon-style terrain
    /// plus the savanna — never on airless bodies, sky worlds or void interiors.</summary>
    private bool HasTableMountains(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        // #1644: the `buttes` tag (data) replaces the former style list (dunes/mesa/canyons/tablelands/badlands)
        // plus the savanna/varied key exceptions — the tagged types are exactly those.
        return planet.HasTag(TerrainTag.Buttes);
    }

    /// <summary>The table mountain's height contribution at a column, or 0 if none covers it (#577): a
    /// talus foot steepening into a near-vertical upper wall (outer 30 % of the radius), then a dead-flat
    /// cap with a light rock roll so the top reads as stone, not glass.</summary>
    private double TableMountainOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x7AB1E0, ButteCellSize, ButteChance, ButteMaxRadius + 20.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ButteMaxRadius - 40.0); // 40..120
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return 0.0;
        }

        double height = 30.0 + ((h >> 26) & 0x3FF) / 1023.0 * 40.0; // 30..70
        double t = 1.0 - dist / radius;
        if (t >= 0.30)
        {
            double roll = FbmT(seed + 0x7AB2E, worldX, worldZ, 24.0, octaves: 2);
            return height + (roll - 0.5) * 2.0; // the table top
        }

        return height * System.Math.Pow(t / 0.30, 1.8); // talus foot → near-vertical upper wall
    }

    private const double MassifCellSize = 3200.0; // very sparse: ~1 in 5 default worlds carries a massif
    private const double MassifChance = 0.10;     // decision "bold but varied": a massif is a FIND, not the norm
    private const double MassifMaxRadius = 300.0;

    /// <summary>Massifs — rare single giant mountains, visible from very far — grow on any solid-ground
    /// world with an atmosphere (#578); airless/cratered bodies are geologically dead, sky worlds stay
    /// floaty, void interiors have no terrain.</summary>
    private bool HasMassifs(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        return !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The massif's height contribution at a column, or 0 if none covers it (#578): a broad cone
    /// with ridged flanks (spurs + gullies from a mid-frequency field); the summit is capped at the rolled
    /// height so flank noise sculpts the sides, never the peak — and the roll itself is clamped so the
    /// summit stays under <see cref="MaxNaturalSurfaceY"/> with margin for the underlying swell.</summary>
    private double MassifOffset(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x3A551F, MassifCellSize, MassifChance, MassifMaxRadius + 20.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 150.0 + ((h >> 16) & 0x3FF) / 1023.0 * (MassifMaxRadius - 150.0); // 150..300
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return 0.0;
        }

        double height = 120.0 + ((h >> 26) & 0x3FF) / 1023.0 * 100.0; // 120..220
        height = System.Math.Min(height, MaxNaturalSurfaceY - 16.0 - planet.BaseHeight);

        double t = 1.0 - dist / radius;
        double flank = FbmT(seed + 0x3A552F, worldX, worldZ, 90.0, octaves: 2);
        return height * System.Math.Min(1.0, System.Math.Pow(t, 1.5) * (0.75 + 0.5 * flank));
    }

    private const double RiftCellSize = 2400.0;
    private const double RiftChance = 0.15; // ~1 in 4 worlds — a gorge is a discovery, not scenery
    private const double RiftMaxHalfLen = 500.0;
    private const double RiftMaxHalfWidth = 28.0;

    /// <summary>Rift chasms cut the same worlds massifs grow on (#578) — solid ground plus an atmosphere.
    /// Where the floor dips under the sea level the rift floods into a fjord lake for free (the sea fill
    /// is by level), and rivers crossing the rim drop in as waterfalls.</summary>
    private bool HasRifts(PlanetType planet) => HasMassifs(planet);

    /// <summary>The rift's (negative) height contribution at a column, or 0 if none covers it (#578): a
    /// straight gorge segment with steep walls dropping to a broad floor, tapered toward both ends so the
    /// chasm closes naturally instead of ending in a cliff face.</summary>
    private double RiftOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x21F7A9, RiftCellSize, RiftChance,
                RiftMaxHalfLen + RiftMaxHalfWidth + 16.0, worldX, worldZ,
                out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double angle = ((h >> 16) & 0x3FF) / 1023.0 * System.Math.PI;
        double halfLen = 260.0 + ((h >> 26) & 0x3FF) / 1023.0 * (RiftMaxHalfLen - 260.0); // 260..500
        double halfWidth = 14.0 + ((h >> 56) & 0xFF) / 255.0 * (RiftMaxHalfWidth - 14.0); // 14..28

        double cos = System.Math.Cos(angle);
        double sin = System.Math.Sin(angle);
        double along = dx * cos + dz * sin;
        double across = -dx * sin + dz * cos;
        if (System.Math.Abs(along) > halfLen || System.Math.Abs(across) > halfWidth)
        {
            return 0.0;
        }

        // Depth comes from a re-hash — the primary hash's roll bits are spent on placement + shape.
        ulong h2 = h * 0x9E3779B97F4A7C15UL;
        double depth = 50.0 + ((h2 >> 20) & 0x3FF) / 1023.0 * 80.0; // 50..130

        double Smooth(double v) => v * v * (3.0 - 2.0 * v);
        double w = 1.0 - System.Math.Abs(across) / halfWidth;         // 0 rim .. 1 axis
        double wall = w >= 0.45 ? 1.0 : Smooth(w / 0.45);             // walls in the outer 45 %, flat floor within
        double endT = 1.0 - System.Math.Abs(along) / halfLen;
        double taper = endT >= 0.15 ? 1.0 : Smooth(endT / 0.15);
        return -depth * wall * taper;
    }

    // --- Mega-rift (#698): ONE colossal canyon girdling the entire planet — the Valles-Marineris
    // signature scar. Not a hotspot cell: a meandering great-circle path at a rolled latitude. The meander
    // is a torus FBM of X alone, so the canyon closes seamlessly on itself after a full circumnavigation.
    // Very rare — a world-defining find, never the norm. ---
    private const double MegaRiftChance = 16 / 256.0;  // ~6 % of eligible worlds (visibility tuning 2026-08-03)
    private const double MegaRiftMeanderFrac = 0.10;   // meander amplitude as a fraction of the latitude period
    private const double MegaRiftMaxHalfWidth = 40.0;  // canyon up to ~80 wide

    /// <summary>True when this world carries the world-girdling canyon (#698) — same solid-ground +
    /// atmosphere gate as massifs, then a rare per-body roll.</summary>
    private bool HasMegaRift(PlanetType planet, long seed)
        => HasMassifs(planet) && (Noise.Hash(seed ^ 0x6E64A11F, 3, 1, 7) & 0xFF) < 256 * MegaRiftChance;

    /// <summary>The mega-rift's (negative) height contribution at a column (#698): steep smoothstep walls
    /// dropping 100–200 blocks to a broad flat floor. Wherever the floor dips under the sea percentile it
    /// floods into a chain of fjord lakes, and rivers crossing the rim fall in as waterfalls — both for
    /// free (the calibration samples the full <see cref="SurfaceHeight"/>).</summary>
    private double MegaRiftOffset(long seed, int worldX, int worldZ)
    {
        ulong h = Noise.Hash(seed ^ 0x6E64A120, 1, 2, 3);
        int period = LatPeriod;
        double z0 = ((((h >> 8) & 0x3FF) / 1023.0) - 0.5) * period; // rolled central latitude

        // Cheap band reject BEFORE the meander FBMs: columns far from the canyon's latitude band pay ~nothing.
        double band = period * MegaRiftMeanderFrac + MegaRiftMaxHalfWidth * 1.5;
        double dz0 = WorldConstants.WrapDeltaZ(worldZ - z0, _circumference);
        if (System.Math.Abs(dz0) > band)
        {
            return 0.0;
        }

        double meander = (FbmT(seed + 0x6E64A121, worldX, 0.0, _circumference / 6.0, octaves: 2) - 0.5)
            * 2.0 * (period * MegaRiftMeanderFrac);
        double halfWidth = 15.0 + ((h >> 18) & 0x3FF) / 1023.0 * (MegaRiftMaxHalfWidth - 15.0); // 15..40
        halfWidth *= 0.75 + FbmT(seed + 0x6E64A122, worldX, 64.0, _circumference / 12.0, octaves: 2) * 0.5;
        double depth = 100.0 + ((h >> 28) & 0x3FF) / 1023.0 * 100.0; // 100..200

        double dz = WorldConstants.WrapDeltaZ(worldZ - z0 - meander, _circumference);
        if (System.Math.Abs(dz) > halfWidth)
        {
            return 0.0;
        }

        double w = 1.0 - System.Math.Abs(dz) / halfWidth;             // 0 rim .. 1 axis
        double wall = w >= 0.45 ? 1.0 : w / 0.45 * (w / 0.45) * (3.0 - 2.0 * (w / 0.45));
        return -depth * wall;
    }

    // --- Ring caldera (#702): a colossal circular mountain wall around a sunken interior basin. Where the
    // basin dips under the sea percentile it floods into a ring lake (lava on dry volcanic worlds). ---
    private const double CalderaCellSize = 2400.0;
    private const double CalderaChance = 0.20;     // visibility tuning 2026-08-03: ~0.6 per start-size world
    private const double CalderaMaxRadius = 400.0;

    private bool HasCalderas(PlanetType planet) => HasMassifs(planet); // same solid-ground + air gate

    /// <summary>The ring caldera's height contribution at a column (#702): a smooth rim wall peaking at
    /// ~86 % of the radius, enclosing a gently dished sunken floor.</summary>
    private double CalderaOffset(long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x0CA1DE7A, CalderaCellSize, CalderaChance, CalderaMaxRadius + 30.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 200.0 + ((h >> 16) & 0x3FF) / 1023.0 * (CalderaMaxRadius - 200.0); // 200..400
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return 0.0;
        }

        double rimH = 40.0 + ((h >> 26) & 0x3FF) / 1023.0 * 30.0;   // 40..70 wall
        double basin = 20.0 + ((h >> 56) & 0xFF) / 255.0 * 20.0;    // 20..40 sunken interior
        double u = dist / radius;
        if (u >= 0.72)
        {
            double v = (u - 0.72) / 0.28;                 // 0 basin edge .. 1 outside foot
            double s = 1.0 - System.Math.Abs(2.0 * v - 1.0); // peak mid-band
            return rimH * (s * s * (3.0 - 2.0 * s));
        }

        return -basin * (1.0 - 0.25 * (u / 0.72)); // near-flat sunken floor, slightly dished toward the wall
    }

    // --- Whole-planet escarpment (#702): a rare great-circle step that splits the world into an upper and
    // a lower storey, separated by a continuous meandering cliff. Applied to the RAW height (before styles
    // and landmarks) so every landform simply rides on its storey. ---
    private const double EscarpmentChance = 14 / 256.0; // ~5.5 % of eligible worlds

    private bool HasEscarpment(PlanetType planet, long seed)
        => !planet.FloatingIslands && !planet.Void
           && (Noise.Hash(seed ^ 0x0E5CA29F, 9, 2, 5) & 0xFF) < 256 * EscarpmentChance;

    /// <summary>The escarpment's height contribution (#702): +step/2 on the upper storey, −step/2 on the
    /// lower, blended across a cliff band whose path meanders like the mega-rift's.</summary>
    private double EscarpmentOffset(long seed, int worldX, int worldZ)
    {
        ulong h = Noise.Hash(seed ^ 0x0E5CA2A0, 4, 4, 4);
        int period = LatPeriod;
        double z0 = ((((h >> 8) & 0x3FF) / 1023.0) - 0.5) * period;
        double step = 60.0 + ((h >> 18) & 0x3FF) / 1023.0 * 40.0;   // 60..100 storey separation
        double blend = 24.0 + ((h >> 28) & 0x3FF) / 1023.0 * 24.0;  // 24..48 cliff band width

        double dz0 = WorldConstants.WrapDeltaZ(worldZ - z0, _circumference);
        double reach = period * MegaRiftMeanderFrac + blend;
        if (System.Math.Abs(dz0) > reach)
        {
            return dz0 > 0 ? step * 0.5 : -step * 0.5; // far from the cliff: plain storey offset
        }

        double meander = (FbmT(seed + 0x0E5CA2A1, worldX, 0.0, _circumference / 6.0, octaves: 2) - 0.5)
            * 2.0 * (period * MegaRiftMeanderFrac);
        double dz = WorldConstants.WrapDeltaZ(worldZ - z0 - meander, _circumference);
        double t = System.Math.Clamp(dz / blend * 0.5 + 0.5, 0.0, 1.0);
        return (t * t * (3.0 - 2.0 * t) - 0.5) * step;
    }

    // --- Travertine spring terraces (#701): Pamukkale — blinding-white stepped decks cascading down a
    // gentle mound, each deck holding a shallow pool. Warm wet worlds only. ---
    private const double TravertineCellSize = 1000.0;
    private const double TravertineChance = 0.45;  // visibility tuning 2026-08-03
    private const double TravertineMaxRadius = 60.0;

    private bool HasTravertine(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        bool hasAir = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
        return hasAir && waterAb >= 0.4 && planet.BaseTemperature >= 5.0;
    }

    /// <summary>The travertine mound's terraced height contribution (#701) plus whether this column is a
    /// deck POOL cell (a 1-deep water pool sitting flush on the deck). White repaint is Generate's job.</summary>
    private bool TryGetTravertine(long seed, int worldX, int worldZ, out double deckRise, out bool pool)
    {
        deckRise = 0.0;
        pool = false;
        if (!TryGetHotspot(seed ^ 0x7AA7E271, TravertineCellSize, TravertineChance, TravertineMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 30.0 + ((h >> 16) & 0x3FF) / 1023.0 * (TravertineMaxRadius - 30.0); // 30..60
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return false;
        }

        double height = 10.0 + ((h >> 26) & 0x3FF) / 1023.0 * 8.0; // 10..18 mound
        double u = 1.0 - dist / radius;
        double rise = height * System.Math.Pow(u, 1.35);
        const double deckStep = 2.0;
        double deck = System.Math.Floor(rise / deckStep) * deckStep;
        deckRise = deck;
        pool = rise - deck > 1.2 && dist < radius * 0.9; // deck interiors pond up; the outer fringe stays dry
        return true;
    }

    // --- Penitente fields (#701): dense blade-like ice spikes in patches on cold worlds. ---
    private const double PenitenteCellSize = 900.0;
    private const double PenitenteChance = 0.50;   // visibility tuning 2026-08-03
    private const double PenitenteMaxRadius = 120.0;

    private bool HasPenitentes(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        return !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase)
            && planet.BaseTemperature <= -5.0;
    }

    /// <summary>The penitente field's spike rise at a column (#701), 0 outside any field. Spikes are a
    /// high-frequency masked field inside sparse patch cells; Generate repaints tall spikes as ice.</summary>
    private double PenitenteRise(PlanetType planet, long seed, int worldX, int worldZ)
    {
        if (!TryGetHotspot(seed ^ 0x9E217E27, PenitenteCellSize, PenitenteChance, PenitenteMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double radius = 50.0 + ((h >> 16) & 0x3FF) / 1023.0 * (PenitenteMaxRadius - 50.0); // 50..120
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return 0.0;
        }

        double sp = FbmT(seed + 0x9E217E28, worldX, worldZ, 7.0, octaves: 2);
        if (sp <= 0.60)
        {
            return 0.0;
        }

        double t = (sp - 0.60) / 0.40;
        double edge = System.Math.Min(1.0, (1.0 - dist / radius) / 0.2);
        return t * t * 5.0 * (edge * edge * (3.0 - 2.0 * edge)); // blades up to ~5 tall, fading at the field edge
    }

    // --- Salt polygons (#701): giant cracked plates on salt pans — a Voronoi edge network raised one
    // block, so the flats read as tessellated crust instead of a billiard table. Trig-free arithmetic. ---
    private const double SaltPolyCell = 26.0;   // plate pitch (blocks)
    private const double SaltPolyEdge = 1.7;    // |d2−d1| below this ⇒ on a plate boundary ridge

    private static bool HasSaltPolygons(PlanetType planet)
        => planet.HasTag(TerrainTag.Salt); // #1644: data tag instead of the salt_flats key / salt beach check

    /// <summary>+1 on the polygon boundary ridges of a salt pan, 0 on the plate interiors (#701). Seam-safe:
    /// the Voronoi cell grid is modular over the torus in both axes.</summary>
    private double SaltPolygonRidge(long seed, int worldX, int worldZ)
    {
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / SaltPolyCell));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / SaltPolyCell));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period;
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));

        double d1 = double.MaxValue, d2 = double.MaxValue;
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                int gx = ((cxI + ix) % nx + nx) % nx;
                int gz = ((czI + iz) % nz + nz) % nz;
                ulong ph = Noise.Hash(seed ^ 0x5A17F1A7, gx, 0, gz);
                double px = (cxI + ix + 0.15 + ((ph >> 8) & 0x3FF) / 1023.0 * 0.7) * cw;
                double pz = (czI + iz + 0.15 + ((ph >> 20) & 0x3FF) / 1023.0 * 0.7) * ch;
                double ddx = WorldConstants.WrapDeltaX(wx - px, _circumference);
                double ddz = zc - pz;
                if (ddz > period / 2.0) { ddz -= period; }
                if (ddz < -period / 2.0) { ddz += period; }
                double d = System.Math.Sqrt(ddx * ddx + ddz * ddz);
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) { d2 = d; }
            }

        return d2 - d1 < SaltPolyEdge ? 1.0 : 0.0;
    }

    // --- Hexagonal basalt column fields (#701): Giant's-Causeway patches on volcanic-reading worlds —
    // stepped hex prisms quantised over a local axial hex grid, repainted basalt by Generate. ---
    private const double BasaltFieldCellSize = 1000.0;
    private const double BasaltFieldChance = 0.40; // visibility tuning 2026-08-03
    private const double BasaltFieldMaxRadius = 140.0;
    private const double Sqrt3Over3 = 0.5773502691896258; // compile-time constant — no libm at runtime

    private bool HasBasaltFields(PlanetType planet)
    {
        if (planet.Void || planet.Cratered || _crateredWorld || planet.FloatingIslands)
        {
            return false;
        }

        return planet.HasTag(TerrainTag.Volcanic); // #1644: data tag instead of the lava/ashen key check
    }

    /// <summary>The basalt column field's stepped height contribution at a column (#701), and whether the
    /// column lies inside a field at all (Generate repaints those basalt). Hex axial rounding on the
    /// feature-LOCAL offset (dx,dz), so the grid needs no torus treatment of its own.</summary>
    private bool TryGetBasaltColumns(long seed, int worldX, int worldZ, out double stepRise)
    {
        stepRise = 0.0;
        if (!TryGetHotspot(seed ^ 0xBA5A17C0, BasaltFieldCellSize, BasaltFieldChance, BasaltFieldMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return false;
        }

        double radius = 60.0 + ((h >> 16) & 0x3FF) / 1023.0 * (BasaltFieldMaxRadius - 60.0); // 60..140
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist > radius)
        {
            return false;
        }

        double s = 4.0 + ((h >> 26) & 0x3) * 1.0; // hex pitch 4..7
        // Pointy-top axial coordinates of the LOCAL offset, cube-rounded to the containing hex.
        double qf = (Sqrt3Over3 * dx - dz / 3.0) / s;
        double rf = (2.0 / 3.0 * dz) / s;
        double xf = qf, zf = rf, yf = -xf - zf;
        double rx = System.Math.Round(xf), ry = System.Math.Round(yf), rz = System.Math.Round(zf);
        double ddx = System.Math.Abs(rx - xf), ddy = System.Math.Abs(ry - yf), ddz = System.Math.Abs(rz - zf);
        if (ddx > ddy && ddx > ddz) { rx = -ry - rz; }
        else if (ddy <= ddz) { rz = -rx - ry; }

        ulong hexHash = Noise.Hash(seed ^ 0xBA5A17C1, (int)rx, 0, (int)rz);
        double level = ((int)(hexHash & 0x7) - 3) * 1.2; // −3.6..+4.8 in ~1.2 steps
        double edge = System.Math.Min(1.0, (1.0 - dist / radius) / 0.15);
        stepRise = level * (edge * edge * (3.0 - 2.0 * edge));
        return true;
    }
}
