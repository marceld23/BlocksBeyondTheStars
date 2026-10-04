// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

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
/// </list>
/// Cost: one small loop per moving lift per tick, one list per 0.2 s while one moves; nothing for a lift at rest.
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
    /// 3×3 platform), above the motor and within the lift's height.</summary>
    private static bool StopBelongsTo(Vector3i stop, Vector3i motor)
    {
        int ring = CrystalNetRules.LiftPlatformRadius + CrystalNetRules.LiftStopReach;
        int dx = Math.Abs(stop.X - motor.X), dz = Math.Abs(stop.Z - motor.Z);
        return Math.Max(dx, dz) == ring && stop.Y - 1 > motor.Y + 1 && stop.Y - motor.Y <= CrystalNetRules.LiftMaxHeight;
    }

    /// <summary>The motor a stop belongs to, or null.</summary>
    private ServerCrystalCell? MotorOfStop(Vector3i stop)
        => CrystalNet.Cells.Values.FirstOrDefault(c => c.Kind == CrystalDeviceKind.LiftMotor && !c.Inert && StopBelongsTo(stop, c.Cell));

    /// <summary>A signal (or a press) on a lift stop calls the platform there; on the motor it sends the platform on.</summary>
    private void LiftSignal(ServerCrystalCell c)
    {
        if (c.Kind == CrystalDeviceKind.LiftStop)
        {
            if (MotorOfStop(c.Cell) is { } motor)
            {
                LiftGoTo(motor, c.Cell.Y - 1);
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
        int next = levels.FirstOrDefault(l => l > here);
        LiftGoTo(c, next > here ? next : levels[0]);
    }

    /// <summary>Starts the platform towards a level — when the shaft between here and there is clear; else the motor's light
    /// says it is blocked.</summary>
    private void LiftGoTo(ServerCrystalCell motor, int level)
    {
        var lift = LiftOf(motor);
        if (Math.Abs(lift.PlatformY - level) < 0.01f && !lift.Moving)
        {
            return;
        }

        int from = (int)Math.Floor(Math.Min(lift.PlatformY, level)), to = (int)Math.Ceiling(Math.Max(lift.PlatformY, level));
        for (int y = from; y <= to; y++)
        {
            if (!LiftLayerClear(motor.Cell, y))
            {
                SetCrystalBlocked(motor, true);
                return;
            }
        }

        SetCrystalBlocked(motor, false);
        lift.TargetY = level;
        lift.Moving = true;
        lift.Riders.Clear();
        CrystalNet.LiftListDirty = true;
        BroadcastToWorld(new SoundFx { SoundId = "lift_motor", X = motor.Cell.X + 0.5f, Y = lift.PlatformY + 0.5f, Z = motor.Cell.Z + 0.5f, Loop = true, SourceId = 100000 + lift.Id });
        UpdateLiftStatuses(motor.Cell);
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

    /// <summary>Every tick (under the Crystal Net's tick): moving platforms advance towards their level; the list goes out on
    /// start / stop and every 0.2 s while one moves.</summary>
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
            if (!lift.Moving)
            {
                continue;
            }

            anyMoving = true;
            float step = (float)(CrystalNetRules.LiftSpeed * dt);
            float delta = lift.TargetY - lift.PlatformY;
            if (delta < 0f && LiftLayerOccupied(lift.Motor, (int)Math.Floor(lift.PlatformY - 0.01f)))
            {
                continue; // kid rule: something stands under the platform — it waits
            }

            GrantLiftRiderGrace(lift);
            if (Math.Abs(delta) <= step)
            {
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
            else
            {
                lift.PlatformY += Math.Sign(delta) * step;
            }
        }

        state.LiftBroadcastIn -= dt;
        if (state.LiftListDirty || (anyMoving && state.LiftBroadcastIn <= 0))
        {
            state.LiftListDirty = false;
            state.LiftBroadcastIn = LiftBroadcastSeconds;
            BroadcastToWorld(LiftMessage());
        }
    }

    /// <summary>Players standing on a moving platform fall without harm (the client carries them; a stutter is no fall).</summary>
    private void GrantLiftRiderGrace(ServerLift lift)
    {
        float top = lift.PlatformY + 1f;
        float r = CrystalNetRules.LiftPlatformRadius + 0.6f;
        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State.Position;
            if (Math.Abs(p.X - (lift.Motor.X + 0.5f)) <= r && Math.Abs(p.Z - (lift.Motor.Z + 0.5f)) <= r && p.Y >= top - 1.5f && p.Y <= top + 3f)
            {
                s.MovingFallGraceUntil = _uptime + CrystalNetRules.MovingFallGraceSeconds;
                if (lift.Riders.Add(s.State.PlayerId))
                {
                    OnCrystalCircuitEvent(s.State.PlayerId, Shared.Missions.CircuitEvents.LiftRide); // #2258: "Going up"
                }
            }
        }
    }

    /// <summary>Each stop's light: ON while the platform rests at its floor.</summary>
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
    }

    /// <summary>A mined motor takes its lift with it.</summary>
    private void RemoveLift(Vector3i motor)
    {
        if (CrystalNet.Lifts.Remove(motor, out var lift))
        {
            BroadcastToWorld(new SoundFx { SoundId = "lift_motor", Stop = true, SourceId = 100000 + lift.Id });
            CrystalNet.LiftListDirty = true;
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
            }).ToArray(),
        };

    /// <summary>Test seam: a lift's platform height (the y of its cells), or null when no lift stands on that motor.</summary>
    public float? LiftPlatformYForTest(Vector3i motor) => CrystalNet.Lifts.TryGetValue(motor, out var l) ? l.PlatformY : null;
}
