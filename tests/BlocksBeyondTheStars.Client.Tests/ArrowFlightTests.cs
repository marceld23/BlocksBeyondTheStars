// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Numerics;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2647 — a hunter's arrow (<see cref="ArrowFlight"/>): a quarter-second shot over a hunter's 12 m on a gentle arc that
/// starts at the bow and ends at the animal, pointing along its path; and the shot animal waits for its arrow
/// (<see cref="ArrowLandings"/>) instead of sparkling away while the arrow is still on its way.
/// </summary>
public sealed class ArrowFlightTests
{
    private static readonly Vector3 Bow = new(10f, 41.5f, -3f);
    private static readonly Vector3 Deer = new(22f, 40.6f, -3f);

    [Fact]
    public void AHuntersShot_TakesAboutAQuarterSecond_AndIsAlwaysSeen()
    {
        Assert.Equal(0.25f, ArrowFlight.Seconds(12f), 3);
        Assert.Equal(ArrowFlight.MinSeconds, ArrowFlight.Seconds(0f));
        Assert.Equal(ArrowFlight.MinSeconds, ArrowFlight.Seconds(1f));
        Assert.Equal(ArrowFlight.MaxSeconds, ArrowFlight.Seconds(400f));
        Assert.Equal(ArrowFlight.Seconds(Vector3.Distance(Bow, Deer)), ArrowFlight.Seconds(Bow, Deer));
    }

    [Fact]
    public void TheArrow_StartsAtTheBow_EndsAtTheAnimal_AndArcsGentlyBetween()
    {
        AssertNear(Bow, ArrowFlight.Position(Bow, Deer, 0f));
        AssertNear(Deer, ArrowFlight.Position(Bow, Deer, 1f));
        AssertNear(Bow, ArrowFlight.Position(Bow, Deer, -1f)); // clamped
        AssertNear(Deer, ArrowFlight.Position(Bow, Deer, 2f));

        var mid = ArrowFlight.Position(Bow, Deer, 0.5f);
        float straight = (Bow.Y + Deer.Y) / 2f;
        Assert.Equal(straight + ArrowFlight.ArcHeight(Vector3.Distance(Bow, Deer)), mid.Y, 4);
        Assert.InRange(mid.Y - straight, 0.2f, ArrowFlight.MaxArc);
        Assert.Equal(ArrowFlight.MaxArc, ArrowFlight.ArcHeight(1000f));
        Assert.Equal(0f, ArrowFlight.ArcHeight(-3f));
    }

    [Fact]
    public void TheArrow_PointsAlongItsPath_NoseUpFirstThenDown()
    {
        var start = ArrowFlight.Direction(Bow, Deer, 0f);
        var end = ArrowFlight.Direction(Bow, Deer, 1f);
        Assert.Equal(1f, start.Length(), 4);
        Assert.Equal(1f, end.Length(), 4);
        Assert.True(start.X > 0.9f && end.X > 0.9f, "flies towards the animal");
        Assert.True(start.Y > end.Y, "noses up as it leaves, down as it arrives");

        // The tangent agrees with the sampled path.
        var a = ArrowFlight.Position(Bow, Deer, 0.40f);
        var b = ArrowFlight.Position(Bow, Deer, 0.41f);
        var sampled = Vector3.Normalize(b - a);
        Assert.True(Vector3.Dot(sampled, ArrowFlight.Direction(Bow, Deer, 0.405f)) > 0.999f);

        Assert.Equal(Vector3.UnitZ, ArrowFlight.Direction(Bow, Bow, 0.5f)); // a shot of no length: any sane direction
    }

    [Fact]
    public void AShotAnimal_WaitsForItsArrow_ButNeverLong()
    {
        var landings = new ArrowLandings();
        Assert.False(landings.Holds(Deer, 0f));

        landings.Add(Deer, landsAt: 10.4f);
        Assert.True(landings.Holds(Deer, 10f));
        Assert.True(landings.Holds(Deer + new Vector3(2f, 0f, 1f), 10f)); // the client's smoothed animal stands nearby
        Assert.False(landings.Holds(Deer + new Vector3(5f, 0f, 0f), 10f)); // another animal elsewhere goes at once
        Assert.False(landings.Holds(Deer, 10.4f));                         // the arrow has arrived: sparkle now

        landings.Add(Deer, landsAt: 30f);
        Assert.False(landings.Holds(Deer, 20f)); // never held longer than MaxHoldSeconds

        landings.Prune(10.5f);
        Assert.Equal(1, landings.Count);
        landings.Clear();
        Assert.Equal(0, landings.Count);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

    [Fact]
    public void TheLandings_StayBounded()
    {
        var landings = new ArrowLandings();
        for (int i = 0; i < ArrowLandings.Capacity * 3; i++)
        {
            landings.Add(new Vector3(i * 10f, 0f, 0f), 100f);
        }

        Assert.Equal(ArrowLandings.Capacity, landings.Count);
        Assert.False(landings.Holds(Vector3.Zero, 99.5f), "the oldest arrow was dropped");
        Assert.True(landings.Holds(new Vector3(((ArrowLandings.Capacity * 3) - 1) * 10f, 0f, 0f), 99.5f));
    }
}
