// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// Where and how a species lives, read once by the server from the planet, the biome and the species and stored with
/// the species' register entry. Everything a profile needs besides the seed — so the profile itself is never stored
/// and a balance change reaches every sample ever taken.
/// </summary>
public sealed class BioContext
{
    public BioKind Kind { get; set; }

    /// <summary>The context flags (<see cref="BioTag"/>).</summary>
    public ulong Tags { get; set; }

    /// <summary>Points for real scarcity: a rare planet type, a hard habitat, a difficult animal. A tier needs several
    /// hurdles at once — rarity is found, never rolled.</summary>
    public int RarityPoints { get; set; }

    /// <summary>0..3, from what the game already knows: a toxic plant, a poison gland, a contaminated world.</summary>
    public int Toxicity { get; set; }

    public BioCarrier Carrier { get; set; }

    public BioTag TagSet => (BioTag)Tags;
}

/// <summary>What a species carries: the substance profile the lab works with. Derived, never stored.</summary>
public sealed class BioProfile
{
    public uint Seed { get; set; }
    public BioKind Kind { get; set; }
    public BioEffect Effect { get; set; }

    /// <summary>1..<see cref="BioRules.MaxLevel"/>.</summary>
    public int Level { get; set; }

    /// <summary>0 common .. 4 legendary.</summary>
    public int Rarity { get; set; }

    public BioSideEffect Side { get; set; }

    /// <summary>0 (none) .. 3.</summary>
    public int SideLevel { get; set; }

    /// <summary>0..3. A toxic sample spoils a mix unless a detoxifier stands by.</summary>
    public int Toxicity { get; set; }

    public BioCarrier Carrier { get; set; }
    public BioThermal Thermal { get; set; }

    /// <summary>The substance group 0..<see cref="BioRules.Groups"/>-1 — what it reacts with.</summary>
    public int Group { get; set; }

    /// <summary>The base duration in seconds, before the form and the stability.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>The coined name of the substance.</summary>
    public string Substance { get; set; } = string.Empty;
}

/// <summary>Derives a species' <see cref="BioProfile"/> from its seed and its context — a pure function, the same on the
/// server and on every client.</summary>
public static class BioProfiles
{
    /// <summary>The profile of a wild species.</summary>
    public static BioProfile Derive(uint seed, BioContext context)
    {
        var tags = context.TagSet;

        // What it does: every effect has weight 1, the context adds to the ones that fit.
        int pick = BioHash.Weighted(BioHash.Draw(seed, 1), BioRules.EffectWeights(tags));
        var effect = BioRules.Effects[pick < 0 ? 0 : pick];

        // How rare: the scarcity points decide the tier, the seed moves it by one at most.
        int rarity = BioRules.RarityOfPoints(context.RarityPoints);
        int luck = (int)(BioHash.Draw(seed, 2) % 100);
        rarity = Math.Clamp(rarity + (luck < 15 ? -1 : luck >= 85 ? 1 : 0), 0, BioRules.MaxRarity);

        var (low, high) = BioRules.LevelBand(rarity);
        int level = low + (int)(BioHash.Draw(seed, 3) % (ulong)(high - low + 1));

        // The catch: the stronger, the likelier.
        var side = BioSideEffect.None;
        int sideLevel = 0;
        if ((int)(BioHash.Draw(seed, 4) % 100) < 20 + 10 * rarity)
        {
            var (first, second) = BioRules.Opponents(effect);
            side = (BioHash.Draw(seed, 5) & 1) == 0 ? first : second;
            sideLevel = Math.Clamp(1 + (rarity >= 2 ? 1 : 0) + (BioHash.Draw(seed, 6) % 3 == 0 ? 1 : 0), 1, 3);
        }

        return new BioProfile
        {
            Seed = seed,
            Kind = context.Kind,
            Effect = effect,
            Level = level,
            Rarity = rarity,
            Side = side,
            SideLevel = sideLevel,
            Toxicity = Math.Clamp(context.Toxicity, 0, 3),
            Carrier = context.Carrier,
            Thermal = ThermalOf(seed, tags, effect),
            Group = GroupOf(seed, context.Kind),
            DurationSeconds = BioRules.Durations[Math.Max(0, BioHash.Weighted(BioHash.Draw(seed, 9), BioRules.DurationWeights))],
            Substance = BioNames.Substance(seed),
        };
    }

    /// <summary>
    /// The profile of a cross. Always the same child for the same pair. The effect comes from one parent, the strength
    /// is the stronger parent's minus one, plus one for parents of different worlds and plus one for different substance
    /// groups. A tier above both parents needs both to be at least rare and from different worlds.
    /// </summary>
    public static BioProfile Cross(BioProfile a, BioProfile b, uint childSeed, bool differentWorlds)
    {
        bool fromA = (BioHash.Draw(childSeed, 1) & 1) == 0;
        var giver = fromA ? a : b;
        var other = fromA ? b : a;

        int topRarity = Math.Max(a.Rarity, b.Rarity);
        if (differentWorlds && a.Rarity >= 2 && b.Rarity >= 2)
        {
            topRarity = Math.Min(BioRules.MaxRarity, topRarity + 1);
        }

        int level = Math.Max(a.Level, b.Level) - 1 + (differentWorlds ? 1 : 0) + (a.Group != b.Group ? 1 : 0);
        level = Math.Clamp(level, 1, BioRules.LevelBand(topRarity).High);

        return new BioProfile
        {
            Seed = childSeed,
            Kind = giver.Kind,
            Effect = giver.Effect,
            Level = level,
            Rarity = Math.Min(topRarity, BioRules.RarityOfLevel(level)),
            Side = giver.Side,
            SideLevel = giver.SideLevel,
            Toxicity = Math.Min(a.Toxicity, b.Toxicity), // a cross breeds the poison out
            Carrier = giver.Carrier,
            Thermal = (BioHash.Draw(childSeed, 7) & 1) == 0 ? a.Thermal : b.Thermal,
            Group = other.Group,
            DurationSeconds = Math.Max(a.DurationSeconds, b.DurationSeconds),
            Substance = BioNames.Substance(childSeed),
        };
    }

    // A substance from a hot place never falls apart in the heat, one from the cold never in the cold — and a heat
    // ward that decays in the heat would be no ward at all.
    private static BioThermal ThermalOf(uint seed, BioTag tags, BioEffect effect)
    {
        int roll = (int)(BioHash.Draw(seed, 7) % 100);
        BioThermal thermal;
        if ((tags & (BioTag.Hot | BioTag.Lava)) != 0)
        {
            thermal = roll < 40 ? BioThermal.ColdSensitive : BioThermal.Stable;
        }
        else if ((tags & BioTag.Cold) != 0)
        {
            thermal = roll < 40 ? BioThermal.HeatSensitive : BioThermal.Stable;
        }
        else
        {
            thermal = roll < 20 ? BioThermal.HeatSensitive : roll < 40 ? BioThermal.ColdSensitive : BioThermal.Stable;
        }

        if ((effect == BioEffect.HeatWard && thermal == BioThermal.HeatSensitive)
            || (effect == BioEffect.ColdWard && thermal == BioThermal.ColdSensitive))
        {
            thermal = BioThermal.Stable;
        }

        return thermal;
    }

    // Plants draw from groups 0..4, animals from 3..7: the two kingdoms share the middle groups, so a plant and an
    // animal sample react more often than two of a kind.
    private static int GroupOf(uint seed, BioKind kind)
        => (int)(BioHash.Draw(seed, 8) % 5) + (kind == BioKind.Animal ? 3 : 0);
}
