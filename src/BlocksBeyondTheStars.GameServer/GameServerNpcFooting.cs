// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Furniture is no floor, a rug is no wall (#1895). The NPC walk read the block id alone, so every form was a cube: a
/// table was a one-block step — the route even preferred it (over 2.6, around 4.0) — and a rug a block the NPC walked
/// across a block above the floor. These predicates add the cell's form (<see cref="NpcFootings"/>):
/// <list type="bullet">
/// <item>the route (<see cref="SearchNpcPath"/>) and the ground probe of <see cref="MoveNpcs"/> use
/// <see cref="NpcStandableAt"/> / <see cref="TryNpcGroundFeetYAt"/> — no-load reads, as before;</item>
/// <item>the movement sweep (<see cref="BlockedByWorld"/>) and every spot helper (<see cref="StandableSpot"/>) use
/// <see cref="NpcBodyBlockedAt"/>.</item>
/// </list>
/// Creatures, bandits and enemies keep <see cref="StandableAt"/> (Marcel 2026-09-14: NPCs only).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How an NPC treats the cell holding <paramref name="id"/> (meaningful for a colliding id). Reads the form
    /// without loading — the chunk is loaded wherever the id came from.</summary>
    private NpcFooting NpcFootingOf(BlockId id, Vector3i cell)
        => id.IsAir ? NpcFooting.Floor : NpcFootings.Of(_content.BlockById(id)?.Key, _world.GetShapeIfLoaded(cell));

    /// <summary>Whether an NPC's body cannot be in a cell holding <paramref name="id"/>: what the walk collides with
    /// (<see cref="IsCollidingBlock"/>, fluids and canopies included) or an unknown block — unless it is a flat plate.</summary>
    private bool NpcBodyBlocked(BlockId id, Vector3i cell)
    {
        if (id.IsAir)
        {
            return false;
        }

        bool blocks = IsCollidingBlock(id, fluidsPass: false, foliagePasses: false) || _content.BlockById(id) == null;
        return blocks && !NpcFootings.PassesBody(NpcFootingOf(id, cell));
    }

    /// <summary><see cref="NpcBodyBlocked"/> for a cell, loading its chunk (the movement sweep and the spot helpers).</summary>
    private bool NpcBodyBlockedAt(int x, int y, int z)
    {
        var cell = new Vector3i(x, y, z);
        return NpcBodyBlocked(_world.GetBlock(cell), cell);
    }

    /// <summary>How an NPC treats a cell, loading its chunk.</summary>
    private NpcFooting NpcFootingAt(int x, int y, int z)
    {
        var cell = new Vector3i(x, y, z);
        return NpcFootingOf(_world.GetBlock(cell), cell);
    }

    /// <summary>
    /// Whether an NPC can stand with its feet in the cell (no-load): feet and head cells free to the body, and the feet
    /// carried — by a real floor below (not water, not furniture) or by a plate lying in the feet cell itself.
    /// </summary>
    private bool NpcStandableAt(int x, int y, int z)
    {
        var feet = new Vector3i(x, y, z);
        var feetId = _world.GetBlockIfLoaded(feet);
        var head = new Vector3i(x, y + 1, z);
        if (NpcBodyBlocked(feetId, feet) || NpcBodyBlocked(_world.GetBlockIfLoaded(head), head))
        {
            return false;
        }

        if (IsCollidingBlock(feetId, fluidsPass: false, foliagePasses: false) && NpcFootingOf(feetId, feet) == NpcFooting.FloorPlate)
        {
            return true; // a rug or a floor panel carries the feet standing in it
        }

        var below = new Vector3i(x, y - 1, z);
        var belowId = _world.GetBlockIfLoaded(below);
        return belowId.Value != _creatureWaterId && IsCollidingBlock(belowId, fluidsPass: false, foliagePasses: false)
            && NpcFootingOf(belowId, below) == NpcFooting.Floor;
    }

    /// <summary>
    /// The NPC twin of <see cref="TryGroundFeetYAt(int, int, int, out int)"/>: the feet cell a walker at
    /// <paramref name="refY"/> comes to rest on in the column — first downward until something stops a fall, then
    /// upward — judged by <see cref="NpcStandableAt"/>. A table in the column stops the fall but is never the floor
    /// found above it, so the caller's step check turns it into a wall.
    /// </summary>
    private bool TryNpcGroundFeetYAt(int x, int z, int refY, out int feetY)
    {
        for (int r = 0; r <= CreatureGroundScan; r++)
        {
            int y = refY - r;
            if (NpcStandableAt(x, y, z))
            {
                feetY = y;
                return true;
            }

            var cell = new Vector3i(x, y, z);
            var id = _world.GetBlockIfLoaded(cell);
            if (id.Value != _creatureWaterId && NpcBodyBlocked(id, cell))
            {
                break; // something solid — nothing deeper can be reached by falling
            }
        }

        for (int r = 1; r <= CreatureGroundScan; r++)
        {
            if (NpcStandableAt(x, refY + r, z))
            {
                feetY = refY + r;
                return true;
            }
        }

        feetY = refY;
        return false;
    }

    /// <summary>Test seam (#1895): whether an NPC could stand with its feet in the cell.</summary>
    public bool NpcStandableAtForTest(int x, int y, int z) => NpcStandableAt(x, y, z);

    /// <summary>Test seam (#1895): the spot an NPC would take in this cell (<see cref="StandableSpot"/>), or null.</summary>
    public Vector3f? NpcStandableSpotForTest(int x, int y, int z) => StandableSpot(x, y, z);
}
