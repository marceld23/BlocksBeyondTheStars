// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2808 / #2818: the ambient motes drifted outdoor pollen through the cabin and the caves, and most planet types got
/// the same neutral white. The rules now live in <see cref="AmbientMotes"/>: outdoor motes only in the open air, the
/// warm lamplight motes at home, and a look per planet type (or its flora theme).
/// </summary>
public sealed class AmbientMotesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void OutdoorMotes_ShowOnlyInTheOpenAir()
    {
        Assert.Equal(AmbientMoteMode.Outdoor, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: true, reducedEffects: false));
        Assert.Equal(AmbientMoteMode.Outdoor, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: true, reducedEffects: true));
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: false, reducedEffects: false)); // a cave, a roof
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: true, aboard: false, atHome: false, openAir: true, reducedEffects: false));  // space, a station
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: false, aboard: true, atHome: false, openAir: true, reducedEffects: false));  // a guest aboard
    }

    [Fact]
    public void Home_GetsTheLamplightMotes_UnlessReducedEffects()
    {
        Assert.Equal(AmbientMoteMode.Lamplight, AmbientMotes.ModeFor(airless: false, aboard: true, atHome: true, openAir: false, reducedEffects: false));
        Assert.Equal(AmbientMoteMode.Lamplight, AmbientMotes.ModeFor(airless: true, aboard: true, atHome: true, openAir: false, reducedEffects: false)); // the cabin in space has air
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: false, aboard: true, atHome: true, openAir: false, reducedEffects: true));
    }

    [Fact]
    public void FungalWorlds_GetRisingVioletSpores()
    {
        var spores = AmbientMotes.StyleFor("fungal", Content.GetPlanet("fungal")?.FloraTheme);
        Assert.Equal("spores", spores.Name);
        Assert.True(spores.Rise > 0f, "spores rise");
        Assert.True(spores.B > spores.G && spores.R > spores.G, "spores are violet");
        Assert.True(spores.Life > AmbientMotes.Dust.Life, "spores drift slowly");
    }

    [Theory]
    [InlineData("swamp")]
    [InlineData("ashen")]
    [InlineData("meadowlands")]
    [InlineData("flower_fields")]
    [InlineData("tundra")]
    [InlineData("boreal")]
    [InlineData("savanna")]
    [InlineData("corrupted")]
    [InlineData("toxic_world")]
    public void PlanetTypesThatUsedToBeNeutralWhite_HaveTheirOwnTint(string planetType)
    {
        var planet = Content.GetPlanet(planetType);
        Assert.NotNull(planet);
        var style = AmbientMotes.StyleFor(planetType, planet!.FloraTheme);
        Assert.NotEqual("dust", style.Name);
    }

    [Fact]
    public void EveryPlanetType_ResolvesToAValidStyle()
    {
        foreach (var kv in Content.Planets)
        {
            var s = AmbientMotes.StyleFor(kv.Key, kv.Value.FloraTheme);
            Assert.False(string.IsNullOrEmpty(s.Name), kv.Key);
            foreach (float c in new[] { s.R, s.G, s.B, s.R2, s.G2, s.B2 })
            {
                Assert.InRange(c, 0f, 1f);
            }

            Assert.True(s.Rate > 0f && s.Size > 0f && s.Life > 0f, kv.Key);
            Assert.InRange(s.Rise, -0.2f, 0.2f); // a drift, never a fall or a fountain
        }

        // Most living worlds have a look of their own; only a few keep the plain dust.
        int plain = Content.Planets.Count(kv => AmbientMotes.StyleFor(kv.Key, kv.Value.FloraTheme).Name == "dust");
        Assert.True(plain < Content.Planets.Count / 3, $"{plain} planet types still get plain dust");
    }

    [Fact]
    public void LamplightMotes_AreWarmTinySlowAndFew()
    {
        var lamp = AmbientMotes.Lamplight;
        Assert.True(lamp.R >= lamp.G && lamp.G > lamp.B, "amber-cream");
        Assert.True(lamp.Size < AmbientMotes.Dust.Size);
        Assert.True(lamp.Life > AmbientMotes.Dust.Life);
        Assert.True(AmbientMotes.LamplightRate < AmbientMotes.OutdoorRate / 4f);
    }
}
