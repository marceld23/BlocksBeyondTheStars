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

    /// <summary>#3326: whether a species bites in chunks — <see cref="CreatureSpecies.BiteDamage"/> once per
    /// <see cref="CreatureSpecies.BiteInterval"/> — instead of draining <see cref="CreatureSpecies.AttackDamage"/> per second.
    /// The Schuppenhund and the Soundrak (#3750) do; every other species keeps the drain. A drain below the player's natural
    /// healing (2 health a second on a breathable world, less after armour) never shows on the health bar, so a species
    /// that is meant to be felt strikes in chunks.</summary>
    public static bool BitesInChunks(CreatureSpecies sp) => sp.BiteInterval > 0f && sp.BiteDamage > 0f;

    /// <summary>#3327/#3750: whether the client shows a body plan's attack — the lunge, the attack call, the red swipe — only
    /// at the moment the server reports a hit from it, never on the random cadence of a draining hunter: every lunge is a
    /// strike and every strike a lunge. The wire carries no bite interval, so the body plan stands in for
    /// <see cref="BitesInChunks"/>: it must name every plan whose species bite in chunks (the Schuppenhund, the Soundrak).
    /// <paramref name="bodyPlan"/> is the plan's name as the wire carries it.</summary>
    public static bool LungesOnHit(string? bodyPlan)
        => bodyPlan == nameof(CreatureBodyPlan.Hound) || bodyPlan == nameof(CreatureBodyPlan.Soundrak);
}
