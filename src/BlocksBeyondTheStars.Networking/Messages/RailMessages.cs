// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>One rail line of the monorail (#2113): the pylon tops in line order (the client and the server build the
/// same <c>RailSpline</c> from them), whether it closes into a loop, and the arcs of its stops.</summary>
public sealed class NetRailLine
{
    public int Id { get; set; }

    /// <summary>The line's control points as x, y, z triples (world coordinates; the spline unwraps them along X).</summary>
    public float[] Points { get; set; } = System.Array.Empty<float>();

    public bool Closed { get; set; }

    /// <summary>The arc positions of the line's stops (a <c>rail_stop</c> block within reach of the line).</summary>
    public float[] StopArcs { get; set; } = System.Array.Empty<float>();
}

/// <summary>Every rail line of the current world (server → client, on join and on every change).</summary>
public sealed class RailList
{
    public NetRailLine[] Lines { get; set; } = System.Array.Empty<NetRailLine>();
}

/// <summary>A train on a line (server → client): its arc position, speed setting, direction, state and wagons; the
/// client reads the pose of every wagon from the shared spline and extrapolates between updates.</summary>
public sealed class NetTrain
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public int LineId { get; set; }

    /// <summary>The cab's arc position on the line.</summary>
    public float Arc { get; set; }

    /// <summary>The speed setting 1..3 — since #3444 its top speed (see <c>RailRules.SpeedTable</c>); the speed it runs at
    /// right now is <see cref="Velocity"/>.</summary>
    public int Speed { get; set; } = 1;

    /// <summary>The way the train runs: +1 along increasing arc, −1 against it.</summary>
    public int Direction { get; set; } = 1;

    /// <summary>The way the cab points (#2741), fixed for the train's life: the wagons hang behind the cab and face this
    /// way, whichever way the train runs (<c>RailRules.WagonArc</c>). Contractless-additive: 0 from an older server, whose
    /// wagons followed <see cref="Direction"/> — <c>RailRules.PoseFacing</c> reads it as the direction then.</summary>
    public int Facing { get; set; }

    public bool Autopilot { get; set; }

    /// <summary>Standing still: the driver's halt, or a stop on the way (then <see cref="HaltRemaining"/> counts down) — or,
    /// since #3444, a train with no room to run (longer than its open line), which cannot move at all.</summary>
    public bool Halted { get; set; }
    public float HaltRemaining { get; set; }

    /// <summary>The wagon items from the cab backwards.</summary>
    public string[] Wagons { get; set; } = System.Array.Empty<string>();

    /// <summary>The riders aboard (player ids).</summary>
    public string[] Riders { get; set; } = System.Array.Empty<string>();

    /// <summary>#3444: the train's speed along the line in blocks per second, signed like <see cref="Direction"/> (+ along
    /// increasing arc) — the speed it runs at over the next broadcast: its current speed, and for a train setting off from
    /// rest the first moment of its ease-in (<c>RailMotion.StartSpeed</c>), so a running train is never 0. 0 while it
    /// stands (halted — <see cref="Halted"/> then says so too, also for a train with no room to run — or a manual train
    /// nobody drives). The client runs its drawing on by it between the lists.
    /// Contractless-additive: 0 from an older server, whose trains ran at a fixed speed per setting — the client falls
    /// back to that table for a running (autopilot) train then (<c>TrainDrawArc.PredictSpeed</c>).</summary>
    public float Velocity { get; set; }

    /// <summary>#3446: whether villagers may ride this train (the owner's switch on the cab panel; the public train always
    /// takes them). Contractless-additive: an older server sends none and the default — on — stays.</summary>
    public bool TakesPassengers { get; set; } = true;
}

/// <summary>Every train of the current world (server → client, ~5 Hz while one moves).</summary>
public sealed class TrainList
{
    public NetTrain[] Trains { get; set; } = System.Array.Empty<NetTrain>();
}

/// <summary>A player boards a train's wagon (client → server): standing (seat −1) or on a seat of the wagon.</summary>
public sealed class EnterTrainIntent
{
    public string TrainId { get; set; } = string.Empty;
    public int Wagon { get; set; }
    public int Seat { get; set; } = -1;
}

/// <summary>The rider leaves the train (client → server): set down beside the wagon.</summary>
public sealed class ExitTrainIntent
{
}

/// <summary>The cab's panel (client → server): a speed setting, a halt or go, the autopilot switch, the passengers switch.
/// −1 = unchanged.</summary>
public sealed class SetTrainIntent
{
    public string TrainId { get; set; } = string.Empty;
    public int Speed { get; set; } = -1;
    public int Halt { get; set; } = -1;
    public int Autopilot { get; set; } = -1;

    /// <summary>#3446: villagers may ride (1) or not (0); −1 = unchanged. Contractless-additive: an older client sends
    /// none and the default −1 changes nothing.</summary>
    public int Passengers { get; set; } = -1;
}

/// <summary>The owner packs a train back into its items (client → server): within reach, nobody else aboard.</summary>
public sealed class StowTrainIntent
{
    public string TrainId { get; set; } = string.Empty;
}
