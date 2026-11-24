// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The shortest way of a listed rail line's leg around what stands in its way (#4014): a polyline from one point to another
/// that never enters an obstacle — a city's districts, a landmark, a village, a pad — each an axis-aligned box in a flat
/// frame (X unwrapped by the caller). The way bends only at the boxes' corners: the shortest path round convex obstacles
/// runs from corner to corner, so a visibility graph of the start, the goal and every corner, searched with A*, finds it.
/// Boxes are inflated by a clearance first (the wagon's half width and the room a rounded turn needs) and the corner nodes
/// sit a little outside the inflated box, so a way that touches a box only grazes its clearance.
/// <para>Pure and deterministic: the same boxes and points give the same way on the server of every save.</para>
/// </summary>
public static class RailLegRouter
{
    /// <summary>How far outside an inflated box its corner nodes stand.</summary>
    private const double CornerOffset = 0.25;

    /// <summary>An axis-aligned box in the flat frame: X from <see cref="X0"/> to <see cref="X1"/>, Z from <see cref="Z0"/> to
    /// <see cref="Z1"/> (continuous coordinates — a block column x covers [x, x + 1)).</summary>
    public readonly struct Box
    {
        public Box(double x0, double z0, double x1, double z1)
        {
            X0 = Math.Min(x0, x1);
            Z0 = Math.Min(z0, z1);
            X1 = Math.Max(x0, x1);
            Z1 = Math.Max(z0, z1);
        }

        public double X0 { get; }
        public double Z0 { get; }
        public double X1 { get; }
        public double Z1 { get; }

        /// <summary>The box grown by <paramref name="by"/> on every side.</summary>
        public Box Grow(double by) => new(X0 - by, Z0 - by, X1 + by, Z1 + by);

        /// <summary>Whether a point lies strictly inside the box.</summary>
        public bool ContainsStrictly(double x, double z) => x > X0 && x < X1 && z > Z0 && z < Z1;

        /// <summary>Whether the open segment from (ax, az) to (bx, bz) passes through the box's interior (a segment that only
        /// runs along an edge or touches a corner does not).</summary>
        public bool Cuts(double ax, double az, double bx, double bz)
        {
            double t0 = 0.0, t1 = 1.0, dx = bx - ax, dz = bz - az;
            return Clip(-dx, ax - X0, ref t0, ref t1) && Clip(dx, X1 - ax, ref t0, ref t1)
                   && Clip(-dz, az - Z0, ref t0, ref t1) && Clip(dz, Z1 - az, ref t0, ref t1)
                   && t1 - t0 > 1e-9
                   && ContainsStrictly(ax + (dx * (t0 + t1) * 0.5), az + (dz * (t0 + t1) * 0.5));
        }

        /// <summary>Liang–Barsky: narrows [t0, t1] to the part of the segment on the inner side of one edge.</summary>
        private static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (Math.Abs(p) < 1e-12)
            {
                return q > 0; // parallel: inside the slab or not
            }

            double r = q / p;
            if (p < 0)
            {
                if (r > t1)
                {
                    return false;
                }

                t0 = Math.Max(t0, r);
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                t1 = Math.Min(t1, r);
            }

            return true;
        }
    }

    /// <summary>Whether the straight way from <paramref name="a"/> to <paramref name="b"/> enters none of the (already
    /// inflated) boxes.</summary>
    public static bool Clear((double X, double Z) a, (double X, double Z) b, IReadOnlyList<Box> boxes)
    {
        foreach (var box in boxes)
        {
            if (box.Cuts(a.X, a.Z, b.X, b.Z))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The shortest way from <paramref name="a"/> to <paramref name="b"/> that enters none of the boxes grown by
    /// <paramref name="clearance"/>: the points it runs through, both ends included. Null when an end lies inside a grown
    /// box or the boxes wall the goal off.</summary>
    public static List<(double X, double Z)>? Route((double X, double Z) a, (double X, double Z) b, IReadOnlyList<Box> boxes, double clearance)
    {
        var grown = new List<Box>(boxes.Count);
        foreach (var box in boxes)
        {
            var g = box.Grow(clearance);
            if (g.ContainsStrictly(a.X, a.Z) || g.ContainsStrictly(b.X, b.Z))
            {
                return null;
            }

            grown.Add(g);
        }

        if (Clear(a, b, grown))
        {
            return new List<(double X, double Z)> { a, b };
        }

        var nodes = new List<(double X, double Z)> { a, b };
        foreach (var g in grown)
        {
            foreach (var corner in Corners(g.Grow(CornerOffset)))
            {
                if (!InsideAny(corner, grown))
                {
                    nodes.Add(corner);
                }
            }
        }

        return Search(nodes, grown);
    }

    /// <summary>The length of a way.</summary>
    public static double Length(IReadOnlyList<(double X, double Z)> way)
    {
        double length = 0;
        for (int i = 1; i < way.Count; i++)
        {
            length += Distance(way[i - 1], way[i]);
        }

        return length;
    }

    private static IEnumerable<(double X, double Z)> Corners(Box g)
    {
        yield return (g.X0, g.Z0);
        yield return (g.X1, g.Z0);
        yield return (g.X0, g.Z1);
        yield return (g.X1, g.Z1);
    }

    private static bool InsideAny((double X, double Z) p, List<Box> boxes)
    {
        foreach (var box in boxes)
        {
            if (box.ContainsStrictly(p.X, p.Z))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A* over the visibility graph of <paramref name="nodes"/> (node 0 the start, node 1 the goal): an edge is a
    /// straight way that enters no box, looked at only when its node is expanded.</summary>
    private static List<(double X, double Z)>? Search(List<(double X, double Z)> nodes, List<Box> boxes)
    {
        int n = nodes.Count;
        var g = new double[n];
        var from = new int[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++)
        {
            g[i] = double.MaxValue;
            from[i] = -1;
        }

        g[0] = 0;
        var open = new SortedSet<(double F, int Node)> { (Distance(nodes[0], nodes[1]), 0) };
        while (open.Count > 0)
        {
            var (_, cur) = open.Min;
            open.Remove(open.Min);
            if (cur == 1)
            {
                return Path(nodes, from);
            }

            if (closed[cur])
            {
                continue;
            }

            closed[cur] = true;
            for (int next = 1; next < n; next++)
            {
                if (closed[next] || next == cur)
                {
                    continue;
                }

                double tentative = g[cur] + Distance(nodes[cur], nodes[next]);
                if (tentative < g[next] - 1e-9 && Clear(nodes[cur], nodes[next], boxes))
                {
                    if (g[next] < double.MaxValue)
                    {
                        open.Remove((g[next] + Distance(nodes[next], nodes[1]), next));
                    }

                    g[next] = tentative;
                    from[next] = cur;
                    open.Add((tentative + Distance(nodes[next], nodes[1]), next));
                }
            }
        }

        return null;
    }

    private static List<(double X, double Z)> Path(List<(double X, double Z)> nodes, int[] from)
    {
        var path = new List<(double X, double Z)>();
        for (int i = 1; i >= 0; i = from[i])
        {
            path.Add(nodes[i]);
        }

        path.Reverse();
        return path;
    }

    private static double Distance((double X, double Z) a, (double X, double Z) b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
