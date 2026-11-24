// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Earth animals living wild away from Earth (#3976, terrain generation 49): a record with a chance
/// (<see cref="AuthoredCreature.WildElsewhere"/> — the dog and the cat, 15 %) joins the roster of a habitable world that is
/// not one of its homes. The roll is the world's own — the roster seed salted with the planet type and the record's key — so
/// no other species of any roster moves, and the stray is appended after every rolled and authored species and the
/// Schuppenhund. It is the same species the people of that world keep (<see cref="CreatureGenerator.MakeAuthored"/>, the
/// key-salted sub-seed), with its coats and voices; where it lives follows the rest of the record (its climate zones, its
/// exclusive rule), and like every wild ground animal it stays out of the towns (the town zone of
/// <c>GameServerCreatureTownRules.cs</c>).
/// </summary>
public static class StrayAnimals
{
    /// <summary>Whether a world of <paramref name="planet"/>'s type is one a stray could live on: air to breathe, plants and
    /// animals (<see cref="PlanetType.HasLife"/> — never an airless body or a void), and ground — never a gas giant.</summary>
    public static bool WorldTakesStrays(PlanetType planet)
        => planet.IsBreathable && planet.HasLife && !planet.IsGasWorld;

    /// <summary>Whether <paramref name="record"/> finds ground of its own on <paramref name="planet"/>'s type: always for a
    /// species that is at home anywhere; an exclusive one needs a biome of its surfaces or climate zones (on a type without a
    /// biome pool, the type's own zone).</summary>
    public static bool FindsGround(AuthoredCreature record, PlanetType planet)
    {
        if (!record.BiomeExclusive)
        {
            return true;
        }

        return planet.Biomes.Count == 0
            ? CreatureBiomes.NamesTheme(record.FloraThemes, CreatureBiomes.ThemeOf(null, planet))
            : CreatureBiomes.AffinityIn(record.BiomeSurfaces, record.FloraThemes, planet) >= 0;
    }

    /// <summary>The world's own roll for <paramref name="record"/>, uniform in [0, 1): <paramref name="rosterSeed"/>
    /// (<see cref="WorldGenerator.RosterSeedFor"/>) salted with the type and the key — no generator of any species touched.</summary>
    public static double Roll(AuthoredCreature record, PlanetType planet, long rosterSeed)
    {
        long s = unchecked(rosterSeed ^ WorldGenerator.StableHash(planet.Key) ^ WorldGenerator.StableHash("stray:" + record.Key));
        return new System.Random(unchecked((int)(s ^ (s >> 32)))).NextDouble();
    }

    /// <summary>Whether a world of <paramref name="planet"/>'s type with roster seed <paramref name="rosterSeed"/> hosts
    /// <paramref name="record"/> wild: a stray record, not at home here, a world it could live on, ground of its own, and the
    /// world's roll below its chance. The generation gate is the caller's.</summary>
    public static bool Hosts(AuthoredCreature record, PlanetType planet, long rosterSeed)
        => record.WildElsewhere > 0.0
           && !record.Homes.Contains(planet.Key)
           && !planet.AuthoredCreatures.Contains(record.Key)
           && WorldTakesStrays(planet)
           && FindsGround(record, planet)
           && Roll(record, planet, rosterSeed) < record.WildElsewhere;

    /// <summary>Appends the strays <paramref name="planet"/>'s world hosts to its roster <paramref name="list"/>, in the
    /// order of <paramref name="strays"/> — on a world of generation <see cref="WorldDescription.EarthSpeciesGeneration"/> or
    /// later only, so every older roster stays exactly as it was. A species the roster already holds is not added twice.</summary>
    internal static void AppendTo(List<CreatureSpecies> list, PlanetType planet, long rosterSeed, int terrainGeneration,
        IReadOnlyList<AuthoredCreature>? strays)
    {
        if (strays is not { Count: > 0 } || terrainGeneration < WorldDescription.EarthSpeciesGeneration)
        {
            return;
        }

        foreach (var record in strays)
        {
            if (Hosts(record, planet, rosterSeed) && !list.Exists(sp => sp.Id == AuthoredSpeciesMapping.IdOf(record.Key)))
            {
                list.Add(CreatureGenerator.MakeAuthored(record, planet, rosterSeed));
            }
        }
    }
}
