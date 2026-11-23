// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3860: swimming no longer bounces. The climb-out hop fires only where a bank stands in front, the body floats at the
/// surface whether Jump is held or not and the Descend key dives. #3923: the float settles with the head point the server
/// breathes at (+1.5) above the water and the chest (+1.1) in it. #3924: the body tilts with the swim — upright treading
/// water, forward in a crawl, nearly level in a sprint, head first in a dive — and the short controls line names the swim
/// keys. #3956: the surface needs open air over the head — a ceiling over flooded water is none, so a tunnel full of water
/// sinks the swimmer instead of hanging it under the ceiling.
/// </summary>
public sealed class SwimRulesTests
{
    private const float Up = 4f;
    private const float Sink = 1.5f;
    private const float SwimAccel = 12f;

    /// <summary>The water's surface in the simulated pool: every cell below it is water.</summary>
    private const float Surface = 62f;

    [Fact]
    public void OpenWater_NeverHops_WithJumpAndForwardHeld()
    {
        Assert.False(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: true, ledgeAhead: false, bankAhead: false));
    }

    [Fact]
    public void ABank_InFront_MountsTheLand()
    {
        Assert.True(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: false, ledgeAhead: true, bankAhead: true)); // swim into a low bank
        Assert.True(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: true, ledgeAhead: false, bankAhead: true));  // Jump at a higher face
        Assert.False(SwimRules.ClimbOutHop(pushing: true, atSurface: true, jumpHeld: false, ledgeAhead: false, bankAhead: true)); // a face without Jump
        Assert.False(SwimRules.ClimbOutHop(pushing: true, atSurface: false, jumpHeld: true, ledgeAhead: true, bankAhead: true));  // under water
        Assert.False(SwimRules.ClimbOutHop(pushing: false, atSurface: true, jumpHeld: true, ledgeAhead: true, bankAhead: true));  // not moving
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtTheFloatDepth_TheBodyHolds_JumpOrNot(bool jump)
    {
        Assert.Equal(0f, SwimRules.VerticalTarget(atSurface: true, jumpHeld: jump, diveHeld: false, Up, Sink));
        Assert.Equal(0f, SwimRules.VerticalTarget(atSurface: true, jumpHeld: jump, diveHeld: false, Up, Sink, SwimRules.FloatDepth));
    }

    [Fact]
    public void BelowTheSurface_JumpRises_AndNothingHeldSinksSlowly()
    {
        Assert.Equal(Up, SwimRules.VerticalTarget(atSurface: false, jumpHeld: true, diveHeld: false, Up, Sink));
        Assert.Equal(-Sink, SwimRules.VerticalTarget(atSurface: false, jumpHeld: false, diveHeld: false, Up, Sink));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Descend_Dives_EvenAtTheSurfaceAndOverJump(bool atSurface, bool jump)
    {
        Assert.Equal(-SwimRules.DiveSpeed, SwimRules.VerticalTarget(atSurface, jump, diveHeld: true, Up, Sink, feetDepth: 1.8f));
    }

    [Fact]
    public void TheFloat_SettlesStifferThanTheSwim_SoARiseDoesNotPopOut()
    {
        Assert.Equal(SwimRules.SurfaceAccel, SwimRules.VerticalAccel(atSurface: true, diveHeld: false, swimAccel: 12f));
        Assert.Equal(12f, SwimRules.VerticalAccel(atSurface: false, diveHeld: false, swimAccel: 12f));
        Assert.Equal(12f, SwimRules.VerticalAccel(atSurface: true, diveHeld: true, swimAccel: 12f));

        // A rise at the swim-up speed stops within the band where the chest is wet and the head is dry (~0.8 m).
        float stop = Up * Up / (2f * SwimRules.SurfaceAccel);
        Assert.True(stop < 0.4f, $"a rise overshoots the waterline by {stop} m");
    }

    [Fact]
    public void TheFloatDepth_KeepsTheBreathPointDry_AndTheChestWet()
    {
        // #3923: the server breathes at the feet + 1.5 (GameServer.HeadUnderwater), the client swims while the feet + 1.1
        // are in the water and the first-person eye sits at + 1.6.
        float feet = Surface - SwimRules.FloatDepth;
        Assert.True(feet + 1.5f >= Surface + 0.15f, $"the breath point is only {feet + 1.5f - Surface} m above the water");
        Assert.True(feet + 1.6f > Surface, "the first-person eye floats under the water");
        Assert.True(feet + 1.1f <= Surface - 0.15f, $"the chest is only {Surface - (feet + 1.1f)} m under the water — a bob leaves it");

        // The old resting point the report measured (feet 1.58 under the surface) had the breath point 8 cm under water; the
        // float now lifts the body from there.
        Assert.True(Surface - 1.58f + 1.5f < Surface);
        Assert.True(SwimRules.FloatTarget(1.58f) > 0f);
        Assert.True(SwimRules.FloatTarget(1.15f) < 0f); // riding too high: back down, before the chest leaves the water
        Assert.Equal(SwimRules.FloatMaxSpeed, SwimRules.FloatTarget(5f));
    }

    [Theory]
    [InlineData(4f, true)]     // swimming up from below with Jump held
    [InlineData(1.85f, false)] // resting where the old float stopped (the top of the head just out)
    [InlineData(1.58f, false)] // the reported resting point (the breath point 8 cm under water)
    [InlineData(1.15f, false)] // riding high, the chest nearly out: back down to the float
    public void ARiseFromBelow_SettlesAtTheFloat_HeadDryChestWet_NoPopOut(float startDepth, bool jump)
    {
        var run = Simulate(Surface - startDepth, jump, dive: false, seconds: 5f);

        Assert.False(run.LeftTheWater, "the chest left the water on the way up — the body would bounce");
        Assert.True(run.Feet + 1.5f > Surface + 0.1f, $"the head point settled {run.Feet + 1.5f - Surface:0.00} m off the water");
        Assert.True(run.Feet + 1.1f < Surface - 0.1f, $"the chest settled {Surface - (run.Feet + 1.1f):0.00} m under the water");
        Assert.InRange(Surface - run.Feet, SwimRules.FloatDepth - 0.05f, SwimRules.FloatDepth + 0.05f);
        Assert.InRange(run.Speed, -0.05f, 0.05f);
    }

    [Fact]
    public void Diving_FromTheFloat_StillSinks()
    {
        var run = Simulate(Surface - SwimRules.FloatDepth, jump: false, dive: true, seconds: 1.5f);
        Assert.True(Surface - run.Feet > 4f, $"a dive of 1.5 s only reached {Surface - run.Feet:0.00} m");
    }

    [Fact]
    public void TreadingWater_StaysUpright()
    {
        Assert.InRange(SwimRules.BodyTilt(0f, 0f), 0f, 10f);
        Assert.InRange(SwimRules.BodyTilt(0.4f, 0f), 0f, 10f);
        Assert.InRange(SwimRules.BodyTilt(0f, -Sink), 0f, 15f); // the idle sink is no dive
        Assert.Equal(1f, SwimRules.TreadBlend(SwimRules.BodyTilt(0f, 0f)));
        Assert.Equal(0f, SwimRules.CrawlBlend(SwimRules.BodyTilt(0f, 0f)));
    }

    [Fact]
    public void ThePlainSwim_LiesForward_InACrawl()
    {
        const float walk = 6f, swimMul = 0.62f;
        float tilt = SwimRules.BodyTilt(walk * swimMul, 0f);
        Assert.InRange(tilt, 58f, 66f);
        Assert.True(SwimRules.CrawlBlend(tilt) > 0.95f, "the plain swim is a crawl");
        Assert.Equal(0f, SwimRules.TreadBlend(tilt));
    }

    [Fact]
    public void ASlowSwim_IsABreastStroke()
    {
        float tilt = SwimRules.BodyTilt(1.6f, 0f);
        Assert.InRange(tilt, 15f, 45f);
        Assert.True(SwimRules.CrawlBlend(tilt) < 0.5f && SwimRules.TreadBlend(tilt) < 0.5f, $"a slow swim at {tilt}° is no breast stroke");
    }

    [Fact]
    public void TheSprintSwim_LiesNearlyLevel()
    {
        float tilt = SwimRules.BodyTilt(6f * 0.62f * SprintState.SpeedFactor, 0f);
        Assert.InRange(tilt, 72.5f, 80f);
        Assert.InRange(SwimRules.BodyTilt(12f, 0f), 72.5f, 80f); // a faster figure (a measured spike) lies no further
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(3.7f)]
    [InlineData(5.6f)]
    public void ADive_GoesHeadFirst(float forward)
    {
        Assert.True(SwimRules.BodyTilt(forward, -SwimRules.DiveSpeed) > 90f);
    }

    [Fact]
    public void SwimmingBackwards_LeansBackALittle_AndARiseBringsTheHeadUp()
    {
        float back = SwimRules.BodyTilt(-3.7f, 0f);
        Assert.InRange(back, -15f, 0f);
        Assert.True(SwimRules.BodyTilt(3.7f, Up) <= SwimRules.RiseTilt);
    }

    [Fact]
    public void TheTilt_GrowsSmoothlyWithTheSpeed()
    {
        float previous = SwimRules.BodyTilt(0f, 0f);
        for (float speed = 0.1f; speed <= 7f; speed += 0.1f)
        {
            float tilt = SwimRules.BodyTilt(speed, 0f);
            Assert.True(tilt >= previous - 0.001f, $"the tilt falls at {speed} m/s");
            Assert.True(tilt - previous < 3.5f, $"the tilt jumps {tilt - previous}° at {speed} m/s");
            previous = tilt;
        }
    }

    [Fact]
    public void TheShortLine_TradesItsMiddleForTheSwimKeys()
    {
        const string keys = "Space up · Ctrl dive";
        Assert.Equal("WASD move · Space up · Ctrl dive · Tab menu", SwimRules.SwimControlsLine("WASD move · Space jump · E use · Tab menu", keys));
        Assert.Equal("A · Space up · Ctrl dive · B", SwimRules.SwimControlsLine("A · B", keys));
        Assert.Equal("WASD move · Space up · Ctrl dive", SwimRules.SwimControlsLine("WASD move", keys));
        Assert.Equal("WASD move · E use", SwimRules.SwimControlsLine("WASD move · E use", null));
        Assert.Equal(string.Empty, SwimRules.SwimControlsLine(string.Empty, keys));
    }

    [Theory]
    [InlineData("en", "ui.hud.hint_short")]
    [InlineData("en", "ui.hud.hint_short_pad")]
    [InlineData("de", "ui.hud.hint_short")]
    [InlineData("de", "ui.hud.hint_short_pad")]
    public void TheSwimmingShortLine_StillNamesFourKeys(string code, string shortKey)
    {
        var table = VegaLineBudgetTests.Core(code);
        string line = SwimRules.SwimControlsLine(table[shortKey], table["ui.hud.hint_swim"]);
        Assert.Equal(4, line.Split(" · ").Length);
        Assert.Contains("{key:Jump}", line);
        Assert.Contains("{key:Crouch}", line);
    }

    [Theory]
    [InlineData(false, false, true)]  // open air (or a lily pad, a torch: walk-through props have no collider)
    [InlineData(true, false, false)]  // water, another liquid, a submerged kelp stalk
    [InlineData(false, true, false)]  // a ceiling over flooded water (#3956)
    [InlineData(true, true, false)]
    public void TheSurface_NeedsOpenAirOverTheHead(bool aboveLiquid, bool aboveColliding, bool surface)
    {
        Assert.Equal(surface, SwimRules.OpenSurface(aboveLiquid, aboveColliding));
    }

    [Fact]
    public void AFloodedTunnel_SinksToItsFloor_InsteadOfHangingUnderTheCeiling()
    {
        // #3956, the reported tunnel: water in the cells 59–61, stone at 62 and at 58; the feet at 60.17.
        float feet = TunnelSwim(60.17f, waterFrom: 59, ceiling: 62, jump: false, seconds: 3f);
        Assert.InRange(feet, 59f, 59.05f);
    }

    [Fact]
    public void AFloodedTunnel_JumpStillRises_UpToTheCeiling()
    {
        float feet = TunnelSwim(59f, waterFrom: 59, ceiling: 62, jump: true, seconds: 3f);
        Assert.True(feet > 60f, $"Jump only reached {feet:0.00}");
    }

    [Fact]
    public void AnAirGapUnderTheCeiling_StillFloats()
    {
        // Water in 59–60, air in 61, stone at 62: the head finds open air, so the body floats at the gap's waterline.
        float feet = TunnelSwim(59.95f, waterFrom: 59, ceiling: 62, jump: false, seconds: 5f, waterTop: 61);
        Assert.InRange(61f - feet, SwimRules.FloatDepth - 0.05f, SwimRules.FloatDepth + 0.05f);
    }

    /// <summary>A swim in a tunnel: water from <paramref name="waterFrom"/> up to (not including) <paramref name="waterTop"/>
    /// (default: the ceiling), air between, a solid ceiling at <paramref name="ceiling"/> and a solid floor under the water.
    /// The capsule stands 1.8 high and stops at the floor and the ceiling, as the character controller does.</summary>
    private static float TunnelSwim(float feet, int waterFrom, int ceiling, bool jump, float seconds, int waterTop = -1)
    {
        const float dt = 1f / 60f, height = 1.8f;
        int top = waterTop < 0 ? ceiling : waterTop;
        bool Water(float y) => System.MathF.Floor(y) >= waterFrom && System.MathF.Floor(y) < top;
        bool Solid(float y) => System.MathF.Floor(y) >= ceiling || System.MathF.Floor(y) < waterFrom;
        float v = 0f;
        for (int i = 0; i < (int)(seconds / dt); i++)
        {
            float head = feet + 1.9f;
            bool atSurface = SwimRules.OpenSurface(aboveLiquid: Water(head), aboveColliding: Solid(head));
            float target = SwimRules.VerticalTarget(atSurface, jump, diveHeld: false, Up, Sink, top - feet);
            v = MoveTowards(v, target, SwimRules.VerticalAccel(atSurface, false, SwimAccel) * dt);
            feet = System.Math.Clamp(feet + (v * dt), waterFrom, ceiling - height);
        }

        return feet;
    }

    private readonly record struct Run(float Feet, float Speed, bool LeftTheWater);

    /// <summary>The controller's water branch (PlayerController.Movement), cell for cell: the chest's cell (+1.1) in water
    /// swims, an open cell over the head (+1.9) is the surface, and out of the water gravity pulls.</summary>
    private static Run Simulate(float feet, bool jump, bool dive, float seconds)
    {
        const float dt = 1f / 60f, gravity = 20f;
        float v = 0f;
        bool reachedSurface = false, left = false;
        for (int i = 0; i < (int)(seconds / dt); i++)
        {
            bool swimming = System.MathF.Floor(feet + 1.1f) < Surface;
            if (swimming)
            {
                bool atSurface = System.MathF.Floor(feet + 1.9f) >= Surface;
                reachedSurface |= atSurface;
                float target = SwimRules.VerticalTarget(atSurface, jump, dive, Up, Sink, Surface - feet);
                v = MoveTowards(v, target, SwimRules.VerticalAccel(atSurface, dive, SwimAccel) * dt);
            }
            else
            {
                left |= reachedSurface;
                v -= gravity * dt;
            }

            feet += v * dt;
        }

        return new Run(feet, v, left);
    }

    private static float MoveTowards(float current, float target, float maxDelta)
        => System.MathF.Abs(target - current) <= maxDelta ? target : current + (System.MathF.Sign(target - current) * maxDelta);
}
