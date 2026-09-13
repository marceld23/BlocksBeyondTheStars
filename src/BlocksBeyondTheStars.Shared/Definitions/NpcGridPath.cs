// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Grid pathfinding for walking NPCs (#1866): A* over voxel cells a human-sized body can stand in. Pure — the
/// world is asked through two predicates — so the search is unit-tested without a server.
/// <list type="bullet">
/// <item>A <b>node</b> is a feet cell: <c>standable(cell)</c> means a floor under it and room for a body in it.</item>
/// <item><b>Moves</b> go to the four horizontal neighbours, level, one step up (the head needs room above the
/// current cell) or down one or two (the columns stepped over must be free). No diagonals: a corner is never cut,
/// so a follower sweeping the straight segment between two waypoints never grazes a wall.</item>
/// <item>A <b>door</b> cell costs a little more — a detour through an open room beats swinging a door.</item>
/// <item>The goal is reached on any node within one block horizontally and one vertically of it (a bed or a counter
/// occupies the exact goal cell, the NPC stands beside it). Bounded by a box around the start and an expansion
/// budget; null when no path fits.</item>
/// </list>
/// Coordinates are the caller's (unwrapped around the start); the predicates canonicalise.
/// </summary>
public static class NpcGridPath
{
    /// <summary>Search bounds.</summary>
    public readonly struct Limits
    {
        public Limits(int halfXZ, int halfY, int maxExpansions)
        {
            HalfXZ = halfXZ;
            HalfY = halfY;
            MaxExpansions = maxExpansions;
        }

        /// <summary>Horizontal half-extent of the search box around the start.</summary>
        public int HalfXZ { get; }

        /// <summary>Vertical half-extent of the search box around the start.</summary>
        public int HalfY { get; }

        /// <summary>Nodes the search may close before it gives up.</summary>
        public int MaxExpansions { get; }

        /// <summary>The server's defaults: a 97 × 17 × 97 box, 3000 nodes.</summary>
        public static Limits Default => new Limits(48, 8, 3000);
    }

    private static readonly (int Dx, int Dz)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>
    /// The cells from <paramref name="start"/> (excluded) to a cell next to <paramref name="goal"/> (included), or
    /// null when none is found inside the limits. <paramref name="standable"/>: a body can stand in the cell (floor
    /// below, the cell and the one above free). <paramref name="free"/>: a body can pass through the cell (not a
    /// wall). <paramref name="door"/>: the cell is a doorway (passable, slightly dearer); may be null.
    /// </summary>
    public static List<Vector3i>? Find(Vector3i start, Vector3i goal, Func<Vector3i, bool> standable, Func<Vector3i, bool> free,
        Func<Vector3i, bool>? door, Limits limits, out int expansions)
    {
        expansions = 0;
        if (Reached(start, goal))
        {
            return new List<Vector3i>();
        }

        var cameFrom = new Dictionary<Vector3i, Vector3i>();
        var cost = new Dictionary<Vector3i, float> { [start] = 0f };
        var closed = new HashSet<Vector3i>();
        var standCache = new Dictionary<Vector3i, bool>();
        var freeCache = new Dictionary<Vector3i, bool>();
        var open = new MinHeap();
        open.Push(start, Heuristic(start, goal));

        bool Stand(Vector3i c)
        {
            if (!standCache.TryGetValue(c, out bool v))
            {
                standCache[c] = v = standable(c);
            }

            return v;
        }

        bool Free(Vector3i c)
        {
            if (!freeCache.TryGetValue(c, out bool v))
            {
                freeCache[c] = v = free(c);
            }

            return v;
        }

        while (open.Count > 0)
        {
            var cur = open.Pop();
            if (!closed.Add(cur))
            {
                continue; // a stale heap entry for a node already closed at a lower cost
            }

            if (Reached(cur, goal))
            {
                return Rebuild(cameFrom, start, cur);
            }

            if (++expansions > limits.MaxExpansions)
            {
                return null;
            }

            float curCost = cost[cur];
            foreach (var (dx, dz) in Dirs)
            {
                int nx = cur.X + dx, nz = cur.Z + dz;
                if (Math.Abs(nx - start.X) > limits.HalfXZ || Math.Abs(nz - start.Z) > limits.HalfXZ)
                {
                    continue;
                }

                // Level first, then one up, then one and two down — the first standable wins (a column has one floor
                // within a step of the walker's feet in practice; the rare overhang loses a longer route, not safety).
                foreach (int dy in StepOrder)
                {
                    var next = new Vector3i(nx, cur.Y + dy, nz);
                    if (Math.Abs(next.Y - start.Y) > limits.HalfY || !Stand(next))
                    {
                        continue;
                    }

                    if (dy > 0 && !Free(new Vector3i(cur.X, cur.Y + 2, cur.Z)))
                    {
                        continue; // no head room to step up
                    }

                    if (dy < 0 && (!Free(new Vector3i(nx, cur.Y, nz)) || !Free(new Vector3i(nx, cur.Y + 1, nz))))
                    {
                        continue; // the column stepped over must be open down to the landing
                    }

                    float step = dy == 0 ? 1f : dy > 0 ? 1.4f : 1.2f;
                    if (door != null && door(next))
                    {
                        step += 0.3f;
                    }

                    float nextCost = curCost + step;
                    if (closed.Contains(next) || (cost.TryGetValue(next, out float known) && known <= nextCost))
                    {
                        break;
                    }

                    cost[next] = nextCost;
                    cameFrom[next] = cur;
                    open.Push(next, nextCost + Heuristic(next, goal));
                    break;
                }
            }
        }

        return null;
    }

    private static readonly int[] StepOrder = { 0, 1, -1, -2 };

    /// <summary>Drops the cells a walker passes on a straight line anyway: keeps every turn and every height change.</summary>
    public static List<Vector3i> Simplify(List<Vector3i> cells, Vector3i start)
    {
        var result = new List<Vector3i>();
        var prev = start;
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (i == cells.Count - 1)
            {
                result.Add(c);
                break;
            }

            var n = cells[i + 1];
            bool sameDir = c.X - prev.X == n.X - c.X && c.Z - prev.Z == n.Z - c.Z;
            bool level = c.Y == prev.Y && n.Y == c.Y;
            if (!sameDir || !level)
            {
                result.Add(c);
            }

            prev = c;
        }

        return result;
    }

    private static bool Reached(Vector3i cell, Vector3i goal)
        => Math.Abs(cell.X - goal.X) <= 1 && Math.Abs(cell.Z - goal.Z) <= 1 && Math.Abs(cell.Y - goal.Y) <= 1
           && (cell.X == goal.X || cell.Z == goal.Z); // beside, not diagonally across a corner

    private static float Heuristic(Vector3i a, Vector3i b)
        => Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) + Math.Abs(a.Y - b.Y);

    private static List<Vector3i> Rebuild(Dictionary<Vector3i, Vector3i> cameFrom, Vector3i start, Vector3i end)
    {
        var path = new List<Vector3i>();
        var c = end;
        while (!c.Equals(start))
        {
            path.Add(c);
            c = cameFrom[c];
        }

        path.Reverse();
        return path;
    }

    /// <summary>A small binary min-heap of cells by priority (netstandard2.1 has no PriorityQueue).</summary>
    private sealed class MinHeap
    {
        private readonly List<(Vector3i Cell, float Priority)> _items = new();

        public int Count => _items.Count;

        public void Push(Vector3i cell, float priority)
        {
            _items.Add((cell, priority));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_items[parent].Priority <= _items[i].Priority)
                {
                    break;
                }

                (_items[parent], _items[i]) = (_items[i], _items[parent]);
                i = parent;
            }
        }

        public Vector3i Pop()
        {
            var top = _items[0].Cell;
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, smallest = i;
                if (l < _items.Count && _items[l].Priority < _items[smallest].Priority)
                {
                    smallest = l;
                }

                if (r < _items.Count && _items[r].Priority < _items[smallest].Priority)
                {
                    smallest = r;
                }

                if (smallest == i)
                {
                    break;
                }

                (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                i = smallest;
            }

            return top;
        }
    }
}
