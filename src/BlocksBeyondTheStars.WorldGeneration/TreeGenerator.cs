// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Deterministically derives a planet's tree identity (<see cref="TreeSpecies"/>) from the world seed +
/// planet — the arboreal counterpart to <see cref="FloraGenerator"/>. Unlike flora there is a single tree
/// block pair (<c>wood_log</c> + <c>tree_leaves</c>) shared by every tree shape, so a world coins exactly
/// one tree species (the trunk and the leaves read as the same plant). Same seed + planet → the same
/// species, so nothing needs storing. Surfaced when the player scans a trunk or leaf block.
/// </summary>
public static class TreeGenerator
{
    /// <summary>This world's tree species, or null on worlds that grow no trees (airless / barren).</summary>
    public static TreeSpecies? Generate(PlanetType planet, long worldSeed)
    {
        // Airless + barren worlds grow no flora, and trees gate on flora (see WorldGenerator.StampTrees),
        // so they have no tree identity either.
        if (planet.IsAirless || planet.FloraDensity <= 0)
        {
            return null;
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ 0x77EEA1100B;
        var rng = new System.Random(unchecked((int)(planetSeed ^ (planetSeed >> 32))));
        return new TreeSpecies
        {
            Id = "tr0",
            Name = NameGenerator.Tree(rng),
            Toxic = rng.NextDouble() < 0.3, // most trees are benign; a notable minority is toxic (matches flora)
        };
    }
}
