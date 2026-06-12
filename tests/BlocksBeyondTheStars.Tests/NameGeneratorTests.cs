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
}
