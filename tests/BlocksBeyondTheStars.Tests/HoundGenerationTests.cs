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

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Schuppenhund class (#3325, epic #3329 — Jafar's scale hound): a dry world of generation 34 may append ONE hound to its
/// roster, at its type's chance, from its own sub-seed (so no other species moves); every world rolls its own looks, the class
/// traits are fixed (very dangerous, alone, ears, teeth, claws, 10 HP a bite, meat), it barks, and its top speed stays below
/// the player's walk.
/// </summary>
public sealed class HoundGenerationTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private const int Gen = WorldDescription.ScaleHoundGeneration;

    private static readonly string[] DryTypes =
    {
        "desert", "savanna", "salt_flats", "tablelands", "badlands", "red_desert", "dust_bowl", "sand_sea", "arch_lands", "bone_desert",
    };

    private static IReadOnlyList<CreatureSpecies> Roster(string key, long seed, int generation)
        => CreatureGenerator.GenerateRoster(Content.GetPlanet(key)!, seed, generation);

    private static CreatureSpecies Hound(long seed, string key = "bone_desert")
        => CreatureGenerator.GenerateScaleHound(Content.GetPlanet(key)!, seed);

    [Fact]
    public void TheDryTypes_CarryAChance_AndTheGreenOnesDoNot()
    {
        foreach (string key in DryTypes)
        {
            var planet = Content.GetPlanet(key);
            Assert.NotNull(planet);
            Assert.InRange(planet!.ScaleHoundChance, 0.3, 0.9);
        }

        foreach (string key in new[] { "varied", "jungle", "ocean", "meadowlands", "arena_nigra", "titas", "valuma" })
        {
            Assert.Equal(0.0, Content.GetPlanet(key)!.ScaleHoundChance);
        }
    }

    [Fact]
    public void NoWorldBeforeTheHoundsGeneration_EverHostsOne()
    {
        foreach (string key in DryTypes)
        {
            for (long seed = 1; seed <= 40; seed++)
            {
                Assert.DoesNotContain(Roster(key, seed, Gen - 1), sp => sp.BodyPlan == CreatureBodyPlan.Hound);
            }
        }
    }

    [Fact]
    public void ATypeWithoutTheChance_NeverHostsOne()
    {
        foreach (string key in new[] { "varied", "jungle", "ocean" })
        {
            for (long seed = 1; seed <= 60; seed++)
            {
                Assert.DoesNotContain(Roster(key, seed, Gen), sp => sp.BodyPlan == CreatureBodyPlan.Hound);
            }
        }
    }

    [Fact]
    public void ADryWorld_HostsAtMostOne_AtRoughlyItsTypesChance()
    {
        int hosts = 0;
        const int Seeds = 300;
        for (long seed = 1; seed <= Seeds; seed++)
        {
            var hounds = Roster("bone_desert", seed, Gen).Where(sp => sp.BodyPlan == CreatureBodyPlan.Hound).ToList();
            Assert.True(hounds.Count <= 1, $"seed {seed}: one hound at most, it lives alone");
            Assert.Equal(CreatureGenerator.HostsScaleHound(Content.GetPlanet("bone_desert")!, seed), hounds.Count == 1);
            hosts += hounds.Count;
        }

        double share = hosts / (double)Seeds;
        Assert.InRange(share, 0.7, 0.9); // the type's chance is 0.8
    }

    [Fact]
    public void TheHound_IsAppendedLast_SoNoOtherSpeciesMoves()
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var before = Roster("savanna", seed, Gen - 1);
            var now = Roster("savanna", seed, Gen);
            var others = now.Where(sp => sp.BodyPlan != CreatureBodyPlan.Hound).ToList();
            Assert.Equal(before.Count, others.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].Id, others[i].Id);
                Assert.Equal(before[i].Name, others[i].Name);
                Assert.Equal(before[i].BodyPlan, others[i].BodyPlan);
                Assert.Equal(before[i].ColorRgb, others[i].ColorRgb);
                Assert.Equal(before[i].Size, others[i].Size);
                Assert.Equal(before[i].Temperament, others[i].Temperament);
            }

            if (now.Count > before.Count)
            {
                Assert.Equal(CreatureBodyPlan.Hound, now[now.Count - 1].BodyPlan);
            }
        }
    }

    [Fact]
    public void EveryHound_CarriesTheClassTraits()
    {
        for (long seed = 1; seed <= 120; seed++)
        {
            var sp = Hound(seed);
            Assert.Equal(HoundRules.SpeciesId, sp.Id);
            Assert.Equal("Schuppenhund", sp.Name); // the same name on every world and in every language
            Assert.Equal(CreatureBodyPlan.Hound, sp.BodyPlan);
            Assert.Equal(CreatureHabitat.Land, sp.Habitat);
            Assert.Equal(CreatureTemperament.Aggressive, sp.Temperament); // very dangerous
            Assert.True(sp.Hostile);
            Assert.Equal(1, sp.SocialGroupSize); // alone
            Assert.Equal(4, sp.Legs);
            Assert.Equal(2, sp.BodySegments);
            Assert.Equal(2, sp.Eyes);
            Assert.Equal(1, sp.Heads);
            Assert.Equal(0, sp.Horns);
            Assert.Equal(0, sp.Arms);
            Assert.True(sp.HasTail);
            Assert.True(sp.HasTeeth);
            Assert.True(sp.HasClaws);
            Assert.False(sp.HasWings);
            Assert.False(sp.HasFins);
            Assert.False(sp.BegsForFood);
            Assert.NotEqual(CreatureEarStyle.None, sp.EarStyle);
            Assert.InRange(sp.SnoutLength, HoundRules.MinSnout, HoundRules.MaxSnout);
            Assert.Contains(sp.Hide, HoundRules.Hides);
            Assert.InRange(sp.Size, HoundRules.MinSize, HoundRules.MaxSize);
            Assert.InRange(sp.MaxHealth, HoundRules.MinHealth, HoundRules.MaxHealth);
            Assert.Equal(10f, sp.BiteDamage);
            Assert.InRange(sp.BiteInterval, HoundRules.MinBiteInterval, HoundRules.MaxBiteInterval);
            Assert.True(CreatureReach.BitesInChunks(sp));
            Assert.Equal((double)(sp.BiteDamage / sp.BiteInterval), sp.AttackDamage, 3);
            Assert.Equal("creature_meat", sp.DropItem);
            Assert.Equal(CreatureDropKind.Food, sp.DropKind);
            Assert.Equal(LocomotionStyle.Prowler, sp.LocoStyle);
            Assert.NotEqual(0, sp.VoiceSeed);
        }
    }

    [Fact]
    public void EveryWorld_RollsItsOwnLooks_AndTheSameWorldTheSameHound()
    {
        var colours = new HashSet<int>();
        var ears = new HashSet<CreatureEarStyle>();
        var hides = new HashSet<string>();
        for (long seed = 1; seed <= 60; seed++)
        {
            var a = Hound(seed);
            var b = Hound(seed);
            Assert.Equal(a.ColorRgb, b.ColorRgb);
            Assert.Equal(a.Size, b.Size);
            Assert.Equal(a.Hide, b.Hide);
            Assert.Equal(a.EarStyle, b.EarStyle);
            Assert.Equal(a.VoiceSeed, b.VoiceSeed);
            colours.Add(a.ColorRgb);
            ears.Add(a.EarStyle);
            hides.Add(a.Hide);
        }

        Assert.True(colours.Count >= 20, $"random colours per world: {colours.Count} of 60");
        Assert.Equal(3, ears.Count);
        Assert.True(hides.Count >= 5, $"the scale variants: {hides.Count}");
    }

    [Fact]
    public void ItsTopSpeed_EvenCharging_StaysBelowThePlayersWalk()
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var profile = LocomotionController.ForSpecies(Hound(seed));
            Assert.True(profile.BurstSpeed * HoundRules.ChargeFactor < 6f,
                $"seed {seed}: {profile.BurstSpeed * HoundRules.ChargeFactor:0.00} blocks/s — a child must be able to run away");
        }
    }

    [Fact]
    public void ItBarks()
    {
        var barks = new[] { "creature_call_bark", "creature_call_bark_deep", "creature_call_bark_rough" };
        for (long seed = 1; seed <= 60; seed++)
        {
            var sp = Hound(seed);
            var voice = CreatureVoices.Derive(sp.VoiceSeed, VoiceTraits.From(sp));
            Assert.Contains(voice.Call, barks);
            Assert.Contains(voice.AlarmCall, barks);
            Assert.InRange(voice.Pulses, 1, 3);
            Assert.Equal("ui.scan.voice.bark", CreatureVoices.DescriptorKey(voice));
        }

        Assert.All(barks, b => Assert.Contains(b, CreatureVoices.AllCalls));
        Assert.Equal(barks, CreatureVoices.PoolFor("Land", "Hound"));
    }

    [Fact]
    public void ItsMuzzle_ReachesFurtherThanAStandardFace()
    {
        var sp = Hound(7);
        var standard = Hound(7);
        standard.BodyPlan = CreatureBodyPlan.Standard;
        float muzzle = CreatureFootprint.HeadReach(sp, 1f) - CreatureFootprint.HeadReach(standard, 1f);
        Assert.Equal((double)CreatureFootprint.HoundMuzzleDepth(sp.SnoutLength, 0.5f * sp.Size), muzzle, 3);
        Assert.True(muzzle > 0.15f);
    }

    [Fact]
    public void AWorksheetHound_KeepsTheClassTraits()
    {
        var record = new AuthoredCreature
        {
            Key = "test_hound",
            NamePrefix = "Rex",
            FixedName = true,
            BodyPlan = CreatureBodyPlan.Hound,
            Temperament = CreatureTemperament.Aggressive,
            Legs = 4,
            HasTail = true,
            EarStyle = CreatureEarStyle.Tall,
            SnoutLength = 0.6f,
            HasClaws = true,
            HasTeeth = true,
            BiteDamage = 8f,
            BiteInterval = 1.4f,
        };
        var roster = CreatureGenerator.GenerateRoster(Content.GetPlanet("varied")!, 11, Gen, new[] { record });
        var sp = Assert.Single(roster, s => s.Id == "au_test_hound");
        Assert.Equal(CreatureEarStyle.Tall, sp.EarStyle);
        Assert.Equal(0.6f, sp.SnoutLength);
        Assert.True(sp.HasClaws);
        Assert.True(sp.HasTeeth);
        Assert.Equal(8f, sp.BiteDamage);
        Assert.Equal(1.4f, sp.BiteInterval);
        Assert.True(CreatureReach.BitesInChunks(sp));
    }

    [Fact]
    public void NoOlderSpecies_BitesInChunks()
    {
        foreach (string key in new[] { "varied", "jungle", "desert" })
        {
            for (long seed = 1; seed <= 30; seed++)
            {
                foreach (var sp in Roster(key, seed, Gen).Where(s => s.BodyPlan != CreatureBodyPlan.Hound))
                {
                    Assert.False(CreatureReach.BitesInChunks(sp), $"{key}/{seed}/{sp.Id}");
                    Assert.Equal(CreatureEarStyle.None, sp.EarStyle);
                    Assert.Equal(0f, sp.SnoutLength);
                }
            }
        }
    }
}
