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
/// The arachnid body plan (#2009): a speeder-sized eight-legger rolled into the Land pool of a generation-10 world,
/// with a rolled head shape (sometimes a pyramid, and then not always the same one), rolled looks and the normal
/// temperament roll — and the pure rules the server and the client share (lurking, the collision height, the head tiers).
/// </summary>
public sealed class CreatureArachnidTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly string[] Planets = { "jungle", "desert", "highland", "swamp", "ocean" };
    private const int Gen10 = WorldDescription.ArachnidGeneration;

    private static IEnumerable<CreatureSpecies> Roster(string planetKey, long seed, int generation)
    {
        var planet = Content.GetPlanet(planetKey);
        return planet == null ? Enumerable.Empty<CreatureSpecies>() : CreatureGenerator.GenerateRoster(planet, seed, generation);
    }

    private static CreatureSpecies Arachnid(CreatureTemperament temperament) => new()
    {
        Id = "spX",
        Habitat = CreatureHabitat.Land,
        BodyPlan = CreatureBodyPlan.Arachnid,
        Temperament = temperament,
        Legs = 8,
        Size = 3.3f,
        LocoStyle = LocomotionStyle.Prowler,
    };

    [Fact]
    public void ArachnidPlan_HoldsItsInvariants_AndActuallyOccurs()
    {
        // Rare (one in twelve standard Land species), but across five planet types and 80 seeds it must show up —
        // and every one must hold the plan's shape. Temperament, activity and colours are the normal roll (Marcel's
        // decision), so the test asserts the body, not the temper.
        int arachnids = 0;
        var heads = new HashSet<CreatureHeadShape>();
        var hides = new[] { "chitin", "plated", "spined", "banded", "shaggy", "mottled" };
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 80; seed++)
            {
                foreach (var sp in Roster(key, seed, Gen10))
                {
                    if (sp.BodyPlan != CreatureBodyPlan.Arachnid)
                    {
                        Assert.Equal(CreatureHeadShape.Box, sp.HeadShape); // only the plan rolls a head shape
                        continue;
                    }

                    arachnids++;
                    heads.Add(sp.HeadShape);
                    Assert.Equal(CreatureHabitat.Land, sp.Habitat);
                    Assert.InRange(sp.Size, ArachnidRules.MinSize, ArachnidRules.MaxSize);
                    Assert.Equal(8, sp.Legs);
                    Assert.Equal(2, sp.BodySegments);
                    Assert.Equal(1, sp.Heads);
                    Assert.False(sp.HasWings);
                    Assert.False(sp.HasGasSac);
                    Assert.False(sp.HasTail);
                    Assert.False(sp.EyeStalks);
                    Assert.Equal(0, sp.Tentacles);
                    Assert.Contains(sp.Eyes, new[] { 2, 4, 6, 8 });
                    Assert.Contains(sp.Horns, new[] { 0, 2 });
                    Assert.Contains(sp.Hide, hides);
                    Assert.True(sp.MaxHealth >= 80f, $"{key}/{seed}: {sp.MaxHealth} HP — a real fight");
                    Assert.True(sp.AttackDamage >= 3f, "always armed, used only when hostile or provoked");
                    Assert.InRange(sp.Speed, 2f, 4f);
                    Assert.InRange(sp.DropCount, 2, 4);
                    Assert.NotEqual(LocomotionStyle.Hopper, sp.LocoStyle);
                    Assert.NotEqual(LocomotionStyle.Slitherer, sp.LocoStyle);
                    Assert.True(sp.Temperament == CreatureTemperament.PackHunter ? sp.SocialGroupSize >= 2 : sp.SocialGroupSize == 1,
                        "solitary unless it hunts as a pack");
                    Assert.Equal(MotionClass.Crawler, CreatureMotion.ClassOf(sp));
                    Assert.False(CreatureMotion.CanJump(sp));
                    Assert.False(sp.IsGiant); // a roster animal, not a one-per-world giant
                }
            }
        }

        Assert.True(arachnids >= 20, $"the plan must actually occur — {arachnids} across {Planets.Length * 80} rosters");
        // "Sometimes a pyramid head, and then not always the same one": the box and every pyramid kind occur.
        foreach (CreatureHeadShape shape in System.Enum.GetValues(typeof(CreatureHeadShape)))
        {
            Assert.Contains(shape, heads);
        }
    }

    [Fact]
    public void TheWaveGatesOnGenerationTen_OlderRostersAreBitForBitUnchanged()
    {
        // The roll is the LAST draw of a species and is gated on the generation, so a generation-9 roster carries no
        // arachnid, and on a generation-10 roster every species that did not become one equals its generation-9
        // self field for field (the draw is consumed after everything that reads the RNG).
        int converted = 0;
        foreach (string key in Planets)
        {
            for (long seed = 1; seed <= 15; seed++)
            {
                var gen9 = Roster(key, seed, Gen10 - 1).ToList();
                var gen10 = Roster(key, seed, Gen10).ToList();
                Assert.Equal(gen9.Count, gen10.Count);
                for (int i = 0; i < gen9.Count; i++)
                {
                    Assert.NotEqual(CreatureBodyPlan.Arachnid, gen9[i].BodyPlan);
                    Assert.Equal(CreatureHeadShape.Box, gen9[i].HeadShape);
                    if (gen10[i].BodyPlan == CreatureBodyPlan.Arachnid)
                    {
                        converted++;
                        Assert.Equal(CreatureHabitat.Land, gen9[i].Habitat);
                        Assert.Equal(CreatureBodyPlan.Standard, gen9[i].BodyPlan);
                        Assert.Equal(gen9[i].Temperament, gen10[i].Temperament); // the normal roll, untouched by the plan
                        Assert.Equal(gen9[i].Activity, gen10[i].Activity);
                        Assert.Equal(gen9[i].ColorRgb, gen10[i].ColorRgb);
                        Assert.Equal(gen9[i].Name, gen10[i].Name);
                    }
                    else
                    {
                        Assert.Equal(JsonSerializer.Serialize(gen9[i]), JsonSerializer.Serialize(gen10[i]));
                    }
                }
            }
        }

        Assert.True(converted > 0, "at least one species across the sample must have rolled the plan");
    }

    [Fact]
    public void GenerationTen_IsDeterministic_AndGenerateArachnidAlwaysYieldsOne()
    {
        var planet = Content.GetPlanet("jungle")!;
        Assert.Equal(
            JsonSerializer.Serialize(CreatureGenerator.GenerateRoster(planet, 4242, Gen10)),
            JsonSerializer.Serialize(CreatureGenerator.GenerateRoster(planet, 4242, Gen10)));

        for (long seed = 1; seed <= 30; seed++)
        {
            var a = CreatureGenerator.GenerateArachnid(planet, seed);
            var b = CreatureGenerator.GenerateArachnid(planet, seed);
            Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
            Assert.Equal(CreatureBodyPlan.Arachnid, a.BodyPlan);
            Assert.Equal(CreatureHabitat.Land, a.Habitat);
            Assert.Equal(8, a.Legs);
            Assert.InRange(a.Size, ArachnidRules.MinSize, ArachnidRules.MaxSize);
            Assert.DoesNotContain(a.Id, new[] { "sp0", "sp1", "sp2", "sp3", "sp4", "sp5", "sp6", "sp7", "sp8" }); // never a roster slot's id
        }
    }

    [Fact]
    public void AnAuthoredArachnid_KeepsItsHeadShape()
    {
        // A worksheet creature may use the plan (#2003-style entries): the record's head shape reaches the roster entry.
        var planet = Content.GetPlanet("jungle")!;
        var authored = new[]
        {
            new AuthoredCreature { Key = "pyr", NamePrefix = "Pyra", BodyPlan = CreatureBodyPlan.Arachnid, HeadShape = CreatureHeadShape.Ziggurat, Legs = 8, Size = 3.2f },
        };
        var roster = CreatureGenerator.GenerateRoster(planet, 7, Gen10, authored);
        var pyra = Assert.Single(roster, sp => sp.Id == "au_pyr");
        Assert.Equal(CreatureBodyPlan.Arachnid, pyra.BodyPlan);
        Assert.Equal(CreatureHeadShape.Ziggurat, pyra.HeadShape);
    }

    [Fact]
    public void Lurks_OnlyWhenItHuntsOrDefends_AndOnlyTheArachnid()
    {
        Assert.True(ArachnidRules.Lurks(Arachnid(CreatureTemperament.Aggressive)));
        Assert.True(ArachnidRules.Lurks(Arachnid(CreatureTemperament.PackHunter)));
        Assert.True(ArachnidRules.Lurks(Arachnid(CreatureTemperament.Territorial)));
        Assert.False(ArachnidRules.Lurks(Arachnid(CreatureTemperament.Passive)));
        Assert.False(ArachnidRules.Lurks(Arachnid(CreatureTemperament.Skittish)));

        var titan = Arachnid(CreatureTemperament.Aggressive);
        titan.BodyPlan = CreatureBodyPlan.Titan;
        Assert.False(ArachnidRules.Lurks(titan));
    }

    [Fact]
    public void BodyHeightCells_IsLowAndWide()
    {
        // A speeder-sized arachnid is three cells tall for collision (Size × 0.8), not the six or seven the upright
        // rule (Size × 1.8) would gate it as — so it can pass under the overhangs its body actually fits under.
        Assert.Equal(3, ArachnidRules.BodyHeightCells(ArachnidRules.MinSize, 2, 8));
        Assert.Equal(3, ArachnidRules.BodyHeightCells(ArachnidRules.MaxSize, 2, 8));
        Assert.Equal(2, ArachnidRules.BodyHeightCells(1f, 2, 8));   // never below the floor
        Assert.Equal(8, ArachnidRules.BodyHeightCells(20f, 2, 8));  // never above the ceiling
    }

    [Theory]
    [InlineData(CreatureHeadShape.Pyramid)]
    [InlineData(CreatureHeadShape.Spire)]
    [InlineData(CreatureHeadShape.Frustum)]
    [InlineData(CreatureHeadShape.Ziggurat)]
    public void HeadTiers_StackFromBaseToTop_AndTheSlopeIsWhereTheEyesGo(CreatureHeadShape shape)
    {
        Assert.True(ArachnidRules.IsPyramid(shape));
        var tiers = ArachnidRules.HeadTiers(shape);
        Assert.NotEmpty(tiers);
        Assert.Equal(0f, tiers[0].Y0);
        Assert.Equal(1f, tiers[tiers.Count - 1].Y1);
        for (int i = 0; i < tiers.Count; i++)
        {
            Assert.True(tiers[i].Y1 > tiers[i].Y0, "a tier has height");
            Assert.True(tiers[i].Bottom >= tiers[i].Top, "a tier narrows upward or stays");
            Assert.InRange(tiers[i].Bottom, 0f, 1f);
            if (i > 0)
            {
                Assert.Equal(tiers[i - 1].Y1, tiers[i].Y0); // contiguous
            }
        }

        // The base is the head's full footprint (or the spire's narrower one), the top an apex — except the frustum.
        Assert.Equal(tiers[0].Bottom, ArachnidRules.FootprintAt(shape, 0f));
        Assert.Equal(tiers[tiers.Count - 1].Top, ArachnidRules.FootprintAt(shape, 1f));
        Assert.Equal(shape == CreatureHeadShape.Frustum, tiers[tiers.Count - 1].Top > 0f);
        Assert.Equal(shape == CreatureHeadShape.Ziggurat ? 2 : 1, tiers.Count);
        Assert.True(ArachnidRules.FootprintAt(shape, 0.25f) >= ArachnidRules.FootprintAt(shape, 0.75f), "the slope recedes with height");
        Assert.True(ArachnidRules.HeightScale(shape) >= 1f);
    }

    [Fact]
    public void TheBoxHead_HasNoTiers_AndAFullFootprint()
    {
        Assert.False(ArachnidRules.IsPyramid(CreatureHeadShape.Box));
        Assert.Empty(ArachnidRules.HeadTiers(CreatureHeadShape.Box));
        Assert.Equal(1f, ArachnidRules.FootprintAt(CreatureHeadShape.Box, 0.5f));
        Assert.Equal(1f, ArachnidRules.HeightScale(CreatureHeadShape.Box));
    }

    [Fact]
    public void Voice_HissesAndChitters_NeverBellows()
    {
        // Its size alone would put it in the titan's bellow pool (Size ≥ 2.6); the plan branch comes first.
        for (int seed = 1; seed <= 50; seed++)
        {
            var traits = new VoiceTraits("Land", "Territorial", "Arachnid", 3.3f, 8, 6, 2, 2, 0, false, false);
            var v = CreatureVoices.Derive(seed, traits);
            Assert.Contains(v.Call, new[] { "creature_call_hiss", "creature_call_sizzle", "creature_call_chitter", "creature_call_click" });
            Assert.InRange(v.Pulses, 2, 4);
            Assert.True(v.CadenceMin >= 9f && v.CadenceMin < 14f, $"seed {seed}: cadence {v.CadenceMin} — quicker than a titan");
        }
    }
}
