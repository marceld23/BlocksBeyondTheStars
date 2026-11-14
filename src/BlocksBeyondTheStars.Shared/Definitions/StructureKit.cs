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

    /// <summary>#3333: an UNDERGROUND kit (the editor's "Underground" toggle) — never offered to the surface pools; only
    /// the underground stamper (#3334, the buried G.D.S. ruins) composes it, into a cavity carved at depth. Meaningful
    /// for settlement and city kits; a station kit with the flag is warned about and treated as a surface kit.</summary>
    public bool Underground { get; set; }

    /// <summary>How many modules a composed structure has (inclusive bounds; 0 = "as the entries allow": the
    /// sum of the minimums / maximums).</summary>
    public int ModulesMin { get; set; }
    public int ModulesMax { get; set; }

    /// <summary>Stations: the module the composition starts from (the arrival hall); "" = the first required entry.</summary>
    public string Start { get; set; } = string.Empty;

    /// <summary>Stations: the largest bounding box (blocks per side) a composition may grow to; 0 = 128.</summary>
    public int MaxExtent { get; set; }

    // --- station exterior detail (#1918; 0 = none) ---

    /// <summary>Stations: how many solar wings the composer mounts on free side walls of the modules.</summary>
    public int SolarWings { get; set; }

    /// <summary>Stations: how many antenna masts the composer puts on free module roofs.</summary>
    public int Antennas { get; set; }

    /// <summary>Stations: how many domes the composer puts on free module roofs (the start module first).</summary>
    public int Domes { get; set; }

    public List<KitEntry> Entries { get; set; } = new();

    /// <summary>True on a kit kept ONLY for the placements that already use it (#1885: the first default settlement
    /// kits) — never drawn for a new structure; a pinned placement still finds it by key.</summary>
    public bool PinOnly { get; set; }

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

    // --- the planned layouts (#3602, terrain generation 41) ---

    /// <summary>The classic plot grid (empty, every kit before #3602).</summary>
    public const string LayoutPlots = "";

    /// <summary>A grown grid (#3586): ordered blocks with uneven strides, a main lane, the market in the middle, houses
    /// turned to their lane, thinning toward the edge.</summary>
    public const string LayoutGrid = "grid";

    /// <summary>Road first (#3587): a road skeleton of one of the <see cref="Forms"/>, the buildings grown along it.</summary>
    public const string LayoutGrown = "grown";

    /// <summary>Every layout, in the order the editor's stepper walks them.</summary>
    public static readonly string[] Layouts = { LayoutPlots, LayoutGrid, LayoutGrown };

    /// <summary>The village forms a grown kit may take (#3587).</summary>
    public static readonly string[] AllForms = { "street", "green", "round", "cluster", "cross", "bank" };

    /// <summary>The oldest terrain generation that may draw this kit for a fresh structure (#3583); 0 = every world. A pinned
    /// replay finds a kit by key whatever this says.</summary>
    public int MinTerrainGeneration { get; set; }

    /// <summary>Settlement kits: how the composer lays the settlement out — <see cref="LayoutPlots"/> (the classic grid of
    /// <see cref="ColsMin"/> … <see cref="ModulesOnly"/>), <see cref="LayoutGrid"/> or <see cref="LayoutGrown"/> (a pinned
    /// settlement plan, #3584).</summary>
    public string Layout { get; set; } = LayoutPlots;

    /// <summary>A grown kit's village forms (empty = every form).</summary>
    public List<string> Forms { get; set; } = new();

    /// <summary>A planned kit's main street width in blocks (1–4); 0 = 2.</summary>
    public int MainStreet { get; set; }

    /// <summary>A planned kit's largest setback of a building from its road in blocks (1–4); 0 = 3.</summary>
    public int Spacing { get; set; }

    /// <summary>A planned kit's box side in blocks (32–128); 0 = the tier's default.</summary>
    public int Extent { get; set; }

    /// <summary>A planned kit that stays on one level instead of following the terrain (#3590).</summary>
    public bool Flat { get; set; }

    /// <summary>City kits (#3599): every other district row shifted by half a district, the wall along the outline of the
    /// districts, a suburb grown outside the gates.</summary>
    public bool Grown { get; set; }

    /// <summary>A grown city kit's suburb: the settlement kit whose modules the suburb grows from ("" = none).</summary>
    public string SuburbKit { get; set; } = string.Empty;

    /// <summary>Whether this is a planned settlement layout (grid or grown) rather than the classic plots.</summary>
    public bool IsPlanned => Layout == LayoutGrid || Layout == LayoutGrown;

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
