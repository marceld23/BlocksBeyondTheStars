// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The fins trait: a legless swimmer's only limbs. The point of the derivation (rather than a generator
/// roll) is that it consumes no RNG — so every world created before fins existed keeps its species
/// bit-for-bit, and a companion snapshot saved back then can be lifted on load from the fields it already
/// carries.
/// </summary>
public sealed class CreatureFinsTests
{
    private static CreatureSpecies Species(CreatureHabitat habitat, int legs, int voiceSeed,
        CreatureBodyPlan plan = CreatureBodyPlan.Standard)
        => new() { Id = "spX", Habitat = habitat, Legs = legs, VoiceSeed = voiceSeed, BodyPlan = plan };

    [Fact]
    public void OnlyWaterAndAmphibianBodiesEverGrowFins()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Land, 0, seed)));
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Land, 4, seed)));
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Air, 2, seed)));
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Cave, 6, seed)));
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Lava, 4, seed)));
        }
    }

    [Fact]
    public void AMedusaNeverGrowsFins_ItsBellIsItsWholeAnatomy()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Water, 0, seed, CreatureBodyPlan.Medusa)));
            Assert.False(CreatureMotion.FinsFor(Species(CreatureHabitat.Air, 0, seed, CreatureBodyPlan.Medusa)));
        }
    }

    [Fact]
    public void MostLeglessSwimmersHaveFins_AndOnlyAFewLeggedOnesDo()
    {
        int leglessWater = 0, leggedWater = 0, leglessAmphibian = 0, leggedAmphibian = 0;
        const int Samples = 1000;
        for (int seed = 0; seed < Samples; seed++)
        {
            if (CreatureMotion.FinsFor(Species(CreatureHabitat.Water, 0, seed))) leglessWater++;
            if (CreatureMotion.FinsFor(Species(CreatureHabitat.Water, 4, seed))) leggedWater++;
            if (CreatureMotion.FinsFor(Species(CreatureHabitat.Amphibian, 0, seed))) leglessAmphibian++;
            if (CreatureMotion.FinsFor(Species(CreatureHabitat.Amphibian, 4, seed))) leggedAmphibian++;
        }

        Assert.InRange(leglessWater / (float)Samples, 0.68f, 0.82f);      // ~75 %
        Assert.InRange(leggedWater / (float)Samples, 0.09f, 0.21f);      // ~15 %
        Assert.InRange(leglessAmphibian / (float)Samples, 0.68f, 0.82f); // legless amphibians swim too
        Assert.Equal(0, leggedAmphibian);                                 // a legged amphibian walks ashore
    }

    [Fact]
    public void TheDerivationIsStable_SoEveryClientDrawsTheSameBody()
    {
        var a = Species(CreatureHabitat.Water, 0, 12345);
        var b = Species(CreatureHabitat.Water, 0, 12345);
        Assert.Equal(CreatureMotion.FinsFor(a), CreatureMotion.FinsFor(b));

        // ...and it actually varies with the seed rather than being a constant.
        var flags = Enumerable.Range(0, 200).Select(s => CreatureMotion.FinsFor(Species(CreatureHabitat.Water, 4, s)));
        Assert.Contains(true, flags);
        Assert.Contains(false, flags);
    }

    [Fact]
    public void HasFins_LiftsASnapshotSavedBeforeTheTraitExisted()
    {
        // Exactly what a companion tamed before this feature looks like on load: the flag is false because
        // the field did not exist, but every input the derivation needs was persisted.
        var old = Species(CreatureHabitat.Water, 0, 12345);
        old.HasFins = false;

        Assert.Equal(CreatureMotion.FinsFor(old), CreatureMotion.HasFins(old));
        Assert.False(CreatureMotion.HasFins(null)); // a missing roster entry must not throw

        // A species that already carries the flag keeps it whatever the derivation says.
        var land = Species(CreatureHabitat.Land, 4, 7);
        land.HasFins = true;
        Assert.True(CreatureMotion.HasFins(land));
    }

    [Fact]
    public void EveryGeneratedSpeciesMatchesTheDerivation_WhichIsWhatMakesTheLiftSafe()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        foreach (string planetKey in new[] { "jungle", "ocean", "desert", "highland" })
        {
            var planet = content.GetPlanet(planetKey);
            if (planet == null)
            {
                continue;
            }

            for (long seed = 1; seed <= 25; seed++)
            {
                foreach (var sp in CreatureGenerator.GenerateRoster(planet, seed))
                {
                    Assert.Equal(CreatureMotion.FinsFor(sp), sp.HasFins);
                    if (sp.HasFins)
                    {
                        Assert.True(sp.Habitat is CreatureHabitat.Water or CreatureHabitat.Amphibian);
                        Assert.NotEqual(CreatureBodyPlan.Medusa, sp.BodyPlan);
                    }
                }
            }
        }
    }

    [Fact]
    public void AnOceanWorldActuallyProducesFinnedFauna()
    {
        var planet = ContentLoader.LoadFromDirectory(TestPaths.DataDir()).GetPlanet("ocean");
        Assert.NotNull(planet);

        bool anyFins = false;
        for (long seed = 1; seed <= 60 && !anyFins; seed++)
        {
            anyFins = CreatureGenerator.GenerateRoster(planet!, seed).Any(sp => sp.HasFins);
        }

        Assert.True(anyFins, "an ocean world should roll at least one finned species across 60 seeds");
    }
}
