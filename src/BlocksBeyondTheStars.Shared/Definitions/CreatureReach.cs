// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// One rule for server and client (#2565): how far a creature's bite reaches. The server bites a player on foot within
/// this distance (3D, with a clear line of sight); the client plays the creature's lunge and attack call within the same
/// distance. The client used a fixed 3 blocks while the server bit from 4 and more, so a flier hovering 3–5 blocks away
/// drained health with no sound and no movement at all.
/// <para>A titan's bite reaches further than a sheep-sized nip (#638): past size 2 the reach grows by half a block per
/// size step, so a provoked giant cannot be poked safely from just outside 4 blocks. The half-rate growth caps it at 6
/// for the size-6 giants — exactly the player's own melee reach, so anything that can bite the player can always be hit
/// back. The size is the SPECIES size, not an individual's cosmetic size variance.</para>
/// </summary>
public static class CreatureReach
{
    /// <summary>Bite reach of every species up to size 2, in blocks.</summary>
    public const float BaseBiteRange = 4f;

    /// <summary>Species size from which the reach starts to grow.</summary>
    public const float GrowthStartSize = 2f;

    /// <summary>Extra reach per size step past <see cref="GrowthStartSize"/>, in blocks.</summary>
    public const float GrowthPerSize = 0.5f;

    /// <summary>How far a creature of species size <paramref name="speciesSize"/> bites, in blocks.</summary>
    public static float BiteRange(float speciesSize)
        => BaseBiteRange + System.Math.Max(0f, (speciesSize - GrowthStartSize) * GrowthPerSize);
}
