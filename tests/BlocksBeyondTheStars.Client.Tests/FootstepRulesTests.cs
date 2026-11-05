// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3239: living footsteps. Every step used to play one take of the surface every 0.45 s at one level and pitch — walking,
/// sneaking and running alike — and every landing, a hop or a five-block drop, was the same thud. <see cref="FootstepRules"/>
/// and <see cref="FootstepCadence"/> now give the rhythm, the takes, the foot alternation and the landing tier.
/// </summary>
public sealed class FootstepRulesTests
{
    private const float Walk = FootstepRules.ReferenceWalkSpeed;
    private const float Frame = 1f / 60f;

    [Fact]
    public void TheWorldsWalk_StepsAtTheOldInterval_FasterQuicker_SlowerSlower()
    {
        Assert.Equal(FootstepRules.WalkInterval, FootstepRules.IntervalFor(FootGait.Walk, Walk, Walk), 4);

        float half = FootstepRules.IntervalFor(FootGait.Walk, Walk * 0.5f, Walk);
        float run = FootstepRules.IntervalFor(FootGait.Run, Walk * 1.5f, Walk);
        float sprint = FootstepRules.IntervalFor(FootGait.Run, Walk * 3f, Walk);
        Assert.True(half > FootstepRules.WalkInterval && half <= FootstepRules.SneakInterval, $"a slow walk steps slower ({half})");
        Assert.True(run < FootstepRules.WalkInterval && run > FootstepRules.RunInterval, $"a run steps quicker ({run})");
        Assert.Equal(FootstepRules.RunInterval, sprint, 4); // never quicker than a run
        Assert.Equal(FootstepRules.SneakInterval, FootstepRules.IntervalFor(FootGait.Walk, 0.6f, Walk), 4); // never slower than a sneak
    }

    [Fact]
    public void TheIntervalShrinks_AsTheSpeedGrows()
    {
        float previous = float.MaxValue;
        for (float speed = 1f; speed <= 15f; speed += 0.5f)
        {
            float interval = FootstepRules.IntervalFor(FootstepRules.GaitFor(speed, Walk, crouched: false), speed, Walk);
            Assert.True(interval <= previous, $"{speed} m/s steps every {interval} s, slower than at {speed - 0.5f} m/s");
            previous = interval;
        }
    }

    [Fact]
    public void ASneak_IsSlowerAndSofter_ARunLouder()
    {
        Assert.Equal(FootGait.Sneak, FootstepRules.GaitFor(Walk * 2f, Walk, crouched: true));
        Assert.Equal(FootGait.Walk, FootstepRules.GaitFor(Walk, Walk, crouched: false));
        Assert.Equal(FootGait.Run, FootstepRules.GaitFor(Walk * FootstepRules.RunSpeedRatio, Walk, crouched: false));

        Assert.Equal(FootstepRules.SneakInterval, FootstepRules.IntervalFor(FootGait.Sneak, Walk, Walk), 4);
        Assert.Equal(0.6f, FootstepRules.GaitVolume(FootGait.Sneak), 4);
        Assert.True(FootstepRules.GaitVolume(FootGait.Run) > FootstepRules.GaitVolume(FootGait.Walk));
    }

    [Fact]
    public void ATake_NeverRepeatsThePreviousOne_AndEveryTakeComesUp()
    {
        for (int previous = 0; previous <= FootstepRules.TakeCount; previous++)
        {
            var seen = new HashSet<int>();
            for (double roll = 0d; roll < 1d; roll += 0.01d)
            {
                int take = FootstepRules.NextTake(previous, roll);
                Assert.InRange(take, 1, FootstepRules.TakeCount);
                Assert.NotEqual(previous, take);
                seen.Add(take);
            }

            Assert.Equal(previous == 0 ? FootstepRules.TakeCount : FootstepRules.TakeCount - 1, seen.Count);
        }

        Assert.InRange(FootstepRules.NextTake(2, 1d), 1, 3); // a roll of exactly 1 stays in range
    }

    [Fact]
    public void TheFeetAlternate_WithASmallPitchAndLevelOffset()
    {
        var left = FootstepRules.Shape(FootGait.Walk, leftFoot: true, take: 1, pitchRoll: 0.5d, volumeRoll: 0.5d);
        var right = FootstepRules.Shape(FootGait.Walk, leftFoot: false, take: 1, pitchRoll: 0.5d, volumeRoll: 0.5d);
        Assert.Equal(1f - FootstepRules.FootPitchOffset, left.Pitch, 4);
        Assert.Equal(1f + FootstepRules.FootPitchOffset, right.Pitch, 4);
        Assert.Equal(1f - FootstepRules.FootVolumeOffset, left.Volume, 4);
        Assert.Equal(1f + FootstepRules.FootVolumeOffset, right.Volume, 4);

        var high = FootstepRules.Shape(FootGait.Walk, leftFoot: false, take: 1, pitchRoll: 0.999999d, volumeRoll: 0.999999d);
        var low = FootstepRules.Shape(FootGait.Walk, leftFoot: true, take: 1, pitchRoll: 0d, volumeRoll: 0d);
        Assert.InRange(high.Pitch, 1f, (1f + FootstepRules.FootPitchOffset) * (1f + FootstepRules.PitchJitter) + 1e-4f);
        Assert.InRange(low.Pitch, (1f - FootstepRules.FootPitchOffset) * (1f - FootstepRules.PitchJitter) - 1e-4f, 1f);
        Assert.InRange(high.Volume, 1f, (1f + FootstepRules.FootVolumeOffset) * (1f + FootstepRules.VolumeJitter) + 1e-4f);
        Assert.InRange(low.Volume, (1f - FootstepRules.FootVolumeOffset) * (1f - FootstepRules.VolumeJitter) - 1e-4f, 1f);
    }

    [Fact]
    public void TheCadence_StepsAtOnce_ThenOnRhythm_AlternatingFeet_NoTakeTwice()
    {
        var cadence = new FootstepCadence(new Random(7));
        var steps = new List<(float At, StepShape Step)>();
        float t = 0f;
        for (int i = 0; i < 240; i++) // four seconds of walking at 60 fps
        {
            if (cadence.Tick(Frame, walking: true, FootGait.Walk, Walk, Walk, out var step))
            {
                steps.Add((t, step));
            }

            t += Frame;
        }

        Assert.Equal(0f, steps[0].At);
        Assert.InRange(steps.Count, 8, 10); // 4 s / 0.45 s
        for (int i = 1; i < steps.Count; i++)
        {
            Assert.InRange(steps[i].At - steps[i - 1].At, FootstepRules.WalkInterval - (2 * Frame), FootstepRules.WalkInterval + (2 * Frame));
            Assert.NotEqual(steps[i - 1].Step.LeftFoot, steps[i].Step.LeftFoot);
            Assert.NotEqual(steps[i - 1].Step.Take, steps[i].Step.Take);
        }
    }

    [Fact]
    public void ABriefLift_KeepsTheRhythm_AStop_StartsTheNextWalkWithAStep()
    {
        var cadence = new FootstepCadence(new Random(3));
        Assert.True(cadence.Tick(Frame, true, FootGait.Walk, Walk, Walk, out _)); // the first step at once
        Assert.False(cadence.Tick(0.1f, false, FootGait.Walk, Walk, Walk, out _)); // a stair lifts the feet for 0.1 s
        Assert.False(cadence.Tick(Frame, true, FootGait.Walk, Walk, Walk, out _)); // …no extra step on touching down

        Assert.False(cadence.Tick(FootstepCadence.IdleReset + 0.01f, false, FootGait.Walk, Walk, Walk, out _)); // a real stop
        Assert.True(cadence.Tick(Frame, true, FootGait.Walk, Walk, Walk, out _)); // the next walk steps at once
    }

    [Fact]
    public void ALanding_PushesTheNextStepAStrideAway()
    {
        var cadence = new FootstepCadence(new Random(5));
        cadence.Landed();
        Assert.False(cadence.Tick(FootstepRules.WalkInterval - 0.05f, true, FootGait.Walk, Walk, Walk, out _));
        Assert.True(cadence.Tick(0.1f, true, FootGait.Walk, Walk, Walk, out _));
    }

    [Fact]
    public void ALongFrame_NeverFiresABurstOfSteps()
    {
        var cadence = new FootstepCadence(new Random(1));
        Assert.True(cadence.Tick(Frame, true, FootGait.Walk, Walk, Walk, out _));
        Assert.True(cadence.Tick(3f, true, FootGait.Walk, Walk, Walk, out _)); // a 3 s hitch: one step, not six
        Assert.False(cadence.Tick(Frame, true, FootGait.Walk, Walk, Walk, out _));
    }

    [Theory]
    [InlineData(2f, 1f, LandingTier.None)]    // a slope's flicker
    [InlineData(4.5f, 0.2f, LandingTier.None)] // a stair down: too short in the air to be a hop
    [InlineData(7f, 0.7f, LandingTier.Soft)]   // a jump on flat ground lands as fast as it took off
    [InlineData(6.3f, 0.32f, LandingTier.Soft)] // a one-block drop
    [InlineData(9.4f, 0.8f, LandingTier.Normal)] // a jump off a block
    [InlineData(12f, 0.6f, LandingTier.Heavy)]   // past 0.85 × the safe 14 m/s
    [InlineData(20f, 1.5f, LandingTier.Heavy)]   // a fall that hurts
    public void ALanding_IsPickedByTheFallSpeed(float fallSpeed, float airSeconds, LandingTier tier)
        => Assert.Equal(tier, FootstepRules.Landing(fallSpeed, 14f, airSeconds).Tier);

    [Fact]
    public void ALandingsClip_AndLevel_GrowWithTheFall()
    {
        var hop = FootstepRules.Landing(7f, 14f, 0.7f);
        var drop = FootstepRules.Landing(9.4f, 14f, 0.8f);
        var hard = FootstepRules.Landing(13f, 14f, 1f);
        var harder = FootstepRules.Landing(22f, 14f, 1.6f);
        Assert.Null(hop.Clip); // a hop is a footstep on the surface
        Assert.Equal("land", drop.Clip);
        Assert.Equal("land_heavy", hard.Clip);
        Assert.True(drop.Volume > hop.Volume - 0.1f && hard.Volume > drop.Volume && harder.Volume >= hard.Volume);
        Assert.True(harder.Volume <= 0.8f + 1e-4f, "never a bang");
    }

    [Fact]
    public void TheLandingTiers_ScaleWithTheWorldsSafeFallSpeed()
    {
        // A light world: the jump and the safe fall speed both shrink — a jump still lands as a hop, not a thud.
        Assert.Equal(LandingTier.Soft, FootstepRules.Landing(4.9f, 9.8f, 0.7f).Tier);
        Assert.Equal(LandingTier.Heavy, FootstepRules.Landing(9f, 9.8f, 1f).Tier);
        Assert.Equal(FootstepRules.Landing(7f, FootstepRules.DefaultSafeFallSpeed, 0.7f).Tier, FootstepRules.Landing(7f, 0f, 0.7f).Tier);
    }

    /// <summary>The Unity half, guarded as source text (the Unity scripts cannot load headless): the player's own feet go
    /// through the rules and <c>PlayOwn</c>, the landing by fall speed, the climber's taps through the takes, other players'
    /// steps through the remote-player budget.</summary>
    [Fact]
    public void TheClient_PlaysItsStepsThroughTheRules()
    {
        string player = ClientTestPaths.PlayerControllerSources();
        Assert.DoesNotContain("_stepTimer", player);
        Assert.DoesNotContain("Cue(\"land\"", player);
        Assert.Contains("MoveAudio(grounded, inWater, h, v, prevVy);", player);
        Assert.Contains("LandingAudio(prevVy);", player);
        Assert.Contains("FootstepRules.Landing(", player);
        Assert.Contains("FootstepSurface.TakeClip(", player);
        Assert.Contains(".PlayOwn(", player);
        Assert.Contains("PlayClimbTap(StepCueFor(", player);

        string remotes = ClientTestPaths.ScriptPartsText("RemotePlayers");
        Assert.Contains("TickRemoteSteps();", remotes);
        Assert.Contains("AudioCategory.RemotePlayer", remotes);
        Assert.Contains("FootstepRules.Landing(", remotes);
    }

    [Fact]
    public void EveryTakeOfEverySurface_IsBundled()
    {
        string audio = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        var cues = new[]
        {
            FootstepSurface.DefaultCue, FootstepSurface.MetalCue, FootstepSurface.WaterCue, FootstepSurface.WoodCue,
            FootstepSurface.LeavesCue, FootstepSurface.GrateCue, FootstepSurface.CrystalCue, "step_grass", "step_sand", "step_snow",
        };
        foreach (string cue in cues)
        {
            for (int take = 1; take <= FootstepRules.TakeCount; take++)
            {
                string id = FootstepSurface.TakeClip(cue, take, null);
                Assert.True(File.Exists(Path.Combine(audio, id + ".mp3")), $"{id}.mp3 is not bundled");
            }
        }

        foreach (string id in new[] { "land", "land_heavy" })
        {
            Assert.True(File.Exists(Path.Combine(audio, id + ".mp3")), $"{id}.mp3 is not bundled");
        }
    }
}
