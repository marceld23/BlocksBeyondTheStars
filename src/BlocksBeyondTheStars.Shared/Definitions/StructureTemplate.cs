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
