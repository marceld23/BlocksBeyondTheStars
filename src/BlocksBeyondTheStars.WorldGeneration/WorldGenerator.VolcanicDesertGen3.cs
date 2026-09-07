// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Terrain generation 3, part 4 — the volcanic and desert landforms: obsidian fields (a paint), lava
/// flows (tongues from a cone foot: an offset row, a basalt paint and 1-deep lava pockets through the body
/// chain), barchans (a field of crescent dunes, an offset row) and frost polygons (the salt-polygon net on
/// tundra ground: a 1-high ridge offset, a stone paint on the ridge and ice-covered ponds in a fifth of the
/// plates). Every family gates on a profile flag <c>WonderFor</c> only sets from generation 3. All geometry is
/// trig-free (directions are hash-drawn integer vectors normalised by a square root) so the Windows and Linux
/// goldens agree.</summary>
public sealed partial class WorldGenerator
{
    // ================= Obsidian fields =================
    private const double ObsidianCellSize = 700.0;
    private const double ObsidianChance = 0.45;
    private const double ObsidianMaxRadius = 90.0;
    private const long ObsidianSalt = 0x0B51D1A;
    private const long ObsidianEdgeSalt = 0x0B51D1B;
    private const long ObsidianGlintSalt = 0x0B51D1C;

    /// <summary>Dry volcanic country with air: a lava or ashen world, never a void / cratered body.</summary>
    private bool HasObsidianFields(PlanetType planet)
        => !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands && HasAir(planet)
           && planet.HasTag(TerrainTag.Volcanic) && WaterAbundanceOf(planet) <= 0.3;

    /// <summary>Obsidian three deep inside a ragged-edged region 40–90 across on flat-ish ground, with a
    /// dithered 15 % of crystal glints on the surface (topsoil only — a stud, not a vein).</summary>
    private BlockId? ObsidianFieldPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!TryGetHotspot(w.Seed ^ ObsidianSalt, ObsidianCellSize, ObsidianChance, ObsidianMaxRadius + 8.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return null;
        }

        double radius = 40.0 + ((h >> 16) & 0x3FF) / 1023.0 * (ObsidianMaxRadius - 40.0);
        double edge = FbmT(w.Seed + ObsidianEdgeSalt, worldX, worldZ, 30.0, octaves: 2);
        double dist = System.Math.Sqrt(dx * dx + dz * dz);
        if (dist >= radius * (0.75 + 0.35 * edge) || SurfaceSlope(planet, worldX, worldZ) > 3)
        {
            return null;
        }

        var obsidian = _content.GetBlock("obsidian")?.NumericId ?? BlockId.Air;
        if (obsidian.IsAir)
        {
            return null;
        }

        var crystal = _content.GetBlock("crystal")?.NumericId ?? BlockId.Air;
        if (!crystal.IsAir && Noise.Value01(w.Seed + ObsidianGlintSalt, WorldConstants.WrapX(worldX, _circumference), 0, Wz(worldZ)) < 0.15)
        {
            return crystal;
        }

        fillToY = surfaceY - 3;
        return obsidian;
    }

    // ================= Lava flows =================
    private const long FlowSalt = 0x1AFA01;
    private const long FlowRopeSalt = 0x1AFA02;
    private const long FlowPocketSalt = 0x1AFA03;
    private const int FlowSegments = 4;
    private const double FlowMaxSegment = 34.0;
    private const double FlowMaxHalfWidth = 8.0;

    private bool HasLavaFlows(PlanetType planet)
        => HasVolcanoes(planet) && planet.HasTag(TerrainTag.Volcanic);

    /// <summary>A unit direction from six hash bits, trig-free: an integer vector in [−4, 4]² (never zero)
    /// normalised by a square root, which every libm rounds the same way.</summary>
    private static (double X, double Z) DirFromBits(ulong bits)
    {
        int ix = (int)(bits % 9UL) - 4;
        int iz = (int)((bits / 9UL) % 9UL) - 4;
        if (ix == 0 && iz == 0)
        {
            ix = 1;
        }

        double len = System.Math.Sqrt((double)(ix * ix + iz * iz));
        return (ix / len, iz / len);
    }

    /// <summary>The lava tongue covering (worldX, worldZ), if any: <paramref name="core"/> is 1 on the tongue's
    /// axis fading to 0 at its edge, <paramref name="tongueHash"/> identifies the tongue. Tongues start at their
    /// cone's foot (the cone's own row owns the cells inside the radius) and run 2–3 per cone, four bent
    /// segments each, 8–16 wide at the foot tapering to half that at the toe. Every cone of the 3×3 cells
    /// around the column is checked, since a tongue may cross its cell's border.</summary>
    private bool TryGetLavaFlow(PlanetType planet, long seed, int worldX, int worldZ, out double core, out ulong tongueHash)
    {
        core = 0.0;
        tongueHash = 0;
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / VolcanoCellSize));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / VolcanoCellSize));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period;
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));
        double reach = 60.0 * 1.5 + FlowSegments * FlowMaxSegment + FlowMaxHalfWidth + 2.0;

        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                int gx = ((cxI + ix) % nx + nx) % nx;
                int gz = ((czI + iz) % nz + nz) % nz;
                if (!TryGetVolcanoInCell(planet, seed, gx, gz, cw, ch, period, out var cone))
                {
                    continue;
                }

                double dx = WorldConstants.WrapDeltaX(wx - cone.CenterX, _circumference);
                double dz = zc - (cone.CenterZ + period / 2);
                if (dz > period / 2.0) dz -= period;
                if (dz < -period / 2.0) dz += period;
                double dist = System.Math.Sqrt(dx * dx + dz * dz);
                if (dist < cone.Radius || dist > reach)
                {
                    continue; // the cone itself, or out of every tongue's reach
                }

                ulong ch0 = Noise.Hash(seed ^ FlowSalt, cone.CenterX, 0, cone.CenterZ);
                int tongues = 2 + (int)(ch0 & 1UL);
                for (int t = 0; t < tongues; t++)
                {
                    ulong th = Noise.Hash(seed ^ FlowSalt, cone.CenterX, t + 1, cone.CenterZ);
                    var (ddx, ddz) = DirFromBits(th >> 8);
                    double halfWidth0 = 4.0 + ((th >> 20) & 0xFF) / 255.0 * (FlowMaxHalfWidth - 4.0); // 4..8
                    double px = ddx * cone.Radius * 0.9, pz = ddz * cone.Radius * 0.9; // the foot, relative to the centre
                    for (int s = 0; s < FlowSegments; s++)
                    {
                        ulong sh = th >> (28 + s * 9);
                        double len = 22.0 + (sh & 0x3F) / 63.0 * (FlowMaxSegment - 22.0);
                        // Bend: rotate the heading by a small hash-drawn angle, as a unit vector (8, k)/|.|, k in −3..3.
                        int k = (int)((sh >> 6) & 0x7) - 3;
                        double rl = System.Math.Sqrt(64.0 + k * k);
                        double rc = 8.0 / rl, rs = k / rl;
                        double ndx = ddx * rc - ddz * rs, ndz = ddx * rs + ddz * rc;
                        ddx = ndx;
                        ddz = ndz;
                        double qx = px + ddx * len, qz = pz + ddz * len;
                        // Point-to-segment distance and the along-parameter for the taper.
                        double vx = qx - px, vz = qz - pz;
                        double u = ((dx - px) * vx + (dz - pz) * vz) / (vx * vx + vz * vz);
                        u = u < 0.0 ? 0.0 : u > 1.0 ? 1.0 : u;
                        double ex = dx - (px + vx * u), ez = dz - (pz + vz * u);
                        double d = System.Math.Sqrt(ex * ex + ez * ez);
                        double along = (s + u) / FlowSegments;
                        double halfWidth = halfWidth0 * (1.0 - 0.5 * along);
                        if (d < halfWidth)
                        {
                            double c = 1.0 - d / halfWidth;
                            if (c > core)
                            {
                                core = c;
                                tongueHash = th;
                            }
                        }

                        px = qx;
                        pz = qz;
                    }
                }
            }

        return core > 0.0;
    }

    /// <summary>The flow's rise: 1–3 blocks, ropy (a ridged field at a 9-block pitch), flat-topped with a soft edge.</summary>
    private double LavaFlowOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetLavaFlow(planet, w.Seed, worldX, worldZ, out double core, out _))
        {
            return 0.0;
        }

        double rope = 1.0 - System.Math.Abs(2.0 * FbmT(w.Seed + FlowRopeSalt, worldX, worldZ, 9.0, octaves: 2) - 1.0);
        return (1.0 + 2.0 * rope) * Smooth01(core / 0.35);
    }

    /// <summary>Basalt through the flow, three deep.</summary>
    private BlockId? LavaFlowPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!TryGetLavaFlow(planet, w.Seed, worldX, worldZ, out _, out _))
        {
            return null;
        }

        var basalt = _content.GetBlock("basalt")?.NumericId ?? BlockId.Air;
        if (basalt.IsAir)
        {
            return null;
        }

        fillToY = surfaceY - 3;
        return basalt;
    }

    /// <summary>One cell in eight on the flow's core still glows: a 1-deep lava pocket in the basalt (a body of the
    /// generation-1 chain, so every surface-fluid query agrees with what the column fills). Never over a cave
    /// mouth — generated fluid is a bottomless source to the automaton, and a pocket with a tunnel under its bed
    /// would pour lava into it forever.</summary>
    private bool LavaPocketAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY)
        => TryGetLavaFlow(planet, w.Seed, worldX, worldZ, out double core, out _) && core > 0.45
           && Noise.Value01(w.Seed + FlowPocketSalt, WorldConstants.WrapX(worldX, _circumference), 0, Wz(worldZ)) < 0.125
           && !CaveMouthNear(planet, worldX, worldZ, surfaceY);

    /// <summary>True when a worm tunnel reaches within two cells of the ground top here (a cave mouth, or the roof
    /// of one just under the skin) — the one place a 1-deep sheet body must not sit.</summary>
    private bool CaveMouthNear(PlanetType planet, int worldX, int worldZ, int surfaceY)
    {
        if (!HasTunnels(planet))
        {
            return false;
        }

        System.Span<(int Lo, int Hi)> spans = stackalloc (int Lo, int Hi)[TunnelMaxSpans];
        int n = TunnelSpans(planet, worldX, worldZ, spans);
        for (int i = 0; i < n; i++)
        {
            if (spans[i].Hi >= surfaceY - 2)
            {
                return true;
            }
        }

        return false;
    }

    // ================= Barchans =================
    private const double BarchanCellSize = 1000.0;
    private const double BarchanChance = 0.35;
    private const double BarchanMaxRadius = 300.0;
    private const long BarchanSalt = 0x5AB0C1;
    private const long BarchanDuneSalt = 0x5AB0C2;

    /// <summary>Wind country with a sand surface that did NOT roll the dune-sea styles (their crests would
    /// swallow a crescent): desert, red_desert, badlands.</summary>
    private bool HasBarchans(PlanetType planet, string[] styles)
        => !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands && HasAir(planet)
           && planet.HasTag(TerrainTag.Wind) && SandSurface(planet)
           && System.Array.IndexOf(styles, "dunes") < 0 && System.Array.IndexOf(styles, "petrified-dunes") < 0;

    /// <summary>The world's wind as a unit vector, from the same grain the dune crests march in.</summary>
    private static (double X, double Z) WindOf(in TerrainGrain g)
    {
        double x = g.Swap ? g.Shear : g.Stretch;
        double z = g.Swap ? g.Stretch : g.Shear;
        double len = System.Math.Sqrt(x * x + z * z);
        return (x / len, z / len);
    }

    private static double Dome(double dx, double dz, double radius, double height)
    {
        double q = (dx * dx + dz * dz) / (radius * radius);
        return q >= 1.0 ? 0.0 : height * (1.0 - q);
    }

    /// <summary>A field 150–300 across of crescent dunes on a 24–40 pitch: each dune is a dome 4–9 high and 10–16
    /// across minus a smaller dome shifted downwind, which leaves the thick convex side upwind and the two horns
    /// trailing downwind. The pitch grid is modular over the torus, so a crescent never tears at the seam.</summary>
    private double BarchanOffset(WonderProfile w, int worldX, int worldZ)
    {
        if (!TryGetHotspot(w.Seed ^ BarchanSalt, BarchanCellSize, BarchanChance, BarchanMaxRadius + 16.0,
                worldX, worldZ, out ulong h, out double dx, out double dz))
        {
            return 0.0;
        }

        double fieldR = 150.0 + ((h >> 16) & 0x3FF) / 1023.0 * (BarchanMaxRadius - 150.0);
        double fieldDist = System.Math.Sqrt(dx * dx + dz * dz);
        if (fieldDist >= fieldR)
        {
            return 0.0;
        }

        double pitch = 24.0 + ((h >> 26) & 0x3FF) / 1023.0 * 16.0;
        int period = LatPeriod;
        int nx = System.Math.Max(1, (int)System.Math.Round(_circumference / pitch));
        int nz = System.Math.Max(1, (int)System.Math.Round(period / pitch));
        double cw = _circumference / (double)nx;
        double ch = period / (double)nz;
        int wx = WorldConstants.WrapX(worldX, _circumference);
        int zc = ((worldZ + period / 2) % period + period) % period;
        int cxI = System.Math.Min(nx - 1, (int)(wx / cw));
        int czI = System.Math.Min(nz - 1, (int)(zc / ch));
        var (windX, windZ) = WindOf(w.Grain);
        double edge = Smooth01((fieldR - fieldDist) / 40.0);

        double best = 0.0;
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                int gx = ((cxI + ix) % nx + nx) % nx;
                int gz = ((czI + iz) % nz + nz) % nz;
                ulong dh = Noise.Hash(w.Seed ^ BarchanDuneSalt, gx, 0, gz);
                if ((dh & 0x7) == 0)
                {
                    continue; // one pitch cell in eight stays bare, so the field breathes
                }

                double px = (cxI + ix + 0.2 + ((dh >> 8) & 0x3FF) / 1023.0 * 0.6) * cw;
                double pz = (czI + iz + 0.2 + ((dh >> 20) & 0x3FF) / 1023.0 * 0.6) * ch;
                double ddx = WorldConstants.WrapDeltaX(wx - px, _circumference);
                double ddz = zc - pz;
                if (ddz > period / 2.0) ddz -= period;
                if (ddz < -period / 2.0) ddz += period;
                double height = 4.0 + ((dh >> 32) & 0xFF) / 255.0 * 5.0; // 4..9
                double radius = 10.0 + ((dh >> 40) & 0xFF) / 255.0 * 6.0; // 10..16
                double crescent = Dome(ddx, ddz, radius, height)
                    - Dome(ddx - windX * radius * 0.55, ddz - windZ * radius * 0.55, radius * 0.75, height * 0.9);
                if (crescent > best)
                {
                    best = crescent;
                }
            }

        return best * edge;
    }

    // ================= Frost polygons =================
    private const long FrostSalt = 0xF205701;
    private const long FrostRegionSalt = 0xF205702;
    private const double FrostPitch = 22.0;
    private const double FrostEdge = 1.6;

    /// <summary>Cold, wet, air-bearing ground that is not a salt pan (the salt polygons own those).</summary>
    private bool HasFrostPolygons(PlanetType planet)
        => !planet.Void && !planet.Cratered && !_crateredWorld && !planet.FloatingIslands && HasAir(planet)
           && planet.BaseTemperature <= -8.0 && WaterAbundanceOf(planet) >= 0.4 && !planet.HasTag(TerrainTag.Salt);

    private bool FrostRegionAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => FbmT(w.Seed + FrostRegionSalt, worldX, worldZ, 380.0, octaves: 2) > 0.56 && !IceCoveredAt(planet, w, worldX, worldZ);

    /// <summary>The polygon net at a column: on a ridge, or inside a plate that holds a pond (a fifth of them).</summary>
    private (bool Ridge, bool Pond) FrostPolygonAt(PlanetType planet, WonderProfile w, int worldX, int worldZ)
    {
        if (!FrostRegionAt(planet, w, worldX, worldZ))
        {
            return (false, false);
        }

        PolygonNet(w.Seed + FrostSalt, worldX, worldZ, FrostPitch, out double d1, out double d2, out ulong plate);
        bool ridge = d2 - d1 < FrostEdge;
        bool pond = !ridge && d2 - d1 > FrostEdge + 1.5 && ((plate >> 60) & 0xF) < 3;
        return (ridge, pond);
    }

    /// <summary>+1 on the ridges of the net (the frost heave), 0 on the plates.</summary>
    private double FrostPolygonOffset(PlanetType planet, WonderProfile w, int worldX, int worldZ)
        => FrostPolygonAt(planet, w, worldX, worldZ).Ridge ? 1.0 : 0.0;

    /// <summary>Bare stone on the ridges; the plates keep the tundra's own skin.</summary>
    private BlockId? FrostPolygonPaint(PlanetType planet, WonderProfile w, int worldX, int worldZ, out int fillToY)
    {
        fillToY = int.MinValue;
        if (!FrostPolygonAt(planet, w, worldX, worldZ).Ridge)
        {
            return null;
        }

        var stone = _content.GetBlock("stone")?.NumericId ?? BlockId.Air;
        return stone.IsAir ? null : stone;
    }

    /// <summary>A 1-deep pond in a fifth of the plates on flat ground — the freeze pass makes it the ice-covered
    /// pool of a patterned tundra (a body of the generation-1 chain).</summary>
    private bool FrostPondAt(PlanetType planet, WonderProfile w, int worldX, int worldZ, int surfaceY)
        => FrostPolygonAt(planet, w, worldX, worldZ).Pond && SurfaceSlope(planet, worldX, worldZ) <= 2
           && !CaveMouthNear(planet, worldX, worldZ, surfaceY);

    // ================= test hooks =================

    /// <summary>The part-4 offset rows by name (tests): lava-flow, barchans, frost-polygons.</summary>
    internal double VolcanicOffsetForTest(string name, PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return name switch
        {
            "lava-flow" => w.LavaFlows ? LavaFlowOffset(planet, w, worldX, worldZ) : 0.0,
            "barchans" => w.Barchans ? BarchanOffset(w, worldX, worldZ) : 0.0,
            "frost-polygons" => w.FrostPolygons ? FrostPolygonOffset(planet, w, worldX, worldZ) : 0.0,
            _ => throw new System.ArgumentException(name, nameof(name)),
        };
    }

    /// <summary>The frost net at a column (tests): ridge / pond, both false off the region or below generation 3.</summary>
    internal (bool Ridge, bool Pond) FrostPolygonForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.FrostPolygons ? FrostPolygonAt(planet, w, worldX, worldZ) : (false, false);
    }

    /// <summary>The lava tongue's core value at a column (tests): 0 off every tongue or below generation 3.</summary>
    internal double LavaFlowCoreForTest(PlanetType planet, int worldX, int worldZ)
    {
        var w = WonderFor(planet);
        return w.LavaFlows && TryGetLavaFlow(planet, w.Seed, worldX, worldZ, out double core, out _) ? core : 0.0;
    }
}
