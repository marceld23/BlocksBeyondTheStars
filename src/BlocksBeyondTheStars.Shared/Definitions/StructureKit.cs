// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A structure KIT (#1873, Marcel's "Baukasten"): the entry behind a module name. Every module that fits together
/// carries the kit's key in <see cref="StructureTemplate.Kit"/>; the kit says how many modules a structure has,
/// which are mandatory and which random (<see cref="Entries"/>), and — for villages and cities — how the grid is
/// laid out. Stations are composed by docking modules port to port (airtight by construction); settlements and
/// cities fill their plots / districts from the entries. Shipped in <c>data/structure_kits.json</c>, authored in the
/// editor into <c>usercontent/structure_kits/&lt;key&gt;.json</c>. Every composition is pinned per world, so editing a
/// kit never changes a structure that already stands.
/// </summary>
public sealed class StructureKit
{
    public const string KindStation = "station";
    public const string KindSettlement = "settlement";
    public const string KindCity = "city";

    public static readonly string[] Kinds = { KindStation, KindSettlement, KindCity };

    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"station" | "settlement" | "city" — which composer builds from this kit.</summary>
    public string Kind { get; set; } = KindStation;

    /// <summary>Size tier the kit competes in: station small … colossal, settlement hamlet … city; a city kit is
    /// keyed by planet type instead and ignores the tier.</summary>
    public string Tier { get; set; } = "medium";

    public string Pack { get; set; } = "default";

    /// <summary>Weight in the joint random table of complete templates and kits of the tier (#1874).</summary>
    public int Weight { get; set; } = 1;

    /// <summary>Settlement / city kits: planet-type keys the kit may appear on; empty = every world.</summary>
    public List<string> PlanetTypes { get; set; } = new();

    /// <summary>How many modules a composed structure has (inclusive bounds; 0 = "as the entries allow": the
    /// sum of the minimums / maximums).</summary>
    public int ModulesMin { get; set; }
    public int ModulesMax { get; set; }

    /// <summary>Stations: the module the composition starts from (the arrival hall); "" = the first required entry.</summary>
    public string Start { get; set; } = string.Empty;

    /// <summary>Stations: the largest bounding box (blocks per side) a composition may grow to; 0 = 128.</summary>
    public int MaxExtent { get; set; }

    public List<KitEntry> Entries { get; set; } = new();

    // --- settlement layout (0 = the tier's procedural default) ---

    public int ColsMin { get; set; }
    public int ColsMax { get; set; }
    public int RowsMin { get; set; }
    public int RowsMax { get; set; }

    /// <summary>Plot stride in blocks (building + street margin); 0 = 8.</summary>
    public int PlotStride { get; set; }

    /// <summary>Largest building footprint per plot; 0 = 6. Must leave a lane: at most stride − 2.</summary>
    public int Building { get; set; }

    /// <summary>Storeys of the procedural buildings; 0 = the tier's default.</summary>
    public int Storeys { get; set; }

    /// <summary>True: a plot no entry fills stays an open square instead of a procedural building.</summary>
    public bool ModulesOnly { get; set; }

    // --- city layout (0 = the composer's default) ---

    /// <summary>Districts per side (odd, 3–9); 0 = 7.</summary>
    public int Grid { get; set; }

    /// <summary>District side in blocks (16–48); 0 = 32.</summary>
    public int DistrictSize { get; set; }

    /// <summary>Street width between districts (2–8); 0 = 4.</summary>
    public int Street { get; set; }

    /// <summary>Structure height; 0 = 20.</summary>
    public int Height { get; set; }

    /// <summary>Optional district map, one string per row and one letter per district: P plaza, O open, M market,
    /// H hall, G garden, T tower, R housing. Empty = the composer's own rule for any odd grid.</summary>
    public List<string> RoleMap { get; set; } = new();

    public string PackOrDefault => string.IsNullOrWhiteSpace(Pack) ? "default" : Pack;

    public string KindOrDefault => string.IsNullOrWhiteSpace(Kind) ? KindStation : Kind.Trim().ToLowerInvariant();

    /// <summary>The sum of the entries' minimum counts (a required entry counts at least one).</summary>
    public int RequiredCount
    {
        get
        {
            int n = 0;
            foreach (var e in Entries)
            {
                n += e.MinOrRequired;
            }

            return n;
        }
    }

    /// <summary>The sum of the entries' maximum counts.</summary>
    public int CapacityCount
    {
        get
        {
            int n = 0;
            foreach (var e in Entries)
            {
                n += System.Math.Max(e.MinOrRequired, e.Max);
            }

            return n;
        }
    }

    /// <summary>The effective module-count bounds: the fields when set, else what the entries allow, never
    /// below the required count nor above the capacity.</summary>
    public (int Min, int Max) EffectiveBounds()
    {
        int required = RequiredCount, capacity = CapacityCount;
        int min = ModulesMin > 0 ? ModulesMin : required;
        int max = ModulesMax > 0 ? ModulesMax : capacity;
        min = System.Math.Clamp(min, required, System.Math.Max(required, capacity));
        max = System.Math.Clamp(max, min, System.Math.Max(min, capacity));
        return (min, max);
    }
}

/// <summary>One line of a kit: a module (by key — the kit's own or a borrowed one) with its count bounds.</summary>
public sealed class KitEntry
{
    public string Module { get; set; } = string.Empty;

    /// <summary>Copies the composition always places (a required entry places at least one).</summary>
    public int Min { get; set; }

    /// <summary>Copies at most; the random draws stop at this.</summary>
    public int Max { get; set; } = 1;

    /// <summary>Mandatory: the structure is not complete without it (min ≥ 1), and a station composition that
    /// cannot dock it fails over to another attempt.</summary>
    public bool Required { get; set; }

    /// <summary>Weight of the random draws once the minimums are placed.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>Stations: whether the composer may turn the module to dock it (a hangar keeps its mouth).</summary>
    public bool Rotate { get; set; } = true;

    public int MinOrRequired => System.Math.Max(Required ? 1 : 0, Min);
}
