// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The rules of the big herds and of the begging behaviour (2026-09, generation 12, #2018) — pure constants and predicates
/// the roster generator, the server and the tests share, in the style of <see cref="ArachnidRules"/>.
/// <list type="bullet">
/// <item><b>Big herds:</b> a peaceful land species may roll a group of 6–12 instead of the classic 2–3. Because a "few"
/// world holds about a dozen animals in total, a herd is <i>cheap</i> for the population model: its members count
/// <see cref="BigHerdCapDivisor"/>-for-one against the world cap and the species share (the hard cap stays a real count).</item>
/// <item><b>Begging:</b> a passive herd species may roll <see cref="CreatureSpecies.BegsForFood"/>. Its animals come hopping to a
/// player who holds something edible, orbit the player at <see cref="OrbitRadiusFor"/>, call fast, and rush a thrown piece.
/// Skittish species never beg (decision 2026-09-26), nor do companions and giants.</item>
/// </list>
/// </summary>
public static class HerdRules
{
    // --- Generation-12 draws (CreatureGenerator.ApplyBigHerds) ---

    /// <summary>Share of passive standard-plan Land species that beg — and then always live in a herd of
    /// <see cref="BeggarHerdMin"/>–<see cref="BeggarHerdMax"/>.</summary>
    public const double BeggingChance = 0.30;
    public const int BeggarHerdMin = 8;
    public const int BeggarHerdMax = 12;

    /// <summary>Share of the OTHER peaceful (passive or skittish) standard-plan Land species that roll a big herd of
    /// <see cref="BigHerdMin"/>–<see cref="BigHerdMax"/> without begging.</summary>
    public const double BigHerdChance = 0.20;
    public const int BigHerdMin = 6;
    public const int BigHerdMax = 9;

    // --- The herd budget (GameServerCreatures) ---

    /// <summary>A species whose group is at least this big is a big herd.</summary>
    public const int BigHerdMinSize = 6;

    /// <summary>How many members of a big herd count as ONE animal against the world cap and the species share.</summary>
    public const int BigHerdCapDivisor = 3;

    /// <summary>The spawner places at most this many members of one group (was 5 before the big herds).</summary>
    public const int MaxGroupSize = 12;

    // --- Begging (GameServerFoodBegging) ---

    /// <summary>A beggar notices food in a hand this far away. No line of sight — it smells it; a sight ray per animal per
    /// tick is exactly the cost the behaviour avoids.</summary>
    public const float LureRange = 10f;

    /// <summary>A begging animal gives up when the player it begs from gets this far away.</summary>
    public const float LeaveRange = 14f;

    /// <summary>Begging without a reward lasts this long...</summary>
    public const double InterestSeconds = 25.0;

    /// <summary>...then the animal ignores food for this long.</summary>
    public const double CooldownSeconds = 60.0;

    /// <summary>The trot away after the food is stowed or the interest ran out.</summary>
    public const double LeaveSeconds = 4.0;

    /// <summary>The inner ring's base radius around the player; the body adds <c>Size × 0.6</c>.</summary>
    public const float OrbitRadius = 2.2f;

    /// <summary>The outer ring sits this much further out — a herd of twelve does not fit on one ring.</summary>
    public const float OuterRingExtra = 2.2f;

    /// <summary>How fast the orbit point travels around the player (radians per second).</summary>
    public const float OrbitRate = 0.9f;

    /// <summary>A grounded jumper hops this often while begging (seconds between launches).</summary>
    public const double HopInterval = 0.55;

    /// <summary>A thrown piece calls begging animals from this far.</summary>
    public const float PacketRushRange = 12f;

    /// <summary>A rushing animal has arrived at the piece within this distance and starts to squabble.</summary>
    public const float ArriveRange = 1.6f;

    /// <summary>The squabble orbit's base radius around the piece; every squabbler adds <see cref="SquabbleRadiusPerAnimal"/>.</summary>
    public const float SquabbleRadius = 1.2f;
    public const float SquabbleRadiusPerAnimal = 0.12f;

    /// <summary>How fast the squabble orbit turns (radians per second) — a scrum, not a parade.</summary>
    public const float SquabbleRate = 2.0f;

    /// <summary>The squabble lasts this long; the first animal whose timer ends eats the piece.</summary>
    public const double SquabbleMinSeconds = 3.0;
    public const double SquabbleMaxSeconds = 5.0;

    // --- The Feed action ---

    /// <summary>The thrown piece lands this far ahead of the player along the facing.</summary>
    public const float ThrowDistance = 2.5f;

    /// <summary>The thrower cannot pick the piece up again for this long (the auto-pickup reach is 2.5 blocks).</summary>
    public const double PickupGraceSeconds = 10.0;

    /// <summary>The client offers the Feed action while a begging animal is this near.</summary>
    public const float FeedProbeRange = 12f;

    /// <summary>Whether the species is a big herd (its members are cheap for the cap).</summary>
    public static bool IsBigHerd(CreatureSpecies sp) => sp.SocialGroupSize >= BigHerdMinSize;

    /// <summary>What <paramref name="members"/> live wild animals of the species cost against the world cap and the species
    /// share: every animal for a normal species, one per <see cref="BigHerdCapDivisor"/> (rounded up) for a big herd.</summary>
    public static int WeightedCount(CreatureSpecies sp, int members)
        => IsBigHerd(sp) ? (members + BigHerdCapDivisor - 1) / BigHerdCapDivisor : members;

    /// <summary>The species begs: the rolled flag on a passive Land species that is not a giant. The flag alone is not enough —
    /// an authored record may set it on anything, and the temper and the habitat are the rule (skittish never beg).</summary>
    public static bool BegsForFood(CreatureSpecies sp)
        => sp.BegsForFood && sp.Habitat == CreatureHabitat.Land && sp.Temperament == CreatureTemperament.Passive && !sp.IsGiant;

    /// <summary>What a begging animal smells: a consumable that restores hunger and does not poison (decision 2026-09-26). The
    /// taming baits restore no hunger and therefore do not count; toxic berries heal negatively and do not either.</summary>
    public static bool IsFoodForCreatures(ItemDefinition? item)
        => item is { Category: ItemCategory.Consumable } && item.ConsumeHunger > 0f && item.ConsumeHealth >= 0f;

    /// <summary>A point on the ring of <paramref name="radius"/> around <paramref name="centre"/> at <paramref name="angle"/>
    /// (radians; dirX = cos, dirZ = sin, like every heading). Horizontal — the caller keeps Y.</summary>
    public static Vector3f OrbitPoint(Vector3f centre, float radius, float angle)
        => new(centre.X + (float)System.Math.Cos(angle) * radius, centre.Y, centre.Z + (float)System.Math.Sin(angle) * radius);

    /// <summary>The ring a begging animal of this species orbits on: the inner ring grown by the body, or the outer ring for
    /// the half of a big herd that would not fit on the inner one.</summary>
    public static float OrbitRadiusFor(CreatureSpecies sp, bool outerRing)
        => OrbitRadius + System.Math.Clamp(sp.Size, 0.4f, 3f) * 0.6f + (outerRing ? OuterRingExtra : 0f);

    /// <summary>The squabble ring around a thrown piece for <paramref name="squabblers"/> animals.</summary>
    public static float SquabbleRadiusFor(int squabblers)
        => SquabbleRadius + System.Math.Max(0, squabblers - 1) * SquabbleRadiusPerAnimal;
}
