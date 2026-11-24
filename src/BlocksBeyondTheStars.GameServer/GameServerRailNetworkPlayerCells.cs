// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A listed rail line's stamp keeps every cell a player wrote (#4016; the network's main file is GameServerRailNetwork.cs). A
/// listed line is planned once but stamped section by section as players come near — long after the plan, on a body players
/// may have built on since; and a line re-planned on a played body (GameServerRailNetworkMigration.cs) runs over ground they
/// built on. So its halls, its pylon columns and its corridor never overwrite a cell whose edit row carries a player's name
/// (the train may then pass through what a player built in its way — their own choice), and its planner does not refuse a
/// way a player built on. Per stretch the stamp asks the store once which chunks of its box hold a player's edit
/// (<c>ListPlayerEditedChunks</c>), and only a cell in one of them is looked up on its own.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many player-edited chunks a stretch's box is asked for at most (a stretch touches a few dozen).</summary>
    private const int ListedPlayerChunkLimit = 4096;

    /// <summary>A line's stamp work for pylons <paramref name="first"/>..<paramref name="last"/>: a listed line's keeps the
    /// players' cells.</summary>
    private IntercityStampWork NewIntercityStampWork(IntercityPlan plan, string surface, int first, int last)
    {
        var work = new IntercityStampWork(plan, surface, first, last);
        if (plan.Listed)
        {
            work.PlayerCell = PlayerCellsOfStretch(plan, first, last);
        }

        return work;
    }

    /// <summary>Whether a cell of the stretch holds a player's edit: the stretch's box (its pylons, its halls, a tree's reach
    /// round them, from under the halls' foundations to over the trees) asked once for its player-edited chunks, then a cell in
    /// one of them looked up (and remembered).</summary>
    private Func<Vector3i, bool> PlayerCellsOfStretch(IntercityPlan plan, int first, int last)
    {
        int circ = _world.Circumference;
        double x0 = plan.Pylons[first].X;
        double U(int x) => x0 + WorldConstants.WrapDeltaX(x - x0, circ);
        double minX = double.MaxValue, maxX = double.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        void Grow(int x, int y, int z)
        {
            (minX, maxX) = (Math.Min(minX, U(x)), Math.Max(maxX, U(x)));
            (minZ, maxZ, minY, maxY) = (Math.Min(minZ, z), Math.Max(maxZ, z), Math.Min(minY, y), Math.Max(maxY, y));
        }

        for (int j = first; j <= last; j++)
        {
            Grow(plan.Pylons[j].X, plan.Pylons[j].Y, plan.Pylons[j].Z);
        }

        foreach (var st in plan.Stops)
        {
            if (StampsWithStretch(plan, st, first, last))
            {
                Grow(st.Origin.X, st.Origin.Y - IntercityStationDepth, st.Origin.Z);
                Grow(st.Origin.X + st.W, st.Origin.Y + st.Structure.Height, st.Origin.Z + st.L);
            }
        }

        int reach = IntercityTreeReach + 4;
        var chunks = PlayerEditedChunks((int)Math.Floor(minX) - reach, (int)Math.Ceiling(maxX) + reach,
            minY - IntercityTreeRise - IntercityStationDepth, maxY + IntercityTreeRise + 8, minZ - reach, maxZ + reach);
        if (chunks.Count == 0)
        {
            return _ => false;
        }

        var known = new Dictionary<Vector3i, bool>();
        return cell =>
        {
            cell = WorldConstants.CanonicalBlock(cell, circ);
            if (!chunks.Contains(WorldConstants.WorldToChunk(cell)))
            {
                return false;
            }

            if (!known.TryGetValue(cell, out bool player))
            {
                known[cell] = player = _repo.HasPlayerBlockEdits(_world.LocationId, cell, cell);
            }

            return player;
        };
    }

    /// <summary>The chunks (canonical) of a box that hold a player's edit — the box asked as two where it runs over the X seam.</summary>
    private HashSet<ChunkCoord> PlayerEditedChunks(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        int circ = _world.Circumference;
        var chunks = new HashSet<ChunkCoord>();
        int cx0 = WorldConstants.WrapX(x0, circ);
        int width = Math.Min(circ - 1, x1 - x0);
        var parts = cx0 + width < circ
            ? new[] { (cx0, cx0 + width) }
            : new[] { (cx0, circ - 1), (0, cx0 + width - circ) };
        foreach (var (a, b) in parts)
        {
            foreach (var c in _repo.ListPlayerEditedChunks(_world.LocationId, new Vector3i(a, y0, z0), new Vector3i(b, y1, z1), ListedPlayerChunkLimit))
            {
                chunks.Add(WorldConstants.CanonicalChunk(c, circ));
            }
        }

        return chunks;
    }
}
