// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC passengers on the monorail (#3445 — the foundation of Lyxette's wish that the villagers who visit him come by train).
/// <list type="bullet">
/// <item><b>Where they sit.</b> An NPC rides in a <b>passenger wagon</b> (every wagon but the cab that has seats) on a free
/// seat — seats are shared with the players (<see cref="NpcSeatHeld"/> looks at both). Players come first: a player who
/// boards a seat an NPC holds sends the NPC to another free seat, else to a standing spot in the aisle
/// (<see cref="MakeSeatForPlayer"/>). A train without a free seat takes nobody — the NPC waits for the next one. The NPC
/// passengers live in <see cref="ServerTrain.NpcPassengers"/>, never in the players' riders (a rider in the cab drives).</item>
/// <item><b>When.</b> Boarding and alighting only while the train is halted at a stop (<see cref="ServerTrain.HaltStop"/>,
/// the autopilot's halt — never the driver's). The NPC steps on and off at the wagon's door: a standable spot beside the
/// wagon on the stop's side (<see cref="NpcDoorSpot"/>), else the stop's platform spot (<see cref="RailStopPlatformSpot"/>).</item>
/// <item><b>While aboard.</b> <see cref="TickTrains"/> places every passenger from its wagon every tick
/// (<see cref="CarryNpcPassengers"/>), seated (pose 1) or standing; the movement, the routine, the route search, the chatter,
/// the guests and the wishes leave it alone (<see cref="ServerNpc.TrainId"/>). The wire carries the frame and the offset
/// (<c>NetNpc.FrameId</c> / <c>LocalX/Y/Z</c> / <c>LocalYaw</c>), so the client draws it in the wagon as it draws it.</item>
/// <item><b>Never lost, never duplicated.</b> A stowed or returned train, a line that was rebuilt into something else, a
/// pylon broken under it: every passenger is set down — beside the wagon where players can see it, else straight home
/// (<see cref="ServerNpc.Rest"/>) where nobody does — and its trip learns it (<see cref="OnNpcPassengerSetDown"/>).
/// Passengers are RAM only: nothing is saved with the train, and a world load regenerates the villagers anyway. No tickets,
/// no fares (Marcel's rule for the rail system).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a stop's platform spot is cached before it is looked for again (a player may build round it).</summary>
    private const double PlatformSpotCacheSeconds = 30.0;

    /// <summary>How far round a stop the platform spot is looked for (blocks).</summary>
    private const int PlatformSearchRadius = 4;

    /// <summary>A platform spot keeps this far (horizontally) from the line's middle — clear of the passing wagons.</summary>
    private const float PlatformLineClearance = 2.2f;

    /// <summary>How far below a wagon's floor a door spot may lie (a line on tall pylons over open ground).</summary>
    private const int DoorDropMax = 6;

    /// <summary>The aisle spots a standing NPC passenger takes (wagon-local Z), the first free one.</summary>
    private static readonly float[] AisleSpots = { 0f, 1.5f, -1.5f, 2.4f, -2.4f };

    private readonly List<PlayerSession> _railNpcTargets = new();

    // ---------------- Seats ----------------

    /// <summary>Whether wagon <paramref name="index"/> carries NPC passengers: any wagon but the cab that has seats.</summary>
    private static bool IsPassengerWagon(ServerTrain t, int index)
        => index >= 0 && index < t.Wagons.Count && t.Wagons[index] != RailRules.CabItemKey
           && RailRules.SeatOffsets(t.Wagons[index]).Count > 0;

    /// <summary>Whether a train takes NPC passengers at all: it runs on its own (the autopilot halts at the stops), the
    /// owner lets villagers ride (#3446; the public train always does), and it has a passenger wagon.</summary>
    private static bool CarriesNpcs(ServerTrain t)
    {
        if (!t.Autopilot || (!RailRules.IsPublic(t.OwnerId) && !t.TakesPassengers))
        {
            return false;
        }

        for (int i = 0; i < t.Wagons.Count; i++)
        {
            if (IsPassengerWagon(t, i))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a player or an NPC (other than <paramref name="exceptNpc"/>) sits on this seat.</summary>
    private static bool NpcSeatHeld(ServerTrain t, int wagon, int seat, int exceptNpc = 0)
    {
        foreach (var r in t.Riders.Values)
        {
            if (r.Wagon == wagon && r.Seat == seat)
            {
                return true;
            }
        }

        foreach (var kv in t.NpcPassengers)
        {
            if (kv.Key != exceptNpc && kv.Value.Wagon == wagon && kv.Value.Seat == seat)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A free seat in a passenger wagon — in <paramref name="preferWagon"/> first, then the others from the
    /// front — or null. <paramref name="reserved"/> counts as taken (the seat a player is boarding right now).</summary>
    private static (int Wagon, int Seat)? FreeNpcSeat(ServerTrain t, int preferWagon = -1, (int Wagon, int Seat)? reserved = null, int exceptNpc = 0)
    {
        foreach (int w in WagonOrder(t, preferWagon))
        {
            if (!IsPassengerWagon(t, w))
            {
                continue;
            }

            int seats = RailRules.SeatOffsets(t.Wagons[w]).Count;
            for (int s = 0; s < seats; s++)
            {
                if (reserved is { } r && r.Wagon == w && r.Seat == s)
                {
                    continue;
                }

                if (!NpcSeatHeld(t, w, s, exceptNpc))
                {
                    return (w, s);
                }
            }
        }

        return null;
    }

    private static IEnumerable<int> WagonOrder(ServerTrain t, int prefer)
    {
        if (prefer >= 0 && prefer < t.Wagons.Count)
        {
            yield return prefer;
        }

        for (int i = 0; i < t.Wagons.Count; i++)
        {
            if (i != prefer)
            {
                yield return i;
            }
        }
    }

    /// <summary>A standing spot in the aisle of a wagon: the first that no other standing NPC passenger takes.</summary>
    private static Vector3f AisleSpot(ServerTrain t, int wagon, int npcId)
    {
        foreach (float z in AisleSpots)
        {
            bool taken = t.NpcPassengers.Any(kv => kv.Key != npcId && kv.Value.Wagon == wagon && kv.Value.Seat < 0
                                                    && Math.Abs(kv.Value.Local.Z - z) < 0.5f);
            if (!taken)
            {
                return new Vector3f(0f, 0f, z);
            }
        }

        return new Vector3f(0f, 0f, AisleSpots[0]);
    }

    /// <summary>The way an NPC passenger faces in its wagon (radians, 0 = toward the cab): seated on a side bench across the
    /// aisle (its back to the wall), standing toward the cab.</summary>
    private static float PassengerLocalYaw(RailRider r)
        => r.Seat < 0 ? 0f : r.Local.X < 0f ? (float)(Math.PI * 0.5) : (float)(-Math.PI * 0.5);

    // ---------------- Boarding and riding ----------------

    /// <summary>
    /// An NPC boards a train halted at <paramref name="stop"/> (null = at whatever stop it halts at): a free seat in a passenger
    /// wagon (the nearest wagon first). False — and nothing changes — when the train does not halt at a stop, does not take
    /// passengers, has no free seat, or the NPC is already aboard.
    /// </summary>
    private bool TryBoardNpc(ServerNpc npc, ServerTrain t, Vector3i? stop = null)
    {
        var rails = Rails;
        if (npc.TrainId.Length > 0 || !t.Halted || t.HaltStop is not { } at || (stop is { } want && at != want)
            || !CarriesNpcs(t) || !rails.Lines.TryGetValue(t.LineId, out var line))
        {
            return false;
        }

        if (FreeNpcSeat(t, NearestPassengerWagon(t, line, npc.Pos)) is not { } seat)
        {
            return false; // full: it waits for the next train
        }

        npc.Goal = null;
        npc.Path = null;
        npc.PathIndex = 0;
        npc.Arrival = NpcArrival.None;
        npc.AttendUntil = 0;
        npc.SeatWait = false;
        var rider = new RailRider { Wagon = seat.Wagon, Seat = seat.Seat, Local = RailRules.SeatOffsets(t.Wagons[seat.Wagon])[seat.Seat] };
        t.NpcPassengers[npc.Id] = rider;
        SyncNpcRide(npc, t, rider);
        PlaceNpcPassenger(t, line, npc, rider);
        BroadcastNpcs();
        return true;
    }

    /// <summary>The passenger wagon nearest a point (−1 when the train has none).</summary>
    private int NearestPassengerWagon(ServerTrain t, RailLine line, Vector3f near)
    {
        int best = -1;
        double bestSq = double.MaxValue;
        for (int i = 0; i < t.Wagons.Count; i++)
        {
            if (!IsPassengerWagon(t, i))
            {
                continue;
            }

            double d = WrapDistSq(near, WagonPose(t, line, i).Pos);
            if (d < bestSq)
            {
                bestSq = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Copies a passenger's seat into the NPC (what the wire carries): the train, the frame, the offset, the facing
    /// and the pose — seated (1) on a seat, standing (0) in the aisle.</summary>
    private static void SyncNpcRide(ServerNpc npc, ServerTrain t, RailRider r)
    {
        npc.TrainId = t.Id;
        npc.RideFrame = RailRules.FrameId(t.Id, r.Wagon);
        npc.RideLocal = r.Local;
        npc.RideYaw = PassengerLocalYaw(r);
        npc.Pose = r.Seat >= 0 ? (byte)1 : (byte)0;
    }

    /// <summary>The NPC is on foot again (the caller places it).</summary>
    private static void ClearNpcRide(ServerNpc npc)
    {
        npc.TrainId = string.Empty;
        npc.RideFrame = string.Empty;
        npc.RideLocal = default;
        npc.RideYaw = 0f;
        npc.Pose = 0;
    }

    /// <summary>A passenger's world position and facing from its wagon (the server owns both while it rides).</summary>
    private void PlaceNpcPassenger(ServerTrain t, RailLine line, ServerNpc npc, RailRider r)
    {
        var (_, yaw) = WagonPose(t, line, Math.Clamp(r.Wagon, 0, Math.Max(0, t.Wagons.Count - 1)));
        npc.Pos = RiderWorld(t, line, r);
        npc.Home = npc.Pos;
        float world = yaw - npc.RideYaw; // the local facing turned into the world: (cos, sin) of the wagon heading minus it
        npc.Facing = (float)Math.Atan2(Math.Cos(world), Math.Sin(world));
        npc.FaceGoal = null;
    }

    /// <summary>Every tick (from <see cref="TickTrains"/>): the train carries its NPC passengers. A passenger whose NPC is
    /// gone (a guest that ended, a resident whose bed went) or no longer rides this train is dropped from the list.</summary>
    private void CarryNpcPassengers(ServerTrain t, RailLine line)
    {
        List<int>? gone = null;
        foreach (var kv in t.NpcPassengers)
        {
            if (NpcWithId(kv.Key) is not { } npc || npc.TrainId != t.Id)
            {
                (gone ??= new List<int>()).Add(kv.Key);
                continue;
            }

            PlaceNpcPassenger(t, line, npc, kv.Value);
        }

        if (gone is not null)
        {
            foreach (int id in gone)
            {
                t.NpcPassengers.Remove(id);
            }
        }
    }

    private ServerNpc? NpcWithId(int id)
    {
        foreach (var n in _npcs)
        {
            if (n.Id == id)
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>Players first (#3445): a player boarding a seat an NPC holds — the NPC moves to another free seat (its own
    /// wagon first), else it stands in the aisle and rides on.</summary>
    private void MakeSeatForPlayer(ServerTrain t, int wagon, int seat)
    {
        if (seat < 0 || !Rails.Lines.TryGetValue(t.LineId, out var line))
        {
            return;
        }

        foreach (var kv in t.NpcPassengers)
        {
            var r = kv.Value;
            if (r.Wagon != wagon || r.Seat != seat || NpcWithId(kv.Key) is not { } npc)
            {
                continue;
            }

            if (FreeNpcSeat(t, wagon, reserved: (wagon, seat), exceptNpc: kv.Key) is { } free)
            {
                r.Wagon = free.Wagon;
                r.Seat = free.Seat;
                r.Local = RailRules.SeatOffsets(t.Wagons[free.Wagon])[free.Seat];
            }
            else
            {
                r.Seat = -1;
                r.Local = AisleSpot(t, wagon, kv.Key);
            }

            SyncNpcRide(npc, t, r);
            PlaceNpcPassenger(t, line, npc, r);
            BroadcastNpcs();
            return; // one NPC per seat
        }
    }

    // ---------------- Getting off ----------------

    /// <summary>
    /// A passenger steps off at a stop: onto the standable spot beside its wagon's door on the stop's side, else onto the
    /// stop's platform spot — out of every player's reach straight onto the platform spot (#3447 review: the door is not
    /// looked for, so no chunk is loaded at a far town; <see cref="RailStopPlatformSpot(Vector3i, out bool)"/> reads only
    /// loaded ones there). Returns true when it stands there; false when no spot fits — then it is sent home (unseen
    /// fallback; a guest copy is left to its caller, which ends the visit) and the caller treats the leg as over.
    /// </summary>
    private bool AlightNpc(ServerNpc npc, Vector3i? stop)
    {
        var rails = Rails;
        var t = rails.Trains.FirstOrDefault(x => x.Id == npc.TrainId);
        Vector3f? spot = null;
        if (t is not null && t.NpcPassengers.TryGetValue(npc.Id, out var r) && rails.Lines.TryGetValue(t.LineId, out var line))
        {
            Vector3f? toward = stop is { } s ? new Vector3f(s.X + 0.5f, s.Y + 0.5f, s.Z + 0.5f) : null;
            var door = InNpcReach(npc.Pos) ? NpcDoorSpot(t, line, r.Wagon, toward) : null;
            spot = door ?? (stop is { } st ? RailStopPlatformSpot(st) : null);
        }

        t?.NpcPassengers.Remove(npc.Id);
        ClearNpcRide(npc);
        if (spot is not { } at)
        {
            if (!_baseGuests.Any(g => g.NpcId == npc.Id))
            {
                SendNpcHome(npc); // a guest copy's resting spot is the base's platform — never "home"
            }

            return false;
        }

        npc.Pos = at;
        npc.Home = at;
        npc.Loco.ModeTimer = 0f;
        BroadcastNpcs();
        return true;
    }

    /// <summary>A train that goes away with passengers aboard (stowed, returned, its line gone or rebuilt): every passenger is
    /// set down (<see cref="SetDownNpcPassenger"/>) — beside the wagon on <paramref name="ranOn"/>, the line they rode (a
    /// rebuild hands the old line in: the new one may lack the train's line or put its arc elsewhere), else on its line.</summary>
    private void SetDownNpcPassengers(ServerTrain t, RailLine? ranOn = null)
    {
        if (t.NpcPassengers.Count == 0)
        {
            return;
        }

        var line = ranOn;
        if (line is null)
        {
            Rails.Lines.TryGetValue(t.LineId, out line);
        }

        foreach (int id in t.NpcPassengers.Keys.ToList())
        {
            if (NpcWithId(id) is { } npc && npc.TrainId == t.Id)
            {
                SetDownNpcPassenger(npc, t, line);
            }

            t.NpcPassengers.Remove(id);
        }
    }

    /// <summary>
    /// Sets one passenger down off a train that cannot carry it on: beside its wagon when a player is near enough to see it
    /// (a standable spot by the door), else — or when nothing fits — straight home where nobody sees it. Out of everybody's
    /// reach with a player at its home (#3447 review: it would pop in there) a traveller stays where it rode, unseen, and its
    /// trip's timed way home brings it in from out of sight. Its trip learns it (<see cref="OnNpcPassengerSetDown"/>). A
    /// guest copy riding home is never sent "home" — its resting spot is the base's platform (<see cref="SetDownTrainGuest"/>).
    /// </summary>
    private void SetDownNpcPassenger(ServerNpc npc, ServerTrain t, RailLine? line)
    {
        Vector3f? spot = null;
        bool reach = InNpcReach(npc.Pos);
        if (line is not null && t.NpcPassengers.TryGetValue(npc.Id, out var r) && reach)
        {
            Vector3f? toward = t.HaltStop is { } s ? new Vector3f(s.X + 0.5f, s.Y + 0.5f, s.Z + 0.5f) : null;
            spot = NpcDoorSpot(t, line, r.Wagon, toward);
        }

        t.NpcPassengers.Remove(npc.Id);
        ClearNpcRide(npc);
        if (_baseGuests.FirstOrDefault(g => g.NpcId == npc.Id) is { } guest)
        {
            SetDownTrainGuest(guest, npc, spot);
            return;
        }

        bool home = false;
        if (spot is { } at)
        {
            npc.Pos = at;
            npc.Home = at;
            npc.Loco.ModeTimer = 0f;
        }
        else if (reach || npc.Trip is null || !SpotWatched(npc.Rest))
        {
            SendNpcHome(npc);
            home = true;
        }

        BroadcastNpcs();
        OnNpcPassengerSetDown(npc, home);
    }

    /// <summary>Takes an NPC off whatever train it rides without placing it (it is leaving the world: a guest that ends).</summary>
    private void DropNpcPassenger(ServerNpc npc)
    {
        if (npc.TrainId.Length == 0)
        {
            return;
        }

        foreach (var t in Rails.Trains)
        {
            t.NpcPassengers.Remove(npc.Id);
        }

        ClearNpcRide(npc);
    }

    /// <summary>The NPC is simply home: at its resting spot, on foot, its routine thinking about the hour again.</summary>
    private static void SendNpcHome(ServerNpc npc)
    {
        npc.Pos = npc.Rest;
        npc.Home = npc.Rest;
        npc.Goal = null;
        npc.Path = null;
        npc.PathIndex = 0;
        npc.Arrival = NpcArrival.None;
        npc.Pose = 0;
        npc.Loco.ModeTimer = 0f;
        npc.PhaseCheckedAt = double.NegativeInfinity;
    }

    /// <summary>While the lines are rebuilt (<see cref="RebuildRailLines"/>), before a train is clamped onto its new line or
    /// packed away: a train whose line <paramref name="now"/> is still exactly the line it ran on, <paramref name="ranOn"/>
    /// (the same pylons in the same order), keeps its passengers; one whose line changed under it (a pylon placed, mined or
    /// relinked into it) or vanished (null) sets them down — beside the wagon as it stood on the line they rode (#3447
    /// review: a vanished line used to send them home in plain view).</summary>
    private void SettleNpcPassengersAfterRebuild(ServerTrain t, RailLine? ranOn, RailLine? now)
    {
        if (t.NpcPassengers.Count == 0
            || (now is not null && ranOn is not null && ranOn.Closed == now.Closed && ranOn.Pylons.SequenceEqual(now.Pylons)))
        {
            return;
        }

        SetDownNpcPassengers(t, ranOn);
    }

    // ---------------- Spots ----------------

    /// <summary>The standable spot beside a wagon's door (its middle, <see cref="RailRules.ExitSide"/> out): on the side of
    /// <paramref name="toward"/> first (the stop), then the other; down to <see cref="DoorDropMax"/> below the floor. Null
    /// when neither side has one.</summary>
    private Vector3f? NpcDoorSpot(ServerTrain t, RailLine line, int wagon, Vector3f? toward)
    {
        var (pos, yaw) = WagonPose(t, line, Math.Clamp(wagon, 0, Math.Max(0, t.Wagons.Count - 1)));
        int first = 1;
        if (toward is { } at)
        {
            first = RailRules.WorldToLocal(pos, yaw, Unwrapped(pos, at)).X >= 0f ? 1 : -1;
        }

        foreach (int side in new[] { first, -first })
        {
            var door = RailRules.LocalToWorld(pos, yaw, new Vector3f(side * RailRules.ExitSide, 0f, 0f));
            if (NpcSpotNear(door, 1) is { } spot)
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>The standable spot nearest a point within <paramref name="radius"/> (square rings), one step above its
    /// cell down to <see cref="DoorDropMax"/> below — the first floor found in each column.</summary>
    private Vector3f? NpcSpotNear(Vector3f at, int radius)
    {
        int cx = (int)Math.Floor(at.X), cy = (int)Math.Floor(at.Y), cz = (int)Math.Floor(at.Z);
        for (int r = 0; r <= radius; r++)
        {
            Vector3f? best = null;
            double bestSq = double.MaxValue;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || ColumnFloor(cx + dx, cy, cz + dz) is not { } spot)
                    {
                        continue;
                    }

                    double d = WrapDistSq(at, spot);
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = spot;
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>The first standable spot in a column from one above <paramref name="y"/> down to
    /// <see cref="DoorDropMax"/> below it.</summary>
    private Vector3f? ColumnFloor(int x, int y, int z)
    {
        for (int fy = y + 1; fy >= y - DoorDropMax; fy--)
        {
            if (StandableSpot(x, fy, z) is { } spot)
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>The platform spot of a stop (<see cref="RailStopPlatformSpot(Vector3i, out bool)"/>), guessed or not.</summary>
    private Vector3f? RailStopPlatformSpot(Vector3i stop) => RailStopPlatformSpot(stop, out _);

    /// <summary>
    /// The platform spot of a stop (#3445): where a passenger waits for the train and steps off when its door has no floor —
    /// the standable spot nearest the stop block (rings up to <see cref="PlatformSearchRadius"/>, one above it to three
    /// below), at least <see cref="PlatformLineClearance"/> from the line's middle so the wagons pass it by. Cached for
    /// <see cref="PlatformSpotCacheSeconds"/>. Null when nothing fits. Out of every player's reach (#3447 review) no chunk is
    /// loaded for it: with its chunks unloaded the last spot found stands (however old), else a guess from the line
    /// (<see cref="GuessPlatformSpot"/>, <paramref name="guessed"/>; never cached).
    /// </summary>
    private Vector3f? RailStopPlatformSpot(Vector3i stop, out bool guessed)
    {
        guessed = false;
        var cache = Rails.PlatformSpots;
        bool known = cache.TryGetValue(stop, out var cached);
        if (known && _uptime - cached.At < PlatformSpotCacheSeconds)
        {
            return cached.Spot;
        }

        if (!InNpcReach(new Vector3f(stop.X + 0.5f, stop.Y, stop.Z + 0.5f)) && !RailAreaLoaded(stop, PlatformSearchRadius, 2, 4))
        {
            guessed = !known;
            return known ? cached.Spot : GuessPlatformSpot(stop);
        }

        var line = Rails.Lines.Values.FirstOrDefault(l => l.Stops.Any(s => s.Cell == stop));
        Vector3f? best = null;
        for (int r = 1; r <= PlatformSearchRadius && best is null; r++)
        {
            double bestSq = double.MaxValue;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || PlatformColumn(stop.X + dx, stop.Y, stop.Z + dz, line) is not { } spot)
                    {
                        continue;
                    }

                    double d = (dx * dx) + (dz * dz) + (0.25 * (spot.Y - stop.Y) * (spot.Y - stop.Y));
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = spot;
                    }
                }
            }
        }

        cache[stop] = (best, _uptime);
        return best;
    }

    private Vector3f? PlatformColumn(int x, int stopY, int z, RailLine? line)
    {
        for (int y = stopY + 1; y >= stopY - 3; y--)
        {
            if (StandableSpot(x, y, z) is not { } spot)
            {
                continue;
            }

            return line is null || HorizontalDistanceToLine(line, spot) >= PlatformLineClearance ? spot : null;
        }

        return null;
    }

    /// <summary>The horizontal distance from a point to the nearest point of a line's spline (both seams the short way).</summary>
    private float HorizontalDistanceToLine(RailLine line, Vector3f at)
    {
        var (arc, _) = line.Spline.Nearest(at);
        var p = line.Spline.PointAt(arc);
        double dx = WorldConstants.WrapDeltaX((double)at.X - p.X, _world.Circumference);
        double dz = WorldConstants.WrapDeltaZ((double)at.Z - p.Z, _world.Circumference);
        return (float)Math.Sqrt((dx * dx) + (dz * dz));
    }

    // ---------------- Who can see it ----------------

    /// <summary>The players around whom NPCs live right now — exactly <see cref="TickNpcs"/>' targets: on the surface, not in
    /// space (an owner aboard still counts while a guest of theirs walks home).</summary>
    private List<PlayerSession> RailNpcTargets()
    {
        _railNpcTargets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if ((!_shipPlaced || !s.State.AboardShip || WatchesGuestLeave(s)) && !InSpace(s.State.PlayerId))
            {
                _railNpcTargets.Add(s);
            }
        }

        return _railNpcTargets;
    }

    /// <summary>Whether a point lies inside a player's area of interest — where NPCs walk, the routine runs and the client
    /// draws them (the streaming radius + two chunks, <see cref="MoveNpcs"/>). Out of it, a trip runs on timers.</summary>
    private bool InNpcReach(Vector3f at)
    {
        var targets = RailNpcTargets();
        if (targets.Count == 0)
        {
            return false;
        }

        float aoi = MaxStreamRadiusBlocks(targets) + (2 * WorldConstants.ChunkSize);
        double aoiSq = (double)aoi * aoi;
        foreach (var s in targets)
        {
            if (WrapDistSq(s.State.Position, at) <= aoiSq)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#3445): an NPC boards a train now (the rules apply: halted at a stop, a free seat).</summary>
    public bool BoardNpcForTest(int npcId, string trainId)
        => NpcWithId(npcId) is { } npc && Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } train && TryBoardNpc(npc, train);

    /// <summary>Test seam (#3445): an NPC passenger steps off at the stop its train halts at; false when it was sent home.</summary>
    public bool AlightNpcForTest(int npcId)
    {
        if (NpcWithId(npcId) is not { } npc || npc.TrainId.Length == 0)
        {
            return false;
        }

        var t = Rails.Trains.FirstOrDefault(x => x.Id == npc.TrainId);
        return AlightNpc(npc, t?.HaltStop);
    }

    /// <summary>Test seam (#3445): a train's NPC passengers (NPC id, wagon, seat −1 = standing, local offset).</summary>
    public IReadOnlyList<(int NpcId, int Wagon, int Seat, Vector3f Local)> NpcPassengersForTest(string trainId)
        => Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } train
            ? train.NpcPassengers.Select(kv => (kv.Key, kv.Value.Wagon, kv.Value.Seat, kv.Value.Local)).ToList()
            : Array.Empty<(int, int, int, Vector3f)>();

    /// <summary>Test seam (#3445): an NPC's ride state (its train "" = on foot, the frame, the pose, where it is).</summary>
    public (string TrainId, string Frame, byte Pose, Vector3f Pos) NpcRideForTest(int npcId)
        => NpcWithId(npcId) is { } n ? (n.TrainId, n.RideFrame, n.Pose, n.Pos) : (string.Empty, string.Empty, (byte)0, default);

    /// <summary>Test seam (#3445): the platform spot of a stop (where passengers wait), or null.</summary>
    public Vector3f? RailStopPlatformSpotForTest(Vector3i stop) => RailStopPlatformSpot(stop);

    /// <summary>Test seam (#3447 review): a stop's cached platform spot is <paramref name="spot"/> (null = nothing fits) for
    /// the next <see cref="PlatformSpotCacheSeconds"/>.</summary>
    public void PinRailPlatformSpotForTest(Vector3i stop, Vector3f? spot) => Rails.PlatformSpots[stop] = (spot, _uptime);

    /// <summary>Test seam (#3447 review): forgets every cached platform spot (the next look searches again).</summary>
    public void ForgetRailPlatformSpotsForTest() => Rails.PlatformSpots.Clear();

    /// <summary>Test seam (#3445): the active world's saved train records (passengers are never part of them).</summary>
    public string TrainSaveRecordForTest()
    {
        SaveRails();
        return _meta.Trains.TryGetValue(_world.LocationId, out string? record) ? record : string.Empty;
    }

    /// <summary>Test seam (#3445): sets every NPC passenger of a train down as an abort does (beside the wagon in reach, home
    /// otherwise).</summary>
    public void SetDownNpcPassengersForTest(string trainId)
    {
        if (Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } train)
        {
            SetDownNpcPassengers(train);
        }
    }

    /// <summary>Test seam (#3445): an NPC's resting spot (where "straight home" puts it).</summary>
    public Vector3f NpcRestForTest(int npcId) => NpcWithId(npcId)?.Rest ?? default;
}
