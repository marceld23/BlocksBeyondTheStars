// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Reflection;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3974/#3975: the last five animals of Earth and the body parts they brought — the beak (chicken, parrot, eagle, the
/// dolphin's rostrum), the horizontal fluke, the water depth band and the whale's own body plan. The records, names, voices
/// and coats are covered with the other seven in <see cref="EarthAnimalsTests"/>.
/// </summary>
public sealed class EarthBirdsAndSeaAnimalsTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    [Fact]
    public void TheBeakAndTheComb_ReachTheWireAndTheCompanionList()
    {
        var chicken = Sp("chicken");
        Assert.Equal(CreatureBeakStyle.Cone, chicken.BeakStyle);
        Assert.True(chicken.HasComb);
        var wire = Wire(chicken);
        Assert.Equal("Cone", wire.BeakStyle);
        Assert.Equal(chicken.BeakLength, wire.BeakLength);
        Assert.Equal(chicken.BeakRgb, wire.BeakRgb);
        Assert.True(wire.HasComb);

        Assert.Equal("Hooked", Wire(Sp("parrot")).BeakStyle);
        Assert.Equal("Hooked", Wire(Sp("eagle")).BeakStyle);
        var dolphin = Wire(Sp("dolphin"));
        Assert.Equal("Rostrum", dolphin.BeakStyle);
        Assert.True(dolphin.TailFluke);

        var pet = new NetCompanion();
        CreatureWireMapping.WriteCompanionSpecies(pet, Sp("dolphin").Clone(), 1f); // a tamed animal's snapshot keeps them
        Assert.Equal(("Rostrum", true), (pet.BeakStyle, pet.TailFluke));

        // Every other species keeps the classic face.
        var plain = Wire(new CreatureSpecies());
        Assert.Equal(("None", 0f, 0, false, false), (plain.BeakStyle, plain.BeakLength, plain.BeakRgb, plain.HasComb, plain.TailFluke));
    }

    [Fact]
    public void TheBeak_ReachesPastTheFace_ByTheDepthTheBuilderDraws()
    {
        var chicken = Sp("chicken");
        var faceless = chicken.Clone();
        faceless.BeakStyle = CreatureBeakStyle.None;
        float unit = 0.5f * Math.Clamp(chicken.Size, 0.4f, 3f);
        Assert.Equal(CreatureFootprint.Radius(chicken, 1f) + CreatureBeaks.Depth(CreatureBeakStyle.Cone, chicken.BeakLength, unit),
            CreatureFootprint.HeadReach(chicken, 1f), 5);
        Assert.Equal(CreatureFootprint.Radius(faceless, 1f), CreatureFootprint.HeadReach(faceless, 1f)); // no beak, no change
        Assert.Equal(CreatureFootprint.Radius(chicken, 1f), CreatureFootprint.Radius(faceless, 1f));     // personal space unchanged

        Assert.Equal(CreatureBeakStyle.Cone, CreatureBeaks.Parse("Cone"));
        Assert.Equal(CreatureBeakStyle.None, CreatureBeaks.Parse("3"));      // a number is no style name on the wire
        Assert.Equal(CreatureBeakStyle.None, CreatureBeaks.Parse("Beaky"));  // a newer server's style
        Assert.Equal(CreatureBeakStyle.None, CreatureBeaks.Parse(null));
        Assert.Equal(CreatureBeaks.DefaultLength(CreatureBeakStyle.Hooked), CreatureBeaks.LengthOf(CreatureBeakStyle.Hooked, 0f));
        Assert.Equal(CreatureBeaks.MaxLength, CreatureBeaks.LengthOf(CreatureBeakStyle.Rostrum, 9f));
        Assert.Equal(0f, CreatureBeaks.LengthOf(CreatureBeakStyle.None, 0.5f));
    }

    [Fact]
    public void TheBirds_AreAGroundBirdAndTwoFliers_AndOnlyTheEagleBitesBack()
    {
        var chicken = Sp("chicken");
        Assert.Equal(MotionClass.Walker, CreatureMotion.ClassOf(chicken));
        Assert.True(CreatureMotion.Glides(chicken), "a startled chicken bounds like a ground bird");
        Assert.Equal(4, chicken.SocialGroupSize);
        Assert.Equal(CreatureDropKind.Food, chicken.DropKind);

        var parrot = Sp("parrot");
        var eagle = Sp("eagle");
        Assert.Equal(MotionClass.Flier, CreatureMotion.ClassOf(parrot));
        Assert.Equal(MotionClass.Flier, CreatureMotion.ClassOf(eagle));
        Assert.False(CreatureBehaviour.RetaliatesWhenAttacked(parrot));
        Assert.True(CreatureBehaviour.RetaliatesWhenAttacked(eagle));
        Assert.Equal(CreatureTemperament.Skittish, CreatureBehaviour.EffectiveTemperament(eagle, provoked: false));
        Assert.Equal(1, eagle.SocialGroupSize);
        Assert.Equal(new[] { "alpine" }, eagle.FloraThemes);
        Assert.Equal(new[] { "tropical" }, parrot.FloraThemes);
    }

    [Fact]
    public void TheDolphin_AlwaysCarriesItsFluke_AndKeepsToTheCoastalBand()
    {
        var dolphin = Sp("dolphin");
        for (int seed = -50; seed < 50; seed++)
        {
            var other = dolphin.Clone();
            other.VoiceSeed = seed * 7919;
            other.HasFins = false;
            Assert.True(CreatureMotion.FinsFor(other), "a fluked swimmer never rolls finless");
        }

        Assert.Equal(CreatureTemperament.Passive, dolphin.Temperament);
        Assert.Equal(4, dolphin.SocialGroupSize);
        Assert.False(WaterDepthBand.SeatsAt(dolphin, 2));
        Assert.True(WaterDepthBand.SeatsAt(dolphin, 3));
        Assert.True(WaterDepthBand.SeatsAt(dolphin, 16));
        Assert.False(WaterDepthBand.SeatsAt(dolphin, 17));

        // A swimmer without a band keeps the classic rule exactly: room to swim, no upper bound, no step gate.
        var fish = new CreatureSpecies { Habitat = CreatureHabitat.Water };
        Assert.Equal(CreatureBehaviour.MinSwimDepth, WaterDepthBand.MinSpawnDepth(fish));
        Assert.Equal(int.MaxValue, WaterDepthBand.MaxSpawnDepth(fish));
        Assert.False(WaterDepthBand.StepTooShallow(fish, 9, 2));
        Assert.Equal(1, WaterDepthBand.MinSpawnDepth(new CreatureSpecies { Habitat = CreatureHabitat.Amphibian }));
    }

    [Fact]
    public void TheWhale_HasItsOwnPlanRow_StaysInTheDeepSea_AndIsABigCalmHurtableAnimal()
    {
        var row = CreatureBodyPlans.Of(CreatureBodyPlan.Whale);
        Assert.Equal(CreatureBodyPlan.Whale, row.Plan);
        Assert.Same(row, CreatureBodyPlans.Of("Whale"));
        Assert.True(row.Solid && row.LongLeash && row.Submerged);
        Assert.False(row.Giant);
        Assert.Equal(BodyFootprint.Whale, row.Footprint);
        Assert.Equal(BodyHeadReach.None, row.HeadReach);
        Assert.Equal(CreatureBodyPlan.Quali + 1, CreatureBodyPlan.Whale); // appended: the numbers are persisted

        // The two refactored flags still say what the name comparisons said.
        foreach (CreatureBodyPlan plan in Enum.GetValues(typeof(CreatureBodyPlan)))
        {
            var r = CreatureBodyPlans.Of(plan);
            Assert.Equal(plan is CreatureBodyPlan.Arachnid or CreatureBodyPlan.Soundrak or CreatureBodyPlan.Whale, r.Solid);
            Assert.Equal(plan is CreatureBodyPlan.Titan or CreatureBodyPlan.Arachnid or CreatureBodyPlan.Whale, r.LongLeash);
            Assert.Equal(plan == CreatureBodyPlan.Whale, r.Submerged);
        }

        var whale = Sp("whale");
        Assert.False(whale.IsGiant); // the spawner places it like any animal
        Assert.False(whale.Hostile);
        Assert.Equal(CreatureTemperament.Passive, whale.Temperament);
        Assert.True(whale.MaxHealth >= 300f, "hurtable, but it takes a lot");
        Assert.Equal(0f, whale.AttackDamage);
        Assert.NotEqual(CreatureDropKind.Food, whale.DropKind);
        Assert.InRange(WhaleRules.BodyLength(whale.Size), 15f, 25f);
        Assert.Equal(WhaleRules.FootprintRadius(whale.Size), CreatureFootprint.Radius(whale, 1f));
        Assert.InRange(CreatureFootprint.Radius(whale, 1.3f), 0f, CreatureFootprint.MaxRadius); // even its biggest individual
        Assert.Equal(0f, CreatureFootprint.HeadReach(whale, 1f));

        Assert.False(WaterDepthBand.SeatsAt(whale, 11));
        Assert.True(WaterDepthBand.SeatsAt(whale, GiantRules.LeviathanMinDepth));
        Assert.True(WaterDepthBand.StepTooShallow(whale, 14, 11));  // out of the deep sea: no
        Assert.True(WaterDepthBand.StepTooShallow(whale, 11, 10));  // further into the shallows: no
        Assert.False(WaterDepthBand.StepTooShallow(whale, 10, 11)); // back toward the deep: always
        Assert.False(WaterDepthBand.StepTooShallow(whale, 10, 10)); // along the shallows: never trapped
        Assert.False(WaterDepthBand.StepTooShallow(whale, 14, 13));
    }

    [Fact]
    public void ContentLoad_RejectsAnEmptyWaterBand()
    {
        var probe = new AuthoredCreature { Key = "probe", DropItem = string.Empty, MinWaterDepth = 12, MaxWaterDepth = 5 };
        var content = new GameContent(
            blocks: Array.Empty<BlockDefinition>(), items: Array.Empty<ItemDefinition>(), recipes: Array.Empty<RecipeDefinition>(),
            blueprints: Array.Empty<BlueprintDefinition>(), shipModules: Array.Empty<ShipModuleDefinition>(),
            locales: new Dictionary<GameLocale, Dictionary<string, string>>(), authoredCreatures: new[] { probe });
        var ex = Assert.Throws<ContentValidationException>(content.Validate);
        Assert.Contains(ex.Problems, p => p.Contains("'probe'") && p.Contains("water band"));
    }

    // ---------------- helpers ----------------

    private static CreatureSpecies Sp(string key)
        => CreatureGenerator.MakeAuthored(Shipped.Value.AuthoredCreatures[key], EarthStandIn(Shipped.Value), 7);

    /// <summary>A copy of the temperate multi-biome type under the Earth key (the real one comes with another package).</summary>
    private static PlanetType EarthStandIn(GameContent content)
    {
        var varied = content.GetPlanet("varied")!;
        var copy = (PlanetType)typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(varied, null)!;
        copy.Key = "sol_earth";
        return copy;
    }

    private static NetCreature Wire(CreatureSpecies sp)
    {
        var n = new NetCreature();
        CreatureWireMapping.WriteSpecies(n, sp, ("sol_earth", 4711), 1);
        return n;
    }
}
