// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Rounds the turns of a listed rail line's polyline (#4014) before its pylons are set. A train rides a Catmull-Rom curve
/// through the pylon tops; at a sharp turn between two long spans that curve swings out by blocks (up to six where a line
/// left a city and turned at once), away from the corridor the stamp carved and into whatever stands beside it. So every
/// turn of more than <see cref="MinTurnDegrees"/> becomes a circular arc tangent to both straights — from a point <c>r</c>
/// before the corner to one <c>r</c> after it (radius <c>r / tan(θ/2)</c> for a turn of θ), one point per
/// <see cref="StepDegrees"/> of turn — with two taper points on each straight beside it, so the spacing grows gradually from
/// the arc's to the free route's (a uniform Catmull-Rom curve overshoots where a long span meets a short one). The curve
/// through such points stays on the polyline within a fraction of a block.
/// <para>The arc runs inside the corner, within the triangle of its two tangent points and the corner, its middle
/// <c>R · (1 / cos(θ/2) − 1)</c> from the corner (2.5 blocks for a right angle at <c>r</c> = 6); a router that went round an
/// obstacle's corner with a clearance of more than that keeps the rounded way clear. Pure and deterministic.</para>
/// </summary>
public static class RailTurnRounding
{
    /// <summary>A turn gentler than this stays a single point. Small: even a few degrees between a long span and a street
    /// bend the curve into the street (a 7° turn where a 32-block sea span met a city's street run swung it 0.3 blocks sideways).</summary>
    public const double MinTurnDegrees = 1.0;

    /// <summary>The turn per point on an arc.</summary>
    public const double StepDegrees = 15.0;

    /// <summary>The share of either neighbouring segment an arc may take (the rest keeps the next turn's room).</summary>
    private const double SegmentShare = 0.3;

    /// <summary>Two points closer than this are one.</summary>
    private const double SamePoint = 0.75;

    /// <summary>The taper points' distances from an arc's end, in arc reaches.</summary>
    private static readonly double[] Taper = { 0.7, 1.8 };

    /// <summary>The polyline with its interior turns rounded. <paramref name="radiusAt"/> gives each interior point's largest
    /// arc reach (blocks before and after the corner, by its index in <paramref name="points"/>); the ends are never moved.
    /// Points closer than a block to the one before are dropped first.</summary>
    public static List<(double X, double Z)> Round(IReadOnlyList<(double X, double Z)> points, Func<int, double> radiusAt)
    {
        var p = Distinct(points, out var origin);
        if (p.Count < 3)
        {
            return p;
        }

        // Each interior point: its arc (or itself) and the reach the arc took on its two straights.
        var arcs = new List<(double X, double Z)>[p.Count];
        var reach = new double[p.Count];
        for (int i = 1; i < p.Count - 1; i++)
        {
            (arcs[i], reach[i]) = Arc(p[i - 1], p[i], p[i + 1], radiusAt(origin[i]));
        }

        var result = new List<(double X, double Z)> { p[0] };
        for (int i = 1; i < p.Count; i++)
        {
            var from = result[result.Count - 1];
            var to = i < p.Count - 1 ? arcs[i][0] : p[i];
            AddTapers(result, from, to, reach[i - 1], i < p.Count - 1 ? reach[i] : 0);
            if (i < p.Count - 1)
            {
                result.AddRange(arcs[i]);
            }
            else
            {
                result.Add(p[i]);
            }
        }

        return result;
    }

    /// <summary>The turn at <paramref name="b"/> between the segments from <paramref name="a"/> and to <paramref name="c"/>
    /// (degrees, 0 = straight on, 180 = straight back).</summary>
    public static double TurnDegrees((double X, double Z) a, (double X, double Z) b, (double X, double Z) c)
    {
        double ux = b.X - a.X, uz = b.Z - a.Z, vx = c.X - b.X, vz = c.Z - b.Z;
        double lu = Math.Sqrt((ux * ux) + (uz * uz)), lv = Math.Sqrt((vx * vx) + (vz * vz));
        if (lu < 1e-9 || lv < 1e-9)
        {
            return 0;
        }

        double cos = Math.Clamp(((ux * vx) + (uz * vz)) / (lu * lv), -1.0, 1.0);
        return Math.Acos(cos) * 180.0 / Math.PI;
    }

    /// <summary>The arc that replaces the corner <paramref name="b"/> and the reach it took on its straights — the corner
    /// alone (reach 0) when the turn is gentle or there is no room.</summary>
    private static (List<(double X, double Z)> Arc, double Reach) Arc((double X, double Z) a, (double X, double Z) b, (double X, double Z) c, double maxRadius)
    {
        double turn = TurnDegrees(a, b, c);
        double lu = Distance(a, b), lv = Distance(b, c);
        double r = Math.Min(maxRadius, SegmentShare * Math.Min(lu, lv));
        if (turn < MinTurnDegrees || r < 1.0)
        {
            return (new List<(double X, double Z)> { b }, 0);
        }

        // The fillet: tangent to the incoming straight at `start`, its centre on the side the line turns to.
        double ux = (b.X - a.X) / lu, uz = (b.Z - a.Z) / lu, vx = (c.X - b.X) / lv, vz = (c.Z - b.Z) / lv;
        double sign = (ux * vz) - (uz * vx) >= 0 ? 1.0 : -1.0; // +1: the line turns toward +90° of its heading
        double theta = turn * Math.PI / 180.0;
        double radius = r / Math.Tan(theta / 2);
        var start = (X: b.X - (ux * r), Z: b.Z - (uz * r));
        var centre = (X: start.X - (sign * uz * radius), Z: start.Z + (sign * ux * radius));
        double phi = Math.Atan2(start.Z - centre.Z, start.X - centre.X);
        int steps = Math.Max(2, (int)Math.Ceiling(turn / StepDegrees));
        var arc = new List<(double X, double Z)>(steps + 1);
        for (int k = 0; k <= steps; k++)
        {
            double angle = phi + (sign * theta * k / steps);
            arc.Add((centre.X + (radius * Math.Cos(angle)), centre.Z + (radius * Math.Sin(angle))));
        }

        return (arc, r);
    }

    /// <summary>The taper points on the straight from <paramref name="from"/> (an arc's end, reach <paramref name="fromReach"/>)
    /// to <paramref name="to"/> (the next arc's start, reach <paramref name="toReach"/>), in order — as many as fit.</summary>
    private static void AddTapers(List<(double X, double Z)> result, (double X, double Z) from, (double X, double Z) to, double fromReach, double toReach)
    {
        double len = Distance(from, to);
        if (len < 1e-9)
        {
            return;
        }

        var at = new List<double>();
        foreach (double k in Taper)
        {
            if (fromReach > 0)
            {
                at.Add(fromReach * k);
            }

            if (toReach > 0)
            {
                at.Add(len - (toReach * k));
            }
        }

        at.Sort();
        double last = 0;
        foreach (double d in at)
        {
            if (d - last >= 1.0 && len - d >= 1.0)
            {
                result.Add((from.X + ((to.X - from.X) * d / len), from.Z + ((to.Z - from.Z) * d / len)));
                last = d;
            }
        }
    }

    /// <summary>The points without the ones that repeat the point before them; <paramref name="origin"/> keeps each kept
    /// point's index in the input.</summary>
    private static List<(double X, double Z)> Distinct(IReadOnlyList<(double X, double Z)> points, out List<int> origin)
    {
        var p = new List<(double X, double Z)>(points.Count);
        origin = new List<int>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (p.Count == 0 || Distance(p[p.Count - 1], points[i]) >= SamePoint)
            {
                p.Add(points[i]);
                origin.Add(i);
            }
        }

        return p;
    }

    private static double Distance((double X, double Z) a, (double X, double Z) b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
