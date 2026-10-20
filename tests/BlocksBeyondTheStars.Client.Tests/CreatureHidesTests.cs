// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The hide pick (#2829, #2831, #2861): keyed off the species' world-unique seed (not the per-slot id <c>sp0..sp8</c>), off
/// the species' disposition (not the momentary hostile flag), from one name table, per body part, never a hole while any
/// tile exists — and fair: glow is no pool any more, a server-named hide is a family, no hide dominates the rosters.
/// </summary>
public sealed class CreatureHidesTests
{
    private static readonly Func<string, bool> AllAvailable = _ => true;

    private static NetCreature Rolled(int voiceSeed, string id = "sp3", string temperament = "Passive",
        string habitat = "Land", bool glows = false, bool wings = false, bool hostile = false)
        => new NetCreature
        {
            Id = "e1",
            SpeciesId = id,
            VoiceSeed = voiceSeed,
            Temperament = temperament,
            Habitat = habitat,
            Glows = glows,
            HasWings = wings,
            Hostile = hostile,
        };

    [Fact]
    public void TheTable_HasNoDuplicates_AndEveryPoolAndFamilyMemberIsInIt()
    {
        Assert.Equal(CreatureHides.All.Count, CreatureHides.All.Distinct(StringComparer.Ordinal).Count());
        foreach (var pool in CreatureHides.Pools)
        {
            Assert.All(pool, name => Assert.True(CreatureHides.IsKnown(name), name));
            Assert.Equal(pool.Count, pool.Distinct(StringComparer.Ordinal).Count());
        }

        foreach (var family in CreatureHides.NamedFamilies)
        {
            Assert.Equal(family.Key, family.Value[0]); // the named hide itself leads its family
            Assert.All(family.Value, name => Assert.True(CreatureHides.IsKnown(name), name));
        }

        Assert.All(CreatureHides.Coarse, name => Assert.True(CreatureHides.IsKnown(name), name));
    }

    [Fact]
    public void EveryHide_ButThePetal_IsInAPool()
    {
        // The petal is the flowerling's designed hide (authored only); every other hide can be rolled.
        var pooled = CreatureHides.Pools.SelectMany(p => p).ToHashSet(StringComparer.Ordinal);
        Assert.All(CreatureHides.All.Where(n => n != "petal"), name => Assert.Contains(name, pooled));
        Assert.Equal("creature_slick", CreatureHides.TileKey("slick"));
    }

    [Fact]
    public void EveryHideTheServerCanRoll_IsInTheClientTable()
    {
        // The roster generator's hide arrays (every static string[] named *Hides) plus the authored records' hides.
        var arrays = typeof(CreatureGenerator)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(string[]) && f.Name.EndsWith("Hides", StringComparison.Ordinal))
            .ToList();
        Assert.True(arrays.Count >= 7, "the generator's hide arrays were not found");
        var rolled = arrays.SelectMany(f => (string[])f.GetValue(null)!).ToList();

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "creatures.json")));
        foreach (var record in doc.RootElement.EnumerateArray())
        {
            if (record.TryGetProperty("hide", out var hide) && hide.GetString() is { Length: > 0 } name)
            {
                rolled.Add(name);
            }
        }

        Assert.All(rolled, name => Assert.True(CreatureHides.IsKnown(name), name + " is rolled but not in CreatureHides.All"));

        // Every hide the generator itself rolls names a family (the authored hides are worn as designed).
        var generated = arrays.SelectMany(f => (string[])f.GetValue(null)!).Distinct();
        Assert.All(generated, name => Assert.True(CreatureHides.NamedFamilies.ContainsKey(name), name + " has no family"));
    }

    [Fact]
    public void ConsecutiveSlots_DoNotGiveConsecutivePicks_TheWorldSeedDecides()
    {
        // The bug (#2829): slots sp0..sp8 hashed to consecutive values, so slot 3 always wore the same hide. Same slot,
        // different worlds → the pick follows the voice seed.
        var picks = Enumerable.Range(0, 64).Select(i => CreatureHides.Pick(Rolled(1000 + i), AllAvailable).Torso).ToList();
        Assert.True(picks.Distinct().Count() >= 12, "consecutive voice seeds should spread over the pool");

        // And the slot id no longer matters for a rolled species: the same seed under another slot is the same hide.
        Assert.Equal(CreatureHides.Pick(Rolled(4242, "sp0"), AllAvailable).Torso, CreatureHides.Pick(Rolled(4242, "sp7"), AllAvailable).Torso);
    }

    [Fact]
    public void EveryPoolMember_IsReachable()
    {
        foreach (var (pool, make) in new (IReadOnlyList<string>, Func<int, NetCreature>)[]
                 {
                     (CreatureHides.PassivePool, s => Rolled(s)),
                     (CreatureHides.HostilePool, s => Rolled(s, temperament: "Aggressive")),
                     (CreatureHides.AquaticPool, s => Rolled(s, habitat: "Water")),
                     (CreatureHides.WingedPool, s => Rolled(s, wings: true)),
                     (CreatureHides.CavePool, s => Rolled(s, habitat: "Cave")),
                     (CreatureHides.LavaPool, s => Rolled(s, habitat: "Lava")),
                 })
        {
            var seen = Enumerable.Range(1, 3000).Select(s => CreatureHides.Pick(make(s), AllAvailable).Torso).ToHashSet();
            Assert.All(pool, name => Assert.Contains(name, seen));
        }
    }

    [Fact]
    public void Glow_PlaysNoPartInTheHide_AndWaterComesBeforeWings()
    {
        for (int seed = 1; seed < 400; seed++)
        {
            foreach (var (habitat, wings) in new[] { ("Land", false), ("Land", true), ("Water", false), ("Cave", false) })
            {
                var plain = Rolled(seed, habitat: habitat, wings: wings);
                var glowing = Rolled(seed, habitat: habitat, wings: wings, glows: true);
                Assert.Same(CreatureHides.PoolFor(plain), CreatureHides.PoolFor(glowing));
                Assert.Equal(CreatureHides.Pick(plain, AllAvailable).Torso, CreatureHides.Pick(glowing, AllAvailable).Torso);
            }
        }

        Assert.Same(CreatureHides.AquaticPool, CreatureHides.PoolFor(Rolled(1, habitat: "Water", wings: true)));
    }

    [Fact]
    public void AProvokedPassiveAnimal_KeepsItsPassiveHide_AndACompanionItsHuntersHide()
    {
        for (int seed = 1; seed < 200; seed++)
        {
            var calm = CreatureHides.Pick(Rolled(seed, hostile: false), AllAvailable);
            var provoked = CreatureHides.Pick(Rolled(seed, hostile: true), AllAvailable);
            Assert.Equal(calm.Torso, provoked.Torso);
            Assert.Contains(calm.Torso!, CreatureHides.PassivePool);

            // A tamed hunter reads Hostile = false on the wire; its species is still a hunter.
            var companion = CreatureHides.Pick(Rolled(seed, temperament: "PackHunter", hostile: false), AllAvailable);
            Assert.Contains(companion.Torso!, CreatureHides.HostilePool);
        }
    }

    [Fact]
    public void AnAuthoredSpecies_WearsItsHideEverywhere_ARolledServerHideNamesAFamily()
    {
        var authored = new NetCreature { SpeciesId = "au_leni", Hide = "shaggy", VoiceSeed = 77 };
        var set = CreatureHides.Pick(authored, AllAvailable, new FaunaThemes.Look("arid", "scales", "spots"));
        Assert.True(set.Uniform);
        Assert.Equal("shaggy", set.Torso);

        var family = CreatureHides.NamedFamily("plated");
        var worn = new HashSet<string>();
        for (int s = 1; s < 400; s++)
        {
            var colossus = new NetCreature { SpeciesId = "gi_colossus", Hide = "plated", VoiceSeed = s, BodyPlan = "Colossus" };
            string torso = CreatureHides.Pick(colossus, AllAvailable).Torso!;
            Assert.Contains(torso, family);
            worn.Add(torso);
        }

        Assert.Equal(family.Count, worn.Count); // every family member shows up, the newer hides included
    }

    [Fact]
    public void AMissingTile_IsSkipped_AndNoPickIsNullWhileAnyHideExists()
    {
        // Only the original set is bundled: no pick may name a missing one.
        var bundled = CreatureHides.All.Take(24).ToHashSet(StringComparer.Ordinal);
        bool Available(string n) => bundled.Contains(n);
        var look = new FaunaThemes.Look("cold", "warmcoat", "stripes");
        for (int seed = 1; seed < 500; seed++)
        {
            foreach (var c in new[] { Rolled(seed), Rolled(seed, glows: true), Rolled(seed, habitat: "Water"), Rolled(seed, temperament: "Aggressive"), Rolled(seed, habitat: "Cave") })
            {
                foreach (var set in new[] { CreatureHides.Pick(c, Available), CreatureHides.Pick(c, Available, look) })
                {
                    foreach (var part in new[] { CoatPart.Torso, CoatPart.Head, CoatPart.Limb, CoatPart.Wing })
                    {
                        Assert.NotNull(set.For(part));
                        Assert.Contains(set.For(part)!, bundled);
                    }
                }
            }
        }

        // A named hide whose family has no tile at all falls through to the pool.
        var giant = new NetCreature { SpeciesId = "gi_leviathan", Hide = "slick", VoiceSeed = 5, Habitat = "Water" };
        Assert.NotEqual("slick", CreatureHides.Pick(giant, Available).Torso);

        // Only one tile at all: everything wears it.
        var lone = CreatureHides.Pick(Rolled(3, glows: true), n => n == "fur");
        Assert.Equal("fur", lone.Torso);
        Assert.Equal("fur", lone.Limbs);

        // Nothing at all: null, never a throw.
        Assert.Null(CreatureHides.Pick(Rolled(3), _ => false).Torso);
    }

    [Fact]
    public void AboutHalfTheSpecies_WearOneHide_TheRestMixPerPart()
    {
        int uniform = 0, mixedHeads = 0;
        const int N = 4000;
        for (int seed = 1; seed <= N; seed++)
        {
            var set = CreatureHides.Pick(Rolled(seed), AllAvailable);
            uniform += set.Uniform ? 1 : 0;
            mixedHeads += set.Head != set.Torso ? 1 : 0;
        }

        Assert.InRange(uniform / (double)N, 0.42, 0.62); // 50 % roll uniform, a few mixed rolls land on one hide anyway
        Assert.True(mixedHeads > 0, "a mixed species may wear another hide on its head");
    }

    [Fact]
    public void SpeciesHostile_FollowsTheTemperament()
    {
        Assert.True(CreatureHides.SpeciesHostile("Aggressive"));
        Assert.True(CreatureHides.SpeciesHostile("PackHunter"));
        Assert.False(CreatureHides.SpeciesHostile("Territorial"));
        Assert.False(CreatureHides.SpeciesHostile("Passive"));
        Assert.False(CreatureHides.SpeciesHostile(null));
    }

    [Fact]
    public void CoarseHides_RepeatAtTwiceTheSize()
    {
        Assert.Equal(CreatureHides.CoarseTileMetres, CreatureHides.TileMetresOf("plated"));
        Assert.Equal(TiledCube.TileMetres, CreatureHides.TileMetresOf("fur"));
        Assert.Equal(TiledCube.TileMetres, CreatureHides.TileMetresOf(null));
    }

    /// <summary>The fairness the old pools lacked (#2861): over real rosters of every planet, themed as the server sends
    /// them, no hide takes more than 7 % of all torsos and every rollable hide shows up.</summary>
    [Fact]
    public void OverRealRosters_NoHideDominates_AndEveryRollableHideShowsUp()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        var torsos = new Dictionary<string, int>(StringComparer.Ordinal);
        int species = 0;
        foreach (var planet in content.Planets.Values.Where(p => !p.IsAirless))
        {
            var authored = content.AuthoredCreaturesFor(planet);
            for (int s = 0; s < 12; s++)
            {
                long seed = 7919L * (s + 1) + 3;
                foreach (var sp in CreatureGenerator.GenerateRoster(planet, seed, WorldDescription.CurrentTerrainGeneration, authored))
                {
                    var c = new NetCreature
                    {
                        Id = sp.Id,
                        SpeciesId = sp.Id,
                        VoiceSeed = sp.VoiceSeed,
                        Hide = sp.Hide ?? string.Empty,
                        Glows = sp.Glows,
                        HasWings = sp.HasWings,
                        Habitat = sp.Habitat.ToString(),
                        Temperament = sp.Temperament.ToString(),
                        BodyPlan = sp.BodyPlan.ToString(),
                        LookPlanet = planet.Key,
                        LookSeed = FaunaLooks.WorldLookSeed(seed, planet.Key),
                    };
                    var look = FaunaThemes.For(c, key => content.GetPlanet(key));
                    string torso = CreatureHides.Pick(c, AllAvailable, look).Torso!;
                    torsos[torso] = torsos.TryGetValue(torso, out int n) ? n + 1 : 1;
                    species++;
                }
            }
        }

        Assert.True(species > 2000, "the sample is too small: " + species);
        var top = torsos.OrderByDescending(kv => kv.Value).First();
        Assert.True(top.Value / (double)species <= 0.07, $"{top.Key} wears {top.Value * 100.0 / species:0.0} % of all torsos");
        var missing = CreatureHides.All.Where(h => h != "petal" && !torsos.ContainsKey(h)).ToList();
        Assert.True(missing.Count == 0, "never worn: " + string.Join(", ", missing));
    }
}
