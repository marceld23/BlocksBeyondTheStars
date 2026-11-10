// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3444 — the monorail train's speed profile (Lyxette: "the train is too slow to connect a farther city"), modelled on
/// the lift's <see cref="LiftMotion"/> (#3204). The three settings are <b>top speeds</b> (<see cref="RailRules.SpeedTable"/>:
/// 8 / 16 / 24 blocks per second); within that a train
/// <list type="bullet">
/// <item><b>eases in</b> from rest at <see cref="Acceleration"/> (0 → 16 in 4 s);</item>
/// <item><b>cruises by the length of its leg</b> (<see cref="CruiseSpeed"/>): the leg is the run from the target behind
/// it to the target ahead — on autopilot the stops, else the open line's ends — and it cruises at the leg over
/// <see cref="LegSeconds"/>, at least <see cref="MinCruise"/>: a 40-block hop in town tops out near 7 blocks/s, a leg
/// of 96 blocks reaches 16 and one of about 150 blocks 24; a loop without stops (and a driver at the panel) cruises at the
/// top speed;</item>
/// <item><b>brakes into the target</b> on the curve v ≤ √(<see cref="ArrivalSpeed"/>² + 2·<see cref="Acceleration"/>·d)
/// (<see cref="BrakingSpeed"/>), so it arrives at a stop or a line's end at a walking pace and the halt never pulls
/// it back;</item>
/// <item><b>slows for sharp bends</b> (<see cref="CurveSpeed(float)"/>): v²·κ ≤ <see cref="LateralAcceleration"/> for a
/// sideways bend, v²·κ ≤ <see cref="VerticalAcceleration"/> for a crest or a dip (<see cref="CrestSpeed"/>) — the linker
/// allows any bend, so a tight one is taken slowly; <see cref="RailCurveLimits"/> measures both along the line and
/// spreads each limit so the train brakes before the bend.</item>
/// </list>
/// A limit that drops (a lower setting, the autopilot switched on near a stop) is followed at no more than
/// <see cref="ServiceBrake"/>. A stop the train could not arrive at even braking that hard (<see cref="CanArriveAt"/> —
/// the autopilot switched on a few blocks before it at speed, a stop placed just ahead) is passed this time, and the train
/// halts at the next one. The server owns the motion (<c>GameServerRailMotion.cs</c>) and sends the train's speed on
/// the wire (<c>NetTrain.Velocity</c>); this class is pure arithmetic, shared so the client and the tests use the same
/// numbers.
/// </summary>
public static class RailMotion
{
    /// <summary>How fast a train speeds up and brakes into a target, in blocks per second per second — the lift's ease
    /// (<see cref="LiftMotion.Acceleration"/>): 0 → 16 in 4 s, 0 → 24 in 6 s.</summary>
    public const float Acceleration = 4f;

    /// <summary>The firmest a train slows down to a limit that dropped under it (blocks/s²) — a lower setting, the
    /// autopilot switched on, a stop placed just ahead. Twice the ease, still no jolt. A stop it cannot arrive at even
    /// braking this hard is passed (<see cref="CanArriveAt"/>) rather than reached with a jolt.</summary>
    public const float ServiceBrake = 8f;

    /// <summary>The speed a train arrives at a stop or a line's end with (blocks/s): it then halts (or turns round) from
    /// a walking pace, so the halt's snap onto the stop is at most this times one tick.</summary>
    public const float ArrivalSpeed = 2.5f;

    /// <summary>The fastest a train may still halt at a stop from (blocks/s) — twice <see cref="ArrivalSpeed"/>, a brisk
    /// walk. A stop it would reach faster even braking at <see cref="ServiceBrake"/> all the way is passed
    /// (<see cref="CanArriveAt"/>). Not <see cref="ArrivalSpeed"/> itself: on the braking curve's discrete steps an ordinary
    /// arrival runs a hair above it on its last tick (≈ 2.9 blocks/s a tenth of a block before the stop at 15 ticks a
    /// second), and that stop must never be passed.</summary>
    public const float MaxHaltSpeed = 2f * ArrivalSpeed;

    /// <summary>A leg's cruise speed is its length over this many seconds (<see cref="CruiseSpeed"/>).</summary>
    public const float LegSeconds = 6f;

    /// <summary>The slowest a leg cruises (blocks/s): a hop of a few blocks still moves on briskly.</summary>
    public const float MinCruise = 4f;

    /// <summary>The comfort bound in a sideways bend: speed² × curvature (the sideways acceleration) stays below this
    /// (blocks/s²). A bend of radius 16 is taken at 8 blocks/s, radius 64 at 16, radius 144 at 24.</summary>
    public const float LateralAcceleration = 4f;

    /// <summary>The comfort bound over a crest or through a dip: speed² × the vertical curvature stays below this
    /// (blocks/s²) — twice the sideways bound, as a rider is pressed into the seat or lifted a little, not thrown sideways:
    /// a crest of radius 8 is taken at 8 blocks/s, radius 32 at 16, radius 72 at 24, so a gently hilly line runs at full
    /// speed.</summary>
    public const float VerticalAcceleration = 8f;

    /// <summary>The speed a train setting off from rest is reported at (#3444, <c>NetTrain.Velocity</c>): the average of
    /// its first broadcast interval's ease-in. Never 0, so a client tells a running train from a standing one (an older
    /// server sends 0 for every train).</summary>
    public const float StartSpeed = Acceleration * (float)RailRules.BroadcastSeconds * 0.5f;

    /// <summary>The fastest any setting runs (the last of <see cref="RailRules.SpeedTable"/>).</summary>
    public static float MaxSpeed => RailRules.SpeedTable[RailRules.SpeedTable.Length - 1];

    /// <summary>The top speed of a speed setting (1..3; clamped).</summary>
    public static float TopSpeed(int setting)
        => RailRules.SpeedTable[Math.Clamp(setting, 1, RailRules.SpeedTable.Length) - 1];

    /// <summary>The cruise speed of a leg of <paramref name="leg"/> blocks under a top speed of <paramref name="cap"/>: the
    /// leg over <see cref="LegSeconds"/>, at least <see cref="MinCruise"/>, at most the cap. No leg (+∞, NaN — a loop
    /// without a stop, a driver at the panel) cruises at the cap.</summary>
    public static float CruiseSpeed(float leg, float cap)
    {
        cap = IsFinite(cap) ? Math.Max(0f, cap) : 0f;
        if (!IsFinite(leg))
        {
            return cap;
        }

        return Math.Min(cap, Math.Max(MinCruise, Math.Max(0f, leg) / LegSeconds));
    }

    /// <summary>The fastest a train may run with <paramref name="remaining"/> blocks to its target and still arrive at
    /// <see cref="ArrivalSpeed"/> braking at <see cref="Acceleration"/>; +∞ when there is no target.</summary>
    public static float BrakingSpeed(float remaining)
    {
        if (!IsFinite(remaining))
        {
            return float.PositiveInfinity;
        }

        return (float)Math.Sqrt((ArrivalSpeed * ArrivalSpeed) + (2f * Acceleration * Math.Max(0f, remaining)));
    }

    /// <summary>Whether a train running at <paramref name="speed"/> can still arrive at a stop <paramref name="remaining"/>
    /// blocks ahead at a walking pace, braking at <see cref="ServiceBrake"/>: v² ≤ <see cref="MaxHaltSpeed"/>² +
    /// 2·ServiceBrake·d. When it cannot (the autopilot switched on just before a stop at speed, a stop placed just ahead) the
    /// autopilot passes that stop this time instead of cutting the train onto it at speed.</summary>
    public static bool CanArriveAt(float speed, float remaining)
    {
        if (!IsFinite(speed) || speed <= MaxHaltSpeed)
        {
            return true;
        }

        float reach = (MaxHaltSpeed * MaxHaltSpeed) + (2f * ServiceBrake * (float.IsNaN(remaining) ? 0f : Math.Max(0f, remaining)));
        return speed * speed <= reach;
    }

    /// <summary>The fastest a sideways bend of <paramref name="curvature"/> (1 / radius, per block) is taken:
    /// √(<see cref="LateralAcceleration"/> / κ), never above <see cref="MaxSpeed"/> (a straight line).</summary>
    public static float CurveSpeed(float curvature) => BoundSpeed(curvature, LateralAcceleration);

    /// <summary>The fastest a crest or a dip of vertical <paramref name="curvature"/> (1 / radius, per block) is taken:
    /// √(<see cref="VerticalAcceleration"/> / κ), never above <see cref="MaxSpeed"/>.</summary>
    public static float CrestSpeed(float curvature) => BoundSpeed(curvature, VerticalAcceleration);

    /// <summary>The bend limit of a spot whose line turns sideways by <paramref name="horizontal"/> and up or down by
    /// <paramref name="vertical"/> (curvatures, 1 / radius): the lower of <see cref="CurveSpeed(float)"/> and
    /// <see cref="CrestSpeed"/>.</summary>
    public static float CurveSpeed(float horizontal, float vertical) => Math.Min(CurveSpeed(horizontal), CrestSpeed(vertical));

    private static float BoundSpeed(float curvature, float bound)
    {
        if (!IsFinite(curvature) || curvature <= 1e-6f)
        {
            return MaxSpeed;
        }

        return Math.Min(MaxSpeed, (float)Math.Sqrt(bound / curvature));
    }

    /// <summary>The speed a train may run at now: its top speed <paramref name="cap"/>, the cruise of its
    /// <paramref name="leg"/>, the braking curve to the target <paramref name="remaining"/> blocks ahead and the bend
    /// limit <paramref name="curve"/> — the lowest of them.</summary>
    public static float Limit(float cap, float leg, float remaining, float curve)
    {
        float limit = Math.Min(CruiseSpeed(leg, cap), BrakingSpeed(remaining));
        if (IsFinite(curve))
        {
            limit = Math.Min(limit, Math.Max(0f, curve));
        }

        return limit;
    }

    /// <summary>The speed for the next step of <paramref name="dt"/> seconds: up from <paramref name="speed"/> by
    /// <see cref="Acceleration"/> (ease in), never above <paramref name="limit"/>, and down toward a lower limit by at
    /// most <see cref="ServiceBrake"/> (no jolt). Never negative.</summary>
    public static float NextSpeed(float speed, float limit, double dt)
    {
        speed = IsFinite(speed) ? Math.Max(0f, speed) : 0f;
        limit = float.IsNaN(limit) ? 0f : Math.Max(0f, limit);
        float step = double.IsNaN(dt) || double.IsInfinity(dt) ? 0f : (float)Math.Max(0.0, dt);
        float rising = Math.Min(speed + (Acceleration * step), limit);
        return Math.Max(0f, Math.Max(speed - (ServiceBrake * step), rising));
    }

    /// <summary>How far ahead of the arc <paramref name="from"/> the arc <paramref name="to"/> lies for a train running
    /// <paramref name="direction"/> (+1 toward increasing arc): on an open line the signed distance (negative = behind
    /// it); on a loop of <paramref name="total"/> the distance going round the way it runs, in [0, total).</summary>
    public static float Gap(float from, float to, int direction, float total, bool closed)
    {
        float d = (to - from) * (direction < 0 ? -1f : 1f);
        if (closed && total > 0f)
        {
            d %= total;
            if (d < 0f)
            {
                d += total;
            }

            if (d >= total)
            {
                d = 0f;
            }
        }

        return d;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
