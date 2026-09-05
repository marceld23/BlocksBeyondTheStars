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
}
