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
/// The worm body plan (#2109, generation 18 — Marcel's finding "the sandworms have legs"): the worm roll comes last and only on
/// a generation-18 world, a worm is legless with a chain of 6–12 links and the slither style, and on a generation-18 world
/// nothing slithers on legs any more. Older rosters are bit for bit unchanged.
/// </summary>
public sealed class CreatureWormTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] Planets = { "jungle", "desert", "highland", "swamp", "savanna", "varied", "sand_sea" };
    private const int Gen18 = WorldDescription.WormGeneration;

    private static List<CreatureSpecies> Roster(string planetKey, long seed, int generation)
    {
        var planet = Content.GetPlanet(planetKey);
        return planet == null
            ? new List<CreatureSpecies>()
            : CreatureGenerator.GenerateRoster(planet, seed, generation, null).ToList();
    }

    [Fact]
    public void TheWormGatesOnGenerationEighteen_OlderRostersAreBitForBitUnchanged()
    {
        int worms = 0;
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 25; seed++)
            {
                var gen17 = Roster(key, seed, Gen18 - 1);
                var gen18 = Roster(key, seed, Gen18);
                Assert.Equal(gen17.Count, gen18.Count);
                for (int i = 0; i < gen17.Count; i++)
                {
                    Assert.NotEqual(CreatureBodyPlan.Worm, gen17[i].BodyPlan);
                    if (gen18[i].BodyPlan == CreatureBodyPlan.Worm)
                    {
                        worms++;
                        Assert.Equal(CreatureHabitat.Land, gen17[i].Habitat);
                        Assert.Equal(CreatureBodyPlan.Standard, gen17[i].BodyPlan);
                        continue;
                    }

                    // The one other generation-18 change: a legged long body no longer rolls the slither style — on every
                    // ground habitat (land, cave, lava, amphibian share the ground-mover roll). Such a species may differ
                    // from its generation-17 self in the style only; every other species is identical field for field.
                    if (gen17[i].Habitat != CreatureHabitat.Air && gen17[i].Habitat != CreatureHabitat.Water
                        && gen17[i].Legs > 0 && gen17[i].BodySegments >= 3)
                    {
                        Assert.NotEqual(LocomotionStyle.Slitherer, gen18[i].LocoStyle);
                        Assert.Equal(gen17[i].Legs, gen18[i].Legs);
                        Assert.Equal(gen17[i].BodySegments, gen18[i].BodySegments);
                        Assert.Equal(gen17[i].ColorRgb, gen18[i].ColorRgb);
                        continue;
                    }

                    Assert.Equal(JsonSerializer.Serialize(gen17[i]), JsonSerializer.Serialize(gen18[i]));
                }
            }
        }

        Assert.True(worms > 5, $"about one standard Land species in seven becomes a worm — found only {worms}");
    }

    [Fact]
    public void ARolledWorm_IsLegless_SlithersOnAChainOfLinks_AndIsSmall()
    {
        var worms = new List<CreatureSpecies>();
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 40; seed++)
            {
                worms.AddRange(Roster(key, seed, Gen18).Where(WormRules.IsWorm));
            }
        }

        Assert.NotEmpty(worms);
        foreach (var sp in worms)
        {
            Assert.Equal(0, sp.Legs);
            Assert.Equal(0, sp.Arms);
            Assert.Equal(LocomotionStyle.Slitherer, sp.LocoStyle);
            Assert.InRange(sp.BodySegments, WormRules.MinSegments, WormRules.MaxSegments);
            Assert.InRange(sp.Size, WormRules.MinSize, WormRules.MaxSize);
            Assert.InRange(sp.Speed, WormRules.MinSpeed, WormRules.MaxSpeed);
            Assert.False(sp.HasWings);
            Assert.False(sp.HasTail);
            Assert.False(sp.HasGasSac);
            Assert.False(sp.HasFins);
            Assert.Equal(1, sp.Heads);
            Assert.Equal(CreatureHabitat.Land, sp.Habitat);
            Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(sp));
            Assert.False(CreatureMotion.CanJump(sp));
            Assert.InRange(sp.SocialGroupSize, 1, 3);
        }
    }

    [Fact]
    public void OnAGenerationEighteenWorld_NothingSlithersOnLegs()
    {
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 40; seed++)
            {
                foreach (var sp in Roster(key, seed, Gen18))
                {
                    if (sp.Habitat == CreatureHabitat.Land && sp.LocoStyle == LocomotionStyle.Slitherer)
                    {
                        Assert.Equal(0, sp.Legs);
                        Assert.Equal(CreatureBodyPlan.Worm, sp.BodyPlan);
                    }
                }
            }
        }
    }
}
