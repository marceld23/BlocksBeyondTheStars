// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2437 (generation 23): a gas giant in almost every system. Justus flew his whole galaxy and found none — the one-in-six
/// roll had landed only in the unnamed catalogue system he skipped. From generation 23 the outermost planet of every system
/// with at least two planets becomes a giant in about 85 % of systems, a system of four planets or more may hang a second
/// one on the orbit inside, and the start system, one-planet systems, the first breathable planet and the once-per-galaxy
/// landmarks stay as they are. A galaxy pinned below 23 keeps the old roll.
/// </summary>
public sealed class GasGiantPerSystemTests
{
    private const string Key = UniverseGenerator.GasGiantKey;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldDescription Desc(int generation) => new()
    {
        StarSystemCount = 80,
        PlanetsPerSystemMin = 2,
        PlanetsPerSystemMax = 6,
        MoonsPerPlanetMin = 0,
        MoonsPerPlanetMax = 2,
        SystemVariance = true,
        TerrainGeneration = generation,
    };

    [Fact]
    public void TheCurrentGeneration_IsTheGasGiantPerSystemGeneration()
    {
        Assert.Equal(23, WorldDescription.GasGiantPerSystemGeneration);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= WorldDescription.GasGiantPerSystemGeneration);
    }

    [Theory]
    [InlineData(4242)]
    [InlineData(-3754363130370412246L)] // Justus' galaxy of 2026-10-09: one giant in twelve systems before
    public void GenerationTwentyThree_PutsAGiantOnAlmostEveryOutermostOrbit(long seed)
    {
        var galaxy = new UniverseGenerator(seed, Desc(WorldDescription.GasGiantPerSystemGeneration), Content).Generate();
        int eligible = 0, outerGiants = 0, secondGiants = 0, lone = 0, loneGas = 0;

        // The start system never changes.
        Assert.DoesNotContain(galaxy.Systems[0].Bodies, b => b.PlanetType == Key);

        for (int si = 1; si < galaxy.Systems.Count; si++)
        {
            var system = galaxy.Systems[si];
            var planets = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            bool isLone = SystemArchetypes.ForIndex(seed, si) == SystemArchetype.LoneGiant;
            var giants = planets.Where(p => p.PlanetType == Key).ToList();

            if (isLone)
            {
                lone++;
                if (giants.Count > 0)
                {
                    loneGas++;
                }

                continue;
            }

            if (planets.Count < UniverseGenerator.GasGiantMinPlanetsPerSystem)
            {
                Assert.Empty(giants); // a one-planet system keeps its landable world
                continue;
            }

            eligible++;
            foreach (var giant in giants)
            {
                int orbit = planets.IndexOf(giant);
                Assert.True(orbit == planets.Count - 1 || orbit == planets.Count - 2, $"seed {seed}, system {si}: a giant on orbit {orbit} of {planets.Count}");
                if (orbit == planets.Count - 1)
                {
                    outerGiants++;
                }
                else
                {
                    secondGiants++;
                    Assert.True(planets.Count >= UniverseGenerator.GasGiantSecondMinPlanets, $"seed {seed}, system {si}: a second giant in a {planets.Count}-planet system");
                }
            }

            // A gas giant's moons keep their own types — they are still landable worlds.
            Assert.DoesNotContain(system.Bodies.Where(b => b.Kind == CelestialKind.Moon), m => m.PlanetType == Key);
        }

        Assert.True(eligible >= 20, $"seed {seed}: only {eligible} eligible systems — widen the galaxy");
        double outerShare = outerGiants / (double)eligible;
        Assert.True(outerShare >= 0.7, $"seed {seed}: only {outerGiants} of {eligible} eligible systems carry a giant on the outermost orbit ({outerShare:P0})");
        Assert.True(lone == 0 || loneGas == lone, $"seed {seed}: a lone giant system without its giant");
        Assert.True(secondGiants > 0 || seed != 4242, "seed 4242: no second giant at all in 80 systems — the second roll is dead");
    }

    [Fact]
    public void JustusGalaxy_HasAGiantInAlmostEveryNamedSystem_NotJustInZM1663()
    {
        // The reporter's seed with a normal 12-system galaxy: before, the single giant hid in catalogue system sys6.
        var desc = Desc(WorldDescription.GasGiantPerSystemGeneration);
        desc.StarSystemCount = 12;
        var galaxy = new UniverseGenerator(-3754363130370412246L, desc, Content).Generate();
        var systemsWithGiant = galaxy.Systems.Skip(1).Count(s => s.Bodies.Any(b => b.PlanetType == Key));
        Assert.True(systemsWithGiant >= 7, $"only {systemsWithGiant} of 11 systems carry a gas giant");
    }

    [Fact]
    public void GenerationTwentyTwo_KeepsTheOldRoll()
    {
        // Below 23 a giant may only be the lone giant or a lone outermost orbit — never a second one, never in a two-planet
        // system beyond that roll — and far fewer systems have one. The pinned generation keeps an existing save as it was.
        var galaxy = new UniverseGenerator(4242, Desc(WorldDescription.GasGiantPerSystemGeneration - 1), Content).Generate();
        int withGiant = 0;
        for (int si = 1; si < galaxy.Systems.Count; si++)
        {
            var planets = galaxy.Systems[si].Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            var giants = planets.Where(p => p.PlanetType == Key).ToList();
            bool lone = SystemArchetypes.ForIndex(4242, si) == SystemArchetype.LoneGiant;
            Assert.True(giants.Count <= 1, $"system {si}: two giants below generation 23");
            foreach (var g in giants)
            {
                Assert.True(lone || ReferenceEquals(g, planets[^1]), $"system {si}: a giant off the outermost orbit below generation 23");
            }

            withGiant += giants.Count > 0 ? 1 : 0;
        }

        Assert.InRange(withGiant / (double)(galaxy.Systems.Count - 1), 0.08, 0.5);
    }
}
