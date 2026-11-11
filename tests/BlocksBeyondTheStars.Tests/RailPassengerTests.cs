// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3445 — NPC passengers on the monorail (the foundation of Lyxette's "the settlers who visit me should come by train"): an
/// NPC boards only while the train halts at a stop, sits on a free seat of a passenger wagon (never the cab), rides with the
/// wagon tick by tick (the wire carries its frame and offset), gives its seat up to a player (another seat, else the aisle)
/// and steps off at a halt beside the door. A full train takes nobody. Every way a train can go away under a passenger —
/// stowed, its line changed by a broken pylon, its line gone (the train packed away), the shared set-down with nobody near —
/// leaves the NPC on its feet beside the wagon or back home; an unrelated line rebuilt elsewhere does not, and nothing of it
/// is ever saved with the train.
/// </summary>
public sealed class RailPassengerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static float Dist(Vector3f a, Vector3f b)
        => (float)Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>Where a passenger in wagon <paramref name="wagon"/> at <paramref name="local"/> stands in the world now.</summary>
    private static Vector3f Aboard(RailTravelArena a, int wagon, Vector3f local)
    {
        var (pos, yaw) = a.Server.WagonPoseForTest(a.TrainId, wagon)!.Value;
        return RailRules.LocalToWorld(pos, yaw, local);
    }

    [Fact]
    public void AnNpc_BoardsAtAHalt_RidesSeatedWithTheWagon_AndStepsOffAtTheNextHalt()
    {
        using var a = new RailTravelArena(Content.Value, "rail_passenger_ride");
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        var platform = a.Server.RailStopPlatformSpotForTest(a.StopEast);
        Assert.NotNull(platform);
        int v = a.Server.SpawnVillagerForTest("Testville", platform!.Value);

        // A running train takes nobody: boarding waits for a halt at a stop.
        a.W.TickAt(0.4, 1.0);
        Assert.False(a.Halt.Halted);
        Assert.False(a.Server.BoardNpcForTest(v, a.TrainId));

        Assert.Equal(a.StopEast, a.TickUntilHalted());
        Assert.True(a.Server.BoardNpcForTest(v, a.TrainId));
        var ride = a.Server.NpcRideForTest(v);
        Assert.Equal(a.TrainId, ride.TrainId);
        Assert.Equal(RailRules.FrameId(a.TrainId, 1), ride.Frame); // the passenger wagon, never the cab
        Assert.Equal(1, ride.Pose);                                  // seated
        var seat = Assert.Single(a.Server.NpcPassengersForTest(a.TrainId));
        Assert.Equal(1, seat.Wagon);
        Assert.True(seat.Seat >= 0);
        Assert.DoesNotContain(v.ToString(System.Globalization.CultureInfo.InvariantCulture), a.Server.TrainsForTest().Single().Riders);

        // The wire: the frame and the seat offset — the client draws it in the wagon.
        var net = a.Server.NetNpcForTest(v);
        Assert.Equal(ride.Frame, net.FrameId);
        Assert.Equal(seat.Local.X, net.LocalX, 3);
        Assert.Equal(seat.Local.Z, net.LocalZ, 3);
        Assert.Equal(1, net.Pose);

        // It rides: tick by tick its world position is the wagon's seat.
        var start = a.Server.NpcRideForTest(v).Pos;
        for (int i = 0; i < 80; i++)
        {
            a.W.TickAt(0.4, 0.25);
            Assert.True(Dist(Aboard(a, 1, seat.Local), a.Server.NpcRideForTest(v).Pos) < 0.05f, $"tick {i}: the passenger left its seat");
        }

        Assert.True(Dist(start, a.Server.NpcRideForTest(v).Pos) > 5f, "the train never carried the passenger anywhere");

        // The next halt: it steps off beside the door onto real ground, the frame is gone.
        Assert.NotNull(a.TickUntilHalted(180));
        var wagonAt = a.Server.WagonPoseForTest(a.TrainId, 1)!.Value.Pos;
        Assert.True(a.Server.AlightNpcForTest(v));
        ride = a.Server.NpcRideForTest(v);
        Assert.Equal(string.Empty, ride.TrainId);
        Assert.Equal(0, ride.Pose);
        Assert.Empty(a.Server.NpcPassengersForTest(a.TrainId));
        Assert.Equal(string.Empty, a.Server.NetNpcForTest(v).FrameId);
        var feet = ride.Pos.ToBlock();
        Assert.True(a.Server.NpcStandableAtForTest(feet.X, feet.Y, feet.Z), $"set down at {ride.Pos}, not on standable ground");
        float dx = ride.Pos.X - wagonAt.X, dz = ride.Pos.Z - wagonAt.Z;
        Assert.True((dx * dx) + (dz * dz) < 5f * 5f, "it did not step off beside its wagon");
    }

    [Fact]
    public void AnNpcInTheSleeper_LiesInItsBerth()
    {
        // #3473: a berth is a bed — the passenger lies on it (pose 2) with its facing from the foot to the head, as a villager
        // lies in bed, instead of sitting inside the bunk.
        using var a = new RailTravelArena(Content.Value, "rail_passenger_sleeper");
        a.W.Player.State.Inventory.SetSlot(9, new BlocksBeyondTheStars.Shared.State.ItemStack(RailWagonLayout.SleeperItemKey, 1));
        a.PlaceTrain(NpcLifeWorld.Cx - 10, RailWagonLayout.SleeperItemKey);
        var platform = a.Server.RailStopPlatformSpotForTest(a.StopEast)!.Value;
        int v = a.Server.SpawnVillagerForTest("Testville", platform);
        Assert.Equal(new[] { RailRules.CabItemKey, RailWagonLayout.SleeperItemKey }, a.Server.TrainsForTest().Single().Wagons);
        a.W.TickAt(0.4, 1.0);
        Assert.Equal(a.StopEast, a.TickUntilHalted());
        Assert.True(a.Server.BoardNpcForTest(v, a.TrainId));

        var berth = Assert.Single(a.Server.NpcPassengersForTest(a.TrainId));
        Assert.True(berth.Seat >= 0);
        var net = a.Server.NetNpcForTest(v);
        Assert.Equal(2, net.Pose);
        Assert.Equal(RailWagonLayout.BerthHeadYaw(berth.Local), net.LocalYaw, 3);
    }

    [Fact]
    public void PlayersComeFirst_AnNpcGivesItsSeatUp_AndAFullTrainTakesNobody()
    {
        using var a = new RailTravelArena(Content.Value, "rail_passenger_seats");
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        var platform = a.Server.RailStopPlatformSpotForTest(a.StopEast)!.Value;
        int first = a.Server.SpawnVillagerForTest("Testville", platform);
        Assert.Equal(a.StopEast, a.TickUntilHalted());
        Assert.True(a.Server.BoardNpcForTest(first, a.TrainId));
        int taken = Assert.Single(a.Server.NpcPassengersForTest(a.TrainId)).Seat;

        // The player boards exactly that seat: accepted, and the NPC moves to another free seat.
        a.W.Player.State.Position = a.Server.WagonPoseForTest(a.TrainId, 1)!.Value.Pos;
        a.Server.EnterTrainForTest(a.PlayerId, a.TrainId, 1, taken);
        Assert.Equal(RailRules.FrameId(a.TrainId, 1), a.W.Player.State.InTrain);
        Assert.Equal(taken, a.W.Player.State.TrainSeat);
        var moved = Assert.Single(a.Server.NpcPassengersForTest(a.TrainId));
        Assert.NotEqual(taken, moved.Seat);
        Assert.True(moved.Seat >= 0);
        Assert.Equal(1, a.Server.NpcRideForTest(first).Pose);

        // More villagers fill the wagon: once the six seats (one of them the player's) are taken, nobody else gets on.
        int seats = RailRules.SeatOffsets("wagon_seats").Count;
        var others = Enumerable.Range(0, seats).Select(_ => a.Server.SpawnVillagerForTest("Testville", platform)).ToList();
        var boarded = others.Where(id => a.Server.BoardNpcForTest(id, a.TrainId)).ToList();
        Assert.Equal(seats - 2, boarded.Count);
        Assert.Equal(seats - 1, a.Server.NpcPassengersForTest(a.TrainId).Count);

        // The player gets up — the last villager on the platform takes the free seat: the wagon is full of NPCs.
        a.Server.ExitTrainForTest(a.PlayerId);
        Assert.Equal(string.Empty, a.W.Player.State.InTrain);
        Assert.True(a.Server.BoardNpcForTest(others.First(id => !boarded.Contains(id)), a.TrainId));
        Assert.Equal(seats, a.Server.NpcPassengersForTest(a.TrainId).Count);

        // The player sits down on an NPC's seat again: no seat is left, so that NPC stands in the aisle and rides on —
        // nobody is ever pushed off the train.
        var victim = a.Server.NpcPassengersForTest(a.TrainId).First(p => p.Seat >= 0);
        a.W.Player.State.Position = a.Server.WagonPoseForTest(a.TrainId, 1)!.Value.Pos;
        a.Server.EnterTrainForTest(a.PlayerId, a.TrainId, 1, victim.Seat);
        Assert.Equal(victim.Seat, a.W.Player.State.TrainSeat);
        var standing = a.Server.NpcPassengersForTest(a.TrainId).Single(p => p.NpcId == victim.NpcId);
        Assert.Equal(-1, standing.Seat);
        Assert.Equal(0, a.Server.NpcRideForTest(victim.NpcId).Pose);
        Assert.Equal(a.TrainId, a.Server.NpcRideForTest(victim.NpcId).TrainId);
        Assert.Equal(seats, a.Server.NpcPassengersForTest(a.TrainId).Count);
    }

    [Fact]
    public void EveryWayATrainGoesAway_SetsItsPassengersDownSafely_AndNothingOfThemIsSaved()
    {
        using var a = new RailTravelArena(Content.Value, "rail_passenger_aborts");
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        var platform = a.Server.RailStopPlatformSpotForTest(a.StopEast)!.Value;
        int v = a.Server.SpawnVillagerForTest("Testville", platform);
        int npcs = a.Server.NpcCount;
        Assert.Equal(a.StopEast, a.TickUntilHalted());
        string saved = a.Server.TrainSaveRecordForTest();
        Assert.True(a.Server.BoardNpcForTest(v, a.TrainId));

        // RAM only: the train's save record does not change with a passenger aboard.
        Assert.Equal(saved, a.Server.TrainSaveRecordForTest());

        // A line rebuilt somewhere else (a lone pylon placed far off) leaves the passenger aboard.
        a.Pylon(NpcLifeWorld.Cx, NpcLifeWorld.Cz - 30);
        Assert.Equal(a.TrainId, a.Server.NpcRideForTest(v).TrainId);

        // The shared set-down with nobody near: straight home, where nobody sees it.
        a.W.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 2000f, a.W.Feet, NpcLifeWorld.Cz);
        a.Server.SetDownNpcPassengersForTest(a.TrainId);
        Assert.Equal(string.Empty, a.Server.NpcRideForTest(v).TrainId);
        Assert.Equal(a.Server.NpcRestForTest(v), a.Server.NpcRideForTest(v).Pos);
        a.Home();

        // A pylon of the line broken (its east end): the line changed under the train — the passenger is set down beside
        // the wagon, on its feet, and the train stays.
        a.W.TickAt(0.4, 60, until: () => a.Halt.Stop is not null);
        Assert.True(a.Server.BoardNpcForTest(v, a.TrainId));
        a.W.Player.State.Position = new Vector3f(RailTravelArena.EastX + 0.5f, a.W.Feet, RailTravelArena.LineZ - 2.5f);
        a.Server.MineBlock(a.PlayerId, RailTravelArena.EastX, a.W.Feet, RailTravelArena.LineZ);
        Assert.Single(a.Server.TrainsForTest());
        var after = a.Server.NpcRideForTest(v);
        Assert.Equal(string.Empty, after.TrainId);
        Assert.Empty(a.Server.NpcPassengersForTest(a.TrainId));
        var feet = after.Pos.ToBlock();
        Assert.True(a.Server.NpcStandableAtForTest(feet.X, feet.Y, feet.Z), $"set down at {after.Pos}, not on standable ground");

        // Stowed: the owner packs the train up — the passenger steps off, nobody is packed away.
        a.W.TickAt(0.4, 90, until: () => a.Halt.Stop is not null);
        Assert.True(a.Server.BoardNpcForTest(v, a.TrainId));
        a.W.Player.State.Position = a.Server.WagonPoseForTest(a.TrainId, 0)!.Value.Pos;
        a.Server.StowTrainForTest(a.PlayerId, a.TrainId);
        Assert.Empty(a.Server.TrainsForTest());
        after = a.Server.NpcRideForTest(v);
        Assert.Equal(string.Empty, after.TrainId);
        Assert.Equal(0, after.Pose);
        feet = after.Pos.ToBlock();
        Assert.True(a.Server.NpcStandableAtForTest(feet.X, feet.Y, feet.Z), $"set down at {after.Pos}, not on standable ground");

        // The line's first pylon broken while the player stands by: the line is renumbered, so the train's line is gone and
        // the train goes back to the pack. The passenger steps off beside the wagon as it stood on the line it rode — never
        // popped back to its resting spot at the other stop in front of the player (#3447 review).
        a.Home();
        a.PlaceTrain(NpcLifeWorld.Cx - 10, "wagon_seats");
        a.W.TickAt(0.4, 90, until: () => a.Halt.Stop is not null);
        Assert.True(a.Server.BoardNpcForTest(v, a.TrainId));
        var wagon = a.Server.WagonPoseForTest(a.TrainId, 1)!.Value.Pos;
        a.W.Player.State.Position = new Vector3f(RailTravelArena.WestX + 0.5f, a.W.Feet, RailTravelArena.LineZ - 2.5f);
        a.Server.MineBlock(a.PlayerId, RailTravelArena.WestX, a.W.Feet, RailTravelArena.LineZ);
        Assert.Empty(a.Server.TrainsForTest());
        after = a.Server.NpcRideForTest(v);
        Assert.Equal(string.Empty, after.TrainId);
        Assert.True(Dist(after.Pos, a.Server.NpcRestForTest(v)) > 10f, $"popped home to {after.Pos}");
        float wx = after.Pos.X - wagon.X, wz = after.Pos.Z - wagon.Z;
        Assert.True((wx * wx) + (wz * wz) < 5f * 5f, $"set down at {after.Pos}, not beside its wagon at {wagon}");
        feet = after.Pos.ToBlock();
        Assert.True(a.Server.NpcStandableAtForTest(feet.X, feet.Y, feet.Z), $"set down at {after.Pos}, not on standable ground");

        // Never lost, never duplicated.
        Assert.Equal(npcs, a.Server.NpcCount);
        Assert.Single(a.Server.NpcRosterForTest(), n => n.Id == v);
    }
}
