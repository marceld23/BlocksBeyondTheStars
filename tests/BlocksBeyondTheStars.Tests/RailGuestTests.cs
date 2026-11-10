// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3447 — friendly visitors by train (Lyxette: "the settlers who visit me should come by train instead of appearing out of
/// nowhere"): a base with a stop on a line to a town 150+ blocks away gets its neighbour from that town by train — it rides,
/// steps off at the base's stop, walks to the owner, and leaves the same way (to the platform, the next train, gone with it).
/// A nearer town keeps the walk-in, and no train in time brings the walk-in too. A base's residents ride to the connected
/// town and back. The admin's <c>/railtrip</c> and <c>/guest</c> start one now, and the bump snapshot lists the lines, the
/// trains with their passengers, the trips and the nearest base's stops and guests.
/// </summary>
public sealed class RailGuestTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const int FarX = NpcLifeWorld.Cx + 170;

    private static void Cheats(ServerConfig c)
    {
        c.Rules.AdminCheats = true;
        c.Rules.AllowCheatsInSurvival = true;
        c.Rules.AllowCheatsInCreative = true;
    }

    private static float Flat(Vector3f a, Vector3f b) => (float)Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static Vector3f PosOf(RailTravelArena a, int id) => a.Server.NpcSnapshots.Single(n => n.Id == id).Pos;

    private static string Base(RailTravelArena a) => a.Server.BaseSnapshots.Single(b => b.Id == a.W.BaseId).Name;

    [Fact]
    public void AGuestFromAFarTown_ComesByTrain_StepsOffAtTheBaseStop_AndLeavesByTrain()
    {
        using var a = new RailTravelArena(Content.Value, "rail_guest_train", foundBase: true);
        a.Server.PauseRailTripPlannerForTest();
        int far = a.FarTown("Fernstadt", FarX)[0];
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        Assert.Equal(new[] { "Fernstadt" }, a.Server.TrainGuestTownsForTest(a.W.BaseId));

        // The visit is due: the neighbour comes from the far town, by train — not the walk-in.
        a.Server.TryBaseGuestVisitForTest("neighbour");
        var trip = Assert.Single(a.Server.RailTripsForTest);
        Assert.Equal(("Guest", "Fernstadt", Base(a)), (trip.Kind, trip.Home, trip.Away));
        Assert.Equal(far, trip.NpcId);
        Assert.Empty(a.Server.BaseGuestsForTest);
        string name = a.Server.NpcRosterForTest().Single(n => n.Id == far).Name;
        Assert.Contains(a.W.MessagesToPlayer(), m => m.Contains(name, StringComparison.Ordinal) && m.Contains("Fernstadt", StringComparison.Ordinal));

        // It rides the train and steps off at the base's stop: the visit begins there — the same person, its villager home.
        a.W.TickAt(0.4, 600, dt: 0.5, until: () => a.Server.BaseGuestsForTest.Count > 0);
        var guest = Assert.Single(a.Server.BaseGuestsForTest);
        Assert.Equal(far, guest.SourceNpcId);
        Assert.Equal(name, a.Server.NpcRosterForTest().Single(n => n.Id == guest.NpcId).Name);
        Assert.True(a.Server.BaseGuestByTrainForTest(guest.NpcId));
        Assert.True(a.Server.NpcVisitingAwayForTest(far));
        Assert.Empty(a.Server.RailTripsForTest);
        Assert.True(Flat(PosOf(a, guest.NpcId), new Vector3f(a.StopWest.X, 0, a.StopWest.Z)) < 8f, $"stepped off at {PosOf(a, guest.NpcId)}");

        // It walks to the owner and greets.
        a.W.TickAt(0.4, 200, until: () => a.Server.BaseGuestsForTest.Single().Greeted);
        Assert.True(a.Server.BaseGuestsForTest.Single().Greeted, "the train guest never reached the owner");

        // Time to go: back to the platform, on the next train, gone with it — the villager is home again.
        a.Server.EndBaseGuestWaitsForTest();
        a.W.TickAt(0.4, 600, dt: 0.5, until: () => a.Server.NpcRideForTest(guest.NpcId).TrainId.Length > 0 || a.Server.BaseGuestsForTest.Count == 0);
        Assert.Equal(a.TrainId, a.Server.NpcRideForTest(guest.NpcId).TrainId);
        Assert.Contains(a.W.MessagesToPlayer(), m => m.Contains(name, StringComparison.Ordinal) && m.Contains("train home", StringComparison.Ordinal));
        a.W.TickAt(0.4, 600, dt: 0.5, until: () => a.Server.BaseGuestsForTest.Count == 0);
        Assert.Empty(a.Server.BaseGuestsForTest);
        Assert.DoesNotContain(a.Server.NpcRosterForTest(), n => n.Id == guest.NpcId);
        Assert.False(a.Server.NpcVisitingAwayForTest(far));
        Assert.Empty(a.Server.NpcPassengersForTest(a.TrainId));
    }

    [Fact]
    public void ALeavingTrainGuestNobodyWatches_EndsItsVisit_AndAFailedGuestTripNoLongerKeepsTheBaseBusy()
    {
        using var a = new RailTravelArena(Content.Value, "rail_guest_unwatched", foundBase: true, configure: Cheats);
        a.Server.PauseRailTripPlannerForTest();
        int far = a.FarTown("Fernstadt", FarX)[0];
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);

        // The far town's guest comes by train, greets the owner and sets off back to the base's platform …
        a.Server.TryBaseGuestVisitForTest("neighbour");
        a.W.TickAt(0.4, 600, dt: 0.5, until: () => a.Server.BaseGuestsForTest.Count > 0);
        var guest = Assert.Single(a.Server.BaseGuestsForTest);
        Assert.True(a.Server.BaseGuestByTrainForTest(guest.NpcId));
        a.W.TickAt(0.4, 200, until: () => a.Server.BaseGuestsForTest.Single().Greeted);
        a.Server.EndBaseGuestWaitsForTest();
        a.W.TickAt(0.4, 1.5);
        Assert.Equal("Leaving", a.Server.BaseGuestsForTest.Single().Phase);
        Assert.True(a.Server.NpcHasGoalForTest(guest.NpcId), "the guest is not on its way to the platform");

        // … and the owner goes far away. NPCs only walk near a player, so that walk would never arrive: the visit simply
        // ends, out of everybody's sight, and the villager is home in its town again (the reviewer's repro waited 1200 s).
        a.FarAway();
        a.W.TickAt(0.4, 10, dt: 0.5, until: () => a.Server.BaseGuestsForTest.Count == 0);
        Assert.Empty(a.Server.BaseGuestsForTest);
        Assert.DoesNotContain(a.Server.NpcRosterForTest(), n => n.Id == guest.NpcId);
        Assert.False(a.Server.NpcVisitingAwayForTest(far));

        // The admin at the far town calls a guest by train; the train's autopilot goes off before it boards, so the trip
        // fails and the villager walks home in view (GoingHome). That villager is no guest on its way any more: /guest is
        // not "busy" (with no train for villagers and the owner away from the base, nobody can come right now).
        a.W.Player.State.Position = new Vector3f(FarX + 2.5f, a.W.Feet, RailTravelArena.LineZ + 11.5f);
        a.Server.HandleForTest(a.W.Player, new AdminCommandIntent { Command = "summon_guest" });
        var trip = Assert.Single(a.Server.RailTripsForTest);
        Assert.Equal(("Guest", far), (trip.Kind, trip.NpcId));
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 0);
        a.W.TickAt(0.4, 400, dt: 0.5, until: () => a.Server.RailTripsForTest.Any(t => t.Phase == "GoingHome"));
        Assert.Equal("GoingHome", Assert.Single(a.Server.RailTripsForTest).Phase);
        a.Server.HandleForTest(a.W.Player, new AdminCommandIntent { Command = "summon_guest" });
        var answer = a.W.Transport.Sent.Where(s => s.Conn == a.W.Player.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Last();
        Assert.Equal("@srv.admin.guest_none", answer.Reason);
    }

    [Fact]
    public void AVisitIsNeverSpentOnNobody_NoPlatformKeepsTheTrainOutOfTheRoll_AndAFailedStartBringsATraveller()
    {
        using var a = new RailTravelArena(Content.Value, "rail_guest_never_lost", foundBase: true);
        a.Server.PauseRailTripPlannerForTest();
        a.FarTown("Fernstadt", FarX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);

        // Somebody stands beside the far town's villager: it may not walk over unseen, so the only neighbour the base can get
        // is one by train.
        var watcher = a.Server.AddLocalPlayer("Watcher");
        watcher.State.AboardShip = false;
        watcher.State.Position = new Vector3f(FarX + 0.5f, a.W.Feet, RailTravelArena.LineZ + 11.5f);
        Assert.True(a.Server.TrainGuestPossibleForTest(a.W.BaseId));

        // The town's stop has no platform spot: the town is still on the line, but no guest can come by train — and the
        // neighbour's weight no longer counts the train, so the roll never lands on a neighbour who cannot come.
        var farStop = new Vector3i(FarX, a.W.Feet, RailTravelArena.LineZ + 3);
        a.Server.PinRailPlatformSpotForTest(farStop, null);
        Assert.Equal(new[] { "Fernstadt" }, a.Server.TrainGuestTownsForTest(a.W.BaseId));
        Assert.False(a.Server.TrainGuestPossibleForTest(a.W.BaseId));
        var kinds = Enumerable.Range(0, 40).Select(_ => a.Server.PickGuestKindForTest(a.W.BaseId)).ToList();
        Assert.DoesNotContain("neighbour", kinds);

        // The platform is back, but the train cannot start after all (as if it went again between the roll and the start):
        // the due visit does not fall through — no walk-in neighbour, so a traveller comes.
        a.Server.ForgetRailPlatformSpotsForTest();
        Assert.True(a.Server.TrainGuestPossibleForTest(a.W.BaseId));
        a.Server.BlockTrainGuestStartForTest();
        a.Server.TryBaseGuestVisitForTest("neighbour");
        Assert.Empty(a.Server.RailTripsForTest);
        Assert.Equal("traveler", Assert.Single(a.Server.BaseGuestsForTest).Kind);
    }

    [Fact]
    public void ANearTownKeepsTheWalkIn_NoTrainInTimeBringsTheWalkIn_AndResidentsRideToo()
    {
        using var a = new RailTravelArena(Content.Value, "rail_guest_fallback", foundBase: true);
        a.Server.PauseRailTripPlannerForTest();
        a.Town("Westdorf", RailTravelArena.StopWestX); // 32 blocks from the core: too near for the train
        a.FarTown("Fernstadt", FarX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        Assert.Equal(new[] { "Fernstadt" }, a.Server.TrainGuestTownsForTest(a.W.BaseId));

        // The far town's guest sets off, but no train halts any more (its autopilot off): after three minutes the walk-in
        // neighbour of the nearest village comes instead.
        a.Server.TryBaseGuestVisitForTest("neighbour");
        Assert.Equal("Guest", Assert.Single(a.Server.RailTripsForTest).Kind);
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 0);
        a.W.TickAt(0.4, 240, dt: 0.5, until: () => a.Server.BaseGuestsForTest.Count > 0);
        var walkIn = Assert.Single(a.Server.BaseGuestsForTest);
        Assert.Equal("neighbour", walkIn.Kind);
        Assert.False(a.Server.BaseGuestByTrainForTest(walkIn.NpcId));
        Assert.DoesNotContain(a.Server.RailTripsForTest, t => t.Kind == "Guest");

        // That guest greets, goes home and is gone; with the train taking no villagers there is no rail route at all.
        a.W.TickAt(0.4, 200, until: () => a.Server.BaseGuestsForTest.Single().Greeted);
        a.Server.EndBaseGuestWaitsForTest();
        a.W.TickAt(0.4, 5);
        a.FarAway();
        a.W.TickAt(0.4, 400, dt: 0.5, until: () => a.Server.BaseGuestsForTest.Count == 0);
        Assert.Empty(a.Server.BaseGuestsForTest);
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, autopilot: 1, passengers: 0);
        Assert.Empty(a.Server.TrainGuestTownsForTest(a.W.BaseId));
        a.Server.SetTrainForTest(a.PlayerId, a.TrainId, passengers: 1);

        // The base's own resident rides from the base's stop (StopEast: no town there, the base is near) to a town and back.
        a.Home();
        a.Server.ScanBaseLifeForTest();
        int resident = Assert.IsType<int>(a.Server.BaseSettlerForTest(a.W.BaseId));
        Assert.Equal((Base(a), "base"), a.Server.RailPlaceForTest(a.StopEast));
        var rest = a.Server.NpcRestForTest(resident);
        int npcs = a.Server.NpcCount;
        Assert.Equal(resident, a.Server.StartRailTripForTest(Base(a)));
        var trip = Assert.Single(a.Server.RailTripsForTest);
        Assert.Equal(("Resident", Base(a)), (trip.Kind, trip.Home));
        a.FarAway(); // out of reach: the timers and the real train
        bool rode = false;
        a.W.TickAt(0.4, 900, dt: 0.5, until: () => a.Server.RailTripsForTest.Count == 0, every: () =>
        {
            rode |= a.Server.NpcRideForTest(resident).TrainId.Length > 0;
            if (a.Server.RailTripsForTest.Any(t => t.Phase == "Visiting"))
            {
                a.Server.EndRailTripVisitsForTest();
            }
        });
        Assert.True(rode, "the resident never rode the train");
        Assert.Empty(a.Server.RailTripsForTest);
        Assert.True(Flat(rest, PosOf(a, resident)) < 2f, $"the resident is not home: {PosOf(a, resident)} vs {rest}");
        Assert.Equal(npcs, a.Server.NpcCount);
    }

    [Fact]
    public void TheAdminCommands_StartATripAndAGuest_AndTheBumpSnapshotDescribesTheRails()
    {
        using var a = new RailTravelArena(Content.Value, "rail_guest_admin", foundBase: true, configure: Cheats);
        a.Server.PauseRailTripPlannerForTest();
        a.Town("Westdorf", RailTravelArena.StopWestX);
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        a.Server.SetLocalDayFractionForTest(0.9, NpcLifeWorld.Cx); // night: the admin's trip goes anyway

        // /railtrip Ostdorf: a villager of Ostdorf takes the train now.
        a.Server.HandleForTest(a.W.Player, new AdminCommandIntent { Command = "rail_trip", StringArg = "Ostdorf" });
        var trip = Assert.Single(a.Server.RailTripsForTest);
        Assert.Equal(("Ostdorf", "Westdorf"), (trip.Home, trip.Away));
        Assert.Contains(a.W.MessagesToPlayer(), m => m.Contains("Ostdorf", StringComparison.Ordinal) && m.Contains("Westdorf", StringComparison.Ordinal));

        // /guest by day at the base: no far town on the line, so the walk-in comes.
        a.Server.SetLocalDayFractionForTest(0.4, NpcLifeWorld.Cx);
        a.Server.HandleForTest(a.W.Player, new AdminCommandIntent { Command = "summon_guest" });
        var guest = Assert.Single(a.Server.BaseGuestsForTest);
        Assert.False(a.Server.BaseGuestByTrainForTest(guest.NpcId));
        a.Server.HandleForTest(a.W.Player, new AdminCommandIntent { Command = "summon_guest" });
        Assert.Contains(a.W.Transport.Sent.Where(s => s.Conn == a.W.Player.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>(),
            r => r.Reason == "@srv.admin.guest_busy");

        // The bump snapshot's rail part: the line with its stops and places, the train, the trip, the base.
        a.W.TickAt(0.4, 120, until: () => a.Server.NpcPassengersForTest(a.TrainId).Count > 0);
        using var doc = JsonDocument.Parse(a.Server.BumpRailSnapshotJsonForTest(a.PlayerId));
        var root = doc.RootElement;
        var line = root.GetProperty("lines").EnumerateArray().First();
        Assert.Equal(5, line.GetProperty("pylons").GetInt32());
        Assert.True(line.GetProperty("length").GetDouble() > 70);
        var places = line.GetProperty("stops").EnumerateArray().Select(s => s.GetProperty("place").GetString()).ToList();
        Assert.Contains("Westdorf", places);
        Assert.Contains("Ostdorf", places);

        // #3457: both towns' stops lie within 96 blocks of the core — the base's too; and the stop nearest the core is
        // described with its distances.
        string baseName = a.Server.BaseSnapshots.Single(b => b.Id == a.W.BaseId).Name;
        Assert.All(line.GetProperty("stops").EnumerateArray(), s => Assert.Equal(baseName, s.GetProperty("alsoBase").GetString()));
        var train = root.GetProperty("trains").EnumerateArray().Single();
        Assert.Equal(a.TrainId, train.GetProperty("id").GetString());
        Assert.True(train.GetProperty("takesPassengers").GetBoolean());
        Assert.True(train.GetProperty("Autopilot").GetBoolean());
        Assert.Contains(train.GetProperty("npcPassengers").EnumerateArray(), p => p.GetProperty("npc").GetInt32() == trip.NpcId);
        Assert.Equal(trip.NpcId, root.GetProperty("trips").EnumerateArray().Single().GetProperty("npc").GetInt32());
        var nearest = root.GetProperty("nearestBase");
        Assert.Equal(a.W.BaseId, nearest.GetProperty("id").GetInt32());
        Assert.Single(nearest.GetProperty("guests").EnumerateArray());
        Assert.Equal(0, nearest.GetProperty("relays").GetInt32());
        var stop = nearest.GetProperty("nearestStop");
        Assert.InRange(stop.GetProperty("coreDistance").GetDouble(), 25.0, 35.0);
        Assert.Equal(2, stop.GetProperty("places").GetArrayLength()); // the town's and the base's
    }
}
