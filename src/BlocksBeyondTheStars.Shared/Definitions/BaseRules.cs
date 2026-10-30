// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// Player bases beyond the single base core (2026-10-29): several bases per world (#2996), discovery of other players'
/// bases (#2997) and the yield every base core and player station gathers over time (#2999).
/// </summary>
public static class BaseRules
{
    /// <summary>The steps of the world rule <c>GameRules.BasesPerWorld</c> (#2996): how many base cores one player may found
    /// on one body. 1 is the rule before 2026-10-29.</summary>
    public static readonly int[] BasesPerWorldChoices = { 1, 2, 3, 5, 8, 12 };

    /// <summary>The default of <c>GameRules.BasesPerWorld</c>.</summary>
    public const int BasesPerWorldDefault = 5;

    /// <summary>Residents one player's bases on one body hold together (#2996). The server runs every resident, so the cap
    /// is per body, not per base: five full bases would otherwise be 160 people on a Raspberry Pi. Every base keeps at
    /// least its founding settler.</summary>
    public const int ResidentCapPerBody = 32;

    /// <summary>Horizontal distance (blocks) from a base core within which a player discovers another player's base (#2997).
    /// A little more than the pioneer outposts' ~40 m, well more than the 17³ core zone, so the dome and the zone hint
    /// are always there before anyone stands in them.</summary>
    public const int DiscoveryRadius = 48;

    /// <summary>The steps of the world rule <c>GameRules.BaseYieldPercent</c> (#2999): Off, Half, Normal, Double.</summary>
    public static readonly int[] YieldChoicesPercent = { 0, 50, 100, 200 };

    /// <summary>The default of <c>GameRules.BaseYieldPercent</c>.</summary>
    public const int YieldDefaultPercent = 100;

    /// <summary>Items one yield store holds before it stops (#2999). A full store simply waits — nothing is lost, and nobody
    /// has to come back every day.</summary>
    public const int YieldCapacity = 64;

    /// <summary>Items per hour every base core or commissioned station gathers on its own.</summary>
    public const double YieldBasePerHour = 6.0;

    /// <summary>Extra items per hour per resident of the base (or crew member of the station), up to <see cref="YieldHelperCap"/>.</summary>
    public const double YieldPerHelperPerHour = 2.0;

    /// <summary>Residents or crew members that add to the rate.</summary>
    public const int YieldHelperCap = 8;

    /// <summary>The longest absence one read credits (a changed clock or a restored backup must not mint a fortune; the
    /// store's capacity caps the result long before this anyway).</summary>
    public const long YieldMaxGapSeconds = 30L * 24 * 3600;

    /// <summary>What a base yields when its body has no common ore at all.</summary>
    public const string BaseYieldFallbackItem = "stone";

    /// <summary>What a player station gathers from its orbit, with weights: ice and dust, metal scrap, now and then a data
    /// fragment. Nothing rare — the economy never mints rare metals from nothing.</summary>
    public static readonly KeyValuePair<string, int>[] StationYield =
    {
        new("ice", 4),
        new("iron_ore", 3),
        new("scrap_metal", 3),
        new("silicate", 2),
        new("carbon", 2),
        new("data_fragment", 1),
    };

    /// <summary>The rate (items per hour) of a store with <paramref name="helpers"/> residents or crew at the world rule's
    /// <paramref name="percent"/>.</summary>
    public static double YieldPerHour(int helpers, int percent)
    {
        int h = System.Math.Max(0, System.Math.Min(helpers, YieldHelperCap));
        return (YieldBasePerHour + (YieldPerHelperPerHour * h)) * System.Math.Max(0, percent) / 100.0;
    }

    /// <summary>
    /// Turns the real time since the store's last read into items (#2999): <paramref name="nowUnix"/> − the last read, at
    /// <paramref name="perHour"/>, up to <see cref="YieldCapacity"/>; <paramref name="pick"/> names the n-th item ever made
    /// (deterministic per store). The first read only starts the clock. A full store does not accrue (it stops, nothing is
    /// lost); a negative gap (the clock went back) credits nothing. Returns how many items were added.
    /// </summary>
    public static int Accrue(BaseYieldStore store, long nowUnix, double perHour, System.Func<long, string> pick)
    {
        if (store.LastUnix <= 0)
        {
            store.LastUnix = nowUnix;
            return 0;
        }

        long gap = nowUnix - store.LastUnix;
        store.LastUnix = nowUnix;
        if (gap <= 0 || perHour <= 0.0)
        {
            return 0;
        }

        int room = YieldCapacity - store.CountReady();
        if (room <= 0)
        {
            store.Progress = 0.0;
            return 0;
        }

        double units = store.Progress + (System.Math.Min(gap, YieldMaxGapSeconds) / 3600.0 * perHour);
        int whole = (int)System.Math.Min(System.Math.Floor(units), room);
        store.Progress = whole >= room ? 0.0 : units - System.Math.Floor(units);
        for (int i = 0; i < whole; i++)
        {
            string item = pick(store.Produced++);
            store.Items[item] = store.Items.TryGetValue(item, out int n) ? n + 1 : 1;
        }

        return whole;
    }

    /// <summary>Picks from a weighted list by a stable hash of <paramref name="seed"/> and <paramref name="index"/> — the same
    /// store makes the same items in the same order.</summary>
    public static string PickWeighted(IReadOnlyList<KeyValuePair<string, int>> table, string seed, long index)
    {
        int total = 0;
        foreach (var e in table)
        {
            total += System.Math.Max(0, e.Value);
        }

        if (total <= 0)
        {
            return table.Count > 0 ? table[0].Key : BaseYieldFallbackItem;
        }

        ulong h = 1469598103934665603UL; // FNV-1a over the seed, then the index mixed in
        foreach (char c in seed)
        {
            h = (h ^ c) * 1099511628211UL;
        }

        h ^= (ulong)index * 0x9E3779B97F4A7C15UL;
        h ^= h >> 31;
        h *= 0xBF58476D1CE4E5B9UL;
        h ^= h >> 29;
        int roll = (int)(h % (ulong)total);
        foreach (var e in table)
        {
            roll -= System.Math.Max(0, e.Value);
            if (roll < 0)
            {
                return e.Key;
            }
        }

        return table[table.Count - 1].Key;
    }
}
