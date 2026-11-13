// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The placement rules of one stamped structure kind (#3517) — one entry of <c>data/structure_types.json</c>, merged over the
/// kind's C# default (<see cref="StructureTypeRules.Defaults"/>). Every number a stamper used to hold as a constant lives here;
/// which fields a kind reads is said on the kind in <see cref="StructureTypeRules"/>. A field a kind does not read is ignored.
/// <para>These rules only decide what is stamped NEXT: what a body already carries is replayed from its placement records
/// (#3513 pins the count per kind, a settlement's tier and ruin verdict and the wreck's design), so changing a number later
/// never moves a structure that stands.</para>
/// </summary>
public sealed class StructureTypeDefinition
{
    /// <summary>The kind (the key in the file).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>False switches the kind off like its server switch (<c>ServerConfig.Place…</c>) does, at the same spot — both
    /// must be on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The oldest terrain generation that stamps the kind (an older world never grows one). 0 = every generation.</summary>
    public int MinGeneration { get; set; }

    /// <summary>The chance (0..1) of a single-roll kind (the abandoned station, the intercity line, the landmark, the buried
    /// ruin), before the world option's factor. A <c>ServerConfig</c> chance that is set overrides it.</summary>
    public double Chance { get; set; }

    /// <summary>The expected count at hospitability 1 and world size 1 of a counted kind (settlements, lodges, outposts).</summary>
    public double Density { get; set; }

    /// <summary>The count roll: for a cumulative kind (ruins, monuments, factories, data cubes, chests) the count is the
    /// number of thresholds at or below the roll (<see cref="CountBelow"/>); the camps' are the "no camp" odds per bandit
    /// level (Rare, Normal, Frequent, Extreme); the vaults' the chance of a first and of a second vault.</summary>
    public List<double> Thresholds { get; set; } = new();

    /// <summary>The upper bound of each threshold after the world option scales it (the vaults: 0.95 and 0.8).</summary>
    public List<double> ThresholdCaps { get; set; } = new();

    /// <summary>The smallest count of a kind drawn as <c>minCount + Next(cap − minCount + 1)</c> (the SPS labs).</summary>
    public int MinCount { get; set; }

    /// <summary>The most of the kind one body carries (0 where the kind has no cap).</summary>
    public int Cap { get; set; }

    /// <summary>How the guaranteed search seats the kind: <c>inhabited</c>, <c>ruin</c>, <c>factory</c>, <c>camp</c>,
    /// <c>monument</c> — or empty for a kind with a seat rule of its own.</summary>
    public string SeatPolicy { get; set; } = string.Empty;

    /// <summary>Terrain generation 38+: the free ground (blocks) kept between this kind's footprint and every other — the
    /// margin its footprint is registered with and the least margin its own placement keeps (the larger one wins).</summary>
    public int KeepClear { get; set; }

    /// <summary>Terrain generation 38+: a wider margin this kind keeps from some kinds when it is placed (a hut keeps 40 blocks
    /// from towns and camps), by kind — <see cref="StructureTypeRules.Pad"/> and <see cref="StructureTypeRules.WreckSite"/>
    /// name the landing pads and the crash site.</summary>
    public Dictionary<string, int> KeepClearFrom { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What a planet type must be to carry the kind (<see cref="StructureTypeRules.ConditionNames"/>).</summary>
    public List<string> Conditions { get; set; } = new();

    /// <summary>The key a planet type with a structure whitelist (<see cref="PlanetType.RestrictStructures"/>) names in
    /// <see cref="PlanetType.AllowedStructures"/> to carry the kind; empty = never on such a type.</summary>
    public string Whitelist { get; set; } = string.Empty;

    /// <summary>Terrain generation 40 (#3563): where the kind likes to stand (empty = neutral, the classic order).</summary>
    public StructureSiteRule Site { get; set; } = new();

    /// <summary>Terrain generation 40 (#3563): how the kind reserves its ground (radius 0 = it reserves none).</summary>
    public StructureGradeRule Grade { get; set; } = new();

    /// <summary>The count of a cumulative roll: how many thresholds lie at or below <paramref name="roll"/> — with
    /// [0.55, 0.85] a roll under 0.55 is 0, under 0.85 is 1, else 2 (the old <c>r &lt; a ? 0 : r &lt; b ? 1 : 2</c>).</summary>
    public int CountBelow(double roll)
    {
        int n = 0;
        while (n < Thresholds.Count && roll >= Thresholds[n])
        {
            n++;
        }

        return n;
    }

    /// <summary>The margin this kind keeps from a footprint of <paramref name="otherKind"/> registered with
    /// <paramref name="otherMargin"/>: the largest of its own <see cref="KeepClear"/>, its <see cref="KeepClearFrom"/> entry
    /// and the other's margin.</summary>
    public int MarginFrom(string otherKind, int otherMargin)
    {
        int margin = Math.Max(KeepClear, otherMargin);
        return KeepClearFrom.TryGetValue(otherKind, out int from) ? Math.Max(margin, from) : margin;
    }

    /// <summary>Whether the kind names the condition.</summary>
    public bool Requires(string condition) => Conditions.Contains(condition, StringComparer.Ordinal);

    /// <summary>A deep copy (the defaults are shared; a merge works on a copy).</summary>
    public StructureTypeDefinition Clone() => new()
    {
        Kind = Kind,
        Enabled = Enabled,
        MinGeneration = MinGeneration,
        Chance = Chance,
        Density = Density,
        Thresholds = new List<double>(Thresholds),
        ThresholdCaps = new List<double>(ThresholdCaps),
        MinCount = MinCount,
        Cap = Cap,
        SeatPolicy = SeatPolicy,
        KeepClear = KeepClear,
        KeepClearFrom = new Dictionary<string, int>(KeepClearFrom, StringComparer.Ordinal),
        Conditions = new List<string>(Conditions),
        Whitelist = Whitelist,
        Site = Site.Clone(),
        Grade = Grade.Clone(),
    };

    /// <summary>Whether two definitions hold the same rules (every field; the margins by kind, the conditions in order).</summary>
    public bool SameAs(StructureTypeDefinition other)
        => Kind == other.Kind && Enabled == other.Enabled && MinGeneration == other.MinGeneration
           && Chance.Equals(other.Chance) && Density.Equals(other.Density)
           && Thresholds.SequenceEqual(other.Thresholds) && ThresholdCaps.SequenceEqual(other.ThresholdCaps)
           && MinCount == other.MinCount && Cap == other.Cap && SeatPolicy == other.SeatPolicy && KeepClear == other.KeepClear
           && KeepClearFrom.Count == other.KeepClearFrom.Count
           && KeepClearFrom.All(kv => other.KeepClearFrom.TryGetValue(kv.Key, out int v) && v == kv.Value)
           && Conditions.SequenceEqual(other.Conditions, StringComparer.Ordinal) && Whitelist == other.Whitelist
           && Site.SameAs(other.Site) && Grade.SameAs(other.Grade);
}
