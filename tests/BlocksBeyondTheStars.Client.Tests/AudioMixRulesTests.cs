// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3249 distance profiles, voice budget and roof occlusion; #3241 the stylised vacuum.</summary>
public sealed class AudioMixRulesTests
{
    [Theory]
    [InlineData("step_rock_2", AudioRange.Small)]
    [InlineData("wing_flap", AudioRange.Small)]
    [InlineData("creature_call_bark", AudioRange.Medium)]
    [InlineData("mine_stone", AudioRange.Medium)]
    [InlineData("sandworm_breach", AudioRange.Large)]
    [InlineData("giant_stomp", AudioRange.Large)]
    [InlineData("ship_destroyed", AudioRange.Huge)]
    [InlineData("ship_landing", AudioRange.Huge)]
    [InlineData("engine_thrust", AudioRange.Huge)]
    [InlineData(null, AudioRange.Medium)]
    public void Range_IsPickedByCueId(string? id, AudioRange expected) => Assert.Equal(expected, AudioMixRules.RangeFor(id));

    [Fact]
    public void Distances_GrowWithTheRange_AndMediumKeepsTheOldCurve()
    {
        Assert.Equal((4f, 20f), AudioMixRules.Distances(AudioRange.Medium));
        float last = 0f;
        foreach (var r in new[] { AudioRange.Small, AudioRange.Medium, AudioRange.Large, AudioRange.Huge })
        {
            var (min, max) = AudioMixRules.Distances(r);
            Assert.True(min < max);
            Assert.True(max > last, $"{r} must carry farther than the range before it");
            last = max;
        }
    }

    [Theory]
    [InlineData("creature_call_hiss", AudioCategory.Creature)]
    [InlineData("creature_large_hostile_hurt", AudioCategory.Creature)]
    [InlineData("wing_flap_2", AudioCategory.Foley)]
    [InlineData("slither_loop", AudioCategory.Foley)]
    [InlineData("mine_metal", AudioCategory.Other)]
    public void Category_IsPickedByCueId(string id, AudioCategory expected) => Assert.Equal(expected, AudioMixRules.CategoryFor(id));

    [Fact]
    public void Budget_AdmitsUnderTheCap_AndAtTheCapOnlyNearer()
    {
        int budget = AudioMixRules.BudgetFor(AudioCategory.Foley);
        Assert.True(AudioMixRules.Admit(budget - 1, budget, 30f, 5f));
        Assert.False(AudioMixRules.Admit(budget, budget, 12f, 10f));
        Assert.True(AudioMixRules.Admit(budget, budget, 4f, 10f));
        Assert.Equal(int.MaxValue, AudioMixRules.BudgetFor(AudioCategory.Other));
    }

    [Fact]
    public void Occlusion_NeedsDifferentRoofs_AndSomeDistance()
    {
        Assert.True(AudioMixRules.Occluded(listenerRoofed: true, sourceRoofed: false, distance: 12f));
        Assert.True(AudioMixRules.Occluded(listenerRoofed: false, sourceRoofed: true, distance: 12f));
        Assert.False(AudioMixRules.Occluded(listenerRoofed: true, sourceRoofed: true, distance: 12f));
        Assert.False(AudioMixRules.Occluded(listenerRoofed: true, sourceRoofed: false, distance: 2f));
    }

    [Fact]
    public void Vacuum_OnSpacewalksAndAirlessSurfaces_NeverInsideACabin()
    {
        Assert.True(VacuumAudio.InVacuum(eva: true, onFootInSpace: false, airlessWorld: false, aboard: false, onStation: false));
        Assert.True(VacuumAudio.InVacuum(eva: false, onFootInSpace: false, airlessWorld: true, aboard: false, onStation: false));
        Assert.False(VacuumAudio.InVacuum(eva: false, onFootInSpace: false, airlessWorld: true, aboard: true, onStation: false));
        Assert.False(VacuumAudio.InVacuum(eva: false, onFootInSpace: true, airlessWorld: false, aboard: false, onStation: true));
        Assert.False(VacuumAudio.InVacuum(eva: false, onFootInSpace: false, airlessWorld: false, aboard: false, onStation: false));
    }

    [Fact]
    public void Vacuum_TouchedSoundsStayClear_FarOnesAreMuffled_AndNothingNeedsAirThere()
    {
        Assert.Equal((false, VacuumAudio.ContactLevel), VacuumAudio.Shape(1f));
        Assert.Equal((true, VacuumAudio.RemoteLevel), VacuumAudio.Shape(15f));
        Assert.True(VacuumAudio.NeedsAir("sonic_boom"));
        Assert.True(VacuumAudio.NeedsAir("wind_light"));
        Assert.False(VacuumAudio.NeedsAir("step_rock"));
        Assert.True(VacuumAudio.IsAirless(planetAirless: false, spaceSky: false, atmosphereDensity: 0f));
        Assert.False(VacuumAudio.IsAirless(planetAirless: false, spaceSky: false, atmosphereDensity: 0.8f));
    }
}
