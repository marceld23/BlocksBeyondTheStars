// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>What one column of a world shows from far above — the input of the planet map previews (#2172).</summary>
public readonly struct SurfaceSummary
{
    public SurfaceSummary(int height, int biome, BlockId surface, double vegetation, bool hot, bool sandSea)
    {
        Height = height;
        Biome = biome;
        Surface = surface;
        Vegetation = vegetation;
        Hot = hot;
        SandSea = sandSea;
    }

    /// <summary>The generated surface Y.</summary>
    public int Height { get; }

    /// <summary>The column's biome index — the key the per-biome weather offset uses (#2174).</summary>
    public int Biome { get; }

    /// <summary>The ground block the column shows: its biome's surface, snow or ice above the snow line.</summary>
    public BlockId Surface { get; }

    /// <summary>Relative plant cover 0..0.6: the type's flora and tree density × the biome's multipliers, faded by
    /// the cold toward the tree line — a forest reads green, a prairie lighter, a summit bare.</summary>
    public double Vegetation { get; }

    /// <summary>A hot-zone column (generation 8).</summary>
    public bool Hot { get; }

    /// <summary>A sand-sea column (generation 9).</summary>
    public bool SandSea { get; }
}

/// <summary>Read-only column summaries for the planet map previews (#2172) — the orbit spheres, the bodies in the
/// sky and the landing-pad map. Never used by chunk generation, so the generated worlds and their goldens are
/// untouched; the decisions mirror the column pass (biome pick, altitude snow/ice line, cold flora fade) closely
/// enough for a map pixel that covers dozens of blocks.</summary>
public sealed partial class WorldGenerator
{
    /// <summary>Summarises one column for a map pixel: height, biome, the ground block a viewer from orbit would
    /// see and the plant cover.</summary>
    public SurfaceSummary SummarizeSurface(PlanetType planet, int worldX, int worldZ)
    {
        int h = SurfaceHeight(planet, worldX, worldZ);
        var biomes = ResolveBiomes(planet);
        var calib = CalibFor(planet);
        int idx = biomes.Count <= 1 ? 0 : BiomeIndex(calib, PlanetSeed(planet), worldX, worldZ, biomes.Count, h);
        var b = biomes[idx];
        BlockId surface = b.Surface;

        // Altitude climate (#476): above the snow line snow, further up solid ice — the column pass's rule without
        // its ±1.5 °C dither (a map pixel spans dozens of blocks).
        if (SnowClimate(planet, calib))
        {
            double t = TempAt(calib, h);
            var ice = _content.GetBlock("ice")?.NumericId ?? BlockId.Air;
            var snow = _content.GetBlock("snow")?.NumericId ?? BlockId.Air;
            if (t < IceLineC && !ice.IsAir)
            {
                surface = ice;
            }
            else if (t < SnowLineC && !snow.IsAir)
            {
                surface = snow;
            }
        }

        double vegetation = planet.FloraDensity * b.FloraMul * 0.9 + (planet.TreeDensity ?? 0.0) * b.TreeMul * 6.0;
        vegetation = System.Math.Clamp(vegetation, 0.0, 0.6) * ColdFloraFactor(calib, h);
        return new SurfaceSummary(h, idx, surface, vegetation, b.Hot, b.SandSea);
    }

    /// <summary>True when this world's sea carries ice at its surface (#494): a cold enough waterline, or a type's
    /// fixed ice sheet (generation 8). Hot zones on a fixed-sheet world stay open — the map ignores that detail.</summary>
    public bool SeaSurfaceFrozen(PlanetType planet)
    {
        var calib = CalibFor(planet);
        if (!CanFreezeWater(planet, calib))
        {
            return calib.FixedIceSheet > 0;
        }

        return calib.FixedIceSheet > 0 || TempAt(calib, SeaLevel(planet)) < SnowLineC;
    }

    /// <summary>The column pass's snow gate: air, no cratered regolith, a snow block, and a world whose highest point
    /// gets cold enough at all.</summary>
    private bool SnowClimate(PlanetType planet, WorldCalibration calib)
    {
        bool airlessBody = planet.Cratered || _crateredWorld;
        bool hasAtmosphere = !string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);
        return hasAtmosphere && !airlessBody
            && !(_content.GetBlock("snow")?.NumericId ?? BlockId.Air).IsAir
            && TempAt(calib, calib.MaxHeight) < SnowLineC + 2.0;
    }
}
