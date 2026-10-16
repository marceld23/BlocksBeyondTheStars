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
/// <item><b>Moves</b> go to the four horizontal neighbours, level, one step up or down one or two. A one-block step
/// needs only the cell it lands on to be standable — the body stands in one column at a time, as the walk moves it —
/// so the usual player staircase is walkable: one-block steps in a three-high room with the ceiling hole open above
/// the upper steps only (#2678). Only a drop of two needs the column it walks out over to be open at its old height.
/// No diagonals: a corner is never cut, so a follower sweeping the straight segment between two waypoints never
/// grazes a wall.</item>
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

                    // #2678: a one-block step (up or down) needs nothing beyond the standable landing. The walk (MoveNpcs)
                    // lifts or lowers the body the moment its centre crosses into the new column and checks only that
                    // column, so the head room above the lower step's column — the ceiling edge of a staircase hole — is
                    // never needed; the capsule's edge only brushes it while crossing. Asking for it (above the step a
                    // walker came FROM) made the usual player staircase, whose hole is open above the upper steps only,
                    // a wall to every resident although the player walks it.
                    if (dy < -1 && (!Free(new Vector3i(nx, cur.Y, nz)) || !Free(new Vector3i(nx, cur.Y + 1, nz))))
                    {
                        continue; // a drop of two walks out over the column at its old height: open down to the landing
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

    /// <summary>Half the width of the strip a smoothed segment must clear (blocks). A person is ~0.6 wide, but the follower
    /// also leaves a waypoint up to its arrival radius (0.45) early, so the strip covers that drift, not just the body.</summary>
    public const float SmoothHalfWidth = 0.45f;

    /// <summary>
    /// String-pulling (#2669): the waypoints of <paramref name="cells"/> (a <see cref="Find"/> result) with every stretch on
    /// one level pulled straight. The search moves in four directions only, so a walk across an open square came out as a
    /// staircase of 90° corners; here each kept waypoint reaches ahead to the farthest later cell of the same level that a
    /// strip <see cref="SmoothHalfWidth"/> to each side can walk to in a straight line (the first leg stays the move to the
    /// neighbour of the start cell) — every cell the body's swath touches must be
    /// <paramref name="standable"/>, a line through a grid corner checks both cells beside it, and a
    /// <paramref name="door"/> is only passed straight through its own row or column. Height changes stay exactly as the
    /// search found them (the cell before the step and the step itself), so the route never clips a wall or a ledge the
    /// grid route avoided.
    /// </summary>
    public static List<Vector3i> Smooth(List<Vector3i> cells, Vector3i start, Func<Vector3i, bool> standable, Func<Vector3i, bool>? door)
    {
        var result = new List<Vector3i>();
        var standCache = new Dictionary<Vector3i, bool>();
        bool Stand(Vector3i c)
        {
            if (!standCache.TryGetValue(c, out bool v))
            {
                standCache[c] = v = standable(c);
            }

            return v;
        }

        if (cells.Count == 0)
        {
            return result;
        }

        // The walker stands anywhere in its start cell, not at its centre: its first leg stays the search's own move to the
        // neighbour (the 2×1 box of the two cells holds that line from any point of the start cell); pulling starts there.
        result.Add(cells[0]);
        var anchor = cells[0];
        int i = 1;
        while (i < cells.Count)
        {
            if (cells[i].Y != anchor.Y)
            {
                result.Add(cells[i]); // the step itself, from the cell beside it — exactly the search's move
                anchor = cells[i];
                i++;
                continue;
            }

            int k = i;
            while (k + 1 < cells.Count && cells[k + 1].Y == anchor.Y)
            {
                k++;
            }

            int j = i;
            for (int t = i + 1; t <= k; t++)
            {
                if (!StraightClear(anchor, cells[t], Stand, door))
                {
                    break;
                }

                j = t;
            }

            result.Add(cells[j]);
            anchor = cells[j];
            i = j + 1;
        }

        return result;
    }

    /// <summary>Whether a body walks in a straight line from the centre of <paramref name="a"/> to the centre of
    /// <paramref name="b"/> (same level): its centre line and both edges of its swath cross standable cells only.</summary>
    private static bool StraightClear(Vector3i a, Vector3i b, Func<Vector3i, bool> stand, Func<Vector3i, bool>? door)
    {
        int ddx = b.X - a.X, ddz = b.Z - a.Z;
        if (Math.Abs(ddx) + Math.Abs(ddz) <= 1)
        {
            return true; // a neighbour — the search's own move
        }

        bool axis = ddx == 0 || ddz == 0;
        double len = Math.Sqrt((ddx * ddx) + (ddz * ddz));
        double nx = -ddz / len * SmoothHalfWidth, nz = ddx / len * SmoothHalfWidth;
        double ax = a.X + 0.5, az = a.Z + 0.5, bx = b.X + 0.5, bz = b.Z + 0.5;
        bool Cell(int x, int z)
        {
            var c = new Vector3i(x, a.Y, z);
            return stand(c) && (axis || door == null || !door(c));
        }

        return Traverse(ax, az, bx, bz, Cell)
            && Traverse(ax + nx, az + nz, bx + nx, bz + nz, Cell)
            && Traverse(ax - nx, az - nz, bx - nx, bz - nz, Cell);
    }

    /// <summary>Visits every grid cell the segment crosses (Amanatides–Woo), both side cells where it passes exactly
    /// through a corner; false as soon as one is not <paramref name="ok"/>.</summary>
    private static bool Traverse(double x0, double z0, double x1, double z1, Func<int, int, bool> ok)
    {
        int cx = (int)Math.Floor(x0), cz = (int)Math.Floor(z0);
        int ex = (int)Math.Floor(x1), ez = (int)Math.Floor(z1);
        if (!ok(cx, cz))
        {
            return false;
        }

        double dx = x1 - x0, dz = z1 - z0;
        int sx = Math.Sign(dx), sz = Math.Sign(dz);
        double tDeltaX = sx != 0 ? Math.Abs(1.0 / dx) : double.PositiveInfinity;
        double tDeltaZ = sz != 0 ? Math.Abs(1.0 / dz) : double.PositiveInfinity;
        double tMaxX = sx > 0 ? (cx + 1 - x0) / dx : sx < 0 ? (x0 - cx) / -dx : double.PositiveInfinity;
        double tMaxZ = sz > 0 ? (cz + 1 - z0) / dz : sz < 0 ? (z0 - cz) / -dz : double.PositiveInfinity;
        // Step while the next cell border is still on the segment (t ≤ 1) — robust against rounding, unlike walking
        // until the end cell is hit; the end cell is checked explicitly below.
        for (int guard = 0; Math.Min(tMaxX, tMaxZ) <= 1.0 - 1e-9 && guard < 4096; guard++)
        {
            if (Math.Abs(tMaxX - tMaxZ) < 1e-9)
            {
                if (!ok(cx + sx, cz) || !ok(cx, cz + sz))
                {
                    return false; // squeezing through a corner touches both cells beside it
                }

                cx += sx;
                cz += sz;
                tMaxX += tDeltaX;
                tMaxZ += tDeltaZ;
            }
            else if (tMaxX < tMaxZ)
            {
                cx += sx;
                tMaxX += tDeltaX;
            }
            else
            {
                cz += sz;
                tMaxZ += tDeltaZ;
            }

            if (!ok(cx, cz))
            {
                return false;
            }
        }

        return ok(ex, ez);
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
