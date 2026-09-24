// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 9 (#2000): the sand-sea planet class. A calibrated region field — the same mechanism as the Titas
/// hot zone, so the share of the surface is exact per world — marks the sea. Inside it the relief is damped to broad
/// dunes, the sea is lifted clear of the water once the sea level is known (a sand sea never floods), the column's
/// topsoil reaches <see cref="PlanetType.SandSeaDepth"/> blocks of sand and its cave shield covers the same band, so no
/// cave, tunnel or cavern opens under the sea and nothing reveals the sandworm's buried body. A tall column inside the
/// region (a butte, an inselberg, a massif) stays rock: a refuge island. Everything around the sea keeps the type's own
/// procedural relief — mountains, canyons, lava, water. Every other type (and every older world) never enters here.
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>How far above the plain the sea's dune floor rides (blocks).</summary>
    private const double SandSeaLift = 3.0;

    /// <summary>A sea column may rise this far above its floor and still be sea — higher ground is a rock island.</summary>
    private const int SandSeaCeilingRise = 10;

    /// <summary>The blend band around the region edge, in field units: the relief eases from the land's into the dunes.</summary>
    private const double SandSeaBlendLo = 0.035, SandSeaBlendHi = 0.015;

    /// <summary>Whether this world lays out a sand sea at all (generation 9, a type with a share).</summary>
    private bool SandSeaWorld(PlanetType planet)
        => _terrainGeneration >= WorldDescription.GiantsGeneration && planet.SandSeaShare > 0.0 && !planet.Void;

    /// <summary>The sand-sea region field: broad basins a few hundred blocks across.</summary>
    private double SandSeaField(long seed, int worldX, int worldZ)
        => FbmT(seed ^ 0x5A5D5EAL, worldX, worldZ, 260.0, octaves: 3);

    /// <summary>The field quantile whose top <see cref="PlanetType.SandSeaShare"/> is sea, measured once per world on the
    /// torus grid (the field alone — no heights, so the relief may read it before the calibration exists).</summary>
    private double SandSeaThreshold(PlanetType planet, long seed)
    {
        int period = LatPeriod;
        int stepX = System.Math.Max(8, _circumference / 128);
        int stepZ = System.Math.Max(8, period / 64);
        var field = new System.Collections.Generic.List<double>((_circumference / stepX + 1) * (period / stepZ + 1));
        for (int z = -period / 2; z < period / 2; z += stepZ)
            for (int x = 0; x < _circumference; x += stepX)
                field.Add(SandSeaField(seed, x, z));
        field.Sort();
        double share = System.Math.Clamp(planet.SandSeaShare, 0.02, 0.95);
        return field[(int)((1.0 - share) * (field.Count - 1))];
    }

    /// <summary>The dune relief of the sea floor (0..~6 blocks): long crests with a soft cross-ripple.</summary>
    private double SandDunes(long seed, int worldX, int worldZ)
    {
        double crest = FbmT(seed ^ 0x0D0E5L, worldX, worldZ * 0.45, 46.0, octaves: 2);
        double ripple = FbmT(seed ^ 0x0D1E5L, worldX, worldZ, 17.0, octaves: 1);
        double ridge = 1.0 - System.Math.Abs(crest - 0.5) * 2.0;
        return ridge * ridge * 5.0 + ripple * 1.2;
    }

    /// <summary>Blends a column's relief offset into the sea's dunes by the region weight (1 inside, easing out at the edge).</summary>
    private double SandSeaBlend(WonderProfile w, long seed, int worldX, int worldZ, double offset)
    {
        double f = SandSeaField(seed, worldX, worldZ);
        double lo = w.SandThreshold - SandSeaBlendLo, hi = w.SandThreshold + SandSeaBlendHi;
        if (f <= lo)
        {
            return offset;
        }

        double t = f >= hi ? 1.0 : (f - lo) / (hi - lo);
        double weight = t * t * (3.0 - 2.0 * t);
        return offset * (1.0 - weight) + (SandSeaLift + SandDunes(seed, worldX, worldZ)) * weight;
    }

    /// <summary>After the sea is known: a sea column that sits at or under the waterline is lifted onto a dune floor just
    /// above it, so the sand sea is always dry sand. Never runs inside the calibration sample.</summary>
    private int SandSeaRaise(PlanetType planet, WonderProfile w, int worldX, int worldZ, int h)
    {
        int sea = SeaLevel(planet);
        if (sea == int.MinValue || h > sea + 2 || SandSeaField(w.Seed, worldX, worldZ) < w.SandThreshold)
        {
            return h;
        }

        return sea + 3 + (int)System.Math.Round(SandDunes(w.Seed, worldX, worldZ) * 0.5);
    }

    /// <summary>The highest surface a sea column may have (its floor plus the dune band); higher is a rock island.</summary>
    private int SandSeaCeiling(PlanetType planet, int seaLevel)
    {
        int floor = planet.BaseHeight + (int)SandSeaLift;
        if (seaLevel != int.MinValue)
        {
            floor = System.Math.Max(floor, seaLevel + 3);
        }

        return floor + SandSeaCeilingRise;
    }

    /// <summary>True when the column is sand sea: inside the region and low enough (not a rock island).</summary>
    private bool SandSeaColumnAt(WorldCalibration calib, long seed, int worldX, int worldZ, int surfaceY)
        => calib.SandBiome >= 0 && surfaceY <= calib.SandCeiling && SandSeaField(seed, worldX, worldZ) >= calib.SandThreshold;

    /// <summary>Whether (x, z) lies in the sand sea of this world — the sandworm's habitat, where the ground carries a
    /// vibration and a thumper is heard. False on every world without one, and on the rock islands inside it.</summary>
    public bool IsSandSeaAt(PlanetType planet, int worldX, int worldZ)
    {
        if (!SandSeaWorld(planet))
        {
            return false; // cheap gate — no calibration lookup on the ordinary types
        }

        var calib = CalibFor(planet);
        return SandSeaColumnAt(calib, PlanetSeed(planet), worldX, worldZ, SurfaceHeight(planet, worldX, worldZ));
    }

    /// <summary>The share of this world's surface that is sand sea, sampled on a coarse grid (tests, tooling).</summary>
    internal double SandSeaCoverageForTest(PlanetType planet, int step = 24)
    {
        int n = 0, sea = 0;
        int period = LatPeriod;
        for (int z = -period / 2; z < period / 2; z += step)
            for (int x = 0; x < _circumference; x += step)
            {
                n++;
                if (IsSandSeaAt(planet, x, z))
                {
                    sea++;
                }
            }

        return n == 0 ? 0.0 : sea / (double)n;
    }
}
