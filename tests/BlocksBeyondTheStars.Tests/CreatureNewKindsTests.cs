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
/// The generation-6 creature kinds (#1778–#1782): the Ray plan (water + sky), the air fish, and the head / wing-pair /
/// fin-pair counts. Two things matter above everything: an older world's roster must be bit-for-bit what it was
/// (every roll of the wave is appended and applied only on a generation-6 world), and on a generation-6 world every
/// new body must hold the shape its plan promises.
/// </summary>
public sealed class CreatureNewKindsTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] Planets = { "jungle", "ocean", "desert", "highland", "swamp" };
    private const int Gen6 = WorldDescription.NewKindsGeneration;

    private static IEnumerable<CreatureSpecies> Roster(string planetKey, long seed, int generation)
    {
        var planet = Content.GetPlanet(planetKey);
        return planet == null ? Enumerable.Empty<CreatureSpecies>() : CreatureGenerator.GenerateRoster(planet, seed, generation);
    }

    [Fact]
    public void TheWaveGatesOnGenerationSix_OlderRostersAreBitForBitUnchanged()
    {
        // The whole point of appending + gating: a generation-5 roster equals the classic two-argument roster field
        // for field, and carries none of the new kinds.
        foreach (string key in Planets)
        {
            var planet = Content.GetPlanet(key);
            if (planet == null)
            {
                continue;
            }

            for (long seed = 1; seed <= 15; seed++)
            {
                var classic = CreatureGenerator.GenerateRoster(planet, seed);
                var gen5 = CreatureGenerator.GenerateRoster(planet, seed, Gen6 - 1);
                Assert.Equal(JsonSerializer.Serialize(classic), JsonSerializer.Serialize(gen5));
                foreach (var sp in gen5)
                {
                    Assert.NotEqual(CreatureBodyPlan.Ray, sp.BodyPlan);
                    Assert.False(CreatureMotion.IsAirFish(sp), $"{key}/{seed}/{sp.Id}: no air fish below generation {Gen6}");
                    Assert.Equal(1, sp.Heads);
                    Assert.Equal(1, sp.WingPairs);
                    Assert.Equal(1, sp.FinPairs);
                }
            }
        }
    }

    [Fact]
    public void GenerationSix_IsDeterministic()
    {
        var planet = Content.GetPlanet("jungle")!;
        Assert.Equal(
            JsonSerializer.Serialize(CreatureGenerator.GenerateRoster(planet, 4242, Gen6)),
            JsonSerializer.Serialize(CreatureGenerator.GenerateRoster(planet, 4242, Gen6)));
    }

    [Fact]
    public void RayPlan_HoldsItsInvariants_AndActuallyOccurs()
    {
        // 20 % of the standard-plan Air and Water species roll the plan on a wet world; every ray is legless, winged,
        // finless, tailed, never a hunter — a sky ray a hoverer that glides, a water ray a bottom-dwelling swimmer.
        int rays = 0, sky = 0, water = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            foreach (var sp in Roster("jungle", seed, Gen6))
            {
                if (sp.BodyPlan != CreatureBodyPlan.Ray)
                {
                    continue;
                }

                rays++;
                Assert.True(sp.Habitat is CreatureHabitat.Air or CreatureHabitat.Water, "rays glide in water or in the sky");
                Assert.Equal(0, sp.Legs);
                Assert.True(sp.HasWings);
                Assert.False(sp.HasFins);
                Assert.False(CreatureMotion.FinsFor(sp));
                Assert.True(sp.HasTail);
                Assert.Equal(0, sp.Tentacles);
                Assert.False(sp.HasGasSac);
                Assert.InRange(sp.Size, 1.2f, 3f);
                Assert.InRange(sp.BodySegments, 1, 2);
                Assert.True(sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish or CreatureTemperament.Territorial);
                Assert.False(sp.Hostile);
                Assert.Equal(1, sp.Heads);
                Assert.Equal(1, sp.WingPairs);
                Assert.Equal(1, sp.FinPairs);
                if (sp.Habitat == CreatureHabitat.Air)
                {
                    sky++;
                    Assert.Equal(LocomotionStyle.Glider, sp.LocoStyle);
                    Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(sp)); // never lands
                    Assert.True(CreatureMotion.IsSkyGlider(sp));
                    Assert.InRange(sp.HoverAltitude, 3f, 12f);
                }
                else
                {
                    water++;
                    Assert.True(sp.LocoStyle is LocomotionStyle.Drifter or LocomotionStyle.Schooler);
                    Assert.Equal(MotionClass.Swimmer, CreatureMotion.ClassOf(sp));
                    Assert.True(CreatureMotion.IsBottomDweller(sp));
                    Assert.False(CreatureMotion.IsSkyGlider(sp));
                }
            }
        }

        Assert.True(rays > 0, "the Ray plan must actually occur across 40 seeds of a wet world");
        Assert.True(sky > 0, "a sky ray must occur");
        Assert.True(water > 0, "a water ray must occur");
    }

    [Fact]
    public void AirFish_HoldsItsInvariants_AndActuallyOccurs()
    {
        // A quarter of the standard-plan Air species that did not become rays: legless, wingless, finned, tailed,
        // gliding — and a hoverer (a fish in the air never perches on a branch).
        int fish = 0;
        for (long seed = 1; seed <= 40; seed++)
        {
            foreach (var sp in Roster("jungle", seed, Gen6))
            {
                if (!CreatureMotion.IsAirFish(sp))
                {
                    continue;
                }

                fish++;
                Assert.Equal(CreatureHabitat.Air, sp.Habitat);
                Assert.Equal(CreatureBodyPlan.Standard, sp.BodyPlan);
                Assert.Equal(0, sp.Legs);
                Assert.False(sp.HasWings);
                Assert.True(sp.HasFins);
                Assert.True(CreatureMotion.FinsFor(sp));
                Assert.True(sp.HasTail);
                Assert.False(sp.HasGasSac);
                Assert.Equal(LocomotionStyle.Glider, sp.LocoStyle);
                Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(sp));
                Assert.True(CreatureMotion.IsSkyGlider(sp));
                Assert.InRange(sp.FinPairs, 1, 3);
                Assert.Equal(1, sp.Heads);
            }
        }

        Assert.True(fish > 0, "the air fish must actually occur across 40 seeds");
    }

    [Fact]
    public void Counts_StayInRange_OnlyWhereTheBodyCarriesThem_AndEveryKindOccurs()
    {
        int multiHeads = 0, hydras = 0, multiWings = 0, multiFins = 0;
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 60; seed++)
            {
                foreach (var sp in Roster(key, seed, Gen6))
                {
                    Assert.InRange(sp.Heads, 1, 3);
                    Assert.InRange(sp.WingPairs, 1, 3);
                    Assert.InRange(sp.FinPairs, 1, 3);
                    if (sp.Heads > 1)
                    {
                        multiHeads++;
                        Assert.True(sp.BodyPlan is CreatureBodyPlan.Standard or CreatureBodyPlan.Titan, "only standard bodies and titans grow extra heads");
                        Assert.True(sp.Habitat is CreatureHabitat.Land or CreatureHabitat.Cave or CreatureHabitat.Lava or CreatureHabitat.Amphibian);
                        if (sp.BodyPlan == CreatureBodyPlan.Titan)
                        {
                            hydras++;
                        }
                    }

                    if (sp.WingPairs > 1)
                    {
                        multiWings++;
                        Assert.True(sp.HasWings);
                        Assert.NotEqual(CreatureBodyPlan.Ray, sp.BodyPlan);
                    }

                    if (sp.FinPairs > 1)
                    {
                        multiFins++;
                        Assert.True(sp.HasFins);
                        Assert.Equal(0, sp.Legs);
                    }

                    // The fins derivation still describes every body, so a snapshot lift stays safe (CreatureFinsTests).
                    Assert.Equal(CreatureMotion.FinsFor(sp), sp.HasFins);
                    if (sp.HasFins)
                    {
                        Assert.True(sp.Habitat is CreatureHabitat.Water or CreatureHabitat.Amphibian || CreatureMotion.IsAirFish(sp));
                    }
                }
            }
        }

        Assert.True(multiHeads > 0, "a two- or three-headed species must occur");
        Assert.True(hydras > 0, "a hydra (multi-headed titan) must occur");
        Assert.True(multiWings > 0, "a multi-winged species must occur");
        Assert.True(multiFins > 0, "a multi-finned species must occur");
    }

    [Fact]
    public void LocoStyle_HabitatBiases_StillHold_OnGenerationSix()
    {
        // The classic roster invariant (CreatureTests.Roster_AssignsLocomotionStyle_BiasedByTraits) must survive the
        // wave: a sky ray glides, a water ray drifts or schools — never a style its habitat does not allow.
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 30; seed++)
            {
                foreach (var sp in Roster(key, seed, Gen6))
                {
                    if (sp.Habitat == CreatureHabitat.Air)
                    {
                        Assert.Contains(sp.LocoStyle, new[] { LocomotionStyle.Glider, LocomotionStyle.Drifter, LocomotionStyle.Strider });
                    }
                    else if (sp.Habitat == CreatureHabitat.Water)
                    {
                        Assert.Contains(sp.LocoStyle, new[] { LocomotionStyle.Schooler, LocomotionStyle.Slitherer, LocomotionStyle.Drifter });
                    }
                    else if (sp.Legs == 0)
                    {
                        Assert.NotEqual(LocomotionStyle.Hopper, sp.LocoStyle);
                    }
                }
            }
        }
    }

    [Fact]
    public void MotionRules_ForTheNewBodies()
    {
        static CreatureSpecies Sp(CreatureHabitat habitat, int legs, CreatureBodyPlan plan = CreatureBodyPlan.Standard, bool wings = false)
            => new() { Id = "spX", Habitat = habitat, Legs = legs, BodyPlan = plan, HasWings = wings, VoiceSeed = 77 };

        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(Sp(CreatureHabitat.Air, 0, CreatureBodyPlan.Ray, wings: true)));
        Assert.Equal(MotionClass.Swimmer, CreatureMotion.ClassOf(Sp(CreatureHabitat.Water, 0, CreatureBodyPlan.Ray, wings: true)));
        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(Sp(CreatureHabitat.Air, 0)));           // the air fish
        Assert.Equal(MotionClass.Flier, CreatureMotion.ClassOf(Sp(CreatureHabitat.Air, 2, wings: true))); // a bird is still a flier
        Assert.True(CreatureMotion.IsAirFish(Sp(CreatureHabitat.Air, 0)));
        Assert.False(CreatureMotion.IsAirFish(Sp(CreatureHabitat.Air, 0, CreatureBodyPlan.Medusa)));
        Assert.False(CreatureMotion.IsAirFish(Sp(CreatureHabitat.Water, 0)));
        Assert.True(CreatureMotion.FinsFor(Sp(CreatureHabitat.Air, 0)));
        Assert.False(CreatureMotion.FinsFor(Sp(CreatureHabitat.Air, 0, CreatureBodyPlan.Ray, wings: true)));
        Assert.False(CreatureMotion.FinsFor(Sp(CreatureHabitat.Water, 0, CreatureBodyPlan.Ray, wings: true)));
        Assert.True(CreatureMotion.IsBottomDweller(Sp(CreatureHabitat.Water, 0, CreatureBodyPlan.Ray)));
        Assert.False(CreatureMotion.IsBottomDweller(Sp(CreatureHabitat.Water, 0)));
        Assert.False(CreatureMotion.CanJump(Sp(CreatureHabitat.Air, 0)));

        // The wire-side rule the client draws pitch and banking from.
        Assert.True(CreatureMotion.IsSkyGliderBody("Air", "Ray", 0));
        Assert.True(CreatureMotion.IsSkyGliderBody("Air", "Standard", 0));
        Assert.False(CreatureMotion.IsSkyGliderBody("Air", "Medusa", 0));
        Assert.False(CreatureMotion.IsSkyGliderBody("Air", "Standard", 2));
        Assert.False(CreatureMotion.IsSkyGliderBody("Water", "Ray", 0));
        Assert.False(CreatureMotion.IsSkyGliderBody(null, null, 0));
    }

    [Fact]
    public void AuthoredSpecies_MayUseTheNewCounts()
    {
        var planet = Content.GetPlanet("jungle")!;
        var authored = new List<AuthoredCreature>
        {
            new() { Key = "hydra", NamePrefix = "Hydra", Habitat = CreatureHabitat.Land, BodyPlan = CreatureBodyPlan.Titan, Legs = 4, Heads = 3, WingPairs = 9, FinPairs = 0 },
        };
        var sp = CreatureGenerator.GenerateRoster(planet, 5, Gen6, authored).Single(s => s.Id == "au_hydra");
        Assert.Equal(3, sp.Heads);
        Assert.Equal(3, sp.WingPairs); // clamped into 1..3
        Assert.Equal(1, sp.FinPairs);
    }
}
