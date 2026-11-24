// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The species half of every creature wire message (#3971): what a <see cref="CreatureSpecies"/> puts on a
/// <see cref="NetCreature"/> (the live animal), a <see cref="NetFaunaSpecies"/> (the distant-fauna roster) and a
/// <see cref="NetCompanion"/> (the companion list). The per-animal half — position, health, motion, owner, the giant's
/// phase — stays where that state lives (<c>GameServer.ToNetCreatureCore</c>, <c>WithGiantWire</c>, …).
/// <para>Every species property either reaches <see cref="NetCreature"/> here or is named in <see cref="ServerOnly"/>; the
/// completeness test fails when a new species property is neither, so a trait can never silently stay on the server.</para>
/// </summary>
internal static class CreatureWireMapping
{
    /// <summary>The species properties that deliberately never reach <see cref="NetCreature"/>: rules the server alone runs
    /// (spawning, combat, taming, harvest) — the client draws and labels an animal without them.</summary>
    internal static readonly IReadOnlyCollection<string> ServerOnly = new[]
    {
        nameof(CreatureSpecies.Id),               // the wire's SpeciesId is the entity's own (which names the same species)
        nameof(CreatureSpecies.LocoStyle),        // the gait runs on the server; the client sees the motion
        nameof(CreatureSpecies.MaxHealth),        // the wire carries the animal's own Hull / HullMax
        nameof(CreatureSpecies.Speed),
        nameof(CreatureSpecies.AttackDamage),
        nameof(CreatureSpecies.HoverAltitude),    // the server places the hoverer; the client draws the height it gets
        nameof(CreatureSpecies.SocialGroupSize),  // the spawner's herd size
        nameof(CreatureSpecies.BiomeAffinity),    // the spawner's biome choice
        nameof(CreatureSpecies.DropItem),         // the harvest
        nameof(CreatureSpecies.DropCount),
        nameof(CreatureSpecies.DropKind),
        nameof(CreatureSpecies.BiomeSurfaces),    // the spawner's hard biome rule
        nameof(CreatureSpecies.BiomeExclusive),
        nameof(CreatureSpecies.AngeredByMining),  // behaviour rules
        nameof(CreatureSpecies.GiftsWhenCalm),
        nameof(CreatureSpecies.BegsForFood),      // the wire carries the begging pose itself (NetCreature.Begging)
        nameof(CreatureSpecies.FavouriteFood),    // the scan reads it from the server's codex
        nameof(CreatureSpecies.FeedsToTame),
        nameof(CreatureSpecies.BiteDamage),
        nameof(CreatureSpecies.BiteInterval),
        nameof(CreatureSpecies.Hearing),          // the burrowing giant's hearing
        nameof(CreatureSpecies.SwallowsCreatures),
        nameof(CreatureSpecies.DuelTame),
        nameof(CreatureSpecies.PackDefender),
        nameof(CreatureSpecies.FloraThemes),      // #3973: the spawner's climate zones, like BiomeSurfaces
        nameof(CreatureSpecies.DefendsItself),    // #3973: the provoke rule; the wire carries the hostility itself
        nameof(CreatureSpecies.MinWaterDepth),    // #3975: the spawner's and the step gate's water band
        nameof(CreatureSpecies.MaxWaterDepth),
    };

    /// <summary>Writes the species traits of a live animal onto <paramref name="n"/>. <paramref name="sp"/> null (a species the
    /// roster no longer holds) writes the classic defaults. <paramref name="worldLook"/> is the world's own fauna look, which a
    /// species without a home look (#2868) wears. <paramref name="animalSeed"/> is the animal's own seed (#3972): a species
    /// with coat variants dresses the animal in the one it picks.</summary>
    internal static void WriteSpecies(NetCreature n, CreatureSpecies? sp, (string Planet, int Seed) worldLook, int animalSeed)
    {
        n.NameKey = sp?.NameKey ?? "creature.generic.name";
        n.Name = sp?.Name ?? string.Empty;
        n.Habitat = (sp?.Habitat ?? CreatureHabitat.Land).ToString();
        n.Activity = (sp?.Activity ?? CreatureActivity.Diurnal).ToString();
        n.Temperament = (sp?.Temperament ?? CreatureTemperament.Passive).ToString();
        n.SpeciesSize = sp?.Size ?? 1f; // #2565: the size the bite reach (CreatureReach) is measured by
        WriteBody(n, sp);
        WriteLook(n, sp, worldLook);
        WriteCoat(n, sp, animalSeed);
        n.VoiceSeed = sp?.VoiceSeed ?? 0; // 0 → client falls back to hashing the trait tuple (#907)
        n.Voices = sp?.Voices?.Encode() ?? string.Empty; // #3973: its own recorded calls by mood
    }

    /// <summary>The giant traits (#1998) — written only for a live giant (<c>WithGiantWire</c>), so every other animal keeps
    /// the wire's defaults.</summary>
    internal static void WriteGiantSpecies(NetCreature n, CreatureSpecies sp)
    {
        n.GiantHeight = sp.GiantHeight;
        n.BackFeature = sp.BackFeature;
        n.LegRatio = sp.LegRatio;
        n.Mandibles = sp.Mandibles;
        n.WormLength = sp.WormLength;
        n.WormGirth = sp.WormGirth;
    }

    private static void WriteBody(NetCreature n, CreatureSpecies? sp)
    {
        n.Legs = sp?.Legs ?? 4;
        n.HasWings = sp?.HasWings ?? false;
        n.HasTail = sp?.HasTail ?? false;
        n.BodySegments = sp?.BodySegments ?? 1;
        n.Horns = sp?.Horns ?? 0;
        n.HasCrest = sp?.HasCrest ?? false;
        n.Tentacles = sp?.Tentacles ?? 0;
        n.EyeStalks = sp?.EyeStalks ?? false;
        n.HasGasSac = sp?.HasGasSac ?? false;
        // Through the helper, not the raw flag: a companion tamed before fins existed carries a snapshot
        // with the flag false, and every input the derivation needs is in that snapshot.
        n.HasFins = CreatureMotion.HasFins(sp);
        n.BodyPlan = (sp?.BodyPlan ?? CreatureBodyPlan.Standard).ToString();
        n.NeckLength = sp?.NeckLength ?? 0;
        n.HasTrunk = sp?.HasTrunk ?? false;
        n.HeadShape = (sp?.HeadShape ?? CreatureHeadShape.Box).ToString(); // #2009
        n.Arms = sp?.Arms ?? 0;                                // #2081: a biped's two arms
        n.HeadRatio = sp is { HeadRatio: > 0f } ? sp.HeadRatio : 1f; // #2081: its big head (a pre-wave snapshot carries 0)
        n.EarStyle = (sp?.EarStyle ?? CreatureEarStyle.None).ToString(); // #3325: the Schuppenhund's ears, muzzle, claws and teeth
        n.SnoutLength = sp?.SnoutLength ?? 0f;
        n.HasClaws = sp?.HasClaws ?? false;
        n.HasTeeth = sp?.HasTeeth ?? false;
        n.BeakStyle = (sp?.BeakStyle ?? CreatureBeakStyle.None).ToString(); // #3974: the birds' beaks, the chicken's comb
        n.BeakLength = sp?.BeakLength ?? 0f;
        n.BeakRgb = sp?.BeakRgb ?? 0;
        n.HasComb = sp?.HasComb ?? false;
        n.TailFluke = sp?.TailFluke ?? false; // #3975: the dolphin's and the whale's horizontal fluke
        n.Heads =System.Math.Max(1, sp?.Heads ?? 1);         // #1780-#1782 (generation 6); a pre-wave snapshot carries 0
        n.WingPairs = System.Math.Max(1, sp?.WingPairs ?? 1);
        n.FinPairs = System.Math.Max(1, sp?.FinPairs ?? 1);
    }

    private static void WriteLook(NetCreature n, CreatureSpecies? sp, (string Planet, int Seed) worldLook)
    {
        n.ColorRgb = sp?.ColorRgb ?? 0xFFFFFF;
        n.BellyRgb = sp?.BellyRgb ?? (sp?.ColorRgb ?? 0xFFFFFF);
        n.Glows = sp?.Glows ?? false;
        n.Eyes = sp?.Eyes ?? 2;
        n.EyeRgb = sp?.EyeRgb ?? 0; // #2069: the iris colour, 0 = the classic eye
        n.Hide = sp?.Hide ?? string.Empty; // #1763: an authored species' fixed hide tile
        n.LookPlanet = sp is { LookPlanet.Length: > 0 } ? sp.LookPlanet : worldLook.Planet; // #2868: a pet's, a clone's home world
        n.LookSeed = sp is { LookSeed: not 0 } ? sp.LookSeed : worldLook.Seed;
    }

    /// <summary>#3972: the coat variant the animal's seed picks, when the species has any — its colours and hide over the
    /// species', its pattern, pattern colour and mask layout on the explicit-coat fields. A species without variants leaves
    /// the explicit coat empty (the client rolls the pattern as always).</summary>
    private static void WriteCoat(NetCreature n, CreatureSpecies? sp, int animalSeed)
    {
        if (CoatVariants.Pick(sp?.Coats, animalSeed) is not { } coat)
        {
            return;
        }

        n.ColorRgb = coat.BodyRgb;
        n.BellyRgb = coat.BellyRgb;
        if (coat.Hide.Length > 0)
        {
            n.Hide = coat.Hide;
        }

        n.CoatPattern = coat.Pattern.Length > 0 ? coat.Pattern : CoatVariants.PlainPattern;
        n.CoatPatternRgb = coat.PatternRgb;
        n.CoatMask = CoatVariants.MaskVariant(animalSeed);
    }

    /// <summary>The distant-fauna roster entry of a species (#3245): its voice and when it is awake.</summary>
    internal static NetFaunaSpecies ToFaunaSpecies(CreatureSpecies sp) => new()
    {
        SpeciesId = sp.Id,
        VoiceSeed = sp.VoiceSeed,
        Habitat = sp.Habitat.ToString(),
        Temperament = sp.Temperament.ToString(),
        BodyPlan = sp.BodyPlan.ToString(),
        Size = sp.Size,
        Legs = sp.Legs,
        Eyes = sp.Eyes,
        Horns = sp.Horns,
        BodySegments = sp.BodySegments,
        Tentacles = sp.Tentacles,
        HasGasSac = sp.HasGasSac,
        Glows = sp.Glows,
        Activity = sp.Activity.ToString(),
        Voices = sp.Voices?.Encode() ?? string.Empty, // #3973: a distant dog barks, not the hound's generated call
    };

    /// <summary>Writes the species traits of a tamed companion's snapshot onto its companion-list entry (the portrait);
    /// <paramref name="sizeScale"/> is the individual's own size.</summary>
    internal static void WriteCompanionSpecies(NetCompanion c, CreatureSpecies sp, float sizeScale)
    {
        c.SpeciesName = sp.Name;
        c.Temperament = sp.Temperament.ToString();
        c.Habitat = sp.Habitat.ToString();
        c.Size = sp.Size * sizeScale;
        c.Legs = sp.Legs;
        c.HasWings = sp.HasWings;
        c.HasTail = sp.HasTail;
        c.BodySegments = sp.BodySegments;
        c.ColorRgb = sp.ColorRgb;
        c.BellyRgb = sp.BellyRgb;
        c.Glows = sp.Glows;
        c.Eyes = sp.Eyes;
        c.EyeRgb = sp.EyeRgb; // #2069
        c.Horns = sp.Horns;
        c.HasCrest = sp.HasCrest;
        c.Tentacles = sp.Tentacles;
        c.EyeStalks = sp.EyeStalks;
        c.HasGasSac = sp.HasGasSac;
        c.HasFins = CreatureMotion.HasFins(sp); // lifts a snapshot saved before the trait existed
        c.BodyPlan = sp.BodyPlan.ToString();
        c.NeckLength = sp.NeckLength;
        c.HasTrunk = sp.HasTrunk;
        c.HeadShape = sp.HeadShape.ToString(); // #2009
        c.Arms = sp.Arms;                      // #2081: a biped companion keeps its arms and its big head
        c.HeadRatio = sp.HeadRatio > 0f ? sp.HeadRatio : 1f;
        c.EarStyle = sp.EarStyle.ToString();   // #3325: a tamed Schuppenhund keeps its ears, muzzle, claws and teeth
        c.SnoutLength = sp.SnoutLength;
        c.HasClaws = sp.HasClaws;
        c.HasTeeth = sp.HasTeeth;
        c.BeakStyle = sp.BeakStyle.ToString(); // #3974: a tamed bird keeps its beak
        c.BeakLength = sp.BeakLength;
        c.BeakRgb = sp.BeakRgb;
        c.HasComb = sp.HasComb;
        c.TailFluke = sp.TailFluke; // #3975
        c.Heads =System.Math.Max(1, sp.Heads);
        c.WingPairs = System.Math.Max(1, sp.WingPairs);
        c.FinPairs = System.Math.Max(1, sp.FinPairs);
    }
}
