// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The procedural name generator (item 12 adds NPC names alongside the existing creature/flora names):
/// deterministic from a seeded <see cref="Random"/>, with plenty of variety, so the same world always
/// names an NPC the same way and a crowd reads as distinct individuals.
/// </summary>
public sealed class NameGeneratorTests
{
    [Fact]
    public void Person_IsDeterministic_ForTheSameSeed()
    {
        Assert.Equal(NameGenerator.Person(new Random(4242)), NameGenerator.Person(new Random(4242)));
        Assert.Equal(NameGenerator.Robot(new Random(7)), NameGenerator.Robot(new Random(7)));
    }

    [Fact]
    public void Person_IsTwoCapitalisedParts()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var parts = NameGenerator.Person(new Random(seed)).Split(' ');
            Assert.Equal(2, parts.Length); // given name + surname
            Assert.All(parts, p =>
            {
                Assert.False(string.IsNullOrEmpty(p));
                Assert.True(char.IsUpper(p[0]), $"each name part should be capitalised: {p}");
            });
        }
    }

    [Fact]
    public void Person_VariesAcrossSeeds()
    {
        var names = Enumerable.Range(0, 60).Select(s => NameGenerator.Person(new Random(s))).ToList();
        Assert.True(names.Distinct().Count() >= 55, "Personal names should be highly varied across seeds.");
    }

    [Fact]
    public void Robot_HasAStemAndUnitNumber()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var name = NameGenerator.Robot(new Random(seed));
            int dash = name.IndexOf('-');
            Assert.True(dash > 0, $"a robot designation needs a stem before the dash: {name}");
            Assert.True(int.TryParse(name[(dash + 1)..], out _), $"a robot designation ends in a number: {name}");
        }
    }

    [Fact]
    public void Clean_ReplacesOnlyTheOffendingWord_TheSameWayEveryTime()
    {
        // 2026-09: a hub station was called "Port Sex" — "s" + "e" + "x" is an ordinary syllable of the mill.
        string port = NameGenerator.Clean("Port Sex");
        Assert.StartsWith("Port ", port);
        Assert.True(NameGenerator.IsFullyClean(port), port);
        Assert.Equal(port, NameGenerator.Clean("Port Sex"));

        Assert.Equal("Port Halvek", NameGenerator.Clean("Port Halvek")); // a clean name never changes
        Assert.Equal("Skarnweed", NameGenerator.Clean("Skarnweed"));

        string flora = NameGenerator.Clean("Sexweed");
        Assert.EndsWith("weed", flora); // the botanical suffix stays
        Assert.True(NameGenerator.IsFullyClean(flora), flora);

        string region = NameGenerator.Clean("Sex's Reach");
        Assert.EndsWith("'s Reach", region);
        Assert.True(char.IsUpper(region[0]));
        Assert.True(NameGenerator.IsFullyClean(region), region);
    }

    [Fact]
    public void CoinedNames_NeverCarryABlockedTerm()
    {
        for (int seed = 0; seed < 6000; seed++)
        {
            var rng = new DeterministicRandom(seed);
            foreach (var name in new[]
                     {
                         NameGenerator.Port(rng), NameGenerator.Star(rng), NameGenerator.Moon(rng), NameGenerator.Asteroid(rng),
                         NameGenerator.Ship(rng), NameGenerator.Region(rng), NameGenerator.PlanetProper(rng, "ice"),
                         NameGenerator.PlanetProper(rng, null), NameGenerator.TwinPair(rng).A,
                         NameGenerator.Person(new Random(seed)), NameGenerator.Creature(new Random(seed)),
                         NameGenerator.Flora(new Random(seed)), NameGenerator.Tree(new Random(seed)), NameGenerator.Robot(new Random(seed)),
                     })
            {
                Assert.True(NameGenerator.IsFullyClean(name), $"seed {seed}: '{name}'");
            }
        }
    }
}
