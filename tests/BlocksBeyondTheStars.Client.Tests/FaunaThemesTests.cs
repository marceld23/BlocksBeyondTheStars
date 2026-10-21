// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The fauna look of a world (#2868): every planet has a theme, every world a random motif, the theme leans the hide
/// pick, and all of it is a pure function of what the server puts on the wire.
/// </summary>
public sealed class FaunaThemesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void EveryFamilyMember_IsAHide_AndEveryThemeNamesKnownFamilies()
    {
        foreach (var family in FaunaThemes.Families)
        {
            Assert.NotEmpty(family.Value);
            Assert.All(family.Value, hide => Assert.True(CreatureHides.IsKnown(hide), family.Key + ": " + hide));
        }

        foreach (var theme in FaunaThemes.Themes)
        {
            Assert.All(theme.Value, f => Assert.True(FaunaThemes.Families.ContainsKey(f.Family), theme.Key + ": " + f.Family));
            Assert.All(theme.Value, f => Assert.InRange(f.Weight, 1, 5));
        }

        Assert.All(FaunaThemes.MotifFamilies, f => Assert.True(FaunaThemes.Families.ContainsKey(f), f));
    }

    [Fact]
    public void EveryPlanetWithFauna_ResolvesToATheme_AndTheObviousOnesAreRight()
    {
        foreach (var planet in Content.Planets.Values.Where(p => !p.IsAirless))
        {
            Assert.True(FaunaThemes.Themes.ContainsKey(FaunaThemes.ThemeOf(planet)), planet.Key);
        }

        Assert.Equal("cold", FaunaThemes.ThemeOf(Content.GetPlanet("glacier")));
        Assert.Equal("cold", FaunaThemes.ThemeOf(Content.GetPlanet("tundra")));
        Assert.Equal("arid", FaunaThemes.ThemeOf(Content.GetPlanet("desert")));
        Assert.Equal("swamp", FaunaThemes.ThemeOf(Content.GetPlanet("swamp")));
        Assert.Equal("fungal", FaunaThemes.ThemeOf(Content.GetPlanet("fungal")));
        Assert.Equal("crystal", FaunaThemes.ThemeOf(Content.GetPlanet("crystal_living")));
        Assert.Equal("ashen", FaunaThemes.ThemeOf(Content.GetPlanet("lava")));
        Assert.Equal("temperate", FaunaThemes.ThemeOf(null));
    }

    [Fact]
    public void AFaunaThemeInTheData_OverridesTheDerivedOne_AnUnknownOneIsIgnored()
    {
        var planet = new PlanetType { Key = "x", FloraTheme = "desert", BaseTemperature = 40, FaunaTheme = "cold" };
        Assert.Equal("cold", FaunaThemes.ThemeOf(planet));
        planet.FaunaTheme = "no-such-theme";
        Assert.Equal("arid", FaunaThemes.ThemeOf(planet));
    }

    [Fact]
    public void TheLook_IsAPureFunctionOfTheWire_AndNullWithoutIt()
    {
        PlanetType? Lookup(string key) => Content.GetPlanet(key);
        var a = new NetCreature { LookPlanet = "desert", LookSeed = FaunaLooks.WorldLookSeed(42, "desert") };
        var b = new NetCreature { LookPlanet = "desert", LookSeed = FaunaLooks.WorldLookSeed(42, "desert") };
        var la = FaunaThemes.For(a, Lookup)!;
        var lb = FaunaThemes.For(b, Lookup)!;
        Assert.Equal("arid", la.Theme);
        Assert.Equal(la.MotifFamily, lb.MotifFamily);
        Assert.Equal(la.PatternGroup, lb.PatternGroup);

        Assert.Null(FaunaThemes.For(new NetCreature(), Lookup));                              // an older server
        Assert.Null(FaunaThemes.For(new NetCreature { LookPlanet = "no_such_planet" }, Lookup)); // an unknown planet
        Assert.Null(FaunaThemes.For(a, null));

        // Worlds differ in their motif: over many seeds every motif family and pattern group shows up.
        var families = new HashSet<string>();
        var groups = new HashSet<string>();
        for (int s = 1; s < 400; s++)
        {
            var look = FaunaThemes.For(new NetCreature { LookPlanet = "desert", LookSeed = FaunaLooks.WorldLookSeed(s, "desert") }, Lookup)!;
            families.Add(look.MotifFamily);
            groups.Add(look.PatternGroup);
        }

        Assert.Equal(FaunaThemes.MotifFamilies.Count, families.Count);
        Assert.Equal(FaunaThemes.PatternGroups.Count, groups.Count);
    }

    [Theory]
    [InlineData("glacier", "warmcoat", "scales")]
    [InlineData("desert", "dry", "warmcoat")]
    [InlineData("swamp", "wet", "warmcoat")]
    public void OnRealRosters_TheThemeFamiliesDominate(string planetKey, string favoured, string other)
    {
        var planet = Content.GetPlanet(planetKey)!;
        var authored = Content.AuthoredCreaturesFor(planet);
        int fav = 0, oth = 0, rolled = 0;
        for (int s = 0; s < 150; s++)
        {
            long seed = 104729L * (s + 1);
            foreach (var sp in CreatureGenerator.GenerateRoster(planet, seed, WorldDescription.CurrentTerrainGeneration, authored))
            {
                if (sp.Id.StartsWith(CreatureCoat.AuthoredPrefix, StringComparison.Ordinal) || !string.IsNullOrEmpty(sp.Hide))
                {
                    continue;
                }

                var c = new NetCreature
                {
                    Id = sp.Id,
                    SpeciesId = sp.Id,
                    VoiceSeed = sp.VoiceSeed,
                    Glows = sp.Glows,
                    HasWings = sp.HasWings,
                    Habitat = sp.Habitat.ToString(),
                    Temperament = sp.Temperament.ToString(),
                    LookPlanet = planet.Key,
                    LookSeed = FaunaLooks.WorldLookSeed(seed, planet.Key),
                };
                string torso = CreatureHides.Pick(c, _ => true, FaunaThemes.For(c, k => Content.GetPlanet(k))).Torso!;
                fav += FaunaThemes.Families[favoured].Contains(torso) ? 1 : 0;
                oth += FaunaThemes.Families[other].Contains(torso) ? 1 : 0;
                rolled++;
            }
        }

        Assert.True(rolled > 200, "too few rolled species: " + rolled);
        Assert.True(fav > oth * 2, $"{planetKey}: {favoured} {fav} vs {other} {oth} of {rolled}");
    }

    [Fact]
    public void ThemedSource_IsThePool_WhenTheThemeSharesNothingWithIt()
    {
        var look = new FaunaThemes.Look("cold", "warmcoat", "spots");
        for (uint s = 1; s < 300; s++)
        {
            var source = CreatureHides.ThemedSource(s, CreatureHides.LavaPool, look);
            Assert.All(source, hide => Assert.Contains(hide, CreatureHides.LavaPool));
        }

        Assert.Same(CreatureHides.PassivePool, CreatureHides.ThemedSource(5, CreatureHides.PassivePool, null));
    }

    [Fact]
    public void EveryPatternGroup_HasMembers_AndTheTilePatternsAllBelongToOne()
    {
        var all = Enum.GetValues(typeof(CoatPattern)).Cast<CoatPattern>().Where(p => p != CoatPattern.None).ToList();
        foreach (string group in FaunaThemes.PatternGroups)
        {
            Assert.Contains(all, p => FaunaThemes.InGroup(p, group));
        }

        Assert.All(CreatureCoat.TilePatterns, p => Assert.Contains(FaunaThemes.PatternGroups, g => FaunaThemes.InGroup(p, g)));
    }
}
