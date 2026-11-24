// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The pure parts of a listed rail line's way (#4014): the router round axis-aligned obstacles, the rounding of the turns so
/// the train's Catmull-Rom curve stays on the way, the chain that picks every city's hall at once, and a city's districts as
/// the router's boxes.
/// </summary>
public sealed class RailLegRoutingTests
{
    private static RailLegRouter.Box Box(double x0, double z0, double x1, double z1) => new(x0, z0, x1, z1);

    [Fact]
    public void TheRouter_RunsStraightWhenClear_AndRoundABoxWhenNot_NeverInsideItsClearance()
    {
        var clear = RailLegRouter.Route((0, 0), (100, 0), new[] { Box(40, 10, 60, 30) }, 2.0)!;
        Assert.Equal(2, clear.Count);

        var block = Box(40, -10, 60, 10);
        var way = RailLegRouter.Route((0, 0), (100, 0), new[] { block }, 3.0)!;
        Assert.True(way.Count >= 4, "round the box over two of its corners");
        Assert.Equal((0.0, 0.0), way[0]);
        Assert.Equal((100.0, 0.0), way[^1]);
        Assert.True(RailLegRouter.Clear(way[0], way[1], new[] { block.Grow(3.0) }));
        for (int i = 1; i < way.Count; i++)
        {
            Assert.True(RailLegRouter.Clear(way[i - 1], way[i], new[] { block.Grow(3.0) }), $"segment {i} keeps the clearance");
        }

        Assert.InRange(RailLegRouter.Length(way), 100.0, 100.0 + (2 * 13.25) + 1.0); // up and over: 10 + 3 + the corner offset each way
    }

    [Fact]
    public void TheRouter_TakesAGapWideEnough_RefusesANarrowOne_AndAnEndInsideABox()
    {
        // Two boxes with a gap of 20 at Z 0..20 (between Z −100..0 and 20..120) across the way.
        var walls = new[] { Box(40, -100, 60, 0), Box(40, 20, 60, 120) };
        var through = RailLegRouter.Route((0, 10), (100, 10), walls, 4.0)!;
        Assert.Equal(2, through.Count); // straight through the middle of the gap (12 free after the clearance)

        var narrow = new[] { Box(40, -100, 60, 7), Box(40, 13, 60, 120) }; // a gap of 6: closed by a clearance of 4 each side
        var round = RailLegRouter.Route((0, 10), (100, 10), narrow, 4.0)!;
        Assert.True(RailLegRouter.Length(round) > 200, "the way goes round the whole wall");

        Assert.Null(RailLegRouter.Route((50, 50), (100, 10), walls, 4.0)); // the start stands in a box
        var ring = new[] { Box(-20, -20, 20, -10), Box(-20, 10, 20, 20), Box(-20, -20, -10, 20), Box(10, -20, 20, 20) };
        Assert.Null(RailLegRouter.Route((0, 0), (100, 0), ring, 1.0)); // walled in
    }

    [Theory]
    [InlineData(90.0)]
    [InlineData(120.0)]
    [InlineData(150.0)]
    public void TheRounding_TurnsInSmallSteps_StaysInsideTheCorner_AndTheCurveFollowsIt(double turn)
    {
        double a = turn * Math.PI / 180.0;
        var corner = (60.0, 0.0);
        var points = new List<(double X, double Z)> { (0, 0), corner, (60.0 + (60 * Math.Cos(a)), 60 * Math.Sin(a)) };
        var rounded = RailTurnRounding.Round(points, _ => 6.0);
        Assert.Equal(points[0], rounded[0]);
        Assert.Equal(points[^1], rounded[^1]);
        for (int i = 1; i + 1 < rounded.Count; i++)
        {
            Assert.True(RailTurnRounding.TurnDegrees(rounded[i - 1], rounded[i], rounded[i + 1]) <= (2 * RailTurnRounding.StepDegrees) + 1e-6,
                $"point {i} turns gently");
        }

        double radius = 6.0 / Math.Tan(a / 2);
        double inset = radius * (1 - Math.Cos(a / 2)); // the fillet's middle off its two straights
        Assert.All(rounded, p => Assert.True(DistanceToPolyline(p, points) <= inset + 1e-6));
        Assert.True(rounded.Min(p => Distance(p, corner)) >= radius * ((1 / Math.Cos(a / 2)) - 1) - 1e-6, "the arc stays off the corner");

        // The train's curve through the rounded points (pylons every few blocks on the straights) stays on the way.
        var pylons = Subdivide(rounded, 18.0).Select(p => new Vector3f((float)p.X, 0f, (float)p.Z)).ToList();
        var spline = RailSpline.Build(pylons, closed: false, circumference: 100000);
        for (float s = 0; s <= spline.TotalArc; s += 0.25f)
        {
            var q = spline.PointAtUnwrapped(s);
            Assert.True(DistanceToPolyline((q.X, q.Z), rounded) < 0.6, $"the curve at arc {s:F1} keeps to the rounded way");
        }
    }

    [Fact]
    public void TheChain_PicksTheCheapestWholeChain_NotTheCheapestHallPerCity()
    {
        // City 0's cheap hall (cost 0) leads only to an expensive leg; its dear one (cost 5) to a cheap one.
        double Hall(int k, int j) => k == 0 ? (j == 0 ? 0 : 5) : 0;
        double Leg(int k, int a, int b) => a == 0 ? 100 : 1;
        Assert.Equal(new[] { 1, 0 }, ListedRailHallChain.Choose(new[] { 2, 1 }, Hall, Leg));
        Assert.Null(ListedRailHallChain.Choose(new[] { 2, 2 }, (_, _) => 0, (_, _, _) => double.PositiveInfinity));
        Assert.Equal(new[] { 0 }, ListedRailHallChain.Choose(new[] { 1 }, (_, _) => 3, (_, _, _) => 0));
    }

    [Fact]
    public void ACitysDistricts_AreTheRoutersBoxes_EveryClaimedOneCovered_NoLandCovered()
    {
        var spec = new CityLayoutSpec(4, MetropolisPlanner.DistrictSize, MetropolisPlanner.Street, MetropolisPlanner.Height,
            new[] { "RR..", "RLLR", ".LLR", "RSRR" }, metro: true);
        var merged = MetroRailHalls.MergedDistrictRects(spec);
        var single = MetroRailHalls.DistrictRects(spec);
        Assert.Equal(13, single.Count);
        Assert.True(merged.Count < single.Count);
        Assert.All(single, d => Assert.Contains(merged, m => m.X0 <= d.X0 && m.Z0 <= d.Z0 && m.X1 >= d.X1 && m.Z1 >= d.Z1));
        for (int gz = 0; gz < 4; gz++)
        {
            for (int gx = 0; gx < 4; gx++)
            {
                if (MetroRailHalls.RoleAt(spec, gx, gz) == MetroRailHalls.LandRole)
                {
                    var (ox, oz) = spec.DistrictOrigin(gx, gz);
                    var land = new MetroRailHalls.Rect(ox, oz, ox + spec.DistrictSize - 1, oz + spec.DistrictSize - 1);
                    Assert.DoesNotContain(merged, m => m.Overlaps(land));
                }
            }
        }
    }

    private static List<(double X, double Z)> Subdivide(List<(double X, double Z)> points, double spacing)
    {
        var result = new List<(double X, double Z)> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            var (p, q) = (points[i - 1], points[i]);
            int n = Math.Max(1, (int)Math.Ceiling(Distance(p, q) / spacing));
            for (int k = 1; k <= n; k++)
            {
                result.Add((p.X + ((q.X - p.X) * k / n), p.Z + ((q.Z - p.Z) * k / n)));
            }
        }

        return result;
    }

    private static double DistanceToPolyline((double X, double Z) p, IReadOnlyList<(double X, double Z)> line)
    {
        double best = double.MaxValue;
        for (int i = 1; i < line.Count; i++)
        {
            var (a, b) = (line[i - 1], line[i]);
            double dx = b.X - a.X, dz = b.Z - a.Z, len2 = (dx * dx) + (dz * dz);
            double t = len2 < 1e-12 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Z - a.Z) * dz)) / len2, 0, 1);
            best = Math.Min(best, Distance(p, (a.X + (dx * t), a.Z + (dz * t))));
        }

        return best;
    }

    private static double Distance((double X, double Z) a, (double X, double Z) b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Z - a.Z) * (b.Z - a.Z)));
}
