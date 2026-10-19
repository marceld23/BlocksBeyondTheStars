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
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The hide pick (#2829, #2831): keyed off the species' world-unique seed (not the per-slot id <c>sp0..sp8</c>), off the
/// species' disposition (not the momentary hostile flag), from one name table, per body part, and never a hole while any
/// tile exists.
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
    public void TheTable_HasNoDuplicates_AndEveryPoolMemberIsInIt()
    {
        Assert.Equal(CreatureHides.All.Count, CreatureHides.All.Distinct(StringComparer.Ordinal).Count());
        foreach (var pool in new[]
                 {
                     CreatureHides.GlowingPool, CreatureHides.WingedPool, CreatureHides.AquaticPool,
                     CreatureHides.HostilePool, CreatureHides.PassivePool,
                 })
        {
            Assert.All(pool, name => Assert.True(CreatureHides.IsKnown(name), name));
        }
    }

    [Fact]
    public void EveryNewHide_AndSlick_IsInTheTable_AndInAPool()
    {
        string[] wave =
        {
            "finefeathers", "down", "wool", "leather", "quilted", "fungal", "barkmoss", "coarsescales", "finescales",
            "lizard", "bristles", "crystalveins", "cracked", "honeycomb", "ridged", "velvet", "snake", "roughbark",
            "pebbled", "knobbly", "coralskin", "wrinkled", "featherscales", "lichen",
        };
        var pooled = CreatureHides.GlowingPool.Concat(CreatureHides.WingedPool).Concat(CreatureHides.AquaticPool)
            .Concat(CreatureHides.HostilePool).Concat(CreatureHides.PassivePool).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(24, wave.Length);
        Assert.All(wave, name => Assert.Contains(name, pooled));
        Assert.True(CreatureHides.IsKnown("slick"));
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
                     (CreatureHides.GlowingPool, s => Rolled(s, glows: true)),
                 })
        {
            var seen = Enumerable.Range(1, 3000).Select(s => CreatureHides.Pick(make(s), AllAvailable).Torso).ToHashSet();
            Assert.All(pool, name => Assert.Contains(name, seen));
        }
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
    public void ANamedHide_WinsForTheTorso_AndAnAuthoredSpeciesWearsItEverywhere()
    {
        var authored = new NetCreature { SpeciesId = "au_leni", Hide = "shaggy", VoiceSeed = 77 };
        var set = CreatureHides.Pick(authored, AllAvailable);
        Assert.True(set.Uniform);
        Assert.Equal("shaggy", set.Torso);

        var giant = new NetCreature { SpeciesId = "gi_sky_giant", Hide = "slick", VoiceSeed = 99, Habitat = "Air", HasWings = true };
        Assert.Equal("slick", CreatureHides.Pick(giant, AllAvailable).Torso);
    }

    [Fact]
    public void AMissingTile_IsSkipped_AndNoPickIsNullWhileAnyHideExists()
    {
        // Only the original set is bundled (the 24 new tiles arrive later): no pick may name a missing one.
        var bundled = CreatureHides.All.Take(24).ToHashSet(StringComparer.Ordinal);
        bool Available(string n) => bundled.Contains(n);
        for (int seed = 1; seed < 500; seed++)
        {
            foreach (var c in new[] { Rolled(seed), Rolled(seed, glows: true), Rolled(seed, habitat: "Water"), Rolled(seed, temperament: "Aggressive") })
            {
                var set = CreatureHides.Pick(c, Available);
                foreach (var part in new[] { CoatPart.Torso, CoatPart.Head, CoatPart.Limb, CoatPart.Wing })
                {
                    Assert.NotNull(set.For(part));
                    Assert.Contains(set.For(part)!, bundled);
                }
            }
        }

        // A named hide whose tile is missing (slick before it is bundled) falls through to the pool.
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
}
