// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — what happens at a place: a market day every third in-game day (travelling merchants at the plaza
/// while the sun is up, the place says so), a calendar festival that gathers the village in the early evening (a ring, the
/// tune, dancing — no speech, no present), and the night watch of a town (a round with a lantern through the night, asleep
/// in the morning).
/// </summary>
public sealed class PlaceEventTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Marketham";
    private const double Day = 0.40;
    private const double EarlyEvening = 0.63;
    private const double Night = 0.92;
    private const double Morning = 0.30;

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: configure);
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        return w;
    }

    private static Vector3f At(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet, NpcLifeWorld.Cz + dz + 0.5f);

    private static Vector3f Marker(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet + 0.5f, NpcLifeWorld.Cz + dz + 0.5f);

    private static NetPlace PlaceOf(NpcLifeWorld w) => Assert.Single(w.Server.PlaceListForTest().Places);

    /// <summary>Moves the in-game calendar on until today is (or is not) a market day of the village.</summary>
    private static void GoToMarketDay(NpcLifeWorld w, bool marketDay)
    {
        for (int i = 0; i < 4 && w.Server.IsMarketDayForTest(Village) != marketDay; i++)
        {
            w.Server.AdvanceSystemDaysForTest(1.0);
        }

        Assert.Equal(marketDay, w.Server.IsMarketDayForTest(Village));
    }

    [Fact]
    public void ThePlaceList_NamesTheSettlement_ItsBoxItsPlazaAndWhatItOffers()
    {
        using var w = World("place_list");
        w.Server.AddSettlementMarkerForTest(Village, "vendor", Marker(w, 12, 0));
        w.Server.AddSettlementMarkerForTest(Village, "tavern", Marker(w, -8, 3));
        w.Server.AddSettlementMarkerForTest(Village, "doctor", Marker(w, 3, -9));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.FireMarker, Marker(w, 2, 2));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.SignMarker, Marker(w, 12, -2), "fn=market;side=0");

        var place = PlaceOf(w);
        Assert.Equal(PlaceKinds.Settlement, place.Kind);
        Assert.Equal(Village, place.Name);
        Assert.Equal("village", place.Tier);
        Assert.StartsWith("settle_", place.Key);
        Assert.Equal((NpcLifeWorld.Cx - 20, NpcLifeWorld.Cz - 20, NpcLifeWorld.Cx + 20, NpcLifeWorld.Cz + 20), (place.MinX, place.MinZ, place.MaxX, place.MaxZ));
        Assert.Equal((NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz), (place.PlazaX, place.PlazaY, place.PlazaZ));
        Assert.Equal(new[] { "market", "tavern", "clinic" }, place.Trades);
        var sign = Assert.Single(place.Signs);
        Assert.Equal(("market", (byte)0, NpcLifeWorld.Cx + 12, NpcLifeWorld.Cz - 2), (sign.Fn, sign.Side, sign.X, sign.Z));
        Assert.Contains(place.Points, p => p.Kind == PlacePointKinds.Fire && p.X == NpcLifeWorld.Cx + 2 && p.Z == NpcLifeWorld.Cz + 2);
        Assert.Equal(PlaceStates.None, place.State);
    }

    [Fact]
    public void OnAMarketDay_MerchantsStandAtThePlaza_ThePlaceSaysSo_AndTheyLeaveInTheEvening()
    {
        using var w = World("place_market");
        w.Server.AddSettlementMarkerForTest(Village, "vendor", Marker(w, 12, 0));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.RoadExitMarker, Marker(w, 0, -19), "side=0");

        GoToMarketDay(w, marketDay: false);
        w.Server.TickPlaceEventsForTest();
        Assert.Empty(w.Server.MarketTradersForTest(Village));
        Assert.Equal(PlaceStates.None, PlaceOf(w).State);

        GoToMarketDay(w, marketDay: true);
        w.Server.TickPlaceEventsForTest();
        int trader = Assert.Single(w.Server.MarketTradersForTest(Village)); // a village: one
        Assert.Equal(PlaceStates.Market, PlaceOf(w).State);
        Assert.Equal("vendor", w.Server.NpcRosterForTest().Single(n => n.Id == trader).Role);
        Assert.Contains(w.Transport.Sent.Select(s => s.Msg).OfType<PlaceList>(), l => l.Places.Any(p => p.State == PlaceStates.Market));

        // It walks in from the road and stands at the plaza.
        var plaza = At(w, 0, 0);
        w.TickAt(Day, 120, until: () => Dist(w.Server.NpcRoutineForTest(trader).Pos, plaza) < 3.5);
        Assert.True(Dist(w.Server.NpcRoutineForTest(trader).Pos, plaza) < 3.5, "the merchant never reached the plaza");

        // The evening: the market closes, the merchant walks out and is gone.
        w.TickAt(EarlyEvening, 180, until: () => w.Server.NpcRosterForTest().All(n => n.Id != trader));
        Assert.Empty(w.Server.MarketTradersForTest(Village));
        Assert.DoesNotContain(w.Server.NpcRosterForTest(), n => n.Id == trader);
        Assert.Equal(PlaceStates.None, PlaceOf(w).State);
    }

    [Fact]
    public void AHamlet_AndAPlaceWithoutAVendor_HoldNoMarket()
    {
        using var w = World("place_no_market");
        for (int i = 0; i < 3; i++)
        {
            w.Server.TickPlaceEventsForTest();
            Assert.False(w.Server.IsMarketDayForTest(Village)); // no vendor: never
            w.Server.AdvanceSystemDaysForTest(1.0);
        }

        Assert.Empty(w.Server.MarketTradersForTest(Village));
    }

    [Fact]
    public void ACalendarFestival_GathersTheVillageInTheEarlyEvening_OncePerDay_WithoutSpeech()
    {
        string festival = Content.Value.Festivals.First().Key;
        using var w = World("place_gathering", c => c.FestivalsOverride = festival);
        var ids = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            ids.Add(w.Server.SpawnVillagerForTest(Village, At(w, -10 + (4 * i), 9)));
        }

        // By day nothing gathers.
        w.TickAt(Day, 4);
        w.Server.TickPlaceEventsForTest();
        Assert.Equal(PlaceStates.None, PlaceOf(w).State);

        // The early evening: the ring forms at the plaza, the tune plays, the ring dances — nobody speaks.
        w.TickAt(EarlyEvening, 2);
        w.Server.TickPlaceEventsForTest();
        Assert.Equal(PlaceStates.Festival, PlaceOf(w).State);
        int greetingsBefore = w.Transport.Sent.Select(s => s.Msg).OfType<NpcGreeting>().Count(g => g.Kind == NpcGreetingKinds.Cheer);
        w.TickAt(EarlyEvening, 40);
        Assert.All(ids, id => Assert.Equal("npc.activity.celebrating", w.Server.NpcRoutineForTest(id).Activity));
        var plaza = At(w, 0, 0);
        Assert.All(ids, id => Assert.InRange(Dist(w.Server.NpcRoutineForTest(id).Pos, plaza), 1.5, 7.5));
        Assert.Contains(w.Transport.Sent.Select(s => s.Msg).OfType<NpcEmote>(), e => e.Gesture is NpcGestures.Dance or NpcGestures.Clap);
        Assert.Contains(w.Transport.Sent.Select(s => s.Msg).OfType<SoundFx>(), s => s.SoundId.StartsWith("note_1_", StringComparison.Ordinal));
        Assert.Equal(greetingsBefore, w.Transport.Sent.Select(s => s.Msg).OfType<NpcGreeting>().Count(g => g.Kind == NpcGreetingKinds.Cheer));

        // Two minutes later it is over — and it does not start again the same day.
        w.TickAt(EarlyEvening, 130);
        w.Server.TickPlaceEventsForTest();
        Assert.Equal(PlaceStates.None, PlaceOf(w).State);
        w.TickAt(EarlyEvening, 10);
        w.Server.TickPlaceEventsForTest();
        Assert.Equal(PlaceStates.None, PlaceOf(w).State);
    }

    [Fact]
    public void WithoutACalendarFestival_NothingGathers()
    {
        using var w = World("place_no_gathering"); // the suite's clock stands on a day without a festival
        for (int i = 0; i < 5; i++)
        {
            w.Server.SpawnVillagerForTest(Village, At(w, -10 + (4 * i), 9));
        }

        w.TickAt(EarlyEvening, 5);
        w.Server.TickPlaceEventsForTest();
        Assert.Equal(PlaceStates.None, PlaceOf(w).State);
    }

    [Fact]
    public void TheNightWatch_WalksItsRoundWithALantern_AndSleepsInTheMorning()
    {
        using var w = World("place_watch");
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.LampMarker, Marker(w, 10, 10));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.LampMarker, Marker(w, -10, 10));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.LampMarker, Marker(w, -10, -10));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.RoadExitMarker, Marker(w, 15, -15), "side=0");
        var home = At(w, 6, 6);
        int id = w.Server.SpawnVillagerForTest(Village, home, job: "watch");

        var stops = new HashSet<Vector3i>();
        w.TickAt(Night, 400, until: () => stops.Count >= 3, every: () =>
        {
            var r = w.Server.NpcRoutineForTest(id);
            if (r.Held == "npc_lantern" && Dist(r.Pos, home) > 4 && !w.Server.NpcHasGoalForTest(id))
            {
                stops.Add(r.Pos.ToBlock());
            }
        });

        Assert.True(stops.Count >= 3, $"the watch stood at only {stops.Count} waypoint(s)");
        Assert.Equal("npc.activity.night_watch", w.Server.NpcRoutineForTest(id).Activity);

        // The morning: off duty, the lantern is out, home.
        w.TickAt(Morning, 120, until: () => Dist(w.Server.NpcRoutineForTest(id).Pos, home) < 2.5 && w.Server.NpcRoutineForTest(id).Held.Length == 0);
        var morning = w.Server.NpcRoutineForTest(id);
        Assert.Equal(string.Empty, morning.Held);
        Assert.True(Dist(morning.Pos, home) < 2.5, $"the watch is not home in the morning: {morning.Pos}");
        Assert.NotEqual("npc.activity.night_watch", morning.Activity);

        // On its round the watch has hello lines of its own — written, without a name in them (a stranger hears them too).
        foreach (var locale in new[] { BlocksBeyondTheStars.Shared.Localization.GameLocale.English, BlocksBeyondTheStars.Shared.Localization.GameLocale.German })
        {
            var loc = w.Content.CreateLocalizer(locale);
            for (int i = 1; i <= BlocksBeyondTheStars.GameServer.GameServer.NightWatchHelloLines; i++)
            {
                string line = loc.Get($"npc.hello.watch.{i}");
                Assert.NotEqual($"npc.hello.watch.{i}", line);
                Assert.DoesNotContain("{0}", line, StringComparison.Ordinal);
            }
        }
    }

    private static double Dist(Vector3f a, Vector3f b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));
}
