// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// What a body IS in one world (#3969): its walkable circumference, its gravity and how big the flight view draws it —
/// resolved in one place (<see cref="Resolve"/>) from the body's fixed values (<see cref="FixedBodyDefinition"/>, Sol) when
/// it has them, else from today's rolls (<see cref="WorldConstants.CircumferenceFor(string, WorldConstants.WorldSizeClass,
/// float, long, int)"/>, <see cref="BodyGravity.FactorFor"/>), which a rolled body gets bit for bit. The server sizes every
/// world, pad, weather and star-map entry from it; the client reads the result from the star map.
/// </summary>
public readonly struct BodyProfile
{
    public BodyProfile(int circumference, float gravityFactor, float fixedVisibleDiameter)
    {
        Circumference = circumference;
        GravityFactor = gravityFactor;
        FixedVisibleDiameter = fixedVisibleDiameter;
    }

    /// <summary>The walkable circumference in blocks.</summary>
    public int Circumference { get; }

    /// <summary>The gravity multiplier (1 = normal).</summary>
    public float GravityFactor { get; }

    /// <summary>The drawn diameter the body's data fixes (Jupiter far bigger than its walkable size); 0 = none.</summary>
    public float FixedVisibleDiameter { get; }

    /// <summary>The body's drawn diameter in the flight view: the fixed one, else the one its circumference gives
    /// (<see cref="SystemBodyLayout.VisibleDiameter"/>).</summary>
    public float VisibleDiameter => SystemBodyLayout.VisibleDiameter(FixedVisibleDiameter, Circumference);

    /// <summary>The profile of body <paramref name="bodyId"/> of size class <paramref name="sizeClass"/> and archetype bias
    /// <paramref name="sizeBias"/> in a world of <paramref name="worldSeed"/> / <paramref name="terrainGeneration"/>: each
    /// value <paramref name="fixedValues"/> sets (above 0) wins, every other one is rolled as before.</summary>
    public static BodyProfile Resolve(FixedBodyDefinition? fixedValues, string bodyId, WorldConstants.WorldSizeClass sizeClass,
        float sizeBias, long worldSeed, int terrainGeneration)
    {
        int circumference = fixedValues is { Circumference: > 0 }
            ? fixedValues.Circumference
            : WorldConstants.CircumferenceFor(bodyId, sizeClass, sizeBias, worldSeed, terrainGeneration);
        float gravity = fixedValues is { Gravity: > 0f }
            ? fixedValues.Gravity
            : BodyGravity.FactorFor(bodyId, sizeClass, worldSeed, terrainGeneration);
        return new BodyProfile(circumference, gravity, fixedValues?.VisibleDiameter ?? 0f);
    }
}
