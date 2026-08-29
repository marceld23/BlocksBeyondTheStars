// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The pure vertical mechanics (#1331): gravity falls, ballistic jumps that clear one block, the crawler's
/// haul-over, the hop beat, and the never-stuck timeout.
/// </summary>
public sealed class VerticalMotionTests
{
    private const float G = VerticalMotion.BaseGravity;
    private const double Dt = 1.0 / 15.0;

    [Fact]
    public void Fall_UnderGravity_LandsOnTheGround_AndStops()
    {
        var s = new VerticalState();
        float y = 70f; // three blocks above the floor
        int ticks = 0;
        while ((s.Airborne || y > 67f) && ticks++ < 200)
        {
            y = VerticalMotion.Ground(ref s, y, 67f, G, Dt);
        }

        Assert.Equal(67f, y, 3);
        Assert.False(s.Airborne);
        Assert.Equal(0f, s.VertVel);
        Assert.InRange(ticks, 8, 20); // ~0.55 s for a 3-block fall at g = 20 — a fall, not a 6 b/s elevator
    }

    [Fact]
    public void Jump_PeaksAtTheRequestedHeight_AndLandsOnTheHigherFloor()
    {
        var s = new VerticalState();
        float impulse = VerticalMotion.ImpulseFor(G, VerticalMotion.JumpHeight);
        VerticalMotion.Launch(ref s, impulse);

        float y = 64f, peak = 64f;
        int ticks = 0;
        while (s.Airborne && ticks++ < 200)
        {
            y = VerticalMotion.Ground(ref s, y, 65f, G, Dt); // the ledge's feet cell is one block up
            peak = System.Math.Max(peak, y);
        }

        Assert.True(peak >= 65.15f && peak <= 65.35f, $"peak {peak:F2} should be ~1.25 above the launch floor (64)");
        Assert.Equal(65f, y, 3); // landed on the ledge, not back on the old floor
        Assert.False(s.Airborne);
        Assert.InRange(ticks, 6, 16);
    }

    [Fact]
    public void JumpHeight_ScalesWithWorldGravity_LikeThePlayer()
    {
        // Lighter worlds jump proportionally higher; heavy worlds never below the base (a ledge stays clearable).
        Assert.Equal(VerticalMotion.JumpHeight, VerticalMotion.JumpHeightFor(1f), 4);
        Assert.Equal(VerticalMotion.JumpHeight * 2f, VerticalMotion.JumpHeightFor(0.5f), 4);
        Assert.Equal(VerticalMotion.JumpHeight, VerticalMotion.JumpHeightFor(1.6f), 4);
        Assert.True(VerticalMotion.Gravity(0.5f) < VerticalMotion.Gravity(1f));
    }

    [Fact]
    public void Climb_HaulsUpInPlace_WithoutGravity_ThenClears()
    {
        var s = new VerticalState();
        VerticalMotion.BeginClimb(ref s, 65f);
        float y = 64f;
        int ticks = 0;
        while (s.ClimbTargetY > 0f && ticks++ < 100)
        {
            y = VerticalMotion.Ground(ref s, y, 64f, G, Dt, riseRate: VerticalMotion.ClimbRate); // ground is still the OLD floor
            Assert.False(s.Airborne, "a haul-over never goes airborne");
        }

        Assert.Equal(65f, y, 3);
        Assert.InRange(ticks, 5, 10); // 1 block at 2.5 b/s ≈ 0.4 s — a visible effort, not a snap
    }

    [Fact]
    public void SubBlockRise_EasesUp_NoJump()
    {
        var s = new VerticalState();
        float y = VerticalMotion.Ground(ref s, 64.2f, 64.5f, G, Dt);
        Assert.False(s.Airborne);
        Assert.True(y > 64.2f && y <= 64.5f);
    }

    [Fact]
    public void AirborneTimeout_SnapsToTheProbe_NeverStuck()
    {
        // A creature launched over a column whose probe never comes back under it (unloaded chunk) is
        // snapped down after the timeout instead of drifting forever.
        var s = new VerticalState();
        VerticalMotion.Launch(ref s, 50f); // absurd impulse — would fly for seconds
        float y = 64f;
        int ticks = 0;
        while (s.Airborne && ticks++ < 100)
        {
            y = VerticalMotion.Ground(ref s, y, 64f, G, Dt);
        }

        Assert.Equal(64f, y, 3);
        Assert.False(s.Airborne);
        Assert.True(ticks * Dt <= VerticalMotion.AirborneTimeout + 0.2, "snapped at the timeout");
    }

    [Fact]
    public void HopBeat_FiresOnceOnTheUpwardZeroCrossing_OnlyWhileGrounded()
    {
        var s = new VerticalState();
        Assert.False(VerticalMotion.HopBeat(ref s, -0.5f));
        Assert.True(VerticalMotion.HopBeat(ref s, 0.2f));
        Assert.False(VerticalMotion.HopBeat(ref s, 0.8f)); // still positive — no second launch
        Assert.False(VerticalMotion.HopBeat(ref s, -0.3f));

        VerticalMotion.Launch(ref s, 3f);
        Assert.False(VerticalMotion.HopBeat(ref s, 0.5f)); // airborne — the beat waits for the landing
    }

    [Fact]
    public void GlideGravity_MakesTheSameJump_LastLonger()
    {
        // A ground bird (#1334) launches for the same height under 0.4 g, so its arc is longer and flatter.
        float gFull = G, gGlide = G * VerticalMotion.GlideGravityScale;
        int Ticks(float g)
        {
            var s = new VerticalState();
            VerticalMotion.Launch(ref s, VerticalMotion.ImpulseFor(g, VerticalMotion.JumpHeight));
            float y = 64f;
            int n = 0;
            while (s.Airborne && n++ < 200)
            {
                y = VerticalMotion.Ground(ref s, y, 64f, g, Dt);
            }

            return n;
        }

        Assert.True(Ticks(gGlide) > Ticks(gFull) * 1.3f, "the glide arc must last noticeably longer");
    }
}
