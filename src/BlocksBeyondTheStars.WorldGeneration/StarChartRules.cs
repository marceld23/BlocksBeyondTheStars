// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The pure rules of the hidden star map (#3997, <c>WorldDescription.HiddenStarMap</c>; docs/developer/HIDDEN_STAR_MAP.md):
/// how many neighbours a system charts when a player first enters it, and which systems are "the nearest". No server state,
/// integer hashing only (no trig or float noise), so the counts are identical on every platform and run.
/// </summary>
public static class StarChartRules
{
    /// <summary>Systems a jump with a radar array fitted charts on top (#3997 rule 2).</summary>
    public const int RadarBonus = 2;

    /// <summary>Systems a player gets when nothing visible is left to enter but some are still hidden (rule 7).</summary>
    public const int RescueCount = 2;

    /// <summary>The fewest neighbours the home system charts, so the first jump always has a choice.</summary>
    public const int HomeMinimum = 2;

    /// <summary>The cumulative percent thresholds of the neighbour count: 1 = 10 %, 2 = 35 %, 3 = 35 %, 4 = 20 %.</summary>
    private static readonly int[] Thresholds = { 10, 45, 80, 100 };

    /// <summary>How many uncharted neighbours entering <paramref name="systemId"/> for the first time charts: 1–4, fixed per
    /// world and system (the same for every player), weighted toward 2 and 3 so a single neighbour is rare. The home system
    /// charts at least <see cref="HomeMinimum"/>.</summary>
    public static int NeighbourCount(long seed, string systemId, bool isHome)
    {
        int roll = Percent(seed, "starchart:neighbours:" + systemId);
        int count = 1;
        while (count < Thresholds.Length && roll >= Thresholds[count - 1])
        {
            count++;
        }

        return isHome ? System.Math.Max(HomeMinimum, count) : count;
    }

    /// <summary>A stable roll in 0..99 for the seed and a salt (FNV-1a of the salt, mixed with the seed by a 64-bit
    /// finalizer so neighbouring ids do not share low bits).</summary>
    internal static int Percent(long seed, string salt)
    {
        unchecked
        {
            ulong h = (ulong)WorldGenerator.StableHash(salt) ^ (ulong)seed;
            h ^= h >> 33;
            h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33;
            h *= 0xc4ceb9fe1a85ec53UL;
            h ^= h >> 33;
            return (int)(h % 100UL);
        }
    }

    /// <summary>Up to <paramref name="count"/> of <paramref name="candidates"/>, nearest to (<paramref name="fromX"/>,
    /// <paramref name="fromY"/>) by star-map distance first; ties go to the lower ordinal id, so the pick is deterministic.</summary>
    public static List<StarSystem> Nearest(IEnumerable<StarSystem> candidates, float fromX, float fromY, int count)
    {
        if (count <= 0)
        {
            return new List<StarSystem>();
        }

        return candidates
            .OrderBy(s => ((double)(s.MapX - fromX) * (s.MapX - fromX)) + ((double)(s.MapY - fromY) * (s.MapY - fromY)))
            .ThenBy(s => s.Id, System.StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }
}
