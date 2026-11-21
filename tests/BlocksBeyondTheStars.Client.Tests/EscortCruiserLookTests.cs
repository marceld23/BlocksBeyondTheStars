// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3900: the escort cruiser's look in numbers — how the flight view tells the boss from a plain cruiser, how its sizes
/// follow the entity's scale, that the model really carries the player idea's picture (a row of windows along the bridge,
/// a long barrel above them whose front end is the muzzle), and the mega laser's aiming line: it pulses faster toward the
/// shot and is strongest for the pilot it points at.
/// </summary>
public sealed class EscortCruiserLookTests
{
    [Fact]
    public void TheBoss_IsACruiserABossStatusNames_OrOneAsBigAsTheBoss()
    {
        Assert.True(EscortCruiserLook.IsEscortCruiser("Cruiser", EscortCruiserRules.ModelScale, namedByBossStatus: false));
        Assert.True(EscortCruiserLook.IsEscortCruiser("Cruiser", 1f, namedByBossStatus: true));
        Assert.False(EscortCruiserLook.IsEscortCruiser("Cruiser", 1f, namedByBossStatus: false)); // the finale's plain cruiser
        Assert.False(EscortCruiserLook.IsEscortCruiser("Cruiser", 0f, namedByBossStatus: false)); // an older server: no scale
        Assert.False(EscortCruiserLook.IsEscortCruiser("SpaceStation", 8f, namedByBossStatus: false)); // a colossal station
        Assert.False(EscortCruiserLook.IsEscortCruiser("Drone", 1f, namedByBossStatus: true));
        Assert.False(EscortCruiserLook.IsEscortCruiser(null, 3f, namedByBossStatus: true));
    }

    [Fact]
    public void TheSizes_FollowTheScale_AndThePlainCruiserKeepsItsOwn()
    {
        Assert.Equal(EscortCruiserRules.ModelScale, EscortCruiserLook.ModelScale(EscortCruiserRules.ModelScale));
        Assert.Equal(3f, EscortCruiserLook.ModelScale(3f));
        Assert.Equal(EscortCruiserRules.ModelScale, EscortCruiserLook.ModelScale(1f));    // named by the status only
        Assert.Equal(EscortCruiserRules.ModelScale, EscortCruiserLook.ModelScale(0f));
        Assert.Equal(EscortCruiserRules.ModelScale, EscortCruiserLook.ModelScale(500f));  // nonsense on the wire
        Assert.Equal(EscortCruiserRules.ModelScale, EscortCruiserLook.ModelScale(float.NaN));

        float boss = EscortCruiserRules.ModelScale;
        Assert.True(EscortCruiserLook.TargetRadius(boss) > 4.5f * 2f, "the boss's brackets are clearly wider than a cruiser's");
        Assert.True(EscortCruiserLook.TargetRadius(3f) > EscortCruiserLook.TargetRadius(2f));
        Assert.Equal(2.2f, EscortCruiserLook.ExplosionScale(1f), 4); // the plain cruiser's explosion
        Assert.InRange(EscortCruiserLook.ExplosionScale(boss), 3.5f, 2.2f * boss); // bigger, but slower than the model
        Assert.Equal(1.3f, EscortCruiserLook.PlumeSize(1f, enraged: false), 4); // the plain cruiser's plume
        Assert.Equal(1.3f * boss, EscortCruiserLook.PlumeSize(boss, enraged: false), 4);
        Assert.True(EscortCruiserLook.PlumeSize(boss, enraged: true) > EscortCruiserLook.PlumeSize(boss, enraged: false) * 1.2f,
            "enraged, the engines burn hotter");
    }

    [Fact]
    public void TheModel_IsLonger_WithWindowsAlongTheBridge_AndALongBarrelAboveThem()
    {
        Assert.True(EscortCruiserLook.HullLength > 5f * 1.3f, "longer than the plain cruiser's 5-unit hull");

        // The windows: an even row inside the bridge's side, front to rear.
        float rear = EscortCruiserLook.BridgeZ - (EscortCruiserLook.BridgeLength * 0.5f);
        float front = EscortCruiserLook.BridgeZ + (EscortCruiserLook.BridgeLength * 0.5f);
        float step = EscortCruiserLook.WindowZ(1) - EscortCruiserLook.WindowZ(0);
        Assert.True(EscortCruiserLook.WindowsPerSide >= 4, "a row, not a pair");
        Assert.True(step > EscortCruiserLook.WindowLength, "the windows do not touch");
        for (int i = 0; i < EscortCruiserLook.WindowsPerSide; i++)
        {
            float z = EscortCruiserLook.WindowZ(i);
            Assert.InRange(z - (EscortCruiserLook.WindowLength * 0.5f), rear, front);
            Assert.InRange(z + (EscortCruiserLook.WindowLength * 0.5f), rear, front);
            if (i > 0)
            {
                Assert.Equal(step, z - EscortCruiserLook.WindowZ(i - 1), 3);
            }
        }

        Assert.Equal(EscortCruiserLook.WindowZ(0), EscortCruiserLook.WindowZ(-3)); // out of range: the nearest window
        Assert.Equal(EscortCruiserLook.WindowZ(EscortCruiserLook.WindowsPerSide - 1), EscortCruiserLook.WindowZ(99));
        float bridgeTop = EscortCruiserLook.BridgeY + (EscortCruiserLook.BridgeHeight * 0.5f);
        float bridgeBottom = EscortCruiserLook.BridgeY - (EscortCruiserLook.BridgeHeight * 0.5f);
        Assert.InRange(EscortCruiserLook.WindowY + (EscortCruiserLook.WindowHeight * 0.5f), bridgeBottom, bridgeTop);
        Assert.InRange(EscortCruiserLook.WindowY - (EscortCruiserLook.WindowHeight * 0.5f), bridgeBottom, bridgeTop);

        // The barrel: above the windows, long, pointing forward past the prow — and the muzzle is its front end.
        Assert.True(EscortCruiserLook.BarrelY - (EscortCruiserLook.BarrelThickness * 0.5f) > EscortCruiserLook.WindowY + (EscortCruiserLook.WindowHeight * 0.5f),
            "the barrel sits above the windows");
        Assert.True(EscortCruiserLook.BarrelLength >= EscortCruiserLook.HullLength * 0.5f, "a long barrel");
        Assert.Equal(EscortCruiserLook.BarrelStartZ + EscortCruiserLook.BarrelLength, EscortCruiserLook.MuzzleZ, 4);
        Assert.Equal(EscortCruiserLook.BarrelY, EscortCruiserLook.MuzzleY);
        Assert.Equal(0f, EscortCruiserLook.MuzzleX);
        Assert.True(EscortCruiserLook.MuzzleZ > EscortCruiserLook.HullLength * 0.5f, "the muzzle is ahead of the hull");
    }

    [Fact]
    public void TheWindows_GlowCalmly_AndBrighterAndPulsingOnceEnraged()
    {
        float calm = EscortCruiserLook.Glow(enraged: false, time: 0f, reducedMotion: false);
        Assert.Equal(calm, EscortCruiserLook.Glow(false, 3.3f, false)); // steady
        float low = float.MaxValue, high = float.MinValue;
        for (float t = 0f; t < 2f; t += 0.01f)
        {
            float g = EscortCruiserLook.Glow(true, t, false);
            low = Math.Min(low, g);
            high = Math.Max(high, g);
        }

        Assert.True(low > calm + 0.2f, "enraged, the windows are brighter at every moment");
        Assert.True(high - low > 0.2f, "…and they pulse");
        Assert.InRange(high, 0f, 1f);
        float still = EscortCruiserLook.Glow(true, 0.4f, reducedMotion: true);
        Assert.Equal(still, EscortCruiserLook.Glow(true, 1.7f, reducedMotion: true)); // reduced motion: no pulse
        Assert.True(still > calm + 0.2f);
    }

    [Fact]
    public void TheMegaLaser_IsTheHeavyShotWithItsStyle()
    {
        Assert.True(MegaLaserAim.IsMegaLaser(SpaceCombatRules.HeavyStyleMegaLaser));
        Assert.True(MegaLaserAim.IsMegaLaser("mega_laser"));
        Assert.False(MegaLaserAim.IsMegaLaser(string.Empty)); // the classic heavy shot (and every older server)
        Assert.False(MegaLaserAim.IsMegaLaser(null));
        Assert.False(MegaLaserAim.IsMegaLaser("Mega_Laser"));
    }

    [Fact]
    public void TheAimingLine_PulsesFasterTowardTheShot()
    {
        const float seconds = 2.8f;
        Assert.Equal(0f, MegaLaserAim.Pulses(0f, seconds));
        float early = MegaLaserAim.Pulses(0.2f, seconds) - MegaLaserAim.Pulses(0.1f, seconds);
        float late = MegaLaserAim.Pulses(1f, seconds) - MegaLaserAim.Pulses(0.9f, seconds);
        Assert.True(late > early * 2f, $"the pulse speeds up (early {early}, late {late})");
        Assert.Equal(seconds * (MegaLaserAim.PulseStartHz + MegaLaserAim.PulseEndHz) * 0.5f, MegaLaserAim.Pulses(1f, seconds), 3);
        Assert.Equal(MegaLaserAim.Pulses(1f, seconds), MegaLaserAim.Pulses(5f, seconds)); // clamped past the end
        Assert.Equal(0f, MegaLaserAim.Pulses(float.NaN, seconds));
    }

    [Fact]
    public void TheAimingLine_IsVisibleToEveryPilot_AndStrongestForTheTargetedOne()
    {
        const float seconds = 2.8f;
        float lowMine = float.MaxValue, highOther = float.MinValue, lowOther = float.MaxValue;
        for (float t = 0.1f; t <= 1f; t += 0.005f)
        {
            float mine = MegaLaserAim.Alpha(t, seconds, atMe: true, reducedMotion: false);
            float other = MegaLaserAim.Alpha(t, seconds, atMe: false, reducedMotion: false);
            Assert.True(mine > other, $"at t={t} the targeted pilot sees it stronger");
            Assert.InRange(mine, 0f, 1f);
            lowMine = Math.Min(lowMine, mine);
            lowOther = Math.Min(lowOther, other);
            highOther = Math.Max(highOther, other);
            Assert.True(MegaLaserAim.Width(t, atMe: true) > MegaLaserAim.Width(t, atMe: false));
        }

        Assert.True(lowOther > 0.15f, "a bystander sees the line the whole time");
        Assert.True(lowMine > highOther, "even at its dimmest the targeted pilot's line beats a bystander's brightest");
        Assert.Equal(0f, MegaLaserAim.Alpha(0f, seconds, atMe: true, reducedMotion: false)); // it fades in
        Assert.True(MegaLaserAim.Width(1f, atMe: true) > MegaLaserAim.Width(0f, atMe: true), "it swells toward the shot");
        Assert.InRange(MegaLaserAim.Width(1f, atMe: true), 0.05f, 0.6f); // thin — the beam itself is the thick one

        // Reduced motion: a steady line.
        Assert.Equal(MegaLaserAim.Alpha(0.3f, seconds, true, reducedMotion: true), MegaLaserAim.Alpha(0.77f, seconds, true, reducedMotion: true));
    }

    [Theory]
    [InlineData(2.8f, 3)]
    [InlineData(2.0f, 2)]
    [InlineData(1.99f, 2)]
    [InlineData(0.4f, 1)]
    [InlineData(0f, 0)]
    [InlineData(-1f, 0)]
    public void TheWarning_CountsTheSecondsDown(float remaining, int shown)
        => Assert.Equal(shown, MegaLaserAim.Countdown(remaining));
}
