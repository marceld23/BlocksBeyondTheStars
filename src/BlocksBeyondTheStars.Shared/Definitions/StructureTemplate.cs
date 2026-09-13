// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// A hand-designed station / settlement template, exported from the in-game structure editor and
/// merged into a pool (<c>data/station_templates.json</c> / <c>data/settlement_templates.json</c>).
/// World-gen may roll one of these instead of the procedural generator. Cells carry either a block
/// (<see cref="TemplateCell.Kind"/> = "block", <see cref="TemplateCell.Id"/> = a block key) or an
/// interaction marker ("marker", id = vendor / mission_board / hangar / heal_tank / quarters / npc …).
/// </summary>
public sealed class StructureTemplate
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tier { get; set; } = "medium";
    public string Kind { get; set; } = string.Empty; // "station" | "settlement" (informational)

    /// <summary>Named group this template belongs to (e.g. "default", "mybuilds"); a world enables a
    /// set of packs and world-gen only rolls templates from the enabled packs. Empty ⇒ "default".</summary>
    public string Pack { get; set; } = "default";

    /// <summary>How world-gen uses this template (#1826). Empty = a WHOLE structure rolled as one piece (the
    /// classic path: the whole settlement / station is this template). A role name from
    /// <see cref="StructureRoles"/> = a building MODULE: the procedural settlement composer stamps it into a
    /// plot whose role matches (<c>house</c> / <c>market</c> / <c>board</c> / <c>greenhouse</c>), the city
    /// composer into a district (<c>city_housing</c> / <c>city_market</c> / …). A module never becomes a
    /// settlement on its own — it is excluded from the whole-template pools.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>True when this template is a building module (<see cref="Role"/> set), false for a whole structure.</summary>
    public bool IsModule => !string.IsNullOrWhiteSpace(Role);

    /// <summary>Relative selection weight within its tier sub-pool (higher = more likely). Clamped to ≥1
    /// at selection time so a 0/negative value never makes a template unpickable by accident.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>Planet-type keys this template may appear on (#1115) — empty = every world. Only consulted
    /// for settlements (stations float in space); an unknown key simply never matches, it never breaks.</summary>
    public List<string> PlanetTypes { get; set; } = new();

    /// <summary>True on the templates that existed BEFORE the pool grew (#1115: river_hamlet,
    /// hub_outpost). Structures stamped before template pinning existed replay against ONLY these, which
    /// reproduces the old selection stream draw-for-draw — their layouts never morph under a bigger pool.
    /// Never set this on a new template, and never REMOVE a template either (a missing key breaks pins).</summary>
    public bool LegacyPool { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }
    public List<TemplateCell> Cells { get; set; } = new();

    /// <summary>The pack this template belongs to, normalized ("default" when unset).</summary>
    public string PackOrDefault => string.IsNullOrWhiteSpace(Pack) ? "default" : Pack;
}

/// <summary>The module roles a settlement template can take (#1826) — the vocabulary the editor's "Use as"
/// stepper, the merge tool and the composers share. Plot modules (<see cref="PlotRoles"/>) replace one
/// building of a procedural settlement and must fit its plot (6 × 6, up to the tier's storey height); city
/// modules (<see cref="CityRoles"/>) replace one 32 × 32 district of the composed city (#1793) and carry the
/// tier <see cref="MetropolisTier"/>.</summary>
public static class StructureRoles
{
    public const string House = "house";
    public const string Market = "market";
    public const string Board = "board";
    public const string Greenhouse = "greenhouse";
    public const string CityHousing = "city_housing";
    public const string CityMarket = "city_market";
    public const string CityHall = "city_hall";
    public const string CityGarden = "city_garden";
    public const string CityTower = "city_tower";

    /// <summary>The tier value of a city-composer module: the district envelope, not a settlement size.</summary>
    public const string MetropolisTier = "metropolis";

    public static readonly string[] PlotRoles = { House, Market, Board, Greenhouse };
    public static readonly string[] CityRoles = { CityHousing, CityMarket, CityHall, CityGarden, CityTower };

    /// <summary>Every role, in the order the editor's stepper walks them (whole first).</summary>
    public static readonly string[] All = { string.Empty, House, Market, Board, Greenhouse, CityHousing, CityMarket, CityHall, CityGarden, CityTower };

    public static bool IsCityRole(string? role) => role != null && System.Array.IndexOf(CityRoles, role) >= 0;

    public static bool IsPlotRole(string? role) => role != null && System.Array.IndexOf(PlotRoles, role) >= 0;

    public static bool IsKnown(string? role) => string.IsNullOrEmpty(role) || IsPlotRole(role) || IsCityRole(role);

    /// <summary>Whether a settlement tier builds town-style (modern iron/glass, multi-storey) — the same split
    /// the procedural generator uses; a plot module only ever lands in a settlement of its own style.</summary>
    public static bool IsTownStyleTier(string? tier) => tier == "town" || tier == "city";
}

/// <summary>One cell of a <see cref="StructureTemplate"/>: a block or an interaction marker.</summary>
public sealed class TemplateCell
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Kind { get; set; } = "block"; // "block" | "marker"
    public string Id { get; set; } = string.Empty;

    /// <summary>Per-cell dye colour (0xRRGGBB; 0 = none). Applied to tintable blocks like in-game dye.</summary>
    public int Tint { get; set; }

    /// <summary>Per-cell glow colour (0xRRGGBB; 0 = none) — the coloured-light blocks.</summary>
    public int Glow { get; set; }

    /// <summary>Packed shape + orientation (<c>ShapeCode.Pack(shape, facing)</c>; 0 = plain cube).</summary>
    public int Shape { get; set; }
}
