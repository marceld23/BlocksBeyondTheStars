// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3976 (terrain generation 49): the Earth species away from Earth, as rules — the records that mark pets, pen animals and
/// strays; the strays on other habitable worlds (their share, their place at the tail of the roster, nothing else moving);
/// the kept species of a world (where, which instance, the half-and-half mix, the products); and every older world's
/// rosters exactly as they were. The server half is <see cref="EarthSpeciesElsewhereServerTests"/>.
/// </summary>
public sealed class EarthSpeciesElsewhereTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const int Gen = WorldDescription.EarthSpeciesGeneration;
    private const int OldGen = Gen - 1;
    private const string Earth = "sol_earth";

    private readonly ITestOutputHelper _out;

    public EarthSpeciesElsewhereTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Generation49_IsTheCurrentOne()
    {
        Assert.Equal(49, Gen);
        Assert.Equal(Gen, WorldDescription.CurrentTerrainGeneration);
    }

    [Fact]
    public void TheRecords_MarkTheDogAndCatAsPetsAndStrays_AndTheFarmAnimalsForThePens()
    {
        var content = Shipped.Value;
        Assert.Equal(new[] { "dog", "cat", "cow", "sheep", "chicken" }, content.KeptAnimalRecords.Select(r => r.Key));
        Assert.Equal(new[] { "dog", "cat" }, content.KeptAnimalRecords.Where(r => r.Pet).Select(r => r.Key));
        Assert.Equal(new[] { ("cow", "milk"), ("sheep", "wool"), ("chicken", "egg") },
            content.KeptAnimalRecords.Where(r => r.Pen.Length > 0).Select(r => (r.Key, r.Pen)));
        Assert.Equal(new[] { ("dog", 0.15), ("cat", 0.15) }, content.StrayAnimalRecords.Select(r => (r.Key, r.WildElsewhere)));
    }

    [Fact]
    public void ContentLoad_RejectsAPenOfAnUnknownItem_AWildPet_AndAChanceAboveOne()
    {
        var probe = new AuthoredCreature
        {
            Key = "probe", DropItem = string.Empty, Habitat = CreatureHabitat.Water, Temperament = CreatureTemperament.Aggressive,
            Pet = true, Pen = "unobtainium", WildElsewhere = 1.5,
        };
        var content = new GameContent(
            blocks: Array.Empty<BlockDefinition>(), items: Array.Empty<ItemDefinition>(), recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(), shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>(), authoredCreatures: new[] { probe });

        var ex = Assert.Throws<ContentValidationException>(content.Validate);
        var mine = ex.Problems.Where(p => p.Contains("'probe'")).ToList();
        Assert.Contains(mine, p => p.Contains("pen lays unknown item 'unobtainium'"));
        Assert.Contains(mine, p => p.Contains("no peaceful land animal"));
        Assert.Contains(mine, p => p.Contains("wildElsewhere 1.5"));
    }

    [Fact]
    public void AnOlderWorld_KeepsItsRoster_AndKeepsNoEarthAnimal()
    {
        var content = Shipped.Value;
        foreach (var planet in content.Planets.Values.Where(p => p.Key != Earth))
        {
            for (long seed = 1; seed <= 6; seed++)
            {
                long rosterSeed = WorldGenerator.RosterSeedFor(seed, "sys0-p1");
                var before = CreatureGenerator.GenerateRoster(planet, rosterSeed, OldGen, content.AuthoredCreaturesFor(planet));
                var now = CreatureGenerator.GenerateRoster(planet, rosterSeed, OldGen, content.AuthoredCreaturesFor(planet), content.StrayAnimalRecords);
                Assert.Equal(before.Select(s => (s.Id, s.VoiceSeed, s.ColorRgb)), now.Select(s => (s.Id, s.VoiceSeed, s.ColorRgb)));
                Assert.Same(KeptAnimals.None, KeptAnimals.For(planet, rosterSeed, OldGen, now, content.KeptAnimalRecords));
            }
        }
    }

    [Fact]
    public void Strays_LiveOnAboutTheirShareOfTheHabitableWorlds_AtTheTail_AndMoveNoOtherSpecies()
    {
        var content = Shipped.Value;
        var strays = content.StrayAnimalRecords;
        var habitable = content.Planets.Values.Where(p => p.Key != Earth && StrayAnimals.WorldTakesStrays(p)).ToList();
        Assert.NotEmpty(habitable);
        var hosted = strays.ToDictionary(r => r.Key, _ => 0);
        var trials = strays.ToDictionary(r => r.Key, _ => 0);
        foreach (var planet in habitable)
        {
            for (long seed = 1; seed <= 60; seed++)
            {
                long rosterSeed = WorldGenerator.RosterSeedFor(seed, "sys1-p" + (seed % 5));
                var without = CreatureGenerator.GenerateRoster(planet, rosterSeed, Gen, content.AuthoredCreaturesFor(planet));
                var with = CreatureGenerator.GenerateRoster(planet, rosterSeed, Gen, content.AuthoredCreaturesFor(planet), strays);

                // Every other species stays where it was; the strays follow at the tail, in file order.
                Assert.Equal(without.Select(s => (s.Id, s.VoiceSeed, s.FavouriteFood)), with.Take(without.Count).Select(s => (s.Id, s.VoiceSeed, s.FavouriteFood)));
                var tail = with.Skip(without.Count).Select(s => s.Id).ToList();
                var expected = strays.Where(r => StrayAnimals.Hosts(r, planet, rosterSeed)).Select(r => "au_" + r.Key).ToList();
                Assert.Equal(expected, tail);

                foreach (var record in strays.Where(r => StrayAnimals.FindsGround(r, planet)))
                {
                    trials[record.Key]++;
                    if (tail.Contains("au_" + record.Key))
                    {
                        hosted[record.Key]++;
                        var sp = with.Single(s => s.Id == "au_" + record.Key);
                        Assert.Equal(record.Coats.Count, sp.Coats.Count); // its own coats and voices, wherever it lives
                        Assert.NotNull(sp.Voices);
                        Assert.Equal(CreatureHabitat.Land, sp.Habitat); // a ground animal: the town rule keeps it out
                    }
                }
            }
        }

        foreach (var record in strays)
        {
            double share = (double)hosted[record.Key] / Math.Max(1, trials[record.Key]);
            _out.WriteLine($"{record.Key}: {hosted[record.Key]} of {trials[record.Key]} habitable worlds ({share:P1})");
            Assert.True(trials[record.Key] > 200, $"{record.Key} finds ground on too few types to measure");
            Assert.InRange(share, record.WildElsewhere * 0.7, record.WildElsewhere * 1.3);
        }
    }

    [Fact]
    public void Strays_NeverLiveOnAWorldWithoutAirLifeOrGround_NorTwiceAtHome()
    {
        var content = Shipped.Value;
        foreach (var planet in content.Planets.Values.Where(p => !StrayAnimals.WorldTakesStrays(p)))
        {
            for (long seed = 1; seed <= 40; seed++)
            {
                var roster = CreatureGenerator.GenerateRoster(planet, seed, Gen, content.AuthoredCreaturesFor(planet), content.StrayAnimalRecords);
                Assert.DoesNotContain(roster, s => s.Id is "au_dog" or "au_cat");
            }
        }

        var earth = content.GetPlanet(Earth)!;
        for (long seed = 1; seed <= 20; seed++)
        {
            var roster = CreatureGenerator.GenerateRoster(earth, seed, Gen, content.AuthoredCreaturesFor(earth), content.StrayAnimalRecords);
            Assert.Single(roster, s => s.Id == "au_dog"); // at home it is Earth's own species, never a second stray
            Assert.Single(roster, s => s.Id == "au_cat");
        }
    }

    [Fact]
    public void OnAHabitableWorld_TheKeptSpecies_AreTheDogAndCat_AndTheFarmAnimals_WithTheirProducts()
    {
        var content = Shipped.Value;
        var meadow = content.GetPlanet("meadowlands")!;
        Assert.True(meadow.IsBreathable);
        long rosterSeed = WorldGenerator.RosterSeedFor(5, "sys0-p2");
        var roster = CreatureGenerator.GenerateRoster(meadow, rosterSeed, Gen, content.AuthoredCreaturesFor(meadow), content.StrayAnimalRecords);
        var kept = KeptAnimals.For(meadow, rosterSeed, Gen, roster, content.KeptAnimalRecords);

        Assert.Equal(new[] { "au_dog", "au_cat" }, kept.Pets.Select(s => s.Id));
        Assert.Equal(new[] { "au_cow", "au_sheep", "au_chicken" }, kept.PenAnimals.Select(s => s.Id));
        Assert.False(kept.PetsAtHome);
        Assert.False(kept.PensAtHome);
        Assert.Equal("milk", kept.PenProductOf("au_cow"));
        Assert.Equal("wool", kept.PenProductOf("au_sheep"));
        Assert.Equal("egg", kept.PenProductOf("au_chicken"));
        Assert.Null(kept.PenProductOf("sp0"));
        Assert.True(kept.IsKeptPet("au_dog"));
        Assert.False(kept.IsKeptPet("au_cow"));

        // The same species a stray of this world would be: the key-salted sub-seed, the record's name, coats and calls.
        var dog = kept.Pets[0];
        Assert.Equal(CreatureGenerator.MakeAuthored(content.AuthoredCreatures["dog"], meadow, rosterSeed).VoiceSeed, dog.VoiceSeed);
        Assert.Equal("creature.dog.name", dog.NameKey);
        Assert.NotEmpty(dog.Coats);

        // No air to breathe (a toxic or airless type), or a station: nobody keeps an Earth animal there.
        foreach (var other in content.Planets.Values.Where(p => p.Key != Earth && (!p.IsBreathable || p.Void)))
        {
            Assert.Same(KeptAnimals.None, KeptAnimals.For(other, rosterSeed, Gen, Array.Empty<CreatureSpecies>(), content.KeptAnimalRecords));
        }
    }

    [Fact]
    public void OnEarth_EveryPetAndPenAnimal_IsAnEarthSpecies_TheRostersOwn_WhateverTheGeneration()
    {
        var content = Shipped.Value;
        var earth = content.GetPlanet(Earth)!;
        foreach (int gen in new[] { OldGen, Gen })
        {
            long rosterSeed = WorldGenerator.RosterSeedFor(11, "sol-3");
            var roster = CreatureGenerator.GenerateRoster(earth, rosterSeed, gen, content.AuthoredCreaturesFor(earth), content.StrayAnimalRecords);
            var kept = KeptAnimals.For(earth, rosterSeed, gen, roster, content.KeptAnimalRecords);
            Assert.True(kept.PetsAtHome);
            Assert.True(kept.PensAtHome);
            Assert.All(kept.Pets.Concat(kept.PenAnimals), sp => Assert.Contains(sp, roster)); // the roster's own instances
            Assert.Equal(new[] { "au_cow", "au_sheep", "au_chicken" }, kept.PenAnimals.Select(s => s.Id));

            var native = roster.Where(s => s.Habitat == CreatureHabitat.Land).ToList(); // a rabbit or a monkey would qualify too
            for (int i = 0; i < 200; i++)
            {
                Assert.Same(kept.Pets, KeptAnimals.PoolFor("house-pet:Person" + i, 11, kept.Pets, native, kept.PetsAtHome));
                Assert.Same(kept.PenAnimals, KeptAnimals.PoolFor("pen:" + i, 11, kept.PenAnimals, native, kept.PensAtHome));
            }
        }
    }

    [Fact]
    public void ThePool_IsTheWorldsOwnWithoutKeptSpecies_HalfAndHalfElsewhere_AndStableByKey()
    {
        var a = new CreatureSpecies { Id = "au_dog" };
        var b = new CreatureSpecies { Id = "sp0" };
        IReadOnlyList<CreatureSpecies> kept = new[] { a }, native = new[] { b }, none = Array.Empty<CreatureSpecies>();

        Assert.Same(native, KeptAnimals.PoolFor("x", 1, none, native, atHome: false)); // an older world: exactly as before
        Assert.Same(kept, KeptAnimals.PoolFor("x", 1, kept, none, atHome: false));     // no candidate of its own
        Assert.Same(kept, KeptAnimals.PoolFor("x", 1, kept, native, atHome: true));    // at home

        int toKept = 0;
        for (int i = 0; i < 2000; i++)
        {
            var pool = KeptAnimals.PoolFor("house-pet:Person" + i, 42, kept, native, atHome: false);
            Assert.Same(pool, KeptAnimals.PoolFor("house-pet:Person" + i, 42, kept, native, atHome: false));
            toKept += ReferenceEquals(pool, kept) ? 1 : 0;
        }

        Assert.InRange(toKept, 900, 1100);
    }
}
