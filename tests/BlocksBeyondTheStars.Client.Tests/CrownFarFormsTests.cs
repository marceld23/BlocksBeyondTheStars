// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Crown far forms: the far stage of needle and palm crown cells. Nobody sees them before the Unity
/// build, so these pin the numbers: the far form follows the near form's envelope (the near boxes restated from
/// <c>BlockShapeGeometry.NeedleBough</c> and friends, guarded against drift by a source check), the solids are closed and
/// wound outward, the cards come in pairs, everything stays in the stated overhang, the texture coordinates stay in the tile,
/// and the face counts the triangle budget is reckoned from.</summary>
public sealed class CrownFarFormsTests
{
    private readonly record struct Box(float X0, float Y0, float Z0, float X1, float Y1, float Z1);

    /// <summary>The near bough's boxes (BlockShapeGeometry.NeedleBough), restated.</summary>
    private static List<Box> NearBough(float reach)
    {
        var boxes = new List<Box>();
        float top = 0.80f;
        for (int i = 0; i < 3; i++)
        {
            float x0 = (reach * i / 3) - 0.03f, x1 = (reach * (i + 1) / 3) + 0.05f;
            float thick = 0.26f - (0.03f * i), half = 0.40f - (0.07f * i);
            boxes.Add(new Box(x0, top - thick, 0.5f - half, x1, top, 0.5f + half));
            top -= 0.20f;
        }

        boxes.Add(new Box(reach - 0.03f, top - 0.12f, 0.36f, reach + 0.16f, top + 0.10f, 0.64f));
        boxes.Add(new Box(-0.03f, 0.76f, 0.28f, 0.12f + (0.14f * reach), 1.0f, 0.72f));
        return boxes;
    }

    private static float NearTop(List<Box> boxes, float x) => boxes.Where(b => x >= b.X0 && x <= b.X1).Select(b => b.Y1).DefaultIfEmpty(float.NaN).Max();

    /// <summary>The far solid's top at x: the highest point of its upward faces above x (on the middle line z = 0.5).</summary>
    private static float FarTop(IReadOnlyList<CrownFarForms.Quad> quads, float x)
    {
        float best = float.NaN;
        foreach (var q in quads.Where(q => q.Normal.Y > 0f))
        {
            var p = new[] { q.A, q.B, q.C, q.D };
            float minX = p.Min(v => v.X), maxX = p.Max(v => v.X);
            if (x < minX || x > maxX || maxX - minX < 1e-5f)
            {
                continue;
            }

            // The wedge's top is planar and symmetric about z = 0.5: interpolate its two cross edges.
            float t = (x - minX) / (maxX - minX);
            float yInner = p.Where(v => Math.Abs(v.X - minX) < 1e-5f).Max(v => v.Y), yOuter = p.Where(v => Math.Abs(v.X - maxX) < 1e-5f).Max(v => v.Y);
            float y = yInner + ((yOuter - yInner) * t);
            best = float.IsNaN(best) ? y : Math.Max(best, y);
        }

        return best;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TheBough_FollowsTheNearBoughsOutline(int variant)
    {
        float reach = CrownFarForms.BoughReach(variant);
        var near = NearBough(reach);
        var far = CrownFarForms.Of(CrownFarForms.NeedleBough, variant);
        var points = far.SelectMany(q => new[] { q.A, q.B, q.C, q.D }).ToList();

        // The same extent: from the crest at the trunk to the hanging tip, as wide as the root step.
        Assert.Equal(near.Min(b => b.X0), points.Min(p => p.X), 3);
        Assert.Equal(near.Max(b => b.X1), points.Max(p => p.X), 3);
        Assert.Equal(1.0f, points.Max(p => p.Y), 3);
        Assert.Equal(near.Min(b => b.Z0), points.Min(p => p.Z), 2);

        // The top: the near one is a staircase of 0.2-high steps under a crest, the far one a slope through it.
        var deviations = new List<float>();
        for (float x = 0f; x <= reach + 0.15f; x += 0.01f)
        {
            float n = NearTop(near, x), f = FarTop(far, x);
            Assert.False(float.IsNaN(n) || float.IsNaN(f), $"no top at x = {x}");
            deviations.Add(Math.Abs(n - f));
        }

        Assert.True(deviations.Max() <= 0.25f, $"the far top leaves the near one by {deviations.Max():F3}");
        Assert.True(deviations.Average() <= 0.10f, $"on average by {deviations.Average():F3}");
    }

    [Fact]
    public void TheSpire_NarrowsLikeTheNearSpire()
    {
        // The near spire (BlockShapeGeometry.NeedleTip): five boxes, at mid-height of each its half-width.
        var steps = new[] { (0.15f, 0.36f), (0.41f, 0.26f), (0.67f, 0.17f), (0.94f, 0.09f), (1.19f, 0.04f) };
        var far = CrownFarForms.Of(CrownFarForms.NeedleTip, 0);
        var points = far.SelectMany(q => new[] { q.A, q.B, q.C, q.D }).ToList();
        Assert.Equal(1.32f, points.Max(p => p.Y), 3);
        Assert.Equal(0f, points.Min(p => p.Y), 3);
        foreach (var (y, half) in steps)
        {
            float farHalf = 0.36f + ((0.04f - 0.36f) * (y / 1.32f));
            Assert.True(Math.Abs(farHalf - half) <= 0.05f, $"at y {y}: far {farHalf:F3}, near {half:F3}");
        }
    }

    [Fact]
    public void TheFronds_LieWhereTheNearFrondsLie()
    {
        var level = CrownFarForms.Of(CrownFarForms.Frond, 0).SelectMany(q => new[] { q.A, q.B, q.C, q.D }).ToList();
        Assert.Equal(-0.04f, level.Min(p => p.X), 3);
        Assert.Equal(1.07f, level.Max(p => p.X), 3); // 1.04 + the last piece's 0.03
        Assert.Equal(0.455f - 0.36f, level.Min(p => p.Z), 3);
        Assert.All(level, p => Assert.Equal(0.58f, p.Y, 3)); // between the rib's 0.58 and 0.60

        // The drooping end: per near piece (centre x, rib y, leaflets + half the rib) the far card within 0.05.
        var tip = CrownFarForms.Of(CrownFarForms.Frond, 1);
        var pieces = new[] { (0.17f, 0.60f, 0.385f), (0.51f, 0.54f, 0.325f), (0.85f, 0.40f, 0.265f), (1.19f, 0.22f, 0.205f) };
        foreach (var (x, y, half) in pieces)
        {
            var seg = tip.First(q => Math.Min(q.A.X, q.B.X) <= x && Math.Max(q.A.X, q.B.X) >= x);
            float t = (x - seg.A.X) / (seg.B.X - seg.A.X);
            float farY = seg.A.Y + ((seg.B.Y - seg.A.Y) * t);
            float farHalf = Math.Abs(seg.A.Z - 0.5f) + ((Math.Abs(seg.B.Z - 0.5f) - Math.Abs(seg.A.Z - 0.5f)) * t);
            Assert.True(Math.Abs(farY - y) <= 0.05f, $"rib at x {x}: far {farY:F3}, near {y:F3}");
            Assert.True(Math.Abs(farHalf - half) <= 0.05f, $"width at x {x}: far {farHalf:F3}, near {half:F3}");
        }

        Assert.Equal(1.39f, tip.Max(q => Math.Max(q.B.X, q.C.X)), 3); // 1.36 + 0.03

        // The heart's spokes reach as far as the near rosette's (its outer rib piece ends at x 1.28: 0.78 from the middle).
        var heart = CrownFarForms.Of(CrownFarForms.FrondRosette, 0).SelectMany(q => new[] { q.A, q.B, q.C, q.D });
        Assert.Equal(0.78f, heart.Max(p => new Vector2(p.X - 0.5f, p.Z - 0.5f).Length()), 2);
    }

    [Theory]
    [InlineData(CrownFarForms.NeedleBough, 0, 6)]
    [InlineData(CrownFarForms.NeedleBough, 2, 6)]
    [InlineData(CrownFarForms.NeedleTip, 0, 6)]
    [InlineData(CrownFarForms.Frond, 0, 2)]
    [InlineData(CrownFarForms.Frond, 1, 8)]
    [InlineData(CrownFarForms.FrondRosette, 0, 18)]
    public void TheFarForms_AreClosedOrTwoSided_WoundOutward_InsideTheirOverhang(int form, int variant, int quads)
    {
        var far = CrownFarForms.Of(form, variant);
        Assert.Equal(quads, far.Count);
        Assert.All(far, q =>
        {
            Assert.True(q.Normal.Length() > 1e-6f, "a degenerate quad");
            foreach (var p in new[] { q.A, q.B, q.C, q.D })
            {
                // The stated overhang: a bough's tip and a frond's end reach up to 0.39 past the cell's outer edge, the
                // spire 0.32 above it; nothing else leaves the cell sideways or below — but the heart's spokes, which
                // reach 0.28 past every side like the near rosette's.
                bool heart = form == CrownFarForms.FrondRosette;
                Assert.InRange(p.X, heart ? -0.30f : -0.05f, heart ? 1.30f : 1.40f);
                Assert.InRange(p.Y, 0f, 1.33f);
                Assert.InRange(p.Z, heart ? -0.30f : 0f, heart ? 1.30f : 1f);
            }

            foreach (var uv in new[] { q.UvA, q.UvB, q.UvC, q.UvD })
            {
                Assert.InRange(uv.X, 0.019f, 0.981f);
                Assert.InRange(uv.Y, 0.019f, 0.981f);
            }

            // Planar: the fourth corner lies in the plane of the first three.
            var n = Vector3.Normalize(q.Normal);
            Assert.True(Math.Abs(Vector3.Dot(q.C - q.A, n)) < 1e-4f, "a bent quad");
        });

        // A closed solid's edges are each shared by exactly two of its quads; a card's quads come in opposite pairs.
        var edges = new Dictionary<(Vector3, Vector3), int>();
        var cards = new List<CrownFarForms.Quad>();
        foreach (var q in far)
        {
            var corners = new[] { q.A, q.B, q.C, q.D };
            bool card = far.Any(o => !SameQuad(o, q) && Vector3.Distance(o.Centre, q.Centre) < 1e-5f && Vector3.Dot(o.Normal, q.Normal) < 0f);
            if (card)
            {
                cards.Add(q);
                continue;
            }

            Assert.True(Vector3.Dot(q.Normal, q.Centre - SolidMiddle(far, q)) > 0f, "a quad faces into its solid");
            for (int k = 0; k < 4; k++)
            {
                var key = Key(corners[k], corners[(k + 1) % 4]);
                edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
            }
        }

        Assert.Equal(0, cards.Count % 2);
        Assert.All(edges, e => Assert.Equal(2, e.Value));
        Assert.Empty(CrownFarForms.Of(CrownFarForms.Leaf, 0));
    }

    [Fact]
    public void TheNearForms_TheseTestsRestate_AreStillTheGamesOwn()
    {
        string geometry = System.IO.File.ReadAllText(System.IO.Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "BlockShapeGeometry.cs"));
        Assert.Contains("float thick = 0.26f - (0.03f * i), half = 0.40f - (0.07f * i);", geometry);
        Assert.Contains("Box(f, reach - 0.03f, top - 0.12f, 0.36f, reach + 0.16f, top + 0.10f, 0.64f, (ShapePart)3);", geometry);
        Assert.Contains("Box(f, -0.03f, 0.76f, 0.28f, 0.12f + (0.14f * reach), 1.0f, 0.72f, (ShapePart)4);", geometry);
        Assert.Contains("case CrownFormNeedleBough: NeedleBough(f, variant <= 0 ? 0.5f : variant == 1 ? 0.85f : 1.2f); break;", geometry);
        Assert.Contains("Box(f, 0.46f, 1.06f, 0.46f, 0.54f, 1.32f, 0.54f, (ShapePart)4);", geometry);
        Assert.Contains("float length = tip ? 1.36f : 1.04f;", geometry);
        Assert.Contains("Box(spoke, 0.90f, 0.44f, 0.46f, 1.28f, 0.54f, 0.54f, (ShapePart)2);", geometry);
        Assert.Contains("public const int CrownFormFrondRosette = 4;", geometry);
    }

    // The middle of the closed solid a quad belongs to: the mean of the corners of every non-card quad that shares a corner
    // with it, transitively (a form holds at most one solid apart from its cards).
    private static Vector3 SolidMiddle(IReadOnlyList<CrownFarForms.Quad> quads, CrownFarForms.Quad of)
    {
        var solid = quads.Where(q => !quads.Any(o => !SameQuad(o, q) && Vector3.Distance(o.Centre, q.Centre) < 1e-5f && Vector3.Dot(o.Normal, q.Normal) < 0f)).ToList();
        return solid.SelectMany(q => new[] { q.A, q.B, q.C, q.D }).Aggregate(Vector3.Zero, (s, p) => s + p) / (solid.Count * 4);
    }

    // Corner by corner: the struct declares no equality of its own, and the default one compares by reflection.
    private static bool SameQuad(CrownFarForms.Quad a, CrownFarForms.Quad b)
        => a.A == b.A && a.B == b.B && a.C == b.C && a.D == b.D;

    private static (Vector3, Vector3) Key(Vector3 a, Vector3 b)
    {
        static Vector3 R(Vector3 v) => new((float)Math.Round(v.X, 4), (float)Math.Round(v.Y, 4), (float)Math.Round(v.Z, 4));
        var (ra, rb) = (R(a), R(b));
        return ra.X < rb.X || (ra.X == rb.X && (ra.Y < rb.Y || (ra.Y == rb.Y && ra.Z <= rb.Z))) ? (ra, rb) : (rb, ra);
    }
}
