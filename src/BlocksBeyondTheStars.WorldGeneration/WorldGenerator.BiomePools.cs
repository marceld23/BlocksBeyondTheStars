// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3575: the biomes of a world in the planet TYPE's numbering. A world takes 2 … pool of its type's biomes and shuffles
/// them (<see cref="ResolveBiomes"/>), so <see cref="BiomeIndexAt"/> counts in the world's own list — while a creature's
/// biome affinity, its colour family and the bio lab's habitat tags count in the type's pool. These queries translate, so
/// the spawner can ask "is this species native to the ground here?" in the species' own numbering.
/// (partial of <see cref="WorldGenerator"/>)
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The pool index (<c>planet.Biomes</c>) of the biome at a column; -1 on a type without a pool (its single
    /// biome is the planet's own surface).</summary>
    public int BiomePoolIndexAt(PlanetType planet, int worldX, int worldZ)
    {
        var biomes = ResolveBiomes(planet);
        int index = biomes.Count <= 1 ? 0 : System.Math.Clamp(BiomeIndexAt(planet, worldX, worldZ), 0, biomes.Count - 1);
        return biomes[index].PoolIndex;
    }

    /// <summary>Which of the type's pool biomes this world carries, by pool index (the rest were not picked for it).</summary>
    public bool[] PoolBiomesOnWorld(PlanetType planet)
    {
        var on = new bool[planet.Biomes.Count];
        foreach (var b in ResolveBiomes(planet))
        {
            if (b.PoolIndex >= 0 && b.PoolIndex < on.Length)
            {
                on[b.PoolIndex] = true;
            }
        }

        return on;
    }
}
