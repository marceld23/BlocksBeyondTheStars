// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>How a walking NPC treats a colliding cell (#1895). The server's movement code used to read the block id
/// alone, so every form was a full cube: a table was a one-block step the NPC climbed onto, and a rug a block it
/// walked across one block above the floor.</summary>
public enum NpcFooting : byte
{
    /// <summary>An ordinary block: a wall to the body, a floor to the feet above it.</summary>
    Floor = 0,

    /// <summary>A wall to the body that never carries feet: furniture, devices, crates, fences, a wall-hung plate.</summary>
    NoFloor = 1,

    /// <summary>A plate lying on the bottom of its cell (a rug, a floor sheet or panel): the body walks through it and
    /// the feet standing in its cell are carried by it.</summary>
    FloorPlate = 2,

    /// <summary>A plate hung at the top of its cell (a ceiling sheet or panel): the body walks through it, nothing
    /// stands on it.</summary>
    CeilingPlate = 3,
}

/// <summary>
/// The rules behind <see cref="NpcFooting"/> (#1895, Marcel 2026-09-14: "no furniture, no devices, no fences —
/// and the rug fix"). Pure: the server asks with the block key and the packed shape descriptor of a cell it already
/// knows collides.
/// <list type="bullet">
/// <item><b>Forms</b> any material can take that are furniture: table, chair, bench, both bed halves, fence, pot.</item>
/// <item><b>Blocks</b> that are furniture or devices whatever their form: <see cref="NoFloorBlocks"/>. Blocks used as
/// floors by the generators stay floors — the hydroponics tray is the deck plate of a greenhouse, a core can sit in
/// a deck, a pipe runs across a floor and is stepped over.</item>
/// <item><b>Plates</b> (sheet, panel) lying flat are walked through; one lying on the bottom of its cell carries the
/// feet in that cell (a rug on the floor, or a panel catwalk over a gap). A plate hung on a wall is furniture.</item>
/// </list>
/// Slabs, stairs and ramps stay floors: they are the steps people build.
/// </summary>
public static class NpcFootings
{
    /// <summary>Block keys that are never a floor to an NPC, whatever form they were placed in. The profession
    /// posts come from the profession table itself, so a post added later cannot be forgotten here (#1940: the
    /// eight posts of 2026-09 were missing, and an NPC could climb onto its own counter).</summary>
    public static readonly IReadOnlyCollection<string> NoFloorBlocks = BuildNoFloorBlocks();

    private static HashSet<string> BuildNoFloorBlocks()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            // furniture and stores
            "bed", "crew_bunk", "campfire", "crate", "wood_crate", "station_container", "flower_pot",
            // workshop devices
            "workbench", "forge", "matter_forge", "detoxifier", "algae_tank", "heal_tank",
            // terminals and posts
            "data_cache", "factory_terminal", "gaming_pc", "gaming_monitor", "gaming_keyboard", "gaming_mouse",
            "station_vendor", "mission_board", "radio_beacon", "sentry_post",
        };

        foreach (var profession in Definitions.NpcProfessions.All)
        {
            keys.Add(profession.PostBlock);
        }

        return keys;
    }

    /// <summary>How an NPC treats a colliding cell holding <paramref name="blockKey"/> in the form
    /// <paramref name="descriptor"/> (a packed <see cref="ShapeCode"/> descriptor; 0 = a plain cube).</summary>
    public static NpcFooting Of(string? blockKey, int descriptor)
    {
        int shape = ShapeCode.ShapeOf(descriptor);
        if (shape == (int)BlockShape.Sheet || shape == (int)BlockShape.Panel)
        {
            return ShapeCode.UpFaceOf(descriptor) switch
            {
                ShapeCode.UpPlusY => NpcFooting.FloorPlate,
                1 => NpcFooting.CeilingPlate, // up-face −Y: the plate hangs at the top of the cell
                _ => NpcFooting.NoFloor,      // a plate hung on a wall
            };
        }

        if (IsFurnitureForm(shape) || (blockKey != null && NoFloorBlocks.Contains(blockKey)))
        {
            return NpcFooting.NoFloor;
        }

        return NpcFooting.Floor;
    }

    /// <summary>True when a body walks through a colliding cell of this footing (the flat plates).</summary>
    public static bool PassesBody(NpcFooting footing) => footing is NpcFooting.FloorPlate or NpcFooting.CeilingPlate;

    /// <summary>True when the form is furniture: table, chair, bench, a bed half, fence or pot.</summary>
    public static bool IsFurnitureForm(int shapeIndex) => shapeIndex switch
    {
        (int)BlockShape.Table or (int)BlockShape.Chair or (int)BlockShape.Bench => true,
        (int)BlockShape.BedHead or (int)BlockShape.BedFoot => true,
        (int)BlockShape.Fence or (int)BlockShape.Pot => true,
        _ => false,
    };
}
