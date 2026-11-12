// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>What a ship layout cell becomes in a built hull (<see cref="ShipCellBlocks.Map"/>).</summary>
public enum ShipCellRole
{
    /// <summary>A block — a content block key or a special element (glass, a light, an engine).</summary>
    Block,

    /// <summary>A station tile: its themed machine block, and a station anchor in a flyable ship.</summary>
    Station,

    /// <summary>An open hatch: no block.</summary>
    Opening,

    /// <summary>A doorway: no block — a flyable ship hangs a slide door there, a wreck leaves it open.</summary>
    Door,
}

/// <summary>One mapped layout cell: its role, the block it stamps as (air for an opening or a door) and the authored dye,
/// glow and shape — which only a plain block cell carries (<see cref="CarriesModifiers"/>).</summary>
public readonly struct ShipCellBlock
{
    public ShipCellBlock(ShipCellRole role, BlockId block, bool carriesModifiers = false, int tint = 0, int glow = 0, int shape = 0,
        bool unknownId = false)
    {
        Role = role;
        Block = block;
        CarriesModifiers = carriesModifiers;
        Tint = tint;
        Glow = glow;
        Shape = shape;
        UnknownId = unknownId;
    }

    public ShipCellRole Role { get; }

    public BlockId Block { get; }

    /// <summary>True for a cell named by a block key: it stamps with its authored dye, glow and shape. Station tiles and
    /// the special elements stamp the plain block.</summary>
    public bool CarriesModifiers { get; }

    public int Tint { get; }

    public int Glow { get; }

    public int Shape { get; }

    /// <summary>The cell named a block the content does not know; it stamps as hull (<see cref="ShipCellPalette.Wall"/>).</summary>
    public bool UnknownId { get; }
}

/// <summary>The blocks a ship hull is built from, resolved once against the content (with the fallbacks the flight hull
/// has always used: a missing block falls back to the hull or the plain glass).</summary>
public sealed class ShipCellPalette
{
    private ShipCellPalette()
    {
    }

    /// <summary>The hull (<c>iron_wall</c>); air when the content has none — then nothing can be built.</summary>
    public BlockId Wall { get; private set; }

    public BlockId Glass { get; private set; }

    /// <summary>The cockpit's front screen (#1283).</summary>
    public BlockId GlassClear { get; private set; }

    /// <summary>Dark plating (<c>carbon</c>, else <c>basalt</c>, else the hull).</summary>
    public BlockId Dark { get; private set; }

    /// <summary>The engine block (#2162): <see cref="ShipCellBlocks.EngineNozzleBlock"/>, else dark plating.</summary>
    public BlockId Nozzle { get; private set; }

    public BlockId LightWhite { get; private set; }

    public BlockId LightRed { get; private set; }

    public BlockId LightGreen { get; private set; }

    public static ShipCellPalette From(GameContent content)
    {
        var wall = content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        var glass = content.GetBlock("glass")?.NumericId ?? wall;
        var dark = content.GetBlock("carbon")?.NumericId ?? content.GetBlock("basalt")?.NumericId ?? wall;
        var lightW = content.GetBlock("light_white")?.NumericId ?? glass;
        return new ShipCellPalette
        {
            Wall = wall,
            Glass = glass,
            GlassClear = content.GetBlock("glass_clear")?.NumericId ?? glass,
            Dark = dark,
            Nozzle = content.GetBlock(ShipCellBlocks.EngineNozzleBlock)?.NumericId ?? dark,
            LightWhite = lightW,
            LightRed = content.GetBlock("light_red")?.NumericId ?? lightW,
            LightGreen = content.GetBlock("light_green")?.NumericId ?? lightW,
        };
    }
}

/// <summary>
/// #3521: the one mapping from a ship layout cell (<see cref="ShipLayoutCell"/>) to the block it is built from — shared by
/// the flyable ship (the server's flight hull) and the wreck built from the same layout
/// (<see cref="WreckGenerator.GenerateFromLayout"/>), so a ship and its wreck cannot drift apart.
/// </summary>
public static class ShipCellBlocks
{
    /// <summary>#2162: the block a layout's <c>engine</c> element and the box ship's rear nozzles stamp as — the client's
    /// exhaust plumes are placed on it.</summary>
    public const string EngineNozzleBlock = "engine_nozzle";

    /// <summary>The block a station cell stamps as (#1009). Each marker is the themed machine block whose world function
    /// matches the station's — heal tank heals/anchors spawn like the medbay, the workbench crafts like the workshop, the
    /// bed is the home-spawn quarters analogue, the crate the storage box — so the rooms read at a glance. Any other
    /// station type stamps as hull.</summary>
    public static string StationBlockKey(string station) => station switch
    {
        "medbay" => "heal_tank",
        "cockpit" => "data_cache",
        "workshop" => "workbench",
        "quarters" => "bed",
        "cargo" => "crate",
        "console" => "data_cache", // #1073: the ship console reads as a terminal instead of vanishing into the hull
        _ => "iron_wall",
    };

    /// <summary>Whether a layout cell is a doorway (a door the flyable ship hangs, an open way into a wreck).</summary>
    public static bool IsDoorId(string? id) => id is "door_slide" or "door_hinge" or "door_energy";

    /// <summary>
    /// What one layout cell becomes: a station tile its themed block (<see cref="StationBlockKey"/>); a hatch an opening;
    /// a door a doorway; the special elements glass, the lights and the engine their blocks; any other id the block of that
    /// key with the authored dye, glow and shape — an unknown id stamps as hull (<see cref="ShipCellBlock.UnknownId"/>).
    /// </summary>
    public static ShipCellBlock Map(ShipLayoutCell cell, ShipCellPalette palette, GameContent content)
    {
        if (cell.Kind == "station")
        {
            return new ShipCellBlock(ShipCellRole.Station, content.GetBlock(StationBlockKey(cell.Id))?.NumericId ?? palette.Wall);
        }

        switch (cell.Id)
        {
            case "hatch":
                return new ShipCellBlock(ShipCellRole.Opening, BlockId.Air);
            case "door_slide":
            case "door_hinge":
            case "door_energy": // ship doors all register as energy doors anyway (item 35)
                return new ShipCellBlock(ShipCellRole.Door, BlockId.Air);
            case "glass":
                return new ShipCellBlock(ShipCellRole.Block, palette.Glass);
            case "light":
            case "headlight":
                return new ShipCellBlock(ShipCellRole.Block, palette.LightWhite);
            case "light_red":
                return new ShipCellBlock(ShipCellRole.Block, palette.LightRed);
            case "light_green":
                return new ShipCellBlock(ShipCellRole.Block, palette.LightGreen);
            case "engine":
                return new ShipCellBlock(ShipCellRole.Block, palette.Nozzle);
        }

        var block = content.GetBlock(cell.Id);
        return new ShipCellBlock(ShipCellRole.Block, block?.NumericId ?? palette.Wall, carriesModifiers: true, cell.Tint, cell.Glow,
            cell.Shape, unknownId: block == null);
    }

    /// <summary>
    /// The columns (x, z) the hull encloses — those with a floor or a roof cell inside the layout's interior box. A built
    /// hull guarantees a solid floor cell (row 0) in each of them, so nobody falls out of the walkable interior; the
    /// notches of a non-rectangular plan and the cells under an exterior attachment (a nav light on a flank) are not in it.
    /// </summary>
    public static HashSet<(int X, int Z)> FloorFootprint(ShipLayout layout)
    {
        var footprint = new HashSet<(int X, int Z)>();
        foreach (var cell in layout.Cells)
        {
            if ((cell.Y == 0 || cell.Y == layout.Height) &&
                cell.X >= 0 && cell.X < layout.Width && cell.Z >= 0 && cell.Z < layout.Length)
            {
                footprint.Add((cell.X, cell.Z));
            }
        }

        return footprint;
    }
}
