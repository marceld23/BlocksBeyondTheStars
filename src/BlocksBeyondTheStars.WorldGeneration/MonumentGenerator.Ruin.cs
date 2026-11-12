// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The weathering of a monument of terrain generation 38 (#3516): the shared ruin pass instead of the old erosion.</summary>
public static partial class MonumentGenerator
{
    /// <summary>The weakest and the strongest a monument decays with: the archetype's old top erosion chance times
    /// <see cref="RuinStrengthPerTopP"/>, clamped between them — a fossil barely weathers, an obelisk's crown comes down.</summary>
    internal const double MinRuinStrength = 0.15, MaxRuinStrength = 0.5;

    internal const double RuinStrengthPerTopP = 1.5;

    /// <summary>The strength a monument decays with for an archetype whose old erosion rose to <paramref name="topP"/>.</summary>
    internal static double RuinStrength(double topP) => Math.Clamp(topP * RuinStrengthPerTopP, MinRuinStrength, MaxRuinStrength);

    /// <summary>
    /// The archetype as it was built (its own breaks — a toppled stone, a fallen tip, a span that broke off — are authored
    /// shape) goes through the shared ruin pass (<see cref="StructureRuiner"/>, profile masonry, ground row 0): crumbled crowns,
    /// bites that rise with height and may take column drums and arch wedges too (<see cref="RuinOptions.ErodeShaped"/>), a
    /// shaft through a roof, a settle that brings down whatever is left hanging, rubble of the surface block where something
    /// fell, the biome's flora on the ground — none on an airless body. The rows the archetype protects stay as built and a
    /// gate's doorway with its jambs and lintel is kept, so the opening stays walkable. The runes and the relic cache follow.
    /// </summary>
    private static void RuinPass(Canvas c, string archetype, long seed, string surfaceBlock, bool airless, GameContent content)
    {
        var (topP, protectY, door) = c.Erosion ?? (0.0, 0, null);
        var grid = c.Grid();
        if (door is { } d)
        {
            grid.KeepBox(d.MinX - 2, 0, d.CZ - 2, d.MaxX + 2, d.Height + 3, d.CZ + 2);
        }

        var options = new RuinOptions
        {
            ErodeShaped = true,
            Overgrowth = !airless,
            RubblePalette = new[] { new RuinWeightedBlock(content.GetBlock(surfaceBlock) is null ? "stone" : surfaceBlock) },
            ProtectRowsUpTo = protectY,
        };
        StructureRuiner.Run(grid, content, StructureDecay.Seed("monument:" + archetype, seed), LandmarkRules.ProfileMasonry,
            RuinStrength(topP), groundRow: 0, Array.Empty<Vector3i>(), surfaceBlock, StructureDecay.Revision, options);
    }
}
