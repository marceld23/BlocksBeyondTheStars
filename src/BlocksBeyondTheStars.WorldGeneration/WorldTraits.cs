// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The traits one world ROLLED (#2024, the toxic-world class): corrosive air, toxic water, cave fauna, rare-ore
/// outcrops. A planet type names a chance per trait; every world of the type rolls its own, so no two toxic worlds
/// are alike. THE one function every consumer calls — worldgen (outcrops), the creature roster (cave fauna), the
/// server (air and water damage, the water tint, VEGA's scan) — with the roster seed of <see cref="WorldGenerator.RosterSeedFor"/>,
/// so they can never disagree (the #1722 discipline). Nothing is persisted: the chances are generator constants.
/// </summary>
public readonly struct WorldTraits
{
    public WorldTraits(bool corrosiveAir, bool toxicWater, bool caveFauna, bool oreOutcrops)
    {
        CorrosiveAir = corrosiveAir;
        ToxicWater = toxicWater;
        CaveFauna = caveFauna;
        OreOutcrops = oreOutcrops;
    }

    /// <summary>The air eats health outdoors (<see cref="PlanetType.AirDamagePerSecond"/>).</summary>
    public bool CorrosiveAir { get; }

    /// <summary>The water hurts (<see cref="PlanetType.WaterDamagePerSecond"/>) and looks poisonous.</summary>
    public bool ToxicWater { get; }

    /// <summary>A world with no procedural fauna keeps a few cave species.</summary>
    public bool CaveFauna { get; }

    /// <summary>Clumps of the type's rare-tier ores lie on the surface.</summary>
    public bool OreOutcrops { get; }

    /// <summary>This world's traits. <paramref name="rosterSeed"/> = <see cref="WorldGenerator.RosterSeedFor"/>(world seed, body);
    /// <paramref name="generation"/> = the save's terrain generation.</summary>
    public static WorldTraits For(PlanetType planet, long rosterSeed, int generation)
        => new(
            planet.AirDamagePerSecond > 0 && Rolls(planet, rosterSeed, generation, "corrosive_air", planet.CorrosiveAirChance),
            planet.WaterDamagePerSecond > 0 && Rolls(planet, rosterSeed, generation, "toxic_water", planet.WaterDamageChance),
            Rolls(planet, rosterSeed, generation, "cave_fauna", planet.CaveFaunaChance),
            Rolls(planet, rosterSeed, generation, "ore_outcrops", planet.OreOutcropChance));

    /// <summary>One trait's roll: a chance of 0 (or less) never hits, 1 (or more) always does on any generation — so a
    /// type that always had the trait (Titas' water, generation 8) keeps it without a roll — and anything between rolls
    /// per world, on generation-13 worlds only. Each trait has its own salt, so the rolls are independent.</summary>
    public static bool Rolls(PlanetType planet, long rosterSeed, int generation, string trait, double chance)
    {
        if (chance <= 0.0)
        {
            return false;
        }

        if (chance >= 1.0)
        {
            return true;
        }

        if (generation < WorldDescription.ToxicWorldsGeneration)
        {
            return false;
        }

        long s = unchecked(rosterSeed ^ WorldGenerator.StableHash(planet.Key) ^ WorldGenerator.StableHash("trait:" + trait));
        return new System.Random(unchecked((int)(s ^ (s >> 32)))).NextDouble() < chance;
    }
}
