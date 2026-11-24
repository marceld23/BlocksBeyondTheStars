// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The giants (#1998–#2001, generation 9): colossus, sky giant, leviathan and sandworm, each rolled from the world
/// seed and its location, plus the authored giants that take a giant slot (#2075).
/// </summary>
public static partial class CreatureGenerator
{
    /// <summary>The species id of a world's colossus.</summary>
    public const string ColossusId = "gi_colossus";

    /// <summary>The species id of a world's sandworms (one species, one or two individuals).</summary>
    public const string SandwormId = "gi_sandworm";

    private static readonly string[] GiantBacks = { "", "plates", "spikes", "crystals", "forest" };
    private static readonly string[] ColossusHides = { "hide", "plated", "scales", "shaggy", "mossy", "barkskin", "mottled", "banded" };
    private static readonly string[] WormHides = { "plated", "banded", "scales", "chitin", "warty", "mottled" };

    /// <summary>A world's colossus (#1999): a 40–60 block quadruped rolled from the world seed and its location — the
    /// same giant on every visit, a different one on every world. The server decides WHETHER a world has one
    /// (<see cref="GiantRules.HostsColossus"/>); this only decides what it is.</summary>
    public static CreatureSpecies GenerateColossus(long worldSeed, string locationId)
    {
        long s = unchecked(worldSeed ^ ((long)WorldGenerator.StableHash("colossus:" + locationId) << 16) ^ 0x0C0105505L);
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        float height = 40f + (float)rng.NextDouble() * 20f;
        var temperament = (CreatureTemperament)Weighted(rng,
            (int)CreatureTemperament.Passive, 40,
            (int)CreatureTemperament.Skittish, 15,
            (int)CreatureTemperament.Territorial, 30,
            (int)CreatureTemperament.Aggressive, 15);
        var sp = new CreatureSpecies
        {
            Id = ColossusId,
            NameKey = "creature.generic.name",
            Name = NameGenerator.Creature(rng),
            Habitat = CreatureHabitat.Land,
            Activity = CreatureActivity.Cathemeral, // a giant never lies down for the night
            Temperament = temperament,
            LocoStyle = LocomotionStyle.Strider,
            BodyPlan = CreatureBodyPlan.Colossus,
            GiantHeight = height,
            Size = height / 10f,
            MaxHealth = 3000f + (height - 40f) * 75f,       // 3000..4500 — very, very tough (decision 8)
            AttackDamage = 22f + (float)rng.NextDouble() * 10f, // per stomp, not per second
            Speed = 3.2f + (float)rng.NextDouble(),          // 3.2..4.2 — always slower than a walking player (6)
            Legs = 4,
            HasTail = rng.NextDouble() < 0.7,
            NeckLength = Weighted(rng, 0, 15, 1, 25, 2, 30, 3, 20, 4, 10),
            Heads = Weighted(rng, 1, 85, 2, 10, 3, 5),
            Horns = rng.Next(5),                              // tusks
            HasCrest = rng.NextDouble() < 0.35,
            BackFeature = GiantBacks[rng.Next(GiantBacks.Length)],
            LegRatio = 0.8f + (float)rng.NextDouble() * 0.5f,
            Eyes = Weighted(rng, 2, 80, 4, 15, 1, 5),
            ColorRgb = PickColor(rng, CreatureHabitat.Land),
            BellyRgb = PickColor(rng, CreatureHabitat.Land),
            Glows = rng.NextDouble() < 0.2,
            Hide = ColossusHides[rng.Next(ColossusHides.Length)],
            DropItem = "creature_meat",
            DropCount = 20,
            DropKind = CreatureDropKind.Food,
        };
        sp.VoiceSeed = unchecked((int)(s ^ (s >> 32)) ^ 0x5EED_1CE);
        return sp;
    }

    /// <summary>A sand-sea world's sandworm (#2001): the fixed sandworm archetype — a long armoured tube, a mouth of
    /// mandible petals with rings of teeth — with its size, colours, plates, hearing and temper rolled per world.</summary>
    /// <summary>The type's AUTHORED giant of this body plan (#2075): the first record in <paramref name="authored"/> with a
    /// giant body plan, built exactly as the roster would build it (same sub-seed, salted with the key, so nothing else moves),
    /// or null — the caller then rolls the procedural one. An authored worm's hunting is its own field, not the generation's.</summary>
    public static CreatureSpecies? GenerateAuthoredGiant(PlanetType planet, long worldSeed, IReadOnlyList<AuthoredCreature>? authored, CreatureBodyPlan kind, int terrainGeneration)
    {
        if (authored is null || planet is null)
        {
            return null;
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key);
        foreach (var record in authored)
        {
            if (record.BodyPlan != kind || !IsGiantPlan(record.BodyPlan) || record.MinGeneration > terrainGeneration)
            {
                continue;
            }

            long s = unchecked(planetSeed ^ WorldGenerator.StableHash("authored:" + record.Key));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            return AuthoredSpeciesMapping.ToSpecies(record, planet, rng, s);
        }

        return null;
    }

    /// <summary>#3359: an authored record built as a species of this world on its own — for a story's own animal (the Qualis of
    /// the Bridge Song, the Sounders pack's Soundi) that must exist even where the type's roster does not hold it. The same
    /// sub-seed rule as <see cref="GenerateAuthoredGiant"/> (salted with the key), so no other species moves.</summary>
    public static CreatureSpecies MakeAuthored(AuthoredCreature record, PlanetType planet, long worldSeed)
    {
        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key);
        long s = unchecked(planetSeed ^ WorldGenerator.StableHash("authored:" + record.Key));
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        return AuthoredSpeciesMapping.ToSpecies(record, planet, rng, s);
    }

    /// <summary>A body plan that lives in the giant slots, never in the roster (#2075, #2111, #2112).</summary>
    private static bool IsGiantPlan(CreatureBodyPlan plan)
        => CreatureBodyPlans.Of(plan).Giant; // #3972: the profile table

    public const string SkyGiantId = "gi_sky_giant";

    private static readonly string[] SkyGiantHides = { "slick", "scales", "mottled", "banded", "hide" };

    /// <summary>A gas giant's sky giant (#2112, generation 18): a 40–80 block sailer — a long tapering body under a row of
    /// wing-sails, a fluke, a blunt head — that drifts between the islands and never lands. Always passive: a spectacle,
    /// not a threat; defeatable like every giant, but it never strikes back. Rolled outside the roster.</summary>
    public static CreatureSpecies GenerateSkyGiant(long worldSeed, string locationId)
    {
        long s = unchecked(worldSeed ^ ((long)WorldGenerator.StableHash("sky-giant:" + locationId) << 16) ^ 0x5C1A7E5L);
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        float length = 40f + (float)rng.NextDouble() * 40f;    // 40–80 blocks
        float girth = 4f + (float)rng.NextDouble() * 3f;       // 4–7
        var sp = new CreatureSpecies
        {
            Id = SkyGiantId,
            NameKey = "creature.generic.name",
            Name = NameGenerator.Creature(rng),
            Habitat = CreatureHabitat.Air,
            Activity = CreatureActivity.Cathemeral,
            Temperament = CreatureTemperament.Passive,
            LocoStyle = LocomotionStyle.Drifter,
            BodyPlan = CreatureBodyPlan.SkyGiant,
            GiantHeight = length * 0.5f,                       // the scan's scale, and the health-bar anchor
            Size = length / 20f,
            WormGirth = girth,
            WormLength = length,
            BodySegments = 18 + rng.Next(9),                   // 18..26
            WingPairs = 3 + rng.Next(3),                       // 3..5 sails along the back
            HasWings = true,
            HasTail = true,
            HasCrest = rng.NextDouble() < 0.5,
            Horns = rng.Next(3),
            Eyes = 2,
            Legs = 0,
            HoverAltitude = 50f,
            MaxHealth = 1800f + (length - 40f) * 30f,          // 1800..3000
            AttackDamage = 0f,
            Speed = 5f + (float)rng.NextDouble() * 3f,         // a slow drift, blocks/s
            ColorRgb = SkyColor(rng),
            BellyRgb = SeaBellyColor(rng),
            Glows = rng.NextDouble() < 0.5,
            Hide = SkyGiantHides[rng.Next(SkyGiantHides.Length)],
            DropItem = "creature_meat",
            DropCount = 16,
            DropKind = CreatureDropKind.Food,
        };
        sp.VoiceSeed = unchecked((int)(s ^ (s >> 32)) ^ 0x5EED_1CE);
        return sp;
    }

    /// <summary>The sky giant's back: dusk violet, storm grey, rose, pale amber — the colours of its own sky.</summary>
    private static int SkyColor(System.Random rng)
    {
        int[] palette = { 0x6C5B7B, 0x8E7C93, 0xA26769, 0xC9A27A, 0x5D6D7E, 0x9B8BB4, 0xB48A78 };
        return Jitter(palette[rng.Next(palette.Length)], rng.Next(-12, 13));
    }

    public const string LeviathanId = "gi_leviathan";

    private static readonly string[] LeviathanHides = { "scales", "slick", "plated", "banded", "mottled" };

    /// <summary>A deep-sea world's leviathan (#2111, generation 18): the largest form of water life — the sandworm's long body
    /// with fins along the flanks and a fluke, two jaws instead of mandible petals, a sea palette, its size, hearing and temper
    /// rolled per world. It always hunts (fish, and whoever swims or sails). The species is rolled outside the roster, so no
    /// older world's species move.</summary>
    public static CreatureSpecies GenerateLeviathan(long worldSeed, string locationId)
    {
        long s = unchecked(worldSeed ^ ((long)WorldGenerator.StableHash("leviathan:" + locationId) << 16) ^ 0x1E71A7A4L);
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        float height = 30f + (float)rng.NextDouble() * 20f;        // how high it breaches (30–50)
        float girth = 4f + (float)rng.NextDouble() * 3f;           // 4–7: hidden at 7.6–11.8 under the surface
        var sp = new CreatureSpecies
        {
            Id = LeviathanId,
            NameKey = "creature.generic.name",
            Name = NameGenerator.Creature(rng),
            Habitat = CreatureHabitat.Water,
            Activity = CreatureActivity.Cathemeral,
            Temperament = rng.NextDouble() < 0.5 ? CreatureTemperament.Aggressive : CreatureTemperament.Territorial,
            LocoStyle = LocomotionStyle.Slitherer,
            BodyPlan = CreatureBodyPlan.Leviathan,
            GiantHeight = height,
            Size = height / 10f,
            WormGirth = girth,
            WormLength = 60f + (float)rng.NextDouble() * 60f,      // 60–120 blocks
            BodySegments = 24 + rng.Next(13),                       // 24..36 segments
            Mandibles = 2,                                          // upper and lower jaw
            Horns = rng.Next(3),                                    // rows of dorsal spines
            HasCrest = rng.NextDouble() < 0.7,                      // a dorsal fin
            HasFins = true,
            FinPairs = 2 + rng.Next(2),                             // 2..3 pairs of flank fins
            HasTail = true,
            Hearing = 0.9f + (float)rng.NextDouble() * 0.5f,
            MaxHealth = 2000f + (height - 30f) * 60f,               // 2000..3200
            AttackDamage = 30f + (float)rng.NextDouble() * 10f,     // per strike
            Speed = 8f + (float)rng.NextDouble() * 4f,              // under the surface, blocks/s
            Legs = 0,
            Eyes = 2,
            ColorRgb = SeaColor(rng),
            BellyRgb = SeaBellyColor(rng),
            Glows = rng.NextDouble() < 0.4,
            Hide = LeviathanHides[rng.Next(LeviathanHides.Length)],
            DropItem = "creature_meat",
            DropCount = 20,
            DropKind = CreatureDropKind.Food,
            SwallowsCreatures = true,                               // it hunts from the first day
        };
        sp.VoiceSeed = unchecked((int)(s ^ (s >> 32)) ^ 0x5EED_1CE);
        return sp;
    }

    /// <summary>The leviathan's back: deep blues, teal, slate, storm grey, bottle green.</summary>
    private static int SeaColor(System.Random rng)
    {
        int[] palette = { 0x1E3A5F, 0x1B4F72, 0x146C6E, 0x2E4053, 0x0E6655, 0x34495E, 0x1A5276, 0x2C3E50 };
        return Jitter(palette[rng.Next(palette.Length)], rng.Next(-12, 13));
    }

    /// <summary>The leviathan's belly: pale — bone, foam, pale teal, silver.</summary>
    private static int SeaBellyColor(System.Random rng)
    {
        int[] palette = { 0xD5DBDB, 0xE8F6F3, 0xAED6F1, 0xBDC3C7, 0xD1F2EB, 0xF4F6F7 };
        return Jitter(palette[rng.Next(palette.Length)], rng.Next(-10, 11));
    }

    private static int Jitter(int c, int jitter)
    {
        int r = System.Math.Clamp(((c >> 16) & 0xFF) + jitter, 0, 255);
        int g = System.Math.Clamp(((c >> 8) & 0xFF) + jitter, 0, 255);
        int b = System.Math.Clamp((c & 0xFF) + jitter, 0, 255);
        return (r << 16) | (g << 8) | b;
    }

    public static CreatureSpecies GenerateSandworm(long worldSeed, string locationId, int terrainGeneration = 0)
    {
        long s = unchecked(worldSeed ^ ((long)WorldGenerator.StableHash("sandworm:" + locationId) << 16) ^ 0x5A2D3A0L);
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        float height = 40f + (float)rng.NextDouble() * 20f;
        float girth = 7f + (float)rng.NextDouble() * 4f;
        var sp = new CreatureSpecies
        {
            Id = SandwormId,
            NameKey = "creature.generic.name",
            Name = NameGenerator.Creature(rng),
            Habitat = CreatureHabitat.Land,
            Activity = CreatureActivity.Cathemeral,
            Temperament = rng.NextDouble() < 0.6 ? CreatureTemperament.Aggressive : CreatureTemperament.Territorial,
            LocoStyle = LocomotionStyle.Slitherer,
            BodyPlan = CreatureBodyPlan.Sandworm,
            GiantHeight = height,
            Size = height / 10f,
            WormGirth = girth,
            WormLength = height * 2.1f + (float)rng.NextDouble() * 20f,
            BodySegments = 30 + rng.Next(15),                 // ring segments
            Mandibles = 3 + rng.Next(3),                      // 3..5 petals
            Horns = rng.Next(4),                              // rows of back spikes
            HasCrest = rng.NextDouble() < 0.5,                // a plated dorsal ridge
            Hearing = 0.8f + (float)rng.NextDouble() * 0.5f,
            MaxHealth = 2500f + (height - 40f) * 75f,         // 2500..4000
            AttackDamage = 35f + (float)rng.NextDouble() * 10f, // per strike
            Speed = 9f + (float)rng.NextDouble() * 3f,        // under the sand, blocks/s
            Legs = 0,
            Eyes = 0,
            ColorRgb = SandColor(rng),
            BellyRgb = SandColor(rng),
            Glows = rng.NextDouble() < 0.3,
            Hide = WormHides[rng.Next(WormHides.Length)],
            DropItem = "creature_meat",
            DropCount = 20,
            DropKind = CreatureDropKind.Food,
            SwallowsCreatures = GiantRules.RolledWormsHunt(terrainGeneration), // #2076: from the generation, not the roll — no trait moves
        };
        sp.VoiceSeed = unchecked((int)(s ^ (s >> 32)) ^ 0x5EED_1CE);
        return sp;
    }

    /// <summary>The sandworm's palette: sand, ochre, rust, umber and dusk tones.</summary>
    private static int SandColor(System.Random rng)
    {
        int[] palette = { 0xC8A26B, 0xB5793E, 0x8E5A33, 0x6B4A36, 0xA88B6A, 0x7E6A7A, 0xD2B48C, 0x9C6644 };
        int c = palette[rng.Next(palette.Length)];
        int jitter = rng.Next(-14, 15);
        int r = System.Math.Clamp(((c >> 16) & 0xFF) + jitter, 0, 255);
        int g = System.Math.Clamp(((c >> 8) & 0xFF) + jitter, 0, 255);
        int b = System.Math.Clamp((c & 0xFF) + jitter, 0, 255);
        return (r << 16) | (g << 8) | b;
    }
}
