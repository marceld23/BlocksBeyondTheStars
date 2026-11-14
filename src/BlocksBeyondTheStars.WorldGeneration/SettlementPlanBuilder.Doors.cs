// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>Turning a module so its door faces the road (#3585).</summary>
internal sealed partial class SettlementPlanBuilder
{
    /// <summary>The module turned so its front (the wall its door or entrance marker sits on) faces <paramref name="front"/>
    /// (0 −Z, 1 +Z, 2 −X, 3 +X), with the door's column after turning.</summary>
    private Shape ShapeOf(StructureTemplate m, int front)
    {
        if (_shapes.TryGetValue((m.Key, front), out var cached))
        {
            return cached;
        }

        var (side, dx, dz) = FrontDoor(m);
        int turns = TurnsBetween(side, front);
        int w = m.Width, l = m.Length;
        for (int t = 0; t < turns; t++)
        {
            (dx, dz) = (l - 1 - dz, dx);
            (w, l) = (l, w);
        }

        var shape = new Shape(w, l, dx, dz, turns);
        _shapes[(m.Key, front)] = shape;
        return shape;
    }

    /// <summary>
    /// The wall a module's front door stands on (0 −Z, 1 +Z, 2 −X, 3 +X) and the door's cell: the first door marker (or
    /// <see cref="EntranceMarker"/>) on the module's outer wall, one on −Z preferred; a module with neither counts its −Z
    /// wall's middle as its front.
    /// </summary>
    internal static (int Side, int X, int Z) FrontDoor(StructureTemplate m)
    {
        (int Side, int X, int Z)? found = null;
        foreach (var c in m.Cells)
        {
            if (c.Kind != "marker" || (!RoomFurnisher.IsDoorMarker(c.Id) && c.Id != EntranceMarker))
            {
                continue;
            }

            int side = c.Z == 0 ? 0 : c.Z == m.Length - 1 ? 1 : c.X == 0 ? 2 : c.X == m.Width - 1 ? 3 : -1;
            if (side == 0)
            {
                return (0, c.X, c.Z);
            }

            if (side > 0 && found is null)
            {
                found = (side, c.X, c.Z);
            }
        }

        return found ?? (0, m.Width / 2, 0);
    }

    /// <summary>The quarter turns (<see cref="TemplateTransform.RotateY"/>) that bring a wall from <paramref name="from"/> to
    /// <paramref name="to"/>: one turn takes −Z to +X, +X to +Z, +Z to −X and −X to −Z.</summary>
    internal static int TurnsBetween(int from, int to)
    {
        int side = from, turns = 0;
        while (side != to && turns < 4)
        {
            side = side switch { 0 => 3, 3 => 1, 1 => 2, _ => 0 };
            turns++;
        }

        return turns & 3;
    }
}
