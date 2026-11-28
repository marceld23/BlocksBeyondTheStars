// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// "Is this cell under water?" for everything that must agree on it — the server's oxygen drain, the client's
/// underwater wash, audio muffle and swimming, and the mesher that draws the water (#1902).
/// <para>
/// A cell holds ONE block id, so a plant, a ladder or a building form standing in water deletes the water in its
/// cell. Asking only "is the block water?" turned every kelp stalk into an air shaft you could breathe in and
/// drew a dry hole in the surface around every reed. A non-full block counts as wet when the water would flow
/// into its cell if the block were air: water directly above it, or on at least two of its four sides. One
/// water block beside a plant on the bank (a notch in the shore, the foot of a waterfall) does not flood it.
/// </para>
/// <para>
/// #4075: the water also runs down a stalk. Inside a dense kelp patch a stalk cell has the next kelp cell above it and
/// mostly kelp around it, so it failed both tests — a dry air pocket under water, and the mesher drew the water's faces
/// toward it: flat "second water surfaces" floating in the sea. A non-full cell is therefore also wet when the non-full
/// cell straight above it is wet (the <see cref="WaterSurrounds(Func{int, int, int, ushort}, ushort, Func{int, int, int, bool}, int, int, int)"/>
/// overload that takes the non-full test).
/// </para>
/// </summary>
public static class WetCell
{
    /// <summary>How many non-full cells above a cell the stalk rule follows (#4075). The tallest generated stalk (an
    /// underwater forest, #1757) is twelve cells; the cap keeps a pillar of ladders from becoming an unbounded walk.</summary>
    public const int MaxStalkCells = 16;

    /// <summary>Blocks that fill only part of their cell by nature: every plant (<c>flora_*</c> and the other catalog
    /// species, <see cref="Definitions.FloraCatalog.IsPlantKey"/>) and the slim props (torch, lantern, ladder). Building forms
    /// are non-full through their shape descriptor instead — callers that hold shape data add <c>!ShapeCode.IsCube(descriptor)</c>.</summary>
    public static bool IsNonFullKey(string? key)
        => key != null && (Definitions.FloraCatalog.IsPlantKey(key) || key is "torch" or "lantern" or "ladder");

    /// <summary>True when water surrounds the cell closely enough to flood it: directly above, or on at least two of
    /// its four horizontal sides. Without a non-full test, so without the stalk rule — see the other overload.</summary>
    public static bool WaterSurrounds(Func<int, int, int, ushort> blockAt, ushort waterId, int x, int y, int z)
        => WaterSurrounds(blockAt, waterId, null, x, y, z);

    /// <summary>True when water surrounds the cell closely enough to flood it: directly above, or on at least two of
    /// its four horizontal sides — or (#4075) when the cell straight above is non-full (<paramref name="nonFull"/>) and
    /// wet by the same rule, so the water runs down a kelp stalk to its foot (at most <see cref="MaxStalkCells"/> cells
    /// up). A null <paramref name="nonFull"/> leaves the stalk rule out.</summary>
    public static bool WaterSurrounds(Func<int, int, int, ushort> blockAt, ushort waterId, Func<int, int, int, bool>? nonFull, int x, int y, int z)
    {
        if (waterId == 0)
        {
            return false;
        }

        for (int step = 0; step <= MaxStalkCells; step++)
        {
            int cy = y + step;
            if (FloodsCell(blockAt, waterId, x, cy, z))
            {
                return true;
            }

            if (nonFull == null || blockAt(x, cy + 1, z) == 0 || !nonFull(x, cy + 1, z))
            {
                return false; // the stalk ends in air or a full block: nothing runs down from above
            }
        }

        return false;
    }

    /// <summary>The one-cell test: water directly above, or on at least two of the four horizontal sides.</summary>
    private static bool FloodsCell(Func<int, int, int, ushort> blockAt, ushort waterId, int x, int y, int z)
    {
        if (blockAt(x, y + 1, z) == waterId)
        {
            return true;
        }

        int sides = 0;
        if (blockAt(x + 1, y, z) == waterId)
        {
            sides++;
        }

        if (blockAt(x - 1, y, z) == waterId)
        {
            sides++;
        }

        if (sides < 2 && blockAt(x, y, z + 1) == waterId)
        {
            sides++;
        }

        if (sides < 2 && blockAt(x, y, z - 1) == waterId)
        {
            sides++;
        }

        return sides >= 2;
    }

    /// <summary>True when the cell is water, or holds a non-full block (<paramref name="nonFull"/>) that water
    /// surrounds (<see cref="WaterSurrounds(Func{int, int, int, ushort}, ushort, Func{int, int, int, bool}, int, int, int)"/>,
    /// stalk rule included).</summary>
    public static bool IsWet(Func<int, int, int, ushort> blockAt, ushort waterId, Func<int, int, int, bool> nonFull, int x, int y, int z)
    {
        if (waterId == 0)
        {
            return false;
        }

        ushort b = blockAt(x, y, z);
        if (b == waterId)
        {
            return true;
        }

        return b != 0 && nonFull(x, y, z) && WaterSurrounds(blockAt, waterId, nonFull, x, y, z);
    }
}
