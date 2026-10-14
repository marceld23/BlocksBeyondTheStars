// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2562: the camera's weapon kick was a semi-implicit Euler spring that blew up for frames longer than ~0.064 s —
/// after a shot and a few slow frames (a weak laptop streaming chunks) the view wobbled up and down without end. The
/// spring is now stepped exactly (closed form): any frame time lands on one curve, the kick only fades, and at
/// 60 FPS it is the very kick players knew.
/// </summary>
public sealed class CameraKickSpringTests
{
    private const float Kick = 2.2f; // the heaviest gun kick in FxShots

    /// <summary>The old FxCamera step, kept as the reference for the 60 FPS feel and for the bug.</summary>
    private static float[] LegacyEuler(float degrees, float dt, int steps)
    {
        float x = 0f, v = degrees * 18f;
        var xs = new float[steps];
        for (int i = 0; i < steps; i++)
        {
            v += ((-x * 220f) - (v * 24f)) * dt;
            x += v * dt;
            xs[i] = x;
        }

        return xs;
    }

    private static float[] Run(float degrees, float dt, int steps)
    {
        var spring = new CameraKickSpring();
        spring.Kick(degrees);
        var xs = new float[steps];
        for (int i = 0; i < steps; i++)
        {
            xs[i] = spring.Step(dt);
        }

        return xs;
    }

    /// <summary>The true peak of the kick's curve (sampled finely).</summary>
    private static float TruePeak(float degrees) => Run(degrees, 1e-4f, 3000).Max();

    [Fact]
    public void At60FpsTheKickMatchesTheOldFeel()
    {
        float[] legacy = LegacyEuler(Kick, 1f / 60f, 60);
        float[] now = Run(Kick, 1f / 60f, 60);
        for (int i = 0; i < legacy.Length; i++)
        {
            Assert.True(Math.Abs(legacy[i] - now[i]) < 1e-4f, $"frame {i}: legacy {legacy[i]} vs now {now[i]}");
        }

        Assert.InRange(now.Max(), 0.31f * Kick, 0.34f * Kick); // ~0.32° of lift per degree of kick
    }

    [Theory]
    [InlineData(0.07f)]  // just past the old stability limit (~0.064 s)
    [InlineData(0.1f)]
    [InlineData(0.333f)] // Unity's longest frame (maximumDeltaTime)
    [InlineData(1f)]
    public void SlowFramesOnlyLetTheKickFade(float dt)
    {
        float peak = TruePeak(Kick);
        float[] now = Run(Kick, dt, 50);
        Assert.All(now, x => Assert.True(Math.Abs(x) <= peak * 1.001f, $"|{x}| exceeds the peak {peak} at dt={dt}"));
        Assert.True(Math.Abs(now[^1]) < 1e-4f, $"still {now[^1]}° after 50 frames of {dt}s");

        // The bug this pins: the old step oscillated and grew at the same frame time.
        float[] legacy = LegacyEuler(Kick, dt, 50);
        Assert.True(Math.Abs(legacy[^1]) > 100f * peak || float.IsNaN(legacy[^1]) || float.IsInfinity(legacy[^1]));
    }

    [Theory]
    [InlineData(1f / 30f)]
    [InlineData(1f / 60f)]
    [InlineData(1f / 144f)]
    [InlineData(1f / 240f)]
    public void ThePeakIsTheSameAtAnyFrameRate(float dt)
    {
        float at60 = Run(Kick, 1f / 60f, 60).Max();
        float here = Run(Kick, dt, (int)(1f / dt)).Max();
        Assert.True(Math.Abs(here - at60) <= at60 * 0.03f, $"peak {here} at dt={dt} vs {at60} at 60 FPS");
    }

    [Fact]
    public void EveryFrameTimeLandsOnTheSameCurve()
    {
        // 0.2 s as 6 frames of 1/30 s and as 24 frames of 1/120 s: the same offset.
        float coarse = Run(Kick, 1f / 30f, 6)[^1];
        float fine = Run(Kick, 1f / 120f, 24)[^1];
        Assert.True(Math.Abs(coarse - fine) < 1e-4f, $"{coarse} vs {fine}");
    }

    [Fact]
    public void NonFiniteInputResetsTheSpring()
    {
        var spring = new CameraKickSpring();
        spring.Kick(Kick);
        spring.Step(1f / 60f);
        Assert.False(spring.AtRest);

        spring.Kick(float.NaN);
        Assert.True(spring.AtRest);
        Assert.Equal(0f, spring.Step(1f / 60f));

        spring.Kick(Kick);
        spring.Step(1f / 60f);
        Assert.Equal(0f, spring.Step(float.NaN));
        Assert.True(spring.AtRest);

        spring.Kick(float.PositiveInfinity);
        Assert.True(spring.AtRest);
        Assert.Equal(0f, spring.Step(float.PositiveInfinity));
    }

    [Fact]
    public void KicksAndStateStayInsideTheirBounds()
    {
        var huge = new CameraKickSpring();
        huge.Kick(1e9f);
        var max = new CameraKickSpring();
        max.Kick(CameraKickSpring.MaxKickDegrees);
        Assert.Equal(max.Velocity, huge.Velocity); // one kick is clamped to MaxKickDegrees

        var spray = new CameraKickSpring();
        for (int i = 0; i < 1000; i++)
        {
            spray.Kick(CameraKickSpring.MaxKickDegrees);
            Assert.InRange(spray.Velocity, -CameraKickSpring.MaxVelocity, CameraKickSpring.MaxVelocity);
            spray.Step(1f / 600f);
            Assert.InRange(spray.Offset, -CameraKickSpring.MaxOffset, CameraKickSpring.MaxOffset);
        }
    }

    [Fact]
    public void ASettledKickIsExactlyAtRest()
    {
        var spring = new CameraKickSpring();
        spring.Kick(Kick);
        for (int i = 0; i < 120; i++)
        {
            spring.Step(1f / 60f);
        }

        Assert.True(spring.AtRest);
        Assert.Equal(0f, spring.Offset);
    }

    [Fact]
    public void AZeroOrNegativeStepChangesNothing()
    {
        var spring = new CameraKickSpring();
        spring.Kick(Kick);
        float offset = spring.Step(1f / 60f);
        float velocity = spring.Velocity;
        Assert.Equal(offset, spring.Step(0f));
        Assert.Equal(offset, spring.Step(-0.5f));
        Assert.Equal(velocity, spring.Velocity);
    }
}
