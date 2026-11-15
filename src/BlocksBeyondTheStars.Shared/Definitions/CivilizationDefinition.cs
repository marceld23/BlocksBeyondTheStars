// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3639: a CIVILIZATION (<c>data/civilizations/&lt;key&gt;.json</c>, docs/developer/CIVILIZATIONS.md) — a people whose
/// buildings exist only on the bodies its <see cref="Scope"/> picks. A <see cref="CivilizationRules.PlacementFramework"/>
/// civilization is stamped by the civilization stage from its own templates (<c>data/civilizations/&lt;key&gt;/*.json</c>,
/// <see cref="StructureTemplate.Civilization"/>); a <see cref="CivilizationRules.PlacementLegacy"/> one (the G.D.S., the
/// Sounders, the SPS) is a tag only: the blocks, items, kits and templates that name it are grouped under it, and its
/// structures keep their own stampers. Only <see cref="CivilizationRules.StateLost"/> exists so far: ruins, no inhabitants.
/// </summary>
public sealed class CivilizationDefinition
{
    /// <summary>Unique key, the file name by default (<c>grosstechniker</c>, <c>labyrinth_builders</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Locale key of the people's name ("the Vanished People").</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Optional locale key of what the people's beings or machines are called ("Grand Technicians").</summary>
    public string BeingsKey { get; set; } = string.Empty;

    /// <summary><see cref="CivilizationRules.StateLost"/> (ruins, nobody home) — the only state so far.</summary>
    public string State { get; set; } = CivilizationRules.StateLost;

    /// <summary><see cref="CivilizationRules.PlacementFramework"/> (stamped by the civilization stage) or
    /// <see cref="CivilizationRules.PlacementLegacy"/> (a tag only — its structures keep their own stampers).</summary>
    public string Placement { get; set; } = CivilizationRules.PlacementFramework;

    /// <summary>The first terrain generation that stamps this civilization; never below
    /// <c>WorldDescription.CivilizationsGeneration</c>.</summary>
    public int MinTerrainGeneration { get; set; }

    /// <summary>Where the civilization lives: how many home bodies, of which kind.</summary>
    public CivilizationScope Scope { get; set; } = new();

    /// <summary>What a home body gives up for the civilization: the structure kinds named in
    /// <see cref="CivilizationHomeBody.Suppress"/> are not stamped there.</summary>
    public CivilizationHomeBody HomeBody { get; set; } = new();

    /// <summary>A body kind a new galaxy guarantees so the civilization always finds a home (generation 43+).</summary>
    public CivilizationGuarantee Guarantee { get; set; } = new();

    /// <summary>Material token (<see cref="MaterialTokens"/>) → block key, for the civilization's templates and sources.</summary>
    public Dictionary<string, string> Materials { get; set; } = new();

    /// <summary>How the civilization's ruins look: rubble, overgrowth.</summary>
    public CivilizationRuin Ruin { get; set; } = new();

    /// <summary>The sites a home body carries, stamped in this order (the giant first).</summary>
    public List<CivilizationSitePool> Sites { get; set; } = new();

    /// <summary>VEGA's lines about the civilization, each spoken once per player.</summary>
    public List<CivilizationVegaLine> Vega { get; set; } = new();

    /// <summary>The Codex entry a player unlocks on the first discovery.</summary>
    public CivilizationCodex Codex { get; set; } = new();

    /// <summary>Locale keys of the rumours settlers on other worlds tell about the civilization.</summary>
    public List<string> Rumours { get; set; } = new();

    /// <summary>Whether the civilization is stamped by the civilization stage.</summary>
    public bool IsFramework => string.Equals(Placement, CivilizationRules.PlacementFramework, System.StringComparison.OrdinalIgnoreCase);
}

/// <summary>Where a civilization lives (#3639): the one rule for "one body", "bodies of one kind" and "bodies of one system".</summary>
public sealed class CivilizationScope
{
    /// <summary>How many home bodies per galaxy: <c>[min, max]</c>; one value = exactly that many.</summary>
    public List<int> Count { get; set; } = new() { 1, 1 };

    /// <summary>Optional weights of the counts <c>min..max</c> (one per count); empty = equal.</summary>
    public List<double> CountWeights { get; set; } = new();

    /// <summary>At most this many home bodies in one system (0 = no limit).</summary>
    public int PerSystem { get; set; } = 1;

    /// <summary>The planet types a home body may have; empty = any.</summary>
    public List<string> PlanetTypes { get; set; } = new();

    /// <summary>The atmospheres a home body may have (<see cref="CivilizationRules.Atmospheres"/>); empty = any.</summary>
    public List<string> Atmospheres { get; set; } = new();

    /// <summary>Further body conditions (<c>notAsteroid</c>, <c>notGas</c>, <c>notVoid</c>, <c>planet</c>, <c>moon</c>).</summary>
    public List<string> Conditions { get; set; } = new();

    /// <summary>Whether the start system may hold a home: <c>allowed</c>, <c>never</c> or <c>only</c>.</summary>
    public string StartSystem { get; set; } = CivilizationRules.StartAllowed;

    /// <summary>Whether every site of the civilization lies under water (seat <c>seabed</c>, water write mode).</summary>
    public bool Underwater { get; set; }
}

/// <summary>What a home body gives up (#3639).</summary>
public sealed class CivilizationHomeBody
{
    /// <summary>Structure kinds (<c>landmark</c>, <c>giant_wreck</c>) never stamped on a home body.</summary>
    public List<string> Suppress { get; set; } = new();
}

/// <summary>A body kind a new galaxy guarantees (#3640).</summary>
public sealed class CivilizationGuarantee
{
    /// <summary>When the galaxy's fixed systems hold no body of these types, one body becomes the first of them.</summary>
    public List<string> PlanetTypes { get; set; } = new();
}

/// <summary>A civilization's ruin look (#3639).</summary>
public sealed class CivilizationRuin
{
    /// <summary>Block keys of the rubble left on the ground (empty = the ruin pass's own).</summary>
    public List<string> Rubble { get; set; } = new();

    /// <summary>Block key of the overgrowth (empty = the ruin pass's own; <c>none</c> = no overgrowth).</summary>
    public string Overgrowth { get; set; } = string.Empty;
}

/// <summary>One pool of a civilization's sites (#3639): how many sites of <see cref="Pool"/> a home body carries.</summary>
public sealed class CivilizationSitePool
{
    /// <summary><see cref="CivilizationRules.PoolGiant"/>, <see cref="CivilizationRules.PoolCity"/> or
    /// <see cref="CivilizationRules.PoolSmall"/>.</summary>
    public string Pool { get; set; } = string.Empty;

    /// <summary><c>[min, max]</c> sites; one value = exactly that many.</summary>
    public List<int> Count { get; set; } = new() { 1, 1 };

    /// <summary>Locale key of a site's map name (empty = the template's name).</summary>
    public string NameKey { get; set; } = string.Empty;
}

/// <summary>One VEGA line of a civilization (#3642): what makes VEGA say <see cref="Key"/>, once per player.</summary>
public sealed class CivilizationVegaLine
{
    /// <summary><see cref="CivilizationRules.VegaFirst"/> (the first site found), <see cref="CivilizationRules.VegaInside"/>
    /// (inside a site's box), <see cref="CivilizationRules.VegaBlock"/> (near a block named by <see cref="Value"/>, a
    /// trailing <c>*</c> = any key with that prefix), <see cref="CivilizationRules.VegaProp"/> (near a prop of the pool
    /// <see cref="Value"/>) or <see cref="CivilizationRules.VegaAir"/> (in an air pocket of a site).</summary>
    public string Trigger { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>The locale key VEGA speaks.</summary>
    public string Key { get; set; } = string.Empty;
}

/// <summary>A civilization's Codex entry (#3642): only what players can know.</summary>
public sealed class CivilizationCodex
{
    public string TitleKey { get; set; } = string.Empty;

    public string TextKey { get; set; } = string.Empty;
}
