// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lift (#2266): a 3×3 platform that rides a vertical shaft between stops.
/// <list type="bullet">
/// <item><b>Building.</b> A <c>lift_motor</c> at the bottom; the shaft is the 3×3 column above it. The platform rests on
/// the motor (its cells one above the motor). A <c>lift_stop</c> placed on a landing right beside the shaft (two cells
/// from the motor's column) is a floor: the platform's top comes level with the stop's own cell.</item>
/// <item><b>Signals.</b> A rising edge (or a press) on a stop calls the platform there; on the motor it sends the platform
/// to the next stop up (from the top back down to the bottom). A stop's status light is ON while the platform waits
/// there; the motor's while it cannot move (a block in the shaft).</item>
/// <item><b>Riding.</b> The server owns the platform's height and sends it about five times a second while it moves (the
/// train's pace); the client draws the platform with a collider and carries whoever stands on it by the platform's
/// height change — a vertical ride needs no moving frame. Players above a moving platform get the moving-block fall
/// grace. Creatures and NPCs do not ride.</item>
/// <item><b>Kid rule.</b> A platform never moves down onto a player, an NPC or an animal in the shaft — it waits.</item>
/// <item><b>Snow.</b> Weather snow never settles in a shaft, and a drift that lay there before is cleared for the ride
/// (#3162) — the platform is no block, so the snow pass would land its drift under the platform and block the lift.</item>
/// <item><b>Telling why.</b> A press that cannot be answered says so (#3163): a stop beside no shaft, a motor without stops,
/// a block in the shaft, a platform that already waits there (#3171). A circuit's signal stays silent — there is no one to
/// tell.</item>
/// <item><b>Choosing the floor</b> (#3171, <c>GameServerLiftFloors.cs</c>): a rider, or a player beside one of the lift's
/// stops, sends the platform to any floor; the floors and their names ride along on the lift list.</item>
/// <item><b>Gates</b> (#3174, <c>GameServerLiftGates.cs</c>): the lift gates at a floor's landing open while the platform
/// rests there and close when it leaves — never onto a body.</item>
/// </list>
/// Cost: one small loop per moving lift per tick, one list per 0.2 s while one moves; at rest one pass over the net's
/// cells (the floors) and one over the shaft's ring of cells (the gates) every half second per lift.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One lift: the platform's height (the y of its cells), where it is going, and whether it moves.</summary>
    internal sealed class ServerLift
    {
        public int Id;
        public Vector3i Motor;
        public float PlatformY;
        public float TargetY;
        public bool Moving;
        public HashSet<string> Riders { get; } = new(); // #2258: who rode this trip ("Going up" counts a ride once)

        /// <summary>#3171: the floors as this world's players were last told them — platform heights bottom first, each
        /// floor's name (a stop's label, empty when it has none) and whether a stop stands there — and when they are read
        /// again.</summary>
        public int[] Levels = Array.Empty<int>();
        public string[] LevelNames = Array.Empty<string>();
        public bool[] LevelHasStop = Array.Empty<bool>();
        public double NextFloorCheck = double.NegativeInfinity;

        /// <summary>#3174: when this lift's gates are read next, and the gate cells that wait for a body to step out before
        /// they close.</summary>
        public double NextGateSync = double.NegativeInfinity;
        public HashSet<Vector3i> GatesWaiting { get; } = new();
    }

    private const double LiftBroadcastSeconds = 0.2;

    /// <summary>The lift of a motor cell, created on first use (its height from the row's <c>at=</c>, else resting on the motor).</summary>
    private ServerLift LiftOf(ServerCrystalCell motor)
    {
        var lifts = CrystalNet.Lifts;
        if (!lifts.TryGetValue(motor.Cell, out var lift))
        {
            int bottom = motor.Cell.Y + 1;
            int at = CrystalConfigInt(motor.Config, "at", bottom);
            lift = new ServerLift { Id = motor.Id, Motor = motor.Cell, PlatformY = at, TargetY = at };
            lifts[motor.Cell] = lift;
            CrystalNet.LiftListDirty = true;
        }

        return lift;
    }

    /// <summary>The floors of a lift, as platform heights, bottom first: resting on the motor, then every stop beside the shaft.</summary>
    private List<int> LiftLevels(Vector3i motor)
    {
        var levels = new List<int> { motor.Y + 1 };
        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.LiftStop && !c.Inert && StopBelongsTo(c.Cell, motor))
            {
                int level = c.Cell.Y - 1;
                if (level > motor.Y + 1 && !levels.Contains(level))
                {
                    levels.Add(level);
                }
            }
        }

        levels.Sort();
        return levels;
    }

    /// <summary>A stop stands on a landing right beside the shaft: two cells from the motor's column (the ring around the
    /// 3×3 platform), on the bottom floor or above it, and within the lift's height. #3161: the bottom floor counts — a stop
    /// there calls the platform back down (it used to belong to no lift, and a press on it did nothing).</summary>
    private static bool StopBelongsTo(Vector3i stop, Vector3i motor)
    {
        int ring = CrystalNetRules.LiftPlatformRadius + CrystalNetRules.LiftStopReach;
        int dx = Math.Abs(stop.X - motor.X), dz = Math.Abs(stop.Z - motor.Z);
        return Math.Max(dx, dz) == ring && stop.Y - 1 >= motor.Y + 1 && stop.Y - motor.Y <= CrystalNetRules.LiftMaxHeight;
    }

    /// <summary>The motor a stop belongs to, or null.</summary>
    private ServerCrystalCell? MotorOfStop(Vector3i stop)
        => CrystalNet.Cells.Values.FirstOrDefault(c => c.Kind == CrystalDeviceKind.LiftMotor && !c.Inert && StopBelongsTo(stop, c.Cell));

    /// <summary>A signal (or a press) on a lift stop calls the platform there; on the motor it sends the platform on. A press
    /// (<paramref name="by"/> set) that cannot be answered says why (#3163).</summary>
    private void LiftSignal(ServerCrystalCell c, PlayerSession? by)
    {
        if (c.Kind == CrystalDeviceKind.LiftStop)
        {
            if (MotorOfStop(c.Cell) is { } motor)
            {
                LiftGoTo(motor, c.Cell.Y - 1, by);
            }
            else if (by is not null)
            {
                Send(by, new ServerMessage { Text = "@srv.lift.stop_no_shaft" }); // #3163: how to place it, not silence
            }

            return;
        }

        if (c.Kind != CrystalDeviceKind.LiftMotor || c.Inert)
        {
            return;
        }

        var lift = LiftOf(c);
        var levels = LiftLevels(c.Cell);
        int here = (int)Math.Round(lift.Moving ? lift.TargetY : lift.PlatformY);
        if (levels.Count == 1 && by is not null && !lift.Moving && Math.Abs(lift.PlatformY - levels[0]) < 0.01f)
        {
            Send(by, new ServerMessage { Text = "@srv.lift.no_stops" }); // #3163: a motor alone has nowhere to send it
            return; // (and "it is already here" on top of that would say nothing new)
        }

        int next = levels.FirstOrDefault(l => l > here);
        LiftGoTo(c, next > here ? next : levels[0], by);
    }

    /// <summary>Starts the platform towards a level — when the shaft between here and there is clear; else the motor's light
    /// says it is blocked, and so does a message to the player who pressed (#3163). Weather snow in the way is cleared
    /// first (#3162). A platform that already waits there tells the player who asked (#3171: a press never fails
    /// silently) and opens that floor's gates again.</summary>
    private void LiftGoTo(ServerCrystalCell motor, int level, PlayerSession? by = null)
    {
        var lift = LiftOf(motor);
        int from = (int)Math.Floor(Math.Min(lift.PlatformY, level)), to = (int)Math.Ceiling(Math.Max(lift.PlatformY, level));
        ClearWeatherSnowInShaft(motor.Cell, from, to);
        if (Math.Abs(lift.PlatformY - level) < 0.01f && !lift.Moving)
        {
            if (by is not null)
            {
                Send(by, new ServerMessage { Text = "@srv.lift.already_here" });
            }

            SyncLiftGates(lift); // #3174: a gate that was saved closed there opens now
            return;
        }

        if (BlockedLiftLayer(motor.Cell, from, to, up: level > lift.PlatformY) is int blocked)
        {
            SetCrystalBlocked(motor, true);
            SayLiftBlocked(by, motor.Cell, blocked);
            return;
        }

        SetCrystalBlocked(motor, false);
        lift.TargetY = level;
        lift.Moving = true;
        lift.Riders.Clear();
        CrystalNet.LiftListDirty = true;
        BroadcastToWorld(new SoundFx { SoundId = "lift_motor", X = motor.Cell.X + 0.5f, Y = lift.PlatformY + 0.5f, Z = motor.Cell.Z + 0.5f, Loop = true, SourceId = 100000 + lift.Id });
        UpdateLiftStatuses(motor.Cell);
    }

    /// <summary>The layer between two heights (taken in the ride's direction, so the nearest one comes first) a solid block
    /// stands in, or null when the whole way is clear.</summary>
    private int? BlockedLiftLayer(Vector3i motor, int from, int to, bool up)
    {
        for (int i = 0; i <= to - from; i++)
        {
            int y = up ? from + i : to - i;
            if (!LiftLayerClear(motor, y))
            {
                return y;
            }
        }

        return null;
    }

    /// <summary>#3163: tells the player who pressed where the shaft is blocked — counted from the bottom floor, the height a
    /// builder can see. A circuit's signal (no player) stays silent; the motor's amber light still shows it.</summary>
    private void SayLiftBlocked(PlayerSession? by, Vector3i motor, int layer)
    {
        if (by is null)
        {
            return;
        }

        int above = layer - (motor.Y + 1);
        Send(by, new ServerMessage
        {
            Text = above <= 0 ? "@srv.lift.blocked_bottom" : "@srv.lift.blocked:" + above.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    /// <summary>#3162: weather snow lying in the shaft between two heights is cleared for the platform — only the snow pass's
    /// tracked drifts (a player's own snow blocks still block). Drifts from before the fix may lie there already; new ones
    /// never settle in a shaft (<see cref="InLiftShaft"/>).</summary>
    private void ClearWeatherSnowInShaft(Vector3i motor, int from, int to)
    {
        if (_worlds.Active.WeatherDeposits.Count == 0)
        {
            return;
        }

        int r = CrystalNetRules.LiftPlatformRadius;
        for (int y = from; y <= to; y++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    var cell = new Vector3i(motor.X + dx, y, motor.Z + dz);
                    if (cell != motor)
                    {
                        ClearWeatherSnowAt(cell);
                    }
                }
            }
        }
    }

    /// <summary>#3162: whether a cell lies in a lift's shaft — the 3×3 column above a motor, up to the lift's height. Weather
    /// snow never settles there: the platform is no block, so the snow pass would lay its drift under the platform.</summary>
    private bool InLiftShaft(Vector3i cell)
    {
        var lifts = CrystalNet.Lifts;
        if (lifts.Count == 0)
        {
            return false;
        }

        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        foreach (var motor in lifts.Keys)
        {
            if (InShaftOf(cell, motor) || InShaftOf(canonical, motor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InShaftOf(Vector3i cell, Vector3i motor)
    {
        int r = CrystalNetRules.LiftPlatformRadius;
        return Math.Abs(cell.X - motor.X) <= r && Math.Abs(cell.Z - motor.Z) <= r
            && cell.Y > motor.Y && cell.Y - motor.Y <= CrystalNetRules.LiftMaxHeight;
    }

    /// <summary>Whether the 3×3 layer of the shaft at this height is free of solid blocks (the motor itself excepted).</summary>
    private bool LiftLayerClear(Vector3i motor, int y)
    {
        int r = CrystalNetRules.LiftPlatformRadius;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                var cell = new Vector3i(motor.X + dx, y, motor.Z + dz);
                if (cell == motor)
                {
                    continue;
                }

                var def = _content.BlockById(_world.GetBlock(cell));
                if (def is not null && def.Solid && !_world.GetBlock(cell).IsAir)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>A body (player, NPC, animal) in the 3×3 layer of the shaft at this height — a platform going down waits.</summary>
    private bool LiftLayerOccupied(Vector3i motor, int y)
    {
        int r = CrystalNetRules.LiftPlatformRadius;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if (CellOccupiedByBody(new Vector3i(motor.X + dx, y, motor.Z + dz)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Every tick (under the Crystal Net's tick): moving platforms advance towards their level, the gates follow
    /// (#3174) and the floors are re-read (#3171); the list goes out on start / stop, every 0.2 s while one moves, and when a
    /// floor or its name changed.</summary>
    private void TickLifts(double dt)
    {
        var state = CrystalNet;
        if (state.Lifts.Count == 0)
        {
            return;
        }

        bool anyMoving = false;
        foreach (var lift in state.Lifts.Values)
        {
            if (lift.Moving)
            {
                anyMoving = true;
                StepLift(lift, dt);
            }

            TickLiftGates(lift);     // #3174: open where the platform rests, closed elsewhere — never onto a body
            RefreshLiftFloors(lift); // #3171: the floors and their names on the wire follow the stops
        }

        state.LiftBroadcastIn -= dt;
        if (state.LiftListDirty || (anyMoving && state.LiftBroadcastIn <= 0))
        {
            state.LiftListDirty = false;
            state.LiftBroadcastIn = LiftBroadcastSeconds;
            BroadcastToWorld(LiftMessage());
        }
    }

    /// <summary>One moving platform's tick: it waits while something stands under it on the way down (kid rule), grants its
    /// riders the fall grace, and arrives — the row remembers where it waits, the stops and the gates learn it is there.</summary>
    private void StepLift(ServerLift lift, double dt)
    {
        var state = CrystalNet;
        float step = (float)(CrystalNetRules.LiftSpeed * dt);
        float delta = lift.TargetY - lift.PlatformY;
        if (delta < 0f && LiftLayerOccupied(lift.Motor, (int)Math.Floor(lift.PlatformY - 0.01f)))
        {
            return; // kid rule: something stands under the platform — it waits
        }

        GrantLiftRiderGrace(lift);
        if (Math.Abs(delta) > step)
        {
            lift.PlatformY += Math.Sign(delta) * step;
            return;
        }

        lift.PlatformY = lift.TargetY;
        lift.Moving = false;
        state.LiftListDirty = true;
        if (state.Cells.TryGetValue(lift.Motor, out var motor))
        {
            motor.Config = CrystalConfigWith(motor.Config, "at", ((int)Math.Round(lift.PlatformY)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            SaveCrystalCell(motor);
        }

        BroadcastToWorld(new SoundFx { SoundId = "lift_motor", Stop = true, SourceId = 100000 + lift.Id });
        BroadcastToWorld(new SoundFx { SoundId = "lift_arrive", X = lift.Motor.X + 0.5f, Y = lift.PlatformY + 1.5f, Z = lift.Motor.Z + 0.5f, SourceId = lift.Id });
        UpdateLiftStatuses(lift.Motor);
    }

    /// <summary>Whether a player stands on (or just above) a lift's platform — the box the rider grace covers, measured the
    /// short way round both seams. #3171: such a player may choose the floor from the platform.</summary>
    private bool OnLiftPlatform(Shared.State.PlayerState player, ServerLift lift)
    {
        float top = lift.PlatformY + 1f;
        double r = CrystalNetRules.LiftPlatformRadius + 0.6;
        var p = player.Position;
        double dx = Math.Abs(WorldConstants.WrapDeltaX(p.X - (lift.Motor.X + 0.5), _world.Circumference));
        double dz = Math.Abs(WorldConstants.WrapDeltaZ(p.Z - (lift.Motor.Z + 0.5), _world.Circumference));
        return dx <= r && dz <= r && p.Y >= top - 1.5f && p.Y <= top + 3f;
    }

    /// <summary>Players standing on a moving platform fall without harm (the client carries them; a stutter is no fall).</summary>
    private void GrantLiftRiderGrace(ServerLift lift)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (OnLiftPlatform(s.State, lift))
            {
                s.MovingFallGraceUntil = _uptime + CrystalNetRules.MovingFallGraceSeconds;
                if (lift.Riders.Add(s.State.PlayerId))
                {
                    OnCrystalCircuitEvent(s.State.PlayerId, Shared.Missions.CircuitEvents.LiftRide); // #2258: "Going up"
                }
            }
        }
    }

    /// <summary>Each stop's light: ON while the platform rests at its floor. #3174: the gates follow at once — the floor's
    /// open when it arrives, all of them closed when it leaves.</summary>
    private void UpdateLiftStatuses(Vector3i motor)
    {
        if (!CrystalNet.Lifts.TryGetValue(motor, out var lift))
        {
            return;
        }

        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind == CrystalDeviceKind.LiftStop && StopBelongsTo(c.Cell, motor))
            {
                bool here = !lift.Moving && Math.Abs(lift.PlatformY - (c.Cell.Y - 1)) < 0.01f;
                if (c.Output != here)
                {
                    c.Output = here;
                    CrystalNet.DeviceListDirty = true;
                }
            }
        }

        SyncLiftGates(lift);
    }

    /// <summary>A mined motor takes its lift with it — and (#3174) closes its gates: the shaft has no platform any more. A
    /// gate someone stands in stays open (there is no lift left to try again).</summary>
    private void RemoveLift(Vector3i motor)
    {
        if (CrystalNet.Lifts.Remove(motor, out var lift))
        {
            BroadcastToWorld(new SoundFx { SoundId = "lift_motor", Stop = true, SourceId = 100000 + lift.Id });
            CrystalNet.LiftListDirty = true;
            SyncLiftGates(lift, gone: true);
        }
    }

    private LiftList LiftMessage()
        => new()
        {
            Lifts = CrystalNet.Lifts.Values.Select(l => new NetLift
            {
                Id = l.Id,
                X = l.Motor.X,
                Y = l.Motor.Y,
                Z = l.Motor.Z,
                PlatformY = l.PlatformY,
                TargetY = l.TargetY,
                Speed = CrystalNetRules.LiftSpeed,
                Moving = l.Moving,
                Levels = l.Levels,             // #3171: the floor picker's floors …
                LevelNames = l.LevelNames,     // … their names (#3172: the stops' labels) …
                LevelHasStop = l.LevelHasStop, // … and which of them a stop can name
            }).ToArray(),
        };

    /// <summary>Test seam: a lift's platform height (the y of its cells), or null when no lift stands on that motor.</summary>
    public float? LiftPlatformYForTest(Vector3i motor) => CrystalNet.Lifts.TryGetValue(motor, out var l) ? l.PlatformY : null;
}
