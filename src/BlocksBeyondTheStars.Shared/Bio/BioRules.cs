// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// The numbers of the bio lab: pure constants and tables the server, the client and the tests share, in the style of
/// <see cref="BlocksBeyondTheStars.Shared.Definitions.HerdRules"/>. The design rule behind all of them: properties are
/// generated, reactions are computed — there is no recipe list. What a species carries follows from where and how it
/// lives (planet → biome → species), so a player can search with reason: heat protection comes from what survives
/// beside a lava lake.
/// </summary>
public static class BioRules
{
    // --- Limits ---

    /// <summary>Strength levels run 1..<see cref="MaxLevel"/>. Fixed levels, not free numbers: preparations with equal
    /// values stack, the best find in an endless galaxy stays bounded, and a balance change reaches every item.</summary>
    public const int MaxLevel = 15;

    /// <summary>Rarity tiers 0 (common) .. 4 (legendary).</summary>
    public const int MaxRarity = 4;

    /// <summary>How many positive effects a player may run at once.</summary>
    public const int MaxActiveEffects = 3;

    /// <summary>Slots of the sample case (<c>PlayerState.SampleCase</c>) and the most samples of one species it stacks.</summary>
    public const int SampleCaseSlots = 24;
    public const int SampleStack = 20;

    /// <summary>A cross of crosses goes at most this deep (a wild species is generation 0).</summary>
    public const int MaxCrossGeneration = 3;

    /// <summary>The most species a save's register holds, and the most research entries a player keeps.</summary>
    public const int MaxRegisterEntries = 512;
    public const int MaxResearchEntries = 512;

    /// <summary>The most bred plants one world holds.</summary>
    public const int MaxBredPlantsPerWorld = 256;

    /// <summary>A mix below this stability fails; above <see cref="CleanStability"/> the side effect is weakened.</summary>
    public const int FailStability = 30;
    public const int CleanStability = 70;

    /// <summary>The lowest level at which a sample of a species is taken while mining ore: the first block of a deposit
    /// always yields one, after that one cell in this many (decided by a hash of the cell, so placing and mining the same
    /// block never yields twice).</summary>
    public const int MineralSampleEvery = 8;

    // --- The strength curve ---

    /// <summary>Per-mille of an effect's cap at each level 0..15: a curve that flattens, so level 15 is about twice
    /// level 5 and never three times. Integer on purpose (see <see cref="BioHash"/>).</summary>
    private static readonly int[] Curve =
    {
        0, 141, 265, 373, 469, 554, 628, 693, 751, 801, 846, 885, 920, 950, 977, 1000,
    };

    /// <summary>The share (0..1) of an effect's cap a level gives.</summary>
    public static float CurveAt(int level) => Curve[Math.Clamp(level, 0, MaxLevel)] / 1000f;

    /// <summary>What an effect gives at the top level. Every cap sits below what researched gear gives for good, and an
    /// effect shares the cap of the gear formula it joins (<see cref="PlayerEffects"/>).</summary>
    public static float Cap(BioEffect effect) => effect switch
    {
        BioEffect.Speed => 0.25f,        // +25 % walking speed
        BioEffect.Jump => 0.40f,         // +40 % jump height
        BioEffect.FeatherFall => 0.40f,  // fall protection, joins the boots under their cap
        BioEffect.Grip => 0.30f,         // climbing grip, joins gloves and claws under their cap
        BioEffect.Shield => 30f,         // cushion points on top of the 100
        BioEffect.Regeneration => 1.5f,  // health per second
        BioEffect.Strength => 0.30f,     // +30 % melee damage
        BioEffect.Mining => 0.30f,       // +30 % mining power
        BioEffect.Reflex => 0.25f,       // -25 % cooldown
        BioEffect.Breath => 0.40f,       // -40 % oxygen drain
        BioEffect.HeatWard => 0.40f,     // thermal insulation against heat
        BioEffect.ColdWard => 0.40f,     // thermal insulation against cold
        BioEffect.ToxinWard => 0.40f,    // corrosion resistance
        BioEffect.Satiety => 0.40f,      // -40 % hunger drain
        BioEffect.NightSight => 1f,      // 0..1 brightness lift
        BioEffect.Perception => 48f,     // blocks
        BioEffect.Stealth => 1f,         // on or off
        BioEffect.Energy => 1f,          // suit energy per second
        BioEffect.Gathering => 2f,       // extra items per harvested plant
        _ => 0f,
    };

    /// <summary>What an effect gives at a level.</summary>
    public static float Magnitude(BioEffect effect, int level)
    {
        if (effect == BioEffect.None || level <= 0)
        {
            return 0f;
        }

        return effect switch
        {
            BioEffect.Stealth => 1f,
            BioEffect.Gathering => level >= 10 ? 2f : 1f,
            BioEffect.Perception => 16f + (Cap(effect) - 16f) * CurveAt(level),
            _ => Cap(effect) * CurveAt(level),
        };
    }

    /// <summary>What a side effect costs at level 1..3.</summary>
    public static float SideMagnitude(BioSideEffect side, int level)
    {
        int l = Math.Clamp(level, 0, 3);
        return side switch
        {
            BioSideEffect.Hungry => 0.15f * l,      // + hunger drain
            BioSideEffect.Breathless => 0.10f * l,  // + oxygen drain
            BioSideEffect.Sluggish => 0.05f * l,    // - walking speed
            BioSideEffect.Leaden => 0.10f * l,      // - jump height
            BioSideEffect.Tired => l switch { 0 => 0f, 1 => 0.5f, 2 => 0.75f, _ => 1f }, // share of natural healing lost
            BioSideEffect.SlowReflex => 0.10f * l,  // + cooldown
            _ => 0f,
        };
    }

    // --- Rarity ---

    /// <summary>Rarity tier of a species from its scarcity points (<see cref="BioContext.RarityPoints"/>).</summary>
    public static int RarityOfPoints(int points)
        => points >= 9 ? 4 : points >= 7 ? 3 : points >= 5 ? 2 : points >= 3 ? 1 : 0;

    /// <summary>The level band of a rarity tier (inclusive).</summary>
    public static (int Low, int High) LevelBand(int rarity) => Math.Clamp(rarity, 0, MaxRarity) switch
    {
        0 => (1, 3),
        1 => (3, 6),
        2 => (6, 9),
        3 => (9, 12),
        _ => (12, 15),
    };

    /// <summary>The rarity tier a level reads as (the tier whose band it tops out in).</summary>
    public static int RarityOfLevel(int level)
        => level >= 13 ? 4 : level >= 10 ? 3 : level >= 7 ? 2 : level >= 4 ? 1 : 0;

    // --- Planet → biome → species: what makes an effect likely ---

    /// <summary>One row of the context table: a species that carries <see cref="Tag"/> draws <see cref="Effect"/> with
    /// <see cref="Bonus"/> extra weight on top of the base weight 1 every effect has.</summary>
    public readonly struct Affinity
    {
        public Affinity(BioEffect effect, BioTag tag, int bonus)
        {
            Effect = effect;
            Tag = tag;
            Bonus = bonus;
        }

        public BioEffect Effect { get; }
        public BioTag Tag { get; }
        public int Bonus { get; }
    }

    /// <summary>The context table. A strong context (a lava dweller) decides the effect in about half of all cases; the
    /// rest stays a surprise.</summary>
    public static readonly Affinity[] Affinities =
    {
        new(BioEffect.HeatWard, BioTag.Lava, 24), new(BioEffect.HeatWard, BioTag.Hot, 8), new(BioEffect.HeatWard, BioTag.Dry, 3),
        new(BioEffect.ColdWard, BioTag.Cold, 16),
        new(BioEffect.Speed, BioTag.Fast, 10), new(BioEffect.Speed, BioTag.Darter, 5), new(BioEffect.Speed, BioTag.Skittish, 3),
        new(BioEffect.Speed, BioTag.Tropical, 2),
        new(BioEffect.Reflex, BioTag.Darter, 8), new(BioEffect.Reflex, BioTag.PackHunter, 6), new(BioEffect.Reflex, BioTag.Small, 3),
        new(BioEffect.Reflex, BioTag.Fast, 3),
        new(BioEffect.Jump, BioTag.Winged, 6), new(BioEffect.Jump, BioTag.Air, 6), new(BioEffect.Jump, BioTag.GasSac, 8),
        new(BioEffect.FeatherFall, BioTag.Winged, 5), new(BioEffect.FeatherFall, BioTag.Air, 5), new(BioEffect.FeatherFall, BioTag.GasSac, 6),
        new(BioEffect.Grip, BioTag.ManyLegs, 8), new(BioEffect.Grip, BioTag.Arachnid, 12), new(BioEffect.Grip, BioTag.Tentacles, 6),
        new(BioEffect.Grip, BioTag.Hanging, 10), new(BioEffect.Grip, BioTag.Rocky, 4),
        new(BioEffect.Shield, BioTag.Big, 8), new(BioEffect.Shield, BioTag.Titan, 12), new(BioEffect.Shield, BioTag.Lush, 3),
        new(BioEffect.Shield, BioTag.Fruit, 3),
        new(BioEffect.Regeneration, BioTag.Lush, 8), new(BioEffect.Regeneration, BioTag.Wetland, 4), new(BioEffect.Regeneration, BioTag.Grazer, 4),
        new(BioEffect.Strength, BioTag.Fierce, 8), new(BioEffect.Strength, BioTag.Horned, 6), new(BioEffect.Strength, BioTag.PackHunter, 6),
        new(BioEffect.Mining, BioTag.Cave, 4), new(BioEffect.Mining, BioTag.Rocky, 8), new(BioEffect.Mining, BioTag.MaterialYield, 8),
        new(BioEffect.Breath, BioTag.Water, 10), new(BioEffect.Breath, BioTag.Wetland, 3),
        new(BioEffect.NightSight, BioTag.Glow, 10), new(BioEffect.NightSight, BioTag.Cave, 8), new(BioEffect.NightSight, BioTag.Nocturnal, 6),
        new(BioEffect.Perception, BioTag.ManyEyes, 10), new(BioEffect.Perception, BioTag.Nocturnal, 4), new(BioEffect.Perception, BioTag.Cave, 3),
        new(BioEffect.ToxinWard, BioTag.ToxicWorld, 12), new(BioEffect.ToxinWard, BioTag.PoisonYield, 8), new(BioEffect.ToxinWard, BioTag.Fungal, 6),
        new(BioEffect.Satiety, BioTag.Dry, 5), new(BioEffect.Satiety, BioTag.Fruit, 6), new(BioEffect.Satiety, BioTag.FoodYield, 4),
        new(BioEffect.Stealth, BioTag.Skittish, 5), new(BioEffect.Stealth, BioTag.Nocturnal, 3),
        new(BioEffect.Energy, BioTag.CrystalYield, 10), new(BioEffect.Energy, BioTag.Glow, 3),
        new(BioEffect.Gathering, BioTag.Floral, 8), new(BioEffect.Gathering, BioTag.Rainbow, 12),
    };

    /// <summary>Every effect a species can carry, in id order (<see cref="BioEffect.None"/> left out).</summary>
    public static readonly BioEffect[] Effects =
    {
        BioEffect.Speed, BioEffect.Jump, BioEffect.FeatherFall, BioEffect.Grip, BioEffect.Shield, BioEffect.Regeneration,
        BioEffect.Strength, BioEffect.Mining, BioEffect.Reflex, BioEffect.Breath, BioEffect.HeatWard, BioEffect.ColdWard,
        BioEffect.ToxinWard, BioEffect.Satiety, BioEffect.NightSight, BioEffect.Perception, BioEffect.Stealth,
        BioEffect.Energy, BioEffect.Gathering,
    };

    /// <summary>The weight of every effect in <see cref="Effects"/> order for a context: 1 plus the bonuses of its tags.</summary>
    public static int[] EffectWeights(BioTag tags)
    {
        var weights = new int[Effects.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = 1;
        }

        foreach (var a in Affinities)
        {
            if ((tags & a.Tag) != 0)
            {
                weights[Array.IndexOf(Effects, a.Effect)] += a.Bonus;
            }
        }

        return weights;
    }

    // --- Side effects ---

    /// <summary>The two catches an effect may come with — never its own opposite twice over, never damage.</summary>
    public static (BioSideEffect First, BioSideEffect Second) Opponents(BioEffect effect) => effect switch
    {
        BioEffect.Speed => (BioSideEffect.Hungry, BioSideEffect.Breathless),
        BioEffect.Jump => (BioSideEffect.Hungry, BioSideEffect.Tired),
        BioEffect.FeatherFall => (BioSideEffect.Sluggish, BioSideEffect.SlowReflex),
        BioEffect.Grip => (BioSideEffect.SlowReflex, BioSideEffect.Hungry),
        BioEffect.Shield => (BioSideEffect.Sluggish, BioSideEffect.Leaden),
        BioEffect.Regeneration => (BioSideEffect.SlowReflex, BioSideEffect.Hungry),
        BioEffect.Strength => (BioSideEffect.Hungry, BioSideEffect.Tired),
        BioEffect.Mining => (BioSideEffect.Hungry, BioSideEffect.Breathless),
        BioEffect.Reflex => (BioSideEffect.Tired, BioSideEffect.Hungry),
        BioEffect.Breath => (BioSideEffect.Sluggish, BioSideEffect.Leaden),
        BioEffect.HeatWard => (BioSideEffect.Sluggish, BioSideEffect.Tired),
        BioEffect.ColdWard => (BioSideEffect.Sluggish, BioSideEffect.Leaden),
        BioEffect.ToxinWard => (BioSideEffect.Tired, BioSideEffect.SlowReflex),
        BioEffect.Satiety => (BioSideEffect.Sluggish, BioSideEffect.Leaden),
        BioEffect.NightSight => (BioSideEffect.SlowReflex, BioSideEffect.Tired),
        BioEffect.Perception => (BioSideEffect.Tired, BioSideEffect.Hungry),
        BioEffect.Stealth => (BioSideEffect.Sluggish, BioSideEffect.Breathless),
        BioEffect.Energy => (BioSideEffect.Hungry, BioSideEffect.Tired),
        BioEffect.Gathering => (BioSideEffect.SlowReflex, BioSideEffect.Leaden),
        _ => (BioSideEffect.None, BioSideEffect.None),
    };

    /// <summary>The material trait that takes a side effect away when the stabiliser carries it.</summary>
    public static MatTrait Counter(BioSideEffect side) => side switch
    {
        BioSideEffect.Hungry => MatTrait.HeatProof,
        BioSideEffect.Breathless => MatTrait.Light,
        BioSideEffect.Sluggish => MatTrait.Conductive,
        BioSideEffect.Leaden => MatTrait.Magnetic,
        BioSideEffect.Tired => MatTrait.Hard,
        BioSideEffect.SlowReflex => MatTrait.ColdProof,
        _ => MatTrait.None,
    };

    // --- Substance groups and reactions ---

    /// <summary>How many substance groups there are (locale keys <c>bio.group.0</c> …).</summary>
    public const int Groups = 8;

    // The reaction table, upper triangle row by row (a group never reacts with itself). The same in every save: a rule
    // learned once holds everywhere, and a wiki can teach it. What differs from world to world is which species carries
    // which substance.
    private static readonly BioReaction[,] Reactions = BuildReactions();

    private static BioReaction[,] BuildReactions()
    {
        var t = new BioReaction[Groups, Groups];
        void Set(int a, int b, BioReaction r)
        {
            t[a, b] = r;
            t[b, a] = r;
        }

        Set(0, 1, BioReaction.Amplify); Set(0, 3, BioReaction.Couple); Set(0, 4, BioReaction.Inhibit);
        Set(0, 6, BioReaction.Transmute); Set(0, 7, BioReaction.Amplify);
        Set(1, 2, BioReaction.Couple); Set(1, 3, BioReaction.Inhibit); Set(1, 5, BioReaction.Amplify);
        Set(1, 7, BioReaction.Transmute);
        Set(2, 3, BioReaction.Amplify); Set(2, 4, BioReaction.Transmute); Set(2, 5, BioReaction.Inhibit);
        Set(2, 6, BioReaction.Couple);
        Set(3, 4, BioReaction.Couple); Set(3, 6, BioReaction.Amplify); Set(3, 7, BioReaction.Inhibit);
        Set(4, 5, BioReaction.Amplify); Set(4, 6, BioReaction.Inhibit); Set(4, 7, BioReaction.Couple);
        Set(5, 6, BioReaction.Transmute); Set(5, 7, BioReaction.Couple);
        Set(6, 7, BioReaction.Amplify);
        return t;
    }

    /// <summary>What two substance groups do to each other.</summary>
    public static BioReaction Reaction(int groupA, int groupB)
        => Reactions[((groupA % Groups) + Groups) % Groups, ((groupB % Groups) + Groups) % Groups];

    /// <summary>What an effect turns into under a <see cref="BioReaction.Transmute"/>. Nothing turns into stealth — the
    /// strongest effect stays a find, never a by-product.</summary>
    public static BioEffect Transmuted(BioEffect effect) => effect switch
    {
        BioEffect.Speed => BioEffect.Reflex,
        BioEffect.Reflex => BioEffect.Speed,
        BioEffect.Jump => BioEffect.FeatherFall,
        BioEffect.FeatherFall => BioEffect.Jump,
        BioEffect.Grip => BioEffect.Mining,
        BioEffect.Mining => BioEffect.Grip,
        BioEffect.Shield => BioEffect.Regeneration,
        BioEffect.Regeneration => BioEffect.Shield,
        BioEffect.Strength => BioEffect.Satiety,
        BioEffect.Satiety => BioEffect.Strength,
        BioEffect.Breath => BioEffect.ToxinWard,
        BioEffect.ToxinWard => BioEffect.Breath,
        BioEffect.HeatWard => BioEffect.ColdWard,
        BioEffect.ColdWard => BioEffect.HeatWard,
        BioEffect.NightSight => BioEffect.Perception,
        BioEffect.Perception => BioEffect.NightSight,
        BioEffect.Energy => BioEffect.Gathering,
        BioEffect.Gathering => BioEffect.Energy,
        BioEffect.Stealth => BioEffect.NightSight,
        _ => effect,
    };

    // --- Forms ---

    /// <summary>What a form does to the level (numerator over denominator, rounded up) and to the duration (per cent).</summary>
    public static (int LevelNum, int LevelDen, int DurationPercent) FormFactors(BioForm form) => form switch
    {
        BioForm.Injector => (1, 1, 50),
        BioForm.Gel => (1, 3, 400),
        BioForm.Bar => (1, 2, 200),
        BioForm.Capsule => (2, 3, 100),
        _ => (1, 1, 100),
    };

    /// <summary>The form a sample of a carrier class takes best to — a match steadies the mix.</summary>
    public static BioForm PreferredForm(BioCarrier carrier) => carrier switch
    {
        BioCarrier.Juice => BioForm.Injector,
        BioCarrier.Fibre => BioForm.Gel,
        BioCarrier.Tissue => BioForm.Bar,
        BioCarrier.Secretion => BioForm.Capsule,
        _ => BioForm.Coating,
    };

    /// <summary>The base durations a substance may have, in seconds, and how likely each is.</summary>
    public static readonly int[] Durations = { 60, 120, 240, 480 };
    public static readonly int[] DurationWeights = { 2, 4, 3, 1 };

    /// <summary>The shortest and the longest a preparation lasts, and the unit a duration is stored in.</summary>
    public const int MinDuration = 30;
    public const int MaxDuration = 600;
    public const int DurationUnit = 15;

    /// <summary>Stealth is the one effect that switches hostiles off altogether: it lasts a quarter as long, a minute at most.</summary>
    public const int StealthMaxDuration = 60;

    /// <summary>How much faster a heat-sensitive effect runs out in the heat (and a cold-sensitive one in the cold).</summary>
    public const float ThermalDecay = 2f;

    /// <summary>Above this air temperature (°C) the heat counts, below <see cref="ColdBelow"/> the cold.</summary>
    public const float HotAbove = 40f;
    public const float ColdBelow = -5f;
}
