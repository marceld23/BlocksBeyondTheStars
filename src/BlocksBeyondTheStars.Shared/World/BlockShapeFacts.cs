// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Facts about the built-in forms that gameplay rules need without the geometry (#2438). The client's fall guard
/// (the last-resort rescue for a player embedded in solid geometry) used to ask only the block KEY at chest and eye
/// height — so a carpet, a plate or a slab laid into the cell the player stands in read as "stuck in stone" and
/// teleported the player back to the last safe spot. Whether a cell can really enclose someone depends on its form.
/// </summary>
public static class BlockShapeFacts
{
    /// <summary>
    /// True when a cell of this form fills enough of its volume to trap a player standing in it — the plain cube and
    /// the forms that reach the cell's full height over most of its floor. Thin and open forms (slab, panel, sheet, low
    /// ramp, quarter cube, pot, post, beam, table, chair, fence, bench, bed) never count: the real collider already
    /// handles them, and a false rescue is far worse than a missed one (the out-of-world floor check still catches a
    /// genuine fall). A player-designed micro form (<see cref="ShapeCode.IsCustomShape"/>) counts as enclosing — its
    /// cells may be solid and the guard has no cheaper way to know.
    /// </summary>
    public static bool CanEnclosePlayer(int shapeIndex)
    {
        if (ShapeCode.IsCustomShape(shapeIndex))
        {
            return true;
        }

        return (BlockShape)shapeIndex switch
        {
            BlockShape.Cube => true,
            BlockShape.Pyramid => true,
            BlockShape.Dome => true,
            BlockShape.Sphere => true,
            BlockShape.Ramp => true,
            BlockShape.Stairs => true,
            BlockShape.Cone => true,
            BlockShape.Cylinder => true,
            _ => false,
        };
    }
}
