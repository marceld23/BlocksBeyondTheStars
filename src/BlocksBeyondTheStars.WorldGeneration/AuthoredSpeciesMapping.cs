// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The ONE mapping from an authored record (<see cref="AuthoredCreature"/>, <c>data/creatures.json</c>) to the species it
/// becomes on a world (#3971). Every record property is either read here or named in <see cref="RecordOnly"/> — the
/// completeness test fails when a new record property is neither, so a designed trait can never silently miss the roster.
/// </summary>
internal static class AuthoredSpeciesMapping
{
    /// <summary>The record properties this mapping deliberately does NOT read, with the place that does: the roster decides
    /// with them WHETHER (and where) the record joins, the species never carries them.</summary>
    internal static readonly IReadOnlyCollection<string> RecordOnly = new[]
    {
        nameof(AuthoredCreature.MinGeneration), // CreatureGenerator.GenerateRoster / GenerateAuthoredGiant: when it joins
        nameof(AuthoredCreature.Homes),         // GameContent.AuthoredCreaturesFor: which types' rosters it joins (#3973)
    };

    /// <summary>Builds the roster entry of an authored record (#1763): the designed traits verbatim, the name's
    /// second word coined per world, the formulas of the rolled species where the record leaves a stat null, and
    /// the derived fields (voice, fins) exactly as for a rolled species.</summary>
    internal static CreatureSpecies ToSpecies(AuthoredCreature a, PlanetType planet, System.Random rng, long speciesSeed)
    {
        string coined = NameGenerator.Creature(rng);
        int space = coined.IndexOf(' ');
        string second = space > 0 ? coined.Substring(0, space) : coined;
        string name = !string.IsNullOrEmpty(a.NameKey) ? a.EnglishName // #3973: a localized species name; Name is its English fallback
            : string.IsNullOrWhiteSpace(a.NamePrefix) ? coined
            : a.FixedName ? a.NamePrefix // #2084: the kids' name, exactly as they wrote it
            : a.NamePrefix + " " + second;

        var species = Body(a, name);
        ApplyLife(species, a, AffinityOf(a, planet));
        ApplyClassTraits(species, a);
        if (species.GiantHeight > 0f)
        {
            species.Size = species.GiantHeight / 10f; // #2075: a giant's scan and voice scale, exactly as for a rolled one
        }

        species.VoiceSeed = unchecked((int)(speciesSeed ^ (speciesSeed >> 32)) ^ 0x5EED_1CE);
        species.HasFins = CreatureMotion.FinsFor(species);
        if (species.Habitat == CreatureHabitat.Air && species.HoverAltitude <= 0f)
        {
            species.HoverAltitude = 3f + (float)rng.NextDouble() * 9f;
        }

        return species;
    }

    /// <summary>The biome affinity by surface block or climate zone (#3973): the first of the type's biomes the record names, so
    /// a rolled roster's "native to biome n" pass still prefers Leni on the snow. The hard rule is BiomeExclusive, which the
    /// spawner checks against the ground under the animal's feet (<see cref="CreatureBiomes.OnOwnGround"/>).</summary>
    private static int AffinityOf(AuthoredCreature a, PlanetType planet)
        => CreatureBiomes.AffinityIn(a.BiomeSurfaces, a.FloraThemes, planet);

    /// <summary>Identity, stats, body and look.</summary>
    private static CreatureSpecies Body(AuthoredCreature a, string name)
    {
        bool hostile = a.Temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
        return new CreatureSpecies
        {
            Id = "au_" + a.Key,
            NameKey = string.IsNullOrEmpty(a.NameKey) ? CreatureNames.GenericKey : a.NameKey, // #3973
            Name = name,
            Habitat = a.Habitat,
            Activity = a.Activity,
            Temperament = a.Temperament,
            LocoStyle = a.LocoStyle,
            BodyPlan = a.BodyPlan,
            HeadShape = a.HeadShape, // #2009: a worksheet arachnid may wear a pyramid
            Size = a.Size,
            MaxHealth = a.MaxHealth ?? 10f + a.Size * 8f + (hostile ? 10f : 0f),
            Speed = a.Speed,
            AttackDamage = a.AttackDamage ?? (hostile ? 4.5f : 0f),
            Legs = a.Legs,
            HasWings = a.HasWings,
            HasTail = a.HasTail,
            BodySegments = a.BodySegments,
            ColorRgb = a.ColorRgb,
            BellyRgb = a.BellyRgb,
            Eyes = a.Eyes,
            EyeRgb = a.EyeRgb, // #2069: a worksheet species may name its iris
            Horns = a.Horns,
            HasCrest = a.HasCrest,
            Glows = a.Glows,
            Hide = a.Hide,
            HoverAltitude = a.HoverAltitude,
            Heads = System.Math.Clamp(a.Heads, 1, 3),         // #1780-#1782: an authored species may use the gen-6 bodies
            WingPairs = System.Math.Clamp(a.WingPairs, 1, 3),
            FinPairs = System.Math.Clamp(a.FinPairs, 1, 3),
            Coats = a.Coats?.ConvertAll(v => v.Clone()) ?? new List<CoatVariant>(), // #3972: the fixed coats each animal picks from
            Voices = a.Voices is { IsEmpty: false } ? a.Voices.Clone() : null,    // #3973: its own recorded calls
        };
    }

    /// <summary>Where it lives, what it yields and how it behaves.</summary>
    private static void ApplyLife(CreatureSpecies species, AuthoredCreature a, int affinity)
    {
        species.SocialGroupSize = a.SocialGroupSize;
        species.BiomeAffinity = affinity;
        species.DropItem = a.DropItem;
        species.DropCount = a.DropCount;
        species.DropKind = a.DropKind;
        species.BiomeSurfaces = a.BiomeSurfaces.ToArray();
        species.FloraThemes = a.FloraThemes.ToArray(); // #3973: its climate zones, beside its surfaces
        species.BiomeExclusive = a.BiomeExclusive;
        species.AngeredByMining = a.AngeredByMining;
        species.GiftsWhenCalm = a.GiftsWhenCalm;
        species.BegsForFood = a.BegsForFood;  // #2018: a worksheet species may beg (the rule honours it on passive Land species only)
        species.FavouriteFood = a.FavouriteFood;                  // #2082: empty = the generation-16 roll for a beggar
        species.FeedsToTame = System.Math.Max(0, a.FeedsToTame);
        species.SwallowsCreatures = a.SwallowsCreatures; // #2076: an authored worm says whether it hunts the animals
        species.DuelTame = a.DuelTame;         // #3365: the Soundi is tamed in a duel …
        species.PackDefender = a.PackDefender; // … and its pack defends it outside one
        species.DefendsItself = a.DefendsItself; // #3973: a shy dog bites back
    }

    /// <summary>The traits of the bodies that need them: the biped's arms and head, the hound's head and feet, the giants.</summary>
    private static void ApplyClassTraits(CreatureSpecies species, AuthoredCreature a)
    {
        species.Arms = a.BodyPlan == CreatureBodyPlan.Biped ? System.Math.Clamp(a.Arms, 0, 2) // #2081: arms on the biped body only …
            : a.BodyPlan == CreatureBodyPlan.Sounder ? BlocksBeyondTheStars.Shared.Sounders.SounderRules.Arms : 0; // … and the Sounder's four (#3355)
        species.HeadRatio = a.HeadRatio > 0f ? System.Math.Clamp(a.HeadRatio, 0.5f, BipedRules.MaxHeadRatio) : 1f;
        species.EarStyle = a.EarStyle;        // #3325: a worksheet hound carries the class traits
        species.SnoutLength = System.Math.Clamp(a.SnoutLength, 0f, HoundRules.MaxSnout);
        species.HasClaws = a.HasClaws;
        species.HasTeeth = a.HasTeeth;
        species.BeakStyle = a.BeakStyle; // #3974: the birds of Earth (and the dolphin's snout)
        species.BeakLength = System.Math.Clamp(a.BeakLength, 0f, CreatureBeaks.MaxLength);
        species.BeakRgb = a.BeakRgb & 0xFFFFFF;
        species.HasComb = a.HasComb;
        species.TailFluke = a.TailFluke; // #3975: the dolphin's and the whale's horizontal fluke …
        species.MinWaterDepth = System.Math.Max(0, a.MinWaterDepth); // … and the water they live in
        species.MaxWaterDepth = System.Math.Max(0, a.MaxWaterDepth);
        species.BiteDamage = System.Math.Max(0f, a.BiteDamage); // #3326: bites in chunks when both are set
        species.BiteInterval = System.Math.Max(0f, a.BiteInterval);
        species.GiantHeight = a.GiantHeight;  // #1998: an authored giant (#2003) carries the same traits as a rolled one
        species.BackFeature = a.BackFeature;
        species.LegRatio = a.LegRatio;
        species.NeckLength = a.NeckLength;
        species.HasTrunk = a.HasTrunk; // #3973: the elephant's trunk (the titan builder draws it)
        species.Mandibles = a.Mandibles;
        species.WormLength = a.WormLength;
        species.WormGirth = a.WormGirth;
        species.Hearing = a.Hearing;
    }
}
