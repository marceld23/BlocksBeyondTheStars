// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The fallen settlement of terrain generation 38 (#3516): the shared ruin pass instead of the old decay.</summary>
public static partial class SettlementGenerator
{
    /// <summary>The strength a fallen settlement decays with (the masonry default of the ruin pass).</summary>
    internal const double RuinStrength = 0.5;

    /// <summary>The strength factor inside the spared plot: the building there decays far less and stands as a tower.</summary>
    internal const double SparedPlotFactor = 0.35;

    /// <summary>The spared plot's box (inclusive, x and z): drawn from the settlement's stream exactly where the old decay
    /// drew it — one plot of the grid, as far around its centre as a building is wide.</summary>
    private static (int MinX, int MinZ, int MaxX, int MaxZ) SparedPlot(System.Random rng, int cols, int rows, int plot, int building)
    {
        int cx = rng.Next(0, System.Math.Max(1, cols)) * plot + 1 + building / 2;
        int cz = rng.Next(0, System.Math.Max(1, rows)) * plot + 1 + building / 2;
        return (cx - building, cz - building, cx + building, cz + building);
    }

    /// <summary>
    /// The decay of a fallen settlement from terrain generation 38: the layout as the generator built it (its broken central
    /// feature included — that is authored shape) goes through the shared ruin pass (<see cref="StructureRuiner"/>, profile
    /// masonry, ground row 0): crumbled wall tops, bites and shafts that rise with height, smashed glass and dead lamps, a
    /// settle that brings down every storey left hanging, rubble of the building material (the planet's stone where only
    /// fittings fell) and the biome's flora on the ground. One plot is spared (<see cref="SparedPlotFactor"/>) so a
    /// half-standing tower keeps reading from afar. The loot markers' cells stay free.
    /// </summary>
    private static void RuinPass(VoxelGrid grid, List<SettlementMarker> markers, (int MinX, int MinZ, int MaxX, int MaxZ) spared,
        string tier, long seed, string biomeSurfaceBlock, GameContent content)
    {
        var options = new RuinOptions
        {
            StrengthAt = RuinOptions.SpareBox(spared.MinX, spared.MinZ, spared.MaxX, spared.MaxZ, SparedPlotFactor),
        };
        StructureRuiner.Run(grid, content, StructureDecay.Seed("settlement:" + tier, seed), LandmarkRules.ProfileMasonry,
            RuinStrength, groundRow: 0, markers, biomeSurfaceBlock, StructureDecay.Revision, options);
    }
}
