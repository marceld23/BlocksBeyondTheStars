// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3941: the chamfer on player forms — width 0 is today's faces, the solid stays closed (every edge segment is
/// matched by one running the other way), every point stays in the cell, the volume of a single box is exactly the box
/// minus its cut edges and corners, and the polygon cap holds.
/// </summary>
public sealed class ShapeChamferTests
{
    private const float W = 0.06f;
    private readonly ITestOutputHelper _out;

    public ShapeChamferTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static string RandomBitmap(int seed, int grid, double fill, int slots)
    {
        var rng = new Random(seed);
        var chars = new char[grid * grid * grid];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = rng.NextDouble() < fill ? (char)('1' + rng.Next(slots)) : '0';
        }

        return new string(chars);
    }

    private static string Bitmap(int grid, Func<int, int, int, char> cell)
    {
        var chars = new char[grid * grid * grid];
        for (int y = 0; y < grid; y++)
        {
            for (int z = 0; z < grid; z++)
            {
                for (int x = 0; x < grid; x++)
                {
                    chars[CustomShape.IndexOf(x, y, z, grid)] = cell(x, y, z);
                }
            }
        }

        return new string(chars);
    }

    /// <summary>Hand-picked forms: an inner box, an L, stairs, a plus, a hollow frame, a form touching the border.</summary>
    public static IEnumerable<object[]> Forms()
    {
        yield return new object[] { Bitmap(8, (x, y, z) => x is >= 2 and < 6 && y is >= 2 and < 6 && z is >= 2 and < 6 ? '1' : '0') };
        yield return new object[] { Bitmap(8, (x, y, z) => y is >= 1 and < 3 && ((x is >= 1 and < 3 && z is >= 1 and < 7) || (z is >= 1 and < 3 && x is >= 1 and < 7)) ? '1' : '0') };
        yield return new object[] { Bitmap(8, (x, y, z) => y <= z ? '1' : '0') };
        yield return new object[] { Bitmap(8, (x, y, z) => (x is 3 or 4 && z is 3 or 4) || (y is 3 or 4 && z is 3 or 4) || (x is 3 or 4 && y is 3 or 4) ? '1' : '0') };
        yield return new object[] { Bitmap(8, (x, y, z) => (x is 1 or 6 || z is 1 or 6) && x is >= 1 and <= 6 && z is >= 1 and <= 6 && y is >= 1 and <= 5 ? '2' : '0') };
        yield return new object[] { Bitmap(4, (x, y, z) => y == 0 || (x == 1 && z == 1) ? '1' : '0') };
        yield return new object[] { Bitmap(8, (x, y, z) => ((x + y + z) & 1) == 0 && x is >= 1 and < 7 && y is >= 1 and < 7 && z is >= 1 and < 7 ? '1' : '0') };
        yield return new object[] { Bitmap(8, (x, y, z) => x is >= 1 and < 7 && z is >= 1 and < 7 && y < 4 ? (x < 4 ? '1' : '4') : '0') };
    }

    private static List<SlotBox> Boxes(string bitmap) => ShapeColourSlots.Merge(bitmap);

    [Fact]
    public void WidthZero_GivesExactlyTodaysMicroBoxFaces()
    {
        string bitmap = Bitmap(8, (x, y, z) => x < 4 && y < 2 ? '1' : '0');
        var boxes = Boxes(bitmap);
        var plain = ShapeChamfer.Plain(boxes);
        Assert.Equal(plain.Count, ShapeChamfer.Build(boxes, 0f).Count);
        Assert.Equal(6 * boxes.Count, plain.Count);

        // The one box 0..0.5 × 0..0.25 × 0..1: MicroBox's +Y face, then its +X face, corners and tile fractions as before.
        var top = plain[0];
        Assert.Equal(new Vector3(0f, 0.25f, 0f), top.A);
        Assert.Equal(new Vector3(0f, 0.25f, 1f), top.B);
        Assert.Equal(new Vector3(0.5f, 0.25f, 1f), top.C);
        Assert.Equal(new Vector3(0.5f, 0.25f, 0f), top.D);
        Assert.Equal(new Vector2(0.5f, 1f), top.UvC);
        var plusX = plain[2];
        Assert.Equal(new Vector3(0.5f, 0f, 0f), plusX.A);
        Assert.Equal(new Vector3(0.5f, 0.25f, 0f), plusX.B);
        Assert.Equal(new Vector2(1f, 0.25f), plusX.UvC);
        Assert.All(plain, f => Assert.True(Vector3.Dot(f.Normal, Centroid(f) - new Vector3(0.25f, 0.125f, 0.5f)) > 0f));
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void HandPickedForms_StayClosed_AndInsideTheCell(string bitmap)
    {
        var facets = ShapeChamfer.Build(Boxes(bitmap), W);
        AssertClosed(facets);
        AssertInsideCell(facets);
        Assert.True(Volume(facets) > 0f);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void RandomForms_StayClosed_AndInsideTheCell(int grid)
    {
        for (int seed = 0; seed < 25; seed++)
        {
            string bitmap = RandomBitmap(seed, grid, 0.5 + (0.02 * (seed % 10)), 3);
            var facets = ShapeChamfer.Build(Boxes(bitmap), ShapeChamfer.WidthFor(ShapeChamfer.DefaultShare, grid, W));
            AssertClosed(facets);
            AssertInsideCell(facets);
        }
    }

    [Fact]
    public void AnInnerBox_LosesExactlyItsEdgesAndCorners_AndHas26Polygons()
    {
        // The box 2..6 of the 8-grid: side 0.5, all twelve edges and eight corners cut.
        string bitmap = Bitmap(8, (x, y, z) => x is >= 2 and < 6 && y is >= 2 and < 6 && z is >= 2 and < 6 ? '1' : '0');
        const float s = 0.5f, w = 0.05f;
        var facets = ShapeChamfer.Build(Boxes(bitmap), w);
        Assert.Equal(6 + 12 + 8, facets.Count);
        float expected = (s * s * s) - (12f * (s - (2f * w)) * w * w / 2f) - (8f * 5f * w * w * w / 6f);
        Assert.Equal(expected, Volume(facets), 5);
    }

    [Fact]
    public void EdgesOnTheCellBorder_StayHard()
    {
        // A slab over the whole floor: its top edges lie on the cell's border, where the neighbour may continue the
        // surface — nothing is cut, the faces are the slab's six.
        string bitmap = Bitmap(8, (x, y, z) => y < 2 ? '1' : '0');
        var facets = ShapeChamfer.Build(Boxes(bitmap), W);
        Assert.Equal(6, facets.Count);
        Assert.All(facets, f => Assert.True(f.IsQuad));
    }

    [Fact]
    public void Facets_CarryTheSlotOfTheirCell()
    {
        string bitmap = Bitmap(8, (x, y, z) => x is >= 1 and < 7 && z is >= 1 and < 7 && y is >= 1 and < 3 ? (x < 4 ? '1' : '3') : '0');
        var facets = ShapeChamfer.Build(Boxes(bitmap), W);
        Assert.Contains(facets, f => f.Slot == ShapeColourSlots.Accent);
        Assert.Contains(facets, f => f.Slot == ShapeColourSlots.Main);
        Assert.All(facets.Where(f => f.Slot == ShapeColourSlots.Accent), f => Assert.True(Centroid(f).X >= 0.5f - 1e-4f));
        AssertClosed(facets);
    }

    [Fact]
    public void Budget_TheCapHolds_AndIsReported()
    {
        int worstPlain = 0, worstSoft = 0, capped = 0, forms = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            // Forms stamped out of a few random boxes — what players build — kept when the server would take them.
            var rng = new Random(seed + 500);
            var cells = new char[CustomShape.LargeChars];
            Array.Fill(cells, '0');
            int stamps = 2 + rng.Next(14);
            for (int k = 0; k < stamps; k++)
            {
                int sx = 1 + rng.Next(5), sy = 1 + rng.Next(5), sz = 1 + rng.Next(5);
                int x0 = rng.Next(9 - sx), y0 = rng.Next(9 - sy), z0 = rng.Next(9 - sz);
                for (int y = y0; y < y0 + sy; y++)
                {
                    for (int z = z0; z < z0 + sz; z++)
                    {
                        for (int x = x0; x < x0 + sx; x++)
                        {
                            cells[CustomShape.IndexOf(x, y, z, 8)] = '1';
                        }
                    }
                }
            }

            var boxes = Boxes(new string(cells));
            if (boxes.Count > CustomShape.MaxBoxes)
            {
                continue; // the server refuses such a form
            }

            var soft = ShapeChamfer.Build(boxes, W);
            var result = ShapeChamfer.BuildCapped(boxes, W);
            Assert.True(result.Count <= ShapeChamfer.MaxFacets || result.Count == 6 * boxes.Count);
            worstPlain = Math.Max(worstPlain, 6 * boxes.Count);
            worstSoft = Math.Max(worstSoft, soft.Count);
            capped += soft.Count > ShapeChamfer.MaxFacets ? 1 : 0;
            forms++;
        }

        _out.WriteLine($"{forms} stamped forms: worst plain {worstPlain} faces, worst chamfered {worstSoft} polygons, "
            + $"capped {capped}, cap {ShapeChamfer.MaxFacets}");
        Assert.True(forms > 100);
        Assert.Equal(576, ShapeChamfer.MaxFacets);
    }

    [Fact]
    public void WorstCase_48LooseCubes_IsCappedToHardEdges()
    {
        // 48 single micro cubes that touch nothing (corner to corner at most) and not the border: every one fully cut.
        var cells = new char[CustomShape.LargeChars];
        Array.Fill(cells, '0');
        int placed = 0;
        for (int y = 1; y < 7 && placed < 48; y += 2)
        {
            for (int z = 1; z < 7 && placed < 48; z += 2)
            {
                for (int x = 1; x < 7 && placed < 48; x += 2)
                {
                    cells[CustomShape.IndexOf(x, y, z, 8)] = '1';
                    placed++;
                }
            }
        }

        var boxes = Boxes(new string(cells));
        Assert.Equal(27, boxes.Count); // a 3 × 3 × 3 lattice fits: 27 loose cubes
        Assert.Equal(27 * 26, ShapeChamfer.Build(boxes, W).Count); // 702 polygons > the cap
        Assert.Equal(27 * 6, ShapeChamfer.BuildCapped(boxes, W).Count);
        _out.WriteLine($"27 loose cubes: plain {27 * 6} faces, chamfered {27 * 26} polygons → capped to plain");
    }

    [Fact]
    public void WidthFor_MatchesTheCubeBevel_OnBothGrids()
    {
        Assert.Equal(0.06f, ShapeChamfer.WidthFor(ShapeChamfer.DefaultShare, 8, 0.06f), 5);
        Assert.Equal(0.06f, ShapeChamfer.WidthFor(ShapeChamfer.DefaultShare, 4, 0.06f), 5); // capped at the cube's width
        Assert.Equal(0f, ShapeChamfer.WidthFor(0f, 8, 0.06f));
    }

    // ---------------------------------------------------------------- helpers

    private static Vector3 Centroid(ShapeFacet f)
        => f.IsQuad ? (f.A + f.B + f.C + f.D) / 4f : (f.A + f.B + f.C) / 3f;

    private static Vector3[] Corners(ShapeFacet f) => f.IsQuad ? new[] { f.A, f.B, f.C, f.D } : new[] { f.A, f.B, f.C };

    private static float Volume(List<ShapeFacet> facets)
    {
        double v = 0;
        foreach (var f in facets)
        {
            var p = Corners(f);
            for (int i = 1; i + 1 < p.Length; i++)
            {
                v += Vector3.Dot(p[0], Vector3.Cross(p[i], p[i + 1])) / 6.0;
            }
        }

        return (float)v;
    }

    internal static void AssertInsideCell(List<ShapeFacet> facets)
    {
        foreach (var f in facets)
        {
            foreach (var p in Corners(f))
            {
                Assert.InRange(p.X, -1e-5f, 1f + 1e-5f);
                Assert.InRange(p.Y, -1e-5f, 1f + 1e-5f);
                Assert.InRange(p.Z, -1e-5f, 1f + 1e-5f);
            }

            Assert.True(f.Normal.LengthSquared() > 1e-12f, "a polygon without area");
        }
    }

    private static (long, long, long) Key(Vector3 p)
        => ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5));

    /// <summary>Every edge is cut at every vertex lying on it (the T-junctions); each resulting segment a→b must be matched
    /// by as many b→a — the surface is closed and consistently wound.</summary>
    internal static void AssertClosed(List<ShapeFacet> facets)
    {
        var open = OpenSegments(facets);
        Assert.True(open.Count == 0, open.Count == 0 ? string.Empty : $"open edge {open[0].A} -> {open[0].B} ({open.Count} open)");
    }

    /// <summary>The edge segments (cut at T-junctions) not matched by as many segments running the other way.</summary>
    internal static List<(Vector3 A, Vector3 B)> OpenSegments(List<ShapeFacet> facets)
    {
        var points = new Dictionary<(long, long, long), Vector3>();
        foreach (var f in facets)
        {
            foreach (var p in Corners(f))
            {
                points[Key(p)] = p;
            }
        }

        var all = points.Values.ToArray();
        var count = new Dictionary<((long, long, long), (long, long, long)), int>();
        foreach (var f in facets)
        {
            var c = Corners(f);
            for (int i = 0; i < c.Length; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % c.Length];
                var cuts = new List<(float T, Vector3 P)> { (0f, a), (1f, b) };
                Vector3 d = b - a;
                float len2 = d.LengthSquared();
                foreach (var p in all)
                {
                    float t = Vector3.Dot(p - a, d) / len2;
                    if (t > 1e-4f && t < 1f - 1e-4f && (a + (d * t) - p).LengthSquared() < 1e-10f)
                    {
                        cuts.Add((t, p));
                    }
                }

                cuts.Sort((x, y) => x.T.CompareTo(y.T));
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    var key = (Key(cuts[k].P), Key(cuts[k + 1].P));
                    count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
        }

        var open = new List<(Vector3, Vector3)>();
        foreach (var pair in count)
        {
            count.TryGetValue((pair.Key.Item2, pair.Key.Item1), out int back);
            if (back != pair.Value)
            {
                open.Add((points[pair.Key.Item1], points[pair.Key.Item2]));
            }
        }

        return open;
    }
}
