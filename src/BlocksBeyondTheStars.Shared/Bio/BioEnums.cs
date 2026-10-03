// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>What a sample was taken from.</summary>
public enum BioKind : byte
{
    Plant = 0,
    Animal = 1,
    Mineral = 2,
}

/// <summary>
/// What a substance does to whoever takes it (the bio lab). The numeric values ride in item keys and in
/// saves, so members are <b>append-only</b>: never renumber, never reuse.
/// </summary>
public enum BioEffect : byte
{
    None = 0,
    Speed = 1,        // walks faster
    Jump = 2,         // jumps higher
    FeatherFall = 3,  // a hard landing hurts less
    Grip = 4,         // a wall costs less grip
    Shield = 5,       // a cushion of extra health on top of the 100
    Regeneration = 6, // health comes back over time
    Strength = 7,     // melee hits harder
    Mining = 8,       // a drill bites harder
    Reflex = 9,       // tools and weapons are ready again sooner
    Breath = 10,      // the suit air lasts longer
    HeatWard = 11,    // heat stresses the suit less
    ColdWard = 12,    // cold stresses the suit less
    ToxinWard = 13,   // corrosive air bites less
    Satiety = 14,     // hunger comes slower
    NightSight = 15,  // sees in the dark
    Perception = 16,  // sees living things through terrain
    Stealth = 17,     // hostiles do not notice the player
    Energy = 18,      // the suit recharges
    Gathering = 19,   // a harvested plant yields one more
}

/// <summary>The mild catch a strong substance may carry. Never direct damage. Append-only, like <see cref="BioEffect"/>.</summary>
public enum BioSideEffect : byte
{
    None = 0,
    Hungry = 1,      // hunger comes faster
    Breathless = 2,  // the suit air goes faster
    Sluggish = 3,    // walks slower
    Leaden = 4,      // jumps lower
    Tired = 5,       // no natural healing
    SlowReflex = 6,  // tools and weapons take longer to be ready
}

/// <summary>What a sample is made of — decided by what the species yields, never rolled.</summary>
public enum BioCarrier : byte
{
    Fibre = 0,
    Juice = 1,
    Secretion = 2,
    Tissue = 3,
    Mineral = 4,
}

/// <summary>How a running effect takes the weather: a heat-sensitive one runs out twice as fast in the heat.</summary>
public enum BioThermal : byte
{
    Stable = 0,
    HeatSensitive = 1,
    ColdSensitive = 2,
}

/// <summary>The form a compound is made into — decided by the carrier in the lab.</summary>
public enum BioForm : byte
{
    Injector = 0, // full strength, half the time
    Gel = 1,      // a third of the strength, four times as long
    Bar = 2,      // half the strength, twice as long, and it feeds
    Capsule = 3,  // two thirds of the strength, the plain duration
    Coating = 4,  // not taken: changes a tool or a piece of gear
}

/// <summary>What two substance groups do to each other in the lab.</summary>
public enum BioReaction : byte
{
    Neutral = 0,
    Amplify = 1,   // the main effect gains two levels
    Inhibit = 2,   // the main effect loses two levels
    Couple = 3,    // the modifier's effect joins at half strength
    Transmute = 4, // the main effect becomes another one
}

/// <summary>
/// The context a species or a deposit lives in: where it grows, how it moves, what it yields. The server reads these
/// from the planet, the biome and the species once and stores them with the species entry; the profile is then a pure
/// function of (seed, context) on both sides. Stored in saves — <b>append-only</b>.
/// </summary>
[Flags]
public enum BioTag : ulong
{
    None = 0,
    Hot = 1UL << 0,          // a hot world or a hot zone
    Cold = 1UL << 1,         // a cold world, snow or ice underfoot
    Lava = 1UL << 2,         // lives in or beside lava
    Water = 1UL << 3,        // lives in water or at its edge
    Air = 1UL << 4,          // flies or drifts
    Cave = 1UL << 5,         // lives underground
    Fast = 1UL << 6,         // among the fast of its world
    Small = 1UL << 7,
    Big = 1UL << 8,
    Titan = 1UL << 9,
    Winged = 1UL << 10,
    GasSac = 1UL << 11,
    ManyLegs = 1UL << 12,    // six or more
    Arachnid = 1UL << 13,
    Tentacles = 1UL << 14,
    Hanging = 1UL << 15,     // a plant that roots in the block above
    Rocky = 1UL << 16,
    Lush = 1UL << 17,
    Wetland = 1UL << 18,
    Dry = 1UL << 19,
    Fungal = 1UL << 20,
    Glow = 1UL << 21,
    Nocturnal = 1UL << 22,
    Skittish = 1UL << 23,
    PackHunter = 1UL << 24,
    Fierce = 1UL << 25,      // deals damage
    Horned = 1UL << 26,      // two horns or more
    ManyEyes = 1UL << 27,    // four or more, or eyes on stalks
    ToxicWorld = 1UL << 28,  // corrosive air or contaminated life
    PoisonYield = 1UL << 29,
    MaterialYield = 1UL << 30,
    FoodYield = 1UL << 31,
    CrystalYield = 1UL << 32,
    Floral = 1UL << 33,
    Rainbow = 1UL << 34,
    Fruit = 1UL << 35,
    Tropical = 1UL << 36,
    Alien = 1UL << 37,
    Grazer = 1UL << 38,      // a peaceful herd animal
    Darter = 1UL << 39,      // moves in quick bursts
    Frontier = 1UL << 40,    // a rare planet type or a rare vein
}

/// <summary>What a material is, wherever it was mined — "copper conducts everywhere". Data: <c>labTraits</c> in
/// <c>data/items.json</c>. Stored in item keys — <b>append-only</b>.</summary>
public enum MatTrait : byte
{
    None = 0,
    Conductive = 1,
    Hard = 2,
    HeatProof = 3,
    ColdProof = 4,
    Heavy = 5,
    Light = 6,
    Magnetic = 7,
    Unstable = 8,
}

/// <summary>A value of a tool or a piece of gear the lab can change. Stored in item keys — <b>append-only</b>.</summary>
public enum ModStat : byte
{
    None = 0,
    Power = 1,       // mining power and weapon damage
    Energy = 2,      // suit energy per use (less is better)
    Cooldown = 3,    // seconds between uses (less is better)
    Range = 4,
    Insulation = 5,  // thermal insulation of a worn piece
    Armor = 6,
    Corrosion = 7,
    Fall = 8,
    Grip = 9,
    Oxygen = 10,     // extra suit oxygen
    Weight = 11,     // a drawback only: the wearer walks a little slower
}
