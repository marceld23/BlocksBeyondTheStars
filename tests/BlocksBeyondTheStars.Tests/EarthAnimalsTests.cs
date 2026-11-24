// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3973: the animals of Earth (#3974/#3975 brought the last five) — fixed species with a localized name, their homes and
/// climate zones, their own recorded calls by mood and their real-life coats. The Earth planet type (<c>sol_earth</c>) comes with another package, so
/// the roster tests stand it in with a copy of a temperate living type under that key.
/// </summary>
public sealed class EarthAnimalsTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Earth = "sol_earth";

    private static readonly (string Key, string En, string De)[] Animals =
    {
        ("dog", "Dog", "Hund"), ("cat", "Cat", "Katze"), ("rabbit", "Rabbit", "Hase"), ("cow", "Cow", "Kuh"),
        ("sheep", "Sheep", "Schaf"), ("monkey", "Monkey", "Affe"), ("elephant", "Elephant", "Elefant"),
        ("chicken", "Chicken", "Huhn"), ("parrot", "Parrot", "Papagei"), ("eagle", "Eagle", "Adler"), // #3974
        ("dolphin", "Dolphin", "Delfin"), ("whale", "Whale", "Wal"),                                // #3975
    };

    [Fact]
    public void TheRecords_LiveOnEarth_AndAreNamedInEnglishAndGerman()
    {
        var content = Shipped.Value;
        var en = content.CreateLocalizer(GameLocale.English);
        var de = content.CreateLocalizer(GameLocale.German);
        foreach (var (key, english, german) in Animals)
        {
            var record = content.AuthoredCreatures[key];
            Assert.Equal(new[] { Earth }, record.Homes);
            Assert.Equal(CreatureNames.KeyFor(key), record.NameKey);
            Assert.True(CreatureNames.IsSpeciesKey(record.NameKey), key);
            Assert.Equal(english, en.Get(record.NameKey));
            Assert.Equal(german, de.Get(record.NameKey));
            Assert.Equal(english, record.EnglishName); // the language-neutral fallback
            Assert.InRange(record.Coats.Count, 3, 7);
            Assert.NotNull(record.Voices);
            if (record.Habitat != CreatureHabitat.Water)
            {
                Assert.NotEmpty(record.FloraThemes); // the sea animals live in any sea of the world
            }
        }
    }

    [Fact]
    public void AnEarthRoster_AppendsTheAnimals_InFileOrder_AndMovesNoRolledSpecies()
    {
        var content = Shipped.Value;
        var earth = EarthStandIn(content);
        string[] expected = Animals.Select(a => "au_" + a.Key).ToArray();
        int gen = WorldDescription.CurrentTerrainGeneration;
        for (long seed = 1; seed <= 12; seed++)
        {
            var with = CreatureGenerator.GenerateRoster(earth, seed, gen, content.AuthoredCreaturesFor(earth));
            var again = CreatureGenerator.GenerateRoster(earth, seed, gen, content.AuthoredCreaturesFor(earth));
            var rolled = CreatureGenerator.GenerateRoster(earth, seed, gen, Array.Empty<AuthoredCreature>());

            Assert.Equal(expected, with.Where(s => s.Id.StartsWith("au_", StringComparison.Ordinal)).Select(s => s.Id));
            Assert.Equal(with.Select(s => (s.Id, s.VoiceSeed)), again.Select(s => (s.Id, s.VoiceSeed))); // stable ids and seeds
            Assert.Equal(rolled.Select(s => (s.Id, s.VoiceSeed, s.ColorRgb)),
                with.Where(s => !s.Id.StartsWith("au_", StringComparison.Ordinal)).Select(s => (s.Id, s.VoiceSeed, s.ColorRgb)));

            var dog = with.Single(s => s.Id == "au_dog");
            Assert.Equal("creature.dog.name", dog.NameKey);
            Assert.Equal("Dog", dog.Name);
        }
    }

    [Fact]
    public void NoOtherPlanetType_GainsOrLosesAnAuthoredSpecies()
    {
        // The roster consumes exactly this list, so an unchanged list is an unchanged roster for every existing type.
        // The Earth type itself (#3970, data/planets.json) is the one that gains them — in file order, after its own list.
        var content = Shipped.Value;
        foreach (var type in content.Planets.Values)
        {
            var listed = type.AuthoredCreatures.Where(k => content.AuthoredCreatures.ContainsKey(k))
                .Select(k => content.AuthoredCreatures[k].Key);
            if (type.Key == Earth)
            {
                listed = listed.Concat(Animals.Select(a => a.Key));
            }

            Assert.Equal(listed, content.AuthoredCreaturesFor(type).Select(r => r.Key));
        }
    }

    [Fact]
    public void EveryAnimal_WearsItsOwnCoat_AndKeepsItWhenTamed()
    {
        var content = Shipped.Value;
        var earth = EarthStandIn(content);
        foreach (var (key, _, _) in Animals)
        {
            var sp = CreatureGenerator.MakeAuthored(content.AuthoredCreatures[key], earth, 7);
            var tamedSnapshot = sp.Clone(); // the companion's species snapshot
            var worn = new HashSet<int>();
            for (int i = 0; i < 300; i++)
            {
                int seed = CoatVariants.AnimalSeedOf("wild:e" + i);
                var wild = Wire(sp, seed);
                var pet = Wire(tamedSnapshot, seed); // TamedCreature.AnimalSeed is the wild animal's seed
                Assert.Equal((wild.ColorRgb, wild.BellyRgb, wild.Hide, wild.CoatPattern, wild.CoatMask),
                    (pet.ColorRgb, pet.BellyRgb, pet.Hide, pet.CoatPattern, pet.CoatMask));
                worn.Add(CoatVariants.PickIndex(sp.Coats, seed));
            }

            Assert.Equal(sp.Coats.Count, worn.Count); // every coat occurs — no two of a herd need look the same
        }
    }

    [Fact]
    public void TheVoices_AreBundledRecordings_KeptNatural()
    {
        string audioDir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        var bundled = Directory.GetFiles(audioDir, "animal_*.mp3").Select(f => Path.GetFileNameWithoutExtension(f)!).ToHashSet();
        var recordedCalls = bundled.Select(b => b.Substring(0, b.Length - 2)).ToHashSet(); // strip "_a" / "_b"
        Assert.Equal(recordedCalls.OrderBy(c => c, StringComparer.Ordinal), AnimalCalls.All.OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(AnimalCalls.All, call => Assert.All(AnimalCalls.Takes(call), take => Assert.Contains(take, bundled)));

        var content = Shipped.Value;
        var earth = EarthStandIn(content);
        foreach (var (key, _, _) in Animals)
        {
            var sp = CreatureGenerator.MakeAuthored(content.AuthoredCreatures[key], earth, 7);
            Assert.All(sp.Voices!.AllTakes(), take => Assert.Contains(take, bundled));
            var voice = CreatureVoices.Derive(sp.VoiceSeed, VoiceTraits.From(sp));
            Assert.Same(sp.Voices, voice.Own);
            Assert.Contains(voice.Call, sp.Voices.CallsFor(VoiceMood.Calm).SelectMany(AnimalCalls.Takes));
            Assert.Contains(voice.AlarmCall, sp.Voices.CallsFor(VoiceMood.Alarm).SelectMany(AnimalCalls.Takes));
            Assert.Equal(VoiceOp.None, voice.Op);
            Assert.Equal(string.Empty, voice.LayerCall);
            Assert.Equal(1, voice.Pulses);
            Assert.True(CreatureVoices.KeepsNaturalPitch(voice, sp.BodyPlan.ToString()), key);
            Assert.Equal(sp.Voices.Encode(), SpeciesVoices.Decode(Wire(sp, 1).Voices)!.Encode()); // the wire carries the table
        }

        // A client without the takes keeps the body plan's generated voice instead of going silent.
        var dog = CreatureGenerator.MakeAuthored(content.AuthoredCreatures["dog"], earth, 7);
        Assert.Null(CreatureVoices.Derive(dog.VoiceSeed, VoiceTraits.From(dog), _ => false).Own);
    }

    [Fact]
    public void TheMoodTable_FallsBackToTheCalmCall_AndAlternatesTakes()
    {
        var rabbit = Shipped.Value.AuthoredCreatures["rabbit"].Voices!;
        Assert.False(rabbit.Has(VoiceMood.Friendly));
        Assert.Equal(rabbit.Calm, rabbit.CallsFor(VoiceMood.Friendly));
        Assert.Equal("animal_rabbit_thump_a", rabbit.Take(VoiceMood.Alarm, 0));
        Assert.Equal("animal_rabbit_thump_b", rabbit.Take(VoiceMood.Alarm, 1));
        Assert.Equal("animal_rabbit_squeak_b", rabbit.Take(VoiceMood.Friendly, 3));
        Assert.Null(SpeciesVoices.Decode(string.Empty));
        Assert.Null(SpeciesVoices.Decode("|animal_dog_growl|")); // no calm call → the generated voice
    }

    [Fact]
    public void TheDog_FleesButBitesBack_TheShyOnesBegForTheirFood_AndNoneIsTheSchuppenhund()
    {
        var content = Shipped.Value;
        var earth = EarthStandIn(content);
        CreatureSpecies Sp(string key) => CreatureGenerator.MakeAuthored(content.AuthoredCreatures[key], earth, 7);

        var dog = Sp("dog");
        Assert.True(CreatureBehaviour.RetaliatesWhenAttacked(dog));
        Assert.Equal(CreatureTemperament.Skittish, CreatureBehaviour.EffectiveTemperament(dog, provoked: false));
        Assert.Equal(CreatureTemperament.Aggressive, CreatureBehaviour.EffectiveTemperament(dog, provoked: true));
        Assert.True(HerdRules.TamesByFeeding(dog) && HerdRules.IsFavouriteFood(dog, "creature_meat"));
        Assert.Equal(CreatureEarStyle.Floppy, dog.EarStyle);

        var cat = Sp("cat");
        Assert.False(CreatureBehaviour.RetaliatesWhenAttacked(cat));
        Assert.True(HerdRules.TamesByFeeding(cat) && HerdRules.IsFavouriteFood(cat, "milk"));
        Assert.True(HerdRules.TamesByFeeding(Sp("monkey")) && HerdRules.IsFavouriteFood(Sp("monkey"), "fruit_banana"));

        var elephant = Sp("elephant");
        Assert.True(elephant.HasTrunk);
        Assert.True(CreatureBehaviour.RetaliatesWhenAttacked(elephant));
        Assert.True(elephant.AttackDamage > 0f, "a provoked elephant must be able to push back");

        foreach (var (key, _, _) in Animals)
        {
            var sp = Sp(key);
            Assert.False(HoundRules.IsScaleHound(sp), key); // no VEGA warning, no one-about cap, no long aggro range
            Assert.False(sp.Hostile, key);
            if (key is "dog" or "cat" or "monkey" or "parrot" or "dolphin" or "whale")
            {
                Assert.NotEqual(CreatureDropKind.Food, sp.DropKind); // nothing edible
            }
        }
    }

    [Fact]
    public void TheExclusiveRule_AcceptsAClimateZone_AsWellAsASurface()
    {
        var cow = new CreatureSpecies { FloraThemes = new[] { "temperate", "Savanna" } };
        Assert.True(CreatureBiomes.OnOwnGround(cow, "grass", "temperate"));
        Assert.True(CreatureBiomes.OnOwnGround(cow, null, "savanna"));
        Assert.False(CreatureBiomes.OnOwnGround(cow, "grass", "tundra"));

        var leni = new CreatureSpecies { BiomeSurfaces = new[] { "snow", "ice" } }; // a surface-only record behaves as before
        Assert.True(CreatureBiomes.OnOwnGround(leni, "snow", "temperate"));
        Assert.False(CreatureBiomes.OnOwnGround(leni, "grass", "tundra"));
        Assert.True(CreatureBiomes.OnOwnGround(new CreatureSpecies(), "grass", "temperate"));

        var planet = new PlanetType
        {
            Key = "probe", FloraTheme = "temperate",
            Biomes = { new Biome { SurfaceBlock = "snow", FloraTheme = "tundra" }, new Biome { SurfaceBlock = "grass" } },
        };
        Assert.Equal(1, CreatureBiomes.AffinityIn(Array.Empty<string>(), new[] { "temperate" }, planet)); // inherits the type's
        Assert.Equal(0, CreatureBiomes.AffinityIn(new[] { "snow" }, Array.Empty<string>(), planet));
        Assert.Equal(-1, CreatureBiomes.AffinityIn(Array.Empty<string>(), new[] { "desert" }, planet));
    }

    [Fact]
    public void ContentLoad_RejectsUnknownZonesCallsAndNames_AndOnlyWarnsAboutAnUnknownHome()
    {
        var probe = new AuthoredCreature
        {
            Key = "probe", DropItem = string.Empty, NameKey = "dog", Homes = { "no_such_type" },
            FloraThemes = { "temperate", "jungle-ish" }, FavouriteFood = "unobtainium",
            Voices = new SpeciesVoices { Calm = { "animal_dog_bark" }, Alarm = { "animal_dog_meow" } },
        };
        var content = new GameContent(
            blocks: Array.Empty<BlockDefinition>(), items: Array.Empty<ItemDefinition>(), recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(), shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>(), authoredCreatures: new[] { probe });

        var ex = Assert.Throws<ContentValidationException>(content.Validate);
        var mine = ex.Problems.Where(p => p.Contains("'probe'")).ToList();
        Assert.Contains(mine, p => p.Contains("'jungle-ish'"));
        Assert.Contains(mine, p => p.Contains("'animal_dog_meow'"));
        Assert.Contains(mine, p => p.Contains("nameKey 'dog'"));
        Assert.Contains(mine, p => p.Contains("'unobtainium'"));
        Assert.DoesNotContain(mine, p => p.Contains("no_such_type")); // a home is a warning, not an error

        var warnings = new List<string>();
        Assert.Equal(1, content.WarnAuthoredCreatureHomes(warnings.Add));
        Assert.Contains("no_such_type", Assert.Single(warnings));
        Assert.Contains(AnimalCalls.Problems("x", new SpeciesVoices()), p => p.Contains("no calm call"));
    }

    // ---------------- helpers ----------------

    /// <summary>A copy of the temperate multi-biome type under the Earth key (the real one comes with another package).</summary>
    private static PlanetType EarthStandIn(GameContent content)
    {
        var varied = content.GetPlanet("varied")!;
        var copy = (PlanetType)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(varied, null)!;
        copy.Key = Earth;
        return copy;
    }

    private static NetCreature Wire(CreatureSpecies sp, int animalSeed)
    {
        var n = new NetCreature();
        CreatureWireMapping.WriteSpecies(n, sp, (Earth, 4711), animalSeed);
        return n;
    }
}
