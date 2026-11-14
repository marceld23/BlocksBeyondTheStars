// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// A bucket index over a world's reserved sites (#3591). A grown settlement reserves one site per lot and per road stretch
/// instead of one box, so a world holds hundreds of sites; the site-grade stage asks for every column which of them reach it.
/// The index files each site under the 32-block buckets its box plus its reach covers (both seams wrapped the way
/// <c>WorldGenerator.DistanceToBox</c> wraps them) and answers a column with the sites of its bucket, in list order — so
/// every caller computes exactly what a scan over the whole list computed, only without the sites that cannot reach it.
/// </summary>
internal sealed class SiteIndex
{
    private const int Bucket = 32;

    /// <summary>The farthest any caller looks past a site's box: a site's own grade radius, or the rune shrines' and the
    /// flora's claim reach (16) plus the one block <c>SiteClaims</c> adds.</summary>
    private const int ClaimReach = 17;

    private readonly Dictionary<long, List<int>> _buckets = new();
    private readonly int _circumference;
    private readonly int _period;

    private SiteIndex(int circumference)
    {
        _circumference = circumference;
        _period = WorldConstants.LatitudePeriodFor(circumference);
    }

    /// <summary>The index of <paramref name="sites"/> on a world of <paramref name="circumference"/>; null when the world has
    /// no sites or no circumference yet (the callers then scan the list).</summary>
    public static SiteIndex? Build(SiteFlat[] sites, int circumference)
    {
        if (sites.Length == 0 || circumference <= 0 || WorldConstants.LatitudePeriodFor(circumference) <= 0)
        {
            return null;
        }

        var index = new SiteIndex(circumference);
        for (int i = 0; i < sites.Length; i++)
        {
            index.Add(i, sites[i]);
        }

        return index;
    }

    /// <summary>The sites whose box plus reach may cover the column, ascending; null when none does.</summary>
    public List<int>? Near(int worldX, int worldZ)
    {
        int bx = WorldConstants.WrapX(worldX, _circumference) / Bucket;
        int bz = (WorldConstants.WrapZ(worldZ, _circumference) + (_period / 2)) / Bucket;
        return _buckets.TryGetValue(Key(bx, bz), out var list) ? list : null;
    }

    private void Add(int siteIndex, in SiteFlat site)
    {
        int reach = System.Math.Max(site.Radius, ClaimReach) + 1;
        var xs = Spans(WorldConstants.WrapX(site.MinX - reach, _circumference), site.Width + (2 * reach), _circumference);
        var zs = Spans(WorldConstants.WrapZ(site.MinZ - reach, _circumference) + (_period / 2), site.Length + (2 * reach), _period);
        foreach (int bx in xs)
        {
            foreach (int bz in zs)
            {
                long key = Key(bx, bz);
                if (!_buckets.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    _buckets[key] = list;
                }

                if (list.Count == 0 || list[list.Count - 1] != siteIndex)
                {
                    list.Add(siteIndex);
                }
            }
        }
    }

    /// <summary>The buckets an interval of <paramref name="length"/> starting at <paramref name="start"/> (in [0, period))
    /// covers on a ring of <paramref name="period"/>.</summary>
    private static List<int> Spans(int start, int length, int period)
    {
        var buckets = new List<int>();
        int count = (period + Bucket - 1) / Bucket;
        if (length >= period)
        {
            for (int b = 0; b < count; b++)
            {
                buckets.Add(b);
            }

            return buckets;
        }

        int end = start + length - 1;
        if (end < period)
        {
            AddRange(buckets, start, end);
        }
        else
        {
            AddRange(buckets, start, period - 1);
            AddRange(buckets, 0, end - period);
        }

        return buckets;
    }

    private static void AddRange(List<int> buckets, int from, int to)
    {
        for (int b = from / Bucket; b <= to / Bucket; b++)
        {
            if (!buckets.Contains(b))
            {
                buckets.Add(b);
            }
        }
    }

    private static long Key(int bx, int bz) => ((long)bx << 32) ^ (uint)bz;
}
