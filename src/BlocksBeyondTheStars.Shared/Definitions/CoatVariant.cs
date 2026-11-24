// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One coat a species with fixed coats can wear (#3972) — a cat is black, grey tabby, ginger tabby, white, black and white
/// or tortoiseshell; each animal wears one, picked by its own seed (<see cref="CoatVariants.Pick"/>). Authored in
/// <c>data/creatures.json</c> as the record's <c>coats</c> list; a species without coats keeps the rolled look.
/// </summary>
public sealed class CoatVariant
{
    /// <summary>How often this coat is picked relative to the others (at least 1).</summary>
    public int Weight { get; set; } = 1;

    /// <summary>The body colour (0xRRGGBB).</summary>
    public int BodyRgb { get; set; } = 0xFFFFFF;

    /// <summary>The belly and accent colour (0xRRGGBB).</summary>
    public int BellyRgb { get; set; } = 0xFFFFFF;

    /// <summary>The coat pattern by its client name (<see cref="CreatureLookNames.Patterns"/>: "Tabby", "Pinto", …); empty or
    /// "None" = a plain coat.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>The pattern colour (0xRRGGBB); unused by a plain coat.</summary>
    public int PatternRgb { get; set; }

    /// <summary>The hide tile (<see cref="CreatureLookNames.Hides"/>); empty = the species' own hide.</summary>
    public string Hide { get; set; } = string.Empty;

    /// <summary>An independent copy.</summary>
    public CoatVariant Clone() => (CoatVariant)MemberwiseClone();
}

/// <summary>The coat-variant rules (#3972): the per-animal seed, the weighted pick and the mask variant — pure functions the
/// server runs; the client only draws what arrives on the wire.</summary>
public static class CoatVariants
{
    /// <summary>How many mask layouts one coat variant has: the animals of a variant share at most this many pattern
    /// textures, so a herd's spots differ without every animal baking its own (potato and browser budgets).</summary>
    public const int MaskVariants = 4;

    /// <summary>The wire's name for a coat without a pattern.</summary>
    public const string PlainPattern = "None";

    /// <summary>A stable, never-zero per-animal seed from a key that names the animal (its entity id, its pen and place, its
    /// owner, its companion id). 0 stays free to mean "no seed" on the wire and in a save.</summary>
    public static int AnimalSeedOf(string key)
    {
        int seed = unchecked((int)StableHashing.Mixed("animal-seed:" + key));
        return seed == 0 ? 1 : seed;
    }

    /// <summary>The index of the coat <paramref name="animalSeed"/> wears among <paramref name="variants"/> (by weight; a
    /// weight below 1 counts as 1), or -1 when there are none.</summary>
    public static int PickIndex(IReadOnlyList<CoatVariant>? variants, int animalSeed)
    {
        if (variants is null || variants.Count == 0)
        {
            return -1;
        }

        long total = 0;
        foreach (var v in variants)
        {
            total += Math.Max(1, v.Weight);
        }

        long roll = (long)(StableHashing.Mix64(unchecked((ulong)(uint)animalSeed) ^ 0xC0A7_5EEDUL) % (ulong)total);
        for (int i = 0; i < variants.Count; i++)
        {
            roll -= Math.Max(1, variants[i].Weight);
            if (roll < 0)
            {
                return i;
            }
        }

        return variants.Count - 1;
    }

    /// <summary>The coat <paramref name="animalSeed"/> wears, or null when the species has none.</summary>
    public static CoatVariant? Pick(IReadOnlyList<CoatVariant>? variants, int animalSeed)
    {
        int i = PickIndex(variants, animalSeed);
        return i < 0 ? null : variants![i];
    }

    /// <summary>The mask layout 0..<see cref="MaskVariants"/> − 1 of an animal — from the same seed, mixed apart from the pick.</summary>
    public static int MaskVariant(int animalSeed)
        => (int)(StableHashing.Mix64(unchecked((ulong)(uint)animalSeed) ^ 0x3A5C_0DE5UL) % MaskVariants);

    /// <summary>The problems of a coat list (unknown pattern or hide names, a weight below 1), each prefixed with
    /// <paramref name="context"/>.</summary>
    public static IEnumerable<string> Problems(string context, IReadOnlyList<CoatVariant>? variants)
    {
        if (variants is null)
        {
            yield break;
        }

        for (int i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            if (v.Weight < 1)
            {
                yield return $"{context} coat {i} has weight {v.Weight} (at least 1).";
            }

            if (!string.IsNullOrEmpty(v.Pattern) && v.Pattern != PlainPattern && !CreatureLookNames.IsPattern(v.Pattern))
            {
                yield return $"{context} coat {i} names unknown pattern '{v.Pattern}'.";
            }

            if (!string.IsNullOrEmpty(v.Hide) && !CreatureLookNames.IsHide(v.Hide))
            {
                yield return $"{context} coat {i} names unknown hide '{v.Hide}'.";
            }
        }
    }
}

/// <summary>
/// The names of the client's coat patterns and hide tiles (#3972), shared so the server can check a data file against them.
/// The client's tables are built from (hides) or guarded against (the <c>CoatPattern</c> enum) these lists.
/// </summary>
public static class CreatureLookNames
{
    /// <summary>Every coat pattern name but "None", in the client enum's order.</summary>
    public static readonly IReadOnlyList<string> Patterns = new[]
    {
        "CrossStripes", "TigerStripes", "Spots", "Rosettes", "Patches", "Speckles", "Marbling", "ScaleBands",
        "DorsalStripe", "Saddle", "TwoTone", "EyeMask", "TailRings", "CrestBand", "BellyBlaze", "Socks", "Countershade",
        "Giraffe", "Dalmatian", "Pinto", "Cheetah", "Jaguar", "Tabby", "Brindle", "Okapi", "Ladybird", "PoisonFrog",
        "Salamander", "EyeSpots", "Tortoise", "Diamondback", "Chevrons", "Lengthwise",
    };

    /// <summary>Every hide the client knows, in a stable order. The first 25 are the original set (#1760 petal, #2083
    /// skin, #2111/#2112 slick); the 24 after them are the hide-variety wave (#2831).</summary>
    public static readonly IReadOnlyList<string> Hides = new[]
    {
        "scales", "fur", "chitin", "hide", "slime", "feathers", "spots", "stripes", "warty", "plated",
        "finned", "tentacled", "mossy", "crystalline", "metallic", "banded", "shaggy", "spined", "mottled",
        "iridescent", "barkskin", "veined", "petal", "skin", "slick",
        "finefeathers", "down", "wool", "leather", "quilted", "fungal", "barkmoss", "coarsescales", "finescales",
        "lizard", "bristles", "crystalveins", "cracked", "honeycomb", "ridged", "velvet", "snake", "roughbark",
        "pebbled", "knobbly", "coralskin", "wrinkled", "featherscales", "lichen",
    };

    private static readonly HashSet<string> PatternSet = new(Patterns, StringComparer.Ordinal);
    private static readonly HashSet<string> HideSet = new(Hides, StringComparer.Ordinal);

    /// <summary>True for a pattern name of <see cref="Patterns"/>.</summary>
    public static bool IsPattern(string? name) => name != null && PatternSet.Contains(name);

    /// <summary>True for a hide name of <see cref="Hides"/>.</summary>
    public static bool IsHide(string? name) => name != null && HideSet.Contains(name);
}
