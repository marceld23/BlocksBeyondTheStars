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

    /// <summary>The speed setting 1..3 (see <c>RailRules.SpeedTable</c>).</summary>
    public int Speed { get; set; } = 1;

    /// <summary>+1 along increasing arc, −1 against it.</summary>
    public int Direction { get; set; } = 1;

    public bool Autopilot { get; set; }

    /// <summary>Standing still: the driver's halt, or a stop on the way (then <see cref="HaltRemaining"/> counts down).</summary>
    public bool Halted { get; set; }
    public float HaltRemaining { get; set; }

    /// <summary>The wagon items from the cab backwards.</summary>
    public string[] Wagons { get; set; } = System.Array.Empty<string>();

    /// <summary>The riders aboard (player ids).</summary>
    public string[] Riders { get; set; } = System.Array.Empty<string>();
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

/// <summary>The cab's panel (client → server): a speed setting, a halt or go, the autopilot switch. −1 = unchanged.</summary>
public sealed class SetTrainIntent
{
    public string TrainId { get; set; } = string.Empty;
    public int Speed { get; set; } = -1;
    public int Halt { get; set; } = -1;
    public int Autopilot { get; set; } = -1;
}

/// <summary>The owner packs a train back into its items (client → server): within reach, nobody else aboard.</summary>
public sealed class StowTrainIntent
{
    public string TrainId { get; set; } = string.Empty;
}
