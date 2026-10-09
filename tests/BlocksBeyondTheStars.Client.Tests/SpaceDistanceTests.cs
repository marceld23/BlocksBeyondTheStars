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

    /// <summary>#2428: a NaN, an infinity or a distance past two billion kilometres used to cast to <c>int.MinValue</c>,
    /// and <c>Group</c> threw an <c>OverflowException</c> out of the flight HUD. The readout now saturates or prints 0.</summary>
    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(float.PositiveInfinity, 0)]
    [InlineData(float.NegativeInfinity, 0)]
    [InlineData(3e8f, int.MaxValue)]        // 3e8 units × 10 km = 3e9 km > int.MaxValue → saturates (used to overflow)
    [InlineData(2.2e8f, int.MaxValue)]      // the first values that used to crash (2.147e8 … 2.147e9)
    [InlineData(float.MaxValue, int.MaxValue)]
    public void Km_NeverOverflows_OnAbsurdDistances(float units, int km) => Assert.Equal(km, SpaceDistance.Km(units));

    [Theory]
    [InlineData(int.MinValue, "-2 147 483 648")]
    [InlineData(int.MaxValue, "2 147 483 647")]
    [InlineData(-5, "-5")]
    [InlineData(-1234, "-1 234")]
    public void Group_NeverThrows_OnTheIntExtremes(int value, string expected) => Assert.Equal(expected, SpaceDistance.Group(value));

    [Theory]
    [InlineData(3e8f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Label_NeverThrows_OnAnAbsurdDistance(float units)
    {
        string label = SpaceDistance.Label(units, "{0} km");
        Assert.EndsWith(" km", label);
    }

    [Theory]
    [InlineData(100f, 5000f, true)]
    [InlineData(5000f, 5000f, true)]
    [InlineData(5001f, 5000f, false)]
    [InlineData(-1f, 5000f, false)]
    [InlineData(float.NaN, 5000f, false)]
    [InlineData(float.PositiveInfinity, 5000f, false)]
    [InlineData(3e8f, 5000f, false)]
    public void IsPlausible_BoundsALockDistanceByTheFlightReach(float units, float reach, bool plausible)
        => Assert.Equal(plausible, SpaceDistance.IsPlausible(units, reach));

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
