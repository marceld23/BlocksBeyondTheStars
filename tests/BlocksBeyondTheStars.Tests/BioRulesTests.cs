// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The bio lab's rules as pure functions (#2200, #2204–#2206, #2208): a species' profile follows from its seed and its
/// context, a mix is computed from what goes in, a changed item never leaves its tier — and all of it is deterministic,
/// so the server and every client agree without talking.
/// </summary>
public sealed class BioRulesTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static BioContext Context(BioTag tags, int points = 0, BioKind kind = BioKind.Animal, int toxicity = 0)
        => new() { Kind = kind, Tags = (ulong)tags, RarityPoints = points, Toxicity = toxicity, Carrier = BioCarrier.Tissue };

    // ---------------- Profiles ----------------

    [Fact]
    public void AProfile_IsTheSameEveryTime_AndPinnedForAKnownSeed()
    {
        var context = Context(BioTag.Lava | BioTag.Hot | BioTag.Glow, points: 6);
        var first = BioProfiles.Derive(0x9F3A11C2, context);
        var second = BioProfiles.Derive(0x9F3A11C2, context);

        Assert.Equal(first.Effect, second.Effect);
        Assert.Equal(first.Level, second.Level);
        Assert.Equal(first.Side, second.Side);
        Assert.Equal(first.Group, second.Group);
        Assert.Equal(first.Substance, second.Substance);

        // Golden values: a change to the derivation changes every sample ever taken — make it on purpose.
        Assert.Equal(BioEffect.HeatWard, first.Effect);
        Assert.Equal(
            "9|2|4|None|0|ColdSensitive|60|Xezol",
            $"{first.Level}|{first.Rarity}|{first.Group}|{first.Side}|{first.SideLevel}|{first.Thermal}|{first.DurationSeconds}|{first.Substance}");
        Assert.InRange(first.Level, 1, BioRules.MaxLevel);
        Assert.NotEqual(BioThermal.HeatSensitive, first.Thermal); // a heat ward never falls apart in the heat
        Assert.False(string.IsNullOrEmpty(first.Substance));
    }

    [Fact]
    public void TheContext_MakesAnEffectLikely_ALavaDwellerMostlyWardsHeat()
    {
        var lava = Context(BioTag.Lava | BioTag.Hot | BioTag.Glow | BioTag.Skittish, points: 4);
        var plain = Context(BioTag.None);
        int heatFromLava = 0, heatFromPlain = 0;
        const int Samples = 2000;
        for (uint seed = 1; seed <= Samples; seed++)
        {
            heatFromLava += BioProfiles.Derive(seed * 2654435761u, lava).Effect == BioEffect.HeatWard ? 1 : 0;
            heatFromPlain += BioProfiles.Derive(seed * 2654435761u, plain).Effect == BioEffect.HeatWard ? 1 : 0;
        }

        Assert.InRange(heatFromLava, Samples * 40 / 100, Samples * 65 / 100); // about half: likely, never certain
        Assert.InRange(heatFromPlain, 0, Samples * 12 / 100);                 // one effect among nineteen
    }

    [Fact]
    public void Rarity_ComesFromScarcity_LegendaryNeedsSeveralHurdles()
    {
        Assert.Equal(0, BioRules.RarityOfPoints(2));
        Assert.Equal(1, BioRules.RarityOfPoints(3));
        Assert.Equal(2, BioRules.RarityOfPoints(6));
        Assert.Equal(3, BioRules.RarityOfPoints(8));
        Assert.Equal(4, BioRules.RarityOfPoints(9));

        // The seed moves the tier by one at most: a common species is never epic, a 9-point one never below epic.
        for (uint seed = 1; seed <= 500; seed++)
        {
            Assert.InRange(BioProfiles.Derive(seed, Context(BioTag.None, points: 0)).Rarity, 0, 1);
            Assert.InRange(BioProfiles.Derive(seed, Context(BioTag.None, points: 9)).Rarity, 3, 4);
        }
    }

    [Fact]
    public void LevelAndSideEffect_StayInBounds_AndASideEffectIsNeverTheEffectsOwnOpposite()
    {
        var tags = new[] { BioTag.None, BioTag.Fast | BioTag.Darter, BioTag.Water, BioTag.Cold, BioTag.Titan | BioTag.Big };
        for (uint seed = 1; seed <= 3000; seed++)
        {
            var profile = BioProfiles.Derive(seed * 7919u, Context(tags[seed % tags.Length], points: (int)(seed % 12)));
            var (low, high) = BioRules.LevelBand(profile.Rarity);
            Assert.InRange(profile.Level, low, high);
            Assert.InRange(profile.SideLevel, 0, 3);
            Assert.Equal(profile.Side == BioSideEffect.None, profile.SideLevel == 0);
            Assert.InRange(profile.Group, 0, BioRules.Groups - 1);
            Assert.False(profile.Effect == BioEffect.Speed && profile.Side == BioSideEffect.Sluggish);
            Assert.False(profile.Effect == BioEffect.Jump && profile.Side == BioSideEffect.Leaden);
            Assert.False(profile.Effect == BioEffect.Reflex && profile.Side == BioSideEffect.SlowReflex);
            Assert.False(profile.Effect == BioEffect.ColdWard && profile.Thermal == BioThermal.ColdSensitive);
        }
    }

    [Fact]
    public void TheStrengthCurve_Flattens_AndEveryEffectHasACap()
    {
        foreach (var effect in BioRules.Effects)
        {
            float prev = 0f;
            for (int level = 1; level <= BioRules.MaxLevel; level++)
            {
                float m = BioRules.Magnitude(effect, level);
                Assert.True(m >= prev, $"{effect} must not get weaker with a higher level");
                Assert.True(m <= Math.Max(BioRules.Cap(effect), 1f) + 0.0001f, $"{effect} level {level} exceeds its cap");
                prev = m;
            }
        }

        // Level 15 is about twice level 5 — never three times.
        float ratio = BioRules.Magnitude(BioEffect.Speed, 15) / BioRules.Magnitude(BioEffect.Speed, 5);
        Assert.InRange(ratio, 1.5f, 2.2f);
    }

    [Fact]
    public void Seeds_AreStable_AndNeverZero()
    {
        Assert.Equal(BioHash.CreatureSeed(1234), BioHash.CreatureSeed(1234));
        Assert.NotEqual(BioHash.CreatureSeed(1234), BioHash.CreatureSeed(1235));
        Assert.Equal(BioHash.FloraSeed(42, "jungle", "flora_bush"), BioHash.FloraSeed(42, "jungle", "flora_bush"));
        Assert.NotEqual(BioHash.FloraSeed(42, "jungle", "flora_bush"), BioHash.FloraSeed(43, "jungle", "flora_bush"));
        Assert.Equal(BioHash.AuthoredSeed("leni"), BioHash.AuthoredSeed("leni"));
        Assert.Equal(BioHash.CrossSeed(7, 9), BioHash.CrossSeed(9, 7)); // A × B is B × A
        Assert.NotEqual(0u, BioHash.Fold(0));
        Assert.NotEqual(0u, BioHash.DepositSeed(0, string.Empty));
    }

    [Fact]
    public void EveryEffect_BelongsToExactlyOneFamily_AndNoFamilyIsEmpty()
    {
        // #2300: the sample case filters by family and the Codex groups by it — an effect without one would vanish.
        var defined = Enum.GetValues(typeof(BioEffect)).Cast<BioEffect>().Where(e => e != BioEffect.None).ToArray();
        Assert.Equal(defined.OrderBy(e => e), BioRules.Effects.OrderBy(e => e));
        foreach (var effect in defined)
        {
            Assert.Contains(BioRules.Family(effect), BioRules.Families);
        }

        Assert.Equal(BioEffectFamily.None, BioRules.Family(BioEffect.None));
        foreach (var family in BioRules.Families)
        {
            Assert.Contains(defined, e => BioRules.Family(e) == family);
        }

        var all = Enum.GetValues(typeof(BioEffectFamily)).Cast<BioEffectFamily>().Where(f => f != BioEffectFamily.None);
        Assert.Equal(all.OrderBy(f => f), BioRules.Families.OrderBy(f => f));
        Assert.Equal(BioEffectFamily.Protection, BioRules.Family(BioEffect.HeatWard));
        Assert.Equal(BioEffectFamily.Senses, BioRules.Family(BioEffect.Stealth));
    }

    // ---------------- The mixer ----------------

    private static BioProfile Sample(BioEffect effect, int level, int group, BioSideEffect side = BioSideEffect.None, int sideLevel = 0,
        int toxicity = 0, BioCarrier carrier = BioCarrier.Fibre, int duration = 120, BioThermal thermal = BioThermal.Stable)
        => new()
        {
            Seed = 1,
            Effect = effect,
            Level = level,
            Group = group,
            Side = side,
            SideLevel = sideLevel,
            Toxicity = toxicity,
            Carrier = carrier,
            DurationSeconds = duration,
            Thermal = thermal,
            Substance = "Testin",
        };

    private static MaterialProfile Material(int purity, params (MatTrait Trait, int Level)[] traits)
    {
        var m = new MaterialProfile { Purity = purity };
        foreach (var (trait, level) in traits)
        {
            m.Levels[(int)trait] = level;
        }

        return m;
    }

    [Fact]
    public void AnExtract_IsTheSampleItself_AndTheCarrierTradesStrengthForTime()
    {
        var sample = Sample(BioEffect.Regeneration, 9, group: 0);
        var extract = Synthesis.Compute(new SynthesisInput { Active = sample, Form = BioForm.Injector });
        var gel = Synthesis.Compute(new SynthesisInput { Active = sample, Form = BioForm.Gel });
        var bar = Synthesis.Compute(new SynthesisInput { Active = sample, Form = BioForm.Bar });

        Assert.False(extract.Failed);
        Assert.Equal(BioEffect.Regeneration, extract.Effect);
        Assert.Equal(9, extract.Level);          // full strength …
        Assert.Equal(3, gel.Level);              // … a third in a gel …
        Assert.Equal(5, bar.Level);              // … half in a bar (rounded up)
        Assert.True(gel.DurationSeconds > bar.DurationSeconds && bar.DurationSeconds > extract.DurationSeconds);
        Assert.InRange(gel.DurationSeconds, BioRules.MinDuration, BioRules.MaxDuration);
    }

    [Fact]
    public void TheSameInputs_AlwaysGiveTheSameResult()
    {
        var input = new SynthesisInput
        {
            Active = Sample(BioEffect.Speed, 8, group: 0, BioSideEffect.Hungry, 2),
            Form = BioForm.Capsule,
            Stabiliser = Material(4, (MatTrait.Conductive, 3)),
            Modifier = Sample(BioEffect.Jump, 6, group: 1),
        };
        Assert.Equal(Synthesis.Compute(input).ToPayload(), Synthesis.Compute(input).ToPayload());
    }

    [Fact]
    public void AMatchingStabiliser_TakesTheSideEffectAway_ForAQuarterOfTheStrength()
    {
        var sample = Sample(BioEffect.ColdWard, 12, group: 0, BioSideEffect.Sluggish, 2);
        var raw = Synthesis.Compute(new SynthesisInput { Active = sample, Form = BioForm.Injector });
        var counter = BioRules.Counter(BioSideEffect.Sluggish);
        var clean = Synthesis.Compute(new SynthesisInput { Active = sample, Form = BioForm.Injector, Stabiliser = Material(3, (counter, 2)) });
        var steadied = Synthesis.Compute(new SynthesisInput { Active = sample, Form = BioForm.Injector, Stabiliser = Material(3, (MatTrait.Heavy, 2)) });

        Assert.Equal(BioSideEffect.Sluggish, raw.Side);
        Assert.Equal(12, raw.Level);
        Assert.Equal(BioSideEffect.None, clean.Side);
        Assert.Equal(9, clean.Level);                       // 12 - 12/4
        Assert.Equal(12, steadied.Level);                   // a stabiliser that does not counter the catch costs nothing …
        Assert.True(steadied.Stability > raw.Stability);    // … and still steadies the mix
    }

    [Fact]
    public void EveryReaction_DoesWhatItsNameSays()
    {
        var active = Sample(BioEffect.Speed, 8, group: 0);
        Compound Mix(int modifierGroup, BioEffect modifierEffect = BioEffect.Jump)
            => Synthesis.Compute(new SynthesisInput { Active = active, Form = BioForm.Injector, Modifier = Sample(modifierEffect, 6, modifierGroup) });

        Assert.Equal(BioReaction.Amplify, BioRules.Reaction(0, 1));
        Assert.Equal(10, Mix(1).Level);

        Assert.Equal(BioReaction.Inhibit, BioRules.Reaction(0, 4));
        Assert.Equal(6, Mix(4).Level);

        Assert.Equal(BioReaction.Couple, BioRules.Reaction(0, 3));
        var coupled = Mix(3);
        Assert.Equal(BioEffect.Jump, coupled.Secondary);
        Assert.Equal(3, coupled.SecondaryLevel);            // half the modifier's strength
        Assert.Equal(9, Mix(3, BioEffect.Speed).Level);     // the same effect twice just adds a level

        Assert.Equal(BioReaction.Transmute, BioRules.Reaction(0, 6));
        Assert.Equal(BioRules.Transmuted(BioEffect.Speed), Mix(6).Effect);

        // The table is the same from both sides, and nothing ever turns into stealth.
        for (int a = 0; a < BioRules.Groups; a++)
        {
            for (int b = 0; b < BioRules.Groups; b++)
            {
                Assert.Equal(BioRules.Reaction(a, b), BioRules.Reaction(b, a));
            }
        }

        Assert.DoesNotContain(BioRules.Effects, e => BioRules.Transmuted(e) == BioEffect.Stealth);
    }

    [Fact]
    public void AToxicSample_SpoilsTheMix_UnlessItWasWashed()
    {
        var mild = Sample(BioEffect.Strength, 8, group: 0, toxicity: 1);
        var strong = Sample(BioEffect.Strength, 8, group: 0, toxicity: 3);

        var spoiled = Synthesis.Compute(new SynthesisInput { Active = mild, Form = BioForm.Injector });
        Assert.False(spoiled.Failed);
        Assert.Equal(BioSideEffect.Tired, spoiled.Side);    // an unwashed toxic sample leaves its mark …
        Assert.Equal(7, spoiled.Level);                     // … and loses strength

        Assert.True(Synthesis.Compute(new SynthesisInput { Active = strong, Form = BioForm.Injector }).Failed); // below 30 % it falls apart

        var washed = Synthesis.Compute(new SynthesisInput { Active = strong, ActiveCleaned = true, Form = BioForm.Injector });
        Assert.False(washed.Failed);
        Assert.Equal(BioSideEffect.None, washed.Side);
        Assert.Equal(8, washed.Level);
    }

    [Fact]
    public void Stealth_IsShort_WhateverTheCarrier()
    {
        var gel = Synthesis.Compute(new SynthesisInput { Active = Sample(BioEffect.Stealth, 12, group: 0, duration: 480), Form = BioForm.Gel });
        Assert.InRange(gel.DurationSeconds, BioRules.MinDuration, BioRules.StealthMaxDuration);
    }

    [Fact]
    public void APreparationsKey_CarriesItsCompound_RoundTrip()
    {
        var compound = Synthesis.Compute(new SynthesisInput
        {
            Active = Sample(BioEffect.NightSight, 11, group: 0, BioSideEffect.Tired, 3, thermal: BioThermal.ColdSensitive),
            Form = BioForm.Capsule,
            Modifier = Sample(BioEffect.Perception, 8, group: 3),
        });
        string key = BioItems.PreparationItem(compound);
        var back = BioItems.CompoundOf(key);

        Assert.StartsWith("prep_capsule#x", key);
        Assert.NotNull(back);
        Assert.Equal(compound.Effect, back!.Effect);
        Assert.Equal(compound.Level, back.Level);
        Assert.Equal(compound.Side, back.Side);
        Assert.Equal(compound.SideLevel, back.SideLevel);
        Assert.Equal(compound.Thermal, back.Thermal);
        Assert.Equal(compound.DurationSeconds, back.DurationSeconds);
        Assert.Equal(compound.Secondary, back.Secondary);
        Assert.Equal(compound.SecondaryLevel, back.SecondaryLevel);
        Assert.Equal(compound.Name, back.Name);                     // the coined name follows from what it does
        Assert.Equal(Content.GetItem("prep_capsule"), Content.GetItem(key)); // the definition lookup strips the payload
        Assert.Null(BioItems.CompoundOf("prep_capsule"));           // a bare key is no preparation
        Assert.Null(BioItems.CompoundOf("prep_capsule#xzz"));       // nor is a broken payload
    }

    // ---------------- Item keys ----------------

    [Fact]
    public void LabTags_LiveBesideTheOldOnes_AndNeverDisturbThem()
    {
        string sample = ItemKey.WithSeed(BioItems.Sample, 0x00AB12CDu);
        Assert.Equal("bio_sample#x00ab12cd", sample);
        Assert.Equal(0x00AB12CDu, ItemKey.Seed(sample));
        Assert.Equal(BioItems.Sample, ItemKey.Base(sample));
        Assert.Equal(0, ItemKey.Tint(sample));
        Assert.Equal(0, ItemKey.Glow(sample));
        Assert.Equal(0, ItemKey.Shape(sample));
        Assert.Equal(0, ItemKey.Design(sample));

        // A dyed block's key reads as before and has no lab tag.
        Assert.Equal(0u, ItemKey.Seed("mud#t3f6fb0"));
        Assert.Equal(0x3F6FB0, ItemKey.Tint("mud#t3f6fb0"));
        Assert.True(ItemMods.Of("stone#g00ffffs04").IsEmpty);

        // Setting and clearing a tag keeps every other part.
        Assert.Equal("stone#t112233u120000", ItemKey.SetTag("stone#t112233", ItemMods.Tag, "120000"));
        Assert.Equal("stone#t112233", ItemKey.SetTag("stone#t112233u120000", ItemMods.Tag, string.Empty));
        Assert.Equal("basic_drill", ItemKey.SetTag("basic_drill#u120000", ItemMods.Tag, string.Empty));
        Assert.Equal(string.Empty, ItemKey.GetTag("basic_drill", ItemMods.Tag));
    }

    [Fact]
    public void ItemMods_RoundTripThroughTheKey()
    {
        var mods = new ItemMods(ModStat.Power, 3, ModStat.Energy, 2, ModStat.Cooldown, 1);
        string key = mods.ApplyTo("titanium_drill");
        Assert.Equal(mods, ItemMods.Of(key));
        Assert.Equal(3, ItemMods.Of(key).Gain(ModStat.Power));
        Assert.Equal(1, ItemMods.Of(key).Loss(ModStat.Cooldown));
        Assert.True(ItemMods.Of("titanium_drill").IsEmpty);
        Assert.True(ItemMods.Of("titanium_drill#uzz").IsEmpty);
        Assert.Equal("titanium_drill", default(ItemMods).ApplyTo(key));
    }

    // ---------------- Changed tools ----------------

    [Fact]
    public void AChange_NeverTouchesTier_Radius_OrIgnition()
    {
        var strongest = new ItemMods(ModStat.Power, 3, ModStat.Cooldown, 3, ModStat.Energy, 3);
        foreach (var item in Content.Items.Values.Where(i => i.Tool is not null))
        {
            var changed = ToolMods.Effective(item, strongest.ApplyTo(item.Key))!;
            Assert.Equal(item.Tool!.Tier, changed.Tier);
            Assert.Equal(item.Tool.MiningRadius, changed.MiningRadius);
            Assert.Equal(item.Tool.Ignites, changed.Ignites);
            Assert.Equal(item.Tool.Kind, changed.Kind);
            Assert.Same(item.Tool, ToolMods.Effective(item, item.Key)); // a plain key reads the definition itself
        }
    }

    [Fact]
    public void AChangedTool_NeverReachesTheNextTier()
    {
        var strongest = new ItemMods(ModStat.Power, 3, ModStat.Power, 3, ModStat.None, 0);
        var tools = Content.Items.Values.Where(i => i.Tool is { Kind: ToolKind.Drill or ToolKind.Weapon }).ToList();
        foreach (var item in tools)
        {
            var changed = ToolMods.Effective(item, strongest.ApplyTo(item.Key))!;
            bool ranged = item.Tool!.Range > 6f;
            foreach (var better in tools.Where(o => o.Tool!.Kind == item.Tool.Kind && o.Tool.Tier > item.Tool.Tier && (o.Tool.Range > 6f) == ranged))
            {
                if (item.Tool.Kind == ToolKind.Drill)
                {
                    Assert.True(changed.MiningPower < better.Tool!.MiningPower,
                        $"a changed {item.Key} ({changed.MiningPower}) must stay below a plain {better.Key} ({better.Tool.MiningPower})");
                }
                else if (item.Tool.Damage > 0f && better.Tool!.Damage > 0f)
                {
                    Assert.True(changed.Damage < better.Tool.Damage,
                        $"a changed {item.Key} ({changed.Damage}) must stay below a plain {better.Key} ({better.Tool.Damage})");
                }
            }
        }
    }

    [Fact]
    public void TheMaterial_DecidesWhichValueChanges_AndABigChangeHasADrawback()
    {
        // A hard, pure material on a drill: power, at the top level — and the price is a slower tool.
        var hard = Material(5, (MatTrait.Hard, 3));
        var coating = new Compound { Form = BioForm.Coating, Effect = BioEffect.Energy, Level = 12 };
        var mods = ItemModRules.Compute(tool: true, hasCooldown: true, hasRange: true, usesEnergy: true, hard, coating);
        Assert.Equal(ModStat.Power, mods.First);
        Assert.Equal(3, mods.FirstLevel);
        Assert.Equal(ModStat.Energy, mods.Second);
        Assert.NotEqual(ModStat.None, mods.Drawback);
        Assert.True(mods.DrawbackLevel >= 1);

        // A small change costs nothing.
        var small = ItemModRules.Compute(true, true, true, true, Material(1, (MatTrait.Hard, 1)), coating: null);
        Assert.Equal(1, small.FirstLevel);
        Assert.Equal(ModStat.None, small.Drawback);

        // A conductive material means nothing to a tool that uses no energy and has no other use for it.
        Assert.True(ItemModRules.Compute(true, hasCooldown: false, hasRange: false, usesEnergy: false, Material(3, (MatTrait.Conductive, 3)), null).IsEmpty);

        // Gear: insulation from a heat-proof material; heavy gear weighs, light material does not.
        var gear = ItemModRules.Compute(tool: false, false, false, false, Material(5, (MatTrait.HeatProof, 3)), new Compound { Form = BioForm.Coating, Effect = BioEffect.ColdWard, Level = 12 });
        Assert.Equal(ModStat.Insulation, gear.First);
        Assert.Equal(ModStat.Weight, gear.Drawback);
        var lightGear = ItemModRules.Compute(false, false, false, false, Material(5, (MatTrait.HeatProof, 3), (MatTrait.Light, 1)), new Compound { Form = BioForm.Coating, Effect = BioEffect.ColdWard, Level = 12 });
        Assert.Equal(ModStat.None, lightGear.Drawback);
    }

    [Fact]
    public void GearChanges_AddUp_AndWeightIsCapped()
    {
        string a = new ItemMods(ModStat.Insulation, 2, ModStat.None, 0, ModStat.Weight, 3).ApplyTo("armor_chest");
        string b = new ItemMods(ModStat.Armor, 1, ModStat.None, 0, ModStat.Weight, 3).ApplyTo("armor_legs");
        string c = new ItemMods(ModStat.Fall, 3, ModStat.None, 0, ModStat.Weight, 3).ApplyTo("boots");
        var worn = new[] { a, b, c, "helmet", null };

        Assert.Equal(2 * GearMods.InsulationPerLevel, GearMods.Bonus(worn, ModStat.Insulation), 4);
        Assert.Equal(GearMods.ArmorPerLevel, GearMods.Bonus(worn, ModStat.Armor), 4);
        Assert.Equal(ItemMods.MaxWeight, GearMods.Bonus(worn, ModStat.Weight), 4); // 9 levels would be 0.18 — capped
        Assert.Equal(0f, GearMods.Bonus(new[] { "helmet", "boots" }, ModStat.Insulation));
        Assert.True(PlayerEffects.MoveFactor(null, GearMods.Bonus(worn, ModStat.Weight)) < 1f);
    }

    // ---------------- Materials ----------------

    [Fact]
    public void WhatAMaterialIs_NeverChanges_OnlyHowMuch()
    {
        int[] copper = MaterialProfiles.BaseLevels(Content.GetItem("copper_ore")!.LabTraits);
        Assert.Equal(3, copper[(int)MatTrait.Conductive]);

        for (uint seed = 1; seed <= 2000; seed++)
        {
            var deposit = MaterialProfiles.Derive(seed * 40503u, copper, Context(BioTag.None, kind: BioKind.Mineral));
            Assert.InRange(deposit.Purity, 1, 5);
            Assert.InRange(deposit.LevelOf(MatTrait.Conductive), 2, 4); // copper conducts on every world
            Assert.True(deposit.LevelOf(MatTrait.Heavy) >= 1);
            if (deposit.Trace != MatTrait.None)
            {
                Assert.Equal(0, copper[(int)deposit.Trace]);            // a trace is a trait the material does not have
                Assert.Equal(1, deposit.LevelOf(deposit.Trace));
            }
        }

        var synthetic = MaterialProfiles.Synthetic(copper);
        Assert.Equal(3, synthetic.Purity);
        Assert.Equal(MatTrait.None, synthetic.Trace);
        Assert.Equal(3, synthetic.LevelOf(MatTrait.Conductive));
    }

    [Fact]
    public void AHotWorld_MakesAHeatProofTraceMoreLikely()
    {
        int[] copper = MaterialProfiles.BaseLevels(Content.GetItem("copper_ore")!.LabTraits);
        int hot = 0, plain = 0;
        for (uint seed = 1; seed <= 6000; seed++)
        {
            hot += MaterialProfiles.Derive(seed * 40503u, copper, Context(BioTag.Hot, kind: BioKind.Mineral)).Trace == MatTrait.HeatProof ? 1 : 0;
            plain += MaterialProfiles.Derive(seed * 40503u, copper, Context(BioTag.None, kind: BioKind.Mineral)).Trace == MatTrait.HeatProof ? 1 : 0;
        }

        Assert.True(hot > plain * 3, $"hot {hot} vs plain {plain}");
    }

    [Fact]
    public void LabData_NamesOnlyKnownTraitsAndForms()
    {
        foreach (var item in Content.Items.Values)
        {
            foreach (var kv in item.LabTraits ?? new Dictionary<string, int>())
            {
                Assert.True(Enum.TryParse<MatTrait>(kv.Key, ignoreCase: true, out var trait) && trait != MatTrait.None,
                    $"{item.Key}: unknown lab trait '{kv.Key}'");
                Assert.InRange(kv.Value, 1, 3);
            }

            if (!string.IsNullOrEmpty(item.LabCarrier))
            {
                Assert.True(BioItems.CarrierForm(item) is not null, $"{item.Key}: unknown lab carrier '{item.LabCarrier}'");
            }
        }

        // Every form has a carrier, and every preparation form is an item.
        foreach (BioForm form in Enum.GetValues(typeof(BioForm)))
        {
            Assert.Contains(Content.Items.Values, i => BioItems.CarrierForm(i) == form);
            Assert.NotNull(Content.GetItem(BioItems.PreparationKey(form)));
        }

        // The lab's own texts exist in both mandatory languages for every effect, side effect and trait.
        foreach (string locale in new[] { "en", "de" })
        {
            var texts = TestLocales.Load(locale);
            foreach (var effect in BioRules.Effects)
            {
                Assert.True(texts.ContainsKey("bio.effect." + effect.ToString().ToLowerInvariant()), $"{locale}: bio.effect.{effect}");
                Assert.True(texts.ContainsKey("bio.effect." + effect.ToString().ToLowerInvariant() + ".desc"), $"{locale}: bio.effect.{effect}.desc");
            }

            foreach (BioSideEffect side in Enum.GetValues(typeof(BioSideEffect)))
            {
                if (side != BioSideEffect.None)
                {
                    Assert.True(texts.ContainsKey("bio.side." + side.ToString().ToLowerInvariant()), $"{locale}: bio.side.{side}");
                }
            }

            foreach (MatTrait trait in Enum.GetValues(typeof(MatTrait)))
            {
                if (trait != MatTrait.None)
                {
                    Assert.True(texts.ContainsKey("bio.trait." + trait.ToString().ToLowerInvariant()), $"{locale}: bio.trait.{trait}");
                }
            }

            foreach (ModStat stat in Enum.GetValues(typeof(ModStat)))
            {
                if (stat != ModStat.None)
                {
                    Assert.True(texts.ContainsKey("bio.stat." + stat.ToString().ToLowerInvariant()), $"{locale}: bio.stat.{stat}");
                }
            }

            for (int group = 0; group < BioRules.Groups; group++)
            {
                Assert.True(texts.ContainsKey("bio.group." + group), $"{locale}: bio.group.{group}");
            }
        }
    }

    // ---------------- Effects ----------------

    [Fact]
    public void Effects_NeverStack_AndThreeAreTheLimit()
    {
        var effects = new List<ActiveEffect>();
        ActiveEffect E(BioEffect effect, int level) => new() { Effect = effect, Level = level, SecondsLeft = 60 };

        Assert.Equal(EffectAddResult.Applied, PlayerEffects.Add(effects, E(BioEffect.Speed, 5)));
        Assert.Equal(EffectAddResult.Weaker, PlayerEffects.Add(effects, E(BioEffect.Speed, 3)));
        Assert.Equal(EffectAddResult.Refreshed, PlayerEffects.Add(effects, E(BioEffect.Speed, 8)));
        Assert.Single(effects);
        Assert.Equal(8, effects[0].Level);

        Assert.Equal(EffectAddResult.Applied, PlayerEffects.Add(effects, E(BioEffect.Jump, 4)));
        Assert.Equal(EffectAddResult.Applied, PlayerEffects.Add(effects, E(BioEffect.Grip, 4)));
        Assert.Equal(EffectAddResult.Full, PlayerEffects.Add(effects, E(BioEffect.Mining, 9)));
        Assert.Equal(3, effects.Count);
        Assert.Equal(EffectAddResult.Refreshed, PlayerEffects.Add(effects, E(BioEffect.Jump, 4))); // the same again is no fourth
    }

    [Fact]
    public void AHeatSensitiveEffect_RunsOutTwiceAsFastInTheHeat()
    {
        var cool = new List<ActiveEffect> { new() { Effect = BioEffect.Speed, Level = 5, SecondsLeft = 60, Thermal = BioThermal.HeatSensitive } };
        var hot = new List<ActiveEffect> { new() { Effect = BioEffect.Speed, Level = 5, SecondsLeft = 60, Thermal = BioThermal.HeatSensitive } };
        PlayerEffects.Tick(cool, 10f, temperatureC: 20f);
        PlayerEffects.Tick(hot, 10f, temperatureC: 80f);
        Assert.Equal(50f, cool[0].SecondsLeft, 3);
        Assert.Equal(40f, hot[0].SecondsLeft, 3);

        Assert.True(PlayerEffects.Tick(hot, 25f, temperatureC: 80f)); // ended
        Assert.Empty(hot);
    }

    [Fact]
    public void TheFormulas_ReadEffectAndSideEffect()
    {
        var effects = new List<ActiveEffect>
        {
            new() { Effect = BioEffect.Speed, Level = 15, SecondsLeft = 60, Side = BioSideEffect.Hungry, SideLevel = 2 },
            new() { Effect = BioEffect.Breath, Level = 15, SecondsLeft = 60, Side = BioSideEffect.Leaden, SideLevel = 3 },
        };
        Assert.Equal(1.25f, PlayerEffects.MoveFactor(effects), 3);
        Assert.Equal(0.6f, PlayerEffects.OxygenDrainFactor(effects), 3);
        Assert.Equal(1.3f, PlayerEffects.HungerDrainFactor(effects), 3);
        Assert.Equal(0.7f, PlayerEffects.JumpFactor(effects), 3);
        Assert.Equal(1f, PlayerEffects.MeleeFactor(effects), 3);
        Assert.Equal(1f, PlayerEffects.MoveFactor(null), 3);
    }

    // ---------------- Crossing ----------------

    [Fact]
    public void ACross_TakesItsEffectFromOneParent_AndItsStrengthFromTheStronger()
    {
        var a = BioProfiles.Derive(101, Context(BioTag.Fast, points: 5));
        var b = BioProfiles.Derive(202, Context(BioTag.Water, points: 5));
        uint seed = BioHash.CrossSeed(101, 202);

        var sameWorld = BioProfiles.Cross(a, b, seed, differentWorlds: false);
        var twoWorlds = BioProfiles.Cross(a, b, seed, differentWorlds: true);
        var mirrored = BioProfiles.Cross(b, a, seed, differentWorlds: true);

        Assert.Contains(sameWorld.Effect, new[] { a.Effect, b.Effect });
        Assert.True(twoWorlds.Level >= sameWorld.Level);
        Assert.InRange(twoWorlds.Level, 1, BioRules.MaxLevel);
        Assert.True(twoWorlds.Rarity <= Math.Max(a.Rarity, b.Rarity) + 1);
        Assert.Equal(Math.Min(a.Toxicity, b.Toxicity), twoWorlds.Toxicity);
        Assert.Equal(twoWorlds.Level, mirrored.Level); // the strength does not depend on the order
    }

    [Fact]
    public void ALegendaryCross_NeedsTwoRareParentsOfDifferentWorlds()
    {
        var epic = new BioProfile { Effect = BioEffect.Speed, Level = 12, Rarity = 3, Group = 0, DurationSeconds = 120 };
        var common = new BioProfile { Effect = BioEffect.Jump, Level = 2, Rarity = 0, Group = 1, DurationSeconds = 60 };
        var rare = new BioProfile { Effect = BioEffect.Jump, Level = 9, Rarity = 2, Group = 1, DurationSeconds = 60 };

        Assert.True(BioProfiles.Cross(epic, common, 1, differentWorlds: true).Level <= 12);  // stays in the epic band
        Assert.True(BioProfiles.Cross(epic, rare, 1, differentWorlds: false).Level <= 12);
        Assert.True(BioProfiles.Cross(epic, rare, 1, differentWorlds: true).Level >= 13);    // the one way up
    }

    [Fact]
    public void APlantCross_IsAlwaysTheSame_AndNeverThePlainLayout()
    {
        var a = new FloraGenome { BodyBlock = "flora_bush", CrownBlock = "flora_bush", TintRgb = 0x40A040, Size = 1, Toxic = true };
        var b = new FloraGenome { BodyBlock = "flora_fern", CrownBlock = "flora_fern", TintRgb = 0xA04040, Size = 2, Toxic = false };
        uint seed = BioHash.CrossSeed(11, 22);
        var child = FloraGenome.Cross(a, b, seed);
        var again = FloraGenome.Cross(a, b, seed);

        Assert.Equal(FloraForm.Pack(child), FloraForm.Pack(again));
        Assert.Equal(child.TintRgb, again.TintRgb);
        Assert.NotEqual(FloraForm.LayoutPlain, child.Layout);
        Assert.NotEqual(child.BodyBlock, child.CrownBlock);     // body from one parent, crown from the other
        Assert.False(child.Toxic);                              // the poison is bred out unless both carry it
        Assert.NotEqual(0, child.TintRgb);

        int packed = FloraForm.Pack(child);
        Assert.True(FloraForm.IsForm(packed));
        Assert.Equal(child.BodyBlock, FloraForm.BodyBlock(packed));
        Assert.Equal(child.CrownBlock, FloraForm.CrownBlock(packed));
        Assert.Equal(child.Layout, FloraForm.Layout(packed));
        Assert.Equal(child.Size, FloraForm.Size(packed));
        Assert.Equal(child.Glow, FloraForm.Glow(packed));
        Assert.Equal(packed, packed & 0xFFFFFF);                // fits the 24 bits of a voxel's glow channel
        Assert.False(FloraForm.IsForm(0x00FFFF));               // an ordinary glow colour is no form

        Assert.True(FloraForm.Breedable("flora_bush"));
        Assert.False(FloraForm.Breedable("flora_cropberry"));   // a crop stays a crop
        Assert.False(FloraForm.Breedable("flora_fruit_round")); // fruit hangs on a tree
        Assert.False(FloraForm.Breedable("flora_sapling"));     // and the sapling becomes one
    }
}
