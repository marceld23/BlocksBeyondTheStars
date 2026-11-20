// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The lively places on the client: the place the player stands in, the greeting when they walk in (once, again only after
/// a while away or when a market starts), and the place's sound layer by size and time of day.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class PlaceTrackerTests
{
    private const int Circumference = 4096;

    private static NetPlace Village(string key = "settle_1", int minX = 100, string tier = "village", string state = "") => new()
    {
        Key = key,
        Kind = PlaceKinds.Settlement,
        Name = "Mira Mill",
        Tier = tier,
        MinX = minX,
        MaxX = minX + 55,
        MinZ = 200,
        MaxZ = 255,
        Y = 70,
        State = state,
    };

    [Fact]
    public void WalkingIn_IsGreetedOnce_AndTheEdgeDoesNotFlicker()
    {
        var tracker = new PlaceTracker();
        var places = new[] { Village() };

        Assert.Null(tracker.Update(places, 80, 71, 220, Circumference, now: 0));
        Assert.Null(tracker.Current);

        var greeted = tracker.Update(places, 98, 71, 220, Circumference, now: 1); // two blocks before the footprint: in
        Assert.Same(places[0], greeted);
        Assert.Same(places[0], tracker.Current);
        Assert.Null(tracker.Update(places, 120, 71, 220, Circumference, now: 2)); // still there: no second greeting

        // Along the edge: stepping a few blocks out keeps the player "in" (leaving needs more than entering) …
        Assert.Null(tracker.Update(places, 93, 71, 220, Circumference, now: 3));
        Assert.Same(places[0], tracker.Current);
        // … and really leaving and coming straight back is not greeted again.
        Assert.Null(tracker.Update(places, 70, 71, 220, Circumference, now: 4));
        Assert.Null(tracker.Current);
        Assert.Null(tracker.Update(places, 110, 71, 220, Circumference, now: 5));
        Assert.Same(places[0], tracker.Current);

        // After a long time away the place greets again.
        Assert.Null(tracker.Update(places, 70, 71, 220, Circumference, now: 6));
        Assert.Same(places[0], tracker.Update(places, 110, 71, 220, Circumference, now: 6 + PlaceTracker.GreetAgainSeconds + 1));
    }

    [Fact]
    public void AMarketThatStartsWhileThePlayerStandsThere_IsAnnounced()
    {
        var tracker = new PlaceTracker();
        Assert.NotNull(tracker.Update(new[] { Village() }, 110, 71, 220, Circumference, now: 0));
        var market = Village(state: PlaceStates.Market); // the server sent the list again
        Assert.Same(market, tracker.Update(new[] { market }, 110, 71, 220, Circumference, now: 10));
        Assert.Null(tracker.Update(new[] { market }, 110, 71, 220, Circumference, now: 11));
        Assert.Null(tracker.Update(new[] { Village() }, 110, 71, 220, Circumference, now: 500)); // the market closing is no news
    }

    [Fact]
    public void AFootprintAcrossTheSeam_AndTheHeight_AreRespected()
    {
        var seam = Village(minX: Circumference - 20); // reaches over the east–west seam
        Assert.True(PlaceTracker.Contains(seam, 10, 71, 220, Circumference, 0));
        Assert.True(PlaceTracker.Contains(seam, Circumference - 5, 71, 220, Circumference, 0));
        Assert.False(PlaceTracker.Contains(seam, 60, 71, 220, Circumference, 0));
        Assert.False(PlaceTracker.Contains(seam, 10, 71 - PlaceTracker.HeightReach - 5, 220, Circumference, 0)); // a deep cave below
    }

    [Fact]
    public void TheFirstOfTwoNeighbours_KeepsThePlayer_UntilTheyHaveLeftIt()
    {
        var tracker = new PlaceTracker();
        var a = Village("a", minX: 100);
        var b = Village("b", minX: 160); // four blocks east of a's edge
        Assert.Same(a, tracker.Update(new[] { a, b }, 150, 71, 220, Circumference, now: 0));
        Assert.Null(tracker.Update(new[] { a, b }, 158, 71, 220, Circumference, now: 1)); // in both margins: still a
        Assert.Same(a, tracker.Current);
        Assert.Same(b, tracker.Update(new[] { a, b }, 175, 71, 220, Circumference, now: 2)); // well into b
    }

    [Fact]
    public void AChildsFigure_HasALargerHead_AndItsNameplateSitsOverIt()
    {
        Assert.True(ChildFigure.Is("child"));
        Assert.False(ChildFigure.Is(string.Empty));
        Assert.False(ChildFigure.Is("gds_guard"));

        // The neck stays where it is: the larger head only grows upward.
        float adultNeck = ChildFigure.HeadCentre - (ChildFigure.HeadSize / 2f);
        float childNeck = ChildFigure.ChildHeadCentre - (ChildFigure.HeadSize * ChildFigure.HeadScale / 2f);
        Assert.Equal(adultNeck, childNeck, 4);
        Assert.True(ChildFigure.Top(child: true) > ChildFigure.Top(child: false));

        // A grown-up's plate is where it always was, whatever its ±8 % of size; a child's sits just over its own head.
        Assert.Equal(ChildFigure.AdultPlate, ChildFigure.PlateHeight(1f, child: false));
        Assert.Equal(ChildFigure.AdultPlate, ChildFigure.PlateHeight(0.92f, child: false));
        Assert.Equal(ChildFigure.AdultPlate, ChildFigure.PlateHeight(0f, child: false)); // an older server sends no size
        float plate = ChildFigure.PlateHeight(0.62f, child: true);
        Assert.InRange(plate, (ChildFigure.Top(child: true) * 0.62f) + 0.1f, 1.9f);
        Assert.True(plate < ChildFigure.AdultPlate - 0.3f, "a child's nameplate hangs as high as a grown-up's");
    }

    [Fact]
    public void SmallCreatures_GatherAtAGardenByDay_AndAtALanternByNight_NeverAtThePlayersFeet()
    {
        const int Circ = 4096;
        var place = Village();
        place.Points = new[]
        {
            new NetPlacePoint { X = 110, Y = 64, Z = 100, Kind = PlacePointKinds.Garden }, // 10 east of the player
            new NetPlacePoint { X = 100, Y = 64, Z = 120, Kind = PlacePointKinds.Garden }, // 20 south
            new NetPlacePoint { X = 102, Y = 64, Z = 100, Kind = PlacePointKinds.Garden }, // too near: nothing pops in at the feet
            new NetPlacePoint { X = 160, Y = 64, Z = 100, Kind = PlacePointKinds.Garden }, // too far: it would be culled at once
            new NetPlacePoint { X = 100, Y = 64, Z = 88, Kind = PlacePointKinds.Lamp },    // 12 north
            new NetPlacePoint { X = 112, Y = 64, Z = 100, Kind = PlacePointKinds.Fire },   // never a gathering point
        };

        // By day one of the two gardens in range, by the roll; the offset is from the player to the point's middle.
        Assert.True(PlaceGathering.TryPick(place, night: false, 100f, 100f, Circ, 0.0f, out float dx, out float dz));
        Assert.Equal((10.5f, 0.5f), (dx, dz));
        Assert.True(PlaceGathering.TryPick(place, night: false, 100f, 100f, Circ, 0.99f, out dx, out dz));
        Assert.Equal((0.5f, 20.5f), (dx, dz));

        // By night the lantern, whatever the roll.
        Assert.True(PlaceGathering.TryPick(place, night: true, 100f, 100f, Circ, 0.7f, out dx, out dz));
        Assert.Equal((0.5f, -11.5f), (dx, dz));

        // The short way round the seam: a lantern just west of the seam, the player just east of it.
        place.Points = new[] { new NetPlacePoint { X = Circ - 6, Y = 64, Z = 100, Kind = PlacePointKinds.Lamp } };
        Assert.True(PlaceGathering.TryPick(place, night: true, 5f, 100f, Circ, 0.5f, out dx, out dz));
        Assert.Equal(-10.5f, dx);

        // Nothing in range, no points, no place: the caller keeps its ring.
        Assert.False(PlaceGathering.TryPick(place, night: false, 5f, 100f, Circ, 0.5f, out _, out _)); // a lantern is no garden
        place.Points = System.Array.Empty<NetPlacePoint>();
        Assert.False(PlaceGathering.TryPick(place, night: true, 5f, 100f, Circ, 0.5f, out _, out _));
        Assert.False(PlaceGathering.TryPick(null, night: true, 5f, 100f, Circ, 0.5f, out _, out _));
        Assert.InRange(PlaceGathering.Share, 0.2f, 0.7f); // a share: the wild around the place keeps its creatures
    }

    [Fact]
    public void ThePlaceLayer_FollowsSizeDayAndMarket()
    {
        Assert.Equal(PlaceAmbience.None, PlaceAmbience.LoopFor(null, 0.4f));
        Assert.Equal(PlaceAmbience.Village, PlaceAmbience.LoopFor(Village(), 0.4f));
        Assert.Equal(PlaceAmbience.Town, PlaceAmbience.LoopFor(Village(tier: "town"), 0.4f));
        Assert.Equal(PlaceAmbience.Market, PlaceAmbience.LoopFor(Village(state: PlaceStates.Market), 0.4f));
        Assert.Equal(PlaceAmbience.None, PlaceAmbience.LoopFor(Village(), 0.9f));                 // night
        Assert.Equal(PlaceAmbience.None, PlaceAmbience.LoopFor(Village(tier: "hamlet"), 0.4f));   // three houses do not murmur
        Assert.Equal(PlaceAmbience.Market, PlaceAmbience.LoopFor(Village(tier: "hamlet", state: PlaceStates.Market), 0.4f));
        Assert.Equal(PlaceAmbience.StationDeck, PlaceAmbience.LoopFor(new NetPlace { Kind = PlaceKinds.Station }, 0.9f));

        Assert.True(PlaceAmbience.LevelFor(Village(tier: "town"), 0.4f) > PlaceAmbience.LevelFor(Village(), 0.4f));
        Assert.True(PlaceAmbience.LevelFor(Village(), 0.75f) < PlaceAmbience.LevelFor(Village(), 0.5f)); // quieter in the evening
        Assert.Equal(0f, PlaceAmbience.LevelFor(Village(), 0.1f));
    }

    [Fact]
    public void AStationsHum_IsComputedFromItsSizeAndItsPurpose()
    {
        static NetPlace Station(string tier, string purpose = "") => new() { Kind = PlaceKinds.Station, Tier = tier, Purpose = purpose };

        // Round the clock, and louder the bigger the station.
        string[] tiers = { "small", "medium", "large", "huge", "colossal" };
        for (int i = 1; i < tiers.Length; i++)
        {
            Assert.True(PlaceAmbience.LevelFor(Station(tiers[i]), 0.9f) > PlaceAmbience.LevelFor(Station(tiers[i - 1]), 0.9f), tiers[i]);
            Assert.True(PlaceAmbience.PitchFor(Station(tiers[i])) < PlaceAmbience.PitchFor(Station(tiers[i - 1])), tiers[i] + " hums deeper");
        }

        Assert.InRange(PlaceAmbience.LevelFor(Station("small"), 0.1f), 0.5f, 1f);
        Assert.Equal(PlaceAmbience.LevelFor(Station("large"), 0.4f), PlaceAmbience.LevelFor(Station("a tier from later"), 0.4f));

        // The purpose bends it: a smelter's station deeper, a research station brighter, a trade station as recorded.
        float trade = PlaceAmbience.PitchFor(Station("large", StationPurposes.Trade));
        Assert.Equal(1f, trade);
        Assert.Equal(trade, PlaceAmbience.PitchFor(Station("large")));
        Assert.True(PlaceAmbience.PitchFor(Station("large", StationPurposes.Mining)) < trade);
        Assert.True(PlaceAmbience.PitchFor(Station("large", StationPurposes.Research)) > trade);

        // Never far from the recording (a hum, not a siren or a growl) — and a settlement's bed is never bent.
        foreach (string tier in tiers)
        {
            foreach (string purpose in StationPurposes.All)
            {
                Assert.InRange(PlaceAmbience.PitchFor(Station(tier, purpose)), 0.85f, 1.15f);
            }
        }

        Assert.Equal(1f, PlaceAmbience.PitchFor(Village(tier: "town")));
        Assert.Equal(1f, PlaceAmbience.PitchFor(null));
    }
}
