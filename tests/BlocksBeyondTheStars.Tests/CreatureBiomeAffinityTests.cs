// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3575: a species' biome affinity counts in the planet TYPE's biome pool, and so does the spawner's question "is this
/// species native here?" — the world's own shuffled subset never enters it. Before, 55 % of the species were native in a
/// different biome and 15 % nowhere.
/// </summary>
public sealed class CreatureBiomeAffinityTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private readonly ITestOutputHelper _out;

    public CreatureBiomeAffinityTests(ITestOutputHelper output) => _out = output;

    private static WorldGenerator Gen(long seed, string body)
    {
        var gen = new WorldGenerator(seed, Content);
        gen.SetTerrainGeneration(WorldDescription.CurrentTerrainGeneration);
        gen.SetWorldMode(6000, cratered: false, landingPads: null, body);
        return gen;
    }

    private static IEnumerable<(int X, int Z)> Columns()
    {
        for (int i = 0; i < 160; i++)
        {
            yield return ((i * 373) % 6000, ((i * 211) % 3000) - 1500);
        }
    }

    [Theory]
    [InlineData("varied")]
    [InlineData("boreal")]
    [InlineData("tundra")]
    [InlineData("sand_sea")]
    [InlineData("titas")]
    [InlineData("river_lowlands")]
    public void ThePoolIndex_NamesTheGroundsBiome_AndTheWorldsBiomesAreItsPick(string type)
    {
        var planet = Content.Planets[type];
        for (int b = 0; b < 6; b++)
        {
            var gen = Gen(1000 + (b * 7919L), $"sys{b}-p1");
            var onWorld = gen.PoolBiomesOnWorld(planet);
            Assert.Equal(gen.BiomeCount(planet), onWorld.Count(o => o));
            foreach (var (x, z) in Columns())
            {
                int pool = gen.BiomePoolIndexAt(planet, x, z);
                Assert.InRange(pool, 0, planet.Biomes.Count - 1);
                Assert.True(onWorld[pool], $"{type}: ({x},{z}) stands in pool biome {pool}, which the world does not carry");
                Assert.Equal(planet.Biomes[pool].SurfaceBlock, gen.BiomeSurfaceKeyAt(planet, x, z));
            }
        }
    }

    /// <summary>The spawner's native pass, rebuilt from its parts: at every sampled spot of many worlds, every species it
    /// would prefer is biome-agnostic, native to the biome there, or homeless on this world — never native elsewhere.</summary>
    [Fact]
    public void TheNativePass_PrefersOnlySpeciesOfTheBiomeUnderfoot()
    {
        int checkedSpecies = 0, homeless = 0;
        foreach (var planet in Content.Planets.Values.Where(p => p.Biomes.Count > 1 && !p.Void && !p.IsAirless).OrderBy(p => p.Key))
        {
            for (int b = 0; b < 2; b++) // two bodies per type: every multi-biome type, within the fast tier
            {
                long seed = 50 + (b * 104729L);
                string body = $"sys{b}-p2";
                var gen = Gen(seed, body);
                var onWorld = gen.PoolBiomesOnWorld(planet);
                var roster = CreatureGenerator.GenerateRoster(planet, WorldGenerator.RosterSeedFor(seed, body),
                    WorldDescription.CurrentTerrainGeneration, Content.AuthoredCreaturesFor(planet));
                homeless += roster.Count(s => s.BiomeAffinity >= 0 && !onWorld[s.BiomeAffinity]);
                foreach (var (x, z) in Columns().Where((_, i) => i % 4 == 0))
                {
                    int here = gen.BiomePoolIndexAt(planet, x, z);
                    foreach (var sp in roster.Where(s => s.BiomeAffinity >= 0 && onWorld[s.BiomeAffinity]))
                    {
                        if (CreatureBehaviour.NativeHere(sp.BiomeAffinity, here, onWorld))
                        {
                            checkedSpecies++;
                            Assert.Equal(sp.BiomeAffinity, here);
                            Assert.Equal(planet.Biomes[sp.BiomeAffinity].SurfaceBlock, gen.BiomeSurfaceKeyAt(planet, x, z));
                        }
                    }
                }
            }
        }

        _out.WriteLine($"{checkedSpecies} native picks checked, {homeless} species homeless on their world (agnostic there)");
        Assert.True(checkedSpecies > 500, $"only {checkedSpecies} native picks checked");
    }

    [Fact]
    public void ASpeciesWhoseBiomeTheWorldLacks_IsAgnostic_NotNativeNowhere()
    {
        var onWorld = new[] { true, false, true };
        Assert.True(CreatureBehaviour.NativeHere(-1, 2, onWorld), "an agnostic species is native everywhere");
        Assert.True(CreatureBehaviour.NativeHere(2, 2, onWorld), "a species is native on its own biome");
        Assert.False(CreatureBehaviour.NativeHere(0, 2, onWorld), "a species whose biome is elsewhere on the world is not native here");
        Assert.True(CreatureBehaviour.NativeHere(1, 2, onWorld), "a species whose biome the world lacks belongs nowhere in particular");
        Assert.True(CreatureBehaviour.NativeHere(5, 2, onWorld), "an index past the pool counts as a missing biome");
        Assert.False(CreatureBehaviour.NativeHere(1, 2), "without the world's biomes every affinity counts as present (the classic rule)");

        var a = new CreatureSpecies { Id = "a", BiomeAffinity = 0 };
        var lacking = new CreatureSpecies { Id = "b", BiomeAffinity = 1 };
        Assert.True(CreatureBehaviour.BiomePassStarved(new[] { a, lacking }, biome: 2), "two foreign species: starved");
        Assert.False(CreatureBehaviour.BiomePassStarved(new[] { a, lacking, lacking }, biome: 2, onWorld), "the homeless pair feeds the pass");
    }

    [Fact]
    public void ATypeWithoutAPool_IsUnchanged()
    {
        var planet = Content.Planets["lava"];
        Assert.Empty(planet.Biomes);
        var gen = Gen(7, "sys0-p1");
        Assert.Equal(-1, gen.BiomePoolIndexAt(planet, 100, 100));
        Assert.Empty(gen.PoolBiomesOnWorld(planet));
        var roster = CreatureGenerator.GenerateRoster(planet, WorldGenerator.RosterSeedFor(7, "sys0-p1"), WorldDescription.CurrentTerrainGeneration);
        Assert.All(roster, s => Assert.True(CreatureBehaviour.NativeHere(s.BiomeAffinity, -1, gen.PoolBiomesOnWorld(planet))));
    }

    /// <summary>Leni (snow and ice, exclusive) is preferred exactly where the ground is hers.</summary>
    [Fact]
    public void Leni_IsNativeWhereTheGroundIsSnowOrIce()
    {
        int checkedSpots = 0;
        foreach (var planet in Content.Planets.Values.Where(p => p.Biomes.Count > 1 && Content.AuthoredCreaturesFor(p).Any(a => a.Key == "leni")))
        {
            for (int b = 0; b < 8; b++)
            {
                long seed = 900 + (b * 31L);
                string body = $"sys{b}-p3";
                var gen = Gen(seed, body);
                var onWorld = gen.PoolBiomesOnWorld(planet);
                var roster = CreatureGenerator.GenerateRoster(planet, WorldGenerator.RosterSeedFor(seed, body),
                    WorldDescription.CurrentTerrainGeneration, Content.AuthoredCreaturesFor(planet));
                var leni = roster.FirstOrDefault(s => s.Id == "au_leni");
                if (leni is null || leni.BiomeAffinity < 0 || !onWorld[leni.BiomeAffinity])
                {
                    continue;
                }

                foreach (var (x, z) in Columns())
                {
                    if (CreatureBehaviour.NativeHere(leni.BiomeAffinity, gen.BiomePoolIndexAt(planet, x, z), onWorld))
                    {
                        checkedSpots++;
                        Assert.Contains(gen.BiomeSurfaceKeyAt(planet, x, z), leni.BiomeSurfaces);
                    }
                }
            }
        }

        _out.WriteLine($"{checkedSpots} spots where Leni is native");
        Assert.True(checkedSpots > 0, "no world carried Leni's biome");
    }
}
