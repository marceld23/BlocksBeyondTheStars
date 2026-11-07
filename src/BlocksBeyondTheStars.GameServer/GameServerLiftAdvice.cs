// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lift's build advice (#3300–#3302). The platform rests right on top of the motor, so the bottom floor's top lies two
/// cells above the motor's own cell, and a stop belongs to the lift only from there up (<c>StopBelongsTo</c>). A builder
/// cannot see that rule, and the natural build — the motor set on the ground — leaves the platform out of reach and every
/// stop on the ground beside it belonging to no lift. The server says so where the build goes wrong:
/// <list type="bullet">
/// <item><b>A motor above the ground</b> (#3302): placing a motor reads the ring around its shaft, where the stops go. When
/// the floor a builder stands on there lies one or two cells below the resting platform's top, the builder hears how high
/// the platform will rest and how to sink the motor (<c>srv.lift.motor_step</c> / <c>srv.lift.motor_high</c>). A level
/// landing (the motor in the floor of a 3×3 pit), or no ground near at all (a lift built in the air), says nothing.</item>
/// <item><b>A stop too low</b> (#3300): a stop in a motor's ring, but up to three cells below the bottom floor's height,
/// says so when it is placed and when it is pressed (<c>srv.lift.stop_too_low</c>) — "belongs to no lift: two blocks from
/// the middle of the motor" pointed at a rule the stop already kept.</item>
/// <item><b>Only the bottom floor</b> (#3301): a press on the motor of a lift whose one floor already has its stop asks for
/// a stop higher up (<c>srv.lift.only_bottom_floor</c>) instead of "this lift has no stops yet".</item>
/// </list>
/// Lifts never run aboard a ship, so nothing here speaks there. Cost: a placed motor reads the 16 ring columns, at most
/// five cells each; a placed or pressed stop makes one pass over the net's cells.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How high a stop stands above its motor at the least: the bottom floor's stop, level with the resting
    /// platform's top (the rule of <c>StopBelongsTo</c>: <c>stop.Y - 1 &gt;= motor.Y + 1</c>).</summary>
    private const int LiftLowestStopRise = 2;

    /// <summary>#3300: how many cells below <see cref="LiftLowestStopRise"/> a stop in a motor's ring still reads as that
    /// lift's stop set too low (on the ground beside a motor set on the ground, level with the motor, …) rather than as a
    /// stop beside no lift.</summary>
    private const int LiftStopTooLowReach = 3;

    /// <summary>#3302: the deepest the ground beside the shaft is looked for below the resting platform's top. Two is the
    /// motor set on the ground; deeper, the cells around the motor are no ground of its floor any more.</summary>
    private const int LiftMaxBottomStep = 2;

    /// <summary>A lift motor or stop this player just placed (#3300, #3302): tell them at once when it will not work the way
    /// it stands.</summary>
    private void OnLiftPartPlaced(PlayerSession session, ServerCrystalCell cell)
    {
        if (cell.Inert || _crystalFrame is not null)
        {
            return;
        }

        if (cell.Kind == CrystalDeviceKind.LiftMotor)
        {
            int step = LiftBottomStep(cell.Cell);
            if (step > 0)
            {
                Send(session, new ServerMessage { Text = step == 1 ? "@srv.lift.motor_step" : "@srv.lift.motor_high" });
            }
        }
        else if (cell.Kind == CrystalDeviceKind.LiftStop && MotorOfStop(cell.Cell) is null && MotorBelowStop(cell.Cell) is not null)
        {
            Send(session, new ServerMessage { Text = "@srv.lift.stop_too_low" });
        }
    }

    /// <summary>#3302: how many cells the resting platform's top stands above the ground beside the shaft — 0 for a level
    /// landing in the stop ring (or no ground near to tell), else the drop to the highest floor found there.</summary>
    private int LiftBottomStep(Vector3i motor)
    {
        for (int drop = 0; drop <= LiftMaxBottomStep; drop++)
        {
            if (LiftRingHasFloor(motor, motor.Y + 1 - drop))
            {
                return drop;
            }
        }

        return 0;
    }

    /// <summary>Whether a builder can stand somewhere in the stop ring with their feet on a block at height
    /// <paramref name="y"/>: a solid block there with two free cells above it (a wall beside the shaft is no floor).</summary>
    private bool LiftRingHasFloor(Vector3i motor, int y)
    {
        int ring = CrystalNetRules.LiftPlatformRadius + CrystalNetRules.LiftStopReach;
        for (int dx = -ring; dx <= ring; dx++)
        {
            for (int dz = -ring; dz <= ring; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring)
                {
                    continue;
                }

                int x = motor.X + dx, z = motor.Z + dz;
                if (LiftSolidAt(new Vector3i(x, y, z)) && !LiftSolidAt(new Vector3i(x, y + 1, z)) && !LiftSolidAt(new Vector3i(x, y + 2, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool LiftSolidAt(Vector3i cell)
    {
        var id = _world.GetBlock(cell);
        return !id.IsAir && _content.BlockById(id) is { Solid: true };
    }

    /// <summary>#3300: the motor in whose ring this stop stands, set too low to belong to it (see
    /// <see cref="LiftStopTooLowReach"/>), or null.</summary>
    private ServerCrystalCell? MotorBelowStop(Vector3i stop)
        => CrystalNet.Cells.Values.FirstOrDefault(c => c.Kind == CrystalDeviceKind.LiftMotor && !c.Inert && StopTooLowFor(stop, c.Cell));

    private static bool StopTooLowFor(Vector3i stop, Vector3i motor)
    {
        int ring = CrystalNetRules.LiftPlatformRadius + CrystalNetRules.LiftStopReach;
        int rise = stop.Y - motor.Y;
        return Math.Max(Math.Abs(stop.X - motor.X), Math.Abs(stop.Z - motor.Z)) == ring
               && rise < LiftLowestStopRise && rise >= LiftLowestStopRise - LiftStopTooLowReach;
    }

    /// <summary>#3300: what a press on a stop that belongs to no lift answers — "too low" in a motor's ring, else where a
    /// stop goes (with the shaft's height).</summary>
    private string LiftStopWithoutLiftText(Vector3i stop)
        => MotorBelowStop(stop) is not null ? "@srv.lift.stop_too_low" : LiftStopNoShaftText;

    /// <summary>#3301: what a press on a motor answers when its lift has no floor but the bottom one — a stop already stands
    /// there (ask for one higher up) or none does yet.</summary>
    private string LiftOnlyFloorText(Vector3i motor)
        => CrystalNet.Cells.Values.Any(c => c.Kind == CrystalDeviceKind.LiftStop && !c.Inert
                                            && c.Cell.Y - motor.Y == LiftLowestStopRise && StopBelongsTo(c.Cell, motor))
            ? "@srv.lift.only_bottom_floor"
            : "@srv.lift.no_stops";
}
