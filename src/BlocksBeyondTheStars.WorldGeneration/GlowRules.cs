// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>How much of a world's SURFACE plant life glows (#2857, terrain generation 29). Caves are not part of it: their
/// glowing moss, threads and prism blooms are the light down there on every world.</summary>
public enum GlowLevel
{
    /// <summary>No glowing plant grows on the surface (40 % of the worlds).</summary>
    None = 0,

    /// <summary>One glowing species, as an occasional find (30 %).</summary>
    Low,

    /// <summary>Glowing species roll like every other species, without the signature-plant bonus (20 %).</summary>
    Normal,

    /// <summary>Every glowing species grows and leads its patches; lantern trees grow (10 %).</summary>
    Luminous,
}

/// <summary>
/// The per-world glow level (#2857) — a pure function of the planet and the roster seed (the world seed salted with the
/// body, <see cref="WorldGenerator.RosterSeedFor"/>), so world generation and the server's scanner roster agree without
/// storing anything. Below <see cref="WorldDescription.TreeVarietyGeneration"/> every world is <see cref="GlowLevel.Normal"/>
/// in name only: no rule of the older generations reads the level.
/// </summary>
public static class GlowRules
{
    /// <summary>The shares of the four levels (decision 2026-10-20: 40 / 30 / 20 / 10 %).</summary>
    public const double NoneShare = 0.40, LowShare = 0.30, NormalShare = 0.20;

    private const long Salt = 0x2857_6C0E_57AA;

    /// <summary>This world's glow level. Themes that prefer glowing plants (crystal, ashen) never roll below
    /// <see cref="GlowLevel.Low"/>: their crystals and embers are what those worlds are.</summary>
    public static GlowLevel LevelFor(PlanetType planet, long rosterSeed, int terrainGeneration)
    {
        if (terrainGeneration < WorldDescription.TreeVarietyGeneration)
        {
            return GlowLevel.Normal;
        }

        long s = unchecked(rosterSeed ^ WorldGenerator.StableHash(planet.Key) ^ Salt);
        double roll = new System.Random(unchecked((int)(s ^ (s >> 32)))).NextDouble();
        var level = roll < NoneShare ? GlowLevel.None
            : roll < NoneShare + LowShare ? GlowLevel.Low
            : roll < NoneShare + LowShare + NormalShare ? GlowLevel.Normal
            : GlowLevel.Luminous;
        if (level == GlowLevel.None && (FloraThemes.Resolve(planet.FloraTheme).Preferred & FloraTag.Glow) != 0)
        {
            level = GlowLevel.Low;
        }

        return level;
    }
}
