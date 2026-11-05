// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3204 — the lift's speed curve (<see cref="LiftMotion"/>), as pure arithmetic: short rides keep the old pace, long rides
/// cruise faster with an ease in and out, the cruise never outruns the kid rule's one-layer-per-tick read, and the speed the
/// lift list carries lands the client's linear easing where the server's platform will be. The server half (a ride
/// through the real tick) is pinned in <see cref="LiftShaftTests"/>.
/// </summary>
public sealed class LiftMotionTests
{
    private const int DefaultTickRate = 15;

    /// <summary>A whole ride at a fixed tick: the speeds it uses, the largest step of one tick, and how long it takes.</summary>
    private static (float MaxSpeed, float FirstSpeed, float LastSpeed, float MaxStep, double Seconds) Ride(float distance, int tickRate, double dt)
    {
        float cruise = LiftMotion.CruiseSpeed(distance, tickRate);
        float covered = 0f, speed = CrystalNetRules.LiftSpeed, maxSpeed = 0f, maxStep = 0f, first = -1f;
        double seconds = 0;
        while (covered < distance && seconds < 600)
        {
            speed = LiftMotion.NextSpeed(speed, distance - covered, cruise, dt);
            first = first < 0f ? speed : first;
            float step = Math.Min(speed * (float)dt, distance - covered);
            covered += step;
            maxSpeed = Math.Max(maxSpeed, speed);
            maxStep = Math.Max(maxStep, step);
            seconds += dt;
        }

        return (maxSpeed, first, speed, maxStep, seconds);
    }

    [Fact]
    public void AShortRide_KeepsTheOldPace_AndALongOneCruisesFaster_UpToTwelve()
    {
        Assert.Equal(CrystalNetRules.LiftSpeed, LiftMotion.CruiseSpeed(5, DefaultTickRate));
        Assert.Equal(CrystalNetRules.LiftSpeed, LiftMotion.CruiseSpeed(36, DefaultTickRate)); // the old 48-cell lift's rides barely change
        Assert.Equal(100f / LiftMotion.CruiseSeconds, LiftMotion.CruiseSpeed(100, DefaultTickRate), 3);
        Assert.Equal(LiftMotion.MaxSpeed, LiftMotion.CruiseSpeed(CrystalNetRules.LiftMaxHeight, DefaultTickRate));
        Assert.Equal(LiftMotion.MaxSpeed, LiftMotion.CruiseSpeed(-255, DefaultTickRate)); // the way down is as fast

        var shortRide = Ride(5, DefaultTickRate, 1.0 / DefaultTickRate);
        Assert.Equal(CrystalNetRules.LiftSpeed, shortRide.MaxSpeed); // no ease at all: exactly the old lift
    }

    [Fact]
    public void TheCruise_NeverOutrunsTheKidRule_OneLayerPerTickAtTheTickRate()
    {
        Assert.True(LiftMotion.MaxSpeed <= LiftMotion.SafeSpeed(DefaultTickRate), "the default tick rate must keep the top speed safe");
        Assert.Equal(10f, LiftMotion.CruiseSpeed(CrystalNetRules.LiftMaxHeight, 10)); // a slower server rides slower
        Assert.Equal(5f, LiftMotion.CruiseSpeed(CrystalNetRules.LiftMaxHeight, 5));
        Assert.Equal(1f, LiftMotion.CruiseSpeed(CrystalNetRules.LiftMaxHeight, 0)); // a nonsense rate still moves, safely

        foreach (int tickRate in new[] { 5, 10, 15, 20 })
        {
            var ride = Ride(CrystalNetRules.LiftMaxHeight - 1, tickRate, 1.0 / tickRate);
            Assert.True(ride.MaxSpeed <= LiftMotion.SafeSpeed(tickRate) + 1e-4f, $"{tickRate} Hz: {ride.MaxSpeed} cells/s");
            Assert.True(ride.MaxStep <= LiftMotion.LayersPerTick + 1e-4f, $"{tickRate} Hz: {ride.MaxStep} layers in one tick");
        }
    }

    [Fact]
    public void ALongRide_EasesInAndOut_AndReachesTheTopInAboutHalfAMinute()
    {
        var ride = Ride(CrystalNetRules.LiftMaxHeight - 1, DefaultTickRate, 1.0 / DefaultTickRate);
        Assert.Equal(LiftMotion.MaxSpeed, ride.MaxSpeed);
        Assert.True(ride.FirstSpeed < CrystalNetRules.LiftSpeed + 0.5f, $"eases in: first step at {ride.FirstSpeed}");
        Assert.True(ride.LastSpeed < CrystalNetRules.LiftSpeed + 1.5f, $"eases out: last step at {ride.LastSpeed}");
        Assert.InRange(ride.Seconds, 20.0, 30.0); // 85 s at the old pace

        var middle = Ride(100, DefaultTickRate, 1.0 / DefaultTickRate);
        Assert.InRange(middle.Seconds, 11.0, 16.0);
    }

    [Fact]
    public void TheWireSpeed_IsTheAverageUntilTheNextList_SoTheClientLandsWhereTheServerIs()
    {
        const double Horizon = 0.2;
        const float Distance = 200f;
        float cruise = LiftMotion.CruiseSpeed(Distance, DefaultTickRate);
        double dt = 1.0 / DefaultTickRate;
        foreach (float startSpeed in new[] { CrystalNetRules.LiftSpeed, 6f, LiftMotion.MaxSpeed })
        {
            foreach (float remaining in new[] { Distance, 30f, 8f, 1.5f })
            {
                float wire = LiftMotion.AverageSpeed(startSpeed, remaining, cruise, Horizon);
                float covered = 0f, speed = startSpeed;
                for (double t = 0; t < Horizon - 1e-6 && covered < remaining; t += dt)
                {
                    speed = LiftMotion.NextSpeed(speed, remaining - covered, cruise, dt);
                    covered = Math.Min(remaining, covered + (speed * (float)dt));
                }

                float eased = Math.Min(remaining, wire * (float)Horizon); // LiftView: PlatformY + Speed × time, clamped to the target
                Assert.True(Math.Abs(eased - covered) < 0.35f, $"from {startSpeed} with {remaining} left: client {eased}, server {covered}");
            }
        }

        Assert.Equal(0f, LiftMotion.AverageSpeed(0f, 10f, 0f, Horizon)); // no cruise: nothing to ease on
    }
}
