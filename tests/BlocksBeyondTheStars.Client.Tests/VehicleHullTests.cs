// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The parked-vehicle "player enclosed" test (#1669) against the real hull numbers: a capsule touching the
/// hull from any of the four sides is NOT enclosed (the collider stays on — blocked), feet on top of the hull
/// are NOT enclosed (you stand on it), and a player really inside the hull IS (so they can walk out).
/// </summary>
public sealed class VehicleHullTests
{
    // SpeederView.MeshOffset / BoatMeshOffset: the hull's min corner relative to the vehicle root.
    private const float SpeederOffX = -1f, SpeederOffY = -0.2f, SpeederOffZ = -2f;
    private const float BoatOffX = -1f, BoatOffY = -0.8f, BoatOffZ = -2f;
    private const float R = VehicleHull.PlayerRadius;

    [Theory]
    [InlineData(SpeederOffX, SpeederOffY, SpeederOffZ)]
    [InlineData(BoatOffX, BoatOffY, BoatOffZ)]
    public void TouchingTheHullFromOutside_IsNotEnclosed_OnAllFourSides(float ox, float oy, float oz)
    {
        float feet = oy + 0.2f; // standing on the ground the hull rests on
        float midX = ox + VehicleHull.Width / 2f, midZ = oz + VehicleHull.Length / 2f;

        Assert.False(VehicleHull.Encloses(ox - R, feet, midZ, ox, oy, oz));                       // −x side
        Assert.False(VehicleHull.Encloses(ox + VehicleHull.Width + R, feet, midZ, ox, oy, oz));   // +x side
        Assert.False(VehicleHull.Encloses(midX, feet, oz - R, ox, oy, oz));                       // −z (stern)
        Assert.False(VehicleHull.Encloses(midX, feet, oz + VehicleHull.Length + R, ox, oy, oz));  // +z (bow)
    }

    [Theory]
    [InlineData(SpeederOffX, SpeederOffY, SpeederOffZ)]
    [InlineData(BoatOffX, BoatOffY, BoatOffZ)]
    public void StandingOnTheHull_IsNotEnclosed(float ox, float oy, float oz)
    {
        float top = oy + VehicleHull.Height;
        Assert.False(VehicleHull.Encloses(ox + 1.5f, top, oz + 2.5f, ox, oy, oz));
        Assert.False(VehicleHull.Encloses(ox + 1.5f, top + 0.05f, oz + 2.5f, ox, oy, oz));
    }

    [Theory]
    [InlineData(SpeederOffX, SpeederOffY, SpeederOffZ)]
    [InlineData(BoatOffX, BoatOffY, BoatOffZ)]
    public void InsideTheHull_IsEnclosed(float ox, float oy, float oz)
    {
        Assert.True(VehicleHull.Encloses(0f, 0f, 0f, ox, oy, oz));                       // the seat (the root)
        Assert.True(VehicleHull.Encloses(ox + 1.5f, oy + 1f, oz + 2.5f, ox, oy, oz));   // standing on the hull floor
        Assert.True(VehicleHull.Encloses(ox + R + 0.05f, oy, oz + R + 0.05f, ox, oy, oz)); // just inside a corner
    }

    [Fact]
    public void TheOldRootCentredBox_WouldHaveLetTheSternThrough_TheNewOneDoesNot()
    {
        // The regression itself: the −z contact point (z = −2.35) lay inside the old |z| < 3.1 box.
        Assert.False(VehicleHull.Encloses(0.5f, 0f, SpeederOffZ - R, SpeederOffX, SpeederOffY, SpeederOffZ));
    }
}
