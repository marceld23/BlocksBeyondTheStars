// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1599: the cockpit instruments print flight-scene distances as kilometres (10 km per unit).</summary>
public sealed class SpaceDistanceTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(83f, 830)]
    [InlineData(83.04f, 830)]
    [InlineData(83.06f, 831)]
    [InlineData(-5f, 0)]
    public void Km_IsTenPerUnit_Rounded_NeverNegative(float units, int km) => Assert.Equal(km, SpaceDistance.Km(units));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1 000")]
    [InlineData(1660, "1 660")]
    [InlineData(12345, "12 345")]
    [InlineData(1234567, "1 234 567")]
    public void Group_SplitsThousandsWithASpace(int value, string expected) => Assert.Equal(expected, SpaceDistance.Group(value));

    [Fact]
    public void Label_UsesTheLocalizedFormat()
    {
        Assert.Equal("830 km", SpaceDistance.Label(83f, "{0} km"));
        Assert.Equal("830 км", SpaceDistance.Label(83f, "{0} км"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ui.space.km_fmt")] // the localizer handing back the key
    public void Label_FallsBackWhenTheFormatHasNoSlot(string? format) => Assert.Equal("1 660 km", SpaceDistance.Label(166f, format));
}
