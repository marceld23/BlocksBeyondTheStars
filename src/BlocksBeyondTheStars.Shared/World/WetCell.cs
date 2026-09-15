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
/// </summary>
public static class WetCell
{
    /// <summary>Blocks that fill only part of their cell by nature: every plant (<c>flora_*</c>) and the slim props
    /// (torch, lantern, ladder). Building forms are non-full through their shape descriptor instead — callers that
    /// hold shape data add <c>!ShapeCode.IsCube(descriptor)</c>.</summary>
    public static bool IsNonFullKey(string? key)
        => key != null && (key.StartsWith("flora_", StringComparison.Ordinal) || key is "torch" or "lantern" or "ladder");

    /// <summary>True when water surrounds the cell closely enough to flood it: directly above, or on at least two of
    /// its four horizontal sides.</summary>
    public static bool WaterSurrounds(Func<int, int, int, ushort> blockAt, ushort waterId, int x, int y, int z)
    {
        if (waterId == 0)
        {
            return false;
        }

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
    /// surrounds (<see cref="WaterSurrounds"/>).</summary>
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

        return b != 0 && nonFull(x, y, z) && WaterSurrounds(blockAt, waterId, x, y, z);
    }
}
