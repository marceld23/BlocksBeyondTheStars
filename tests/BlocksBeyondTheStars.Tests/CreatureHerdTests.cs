// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The big herds and the begging trait (#2018, generation 12): the rolls come after every older roll and only on a
/// generation-12 world, a passive land species begs with 30 % (and then lives in a herd of 8–12), other peaceful land species
/// roll a herd of 6–9 with 20 %, skittish species never beg — and the <see cref="HerdRules"/> the server reads are pure.
/// </summary>
public sealed class CreatureHerdTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] Planets = { "jungle", "desert", "highland", "swamp", "savanna" };
    private const int Gen12 = WorldDescription.BigHerdsGeneration;

    private static IEnumerable<CreatureSpecies> Roster(string planetKey, long seed, int generation)
    {
        var planet = Content.GetPlanet(planetKey);
        return planet == null ? Enumerable.Empty<CreatureSpecies>() : CreatureGenerator.GenerateRoster(planet, seed, generation);
    }

    private static bool Eligible(CreatureSpecies sp)
        => sp.Habitat == CreatureHabitat.Land && sp.BodyPlan == CreatureBodyPlan.Standard
           && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish;

    [Fact]
    public void TheWaveGatesOnGenerationTwelve_OlderRostersAreBitForBitUnchanged()
    {
        // A generation-11 roster carries no beggar and no big herd; on a generation-12 roster every species that did not roll
        // one equals its generation-11 self field for field, and a converted one differs ONLY in the two herd fields.
        int beggars = 0, bigHerds = 0;
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 20; seed++)
            {
                var gen11 = Roster(key, seed, Gen12 - 1).ToList();
                var gen12 = Roster(key, seed, Gen12).ToList();
                Assert.Equal(gen11.Count, gen12.Count);
                for (int i = 0; i < gen11.Count; i++)
                {
                    Assert.False(gen11[i].BegsForFood);
                    Assert.True(gen11[i].SocialGroupSize <= 5, $"gen 11 {key}/{seed}/{i}: group {gen11[i].SocialGroupSize}");

                    var converted = gen12[i];
                    if (converted.BegsForFood || converted.SocialGroupSize >= HerdRules.BigHerdMinSize)
                    {
                        Assert.True(Eligible(gen11[i]), "only a peaceful standard-plan Land species rolls a herd");
                        if (converted.BegsForFood)
                        {
                            beggars++;
                            Assert.Equal(CreatureTemperament.Passive, converted.Temperament); // skittish never beg
                            Assert.InRange(converted.SocialGroupSize, HerdRules.BeggarHerdMin, HerdRules.BeggarHerdMax);
                        }
                        else
                        {
                            bigHerds++;
                            Assert.InRange(converted.SocialGroupSize, HerdRules.BigHerdMin, HerdRules.BigHerdMax);
                        }

                        // Everything but the two herd fields is the generation-11 species.
                        var normalized = JsonSerializer.Deserialize<CreatureSpecies>(JsonSerializer.Serialize(converted))!;
                        normalized.BegsForFood = false;
                        normalized.SocialGroupSize = gen11[i].SocialGroupSize;
                        Assert.Equal(JsonSerializer.Serialize(gen11[i]), JsonSerializer.Serialize(normalized));
                    }
                    else
                    {
                        Assert.Equal(JsonSerializer.Serialize(gen11[i]), JsonSerializer.Serialize(converted));
                    }
                }
            }
        }

        Assert.True(beggars > 0, "the sample must contain a begging species");
        Assert.True(bigHerds > 0, "the sample must contain a big non-begging herd");
    }

    [Fact]
    public void GenerationTwelve_IsDeterministic()
    {
        var planet = Content.GetPlanet("savanna")!;
        Assert.Equal(
            JsonSerializer.Serialize(CreatureGenerator.GenerateRoster(planet, 4242, Gen12)),
            JsonSerializer.Serialize(CreatureGenerator.GenerateRoster(planet, 4242, Gen12)));
    }

    [Fact]
    public void TheDraws_HitTheirShares()
    {
        // Across five planet types and 60 seeds: about 30 % of the passive standard-plan Land species beg, and about 20 % of the
        // remaining peaceful ones (passive non-beggars + skittish) live in a big herd. Wide tolerances — this guards the wiring
        // (the right species, the right draw), not the RNG.
        int passive = 0, beggars = 0, eligibleForBigHerd = 0, bigHerds = 0;
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 60; seed++)
            {
                foreach (var sp in Roster(key, seed, Gen12))
                {
                    if (!Eligible(sp))
                    {
                        Assert.False(sp.BegsForFood, $"{key}/{seed}: a {sp.Habitat} {sp.BodyPlan} {sp.Temperament} species must not beg");
                        continue;
                    }

                    if (sp.Temperament == CreatureTemperament.Passive)
                    {
                        passive++;
                        if (sp.BegsForFood)
                        {
                            beggars++;
                            continue;
                        }
                    }

                    Assert.False(sp.BegsForFood);
                    eligibleForBigHerd++;
                    if (sp.SocialGroupSize >= HerdRules.BigHerdMinSize)
                    {
                        bigHerds++;
                    }
                }
            }
        }

        Assert.True(passive >= 60, $"sample too small: {passive} passive land species");
        double begShare = beggars / (double)passive;
        double herdShare = bigHerds / (double)eligibleForBigHerd;
        Assert.InRange(begShare, 0.20, 0.40);
        Assert.InRange(herdShare, 0.11, 0.30);
    }

    [Fact]
    public void AnAuthoredBeggar_ReachesTheRoster_ButTheRuleIgnoresASkittishOne()
    {
        var planet = Content.GetPlanet("jungle")!;
        var authored = new[]
        {
            new AuthoredCreature { Key = "muncher", NamePrefix = "Muncher", Temperament = CreatureTemperament.Passive, BegsForFood = true, SocialGroupSize = 8 },
            new AuthoredCreature { Key = "flincher", NamePrefix = "Flincher", Temperament = CreatureTemperament.Skittish, BegsForFood = true, SocialGroupSize = 8 },
        };
        var roster = CreatureGenerator.GenerateRoster(planet, 7, Gen12, authored);
        var muncher = Assert.Single(roster, sp => sp.Id == "au_muncher");
        var flincher = Assert.Single(roster, sp => sp.Id == "au_flincher");
        Assert.True(muncher.BegsForFood);
        Assert.True(HerdRules.BegsForFood(muncher));
        Assert.True(flincher.BegsForFood);       // the record says so...
        Assert.False(HerdRules.BegsForFood(flincher)); // ...but skittish species never beg
    }

    [Fact]
    public void HerdRules_TheBudget()
    {
        var herd = new CreatureSpecies { SocialGroupSize = 12 };
        var trio = new CreatureSpecies { SocialGroupSize = 3 };
        Assert.True(HerdRules.IsBigHerd(herd));
        Assert.False(HerdRules.IsBigHerd(trio));
        Assert.Equal(4, HerdRules.WeightedCount(herd, 12)); // twelve bodies, four slots
        Assert.Equal(4, HerdRules.WeightedCount(herd, 10)); // rounded up
        Assert.Equal(1, HerdRules.WeightedCount(herd, 1));
        Assert.Equal(0, HerdRules.WeightedCount(herd, 0));
        Assert.Equal(3, HerdRules.WeightedCount(trio, 3));  // a normal species counts every animal
    }

    [Fact]
    public void HerdRules_WhoBegs()
    {
        Assert.True(HerdRules.BegsForFood(new CreatureSpecies { Habitat = CreatureHabitat.Land, Temperament = CreatureTemperament.Passive, BegsForFood = true }));
        Assert.False(HerdRules.BegsForFood(new CreatureSpecies { Habitat = CreatureHabitat.Land, Temperament = CreatureTemperament.Passive }));
        Assert.False(HerdRules.BegsForFood(new CreatureSpecies { Habitat = CreatureHabitat.Land, Temperament = CreatureTemperament.Skittish, BegsForFood = true }));
        Assert.False(HerdRules.BegsForFood(new CreatureSpecies { Habitat = CreatureHabitat.Air, Temperament = CreatureTemperament.Passive, BegsForFood = true }));
        Assert.False(HerdRules.BegsForFood(new CreatureSpecies { Habitat = CreatureHabitat.Land, Temperament = CreatureTemperament.Passive, BegsForFood = true, BodyPlan = CreatureBodyPlan.Colossus }));
    }

    [Fact]
    public void HerdRules_WhatCountsAsFood()
    {
        // Marcel's rule (2026-09-26): everything that restores hunger and is not poisonous. Baits restore no hunger.
        Assert.True(HerdRules.IsFoodForCreatures(Content.GetItem("berries")));
        Assert.True(HerdRules.IsFoodForCreatures(Content.GetItem("grain")));
        Assert.True(HerdRules.IsFoodForCreatures(Content.GetItem("cooked_meat")));
        Assert.True(HerdRules.IsFoodForCreatures(Content.GetItem("emergency_ration")));
        Assert.False(HerdRules.IsFoodForCreatures(Content.GetItem("toxic_berries")));
        Assert.False(HerdRules.IsFoodForCreatures(Content.GetItem("forage_bait")));
        Assert.False(HerdRules.IsFoodForCreatures(Content.GetItem("meat_bait")));
        Assert.False(HerdRules.IsFoodForCreatures(Content.GetItem("stone")));
        Assert.False(HerdRules.IsFoodForCreatures(null));
    }

    [Fact]
    public void HerdRules_TheRings()
    {
        var p = HerdRules.OrbitPoint(new Vector3f(10f, 5f, 20f), 3f, 0f);
        Assert.Equal(13f, p.X, 3);
        Assert.Equal(5f, p.Y, 3);
        Assert.Equal(20f, p.Z, 3);
        var q = HerdRules.OrbitPoint(new Vector3f(10f, 5f, 20f), 3f, (float)System.Math.PI / 2f);
        Assert.Equal(10f, q.X, 3);
        Assert.Equal(23f, q.Z, 3);

        var sp = new CreatureSpecies { Size = 1f, SocialGroupSize = 10 };
        Assert.Equal(HerdRules.OrbitRadius + 0.6f, HerdRules.OrbitRadiusFor(sp, outerRing: false), 3);
        Assert.Equal(HerdRules.OrbitRadius + 0.6f + HerdRules.OuterRingExtra, HerdRules.OrbitRadiusFor(sp, outerRing: true), 3);
        Assert.Equal(HerdRules.SquabbleRadius, HerdRules.SquabbleRadiusFor(1), 3);
        Assert.Equal(HerdRules.SquabbleRadius + 11 * HerdRules.SquabbleRadiusPerAnimal, HerdRules.SquabbleRadiusFor(12), 3);
    }
}
