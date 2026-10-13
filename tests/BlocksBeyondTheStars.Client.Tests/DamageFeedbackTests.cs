// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2518: a scan drone's 2 HP/s aura arrives as a 0.13 HP drop on every 15 Hz health update, and each one used to fire
/// the full hit effect — a steady red screen and the camera shake stacked to its maximum. Damage is now summed over a
/// half-second window and scaled by the share of max health: chip damage is a faint edge glow, a real hit still fires
/// at once, and armour that keeps each drop tiny no longer silences the feedback.
/// </summary>
public sealed class DamageFeedbackTests
{
    private const float Max = DamageFeedback.PlayerMaxHealth;
    private const float Frame = 1f / 60f;
    private const float Tick = 1f / 15f;

    /// <summary>Plays <paramref name="seconds"/> of steady damage at <paramref name="hpPerSecond"/> with the server's
    /// 15 Hz health updates seen through 60 fps frames, and returns every pulse that fired.</summary>
    private static List<DamagePulse> Run(DamageFeedback fb, float startHealth, float hpPerSecond, float seconds)
    {
        var pulses = new List<DamagePulse>();
        float t = 0f, health = startHealth, nextTick = Tick;
        fb.Update(health, Max, 0f, out _);
        while (t < seconds - 1e-4f)
        {
            t += Frame;
            if (t >= nextTick - 1e-4f)
            {
                health -= hpPerSecond * Tick; // the server's health update lands on this frame
                nextTick += Tick;
            }

            if (fb.Update(health, Max, Frame, out var pulse))
            {
                pulses.Add(pulse);
            }
        }

        return pulses;
    }

    [Fact]
    public void ChipDamage_FromADrone_PulsesAboutTwiceASecond_WithoutFlashOrShake()
    {
        var fb = new DamageFeedback();
        var pulses = Run(fb, 100f, 2f, 3f);

        // 3 s of damage → the immediate first pulse plus one per closed half-second window (not 45 updates).
        Assert.InRange(pulses.Count, 5, 7);
        Assert.All(pulses, p =>
        {
            Assert.False(p.FullScreenFlash);   // 1 HP per window is far below 5 % of max health
            Assert.InRange(p.Vignette, 0.01f, 0.3f); // a little red at the edge — the tester's "a little red is enough"
            Assert.True(p.Trauma < 0.02f);    // a shake that small is invisible (the shake is trauma²)
        });

        float total = 0f;
        foreach (var p in pulses)
        {
            total += p.Damage;
        }

        // Every HP lost is accounted for: fired, or held in the window that is still open.
        Assert.Equal(6f, total + fb.PendingDamage, 2);
    }

    [Fact]
    public void ChipDamage_TraumaNeverBuildsUp_UnderTheCameraDecay()
    {
        var pulses = Run(new DamageFeedback(), 100f, 2f, 10f);

        // FxCamera decays trauma by 1.1 per second; replay that between the pulses.
        float trauma = 0f, peak = 0f;
        foreach (var p in pulses)
        {
            trauma = System.Math.Max(0f, trauma - (1.1f * DamageFeedback.WindowSeconds));
            trauma = DamageFeedback.AddCappedTrauma(trauma, p.Trauma);
            peak = System.Math.Max(peak, trauma);
        }

        Assert.True(peak < 0.05f, $"chip damage built the shake trauma up to {peak}");
    }

    [Fact]
    public void ABigHit_FiresAtOnce_WithTheFullScreenFlash()
    {
        var fb = new DamageFeedback();
        fb.Update(100f, Max, Frame, out _);

        Assert.True(fb.Update(80f, Max, Frame, out var pulse));
        Assert.True(pulse.FullScreenFlash);
        Assert.Equal(20f, pulse.Damage, 3);
        Assert.Equal(0.2f, pulse.Share, 3);
    }

    [Fact]
    public void ABigHit_BreaksThroughAnOpenWindow_AndTakesTheHeldChipDamageAlong()
    {
        var fb = new DamageFeedback();
        fb.Update(100f, Max, Frame, out _);
        Assert.True(fb.Update(99.8f, Max, Frame, out _)); // the first chip fires at once and opens the window
        Assert.False(fb.Update(99.6f, Max, Frame, out _)); // held
        Assert.Equal(0.2f, fb.PendingDamage, 3);

        Assert.True(fb.Update(89.6f, Max, Frame, out var pulse)); // a 10 HP bite while the window is open
        Assert.True(pulse.FullScreenFlash);
        Assert.Equal(10.2f, pulse.Damage, 3);
        Assert.Equal(0f, fb.PendingDamage);
    }

    [Fact]
    public void ArmouredChipDamage_BelowTheOldThreshold_StillGivesFeedback()
    {
        // 0.04 HP per update used to fall under the 0.05 HP trigger and showed nothing at all.
        var pulses = Run(new DamageFeedback(), 100f, 0.04f * 15f, 2f);

        Assert.NotEmpty(pulses);
        Assert.All(pulses, p => Assert.False(p.FullScreenFlash));
    }

    [Fact]
    public void HealingAndTheFirstSample_NeverFire()
    {
        var fb = new DamageFeedback();

        Assert.False(fb.Update(40f, Max, Frame, out _)); // joining with 40 HP is not a 60 HP hit
        Assert.False(fb.Update(41f, Max, Frame, out _));
        Assert.False(fb.Update(100f, Max, Frame, out _)); // respawn
        Assert.Equal(0f, fb.PendingDamage);
    }

    [Fact]
    public void Reset_DropsHeldDamage_AndRebaselines()
    {
        var fb = new DamageFeedback();
        fb.Update(50f, Max, Frame, out _);
        fb.Update(49.9f, Max, Frame, out _); // fires, opens the window
        fb.Update(49.8f, Max, Frame, out _); // held

        fb.Reset(100f);

        Assert.Equal(0f, fb.PendingDamage);
        Assert.False(fb.Update(100f, Max, Frame, out _));
        Assert.True(fb.Update(99.9f, Max, Frame, out _)); // a fresh window: the next drop fires at once
    }

    [Theory]
    [InlineData(4.9f, false)]
    [InlineData(5f, true)]
    [InlineData(30f, true)]
    public void TheFullScreenFlash_StartsAtFivePercentOfMaxHealth(float damage, bool flash)
    {
        Assert.Equal(flash, DamageFeedback.Scale(damage, Max).FullScreenFlash);
    }

    [Fact]
    public void Scale_HasNoFixedMinimums_AndGrowsWithTheHit()
    {
        var none = DamageFeedback.Scale(0f, Max);
        Assert.Equal(0f, none.Vignette);
        Assert.Equal(0f, none.VisorGlitch);
        Assert.Equal(0f, none.Trauma);
        Assert.Equal(0f, none.FlashSeconds);

        var tiny = DamageFeedback.Scale(0.05f, Max);
        Assert.True(tiny.Vignette < 0.06f);
        Assert.True(tiny.VisorGlitch < 0.01f);
        Assert.True(tiny.Trauma < 0.001f);

        DamagePulse prev = none;
        foreach (float hp in new[] { 0.5f, 1f, 3f, 5f, 10f, 20f, 40f })
        {
            var p = DamageFeedback.Scale(hp, Max);
            Assert.True(p.Vignette >= prev.Vignette && p.VisorGlitch >= prev.VisorGlitch && p.Trauma >= prev.Trauma);
            Assert.True(p.FlashSeconds >= prev.FlashSeconds);
            prev = p;
        }
    }

    [Fact]
    public void Scale_FollowsTheShareOfMaxHealth_NotTheRawHp()
    {
        Assert.Equal(DamageFeedback.Scale(10f, 100f).Vignette, DamageFeedback.Scale(20f, 200f).Vignette, 4);
        Assert.Equal(DamageFeedback.Scale(10f, 0f).Vignette, DamageFeedback.Scale(10f, 100f).Vignette, 4); // no max → 100
    }

    [Fact]
    public void DamageShake_IsCappedAtPointThree_ButNeverLowersAnExplosion()
    {
        Assert.True(DamageFeedback.Scale(100f, Max).Trauma <= DamageFeedback.MaxTrauma + 1e-6f);

        float trauma = 0f;
        for (int i = 0; i < 20; i++)
        {
            trauma = DamageFeedback.AddCappedTrauma(trauma, DamageFeedback.Scale(25f, Max).Trauma);
        }

        Assert.Equal(DamageFeedback.MaxTrauma, trauma, 4);
        Assert.Equal(0.8f, DamageFeedback.AddCappedTrauma(0.8f, 0.3f), 4); // an explosion's trauma stays
        Assert.Equal(0.25f, DamageFeedback.AddCappedTrauma(0.2f, 0.05f), 4);
    }
}
