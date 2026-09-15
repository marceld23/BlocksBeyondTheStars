// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// A named piece of a built-in building form (#1900). The client's shape geometry tags every box with one, and a
/// block in <c>data/blocks.json</c> may give each part its own texture (<see cref="Definitions.BlockFaceTexture"/>) —
/// the bed's blanket, pillow and boards, the flower pot's body and rim. Everything else is <see cref="Body"/>.
/// The JSON names are the snake_case member names (<see cref="ShapeParts.Name"/>). Values are stable: append only.
/// </summary>
public enum ShapePart : byte
{
    Body = 0,
    BedHead = 1,   // the head half's mattress (BlockShape.BedHead)
    BedFoot = 2,   // the foot half's mattress (BlockShape.BedFoot)
    Pillow = 3,
    Headboard = 4,
    Footboard = 5,
    Rim = 6,       // the flower pot's rim (BlockShape.Pot's body is Body)
}

/// <summary>Which way a face of a form looks in the form's OWN frame, before yaw and tilt — so a ladder plate's big
/// face stays its "top" when it hangs on a wall.</summary>
public enum FaceSide : byte
{
    Top = 0,
    Bottom = 1,
    Side = 2,
}

/// <summary>Names and counts for <see cref="ShapePart"/> and <see cref="FaceSide"/>.</summary>
public static class ShapeParts
{
    /// <summary>Number of <see cref="ShapePart"/> values (table size for per-part lookups).</summary>
    public const int PartCount = 7;

    /// <summary>Number of <see cref="FaceSide"/> values.</summary>
    public const int SideCount = 3;

    private static readonly string[] PartNames = { "body", "bed_head", "bed_foot", "pillow", "headboard", "footboard", "rim" };
    private static readonly string[] SideNames = { "top", "bottom", "side" };

    /// <summary>The data name of a part, e.g. <c>"bed_head"</c>.</summary>
    public static string Name(ShapePart part) => PartNames[(int)part];

    /// <summary>The data name of a side, e.g. <c>"top"</c>.</summary>
    public static string Name(FaceSide side) => SideNames[(int)side];

    /// <summary>Parses a part name; false for anything unknown.</summary>
    public static bool TryParsePart(string? name, out ShapePart part)
    {
        int i = System.Array.IndexOf(PartNames, name);
        part = i >= 0 ? (ShapePart)i : ShapePart.Body;
        return i >= 0;
    }

    /// <summary>Parses a side name; false for anything unknown.</summary>
    public static bool TryParseSide(string? name, out FaceSide side)
    {
        int i = System.Array.IndexOf(SideNames, name);
        side = i >= 0 ? (FaceSide)i : FaceSide.Top;
        return i >= 0;
    }
}
