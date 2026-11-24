// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The Schuppenhund (#3325, generation 34): whether a world hosts the hound, and its fixed body plan.
/// </summary>
public static partial class CreatureGenerator
{
    /// <summary>Whether a world of <paramref name="planet"/>'s type hosts the Schuppenhund (#3325): the world's own roll — the
    /// roster seed salted, no RNG of any other species touched — below the type's <see cref="PlanetType.ScaleHoundChance"/>.
    /// The generation gate is the caller's.</summary>
    public static bool HostsScaleHound(PlanetType planet, long worldSeed)
    {
        if (planet.ScaleHoundChance <= 0.0)
        {
            return false;
        }

        long s = unchecked(worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ WorldGenerator.StableHash("scale-hound"));
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        return rng.NextDouble() < planet.ScaleHoundChance;
    }

    /// <summary>A world's Schuppenhund (#3325): the class traits (<see cref="ApplyHoundPlan"/>) with this world's looks —
    /// colours, size, scale hide, ears, muzzle, eye colour, the hours it is awake. Its own sub-seed, so the same hound on every
    /// visit and a different one on every world. Whether a world has one is <see cref="HostsScaleHound"/>.</summary>
    public static CreatureSpecies GenerateScaleHound(PlanetType planet, long worldSeed)
    {
        long s = unchecked(worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ WorldGenerator.StableHash("scale-hound:species"));
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        var sp = new CreatureSpecies
        {
            Id = HoundRules.SpeciesId,
            NameKey = "creature.generic.name",
            Name = HoundRules.SpeciesName, // Marcel 2026-11-07: the same name on every world and in every language
            Habitat = CreatureHabitat.Land,
            Activity = (CreatureActivity)Weighted(rng,
                (int)CreatureActivity.Diurnal, 40,
                (int)CreatureActivity.Nocturnal, 30,
                (int)CreatureActivity.Crepuscular, 20,
                (int)CreatureActivity.Cathemeral, 10),
            ColorRgb = PickColor(rng, CreatureHabitat.Land), // "zufällig" — random colours per world
            BellyRgb = PickColor(rng, CreatureHabitat.Land),
            BiomeAffinity = -1,
        };

        ApplyHoundPlan(rng, sp);
        ApplyEyeColour(rng, sp); // a hunter: half of the coloured eyes are red
        sp.VoiceSeed = unchecked((int)(s ^ (s >> 32)) ^ 0x5EED_1CE);
        return sp;
    }

    /// <summary>The hound plan (#3325, Jafar's sketch): a scaly four-legged dog with a chest and hindquarters, two ears, a
    /// toothed muzzle, three claws on every foot and a tail — very dangerous (Aggressive, 10 HP a bite, tough), alone, a
    /// prowler that charges the last metres, meat when it falls. Overrides every trait the body demands and rolls the looks the
    /// class varies; it takes any species, so a rolled roster or a worksheet record can wear it later unchanged.</summary>
    internal static void ApplyHoundPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Hound;
        sp.Size = HoundRules.MinSize + (float)rng.NextDouble() * (HoundRules.MaxSize - HoundRules.MinSize);
        sp.Speed = HoundRules.MinSpeed + (float)rng.NextDouble() * (HoundRules.MaxSpeed - HoundRules.MinSpeed);
        sp.MaxHealth = HoundRules.MinHealth + (float)rng.NextDouble() * (HoundRules.MaxHealth - HoundRules.MinHealth);
        sp.BiteInterval = HoundRules.MinBiteInterval + (float)rng.NextDouble() * (HoundRules.MaxBiteInterval - HoundRules.MinBiteInterval);
        sp.EarStyle = HoundRules.EarStyles[rng.Next(HoundRules.EarStyles.Count)];
        sp.SnoutLength = HoundRules.MinSnout + (float)rng.NextDouble() * (HoundRules.MaxSnout - HoundRules.MinSnout);
        sp.Hide = HoundRules.Hides[rng.Next(HoundRules.Hides.Count)];
        sp.Temperament = CreatureTemperament.Aggressive; // "sehr gefährlich" — and alone, so never a pack
        sp.BiteDamage = HoundRules.BiteDamage;
        sp.AttackDamage = sp.BiteDamage / sp.BiteInterval; // every per-second reader (codex, robot hit-back) sees the same danger
        sp.LocoStyle = LocomotionStyle.Prowler;
        sp.SocialGroupSize = 1;
        ApplyHoundBody(sp);
        sp.DropItem = HoundRules.DropItem;
        sp.DropCount = HoundRules.DropCount;
        sp.DropKind = CreatureDropKind.Food;
        sp.HasFins = CreatureMotion.FinsFor(sp);
    }

    /// <summary>The fixed body of the hound plan: four legs under a chest and hindquarters, a tail, claws, teeth, two eyes, one
    /// box head — and nothing an older roll may have left on the species (wings, fins, tentacles, horns, a second head).</summary>
    private static void ApplyHoundBody(CreatureSpecies sp)
    {
        sp.Legs = 4;
        sp.Arms = 0;
        sp.BodySegments = 2; // chest + hindquarters
        sp.HasTail = true;
        sp.HasClaws = true;
        sp.HasTeeth = true;
        sp.Eyes = 2;
        sp.EyeStalks = false;
        sp.Horns = 0; // the two black strokes on the head of the sketch are its ears
        sp.HasCrest = false;
        sp.HasWings = false;
        sp.WingPairs = 1;
        sp.FinPairs = 1;
        sp.HasGasSac = false;
        sp.Tentacles = 0;
        sp.Heads = 1;
        sp.HeadRatio = 1f;
        sp.HeadShape = CreatureHeadShape.Box;
        sp.NeckLength = 0;
        sp.HasTrunk = false;
        sp.HoverAltitude = 0f;
        sp.Glows = false;
        sp.BegsForFood = false;
        sp.FavouriteFood = string.Empty;
    }
}
