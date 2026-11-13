// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Terrain generation 40 (#3563): where a structure kind likes to stand — the <c>site</c> object of a kind in
/// <c>data/structure_types.json</c>. The placement search measures each candidate (distance to fresh water and to the coast,
/// prominence against its surroundings, local relief, altitude among the world's heights, the biome's ground) and tries the
/// candidates best-first; the hard rules of the search (dry, flat enough, clear, reserved, the placement guarantee) are
/// untouched, so a preference only ever decides the order. An empty rule (every field empty) is neutral: the kind places
/// exactly as before.
/// </summary>
public sealed class StructureSiteRule
{
    /// <summary>The weight names <see cref="Weights"/> may use, and what an absent one counts.</summary>
    public static readonly IReadOnlyDictionary<string, double> DefaultWeights = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["water"] = 3.0,
        ["coast"] = 2.0,
        ["prominence"] = 1.0,
        ["relief"] = 1.0,
        ["altitude"] = 1.0,
        ["ground"] = 2.0,
    };

    /// <summary>Preferred distance to fresh water (a river or a lake), in blocks: [low, high], or empty.</summary>
    public List<int> Water { get; set; } = new();

    /// <summary>Preferred distance to the sea, in blocks: [low, high], or empty.</summary>
    public List<int> Coast { get; set; } = new();

    /// <summary><c>high</c> (a hilltop), <c>low</c> (a hollow) or empty.</summary>
    public string Prominence { get; set; } = string.Empty;

    /// <summary><c>flat</c> (even ground around), <c>rugged</c> (uneven ground) or empty.</summary>
    public string Relief { get; set; } = string.Empty;

    /// <summary>Preferred band of the world's height quantiles (0 = its lowest ground, 1 = its highest): [low, high], or empty.</summary>
    public List<double> Altitude { get; set; } = new();

    /// <summary>Preferred biome surface blocks (block keys), or empty.</summary>
    public List<string> Ground { get; set; } = new();

    /// <summary>How much each term counts (names: <see cref="DefaultWeights"/>); an absent name counts its default.</summary>
    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.Ordinal);

    /// <summary>True when the rule prefers nothing — the search then keeps its classic first-fit order.</summary>
    public bool IsNeutral => Water.Count == 0 && Coast.Count == 0 && Prominence.Length == 0 && Relief.Length == 0
                             && Altitude.Count == 0 && Ground.Count == 0;

    /// <summary>The weight of a term (<see cref="DefaultWeights"/> when not set).</summary>
    public double WeightOf(string term)
        => Weights.TryGetValue(term, out double w) ? w : DefaultWeights.TryGetValue(term, out double d) ? d : 0.0;

    /// <summary>What is wrong with the rule, one sentence each — empty when it is fine.</summary>
    public IEnumerable<string> Problems()
    {
        if ((Water.Count != 0 && (Water.Count != 2 || Water[0] < 0 || Water[1] < Water[0]))
            || (Coast.Count != 0 && (Coast.Count != 2 || Coast[0] < 0 || Coast[1] < Coast[0])))
        {
            yield return "site.water and site.coast are [low, high] in blocks, low ≥ 0 and high ≥ low";
        }

        if (Altitude.Count != 0 && (Altitude.Count != 2 || Altitude[0] < 0 || Altitude[1] > 1 || Altitude[1] < Altitude[0]))
        {
            yield return "site.altitude is [low, high] between 0 and 1";
        }

        if (Prominence.Length != 0 && Prominence != "high" && Prominence != "low")
        {
            yield return $"site.prominence '{Prominence}' is neither 'high' nor 'low'";
        }

        if (Relief.Length != 0 && Relief != "flat" && Relief != "rugged")
        {
            yield return $"site.relief '{Relief}' is neither 'flat' nor 'rugged'";
        }

        foreach (var kv in Weights)
        {
            if (!DefaultWeights.ContainsKey(kv.Key) || kv.Value < 0 || double.IsNaN(kv.Value))
            {
                yield return $"site.weights '{kv.Key}' names no term ({string.Join(", ", DefaultWeights.Keys)}) or is negative";
            }
        }
    }

    public StructureSiteRule Clone() => new()
    {
        Water = new List<int>(Water),
        Coast = new List<int>(Coast),
        Prominence = Prominence,
        Relief = Relief,
        Altitude = new List<double>(Altitude),
        Ground = new List<string>(Ground),
        Weights = new Dictionary<string, double>(Weights, StringComparer.Ordinal),
    };

    public bool SameAs(StructureSiteRule other)
        => Water.SequenceEqual(other.Water) && Coast.SequenceEqual(other.Coast) && Prominence == other.Prominence
           && Relief == other.Relief && Altitude.SequenceEqual(other.Altitude)
           && Ground.SequenceEqual(other.Ground, StringComparer.Ordinal)
           && Weights.Count == other.Weights.Count
           && Weights.All(kv => other.Weights.TryGetValue(kv.Key, out double v) && v.Equals(kv.Value));
}

/// <summary>
/// Terrain generation 40 (#3563, #3567): how a stamped structure of a kind reserves its ground — the <c>grade</c> object of a
/// kind in <c>data/structure_types.json</c>. The generator levels the footprint to the structure's floor and grades the land
/// within <see cref="Radius"/> blocks around it, lowering a column by at most <see cref="MaxCut"/> and raising it by at most
/// <see cref="MaxFill"/>. Radius 0 reserves nothing (the classic stamp: skirt, cut and apron as block edits only).
/// </summary>
public sealed class StructureGradeRule
{
    public int Radius { get; set; }

    public int MaxCut { get; set; }

    public int MaxFill { get; set; }

    /// <summary>True when the kind reserves ground at all.</summary>
    public bool Reserves => Radius > 0 || MaxCut > 0 || MaxFill > 0;

    public IEnumerable<string> Problems()
    {
        if (Radius < 0 || Radius > 64 || MaxCut < 0 || MaxCut > 96 || MaxFill < 0 || MaxFill > 96)
        {
            yield return "grade.radius lies between 0 and 64, grade.maxCut and grade.maxFill between 0 and 96";
        }
    }

    public StructureGradeRule Clone() => new() { Radius = Radius, MaxCut = MaxCut, MaxFill = MaxFill };

    public bool SameAs(StructureGradeRule other) => Radius == other.Radius && MaxCut == other.MaxCut && MaxFill == other.MaxFill;
}
