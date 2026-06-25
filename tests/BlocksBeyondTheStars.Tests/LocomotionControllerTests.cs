// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The pure <see cref="LocomotionController"/> (natural movement: stop-and-go, speed easing, turn-rate
/// inertia, weave, vertical life). Deterministic from a seed, so these are plain numeric assertions with no
/// server needed.
/// </summary>
public sealed class LocomotionControllerTests
{
    private static LocomotionProfile Roamer(float cruise = 2f, float accel = 4f, float turn = 3f) => new()
    {
        Style = LocomotionStyle.Strider,
        CruiseSpeed = cruise,
        BurstSpeed = cruise,
        Accel = accel,
        TurnRate = turn,
        HoldMin = 2f,
        HoldMax = 2f,
        PauseChance = 0f,
        PauseMin = 1f,
        PauseMax = 1f,
        WeaveAmp = 0f,
        WeaveFreq = 0f,
        VertAmp = 0f,
        VertFreq = 0f,
    };

    [Fact]
    public void Step_IsDeterministic_ForSameSeed()
    {
        var p = Roamer();
        Vector3f RunSim()
        {
            var s = default(LocomotionState);
            var pos = new Vector3f(0, 0, 0);
            for (int i = 0; i < 200; i++)
            {
                var r = LocomotionController.Step(s, p, pos, MoveMode.Roam, null, 0.05, 777u);
                s = r.State; pos = r.Position;
            }

            return pos;
        }

        var a = RunSim();
        var b = RunSim();
        Assert.Equal(a.X, b.X, 5);
        Assert.Equal(a.Z, b.Z, 5);
    }

    [Fact]
    public void DifferentSeeds_DivergeAtLeastSometimes()
    {
        var p = Roamer();
        Vector3f Run(uint seed)
        {
            var s = default(LocomotionState);
            var pos = new Vector3f(0, 0, 0);
            for (int i = 0; i < 200; i++)
            {
                var r = LocomotionController.Step(s, p, pos, MoveMode.Roam, null, 0.05, seed);
                s = r.State; pos = r.Position;
            }

            return pos;
        }

        var a = Run(1u);
        var b = Run(2u);
        Assert.True(System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Z - b.Z) > 0.5f, "two seeds should wander apart");
    }

    [Fact]
    public void Speed_EasesIn_NotInstant()
    {
        var p = Roamer(cruise: 4f, accel: 2f);
        var s = default(LocomotionState);
        var pos = new Vector3f(0, 0, 0);

        var r = LocomotionController.Step(s, p, pos, MoveMode.Roam, null, 0.1, 5u);
        Assert.True(r.State.Speed > 0f, "should start moving");
        Assert.True(r.State.Speed < p.CruiseSpeed, "must ease in, not jump to cruise instantly");

        // After enough time it should reach (very near) cruise.
        s = r.State; pos = r.Position;
        for (int i = 0; i < 100; i++)
        {
            r = LocomotionController.Step(s, p, pos, MoveMode.Roam, null, 0.1, 5u);
            s = r.State; pos = r.Position;
        }

        Assert.True(s.Speed > p.CruiseSpeed - 0.2f, "should converge to cruise");
    }

    [Fact]
    public void Heading_TurnsAtClampedRate_NoSnap()
    {
        const float turn = 1.0f; // rad/s
        const double dt = 0.1;
        var p = Roamer(turn: turn);

        // Controlled start: heading 0, already moving, roaming so it doesn't re-roll for a while.
        var s = new LocomotionState { Initialized = true, Mode = MoveMode.Roam, ModeTimer = 100f, Heading = 0f, Speed = 2f, Seq = 99 };
        var pos = new Vector3f(0, 0, 0);
        var target = new Vector3f(-10, 0, 0); // ~180° away → forces a big desired turn

        float prev = s.Heading;
        for (int i = 0; i < 30; i++)
        {
            var r = LocomotionController.Step(s, p, pos, MoveMode.Seek, target, dt, 1u);
            float delta = System.Math.Abs(WrapPi(r.Facing - prev));
            Assert.True(delta <= turn * (float)dt + 1e-3f, $"heading jumped {delta} > clamp");
            prev = r.Facing; s = r.State; pos = r.Position;
        }
    }

    [Fact]
    public void Seek_ClosesDistance_Flee_OpensIt()
    {
        var p = Roamer(cruise: 3f);

        float SeekClosestApproach()
        {
            // A constant-speed pursuer with a clamped turn rate ORBITS the target rather than halting on it, so
            // we assert the closest approach (it reaches the target), not the final resting spot.
            var s = default(LocomotionState);
            var pos = new Vector3f(0, 0, 0);
            var target = new Vector3f(20, 0, 0);
            float min = float.MaxValue;
            for (int i = 0; i < 300; i++)
            {
                var r = LocomotionController.Step(s, p, pos, MoveMode.Seek, target, 0.05, 3u);
                s = r.State; pos = r.Position;
                float dx = pos.X - 20f;
                float d = (float)System.Math.Sqrt(dx * dx + pos.Z * pos.Z);
                if (d < min) min = d;
            }

            return min;
        }

        float FleeFinalDist()
        {
            var s = default(LocomotionState);
            var pos = new Vector3f(0, 0, 0);
            var target = new Vector3f(0.5f, 0, 0);
            for (int i = 0; i < 200; i++)
            {
                var r = LocomotionController.Step(s, p, pos, MoveMode.Flee, target, 0.05, 4u);
                s = r.State; pos = r.Position;
            }

            float dx = pos.X - 0.5f;
            return (float)System.Math.Sqrt(dx * dx + pos.Z * pos.Z);
        }

        Assert.True(SeekClosestApproach() < 2f, "seeker should reach the target");
        Assert.True(FleeFinalDist() > 5f, "fleer should open the gap");
    }

    [Fact]
    public void Pause_StopsMovement_StopAndGo()
    {
        var p = Roamer(cruise: 3f, accel: 8f);
        p.HoldMin = 0.2f; p.HoldMax = 0.2f;   // a short roam segment...
        p.PauseChance = 1f;                    // ...always followed by a pause
        p.PauseMin = 3f; p.PauseMax = 3f;

        var s = default(LocomotionState);
        var pos = new Vector3f(0, 0, 0);
        bool sawPauseStop = false;
        for (int i = 0; i < 40; i++) // ~4 s
        {
            var r = LocomotionController.Step(s, p, pos, MoveMode.Roam, null, 0.1, 9u);
            s = r.State; pos = r.Position;
            if (s.Mode == MoveMode.Pause && i > 10 && s.Speed < 0.05f)
            {
                sawPauseStop = true;
            }
        }

        Assert.True(sawPauseStop, "a paused actor should ease to a stand-still (stop-and-go)");
    }

    [Fact]
    public void ForSpecies_BuildsAUsableProfile()
    {
        var sp = new CreatureSpecies { Id = "sp0", Speed = 2.5f, Size = 1.2f, LocoStyle = LocomotionStyle.Grazer };
        var prof = LocomotionController.ForSpecies(sp);
        Assert.Equal(LocomotionStyle.Grazer, prof.Style);
        Assert.True(prof.CruiseSpeed > 0f);
        Assert.True(prof.TurnRate > 0f);
        Assert.True(prof.PauseChance > 0f, "grazers pause to feed");

        // Deterministic per species.
        var prof2 = LocomotionController.ForSpecies(sp);
        Assert.Equal(prof.CruiseSpeed, prof2.CruiseSpeed, 5);
        Assert.Equal(prof.TurnRate, prof2.TurnRate, 5);
    }

    private static float WrapPi(float a)
    {
        while (a > System.Math.PI) a -= 6.2831853f;
        while (a < -System.Math.PI) a += 6.2831853f;
        return a;
    }
}
