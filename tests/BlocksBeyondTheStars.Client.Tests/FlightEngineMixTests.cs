// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3248: the flight audio rules. The engine mix stays calm (idle at the old level, thrust swells, the afterburner only on
/// a surge), the cockpit hears a darker engine plus its electronics, the RCS puffs fire on a start or a flip and never per
/// frame, the hand-made Doppler bends the pitch the right way inside its band, other ships fade out with distance, the
/// shield recharge plays once per drop, and every weapon shot picks its sound from the module's fx style.
/// </summary>
public sealed class FlightEngineMixTests
{
    [Fact]
    public void Cruise_AtRest_IsTheIdleBedOnly()
    {
        var mix = FlightEngineMix.Target(FlightEnginePhase.Cruise, 0f, 0f, boost: false, firstPerson: false);
        Assert.Equal(FlightEngineMix.IdleLevel, mix.Idle, 4);
        Assert.Equal(0f, mix.Thrust);
        Assert.Equal(0f, mix.Boost);
        Assert.Equal(0f, mix.Cockpit);
        Assert.Equal(1f, mix.Pitch, 4);
    }

    [Fact]
    public void Thrust_SwellsWithTheThrottle_AndTheIdleDipsUnderIt()
    {
        float previousThrust = -1f;
        float previousPitch = 0f;
        for (float t = 0f; t <= 1.0001f; t += 0.25f)
        {
            var mix = FlightEngineMix.Target(FlightEnginePhase.Cruise, t, 0f, boost: false, firstPerson: false);
            Assert.True(mix.Thrust > previousThrust || (t == 0f && mix.Thrust == 0f));
            Assert.True(mix.Pitch >= previousPitch);
            previousThrust = mix.Thrust;
            previousPitch = mix.Pitch;
        }

        var full = FlightEngineMix.Target(FlightEnginePhase.Cruise, 1f, 1f, boost: false, firstPerson: false);
        Assert.Equal(FlightEngineMix.ThrustLevel, full.Thrust, 4);
        Assert.Equal(FlightEngineMix.IdleLevel - FlightEngineMix.IdleDuck, full.Idle, 4);
        Assert.Equal(1f + FlightEngineMix.DrivePitch, full.Pitch, 4);

        // A gentle start: half throttle is well under half the full thrust layer.
        var half = FlightEngineMix.Target(FlightEnginePhase.Cruise, 0.5f, 0f, boost: false, firstPerson: false);
        Assert.True(half.Thrust < 0.5f * full.Thrust);

        // Reverse thrust is thrust too.
        var reverse = FlightEngineMix.Target(FlightEnginePhase.Cruise, -1f, 0f, boost: false, firstPerson: false);
        Assert.Equal(full.Thrust, reverse.Thrust, 4);
    }

    [Fact]
    public void AStrafe_DrivesTheEngine_WithoutTheThrottle()
    {
        var strafing = FlightEngineMix.Target(FlightEnginePhase.Cruise, 0f, 1f, boost: false, firstPerson: false);
        Assert.True(strafing.Thrust > 0f);
        Assert.True(strafing.Thrust < FlightEngineMix.ThrustLevel);
    }

    [Fact]
    public void TheMix_IsCalm_NeverAConstantRoar()
    {
        // Everything at once — full throttle, full speed, a surge, outside — stays well under full scale.
        var loudest = FlightEngineMix.Target(FlightEnginePhase.Cruise, 1f, 1f, boost: true, firstPerson: false);
        Assert.True(loudest.Idle + loudest.Thrust + loudest.Boost < 0.9f);
        Assert.True(loudest.Boost > 0f);
        Assert.True(loudest.Pitch > 1f + FlightEngineMix.DrivePitch);
    }

    [Fact]
    public void TheCockpit_HearsADarkerEngine_AndItsElectronics()
    {
        var outside = FlightEngineMix.Target(FlightEnginePhase.Cruise, 1f, 1f, boost: true, firstPerson: false);
        var inside = FlightEngineMix.Target(FlightEnginePhase.Cruise, 1f, 1f, boost: true, firstPerson: true);
        Assert.Equal(0f, outside.Cockpit);
        Assert.Equal(FlightEngineMix.CockpitLevel, inside.Cockpit, 4);
        Assert.True(inside.Thrust < outside.Thrust);
        Assert.True(inside.Boost < outside.Boost);
        Assert.True(inside.Pitch < outside.Pitch);

        // The idle carries: relative to the thrust it is louder inside than outside.
        Assert.True(inside.Idle / inside.Thrust > outside.Idle / outside.Thrust);
    }

    [Theory]
    [InlineData(FlightEnginePhase.Sequence, FlightEngineMix.SequenceIdleLevel)]
    [InlineData(FlightEnginePhase.Eva, FlightEngineMix.EvaIdleLevel)]
    [InlineData(FlightEnginePhase.Off, 0f)]
    public void OutsideCruise_OnlyAQuietIdle(FlightEnginePhase phase, float idle)
    {
        var mix = FlightEngineMix.Target(phase, 1f, 1f, boost: true, firstPerson: true);
        Assert.Equal(idle, mix.Idle, 4);
        Assert.Equal(0f, mix.Thrust);
        Assert.Equal(0f, mix.Boost);
        Assert.Equal(0f, mix.Cockpit);
    }

    [Fact]
    public void BrokenInputs_AreClamped()
    {
        var mix = FlightEngineMix.Target(FlightEnginePhase.Cruise, float.NaN, float.PositiveInfinity, boost: false, firstPerson: false);
        Assert.Equal(FlightEngineMix.IdleLevel, mix.Idle, 4);
        Assert.Equal(0f, mix.Thrust);

        var over = FlightEngineMix.Target(FlightEnginePhase.Cruise, 7f, -3f, boost: false, firstPerson: false);
        Assert.Equal(FlightEngineMix.ThrustLevel, over.Thrust, 4);
    }

    [Fact]
    public void Ease_IsFrameRateIndependent_AndRisesAndFallsAtItsOwnPace()
    {
        float once = FlightEngineMix.Ease(0f, 1f, 0.2f, 0.35f, 0.8f);
        float twice = FlightEngineMix.Ease(FlightEngineMix.Ease(0f, 1f, 0.1f, 0.35f, 0.8f), 1f, 0.1f, 0.35f, 0.8f);
        Assert.Equal(once, twice, 4);
        Assert.True(once > 0f && once < 1f);

        float fall = 1f - FlightEngineMix.Ease(1f, 0f, 0.2f, 0.35f, 0.8f);
        Assert.True(fall < once); // settling is slower than swelling

        Assert.Equal(0.4f, FlightEngineMix.Ease(0.4f, 1f, 0f, 0.35f, 0.8f));       // no time, no change
        Assert.Equal(0.4f, FlightEngineMix.Ease(0.4f, 1f, float.NaN, 0.35f, 0.8f));
        Assert.Equal(1f, FlightEngineMix.Ease(float.NaN, 1f, 0.1f, 0.35f, 0.8f));  // a broken value snaps
        Assert.Equal(1f, FlightEngineMix.Ease(0f, 1f, 0.1f, 0f, 0f));               // no time constant = instant
        Assert.Equal(1f, FlightEngineMix.Ease(0f, 1f, 100f, 0.35f, 0.8f), 4);
    }

    [Fact]
    public void Doppler_RisesApproaching_FallsReceding_InsideItsBand()
    {
        Assert.Equal(1f, FlightEngineMix.DopplerPitch(0f), 4);
        Assert.True(FlightEngineMix.DopplerPitch(20f) > 1f);
        Assert.True(FlightEngineMix.DopplerPitch(-20f) < 1f);
        Assert.Equal(FlightEngineMix.DopplerMax, FlightEngineMix.DopplerPitch(10000f), 4);
        Assert.Equal(FlightEngineMix.DopplerMin, FlightEngineMix.DopplerPitch(-10000f), 4);
        Assert.Equal(1f, FlightEngineMix.DopplerPitch(float.NaN), 4);
    }

    [Fact]
    public void ClosingSpeed_ReadsTheVelocityAlongTheLineOfSight()
    {
        // The source sits 10 units ahead (+z) and flies toward the listener at 5 units/s.
        Assert.Equal(5f, FlightEngineMix.ClosingSpeed(0f, 0f, 10f, 0f, 0f, -5f), 4);

        // …flies away.
        Assert.Equal(-5f, FlightEngineMix.ClosingSpeed(0f, 0f, 10f, 0f, 0f, 5f), 4);

        // …passes sideways: no closing at all.
        Assert.Equal(0f, FlightEngineMix.ClosingSpeed(0f, 0f, 10f, 8f, 0f, 0f), 4);

        // Same spot or broken input: nothing.
        Assert.Equal(0f, FlightEngineMix.ClosingSpeed(0f, 0f, 0f, 1f, 1f, 1f));
        Assert.Equal(0f, FlightEngineMix.ClosingSpeed(float.NaN, 0f, 1f, 1f, 1f, 1f));
    }

    [Fact]
    public void RemoteEngines_FadeWithDistance_AndScaleWithSize()
    {
        const float range = 90f;
        float near = FlightEngineMix.RemoteEngineVolume(2f, range, 1f, muffled: false);
        float mid = FlightEngineMix.RemoteEngineVolume(45f, range, 1f, muffled: false);
        float far = FlightEngineMix.RemoteEngineVolume(85f, range, 1f, muffled: false);
        Assert.Equal(FlightEngineMix.RemoteEngineLevel, near, 4);
        Assert.True(near > mid && mid > far && far > 0f);
        Assert.Equal(0f, FlightEngineMix.RemoteEngineVolume(range, range, 1f, muffled: false));
        Assert.Equal(0f, FlightEngineMix.RemoteEngineVolume(float.NaN, range, 1f, muffled: false));

        Assert.True(FlightEngineMix.RemoteEngineVolume(20f, range, 1.8f, false) > FlightEngineMix.RemoteEngineVolume(20f, range, 0.6f, false));
        Assert.True(FlightEngineMix.RemoteEngineVolume(20f, range, 1f, true) < FlightEngineMix.RemoteEngineVolume(20f, range, 1f, false));

        // A big hull rumbles lower, a drone whines higher.
        Assert.True(FlightEngineMix.RemoteEnginePitch(1.8f) < 1f);
        Assert.True(FlightEngineMix.RemoteEnginePitch(0.6f) > 1f);
        Assert.Equal(1f, FlightEngineMix.RemoteEnginePitch(0f), 4);
        Assert.InRange(FlightEngineMix.RemoteEnginePitch(100f), 0.7f, 1.3f);
    }

    [Fact]
    public void EngineSizes_OnlyForShipsWithEngines()
    {
        Assert.True(FlightEngineMix.EngineSizeForKind(SpaceTargeting.Cruiser) > FlightEngineMix.EngineSizeForKind(SpaceTargeting.Drone));
        Assert.True(FlightEngineMix.EngineSizeForKind(SpaceTargeting.BanditShip) > 0f);
        Assert.True(FlightEngineMix.EngineSizeForKind(SpaceTargeting.HunterShuttle) > 0f);
        Assert.True(FlightEngineMix.EngineSizeForKind(SpaceTargeting.Ufo) > 0f);
        Assert.Equal(0f, FlightEngineMix.EngineSizeForKind(SpaceTargeting.SpaceStation));
        Assert.Equal(0f, FlightEngineMix.EngineSizeForKind(SpaceTargeting.Asteroid));
        Assert.Equal(0f, FlightEngineMix.EngineSizeForKind(SpaceTargeting.EscapePod));
        Assert.Equal(0f, FlightEngineMix.EngineSizeForKind(null));

        Assert.Equal(1f, FlightEngineMix.SizeFromExtent(2f), 4);
        Assert.Equal(1f, FlightEngineMix.SizeFromExtent(0f), 4);
        Assert.Equal(3f, FlightEngineMix.SizeFromExtent(50f), 4);
        Assert.Equal(0.5f, FlightEngineMix.SizeFromExtent(0.1f), 4);
    }

    [Fact]
    public void TurnAxis_NormalisesTheTurnRate()
    {
        Assert.Equal(0.5f, FlightEngineMix.TurnAxis(1f, 1f / 60f), 3);
        Assert.Equal(-1f, FlightEngineMix.TurnAxis(-90f, 0.1f), 4);
        Assert.Equal(0f, FlightEngineMix.TurnAxis(5f, 0f));
        Assert.Equal(0f, FlightEngineMix.TurnAxis(float.NaN, 0.1f));
    }

    [Fact]
    public void Rcs_PuffsOnAStart_NotPerFrameWhileHeld()
    {
        var rcs = new RcsPuffTrigger();
        Assert.False(rcs.Step(0f, 0f, 0f, 0f, out _));
        Assert.True(rcs.Step(0.02f, 1f, 0f, 0f, out int first));
        Assert.Equal(0, first);

        // Held for a second: silence.
        for (float t = 0.04f; t < 1f; t += 0.02f)
        {
            Assert.False(rcs.Step(t, 1f, 0f, 0f, out _));
        }
    }

    [Fact]
    public void Rcs_PuffsOnAFlip_AndAlternatesTheTakes()
    {
        var rcs = new RcsPuffTrigger();
        Assert.True(rcs.Step(0f, 1f, 0f, 0f, out int a));
        Assert.True(rcs.Step(0.5f, -1f, 0f, 0f, out int b)); // the other way: a flip
        Assert.NotEqual(a, b);
        Assert.True(rcs.Step(1f, 0f, 1f, 0f, out int c));    // a yaw starts
        Assert.Equal(a, c);
    }

    [Fact]
    public void Rcs_IsRateLimited()
    {
        var rcs = new RcsPuffTrigger();
        Assert.True(rcs.Step(0f, 1f, 0f, 0f, out _));
        Assert.False(rcs.Step(0.05f, 1f, 1f, 0f, out _));  // another axis inside the global gap: dropped…
        Assert.False(rcs.Step(0.3f, 1f, 1f, 0f, out _));   // …and not replayed later (it is already firing)
        Assert.True(rcs.Step(0.3f, 1f, 1f, 1f, out _));    // a third axis past the global gap
        Assert.False(rcs.Step(0.32f, -1f, 1f, 1f, out _)); // a quick flip of the strafe inside the gaps
    }

    [Fact]
    public void Rcs_AJitteryInput_IsOneLongTurn()
    {
        var rcs = new RcsPuffTrigger();
        int puffs = 0;
        for (int i = 0; i < 120; i++)
        {
            float yaw = i % 2 == 0 ? 0.6f : 0f; // a mouse polled every other frame
            if (rcs.Step(i / 60f, 0f, yaw, 0f, out _))
            {
                puffs++;
            }
        }

        Assert.Equal(1, puffs);

        // A real release (quiet for longer than the hold) and a new start puffs again.
        for (int i = 120; i < 150; i++)
        {
            rcs.Step(i / 60f, 0f, 0f, 0f, out _);
        }

        Assert.True(rcs.Step(150 / 60f, 0f, 0.6f, 0f, out _));
    }

    [Fact]
    public void Rcs_Hysteresis_HoldsBetweenTheThresholds_AndIgnoresBrokenInput()
    {
        var rcs = new RcsPuffTrigger();
        Assert.False(rcs.Step(0f, 0.3f, 0f, 0f, out _)); // below the press threshold
        Assert.True(rcs.Step(0.5f, 0.5f, 0f, 0f, out _));
        Assert.False(rcs.Step(1f, 0.25f, 0f, 0f, out _)); // eased off, but not released
        Assert.False(rcs.Step(2f, 0.5f, 0f, 0f, out _));  // so back up is no new start
        Assert.False(rcs.Step(3f, float.NaN, float.PositiveInfinity, 0f, out _));

        rcs.Reset();
        Assert.True(rcs.Step(4f, 0.5f, 0f, 0f, out _)); // a reset forgets the held axis
    }

    [Fact]
    public void BoostSurge_KicksInOnAQuickPush_ThenSettles()
    {
        var surge = new FlightBoostSurge();
        Assert.False(surge.Step(0.016f, 0f));
        Assert.True(surge.Step(0.016f, 1f));
        float t = 0f;
        while (surge.Step(0.1f, 1f))
        {
            t += 0.1f;
            Assert.True(t < FlightBoostSurge.SurgeSeconds + 0.2f, "the surge never ends while the throttle is held");
        }

        Assert.False(surge.Active);
        Assert.False(surge.Step(0.1f, 1f)); // held full throttle: no roar on and on
    }

    [Fact]
    public void BoostSurge_NeedsAQuickPush_EndsOnRelease_AndCoolsDown()
    {
        var slow = new FlightBoostSurge();
        slow.Step(0.016f, 0f);
        Assert.False(slow.Step(0.5f, 0.6f)); // eased up past Low…
        Assert.False(slow.Step(0.5f, 1f));   // …and only later to full: no surge

        var surge = new FlightBoostSurge();
        surge.Step(0.016f, 0f);
        Assert.True(surge.Step(0.016f, 1f));
        Assert.False(surge.Step(0.016f, 0.2f)); // let go: the surge ends at once
        Assert.False(surge.Step(0.016f, 1f));   // tapped again inside the cooldown
        surge.Step(FlightBoostSurge.Cooldown, 0f);
        Assert.True(surge.Step(0.016f, 1f));    // after the cooldown it kicks again

        surge.Reset();
        Assert.False(surge.Active);
        Assert.False(surge.Step(float.NaN, float.NaN));
    }

    [Fact]
    public void ShieldRecharge_PlaysOnceWhenTheShieldRisesAfterADrop()
    {
        var cue = new ShieldRechargeCue();
        cue.Reset(50f);
        Assert.False(cue.Step(0f, 50f, 50f));
        Assert.False(cue.Step(1f, 52f, 60f));  // a rise without a drop: nothing (a new module, a repair)
        Assert.False(cue.Step(2f, 30f, 60f));  // hit
        Assert.False(cue.Step(3f, 20f, 60f));  // hit again
        Assert.True(cue.Step(4f, 22f, 60f));   // recharging
        Assert.False(cue.Step(5f, 24f, 60f));  // still recharging: once only
        Assert.False(cue.Step(6f, 10f, 60f));  // hit…
        Assert.False(cue.Step(7f, 12f, 60f));  // …and recharging again inside the gap: quiet
        Assert.False(cue.Step(20f, 5f, 60f));
        Assert.True(cue.Step(21f, 7f, 60f));   // a later drop and rise plays again
    }

    [Fact]
    public void ShieldRecharge_IgnoresUnknownAndBrokenValues()
    {
        var cue = new ShieldRechargeCue();
        cue.Reset(-1f);
        Assert.False(cue.Step(0f, 10f, 0f));         // no shield fitted
        Assert.False(cue.Step(1f, float.NaN, 50f));
        Assert.False(cue.Step(2f, 5f, 50f));          // first known value
        Assert.False(cue.Step(3f, 0f, 50f));
        Assert.True(cue.Step(10f, 3f, 50f));
    }

    [Theory]
    [InlineData(FxStyles.TwinPulse, false, FlightWeaponCue.Laser)]
    [InlineData(FxStyles.PlasmaBolt, false, FlightWeaponCue.Cannon)]
    [InlineData(FxStyles.HeavyBeam, false, FlightWeaponCue.Cannon)]
    [InlineData(FxStyles.DrillBeam, false, FlightWeaponCue.Mine)]
    [InlineData(FxStyles.PlasmaBolt, true, FlightWeaponCue.Mine)]
    [InlineData(null, false, FlightWeaponCue.Laser)]
    [InlineData("generic", false, FlightWeaponCue.Laser)]
    public void WeaponShots_PickTheirSoundFromTheFxStyle(string? style, bool mining, string cue)
        => Assert.Equal(cue, FlightWeaponCue.For(style, mining));

    [Fact]
    public void TheShipWeaponsOfTheData_SoundAsIntended()
    {
        var content = BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        string Cue(string module) => FlightWeaponCue.For(FxStyleResolver.ForShipModule(module, content.GetShipModule(module)).Style, mining: false);

        Assert.Equal(FlightWeaponCue.Laser, Cue("ship_laser_basic"));
        Assert.Equal(FlightWeaponCue.Cannon, Cue("ship_cannon_1"));
        Assert.Equal(FlightWeaponCue.Cannon, Cue("laser_cannon_2"));
        Assert.Equal(FlightWeaponCue.Mine, Cue("asteroid_breaker"));
        Assert.True(FlightWeaponCue.Pitch(FxStyles.HeavyBeam) < FlightWeaponCue.Pitch(FxStyles.PlasmaBolt));
    }
}
