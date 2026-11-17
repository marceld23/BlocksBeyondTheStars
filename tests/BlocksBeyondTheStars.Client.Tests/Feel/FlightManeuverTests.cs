// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feel;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests.Feel;

/// <summary>The barrel roll as the flight view plays it (#3753) and the rules of the ship-systems bar (#3757).</summary>
public sealed class FlightManeuverTests
{
    private const float Frame = 1f / 60f;

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void ARoll_TurnsOnceAround_AndSlidesItsWholeDistance()
    {
        var roll = default(ShipRollState);
        Assert.True(roll.TryStart(direction: 1, cooldown: 4f));
        Assert.True(roll.Rolling);
        Assert.True(roll.Evading);

        float slide = 0f;
        float steepest = 0f;
        float last = 0f;
        for (float t = 0f; t < ShipManeuverRules.RollSeconds + 0.1f; t += Frame)
        {
            slide += roll.Tick(Frame);
            if (roll.Rolling)
            {
                Assert.True(roll.Angle >= last, "the turn never runs backwards");
                steepest = System.Math.Max(steepest, roll.Angle - last);
                last = roll.Angle;
            }
        }

        Assert.False(roll.Rolling);
        Assert.Equal(0f, roll.Angle);        // a full turn ends level
        Assert.Equal(1f, slide, 3);          // the whole slide was handed out, whatever the frame rate
        Assert.InRange(last, 340f, 360f);    // it came all the way round
        Assert.True(steepest < 20f, "no frame jumps the hull more than a few degrees");
    }

    [Fact]
    public void ARollToTheLeft_TurnsTheOtherWay()
    {
        var roll = default(ShipRollState);
        roll.TryStart(direction: -1, cooldown: 4f);
        roll.Tick(ShipManeuverRules.RollSeconds / 2f);
        Assert.Equal(-180f, roll.Angle, 1);
    }

    [Fact]
    public void TheNextRoll_WaitsForTheCooldown()
    {
        var roll = default(ShipRollState);
        Assert.True(roll.TryStart(1, cooldown: 4f));
        Assert.False(roll.TryStart(1, cooldown: 4f)); // still turning

        for (float t = 0f; t < 2f; t += Frame)
        {
            roll.Tick(Frame);
        }

        Assert.False(roll.TryStart(1, cooldown: 4f)); // turned, but not ready
        Assert.InRange(roll.Readiness(4f), 0.45f, 0.55f);
        Assert.False(roll.Evading);                   // the evade window is long over

        for (float t = 0f; t < 2.1f; t += Frame)
        {
            roll.Tick(Frame);
        }

        Assert.Equal(1f, roll.Readiness(4f));
        Assert.True(roll.TryStart(-1, cooldown: 4f));
    }

    [Fact]
    public void TheEvadeWindow_OutlastsTheTurn()
    {
        Assert.True(ShipManeuverRules.RollEvadeSeconds > ShipManeuverRules.RollSeconds);
        var roll = default(ShipRollState);
        roll.TryStart(1, 4f);
        roll.Tick(ShipManeuverRules.RollSeconds + 0.05f);
        Assert.False(roll.Rolling);
        Assert.True(roll.Evading);
    }

    [Fact]
    public void TheSystemsBar_OffersEveryWeaponThePilotFires_FromTheData()
    {
        foreach (var key in new[] { "ship_laser_basic", "ship_cannon_1", "laser_cannon_2", "asteroid_breaker", "missile_launcher", "scatter_cannon", "ion_pulse" })
        {
            Assert.True(ShipWeaponView.InSystemsBar(Content.GetShipModule(key)), key);
        }

        // The turret fires by itself; everything that is no weapon stays out.
        Assert.False(ShipWeaponView.InSystemsBar(Content.GetShipModule("point_defense")));
        Assert.False(ShipWeaponView.InSystemsBar(Content.GetShipModule("tractor_beam")));
        Assert.False(ShipWeaponView.InSystemsBar(Content.GetShipModule("afterburner")));
        Assert.False(ShipWeaponView.InSystemsBar(null));
    }

    [Fact]
    public void OnlyCombatWeapons_FireOnStations()
    {
        Assert.True(ShipWeaponView.FiresOnStations(Content.GetShipModule("ship_laser_basic")));   // the dual laser
        Assert.True(ShipWeaponView.FiresOnStations(Content.GetShipModule("missile_launcher")));
        Assert.False(ShipWeaponView.FiresOnStations(Content.GetShipModule("asteroid_breaker"))); // a mining tool
        Assert.False(ShipWeaponView.FiresOnStations(Content.GetShipModule("shield_generator")));
    }
}
