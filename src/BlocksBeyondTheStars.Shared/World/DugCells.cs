// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// #4011: which removals make a cell a DUG cell (<see cref="ChunkData.IsDugLocal"/>) — the one rule the server (from the edit
/// rows at chunk load, and live in <c>ServerWorld.SetBlock</c>) and the client (live in <c>ClientWorld.ApplyBlockChange</c>)
/// share. A cell is dug when its NATURAL solid terrain (a rock, soil or ore cube) was replaced: a plant picked off the
/// ground, a tree felled, water drained or snow that weather laid and that melted again leave the cell natural, so the
/// terrain forms around them stay.
/// </summary>
public static class DugCells
{
    /// <summary>Whether removing a natural cell of this block makes the cell dug: a solid, non-liquid terrain or ore block
    /// (lava is declared solid but flows like water — <c>BlockShape</c> keeps it out the same way).</summary>
    public static bool Counts(BlockDefinition? natural)
        => natural is { Solid: true, Liquid: false } && natural.Key != "lava"
           && (natural.Category == "terrain" || natural.Category == "ore");

    /// <summary>The live rule: a change of a cell to air digs it when the cell held natural terrain — a counting block
    /// (<see cref="Counts"/>) that no stored edit had placed (<paramref name="previousEdited"/> false; a player's own block
    /// or weather snow on natural air leaves natural air behind). A dug cell stays dug.</summary>
    public static bool DigsLive(BlockDefinition? previous, bool previousEdited, bool nowAir)
        => nowAir && !previousEdited && Counts(previous);

    /// <summary>The numeric ids of every counting block (<see cref="Counts"/>) — the client's live table.</summary>
    public static List<ushort> CountingIds(IEnumerable<BlockDefinition> blocks)
    {
        var ids = new List<ushort>();
        foreach (var block in blocks)
        {
            if (Counts(block) && !block.NumericId.IsAir)
            {
                ids.Add(block.NumericId.Value);
            }
        }

        return ids;
    }
}
