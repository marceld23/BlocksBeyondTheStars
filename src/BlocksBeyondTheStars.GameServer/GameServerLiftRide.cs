// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3204 — the lift's ride up to 256 cells: a space elevator.
/// <list type="bullet">
/// <item><b>Speed curve.</b> A short ride keeps the old 3 cells per second; a long one cruises at the distance over 12 s, up
/// to 12 cells per second, easing in and out (<see cref="LiftMotion"/>), and never faster than the kid rule's one layer per
/// tick at the configured tick rate. The lift list carries the ride's average speed until the next list
/// (<see cref="LiftWireSpeed"/>), so the client's linear easing (<c>LiftView</c>) lands where the next list says — no
/// client change; 0 while the platform waits for a body, so the clients hold it.</item>
/// <item><b>The shaft, read on the way.</b> <c>LiftGoTo</c> reads only the first <see cref="LiftStartCheckLayers"/> layers
/// (a blocked start still answers at once). Every tick the platform reads the layers its step enters and the
/// <see cref="LiftLookaheadLayers"/> beyond them — a few layers, at most about 48 block reads — so a ride to the top never
/// loads the whole shaft's chunks in one tick, and a block put into the shaft during the ride is seen. Going up the
/// rider's room counts as part of the platform (<see cref="LiftRiderHeadroom"/>: the client carries a rider without
/// collision, so the head must never be driven into a block). A block ahead shortens the ride so platform and rider stop
/// clear of it; one put right in front of the platform stops it at once. Either way the motor's light turns amber and
/// the player who called it is told where (<c>srv.lift.blocked</c>).</item>
/// <item><b>Kid rule.</b> On the way down every layer the step sweeps is read for a body (not only the next one), so a slow
/// tick cannot carry the platform past someone; it waits.</item>
/// <item><b>Above the air.</b> Stops above the planet's atmosphere line work like any other. A ride that starts below the
/// line and ends above it tells its riders once that the suit now supplies the air (<c>srv.lift.space_elevator</c>) — not on
/// a body without air (an asteroid, an airless moon), and not on rides between two floors that are both above the line.
/// Stepping off up there is the planet's usual zero-g float.</item>
/// <item><b>Riders.</b> The rider box widens with the speed (<see cref="LiftRiderLag"/>): the position a rider reports
/// trails the server's platform by the round trip, and the fall grace must not slip for that.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3204: how many layers of the way <c>LiftGoTo</c> reads before the platform starts (the rest is read on the
    /// way). A blocked layer this close answers the press at once.</summary>
    private const int LiftStartCheckLayers = 8;

    /// <summary>#3204: the most layers one tick may carry a platform (a slow tick is cut short — the next list puts the
    /// clients right). Bounds the shaft reads of one tick.</summary>
    private const float LiftMaxStepLayers = 2f;

    /// <summary>#3204: how far behind the server's platform a rider's reported position may trail (a round trip and a move
    /// report), as time at the ride's speed above the short-ride pace.</summary>
    private const float LiftRiderLagSeconds = 0.4f;

    /// <summary>#3204 (review): the rider's room above the platform, in layers. A rider's feet stand on the platform's top
    /// (one above its cell) and the head reaches 1.8 above the feet — so a platform at height y carries its rider through
    /// ceil(y)+1 and ceil(y)+2. Going up these layers move ahead of the platform: a block there must stop the ride before
    /// the rider's head reaches it, not when the platform itself does (the client moves a rider with the platform without
    /// collision).</summary>
    private const int LiftRiderHeadroom = 2;

    /// <summary>#3204 (review): how many layers beyond what the step enters each tick reads ahead. A block found there
    /// shortens the ride (<see cref="ServerLift.TargetY"/>) before the platform gets close, so the clients — which ease the
    /// platform towards the target and never past it — slow it at the same height instead of running on into the block for
    /// the latency of one list.</summary>
    private const int LiftLookaheadLayers = 2;

    /// <summary>Sets a platform off towards a level: its cruise speed from the distance, a fresh ease-in (or the speed it
    /// already has when the new level lies the same way), and who asked.</summary>
    private void StartLiftRide(ServerLift lift, int level, PlayerSession? by)
    {
        float distance = Math.Abs(level - lift.PlatformY);
        bool sameWay = lift.Moving && lift.Speed > 0f && Math.Sign(level - lift.PlatformY) == Math.Sign(lift.TargetY - lift.PlatformY);
        float carried = sameWay ? lift.Speed : 0f;
        lift.Cruise = Math.Max(LiftMotion.CruiseSpeed(distance, _config.TickRate), Math.Min(carried, LiftMotion.MaxSpeed));
        lift.Speed = sameWay ? carried : CrystalNetRules.LiftSpeed;
        lift.StartY = lift.PlatformY;
        lift.CalledBy = by?.State.PlayerId;
        lift.BlockedAt = null;
        lift.TargetY = level;
        lift.Moving = true;
        lift.Riders.Clear();
        CrystalNet.LiftListDirty = true;
    }

    /// <summary>One moving platform's tick: it waits while something stands in the way down (kid rule), grants its riders the
    /// fall grace, follows the speed curve, reads the layers it (and, going up, its rider) enters — a block there stops it
    /// short — and the layers just ahead — a block there shortens the ride — and arrives: the row remembers where it waits,
    /// the stops and the gates learn it is there.</summary>
    private void StepLift(ServerLift lift, double dt)
    {
        float delta = lift.TargetY - lift.PlatformY;
        bool up = delta > 0f;
        float remaining = Math.Abs(delta);
        float speed = LiftMotion.NextSpeed(lift.Speed, remaining, lift.Cruise, dt);
        float step = Math.Min(speed * (float)dt, LiftMaxStepLayers);
        float next = remaining > step ? lift.PlatformY + (Math.Sign(delta) * step) : lift.TargetY;
        if (delta < 0f && LiftWayDownOccupied(lift.Motor, lift.PlatformY, next))
        {
            if (lift.Speed > 0f)
            {
                lift.Speed = 0f; // kid rule: something stands under the platform — it waits, and the clients hold it there
                CrystalNet.LiftListDirty = true;
            }

            return;
        }

        if (lift.Speed <= 0f)
        {
            CrystalNet.LiftListDirty = true; // the way is free again: the clients ease it on
        }

        GrantLiftRiderGrace(lift);
        if (BlockedLayerEntered(lift.Motor, lift.PlatformY, next, up) is int blocked)
        {
            StopLiftShort(lift, blocked, up); // put into the shaft right in front of it: stop now
            return;
        }

        ShortenRideBeforeBlock(lift, next, up);
        next = up ? Math.Min(next, lift.TargetY) : Math.Max(next, lift.TargetY);
        lift.Speed = speed;
        if (next != lift.TargetY)
        {
            lift.PlatformY = next;
            return;
        }

        lift.PlatformY = lift.TargetY;
        EndLiftRide(lift);
    }

    /// <summary>Kid rule: a body in any layer the platform's bottom sweeps on its way from <paramref name="from"/> down to
    /// <paramref name="to"/> (the layer right under it included).</summary>
    private bool LiftWayDownOccupied(Vector3i motor, float from, float to)
    {
        for (int y = (int)Math.Floor(from - 0.01f); y >= (int)Math.Floor(to); y--)
        {
            if (LiftLayerOccupied(motor, y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The leading layer of what a platform at height <paramref name="y"/> moves through the shaft: going up the top
    /// of its rider's room (the platform fills floor(y) and, between two layers, ceil(y); the rider ceil(y)+1 and
    /// ceil(y)+2), going down the platform's own bottom layer.</summary>
    private static int LiftLeadLayer(float y, bool up) => up ? (int)Math.Ceiling(y) + LiftRiderHeadroom : (int)Math.Floor(y);

    /// <summary>The platform height that keeps the platform and its rider clear of a blocked layer: going up the rider's head
    /// stays below it (the platform three under it), going down the platform rests on it.</summary>
    private static int LiftStopBefore(int blocked, bool up) => up ? blocked - 1 - LiftRiderHeadroom : blocked + 1;

    /// <summary>The first layer between <paramref name="first"/> and <paramref name="last"/>, taken in the ride's direction,
    /// a solid block stands in — or null (also when the range is empty).</summary>
    private int? FirstBlockedLiftLayer(Vector3i motor, int first, int last, bool up)
    {
        for (int y = first; up ? y <= last : y >= last; y += up ? 1 : -1)
        {
            if (!LiftLayerClear(motor, y))
            {
                return y;
            }
        }

        return null;
    }

    /// <summary>The first layer (nearest first) a step from <paramref name="from"/> to <paramref name="to"/> moves the platform
    /// or its rider into that a solid block stands in, or null: going up <c>lead(from)+1 … lead(to)</c> (the rider's room
    /// included), going down <c>floor(from)-1 … floor(to)</c>.</summary>
    private int? BlockedLayerEntered(Vector3i motor, float from, float to, bool up)
        => FirstBlockedLiftLayer(motor, LiftLeadLayer(from, up) + (up ? 1 : -1), LiftLeadLayer(to, up), up);

    /// <summary>Reads the <see cref="LiftLookaheadLayers"/> layers beyond what this step enters (never past what the target
    /// itself needs: going up the target floor's rider room). A block there ends the ride where the rider stays clear of it —
    /// the target moves there, and the arrival turns the motor's light amber and tells the caller
    /// (<see cref="ServerLift.BlockedAt"/>). Over a ride the window advances layer by layer, so every layer is read once.</summary>
    private void ShortenRideBeforeBlock(ServerLift lift, float to, bool up)
    {
        int ahead = up ? LiftLookaheadLayers : -LiftLookaheadLayers;
        int limit = LiftLeadLayer(lift.TargetY, up);
        int first = LiftLeadLayer(lift.PlatformY, up) + (up ? 1 : -1) + ahead;
        int last = up ? Math.Min(LiftLeadLayer(to, up) + ahead, limit) : Math.Max(LiftLeadLayer(to, up) + ahead, limit);
        if (FirstBlockedLiftLayer(lift.Motor, first, last, up) is int blocked)
        {
            lift.TargetY = LiftStopBefore(blocked, up);
            lift.BlockedAt = blocked;
            CrystalNet.LiftListDirty = true; // the clients ease it to the new end, never past it
        }
    }

    /// <summary>A block put in the way right in front of the platform: it stops at once where it and its rider stay clear of
    /// the block (which it reaches this tick at the latest).</summary>
    private void StopLiftShort(ServerLift lift, int blocked, bool up)
    {
        int stopAt = LiftStopBefore(blocked, up);
        lift.PlatformY = stopAt;
        lift.TargetY = stopAt;
        lift.BlockedAt = blocked;
        EndLiftRide(lift);
    }

    /// <summary>The end of a ride (arrived, or stopped short): the row remembers where the platform waits, the motor's hum
    /// stops, the stops and the gates learn it is there, and a ride that left the air says so. A ride a block cut short turns
    /// the motor's light amber and tells the player who called it where the shaft is blocked.</summary>
    private void EndLiftRide(ServerLift lift)
    {
        var state = CrystalNet;
        lift.Moving = false;
        state.LiftListDirty = true;
        if (lift.BlockedAt is int blocked)
        {
            if (state.Cells.TryGetValue(lift.Motor, out var blockedMotor))
            {
                SetCrystalBlocked(blockedMotor, true);
            }

            if (lift.CalledBy is { } caller && FindSessionByPlayerId(caller) is { } session)
            {
                SayLiftBlocked(session, lift.Motor, blocked);
            }

            lift.BlockedAt = null;
        }

        if (state.Cells.TryGetValue(lift.Motor, out var motor))
        {
            motor.Config = CrystalConfigWith(motor.Config, "at", ((int)Math.Round(lift.PlatformY)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            SaveCrystalCell(motor);
        }

        BroadcastToWorld(new SoundFx { SoundId = "lift_motor", Stop = true, SourceId = 100000 + lift.Id });
        BroadcastToWorld(new SoundFx { SoundId = "lift_arrive", X = lift.Motor.X + 0.5f, Y = lift.PlatformY + 1.5f, Z = lift.Motor.Z + 0.5f, SourceId = lift.Id });
        UpdateLiftStatuses(lift.Motor);
        TellLiftRidersAboveAir(lift);
        lift.Speed = 0f;
        lift.CalledBy = null;
    }

    /// <summary>#3204: the space elevator's one line — a ride that started below the atmosphere line and ended above it tells
    /// each of its riders once that the suit supplies the air up here. A body without air has nothing to leave.</summary>
    private void TellLiftRidersAboveAir(ServerLift lift)
    {
        double line = _atmosphereHeight;
        if (line <= 0 || lift.Riders.Count == 0 || lift.StartY + 1f > line || lift.PlatformY + 1f <= line || !AtmospherePresent)
        {
            return;
        }

        foreach (var s in JoinedInActiveWorld())
        {
            if (lift.Riders.Contains(s.State.PlayerId))
            {
                Send(s, new ServerMessage { Text = "@srv.lift.space_elevator" });
            }
        }
    }

    /// <summary>#3204: what the lift list says the platform's speed is — the ride's average until the next list, so the
    /// client's linear easing lands where the next list puts it; 0 while it waits for a body (the clients hold it).</summary>
    private static float LiftWireSpeed(ServerLift lift)
    {
        if (!lift.Moving)
        {
            return CrystalNetRules.LiftSpeed;
        }

        return lift.Speed <= 0f ? 0f
            : LiftMotion.AverageSpeed(lift.Speed, Math.Abs(lift.TargetY - lift.PlatformY), lift.Cruise, LiftBroadcastSeconds);
    }

    /// <summary>#3204: how far (in cells) a rider's reported position may trail a moving platform — nothing at the short-ride
    /// pace (the old box covered that), more the faster it rides.</summary>
    private static float LiftRiderLag(ServerLift lift)
        => lift.Moving ? Math.Max(0f, lift.Speed - CrystalNetRules.LiftSpeed) * LiftRiderLagSeconds : 0f;

    /// <summary>Test seam (#3204): a lift's speed now (cells per second; 0 while it rests or waits), or null without a lift.</summary>
    public float? LiftSpeedForTest(Vector3i motor) => CrystalNet.Lifts.TryGetValue(motor, out var l) ? (l.Moving ? l.Speed : 0f) : null;

    /// <summary>Test seam (#3204): the speed the lift list would carry for this lift now, or null without a lift.</summary>
    public float? LiftWireSpeedForTest(Vector3i motor) => CrystalNet.Lifts.TryGetValue(motor, out var l) ? LiftWireSpeed(l) : null;
}
