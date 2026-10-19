// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where the flight view caps a ship's rear hatch with its blue energy panel (#2587). Screelit built a few leaves
    /// onto his ship and the panel at the helm's exterior view grew past the hull: the old guess took every air cell in
    /// the bounding box of the stern plane as "the opening", with the plane's box grown by every leaf beside the stern.
    /// <para>A current server names the hatch itself (<c>SpaceShipDesign.HatchX/Y/Z</c>, the opening cells of the design's
    /// own doors that open to the outside): the panels sit exactly on them. An older server sends none, and the guess
    /// runs — made robust: only cells inside the design box count (decorations around the hull are no wall), only air
    /// ENCLOSED in the stern plane is an opening (a flood from the plane's edge reaches everything else), the panel covers
    /// the largest such opening and so stays inside the wall, and the cells of the doors the owner built (#2119) are
    /// filled — their own energy field closes them, so the hatch panel never doubles it (#2607).</para>
    /// <para>The marker cube that pulses on a spacewalk (the "board here" sign) sits on the chosen wall plane under the
    /// hatch, not on the hull's global rear, where a leaf behind the stern used to push it into empty space.</para>
    /// Unity-free, unit-tested headless.
    /// </summary>
    public static class ShipHatch
    {
        /// <summary>A wall face counts as the stern when its cells fill at least this share of their bounding box.</summary>
        public const float WallFill = 0.55f;

        /// <summary>…and when that box holds at least this many cells (a stray block is no wall).</summary>
        public const int MinWallArea = 6;

        /// <summary>One opening to cap, in structure cells (both corners inclusive). A panel in the stern stands across
        /// Z (<see cref="FacesZ"/>); one in a side wall (a self-built ship's side door) stands across X.</summary>
        public readonly struct Panel
        {
            public Panel(int minX, int maxX, int minY, int maxY, int minZ, int maxZ, bool facesZ)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
                MinZ = minZ;
                MaxZ = maxZ;
                FacesZ = facesZ;
            }

            public int MinX { get; }

            public int MaxX { get; }

            public int MinY { get; }

            public int MaxY { get; }

            public int MinZ { get; }

            public int MaxZ { get; }

            /// <summary>True when the panel stands across Z (in a stern or bow wall, the wall runs along X); false when
            /// it stands across X.</summary>
            public bool FacesZ { get; }
        }

        /// <summary>What the flight view draws: the panels (empty = nothing to cap — a sealed stern) and the marker's
        /// anchor in structure cell units (x and y are the cube's centre, z the rear face of the wall plane it sits on).</summary>
        public sealed class Plan
        {
            public Plan(IReadOnlyList<Panel> panels, bool fromServer, bool hasMarker, float markerX, float markerY, float markerZ)
            {
                Panels = panels;
                FromServer = fromServer;
                HasMarker = hasMarker;
                MarkerX = markerX;
                MarkerY = markerY;
                MarkerZ = markerZ;
            }

            public IReadOnlyList<Panel> Panels { get; }

            /// <summary>True when the panels came from the server's hatch cells, false when they were guessed.</summary>
            public bool FromServer { get; }

            /// <summary>False when no stern wall was found at all — the caller keeps its old marker spot.</summary>
            public bool HasMarker { get; }

            public float MarkerX { get; }

            public float MarkerY { get; }

            public float MarkerZ { get; }
        }

        private static readonly Plan Nothing = new Plan(Array.Empty<Panel>(), false, false, 0f, 0f, 0f);

        /// <summary>
        /// Finds the hatch panels of a hull. <paramref name="cells"/> are its solid cells; <paramref name="width"/>,
        /// <paramref name="height"/>, <paramref name="length"/> the design box (x 0…width−1, y 0…height, z 0…length−1 —
        /// the server's own design extents; all three 0 = unknown, the cells' bounds stand in). <paramref name="serverHatch"/>
        /// are the server's hatch cells: null = an older server, the guess runs; EMPTY = the server says no doorway opens to
        /// space — no panel, only the marker on the stern the guess finds. <paramref name="ownerDoors"/> are the base cells
        /// of the doors the owner built; each fills its cell and the two above it.
        /// </summary>
        public static Plan Find(
            IEnumerable<(int X, int Y, int Z)> cells,
            int width,
            int height,
            int length,
            IEnumerable<(int X, int Y, int Z)>? serverHatch = null,
            IEnumerable<(int X, int Y, int Z)>? ownerDoors = null)
        {
            if (cells is null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            if (serverHatch == null)
            {
                return Guess(cells, width, height, length, ownerDoors);
            }

            var hatch = new HashSet<(int X, int Y, int Z)>(serverHatch);
            if (hatch.Count > 0)
            {
                return FromServerCells(hatch, new HashSet<(int X, int Y, int Z)>(cells));
            }

            // The server knows the ship has no doorway open to space (a room built behind the stern, a sealed self-built
            // hull): nothing to cap, whatever the guess would take for an opening — the marker still marks the stern.
            var guess = Guess(cells, width, height, length, ownerDoors);
            return new Plan(Array.Empty<Panel>(), true, guess.HasMarker, guess.MarkerX, guess.MarkerY, guess.MarkerZ);
        }

        /// <summary>The server path: one panel per connected group of hatch cells, the rearmost first; the marker sits
        /// under the first one. A group wider along X than along Z stands across Z; a one-cell-wide doorway asks its
        /// jambs, like the door probe: hull beside it along X (and not along Z) means the wall runs along X.</summary>
        private static Plan FromServerCells(HashSet<(int X, int Y, int Z)> hatch, HashSet<(int X, int Y, int Z)> solid)
        {
            var panels = new List<Panel>();
            var seen = new HashSet<(int X, int Y, int Z)>();
            var stack = new Stack<(int X, int Y, int Z)>();
            foreach (var start in hatch)
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                int minX = start.X, maxX = start.X, minY = start.Y, maxY = start.Y, minZ = start.Z, maxZ = start.Z;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                    minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y);
                    minZ = Math.Min(minZ, c.Z); maxZ = Math.Max(maxZ, c.Z);
                    foreach (var n in Neighbours6(c))
                    {
                        if (hatch.Contains(n) && seen.Add(n))
                        {
                            stack.Push(n);
                        }
                    }
                }

                bool facesZ;
                if (maxX - minX != maxZ - minZ)
                {
                    facesZ = maxX - minX > maxZ - minZ;
                }
                else
                {
                    bool xJamb = solid.Contains((minX - 1, minY, minZ)) || solid.Contains((maxX + 1, minY, minZ));
                    bool zJamb = solid.Contains((minX, minY, minZ - 1)) || solid.Contains((minX, minY, maxZ + 1));
                    facesZ = xJamb || !zJamb;
                }

                panels.Add(new Panel(minX, maxX, minY, maxY, minZ, maxZ, facesZ));
            }

            panels.Sort((a, b) => a.MinZ != b.MinZ ? a.MinZ.CompareTo(b.MinZ)
                : a.MinY != b.MinY ? a.MinY.CompareTo(b.MinY)
                : a.MinX.CompareTo(b.MinX));
            var first = panels[0];
            return new Plan(panels, fromServer: true, hasMarker: true,
                (first.MinX + first.MaxX + 1) * 0.5f, first.MinY - 0.5f, first.MinZ);
        }

        /// <summary>The older-server path: the robust guess (see the type summary).</summary>
        private static Plan Guess(IEnumerable<(int X, int Y, int Z)> cells, int width, int height, int length,
            IEnumerable<(int X, int Y, int Z)>? ownerDoors)
        {
            var all = new HashSet<(int X, int Y, int Z)>(cells);
            if (all.Count == 0)
            {
                return Nothing;
            }

            // The design box — the server's DesignExtents: y runs up to the roof row AT height (an authored layout puts
            // its roof there). Without a box (an older payload, a malformed design) the cells' own bounds stand in.
            int bx0, bx1, by0, by1, bz0, bz1;
            if (width > 0 && height > 0 && length > 0)
            {
                bx0 = 0; bx1 = width - 1;
                by0 = 0; by1 = height;
                bz0 = 0; bz1 = length - 1;
            }
            else
            {
                bx0 = by0 = bz0 = int.MaxValue;
                bx1 = by1 = bz1 = int.MinValue;
                foreach (var c in all)
                {
                    bx0 = Math.Min(bx0, c.X); bx1 = Math.Max(bx1, c.X);
                    by0 = Math.Min(by0, c.Y); by1 = Math.Max(by1, c.Y);
                    bz0 = Math.Min(bz0, c.Z); bz1 = Math.Max(bz1, c.Z);
                }
            }

            bool InBox(int x, int y, int z) => x >= bx0 && x <= bx1 && y >= by0 && y <= by1 && z >= bz0 && z <= bz1;

            // #2607: an owner-built door fills its three-high doorway — its own field closes it, so it is wall here.
            var filled = new HashSet<(int X, int Y, int Z)>();
            if (ownerDoors != null)
            {
                foreach (var d in ownerDoors)
                {
                    for (int dy = 0; dy <= 2; dy++)
                    {
                        filled.Add((d.X, d.Y + dy, d.Z));
                    }
                }
            }

            bool Solid(int x, int y, int z) => InBox(x, y, z) && (all.Contains((x, y, z)) || filled.Contains((x, y, z)));

            // The stern: the lowest z whose plane is a mostly filled face (an end cap), not engine or wing cells poking
            // out behind it.
            int zWall = int.MinValue, wx0 = 0, wx1 = 0, wy0 = 0, wy1 = 0;
            for (int z = bz0; z <= bz1 && zWall == int.MinValue; z++)
            {
                int count = 0, sx0 = int.MaxValue, sx1 = int.MinValue, sy0 = int.MaxValue, sy1 = int.MinValue;
                for (int x = bx0; x <= bx1; x++)
                {
                    for (int y = by0; y <= by1; y++)
                    {
                        if (Solid(x, y, z))
                        {
                            count++;
                            sx0 = Math.Min(sx0, x); sx1 = Math.Max(sx1, x);
                            sy0 = Math.Min(sy0, y); sy1 = Math.Max(sy1, y);
                        }
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                int area = (sx1 - sx0 + 1) * (sy1 - sy0 + 1);
                if (area >= MinWallArea && count >= area * WallFill)
                {
                    zWall = z;
                    wx0 = sx0; wx1 = sx1; wy0 = sy0; wy1 = sy1;
                }
            }

            if (zWall == int.MinValue)
            {
                return Nothing;
            }

            // Air the plane's edge reaches is outside; what it cannot reach is an opening in the wall.
            var outside = new HashSet<(int X, int Y)>();
            var queue = new Queue<(int X, int Y)>();
            for (int x = wx0; x <= wx1; x++)
            {
                for (int y = wy0; y <= wy1; y++)
                {
                    bool edge = x == wx0 || x == wx1 || y == wy0 || y == wy1;
                    if (edge && !Solid(x, y, zWall) && outside.Add((x, y)))
                    {
                        queue.Enqueue((x, y));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in Neighbours4(c))
                {
                    if (n.X >= wx0 && n.X <= wx1 && n.Y >= wy0 && n.Y <= wy1 && !Solid(n.X, n.Y, zWall) && outside.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            // The enclosed openings, one group each; the largest is the hatch (ties: the lowest, then the leftmost).
            var seen = new HashSet<(int X, int Y)>();
            Panel? best = null;
            int bestCount = 0;
            var stack = new Stack<(int X, int Y)>();
            for (int y = wy0; y <= wy1; y++)
            {
                for (int x = wx0; x <= wx1; x++)
                {
                    if (Solid(x, y, zWall) || outside.Contains((x, y)) || !seen.Add((x, y)))
                    {
                        continue;
                    }

                    int count = 0, ox0 = x, ox1 = x, oy0 = y, oy1 = y;
                    stack.Push((x, y));
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        count++;
                        ox0 = Math.Min(ox0, c.X); ox1 = Math.Max(ox1, c.X);
                        oy0 = Math.Min(oy0, c.Y); oy1 = Math.Max(oy1, c.Y);
                        foreach (var n in Neighbours4(c))
                        {
                            if (n.X >= wx0 && n.X <= wx1 && n.Y >= wy0 && n.Y <= wy1
                                && !Solid(n.X, n.Y, zWall) && !outside.Contains(n) && seen.Add(n))
                            {
                                stack.Push(n);
                            }
                        }
                    }

                    if (count > bestCount)
                    {
                        bestCount = count; // scanned bottom row first, left to right: an equal group later never wins
                        best = new Panel(ox0, ox1, oy0, oy1, zWall, zWall, facesZ: true);
                    }
                }
            }

            if (best is not { } panel)
            {
                // A sealed stern: nothing to cap; the marker still marks the wall (bottom row, centred).
                return new Plan(Array.Empty<Panel>(), false, true, (wx0 + wx1 + 1) * 0.5f, wy0 + 0.5f, zWall);
            }

            return new Plan(new[] { panel }, false, true, (panel.MinX + panel.MaxX + 1) * 0.5f, panel.MinY - 0.5f, zWall);
        }

        /// <summary>
        /// #2817: whether a doorway of a parked hull leads OUT — the hatch, or a side door the owner built into the outer
        /// wall — rather than between two of its rooms. A line across the doorway (along Z through a wall that runs along X,
        /// along X otherwise) at the doorway's height leaves the hull's box on one side without meeting a hull cell. The
        /// doorway and the hull are in structure cells (x 0…<paramref name="width"/>−1, z 0…<paramref name="length"/>−1);
        /// a doorway already outside the box counts as leading out. <paramref name="hullAt"/> answers "is this cell part of
        /// the hull" (anything not air).
        /// </summary>
        public static bool OpensToOutside(Func<int, int, int, bool> hullAt, int x, int y, int z, bool wallAlongX, int width, int length)
        {
            if (hullAt is null)
            {
                throw new ArgumentNullException(nameof(hullAt));
            }

            int dx = wallAlongX ? 0 : 1, dz = wallAlongX ? 1 : 0;
            return LeavesHull(hullAt, x, y, z, dx, dz, width, length) || LeavesHull(hullAt, x, y, z, -dx, -dz, width, length);
        }

        /// <summary>Walks from the doorway in one direction: true once the walk leaves the box, false at the first hull cell.</summary>
        private static bool LeavesHull(Func<int, int, int, bool> hullAt, int x, int y, int z, int dx, int dz, int width, int length)
        {
            for (int step = 0; step <= width + length + 1; step++)
            {
                int cx = x + dx * step, cz = z + dz * step;
                if (cx < 0 || cz < 0 || cx >= width || cz >= length)
                {
                    return true;
                }

                if (step > 0 && hullAt(cx, y, cz))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<(int X, int Y)> Neighbours4((int X, int Y) c)
        {
            yield return (c.X + 1, c.Y);
            yield return (c.X - 1, c.Y);
            yield return (c.X, c.Y + 1);
            yield return (c.X, c.Y - 1);
        }

        private static IEnumerable<(int X, int Y, int Z)> Neighbours6((int X, int Y, int Z) c)
        {
            yield return (c.X + 1, c.Y, c.Z);
            yield return (c.X - 1, c.Y, c.Z);
            yield return (c.X, c.Y + 1, c.Z);
            yield return (c.X, c.Y - 1, c.Z);
            yield return (c.X, c.Y, c.Z + 1);
            yield return (c.X, c.Y, c.Z - 1);
        }
    }
}
