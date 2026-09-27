// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The bipeds, the favourite foods and Mini-Michi-Paul (#2080–#2084, generation 16 — the school club idea of Paul and Ben):
/// the biped roll comes last and only on a generation-16 world, a biped is always peaceful with two arms, two legs and a big
/// head, every begging species of a generation-16 world loves a food its world grows, the guaranteed fruit hangs on the
/// palms and jungle trees of its types, and the authored Mini-Michi-Paul joins only generation-16 tropical worlds.
/// </summary>
public sealed class CreatureBipedTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] Planets = { "jungle", "desert", "highland", "swamp", "savanna", "varied" };
    private static readonly string[] TropicalHosts = { "jungle", "karst", "archipelago", "coral_sea", "rainbow_sea" };
    private const int Gen16 = WorldDescription.BipedGeneration;

    private static List<CreatureSpecies> Roster(string planetKey, long seed, int generation, bool authored = false)
    {
        var planet = Content.GetPlanet(planetKey);
        if (planet == null)
        {
            return new List<CreatureSpecies>();
        }

        return CreatureGenerator.GenerateRoster(planet, seed, generation, authored ? Content.AuthoredCreaturesFor(planet) : null).ToList();
    }

    [Fact]
    public void TheWaveGatesOnGenerationSixteen_OlderRostersAreBitForBitUnchanged()
    {
        // A generation-15 roster carries no biped and no favourite food. On generation 16 a species is either the same species
        // field for field (a favourite food aside, which only a beggar gets), or a biped rolled from a standard Land species.
        int bipeds = 0;
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 25; seed++)
            {
                var gen15 = Roster(key, seed, Gen16 - 1);
                var gen16 = Roster(key, seed, Gen16);
                Assert.Equal(gen15.Count, gen16.Count);
                for (int i = 0; i < gen15.Count; i++)
                {
                    Assert.NotEqual(CreatureBodyPlan.Biped, gen15[i].BodyPlan);
                    Assert.Equal(string.Empty, gen15[i].FavouriteFood);
                    Assert.Equal(0, gen15[i].Arms);

                    if (gen16[i].BodyPlan == CreatureBodyPlan.Biped)
                    {
                        bipeds++;
                        Assert.Equal(CreatureHabitat.Land, gen15[i].Habitat);
                        Assert.Equal(CreatureBodyPlan.Standard, gen15[i].BodyPlan);
                        continue;
                    }

                    var normalized = JsonSerializer.Deserialize<CreatureSpecies>(JsonSerializer.Serialize(gen16[i]))!;
                    normalized.FavouriteFood = string.Empty;
                    Assert.Equal(JsonSerializer.Serialize(gen15[i]), JsonSerializer.Serialize(normalized));
                }
            }
        }

        Assert.True(bipeds > 5, $"about one standard Land species in six becomes a biped — found only {bipeds}");
    }

    [Fact]
    public void ARolledBiped_IsPeaceful_WithTwoArmsTwoLegsAndABigHead_AndNeverAlone()
    {
        var bipeds = new List<CreatureSpecies>();
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 40; seed++)
            {
                bipeds.AddRange(Roster(key, seed, Gen16).Where(BipedRules.IsBiped));
            }
        }

        Assert.NotEmpty(bipeds);
        Assert.Contains(bipeds, sp => sp.BegsForFood);
        foreach (var sp in bipeds)
        {
            Assert.Contains(sp.Temperament, new[] { CreatureTemperament.Passive, CreatureTemperament.Skittish });
            Assert.False(sp.Hostile);
            Assert.Equal(0f, sp.AttackDamage);
            Assert.Equal(2, sp.Legs);
            Assert.Equal(2, sp.Arms);
            Assert.Equal(1, sp.Heads);
            Assert.False(sp.HasWings);
            Assert.Equal(0, sp.Tentacles);
            Assert.InRange(sp.Size, BipedRules.MinSize, BipedRules.MaxSize);
            Assert.InRange(sp.HeadRatio, BipedRules.MinHeadRatio, BipedRules.MaxHeadRatio);
            if (sp.BegsForFood)
            {
                Assert.Equal(CreatureTemperament.Passive, sp.Temperament);
                Assert.InRange(sp.SocialGroupSize, HerdRules.BeggarHerdMin, HerdRules.BeggarHerdMax);
            }
            else
            {
                Assert.InRange(sp.SocialGroupSize, BipedRules.GroupMin, BipedRules.GroupMax);
            }
        }
    }

    [Fact]
    public void EveryBeggarOfAGenerationSixteenWorld_LovesAFoodItsWorldGrows()
    {
        int beggars = 0, fruitLovers = 0;
        foreach (string key in Planets)
        {
            var planet = Content.GetPlanet(key)!;
            for (long seed = 1; seed <= 25; seed++)
            {
                var foods = FruitRules.CleanFruitItems(planet, seed, Gen16);
                var roster = Roster(key, seed, Gen16);
                Assert.Equal(JsonSerializer.Serialize(roster), JsonSerializer.Serialize(Roster(key, seed, Gen16))); // deterministic
                foreach (var sp in roster)
                {
                    if (!HerdRules.BegsForFood(sp))
                    {
                        Assert.Equal(string.Empty, sp.FavouriteFood);
                        continue;
                    }

                    beggars++;
                    Assert.True(HerdRules.TamesByFeeding(sp));
                    if (foods.Count == 0)
                    {
                        Assert.Equal(HerdRules.FallbackFavouriteFood, sp.FavouriteFood);
                    }
                    else
                    {
                        fruitLovers++;
                        Assert.Contains(sp.FavouriteFood, foods);
                    }

                    var item = Content.GetItem(sp.FavouriteFood);
                    Assert.True(HerdRules.IsFoodForCreatures(item), $"{sp.FavouriteFood} must be clean food");
                }
            }
        }

        Assert.True(beggars > 5, $"too few beggars sampled: {beggars}");
        Assert.True(fruitLovers > 0, "some beggar must love a fruit of its world");
    }

    [Fact]
    public void TheRules_ArePure()
    {
        var sp = new CreatureSpecies { Habitat = CreatureHabitat.Land, Temperament = CreatureTemperament.Passive, BegsForFood = true };
        Assert.False(HerdRules.TamesByFeeding(sp)); // no favourite yet
        sp.FavouriteFood = "fruit_banana";
        Assert.True(HerdRules.TamesByFeeding(sp));
        Assert.True(HerdRules.IsFavouriteFood(sp, "fruit_banana"));
        Assert.False(HerdRules.IsFavouriteFood(sp, "toxic_fruit_banana"));
        Assert.False(HerdRules.IsFavouriteFood(sp, "berries"));
        Assert.Equal(2, HerdRules.FeedsToTameFor(sp));
        sp.FeedsToTame = 4;
        Assert.Equal(4, HerdRules.FeedsToTameFor(sp));
        Assert.True(HerdRules.LureRangeFor(sp, "fruit_banana") > HerdRules.LureRangeFor(sp, "berries"));
        Assert.True(HerdRules.FavouriteLureRange < HerdRules.LeaveRange, "a lured animal must not give up on its next step");

        // Knee-high: Mini-Michi-Paul (size 0.5, big head 1.8) stands about half a block tall — a one-cell body.
        float h = BipedRules.HeightFor(0.5f, 1.8f);
        Assert.InRange(h, 0.45f, 0.65f);
        Assert.Equal(1, BipedRules.BodyHeightCells(0.5f, 1.8f, 1, 12));
        Assert.Equal(2, BipedRules.BodyHeightCells(1.3f, 2.0f, 1, 12)); // the tallest biped: child-high, two cells
    }

    [Fact]
    public void TheBipedVoice_IsTheGibberishPool_ASingleCleanDoubleCall()
    {
        var harsh = new[] { VoiceOp.Drive, VoiceOp.Crush, VoiceOp.ReverseTail, VoiceOp.Shape, VoiceOp.Comb };
        for (int seed = 1; seed <= 200; seed++)
        {
            var traits = new VoiceTraits("Land", "Passive", "Biped", 0.5f, 2, 2, 0, 1, 0, false, false);
            var v = CreatureVoices.Derive(seed, traits);
            Assert.StartsWith("creature_call_gibber", v.Call);
            Assert.Equal(1, v.Pulses);
            Assert.DoesNotContain(v.Op, harsh);
            Assert.Equal("ui.scan.voice.gibber", CreatureVoices.DescriptorKey(v));
        }

        // A client without the samples keeps a habitat voice instead of going silent.
        var fallback = CreatureVoices.Derive(7, new VoiceTraits("Land", "Passive", "Biped", 0.5f, 2, 2, 0, 1, 0, false, false),
            name => !name.StartsWith("creature_call_gibber", System.StringComparison.Ordinal));
        Assert.DoesNotContain("gibber", fallback.Call);
        Assert.Contains("creature_call_gibber", CreatureVoices.AllCalls);
    }

    [Fact]
    public void TheGuaranteedFruit_IsActive_AndHangsOnThePalmsAndJungleTrees_FromGenerationSixteen()
    {
        var jungle = Content.GetPlanet("jungle")!;
        Assert.Equal("flora_fruit_banana", jungle.GuaranteedFruit);
        Assert.Null(FruitRules.GuaranteedFruitFor(jungle, Gen16 - 1));
        Assert.Equal("flora_fruit_banana", FruitRules.GuaranteedFruitFor(jungle, Gen16));
        Assert.Null(FruitRules.GuaranteedFruitFor(Content.GetPlanet("desert")!, Gen16));

        for (long seed = 1; seed <= 40; seed++)
        {
            var roster = FloraGenerator.GenerateRoster(jungle, seed, Gen16);
            Assert.True(roster.Single(f => f.BlockKey == "flora_fruit_banana").Active, $"seed {seed}: bananas must be active");
            var active = roster.Where(f => f.Active && FloraCatalog.IsFruit(f.BlockKey)).Select(f => f.BlockKey).ToList();
            Assert.Equal("flora_fruit_banana", FruitRules.ShapeFor(seed, TreeKind.Palm, active, "flora_fruit_banana"));
            Assert.Equal("flora_fruit_banana", FruitRules.ShapeFor(seed, TreeKind.Jungle, active, "flora_fruit_banana"));
            Assert.Equal(FruitRules.ShapeFor(seed, TreeKind.Broadleaf, active), FruitRules.ShapeFor(seed, TreeKind.Broadleaf, active, "flora_fruit_banana"));

            // A clean jungle world offers its bananas to the fauna.
            if (TreeGenerator.Generate(jungle, seed) is { Toxic: false })
            {
                Assert.Contains("fruit_banana", FruitRules.CleanFruitItems(jungle, seed, Gen16));
            }
        }
    }

    [Fact]
    public void EveryTypeWithAGuaranteedFruit_GrowsPalmsOrJungleTrees()
    {
        foreach (var planet in Content.Planets.Values.Where(p => !string.IsNullOrEmpty(p.GuaranteedFruit)))
        {
            Assert.True(FloraCatalog.IsFruit(planet.GuaranteedFruit), planet.Key);
            var kinds = new HashSet<TreeKind>(FloraThemes.Resolve(planet.FloraTheme).Trees);
            foreach (var biome in planet.Biomes)
            {
                kinds.UnionWith(FloraThemes.Resolve(string.IsNullOrEmpty(biome.FloraTheme) ? planet.FloraTheme : biome.FloraTheme).Trees);
            }

            Assert.True(kinds.Any(FruitRules.BearsGuaranteedFruit), $"{planet.Key} grows no palm or jungle tree — its fruit would never hang");
            Assert.True(planet.TreeDensity is null or > 0.0, $"{planet.Key} grows no trees at all");
        }
    }

    [Fact]
    public void MiniMichiPaul_JoinsOnlyGenerationSixteenTropicalWorlds_AsTheKidsDesignedIt()
    {
        var record = Content.AuthoredCreatures["mini_michi_paul"];
        Assert.Equal(Gen16, record.MinGeneration);

        var hosts = Content.Planets.Values.Where(p => p.AuthoredCreatures.Contains("mini_michi_paul")).Select(p => p.Key).OrderBy(k => k).ToArray();
        Assert.Equal(TropicalHosts.OrderBy(k => k).ToArray(), hosts);

        foreach (string key in TropicalHosts)
        {
            Assert.DoesNotContain(Roster(key, 11, Gen16 - 1, authored: true), s => s.Id == "au_mini_michi_paul");
            var mmp = Roster(key, 11, Gen16, authored: true).Single(s => s.Id == "au_mini_michi_paul");
            Assert.Equal("Mini-Michi-Paul", mmp.Name); // the kids' name, no coined second word
            Assert.Equal(CreatureBodyPlan.Biped, mmp.BodyPlan);
            Assert.Equal(CreatureTemperament.Passive, mmp.Temperament);
            Assert.Equal(2, mmp.Arms);
            Assert.Equal(2, mmp.Legs);
            Assert.Equal(2, mmp.Eyes);
            Assert.Equal(1.8f, mmp.HeadRatio, 3);
            Assert.Equal(0.5f, mmp.Size, 3);
            Assert.Equal("skin", mmp.Hide);
            Assert.Equal(0xF2D335, mmp.ColorRgb); // yellow
            Assert.True(HerdRules.BegsForFood(mmp));
            Assert.Equal("fruit_banana", mmp.FavouriteFood);
            Assert.Equal(2, HerdRules.FeedsToTameFor(mmp));
            Assert.Equal(10, mmp.SocialGroupSize);
            Assert.Equal(0f, mmp.AttackDamage);
        }
    }
}
