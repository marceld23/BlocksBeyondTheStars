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

    /// <summary>The structure KIT this module belongs to (#1873): the module name every segment that fits together
    /// shares ("station_small_1"). A kit entry (<see cref="StructureKit"/>) lists the modules by key, so a module
    /// may also be borrowed by other kits. Empty = not a kit module.</summary>
    public string Kit { get; set; } = string.Empty;

    /// <summary>What a kit module is for (#1873): a station function (<see cref="StructureRoles.StationFunctions"/>:
    /// hub, corridor, cabins, canteen, bar, market, mission, medbay, hydro, storage, hangar, room) or, for settlement
    /// and city kits, one of the plot / district roles. Empty falls back to <see cref="Role"/>.</summary>
    public string Function { get; set; } = string.Empty;

    /// <summary>The function of a module, whichever field carries it (<see cref="Function"/> first, else <see cref="Role"/>).</summary>
    public string FunctionOrRole => string.IsNullOrWhiteSpace(Function) ? Role : Function;

    /// <summary>True on a template that stays ONLY for pinned replays (#1874: the four original tiny stations) —
    /// never rolled for a new structure, but a world that pinned it keeps it. Never remove such a template.</summary>
    public bool PinOnly { get; set; }

    /// <summary>True when this template is a building module (<see cref="Role"/> or <see cref="Kit"/> set), false
    /// for a whole structure.</summary>
    public bool IsModule => !string.IsNullOrWhiteSpace(Role) || !string.IsNullOrWhiteSpace(Kit);

    /// <summary>Who a settlement module is built for (#1885): empty = human, <see cref="StyleAlien"/> = the alien
    /// variant (crystal, other roofs). A kit only puts modules of the settlement's own inhabitants into its plots.</summary>
    public string Style { get; set; } = string.Empty;

    public const string StyleAlien = "alien";

    /// <summary>Whether this module is an alien variant (<see cref="Style"/>).</summary>
    public bool IsAlienStyle => string.Equals(Style, StyleAlien, System.StringComparison.OrdinalIgnoreCase);

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

    /// <summary>The village / town meeting place (#1885): tables and a counter, the residents' evening seats. It takes a
    /// dwelling plot.</summary>
    public const string Tavern = "tavern";

    /// <summary>A craftsman's workshop (#1885): workbench, forge, crates. It takes a dwelling plot.</summary>
    public const string Workshop = "workshop";

    // --- the profession buildings (2026-09, NpcProfessions): each houses one profession post; it takes a dwelling plot ---

    public const string Clinic = "clinic";
    public const string Shop = "shop";
    public const string Armory = "armory";
    public const string Library = "library";
    public const string Stable = "stable";
    public const string Quarry = "quarry";
    public const string Studio = "studio";
    public const string Newsroom = "newsroom";

    public static readonly string[] PlotRoles = { House, Market, Board, Greenhouse, Tavern, Workshop, Clinic, Shop, Armory, Library, Stable, Quarry, Studio, Newsroom };
    public static readonly string[] CityRoles = { CityHousing, CityMarket, CityHall, CityGarden, CityTower };

    /// <summary>Every role, in the order the editor's stepper walks them (whole first).</summary>
    public static readonly string[] All =
    {
        string.Empty, House, Market, Board, Greenhouse, Tavern, Workshop, Clinic, Shop, Armory, Library, Stable, Quarry, Studio, Newsroom,
        CityHousing, CityMarket, CityHall, CityGarden, CityTower,
    };

    public static bool IsCityRole(string? role) => role != null && System.Array.IndexOf(CityRoles, role) >= 0;

    public static bool IsPlotRole(string? role) => role != null && System.Array.IndexOf(PlotRoles, role) >= 0;

    public static bool IsKnown(string? role) => string.IsNullOrEmpty(role) || IsPlotRole(role) || IsCityRole(role);

    /// <summary>Whether a settlement tier builds town-style (modern iron/glass, multi-storey) — the same split
    /// the procedural generator uses; a plot module only ever lands in a settlement of its own style.</summary>
    public static bool IsTownStyleTier(string? tier) => tier == "town" || tier == "city";

    // --- station kit functions (#1873) ---

    public const string Hub = "hub";
    public const string Corridor = "corridor";
    public const string Cabins = "cabins";
    public const string Canteen = "canteen";
    public const string Bar = "bar";
    public const string Mission = "mission";
    public const string Medbay = "medbay";
    public const string Hydro = "hydro";
    public const string Storage = "storage";
    public const string Hangar = "hangar";
    public const string Room = "room";

    /// <summary>What a station kit module can be — the vocabulary the composer's furnishing and crew posts read.
    /// <see cref="Market"/> is shared with the settlement plot roles (a market is a market).</summary>
    public static readonly string[] StationFunctions = { Hub, Corridor, Cabins, Canteen, Bar, Market, Mission, Medbay, Hydro, Storage, Hangar, Room };

    /// <summary>Functions a canteen or bar module's rooms count as a lounge: where the crew sits in the evening.</summary>
    public static bool IsLoungeFunction(string? function) => function == Canteen || function == Bar;

    public static bool IsStationFunction(string? function) => function != null && System.Array.IndexOf(StationFunctions, function) >= 0;

    /// <summary>The functions a kit of <paramref name="kind"/> (station / settlement / city) accepts on its modules.</summary>
    public static string[] FunctionsForKind(string? kind) => kind switch
    {
        StructureKit.KindStation => StationFunctions,
        StructureKit.KindCity => CityRoles,
        _ => PlotRoles,
    };

    /// <summary>Whether <paramref name="function"/> is one a module of a <paramref name="kind"/> kit may carry — any
    /// unknown word counts as a dwelling / a plain room, so an author's "tavern" is never rejected.</summary>
    public static bool IsKnownFunction(string? kind, string? function)
        => !string.IsNullOrEmpty(function);
}

/// <summary>
/// Material TOKENS (#1885, Marcel 2026-09-14: "walls follow the biome"): a block cell of a settlement module may name a
/// token instead of a block key, and the composer puts the settlement's own material there — the rules the procedural
/// buildings use: a village-style wall is the planet's surface block, a town-style wall iron; accents are crystal for
/// aliens, glass for towns, carbon for villages. Complete templates and city districts resolve them the same way.
/// </summary>
public static class MaterialTokens
{
    public const string Wall = "@wall";
    public const string Accent = "@accent";
    public const string Roof = "@roof";
    public const string Floor = "@floor";
    public const string Path = "@path";

    public static readonly string[] All = { Wall, Accent, Roof, Floor, Path };

    public static bool IsToken(string? id) => id != null && id.Length > 1 && id[0] == '@' && System.Array.IndexOf(All, id) >= 0;
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

    /// <summary>
    /// A docking PORT on a wall block (#1873, block cells only): <c>tag[:door]</c> — the tag says which ports fit
    /// together (<c>door</c>, <c>wide</c>, <c>ladder</c>, or any word; equal tag + equal rectangle + opposite faces
    /// dock), the door option says what fills the opened joint: <c>slide</c> (default), <c>energy</c>, <c>hinge</c>
    /// or <c>open</c> (no door). The block itself stays in the data: it is the seal while nothing docks here, and it
    /// is cut away when a module docks. Empty = a plain block.
    /// </summary>
    public string Port { get; set; } = string.Empty;
}
