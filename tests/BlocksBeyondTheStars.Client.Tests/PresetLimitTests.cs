// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3946: the browser build offers only Potato and Low; every other platform offers all four presets.</summary>
public sealed class PresetLimitTests
{
    [Fact]
    public void Highest_IsLowInTheBrowser_AndHighElsewhere()
    {
        Assert.Equal(QualityCalibration.Low, PresetLimit.Highest(browser: true));
        Assert.Equal(QualityCalibration.High, PresetLimit.Highest(browser: false));
    }

    [Theory]
    [InlineData(QualityCalibration.Potato, QualityCalibration.Potato)]
    [InlineData(QualityCalibration.Low, QualityCalibration.Low)]
    [InlineData(QualityCalibration.Medium, QualityCalibration.Low)]
    [InlineData(QualityCalibration.High, QualityCalibration.Low)]
    [InlineData(-1, QualityCalibration.Potato)]
    [InlineData(7, QualityCalibration.Low)]
    public void Clamp_InTheBrowser_LowersEverythingAboveLow(int stored, int expected)
        => Assert.Equal(expected, PresetLimit.Clamp(stored, PresetLimit.Highest(browser: true)));

    [Theory]
    [InlineData(QualityCalibration.Potato)]
    [InlineData(QualityCalibration.Low)]
    [InlineData(QualityCalibration.Medium)]
    [InlineData(QualityCalibration.High)]
    public void Clamp_OnTheDesktop_KeepsEveryPreset(int stored)
        => Assert.Equal(stored, PresetLimit.Clamp(stored, PresetLimit.Highest(browser: false)));

    [Fact]
    public void Count_IsTheLengthOfTheSettingsCycle()
    {
        Assert.Equal(2, PresetLimit.Count(PresetLimit.Highest(browser: true)));
        Assert.Equal(4, PresetLimit.Count(PresetLimit.Highest(browser: false)));
    }
}
