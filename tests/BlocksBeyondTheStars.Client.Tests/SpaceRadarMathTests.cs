// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1880 / #1881: the space radar is a heading-turned top-down disc, and height travels as an ▲/▼ cue with
/// its own distance — plus the instruments' ALT readout.</summary>
public sealed class SpaceRadarMathTests
{
    private const float Eps = 1e-3f;

    private static void Near(float expected, float actual, float tolerance = Eps)
        => Assert.True(System.Math.Abs(expected - actual) <= tolerance, $"expected {expected} ± {tolerance}, got {actual}");

    [Fact]
    public void Project_HeadingNorth_MapsEastToRight_AndNorthToAhead()
    {
        var east = SpaceRadarMath.Project(10f, 0f, 0f, 1f);
        Near(10f, east.Right);
        Near(0f, east.Ahead);

        var north = SpaceRadarMath.Project(0f, 10f, 0f, 1f);
        Near(0f, north.Right);
        Near(10f, north.Ahead);
    }

    [Fact]
    public void Project_TurnsWithTheHeading()
    {
        // Heading east: a target to the east is straight ahead, one to the north is on the left.
        var ahead = SpaceRadarMath.Project(10f, 0f, 1f, 0f);
        Near(0f, ahead.Right);
        Near(10f, ahead.Ahead);

        var left = SpaceRadarMath.Project(0f, 10f, 1f, 0f);
        Near(-10f, left.Right);
        Near(0f, left.Ahead);
    }

    [Fact]
    public void Project_IgnoresHowFarTheViewIsPitched()
    {
        // A chase camera tilted 17° down (0.956, -0.294) or a nose pitched 80° up only shortens the horizontal part of
        // the view vector — the disc must still turn with the same heading and keep the same scale.
        var level = SpaceRadarMath.Project(-12.4f, -14.8f, 0.7071f, 0.7071f);
        var tilted = SpaceRadarMath.Project(-12.4f, -14.8f, 0.676f, 0.676f);
        var steep = SpaceRadarMath.Project(-12.4f, -14.8f, 0.1228f, 0.1228f);
        Near(level.Right, tilted.Right);
        Near(level.Ahead, tilted.Ahead);
        Near(level.Right, steep.Right);
        Near(level.Ahead, steep.Ahead);
    }

    [Fact]
    public void Project_TheReportedWreck_SitsNearTheCentre_ForItsHorizontalGapOnly()
    {
        // Lyxette's snapshot: ship (-531.2, -217.2, -350.2), wreck (-543.6, 12, -365.0), heading 045°.
        var v = SpaceRadarMath.Project(-12.4f, -14.8f, 0.7071f, 0.7071f);
        float horizontal = (float)System.Math.Sqrt(v.Right * v.Right + v.Ahead * v.Ahead);
        Near(19.3f, horizontal, 0.05f); // the 229 units of height no longer leak into "behind"
    }

    [Fact]
    public void Project_LookingStraightUp_FallsBackToTheLastHeading()
    {
        var v = SpaceRadarMath.Project(0f, 10f, 0f, 0f, fallbackX: 0f, fallbackZ: -1f);
        Near(-10f, v.Ahead);

        var none = SpaceRadarMath.Project(0f, 10f, 0f, 0f, fallbackX: 0f, fallbackZ: 0f); // nothing usable: north
        Near(10f, none.Ahead);
    }

    [Theory]
    [InlineData(229f, 1)]
    [InlineData(10.5f, 1)]
    [InlineData(10f, 0)]
    [InlineData(0f, 0)]
    [InlineData(-10f, 0)]
    [InlineData(-10.5f, -1)]
    [InlineData(-150f, -1)]
    public void VerticalState_UsesTheTenUnitBand(float dy, int expected) => Assert.Equal(expected, SpaceRadarMath.VerticalState(dy));

    [Fact]
    public void Glyph_IsAnArrowOnlyOffLevel()
    {
        Assert.Equal("▲", SpaceRadarMath.Glyph(1));
        Assert.Equal("▼", SpaceRadarMath.Glyph(-1));
        Assert.Equal(string.Empty, SpaceRadarMath.Glyph(0));
    }

    [Fact]
    public void DistanceWithHeight_AddsTheClimb_OnlyWhenOffLevel()
    {
        Assert.Equal("2 300 km · ▲ 2 292 km", SpaceRadarMath.DistanceWithHeight(230f, 229.2f, "{0} km"));
        Assert.Equal("850 km · ▼ 500 km", SpaceRadarMath.DistanceWithHeight(85f, -50f, "{0} km"));
        Assert.Equal("850 km", SpaceRadarMath.DistanceWithHeight(85f, 4.5f, "{0} km"));
        Assert.Equal("850 км · ▲ 120 км", SpaceRadarMath.DistanceWithHeight(85f, 12f, "{0} км"));
    }

    [Theory]
    [InlineData(12f, "+120 km", 120)]
    [InlineData(-217.2f, "-2 172 km", -2172)]
    [InlineData(0f, "0 km", 0)]
    [InlineData(-0.04f, "0 km", 0)]
    [InlineData(0.04f, "0 km", 0)]
    public void Altitude_IsSignedKilometresOverTheFlightPlane(float y, string expected, int km)
    {
        Assert.Equal(expected, SpaceRadarMath.Altitude(y, "{0} km"));
        Assert.Equal(km, SpaceRadarMath.SignedKm(y));
    }

    [Fact]
    public void Heading_NormalisesTheViewDirection()
    {
        SpaceRadarMath.Heading(3f, 4f, 0f, 1f, out float x, out float z);
        Assert.InRange(x, 0.6f - Eps, 0.6f + Eps);
        Assert.InRange(z, 0.8f - Eps, 0.8f + Eps);
    }
}
