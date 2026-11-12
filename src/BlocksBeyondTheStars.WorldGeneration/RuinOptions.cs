// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3515: what a caller may change about the shared ruin pass (<see cref="StructureRuiner"/>). Every setting defaults to
/// the pass as the landmarks have always run it, so a fresh <see cref="RuinOptions"/> (or none at all) builds exactly the
/// ruin of its revision. A block key the content does not know is ignored, as if the setting were left at its default.
/// <para>A caller that changes world output with a setting gates that on its own terrain generation: the options are
/// part of what a caller builds, not of the pass's revision.</para>
/// </summary>
public sealed class RuinOptions
{
    /// <summary>Whether flora pushes through the floor where the structure stands (default yes). Off for airless
    /// bodies, vaults and wrecks.</summary>
    public bool Overgrowth { get; set; } = true;

    /// <summary>The flora block that grows; null (default) = the biome's flora for the surface block.</summary>
    public string? OvergrowthBlock { get; set; }

    /// <summary>The rubble's material, weighted, picked per rubble cell. Null or empty (default) = what last came down
    /// in the column (a plain, unshaped block that is no glass, light or ladder), else the planet's stone.</summary>
    public IReadOnlyList<RuinWeightedBlock>? RubblePalette { get; set; }

    /// <summary>Whether erosion may bite shaped pieces too — a monument's column cylinders and arch ramps. Default false:
    /// a stair, a slab or a piece of furniture only falls with the wall that carried it. A ladder is never eroded.</summary>
    public bool ErodeShaped { get; set; }

    /// <summary>A factor on the strength per column (x, z) of the grid: 1 = as set, 0.35 = a spared plot, 2 = a smashed
    /// corner (the result is clamped to 0..1). A spot collapse or a cave-in also spares a column's cells with the
    /// chance the factor leaves. Null (default) = 1 everywhere. <see cref="SpareBox"/> builds the common case. A factor
    /// of 0 still lets the weakest decay in — use kept cells for a part that must stay untouched.</summary>
    public Func<int, int, double>? StrengthAt { get; set; }

    /// <summary>Rows 0 up to this one are untouched like kept cells: nothing is taken from them, nothing written into
    /// them (no rubble, no flora, no weathering), and they hold what hangs on them. -1 (default) = none.</summary>
    public int ProtectRowsUpTo { get; set; } = -1;

    /// <summary>What a dead light becomes. Null (default) = its switched-off twin <c>&lt;key&gt;_off</c> where the
    /// content has one, else air.</summary>
    public string? LightOffBlock { get; set; }

    /// <summary>Weathering, a step of its own right after the openings: a block becomes another with a chance (stone →
    /// moss stone, a panel → a rusted panel, a machine → scrap). The first rule of a block that hits wins. Null or empty
    /// (default) = none.</summary>
    public IReadOnlyList<RuinSubstitution>? Substitutions { get; set; }

    /// <summary>Scorch or rust marks, after the weathering: more of them around the breaches than elsewhere. Null
    /// (default) = none.</summary>
    public RuinScorch? Scorch { get; set; }

    /// <summary>A <see cref="StrengthAt"/> map that spares the columns of one box (inclusive): <paramref name="factor"/>
    /// inside, 1 outside.</summary>
    public static Func<int, int, double> SpareBox(int minX, int minZ, int maxX, int maxZ, double factor)
        => (x, z) => x >= minX && x <= maxX && z >= minZ && z <= maxZ ? factor : 1.0;
}

/// <summary>A block key with a weight, for <see cref="RuinOptions.RubblePalette"/>.</summary>
public sealed class RuinWeightedBlock
{
    public RuinWeightedBlock(string block, int weight = 1)
    {
        Block = block ?? string.Empty;
        Weight = weight;
    }

    public string Block { get; }

    /// <summary>The relative weight; 0 or less leaves the block out.</summary>
    public int Weight { get; }
}

/// <summary>One weathering rule of <see cref="RuinOptions.Substitutions"/>: <see cref="From"/> becomes <see cref="To"/>
/// with <see cref="Chance"/> per cell. An empty <see cref="To"/> takes the block away (a broken machine is gone). The
/// cell keeps its shape and loses its tint and glow (the paint and the light are gone with the weather).</summary>
public sealed class RuinSubstitution
{
    public RuinSubstitution(string from, string to, double chance)
    {
        From = from ?? string.Empty;
        To = to ?? string.Empty;
        Chance = chance;
    }

    public string From { get; }

    public string To { get; }

    /// <summary>0..1 per cell.</summary>
    public double Chance { get; }
}

/// <summary>Scorch or rust marks of <see cref="RuinOptions.Scorch"/>: a cell is marked with
/// <see cref="NearBreachChance"/> when it touches (faces, edges or corners) a cell the decay took out, else with
/// <see cref="ElsewhereChance"/>. A mark replaces the block by <see cref="Block"/> (keeping its shape, losing tint and
/// glow) when that is set, and tints it with <see cref="Tint"/> (0xRRGGBB) when that is set — both, or one of them.
/// Ladders, glass and lights are never marked.</summary>
public sealed class RuinScorch
{
    /// <summary>The block a marked cell becomes; null or empty = the block stays.</summary>
    public string? Block { get; set; }

    /// <summary>The tint a marked cell takes (0xRRGGBB); 0 = none.</summary>
    public int Tint { get; set; }

    public double NearBreachChance { get; set; }

    public double ElsewhereChance { get; set; }
}
