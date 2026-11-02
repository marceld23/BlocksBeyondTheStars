// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3097: the hunter shuttle's one hull — a stubby Guardian lander in pale steel with a red band, a cockpit screen, a red
/// sensor "eye" under it, two engine nozzles beside an open rear hatch and short fins. The same cells are the voxel hull the
/// flight view draws (a <c>ship_remote</c> design, like the raider's) and the blocks the landing stamps on the ground, so the
/// thing a player chased in space is the thing they find on the planet. Existing blocks only (no new block, no save-version
/// bump). Local cells: X across (0..6, the fins at 0 and 6), Y up (0 = the floor row), Z along (0 = the hatch, 9 = the nose).
/// </summary>
internal static class HunterShuttleHull
{
    internal const int Width = 7;
    internal const int Height = 4;
    internal const int Length = 10;

    /// <summary>The hull plating: a pale Guardian steel, apart from the raider's near-black.</summary>
    internal const int HullDye = 0x7A8494;

    /// <summary>The band along the flanks and the fins: the Guardian machines' red.</summary>
    internal const int TrimDye = 0xC8322A;

    /// <summary>The deck: a darker steel.</summary>
    internal const int FloorDye = 0x4A505A;

    /// <summary>What a cell is made of — <see cref="GameServer"/> maps each to an existing block.</summary>
    internal enum Part : byte
    {
        Plate,
        Trim,
        Floor,
        Glass,
        Window,
        Eye,
        Nozzle,
    }

    /// <summary>The shuttle's own robot dock on the ground: at the far end of the hold, its front toward the open hatch
    /// (−Z, face 5), so what it releases walks out the back.</summary>
    internal static readonly Vector3i Bay = new(3, 1, 7);

    /// <summary>Face index of the bay's front (−Z): it looks down the hold at the hatch.</summary>
    internal const int BayFront = 5;

    /// <summary>Where the cleared camp's loot crate stands — inside the hold, near the hatch.</summary>
    internal static readonly Vector3i LootCell = new(3, 1, 2);

    /// <summary>The guard's post: outside the hatch, a few steps behind the shuttle (local X/Z; Y is the ground there).</summary>
    internal const int GuardX = 3;
    internal const int GuardZ = -3;

    /// <summary>The robot docks beside the shuttle (local X/Z on the ground there, the face their front looks along: −X on
    /// the left, +X on the right) — the first two, the third only on some landings.</summary>
    internal static readonly (int X, int Z, int Front)[] OutsideDocks = { (-3, 5, 3), (9, 5, 2) };

    /// <summary>Every hull cell with its part, in a fixed order.</summary>
    internal static IReadOnlyList<(Vector3i Cell, Part Part)> Cells { get; } = Build();

    /// <summary>The dye a part carries (0 = none — glass, the eye and the nozzles keep their own look).</summary>
    internal static int DyeOf(Part part) => part switch
    {
        Part.Plate => HullDye,
        Part.Trim => TrimDye,
        Part.Floor => FloorDye,
        _ => 0,
    };

    /// <summary>Whether a local cell lies inside the hull's box (the volume the ground stamp clears).</summary>
    internal static bool InBox(int x, int y, int z) => x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Length;

    private static List<(Vector3i Cell, Part Part)> Build()
    {
        var cells = new List<(Vector3i Cell, Part Part)>();
        void Add(int x, int y, int z, Part p) => cells.Add((new Vector3i(x, y, z), p));

        for (int z = 0; z < Length; z++)
        {
            bool nose = z >= 8;
            int x0 = nose ? 2 : 1;
            int x1 = nose ? 4 : 5;
            for (int x = x0; x <= x1; x++)
            {
                Add(x, 0, z, Part.Floor);
                Add(x, 3, z, Part.Plate);
            }

            if (!nose)
            {
                // The flanks: plating below, the red band above — an engine nozzle on each rear corner.
                Add(1, 1, z, Part.Plate);
                Add(5, 1, z, Part.Plate);
                Add(1, 2, z, z == 0 ? Part.Nozzle : Part.Trim);
                Add(5, 2, z, z == 0 ? Part.Nozzle : Part.Trim);
            }
        }

        // The cockpit: side windows, the front screen, the red sensor eye under it.
        Add(2, 1, 8, Part.Plate);
        Add(4, 1, 8, Part.Plate);
        Add(2, 2, 8, Part.Window);
        Add(4, 2, 8, Part.Window);
        Add(2, 1, 9, Part.Plate);
        Add(3, 1, 9, Part.Eye);
        Add(4, 1, 9, Part.Plate);
        Add(2, 2, 9, Part.Glass);
        Add(3, 2, 9, Part.Glass);
        Add(4, 2, 9, Part.Glass);

        // Stubby fins.
        for (int z = 2; z <= 4; z++)
        {
            Add(0, 1, z, Part.Trim);
            Add(6, 1, z, Part.Trim);
        }

        return cells;
    }
}
