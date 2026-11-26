// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4028: a double-tap of Jump starts flight from the water's surface again (it could not since the float of #3860 keeps
/// the chest in the water) — never from under water — and the take-off lifts the body out before the water may end the
/// flight (#836) again.
/// </summary>
public sealed class WaterTakeoffTests
{
    [Fact]
    public void ATap_CountsOnLand_AndAtTheSurface_ButNotUnderWater()
    {
        Assert.True(WaterTakeoff.TapCounts(inWater: false, atSurface: false));
        Assert.True(WaterTakeoff.TapCounts(inWater: true, atSurface: true));
        Assert.False(WaterTakeoff.TapCounts(inWater: true, atSurface: false));
    }

    [Fact]
    public void OnlyAFlight_FromTheWater_GetsATakeoff()
    {
        Assert.Equal(WaterTakeoff.Grace, WaterTakeoff.Start(fromWater: true));
        Assert.Equal(0f, WaterTakeoff.Start(fromWater: false));
    }

    [Fact]
    public void TheWater_EndsTheFlight_OnlyAfterTheTakeoff()
    {
        float grace = WaterTakeoff.Start(fromWater: true);
        Assert.False(WaterTakeoff.WaterEndsFlight(inWater: true, grace));
        grace = WaterTakeoff.Tick(grace, WaterTakeoff.Grace + 0.1f);
        Assert.Equal(0f, grace);
        Assert.True(WaterTakeoff.WaterEndsFlight(inWater: true, grace));
        Assert.False(WaterTakeoff.WaterEndsFlight(inWater: false, grace));
    }

    [Fact]
    public void TheTakeoff_LiftsTheFeet_OutOfTheWater()
    {
        // The float holds the feet SwimRules.FloatDepth under the surface; the take-off's least rise has to clear them.
        Assert.True(WaterTakeoff.Grace * WaterTakeoff.Lift > SwimRules.FloatDepth);
    }

    [Fact]
    public void DuringTheTakeoff_TheBodyRises_AndJumpStillRisesFaster()
    {
        Assert.Equal(WaterTakeoff.Lift, WaterTakeoff.Vertical(0f, 0.2f));
        Assert.Equal(WaterTakeoff.Lift, WaterTakeoff.Vertical(-9f, 0.2f)); // Descend cannot pull it back into the water
        Assert.Equal(9f, WaterTakeoff.Vertical(9f, 0.2f));
        Assert.Equal(0f, WaterTakeoff.Vertical(0f, 0f)); // afterwards the flight holds its height as always
        Assert.Equal(-9f, WaterTakeoff.Vertical(-9f, 0f));
    }

    [Fact]
    public void TheController_CountsTapsAtTheSurface_AndTheHudNamesTheFlight()
    {
        string controller = ClientTestPaths.PlayerControllerSources();
        Assert.Contains("UpdateFlight(grounded, inWater, onLadder || _climbing, atSurface);", controller);
        Assert.Contains("!WaterTakeoff.TapCounts(inWater, atSurface)", controller);
        Assert.Contains("WaterTakeoff.WaterEndsFlight(inWater, _waterTakeoff)", controller);
        Assert.Contains("_waterTakeoff = _flying ? WaterTakeoff.Start(inWater) : 0f;", controller);
        Assert.Contains("Game.SwimSurfaceFrame = Time.frameCount;", controller);

        string hud = ClientTestPaths.ScriptPartsText("HudUi");
        Assert.Contains("WaterTakeoff.ShowsFlyHint(Game.CanFly, swimming, Game.SwimmingAtSurface)", hud);
        Assert.Contains("loc.Get(\"ui.hud.hint_zero_g\")", hud);
    }

    [Theory]
    [InlineData("en", "ui.hud.hint_short")]
    [InlineData("en", "ui.hud.hint_short_pad")]
    [InlineData("de", "ui.hud.hint_short")]
    [InlineData("de", "ui.hud.hint_short_pad")]
    public void TheZeroGShortLine_NamesTheWayDown_InFourKeys(string code, string shortKey)
    {
        // Above the atmosphere line the short line said "jump"; the slot of the swim keys names rise and sink there.
        var table = VegaLineBudgetTests.Core(code);
        string line = SwimRules.SwimControlsLine(table[shortKey], table["ui.hud.hint_zero_g"]);
        Assert.Equal(4, line.Split(" · ").Length);
        Assert.Contains("{key:Jump}", line);
        Assert.Contains("{key:Crouch}", line);
    }

    [Fact]
    public void TheFlyHint_ShowsAtTheSurface_ButNotUnderWater()
    {
        Assert.True(WaterTakeoff.ShowsFlyHint(canFly: true, swimming: false, atSurface: false));
        Assert.True(WaterTakeoff.ShowsFlyHint(canFly: true, swimming: true, atSurface: true));
        Assert.False(WaterTakeoff.ShowsFlyHint(canFly: true, swimming: true, atSurface: false));
        Assert.False(WaterTakeoff.ShowsFlyHint(canFly: false, swimming: false, atSurface: false));
    }
}
