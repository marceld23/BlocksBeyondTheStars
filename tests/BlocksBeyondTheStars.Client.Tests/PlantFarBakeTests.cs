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

/// <summary>Plant far pictures: the CPU rasteriser that bakes a plant body's outline into its far picture.
/// Nobody looks at the pictures before the Unity build, so these pin the numbers: coverage of known shapes, the covered
/// bounds against the mesh's projected bounds, the clear border and holes, both views, the encoding, determinism.</summary>
public sealed class PlantFarBakeTests
{
    private static readonly PlantFarBake.Frame Card = new(0.42f, 1f);
    private static readonly PlantFarBake.Colours Grey = new(new Vector3(0.3f), new Vector3(0.375f), new Vector3(0.6f, 0.1f, 0.2f), new Vector3(0.18f));

    /// <summary>A flat quad in the plane z = <paramref name="z"/> (front view) — seen edge-on from the side.</summary>
    private static (List<Vector3> P, List<Vector4> W, List<int> T) Quad(float x0, float x1, float y0, float y1, float z = 0f, float tone = 1f)
    {
        var p = new List<Vector3> { new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z) };
        var w = Enumerable.Repeat(new Vector4(tone, 0f, 0f, 0f), 4).ToList();
        return (p, w, new List<int> { 0, 1, 2, 0, 2, 3 });
    }

    [Fact]
    public void AFlatLeaf_CoversItsShareOfThePicture_AndItsBoundsAreTheMeshs()
    {
        var (p, w, t) = Quad(-0.2f, 0.2f, 0f, 0.5f);
        var r = PlantFarBake.Bake(p, w, t, Card, Grey, linearSpace: true);

        // 0.4 of the 0.84 m width and 0.5 of the 1 m height, less the clear bottom row: about 23 %.
        Assert.InRange(r.Coverage, 0.225f, 0.245f);
        var b = PlantFarBake.ProjectedBounds(p, t, Card);
        Assert.InRange(r.MinX, b.MinX - 1f, b.MinX + 1f);
        Assert.InRange(r.MaxX, b.MaxX - 2f, b.MaxX);
        Assert.Equal(1, r.MinY); // the root row is the first inside the clear border
        Assert.InRange(r.MaxY, b.MaxY - 2f, b.MaxY);
    }

    [Fact]
    public void TheBorderAndTheHoles_StayClearAndBlack()
    {
        var (p, w, t) = Quad(-2f, 2f, -1f, 3f); // larger than the frame: everything inside is covered
        var r = PlantFarBake.Bake(p, w, t, Card, Grey, linearSpace: true);
        Assert.Equal(62 * 62, r.Covered);
        for (int i = 0; i < PlantFarBake.Size; i++)
        {
            foreach (int texel in new[] { i, ((PlantFarBake.Size - 1) * PlantFarBake.Size) + i, i * PlantFarBake.Size, (i * PlantFarBake.Size) + PlantFarBake.Size - 1 })
            {
                Assert.Equal(0, r.Rgba[(texel * 4) + 3]);
                Assert.Equal(0, r.Rgba[texel * 4]);
            }
        }

        var empty = PlantFarBake.Bake(new List<Vector3>(), new List<Vector4>(), new List<int>(), Card, Grey, true);
        Assert.Equal(0, empty.Covered);
        Assert.Equal(-1, empty.MinX);
        Assert.All(empty.Rgba, v => Assert.Equal(0, v));
    }

    [Fact]
    public void TheSideView_FillsWhatTheFrontViewSeesEdgeOn()
    {
        // A leaf in the plane x = 0 is a line from the front and a full quad from the side.
        var p = new List<Vector3> { new(0f, 0f, -0.2f), new(0f, 0f, 0.2f), new(0f, 0.5f, 0.2f), new(0f, 0.5f, -0.2f) };
        var w = Enumerable.Repeat(new Vector4(1f, 0f, 0f, 0f), 4).ToList();
        var r = PlantFarBake.Bake(p, w, new List<int> { 0, 1, 2, 0, 2, 3 }, Card, Grey, true);
        Assert.InRange(r.Coverage, 0.225f, 0.245f);

        // Two crossed leaves cover their union, not their sum.
        var (fp, fw, ft) = Quad(-0.2f, 0.2f, 0f, 0.5f);
        var both = new List<Vector3>(fp);
        both.AddRange(p);
        var bothW = new List<Vector4>(fw);
        bothW.AddRange(w);
        var bothT = new List<int>(ft);
        bothT.AddRange(new[] { 4, 5, 6, 4, 6, 7 });
        Assert.Equal(r.Covered, PlantFarBake.Bake(both, bothW, bothT, Card, Grey, true).Covered);
    }

    [Fact]
    public void TheNearerTriangle_WinsTheTexel()
    {
        var (p, w, t) = Quad(-0.3f, 0.3f, 0f, 0.6f, z: 0.1f, tone: 0f);   // behind: dark ramp
        var (q, v, u) = Quad(-0.1f, 0.1f, 0.1f, 0.3f, z: -0.1f, tone: 1f); // in front: light ramp
        p.AddRange(q);
        w.AddRange(v);
        t.AddRange(u.Select(i => i + 4));
        var r = PlantFarBake.Bake(p, w, t, Card, Grey, linearSpace: false);
        int Texel(int x, int y) => r.Rgba[(((y * PlantFarBake.Size) + x) * 4) + 1];
        Assert.True(Texel(32, 13) > Texel(32, 30), "the light front leaf covers the dark one behind it");
        Assert.True(Texel(12, 30) > 0, "the dark leaf shows around it");
    }

    [Fact]
    public void TheColour_IsTheBodysRecipe_InSrgbForALinearProject()
    {
        var (p, w, t) = Quad(-2f, 2f, -1f, 3f, tone: 1f);
        var lin = PlantFarBake.Bake(p, w, t, Card, Grey, linearSpace: true);
        var gamma = PlantFarBake.Bake(p, w, t, Card, Grey, linearSpace: false);
        int o = ((32 * PlantFarBake.Size) + 32) * 4;
        // main 0.3 × the light end of the ramp (green 1.06) × the shade of a quad facing the eye (0.72 + 0.38 × 0.5).
        float expected = 0.3f * 1.06f * (0.72f + (0.38f * Vector3.Normalize(new Vector3(-0.35f, 0.8f, -0.5f)).Z * -1f));
        Assert.InRange(gamma.Rgba[o + 1], (int)(expected * 255f) - 2, (int)(expected * 255f) + 2);
        Assert.True(lin.Rgba[o + 1] > gamma.Rgba[o + 1] + 40, "the sRGB bytes of a dark linear colour are brighter");
        Assert.Equal(255, lin.Rgba[o + 3]);

        // Accent weight 1 paints the accent slot (bright red stays red).
        var red = PlantFarBake.Bake(p, Enumerable.Repeat(new Vector4(1f, 0f, 1f, 0f), 4).ToList(), t, Card, Grey, false);
        Assert.True(red.Rgba[o] > 3 * red.Rgba[o + 1]);
    }

    [Fact]
    public void TheBake_IsDeterministic()
    {
        var rng = new Random(3);
        var p = new List<Vector3>();
        var w = new List<Vector4>();
        var t = new List<int>();
        for (int i = 0; i < 300; i++)
        {
            p.Add(new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble(), (float)rng.NextDouble() - 0.5f));
            w.Add(new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), 0f, 0f));
        }

        for (int i = 0; i < 100; i++)
        {
            t.AddRange(new[] { rng.Next(300), rng.Next(300), rng.Next(300) });
        }

        var a = PlantFarBake.Bake(p, w, t, Card, Grey, true);
        var b = PlantFarBake.Bake(p, w, t, Card, Grey, true);
        Assert.Equal(a.Rgba, b.Rgba);
        Assert.InRange(a.Coverage, 0.05f, 0.95f);
    }
}
