// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2970 (Justus: "if I put one at 100 %, all the others are at 0 %"): the planet-type page's "Only this", "All normal" and
/// share column.
/// </summary>
public sealed class PlanetTypeMixTests
{
    private static readonly PlanetTypeMix.Row[] Rows =
    {
        new("ocean", 12, false), new("rocky", 14, false), new("ice", 10, false), new("crystal_living", 5, true),
    };

    [Fact]
    public void OnlyThis_LeavesJustThatType_AtAHundredPercent()
    {
        var overrides = new Dictionary<string, int> { ["rocky"] = 2 };
        PlanetTypeMix.OnlyThis(overrides, Rows, "ice");

        Assert.Equal((int)Frequency.Frequent, overrides["ice"]);
        Assert.All(Rows.Where(r => r.Key != "ice"), r => Assert.Equal((int)Frequency.Off, overrides[r.Key]));
        var shares = PlanetTypeMix.SharesPercent(overrides, Rows, (int)Frequency.Normal);
        Assert.Equal(100, shares["ice"]);
        Assert.All(Rows.Where(r => r.Key != "ice"), r => Assert.Equal(0, shares[r.Key]));
    }

    [Fact]
    public void AllNormal_GivesAnEvenMix()
    {
        var overrides = new Dictionary<string, int>();
        PlanetTypeMix.AllNormal(overrides, Rows);

        var shares = PlanetTypeMix.SharesPercent(overrides, Rows, (int)Frequency.Normal);
        Assert.All(Rows, r => Assert.Equal(25, shares[r.Key]));
    }

    [Fact]
    public void UntouchedRows_FollowTheDataWeights_AndTheExoticSlider()
    {
        var none = new Dictionary<string, int>();
        // 12 + 14 + 10 + 5 = 41 at the Normal exotic step.
        var shares = PlanetTypeMix.SharesPercent(none, Rows, (int)Frequency.Normal);
        Assert.Equal(34, shares["rocky"]); // 14 / 41
        Assert.Equal(12, shares["crystal_living"]); // 5 / 41
        Assert.InRange(shares.Values.Sum(), 99, 101);

        // Exotic worlds Off: the exotic type never rolls, the others share everything.
        var noExotic = PlanetTypeMix.SharesPercent(none, Rows, (int)Frequency.Off);
        Assert.Equal(0, noExotic["crystal_living"]);
        Assert.Equal(39, noExotic["rocky"]); // 14 / 36
    }

    [Fact]
    public void ATouchedRow_UsesItsFrequencyWeight()
    {
        var overrides = new Dictionary<string, int> { ["ocean"] = (int)Frequency.Off };
        Assert.Equal(0, PlanetTypeMix.Weight(overrides, Rows[0], (int)Frequency.Normal));
        overrides["ocean"] = (int)Frequency.Frequent;
        Assert.Equal(Frequency.Frequent.Weight(), PlanetTypeMix.Weight(overrides, Rows[0], (int)Frequency.Normal));
    }
}
