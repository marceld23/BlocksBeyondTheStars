// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The monorail train's run (#2113, #2741) and its speed (#3444, Lyxette's request). A running train's speed follows
/// <see cref="RailMotion"/>: it eases in from rest, cruises by the length of the leg it is on (from the target behind it
/// to the target ahead — on autopilot the stops it halts at, else the open line's ends; a loop without stops, or a
/// driver at the panel, cruises at the setting's top speed), brakes into the target so it arrives at
/// <see cref="RailMotion.ArrivalSpeed"/> (a stop too close to brake for is passed, <see cref="TrainTargets"/>), and slows
/// for sharp bends, crests and dips anywhere along its length (<see cref="RailCurveLimits"/>). The speed is RAM state
/// (<see cref="ServerTrain.Velocity"/>): a halt, a reversal at a line end and a world load start it from rest. Driving by
/// hand (autopilot off, a rider in the cab) runs the same profile at the top speed and never halts at a stop; it still
/// brakes into an open line's end. A train longer than its open line stands (<see cref="NoRoomToRun"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A stop the train brakes for is reached by running this little bit past it, so the inclusive crossing check
    /// (<see cref="Crossed"/>) catches it on the tick it arrives; the halt then moves the train back by no more than this.</summary>
    private const float ArriveOvershoot = 0.001f;

    /// <summary>Two arcs closer than this count as the same spot (a train standing on a stop).</summary>
    private const float SameArc = 0.001f;

    /// <summary>Whether a train runs: on autopilot, or with a rider in the cab. A manual train nobody drives waits.</summary>
    private static bool TrainRuns(ServerTrain t) => t.Autopilot || t.Riders.Values.Any(r => r.Wagon == 0);

    /// <summary>A halted train sets off (#3444 seam — THE "train departs" event: the end of a stop's halt, a signal or a
    /// press on the stop, the driver's "go"): the halt ends and it starts from rest. Its last stop stays, so the stop it
    /// stood on does not catch it again. A train that is not halted is left alone.</summary>
    private static void DepartTrain(ServerTrain t)
    {
        if (!t.Halted)
        {
            return;
        }

        t.Halted = false;
        t.HaltStop = null;
        t.HaltUntil = 0;
        t.Velocity = 0f;
    }

    /// <summary>One tick of a running train: the speed profile gives the distance, the arc runs it — and on an open line
    /// bounces off the ends of the train's range, only the direction flips (#2741: the wagons keep their places and
    /// headings, so the riders, re-derived from their wagons every tick, ride through the reversal without a jump). On
    /// autopilot a stop the leading wagon reached halts the train (<see cref="HaltAtCrossedStop"/>).</summary>
    private void RunTrain(ServerTrain t, RailLine line, double dt)
    {
        float span = TrainSpan(t);
        int dir = t.Direction;
        float leadFrom = LeadArc(t, line, t.Arc, dir);
        float run = TrainRunDistance(t, line, span, dt);
        var (arcNow, dirNow) = RailRules.Advance(t.Arc, dir, t.Facing, run, line.Spline.TotalArc, span, line.Closed);
        t.Arc = arcNow;
        t.Direction = dirNow;
        if (dirNow != dir)
        {
            t.Velocity = 0f; // it braked into the line's end and turned round: it sets off the other way from rest
        }

        if (t.Autopilot && line.Stops.Count > 0)
        {
            HaltAtStopsReached(t, line, span, leadFrom, dir, dirNow);
        }
    }

    /// <summary>The autopilot's stop check after a run (once per stop, the next stop resets): a reversal splits the tick into
    /// the run to the end and the run back. Clear of its last stop again, that stop may catch the train next lap.</summary>
    private void HaltAtStopsReached(ServerTrain t, RailLine line, float span, float leadFrom, int dir, int dirNow)
    {
        if (dirNow != dir && !line.Closed)
        {
            var (lo, hi) = RailRules.CabRange(line.Spline.TotalArc, span, t.Facing);
            float bound = dir > 0 ? hi : lo;
            if (!HaltAtCrossedStop(t, line, leadFrom, LeadArc(t, line, bound, dir), dir))
            {
                HaltAtCrossedStop(t, line, LeadArc(t, line, bound, dirNow), LeadArc(t, line, t.Arc, dirNow), dirNow);
            }
        }
        else
        {
            HaltAtCrossedStop(t, line, leadFrom, LeadArc(t, line, t.Arc, dir), dir);
        }

        float leadAt = LeadArc(t, line, t.Arc, t.Direction);
        if (t.LastStop is { } last && !line.Stops.Any(s => s.Cell == last && Math.Abs(NormDelta(leadAt, s.Arc, line)) < RailRules.WagonLength))
        {
            t.LastStop = null;
        }
    }

    /// <summary>The train's new speed for this tick (<see cref="RailMotion.NextSpeed"/> under <see cref="RailMotion.Limit"/>)
    /// and the distance it runs. The last step onto a stop it halts at is cut to land on the stop. A train with no room to
    /// run (<see cref="NoRoomToRun"/>) stands at 0.</summary>
    private static float TrainRunDistance(ServerTrain t, RailLine line, float span, double dt)
    {
        if (NoRoomToRun(t, line, span))
        {
            t.Velocity = 0f; // longer than its open line: it stands where the line pins it, it does not "run" on the spot
            return 0f;
        }

        var (behind, ahead, stopAhead) = TrainTargets(t, line, span);
        float leg = t.Autopilot ? behind + ahead : float.PositiveInfinity; // by hand: the top speed
        float limit = RailMotion.Limit(RailMotion.TopSpeed(t.Speed), leg, ahead, TrainCurveLimit(t, line));
        t.Velocity = RailMotion.NextSpeed(t.Velocity, limit, dt);
        float run = t.Velocity * (float)Math.Max(0.0, dt);
        return stopAhead ? Math.Min(run, ahead + ArriveOvershoot) : run;
    }

    /// <summary>Whether a train has no room to run on its line (#3444): an open line no longer than the train (the cab's
    /// range, <see cref="RailRules.CabRange"/>, is a single spot — the line pins it at one end), or a line of no length. It
    /// stands, and the clients are told so (<see cref="WireHalted"/>, <see cref="WireVelocity"/>). A loop always has room.</summary>
    internal static bool NoRoomToRun(ServerTrain t, RailLine line, float span)
    {
        float total = line.Spline.TotalArc;
        if (total <= 0f)
        {
            return true;
        }

        if (line.Closed)
        {
            return false;
        }

        var (lo, hi) = RailRules.CabRange(total, span, t.Facing);
        return hi - lo <= 0f;
    }

    /// <summary>How far the train is from the target behind it and the target ahead (+∞ for none), and whether the one ahead
    /// is a stop it will halt at. The targets are an open line's ends (the bounds of the cab's range) and, on autopilot,
    /// the stops — measured from the leading wagon (<see cref="StopTargetDistance"/>).
    /// <para>#3444: a stop ahead the train could not arrive at even braking at <see cref="RailMotion.ServiceBrake"/>
    /// (<see cref="RailMotion.CanArriveAt"/> — the autopilot switched on a few blocks before it at speed, a stop placed
    /// just ahead) is no target: the nearest such stop becomes the train's <see cref="ServerTrain.LastStop"/>, so the
    /// halt check lets it pass this time, and the train runs on to the next stop instead of being cut onto this one at
    /// speed. (Two such stops less than one tick's run apart would still catch it on the second.)</para></summary>
    internal static (float Behind, float Ahead, bool StopAhead) TrainTargets(ServerTrain t, RailLine line, float span)
    {
        float total = line.Spline.TotalArc;
        int dir = t.Direction < 0 ? -1 : 1;
        float behind = float.PositiveInfinity, ahead = float.PositiveInfinity;
        if (!line.Closed)
        {
            var (lo, hi) = RailRules.CabRange(total, span, t.Facing);
            float arc = Math.Clamp(t.Arc, lo, hi);
            ahead = dir > 0 ? hi - arc : arc - lo;
            behind = dir > 0 ? arc - lo : hi - arc;
        }

        bool stopAhead = false;
        if (!t.Autopilot)
        {
            return (behind, ahead, stopAhead);
        }

        float lead = LeadArc(t, line, t.Arc, dir);
        Vector3i? passing = null;
        float passingAt = float.PositiveInfinity;
        foreach (var (arc, cell) in line.Stops)
        {
            float d = RailMotion.Gap(lead, arc, dir, total, line.Closed);
            float back = line.Closed ? (d <= SameArc ? 0f : total - d) : -d;
            if (back >= -SameArc)
            {
                behind = Math.Min(behind, Math.Max(0f, back));
            }

            float toStop = StopTargetDistance(t, line, cell, lead, arc, d, dir);
            if (toStop >= ahead)
            {
                continue;
            }

            if (!RailMotion.CanArriveAt(t.Velocity, toStop))
            {
                if (toStop < passingAt)
                {
                    passingAt = toStop;
                    passing = cell;
                }

                continue;
            }

            ahead = toStop;
            stopAhead = true;
        }

        if (passing is { } passed)
        {
            t.LastStop = passed;
        }

        return (behind, ahead, stopAhead);
    }

    /// <summary>How far ahead of the leading wagon (at <paramref name="lead"/>) a stop is as the train's target, +∞ when it
    /// is none: a stop behind it on an open line never is; the stop it last halted at (or passed) is none while it still
    /// lies ahead — but on a loop, once the train is level with it or past it, it is the target one lap ahead (#3444: a
    /// loop with a single stop used to leave the train without a target for its first wagon length after the halt, so it
    /// set off toward the top speed and braked to the loop's cruise again). <paramref name="d"/> is
    /// <see cref="RailMotion.Gap"/> from the lead to the stop.</summary>
    private static float StopTargetDistance(ServerTrain t, RailLine line, Vector3i cell, float lead, float stopArc, float d, int dir)
    {
        if (cell != t.LastStop)
        {
            return d >= -SameArc ? Math.Max(0f, d) : float.PositiveInfinity;
        }

        if (!line.Closed)
        {
            return float.PositiveInfinity;
        }

        float passedBy = NormDelta(lead, stopArc, line) * dir; // how far the lead is past the stop (negative: still before it)
        return passedBy >= -SameArc ? line.Spline.TotalArc - Math.Max(0f, passedBy) : float.PositiveInfinity;
    }

    /// <summary>The bend limit over the whole train, cab to last wagon (half a wagon past each end): its last wagon is not
    /// whipped round the bend its cab just left. The line's limits are sampled on first use.</summary>
    private static float TrainCurveLimit(ServerTrain t, RailLine line)
    {
        if (line.Curves is null || !ReferenceEquals(line.Curves.Spline, line.Spline))
        {
            line.Curves = RailCurveLimits.Build(line.Spline);
        }

        float tail = RailRules.WagonArc(t.Arc, Math.Max(0, t.Wagons.Count - 1), t.Facing);
        float half = RailRules.WagonLength * 0.5f;
        return line.Curves.MinOver(Math.Min(t.Arc, tail) - half, Math.Max(t.Arc, tail) + half);
    }

    /// <summary>What the clients get as <c>NetTrain.Velocity</c> (#3444): the speed it runs at over the next broadcast,
    /// signed like its direction — its current speed, at least <see cref="RailMotion.StartSpeed"/> for a running train
    /// (one setting off from rest), so a client never mistakes a running train for an older server's (which sends 0); 0
    /// while it stands — and for a train with no room to run (<see cref="NoRoomToRun"/>; <paramref name="line"/> null:
    /// its line is unknown), which is sent as halted too (<see cref="WireHalted"/>).</summary>
    internal static float WireVelocity(ServerTrain t, RailLine? line)
    {
        if (t.Halted || !TrainRuns(t) || (line is not null && NoRoomToRun(t, line, TrainSpan(t))))
        {
            return 0f;
        }

        float v = Math.Max(t.Velocity, RailMotion.StartSpeed);
        return t.Direction < 0 ? -v : v;
    }

    /// <summary>What the clients get as <c>NetTrain.Halted</c>: the train's halt — and, since #3444, a train with no room to
    /// run on its line (<see cref="NoRoomToRun"/>), so the cab reads "Halted" instead of "Running — 3 blocks/s" for a train
    /// that cannot move, and a client never runs its drawing on (a velocity of 0 on a train that is not halted would read
    /// as an older server's).</summary>
    internal static bool WireHalted(ServerTrain t, RailLine? line)
        => t.Halted || (line is not null && NoRoomToRun(t, line, TrainSpan(t)));
}
