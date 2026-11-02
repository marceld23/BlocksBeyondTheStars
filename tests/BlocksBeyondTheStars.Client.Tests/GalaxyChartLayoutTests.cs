// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The hyperspace chart's projection and display rules (#1603), checked against REAL generated
/// galaxies fed through the same conversion <c>GalaxyChartWidget</c> uses: every star of a grown
/// galaxy fits, a fixed galaxy is not zoomed out needlessly, unknown systems never leak their name,
/// lanes are undirected and a click picks the nearest star only inside the snap radius.
/// </summary>
public sealed class GalaxyChartLayoutTests
{
    // Mirrors GalaxyChartWidget.
    private const float ChartHalf = (900f * 0.5f) - 30f;
    private const float StarDisc = 22f;
    private const float MarkerPad = (StarDisc * 0.5f) + 10f;

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static (float[] X, float[] Y) Positions(Galaxy g)
        => (g.Systems.Select(s => s.MapX).ToArray(), g.Systems.Select(s => s.MapY).ToArray());

    [Theory]
    [InlineData(42, 8, 0)]
    [InlineData(42, 8, 8)]   // grown rim ~1500 units out from home
    [InlineData(7, 12, 20)]
    [InlineData(99, 2, 0)]   // a near-empty galaxy is held at the minimum extent, not blown up
    public void FitScale_KeepsEveryStarInsideTheChart(long seed, int systems, int grown)
    {
        var desc = new WorldDescription { StarSystemCount = systems, SystemVariance = true };
        var galaxy = new UniverseGenerator(seed, desc, Content).Generate(systems + grown);
        var (x, y) = Positions(galaxy);

        GalaxyChartLayout.Centre(x, y, out float cx, out float cy);
        float scale = GalaxyChartLayout.FitScale(ChartHalf, x, y, cx, cy, MarkerPad);
        Assert.True(scale > 0f);

        for (int i = 0; i < x.Length; i++)
        {
            GalaxyChartLayout.ToChart(x[i], y[i], scale, cx, cy, out float px, out float py);
            float reach = MathF.Sqrt(px * px + py * py) + StarDisc * 0.5f;
            Assert.True(reach <= ChartHalf + 0.01f, $"{galaxy.Systems[i].Id} reaches {reach:F1} of {ChartHalf}");
        }

        // The fit is tight: the farthest star sits at (or just inside, by the fit margin) the usable radius.
        float farthest = 0f;
        for (int i = 0; i < x.Length; i++)
        {
            GalaxyChartLayout.ToChart(x[i], y[i], scale, cx, cy, out float px, out float py);
            farthest = MathF.Max(farthest, MathF.Sqrt(px * px + py * py));
        }

        float extent = MathF.Max(GalaxyChartLayout.MinExtent, Reach(x, y, cx, cy));
        float expected = (ChartHalf - MarkerPad) / GalaxyChartLayout.FitMargin * (Reach(x, y, cx, cy) / extent);
        Assert.InRange(farthest, expected - 0.5f, expected + 0.5f);
    }

    private static float Reach(float[] x, float[] y, float cx, float cy)
    {
        float r = 0f;
        for (int i = 0; i < x.Length; i++)
        {
            r = MathF.Max(r, MathF.Sqrt((x[i] - cx) * (x[i] - cx) + (y[i] - cy) * (y[i] - cy)));
        }

        return r;
    }

    [Fact]
    public void Centre_IsTheBoundingBoxCentre_AndEmptyIsOrigin()
    {
        GalaxyChartLayout.Centre(new[] { 0f, 1000f, 200f }, new[] { 100f, 300f, 900f }, out float cx, out float cy);
        Assert.Equal(500f, cx);
        Assert.Equal(500f, cy);
        GalaxyChartLayout.Centre(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, out cx, out cy);
        Assert.Equal(0f, cx);
        Assert.Equal(0f, cy);
    }

    [Theory]
    [InlineData(false, false, false, "?")]
    [InlineData(true, false, false, "Kel")]
    [InlineData(false, true, false, "Kel")]
    [InlineData(false, false, true, "Kel")]
    public void DisplayName_ShowsTheRealNameOnlyWhenKnownCurrentOrScanned(bool known, bool current, bool radar, string expected)
        => Assert.Equal(expected, GalaxyChartLayout.DisplayName("Kel", known, current, radar, "?"));

    [Fact]
    public void HasLane_IsUndirected_AndTolerantOfMissingData()
    {
        var a = new[] { "sys0", "sys4" };
        var b = new[] { "sys2", "sys5" };
        Assert.True(GalaxyChartLayout.HasLane(a, b, "sys0", "sys2"));
        Assert.True(GalaxyChartLayout.HasLane(a, b, "sys2", "sys0"));
        Assert.True(GalaxyChartLayout.HasLane(a, b, "sys5", "sys4"));
        Assert.False(GalaxyChartLayout.HasLane(a, b, "sys0", "sys4"));
        Assert.False(GalaxyChartLayout.HasLane(a, b, "sys0", "sys0"));
        Assert.False(GalaxyChartLayout.HasLane(null, b, "sys0", "sys2"));
        Assert.False(GalaxyChartLayout.HasLane(a, new[] { "sys2" }, "sys4", "sys5")); // ragged arrays: walk the shorter
    }

    [Fact]
    public void Pick_ReturnsTheNearestStarInsideTheSnapRadius()
    {
        var x = new[] { 0f, 100f, 105f };
        var y = new[] { 0f, 0f, 0f };
        Assert.Equal(0, GalaxyChartLayout.Pick(x, y, 5f, 5f, 26f));
        Assert.Equal(1, GalaxyChartLayout.Pick(x, y, 101f, 0f, 26f));
        Assert.Equal(2, GalaxyChartLayout.Pick(x, y, 104f, 0f, 26f));
        Assert.Equal(-1, GalaxyChartLayout.Pick(x, y, 50f, 0f, 26f));
        Assert.Equal(-1, GalaxyChartLayout.Pick(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, 0f, 0f, 26f));
    }

    // ---- #3129: zoom and pan ----

    [Fact]
    public void ZoomedProjection_AtZoomOneOnTheCentre_IsTheFittedChart()
    {
        GalaxyChartLayout.ToChart(700f, 250f, 0.4f, 500f, 500f, out float fx, out float fy);
        GalaxyChartLayout.ToChart(700f, 250f, 0.4f, new GalaxyChartView(1f, 500f, 500f), out float vx, out float vy);
        Assert.Equal(fx, vx, 3);
        Assert.Equal(fy, vy, 3);

        GalaxyChartLayout.FromChart(vx, vy, 0.4f, new GalaxyChartView(1f, 500f, 500f), out float mx, out float my);
        Assert.Equal(700f, mx, 2);
        Assert.Equal(250f, my, 2);
    }

    [Fact]
    public void ZoomAbout_KeepsThePointUnderTheCursorInPlace()
    {
        const float scale = 0.4f;
        var view = new GalaxyChartView(1f, 500f, 500f);
        GalaxyChartLayout.FromChart(120f, -80f, scale, view, out float beforeX, out float beforeY);

        var zoomed = GalaxyChartLayout.ZoomAbout(view, 3f, 120f, -80f, scale);
        Assert.Equal(3f, zoomed.Zoom);
        GalaxyChartLayout.FromChart(120f, -80f, scale, zoomed, out float afterX, out float afterY);
        Assert.Equal(beforeX, afterX, 2);
        Assert.Equal(beforeY, afterY, 2);

        // And a star there is drawn 3× as far from the chart centre as the cursor's map point moved — i.e. it spread.
        GalaxyChartLayout.ToChart(500f, 500f, scale, zoomed, out float cx, out float cy);
        Assert.True(MathF.Abs(cx) > 0f || MathF.Abs(cy) > 0f);
    }

    [Fact]
    public void Pan_MovesTheGalaxyWithThePointer()
    {
        const float scale = 0.5f;
        var view = new GalaxyChartView(2f, 500f, 500f);
        GalaxyChartLayout.ToChart(600f, 500f, scale, view, out float before, out _);
        var panned = GalaxyChartLayout.Pan(view, 40f, 0f, scale);
        GalaxyChartLayout.ToChart(600f, 500f, scale, panned, out float after, out _);
        Assert.Equal(before + 40f, after, 2);
    }

    [Fact]
    public void Clamp_PinsZoomOneToTheCentre_AndKeepsTheFocusInsideTheGalaxy()
    {
        var fit = GalaxyChartLayout.Clamp(new GalaxyChartView(0.3f, 9000f, -9000f), 6f, 0f, 1000f, 0f, 800f);
        Assert.Equal(1f, fit.Zoom);
        Assert.Equal(500f, fit.FocusX);
        Assert.Equal(400f, fit.FocusY);

        var deep = GalaxyChartLayout.Clamp(new GalaxyChartView(40f, 9000f, -9000f), 6f, 0f, 1000f, 0f, 800f);
        Assert.Equal(6f, deep.Zoom);
        Assert.InRange(deep.FocusX, 0f, 1000f);
        Assert.InRange(deep.FocusY, 0f, 800f);
        Assert.True(deep.FocusX > 500f && deep.FocusY < 400f); // it roams toward the asked-for corner
    }

    [Theory]
    [InlineData(42, 32, 0)]
    [InlineData(7, 32, 0)]
    [InlineData(1234, 12, 20)] // a grown galaxy squeezes the original stars together
    public void MaxZoom_SeparatesEvenTheClosestPairOfARealGalaxy(long seed, int systems, int grown)
    {
        var desc = new WorldDescription { StarSystemCount = systems, SystemVariance = true };
        var galaxy = new UniverseGenerator(seed, desc, Content).Generate(systems + grown);
        var (x, y) = Positions(galaxy);
        GalaxyChartLayout.Centre(x, y, out float cx, out float cy);
        float scale = GalaxyChartLayout.FitScale(ChartHalf, x, y, cx, cy, MarkerPad);
        const float minSeparation = StarDisc * 1.5f;

        float maxZoom = GalaxyChartLayout.MaxZoom(x, y, scale, minSeparation);
        Assert.InRange(maxZoom, GalaxyChartLayout.MinMaxZoom, GalaxyChartLayout.MaxZoomCap);

        float closest = float.MaxValue;
        for (int i = 0; i < x.Length; i++)
        {
            for (int j = i + 1; j < x.Length; j++)
            {
                closest = MathF.Min(closest, MathF.Sqrt((x[i] - x[j]) * (x[i] - x[j]) + (y[i] - y[j]) * (y[i] - y[j])));
            }
        }

        if (maxZoom < GalaxyChartLayout.MaxZoomCap)
        {
            Assert.True(closest * scale * maxZoom >= minSeparation - 0.01f, $"closest pair {closest * scale * maxZoom:F1} chart units at the max zoom");
        }
    }

    [Fact]
    public void MaxZoom_OfTinyGalaxies_IsTheFloor()
    {
        Assert.Equal(GalaxyChartLayout.MinMaxZoom, GalaxyChartLayout.MaxZoom(new[] { 5f }, new[] { 5f }, 0.4f, 33f));
        Assert.Equal(GalaxyChartLayout.MaxZoomCap, GalaxyChartLayout.MaxZoom(new[] { 5f, 5f }, new[] { 5f, 5f }, 0.4f, 33f));
    }

    [Fact]
    public void LabelsShown_KeepsTheImportantLabel_AndDropsTheOneItWouldCover()
    {
        // Labels 0 and 1 overlap, 2 stands apart. Label 1 comes first in the order (the current system).
        var cx = new[] { 0f, 10f, 300f };
        var cy = new[] { 0f, 2f, 0f };
        var w = new[] { 80f, 80f, 80f };
        var h = new[] { 18f, 18f, 18f };
        var shown = GalaxyChartLayout.LabelsShown(cx, cy, w, h, new[] { 1, 0, 2 });
        Assert.Equal(new[] { false, true, true }, shown);

        // Spread apart (zoomed in), both come back.
        cx[1] = 120f;
        Assert.Equal(new[] { true, true, true }, GalaxyChartLayout.LabelsShown(cx, cy, w, h, new[] { 1, 0, 2 }));
    }

    [Fact]
    public void Bounds_OfSystems()
    {
        GalaxyChartLayout.Bounds(new[] { 3f, -2f, 8f }, new[] { 1f, 9f, -4f }, out float minX, out float maxX, out float minY, out float maxY);
        Assert.Equal((-2f, 8f, -4f, 9f), (minX, maxX, minY, maxY));
    }
}
