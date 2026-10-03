// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>The item and block keys of the bio lab, and what an item key of theirs carries.</summary>
public static class BioItems
{
    /// <summary>A sample of a plant or an animal species; the species seed rides in the key.</summary>
    public const string Sample = "bio_sample";

    /// <summary>A sample of a deposit: one material on one world.</summary>
    public const string MineralSample = "mineral_sample";

    /// <summary>A seedling of a plant species: planted like a seed.</summary>
    public const string Seedling = "seedling";

    /// <summary>The gadget that takes a sample from a living animal.</summary>
    public const string Sampler = "bio_sampler";

    /// <summary>The lab block.</summary>
    public const string Lab = "bio_lab";

    /// <summary>The blueprints that open the lab's functions one by one — the first is enough for a child's first extract.</summary>
    public const string LabBlueprint = "bio_lab";
    public const string SynthesisBlueprint = "bio_synthesis";
    public const string TuningBlueprint = "bio_tuning";
    public const string CrossingBlueprint = "bio_crossing";

    private static readonly string[] Preparations = { "prep_injector", "prep_gel", "prep_bar", "prep_capsule", "prep_coating" };

    /// <summary>The item key of a form.</summary>
    public static string PreparationKey(BioForm form) => Preparations[Math.Clamp((int)form, 0, Preparations.Length - 1)];

    /// <summary>The form a base item key is, or null when it is no preparation.</summary>
    public static BioForm? FormOf(string? baseKey)
    {
        int at = baseKey is null ? -1 : Array.IndexOf(Preparations, baseKey);
        return at < 0 ? null : (BioForm)at;
    }

    /// <summary>Whether a base item key carries a species seed after the <c>x</c> tag.</summary>
    public static bool CarriesSpecies(string? baseKey) => baseKey is Sample or MineralSample or Seedling;

    /// <summary>The full key of a preparation.</summary>
    public static string PreparationItem(Compound compound)
        => ItemKey.SetTag(PreparationKey(compound.Form), ItemKey.SeedTag, compound.ToPayload());

    /// <summary>The compound a preparation's key carries, or null for anything else.</summary>
    public static Compound? CompoundOf(string? itemKey)
        => FormOf(ItemKey.Base(itemKey ?? string.Empty)) is { } form
            ? Compound.FromPayload(form, ItemKey.GetTag(itemKey, ItemKey.SeedTag))
            : null;

    /// <summary>
    /// Whether an item key is a lab item that is nothing without what it carries (#2216): a sample, a mineral sample or a
    /// seedling without a species seed, a preparation without a compound <see cref="Compound.FromPayload"/> accepts. Such
    /// a blank does nothing anywhere — it is no sample for the case, cannot be planted, has no effect — so it is never
    /// handed out (the Sandbox catalog leaves it out) and never taken. False for every other item.
    /// </summary>
    public static bool NeedsPayload(string? itemKey)
    {
        if (string.IsNullOrEmpty(itemKey))
        {
            return false;
        }

        string baseKey = ItemKey.Base(itemKey!);
        if (CarriesSpecies(baseKey))
        {
            return ItemKey.Seed(itemKey) == 0;
        }

        return FormOf(baseKey) is not null && CompoundOf(itemKey) is null;
    }

    /// <summary>The form an item makes as a carrier in the mixer (its <c>labCarrier</c>), or null when it is none.</summary>
    public static BioForm? CarrierForm(ItemDefinition? item)
        => item?.LabCarrier is { Length: > 0 } carrier && Enum.TryParse<BioForm>(carrier, ignoreCase: true, out var form) ? form : null;

    /// <summary>Strength as a player reads it: levels 1..15 shown as I..V.</summary>
    public static string Roman(int level) => BioRules.RarityOfLevel(level) switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", _ => "V" };

    /// <summary>"Speed III" in the player's language.</summary>
    public static string EffectLabel(Localizer localizer, BioEffect effect, int level)
        => localizer.Get("bio.effect." + effect.ToString().ToLowerInvariant()) + " " + Roman(level);
}
