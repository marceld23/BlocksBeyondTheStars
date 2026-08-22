// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Music;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Rest windows between tracks (#1173): planets and space breathe, the menu / loading / station / UI
/// contexts never go silent, and a rolled rest stays within its context's bounds.
/// </summary>
public sealed class MusicRestPolicyTests
{
    [Theory]
    [InlineData(MusicLibrary.Menu)]
    [InlineData(MusicLibrary.Loading)]
    [InlineData(MusicLibrary.Station)]
    [InlineData(MusicLibrary.StarChart)]
    [InlineData(MusicLibrary.Workshop)]
    [InlineData(MusicLibrary.Research)]
    [InlineData("finale_or_unknown")]
    public void RollRest_ShortStayContexts_NeverRest(string context)
    {
        Assert.Equal(0.0, MusicRestPolicy.RestChance(context));
        var rng = new Random(1);
        for (int i = 0; i < 300; i++)
        {
            Assert.Equal(0f, MusicRestPolicy.RollRest(context, rng));
        }
    }

    [Theory]
    [InlineData(MusicLibrary.PlanetIce)]
    [InlineData(MusicLibrary.PlanetGeneric)]
    [InlineData(MusicLibrary.PlanetCave)]
    [InlineData(MusicLibrary.PlanetDeep)]
    [InlineData(MusicLibrary.Space)]
    [InlineData(MusicLibrary.ShipInterior)]
    public void RollRest_LongStayContexts_RestSometimes_WithinBounds(string context)
    {
        double chance = MusicRestPolicy.RestChance(context);
        Assert.InRange(chance, 0.2, 0.7);
        var (min, max) = MusicRestPolicy.RestRange(context);
        Assert.True(min >= 45f && max <= 180f && min < max);

        var rng = new Random(42);
        int rests = 0;
        const int N = 4000;
        for (int i = 0; i < N; i++)
        {
            float rest = MusicRestPolicy.RollRest(context, rng);
            if (rest > 0f)
            {
                rests++;
                Assert.InRange(rest, min, max);
            }
        }

        Assert.InRange(rests / (double)N, chance - 0.05, chance + 0.05);
    }

    [Fact]
    public void RollRest_NullRng_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MusicRestPolicy.RollRest(MusicLibrary.PlanetIce, null!));
    }
}
