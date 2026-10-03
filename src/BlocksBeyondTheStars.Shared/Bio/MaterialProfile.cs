// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// What a material is on one world. Two layers: the <b>fixed</b> traits of the material — copper conducts everywhere,
/// data in <c>labTraits</c> of <c>data/items.json</c> — and the <b>origin values</b> of the deposit it was mined from:
/// a purity, one of the fixed traits a level stronger or weaker, and sometimes one trace trait. The origin never
/// changes what a material <i>is</i>, only how much of it.
/// </summary>
public sealed class MaterialProfile
{
    /// <summary>The highest <see cref="MatTrait"/> id plus one — the length of <see cref="Levels"/>.</summary>
    public const int TraitCount = 9;

    public uint Seed { get; set; }

    /// <summary>1..5; 3 is what synthesised ore has. Scales how strongly the material acts in the lab.</summary>
    public int Purity { get; set; } = 3;

    /// <summary>The level of every trait on this world, indexed by <see cref="MatTrait"/> (0 = the material lacks it).</summary>
    public int[] Levels { get; set; } = new int[TraitCount];

    /// <summary>The fixed trait this deposit shifted, and by how much (+1 / -1); <see cref="MatTrait.None"/> = none.</summary>
    public MatTrait Shifted { get; set; }
    public int Shift { get; set; }

    /// <summary>The one extra trait of this deposit, at level 1; <see cref="MatTrait.None"/> = none.</summary>
    public MatTrait Trace { get; set; }

    public int LevelOf(MatTrait trait) => (int)trait > 0 && (int)trait < Levels.Length ? Levels[(int)trait] : 0;

    /// <summary>The trait the material shows most strongly (the lowest id wins a tie), or none for a traitless material.</summary>
    public MatTrait Strongest(Func<MatTrait, bool>? only = null)
    {
        var best = MatTrait.None;
        int bestLevel = 0;
        for (int i = 1; i < Levels.Length; i++)
        {
            if (Levels[i] > bestLevel && (only is null || only((MatTrait)i)))
            {
                best = (MatTrait)i;
                bestLevel = Levels[i];
            }
        }

        return best;
    }
}

/// <summary>Derives a deposit's <see cref="MaterialProfile"/> — a pure function of the deposit seed, the material's fixed
/// traits and the world's context.</summary>
public static class MaterialProfiles
{
    private static readonly int[] PurityWeights = { 10, 25, 35, 22, 8 }; // purity 1..5

    /// <summary>The fixed trait levels of a material from its data (<c>labTraits</c>: trait name → 1..3). Unknown names
    /// are ignored, so a content pack with a newer trait still loads.</summary>
    public static int[] BaseLevels(IReadOnlyDictionary<string, int>? labTraits)
    {
        var levels = new int[MaterialProfile.TraitCount];
        if (labTraits is null)
        {
            return levels;
        }

        foreach (var kv in labTraits)
        {
            if (Enum.TryParse<MatTrait>(kv.Key, ignoreCase: true, out var trait) && trait != MatTrait.None
                && (int)trait < levels.Length)
            {
                levels[(int)trait] = Math.Clamp(kv.Value, 1, 3);
            }
        }

        return levels;
    }

    /// <summary>The profile of material without an origin (synthesised in the matter forge, bought, found in a crate):
    /// exactly the fixed traits at purity 3. Convenient, never the best.</summary>
    public static MaterialProfile Synthetic(int[] baseLevels)
        => new() { Seed = 0, Purity = 3, Levels = (int[])baseLevels.Clone() };

    /// <summary>The profile of a deposit.</summary>
    public static MaterialProfile Derive(uint seed, int[] baseLevels, BioContext context)
    {
        var profile = new MaterialProfile { Seed = seed, Levels = (int[])baseLevels.Clone() };
        var tags = context.TagSet;

        // Purity: rare veins and frontier worlds run purer.
        int purity = 1 + Math.Max(0, BioHash.Weighted(BioHash.Draw(seed, 1), PurityWeights));
        if (context.RarityPoints >= 4 || (tags & BioTag.Frontier) != 0)
        {
            purity++;
        }

        profile.Purity = Math.Clamp(purity, 1, 5);

        // One fixed trait a level up or down — never to zero: what a material is does not change.
        var present = new List<int>();
        for (int i = 1; i < baseLevels.Length; i++)
        {
            if (baseLevels[i] > 0)
            {
                present.Add(i);
            }
        }

        if (present.Count > 0 && BioHash.Draw(seed, 2) % 100 < 60)
        {
            int trait = present[(int)(BioHash.Draw(seed, 3) % (ulong)present.Count)];
            int shift = (BioHash.Draw(seed, 4) & 1) == 0 ? 1 : -1;
            int shifted = Math.Clamp(profile.Levels[trait] + shift, 1, 4);
            if (shifted != profile.Levels[trait])
            {
                profile.Shifted = (MatTrait)trait;
                profile.Shift = shifted - profile.Levels[trait];
                profile.Levels[trait] = shifted;
            }
        }

        // A trace trait at every third deposit, biased by the world it formed on.
        if (BioHash.Draw(seed, 5) % 3 == 0)
        {
            var weights = new int[MaterialProfile.TraitCount];
            for (int i = 1; i < weights.Length; i++)
            {
                weights[i] = baseLevels[i] > 0 ? 0 : 1;
            }

            Bias(weights, MatTrait.HeatProof, (tags & (BioTag.Hot | BioTag.Lava)) != 0);
            Bias(weights, MatTrait.ColdProof, (tags & BioTag.Cold) != 0);
            Bias(weights, MatTrait.Unstable, (tags & BioTag.ToxicWorld) != 0);
            int pick = BioHash.Weighted(BioHash.Draw(seed, 6), weights);
            if (pick > 0)
            {
                profile.Trace = (MatTrait)pick;
                profile.Levels[pick] = 1;
            }
        }

        return profile;
    }

    private static void Bias(int[] weights, MatTrait trait, bool applies)
    {
        if (applies && weights[(int)trait] > 0)
        {
            weights[(int)trait] += 6;
        }
    }
}
