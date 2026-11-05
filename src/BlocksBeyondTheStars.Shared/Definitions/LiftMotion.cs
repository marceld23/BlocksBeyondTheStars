// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3204 — the lift's speed curve. A short ride keeps the old pace (<see cref="CrystalNetRules.LiftSpeed"/>, 3 cells per
/// second); a long ride speeds up so that a ride of any length takes about <see cref="CruiseSeconds"/>, up to
/// <see cref="MaxSpeed"/> — 256 cells take about 25 seconds instead of 85. It eases in from the short-ride pace and eases
/// out again before the floor (<see cref="Acceleration"/>), so a rider never gets a jolt. The server owns the motion and
/// sends the speed per lift on the wire (<c>NetLift.Speed</c>); this class is pure arithmetic, shared so the client could
/// mirror it.
/// <para><b>Kid rule.</b> The server reads the layer a platform going down is about to enter for a body every tick, so the
/// cruise speed never goes above one layer per tick at the configured tick rate (<see cref="SafeSpeed"/>: 15 cells per
/// second at the default 15 Hz, which <see cref="MaxSpeed"/> stays below). The server also reads every layer a longer step
/// sweeps (a slow tick), so this is a second line of defence, not the only one.</para>
/// </summary>
public static class LiftMotion
{
    /// <summary>The fastest a lift ever rides, in cells per second.</summary>
    public const float MaxSpeed = 12f;

    /// <summary>A long ride aims to take about this long: its cruise speed is the distance over this, within
    /// [<see cref="CrystalNetRules.LiftSpeed"/>, <see cref="MaxSpeed"/>]. Rides up to 36 cells keep the short-ride pace.</summary>
    public const float CruiseSeconds = 12f;

    /// <summary>How fast a long ride speeds up and slows down, in cells per second per second (3 → 12 in about 2 s).</summary>
    public const float Acceleration = 4f;

    /// <summary>The kid rule reads one layer ahead per tick: at most this many layers per tick at the nominal tick rate.</summary>
    public const float LayersPerTick = 1f;

    /// <summary>The highest speed the kid rule's one-layer-per-tick read keeps safe at this tick rate (Hz).</summary>
    public static float SafeSpeed(int tickRate) => Math.Max(1, tickRate) * LayersPerTick;

    /// <summary>The speed a ride of <paramref name="distance"/> cells cruises at on a server ticking at
    /// <paramref name="tickRate"/> Hz: the distance over <see cref="CruiseSeconds"/>, clamped to the short-ride pace and
    /// <see cref="MaxSpeed"/>, and never above <see cref="SafeSpeed"/>.</summary>
    public static float CruiseSpeed(float distance, int tickRate)
    {
        float cruise = Math.Min(Math.Max(Math.Abs(distance) / CruiseSeconds, CrystalNetRules.LiftSpeed), MaxSpeed);
        return Math.Min(cruise, SafeSpeed(tickRate));
    }

    /// <summary>The speed for the next step of a ride: up from <paramref name="speed"/> by <see cref="Acceleration"/> (ease
    /// in), never above <paramref name="cruise"/>, and down again so the platform can slow to the short-ride pace by the time
    /// it covers <paramref name="remaining"/> cells (ease out). Never below the short-ride pace (or the cruise, when that is
    /// lower) — a ride always moves on.</summary>
    public static float NextSpeed(float speed, float remaining, float cruise, double dt)
    {
        float floor = Math.Min(CrystalNetRules.LiftSpeed, cruise);
        float rising = Math.Max(speed, floor) + Acceleration * (float)Math.Max(0.0, dt);
        float braking = (float)Math.Sqrt((floor * floor) + (2f * Acceleration * Math.Max(0f, remaining)));
        return Math.Max(floor, Math.Min(cruise, Math.Min(rising, braking)));
    }

    /// <summary>The average speed over the next <paramref name="horizon"/> seconds of a ride that goes on undisturbed — what
    /// the server sends as <c>NetLift.Speed</c>: the client eases the platform on at that speed from the last list, so the
    /// average (not the speed of this instant) lands it where the next list will say it is. A ride that arrives within the
    /// horizon averages over the time until it arrives.</summary>
    public static float AverageSpeed(float speed, float remaining, float cruise, double horizon)
    {
        const int Steps = 4;
        if (horizon <= 0.0 || remaining <= 0f)
        {
            return Math.Max(0f, speed);
        }

        float dt = (float)(horizon / Steps);
        float covered = 0f, elapsed = 0f, v = speed;
        for (int i = 0; i < Steps; i++)
        {
            v = NextSpeed(v, remaining - covered, cruise, dt);
            if (v <= 0f)
            {
                return 0f; // no cruise speed: nothing to ease on
            }

            if (covered + (v * dt) >= remaining)
            {
                elapsed += (remaining - covered) / v;
                covered = remaining;
                break;
            }

            covered += v * dt;
            elapsed += dt;
        }

        return elapsed > 0f ? covered / elapsed : v;
    }
}
