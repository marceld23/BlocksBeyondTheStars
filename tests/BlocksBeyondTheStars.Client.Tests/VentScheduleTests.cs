// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3275/#3276: the geyser vents' random eruption schedule — deterministic per vent, intervals in range, varying
/// strength, one clock, and no eruption storm after a long frame or a jump of the clock.</summary>
public sealed class VentScheduleTests
{
    private static List<(float Time, float Strength)> Run(VentSchedule s, float from, float to, float step)
    {
        var fired = new List<(float, float)>();
        for (float t = from; t <= to; t += step)
        {
            if (s.Poll(t, out float strength))
            {
                fired.Add((t, strength));
            }
        }

        return fired;
    }

    [Fact]
    public void TheSameVent_EruptsAtTheSameTimes_AndAnotherVentKeepsItsOwnRhythm()
    {
        var a = Run(new VentSchedule(120, 64, -33), 0f, 600f, 1f / 30f);
        var b = Run(new VentSchedule(120, 64, -33), 0f, 600f, 1f / 30f);
        var other = Run(new VentSchedule(121, 64, -33), 0f, 600f, 1f / 30f);

        Assert.Equal(a, b);
        Assert.NotEmpty(a);
        Assert.NotEqual(a.Select(e => e.Time), other.Select(e => e.Time));
    }

    [Fact]
    public void Intervals_StayBetweenTheLimits_AndVary()
    {
        var gaps = new List<float>();
        for (int v = 0; v < 40; v++)
        {
            var s = new VentSchedule(v * 37, 50 + v, v * -11);
            for (long slot = 0; slot < 60; slot++)
            {
                gaps.Add(s.EruptionInSlot(slot + 1).Time - s.EruptionInSlot(slot).Time);
            }
        }

        Assert.All(gaps, g => Assert.InRange(g, VentSchedule.MinInterval, VentSchedule.MaxInterval));
        Assert.True(gaps.Max() - gaps.Min() > 20f, $"gaps should vary widely, saw {gaps.Min():F1}..{gaps.Max():F1}");
        Assert.InRange(gaps.Average(), VentSchedule.SlotSeconds - 1.5f, VentSchedule.SlotSeconds + 1.5f);
    }

    [Fact]
    public void Strength_StaysInRange_AndVaries_WithBigEruptionsRarer()
    {
        var s = new VentSchedule(7, 70, 7);
        var strengths = Enumerable.Range(0, 400).Select(i => s.EruptionInSlot(i).Strength).ToList();

        Assert.All(strengths, x => Assert.InRange(x, VentSchedule.MinStrength, 1f));
        Assert.True(strengths.Max() - strengths.Min() > 0.4f);
        int big = strengths.Count(x => x > 0.85f), small = strengths.Count(x => x < 0.55f);
        Assert.True(small > big, $"small {small} vs big {big}: the big eruptions should be the rare ones");
    }

    [Fact]
    public void SteadyFrames_FireEveryEruptionExactlyOnce()
    {
        var s = new VentSchedule(-5, 80, 900);
        var fired = Run(s, 0f, 1200f, 1f / 60f);

        var reference = new VentSchedule(-5, 80, 900);
        var expected = Enumerable.Range(0, 60).Select(i => reference.EruptionInSlot(i).Time).Where(t => t > 0f && t <= 1200f).ToList();
        Assert.Equal(expected.Count, fired.Count);
        for (int i = 0; i < fired.Count; i++)
        {
            Assert.InRange(fired[i].Time - expected[i], 0f, 1f / 60f + 1e-3f);
        }
    }

    [Fact]
    public void ALongFrameOrAClockJump_FiresAtMostOnce_AndOnlyAFreshEruption()
    {
        var s = new VentSchedule(3, 3, 3);
        Assert.False(s.Poll(10f, out _)); // the first call only arms the schedule

        // A ten-minute jump in one frame: dozens of eruptions lay in between — none may fire as a backlog.
        var latest = s.LatestAtOrBefore(610f);
        bool fired = s.Poll(610f, out _);
        Assert.Equal(610f - latest.Time <= VentSchedule.MaxLateness, fired);

        // Polling on from there: one eruption per slot, never two within the shortest interval.
        var after = Run(s, 610f + 1f / 30f, 1210f, 1f / 30f);
        for (int i = 1; i < after.Count; i++)
        {
            Assert.True(after[i].Time - after[i - 1].Time >= VentSchedule.MinInterval - 0.05f);
        }
    }

    [Fact]
    public void AClockThatRunsBackwards_ReArms_WithoutFiring()
    {
        var s = new VentSchedule(11, 12, 13);
        Run(s, 0f, 500f, 0.5f);

        Assert.False(s.Poll(2f, out _)); // a new world's clock starts again near zero
        var next = s.NextAfter(2f);
        bool any = false;
        for (float t = 2f; t < next.Time + 0.5f; t += 0.1f)
        {
            any |= s.Poll(t, out _);
        }

        Assert.True(any, "after re-arming, the next eruption fires normally");
    }

    [Fact]
    public void NonFiniteClocks_AreIgnored()
    {
        var s = new VentSchedule(1, 2, 3);
        Assert.False(s.Poll(float.NaN, out _));
        Assert.False(s.Poll(float.PositiveInfinity, out _));
        Assert.False(s.Poll(5f, out _)); // still the arming call
    }
}
