// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// The gravity multiplier of a body (1.0 = baseline) — THE one formula (#2697). The body's size class sets the band
/// (asteroids feather-light, moons low, planets full and sometimes heavy) and a seeded roll picks within it, so two
/// same-size worlds still differ. It drives jump height, walk speed, the jetpack, fall damage and the colossus gate.
/// <para>
/// The CLASSIC roll (every world below <see cref="WorldDescription.HashVarietyGeneration"/>) reads the low 16 bits of
/// <c>h·31+c</c> of the body id XOR the low seed word. Consecutive ids ("sys0-p0", "sys0-p1" …) differ only in the last
/// character, which moves those bits by one — so every planet of a system came out within a few ten-thousandths of a g
/// of the others. From generation 26 the roll is the mixed hash of the id salted with the world seed
/// (<see cref="StableHashing"/>, its own salt), spread over the whole band. An older world keeps its gravity bit for bit,
/// so a planet players know never suddenly feels different.
/// </para>
/// The server computes it in one place (<c>GameServer.WorldGravityOf</c>: the loaded world's environment and the ship
/// scanner's overview card) and sends it in <c>WorldEnvironment.GravityFactor</c>; the client never recomputes it.
/// <b>Bit-frozen once shipped</b> like every generation-26 roll: <c>BodyGravityTests</c> pins known answers.
/// </summary>
public static class BodyGravity
{
    /// <summary>Salt of the seeded gravity roll (#2697), so it never correlates with another choice keyed by the same
    /// (body id, seed) pair (the size roll, the colour families).</summary>
    private const ulong GravitySalt = 0x2697_0000_6EA7_0001UL;

    /// <summary>True when a world of <paramref name="terrainGeneration"/> rolls its gravity from the mixed hash (#2697).</summary>
    public static bool HasSeededGravity(int terrainGeneration) => terrainGeneration >= WorldDescription.HashVarietyGeneration;

    /// <summary>The gravity band of a size class: asteroids 0.35–0.55 g, moons 0.55–0.85 g, planets 0.8–1.6 g.</summary>
    public static (float Lo, float Hi) BandFor(WorldConstants.WorldSizeClass cls) => cls switch
    {
        WorldConstants.WorldSizeClass.Asteroid => (0.35f, 0.55f),
        WorldConstants.WorldSizeClass.Moon => (0.55f, 0.85f),
        _ => (0.80f, 1.60f),
    };

    /// <summary>The gravity multiplier of <paramref name="locationId"/> with the size class <paramref name="cls"/> in a
    /// world of <paramref name="terrainGeneration"/> created with <paramref name="worldSeed"/>: the classic roll below
    /// <see cref="WorldDescription.HashVarietyGeneration"/>, the mixed one from it.</summary>
    public static float FactorFor(string? locationId, WorldConstants.WorldSizeClass cls, long worldSeed, int terrainGeneration)
    {
        (float lo, float hi) = BandFor(cls);
        if (!HasSeededGravity(terrainGeneration))
        {
            return ClassicFactor(lo, hi, unchecked((uint)(AtmosphereTints.StableHash(locationId) ^ (int)worldSeed)));
        }

        ulong mixed = StableHashing.Mix64(StableHashing.Mixed(locationId ?? string.Empty, worldSeed) ^ GravitySalt);
        return (float)(lo + StableHashing.Unit(mixed) * (hi - lo));
    }

    /// <summary>The classic roll, bit-identical to every release before #2697: the low 16 bits of the salted
    /// <c>h·31+c ^ seed</c> word, in single precision.</summary>
    private static float ClassicFactor(float lo, float hi, uint h)
    {
        // Salt the hash (so gravity isn't correlated with the sky/flora hues seeded from the same id ^ seed).
        float t = ((h ^ 0x9E3779B9u) & 0xFFFFu) / 65535f;
        return lo + t * (hi - lo);
    }
}
