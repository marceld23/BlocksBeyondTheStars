// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Places the wormholes of a galaxy (#2242) — pure and seed-stable, run AFTER <see cref="UniverseGenerator"/> so the
/// galaxy layout itself (and its regression pin) is untouched.
/// <list type="bullet">
/// <item>Only the procedural systems of the FIXED prefix are candidates (<c>sys0 … sys(fixedCount-1)</c>): the story's
/// finale system (<c>guardian_finale</c>) is never one, and a growing galaxy never moves a wormhole.</item>
/// <item>At most one end per system — no chains; ends come in pairs that point at each other — always two-way.</item>
/// <item>The two ends of a pair are far apart on the star map (<see cref="WormholeDefinition.MinMapDistance"/>).</item>
/// <item>Each end opens beyond the outermost orbit of its system, with a seeded angle, distance and height.</item>
/// </list>
/// Every roll uses its own salt (<c>"wormhole:"</c>), never the generator's system stream.
/// </summary>
public static class WormholePlacer
{
    /// <summary>The wormhole ends for a galaxy, in pairs (empty for <see cref="Frequency.Off"/>).</summary>
    public static List<Wormhole> Place(IReadOnlyList<StarSystem> systems, long seed, Frequency frequency, int fixedCount,
        WormholeDefinition settings)
    {
        var result = new List<Wormhole>();
        double perTwelve = frequency switch
        {
            Frequency.VeryRare => settings.VeryRarePairsPer12,
            Frequency.Rare => settings.RarePairsPer12,
            Frequency.Normal => settings.NormalPairsPer12,
            Frequency.Frequent => settings.FrequentPairsPer12,
            _ => 0.0,
        };
        if (perTwelve <= 0.0 || fixedCount < 2)
        {
            return result;
        }

        var candidates = systems
            .Select(s => (System: s, Index: FixedIndex(s.Id)))
            .Where(c => c.Index >= 0 && c.Index < fixedCount && c.System.Bodies.Count > 0)
            .OrderBy(c => Hash01(seed, "order", c.Index))
            .ThenBy(c => c.Index)
            .Select(c => c.System)
            .ToList();

        double expected = fixedCount * perTwelve / 12.0;
        int pairs = (int)Math.Floor(expected + Hash01(seed, "count", fixedCount));
        pairs = Math.Min(pairs, candidates.Count / 2);

        var free = new List<StarSystem>(candidates);
        for (int p = 0; p < pairs && free.Count >= 2; p++)
        {
            var a = free[0];
            free.RemoveAt(0);

            // The partner: a free system far enough away, the seed choosing among them; else the farthest one left.
            var far = free.Where(s => MapDistance(a, s) >= settings.MinMapDistance).ToList();
            var b = far.Count > 0
                ? far.OrderBy(s => Hash01(seed, "partner:" + a.Id, FixedIndex(s.Id))).First()
                : free.OrderByDescending(s => MapDistance(a, s)).First();
            free.Remove(b);

            var endA = EndIn(a, seed, settings);
            var endB = EndIn(b, seed, settings);
            endA.LinkedId = endB.Id;
            endB.LinkedId = endA.Id;
            result.Add(endA);
            result.Add(endB);
        }

        return result;
    }

    /// <summary>Whether a system may hold a wormhole end at all (#2242): only the procedural <c>sys*</c> systems. The
    /// story's finale system — and any other hand-made system — never does. The server checks this again at every
    /// transit, so even an edited save cannot open a way there.</summary>
    public static bool MayHoldWormhole(string systemId)
        => systemId.StartsWith("sys", StringComparison.Ordinal) && FixedIndex(systemId) >= 0;

    private static Wormhole EndIn(StarSystem system, long seed, WormholeDefinition settings)
    {
        float outer = 0f;
        foreach (var body in system.Bodies)
        {
            float r = (float)Math.Sqrt(body.SystemX * body.SystemX + body.SystemZ * body.SystemZ);
            outer = Math.Max(outer, r);
        }

        int index = FixedIndex(system.Id);
        double angle = Hash01(seed, "angle", index) * Math.PI * 2.0;
        float radius = outer + settings.EdgeMargin + (float)(Hash01(seed, "radius", index) * settings.EdgeSpread);
        float height = (float)((Hash01(seed, "height", index) - 0.5) * 160.0);
        return new Wormhole
        {
            Id = system.Id + "-wh",
            SystemId = system.Id,
            SystemX = (float)(Math.Cos(angle) * radius),
            SystemY = height,
            SystemZ = (float)(Math.Sin(angle) * radius),
        };
    }

    private static int FixedIndex(string systemId)
        => systemId.Length > 3 && systemId.StartsWith("sys", StringComparison.Ordinal)
           && int.TryParse(systemId.Substring(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int i)
            ? i
            : -1;

    private static float MapDistance(StarSystem a, StarSystem b)
    {
        float dx = a.MapX - b.MapX;
        float dy = a.MapY - b.MapY;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Hash01(long seed, string salt, int index)
    {
        ulong h = unchecked((ulong)(WorldGenerator.StableHash("wormhole:" + salt + ":" + index) ^ seed));
        h ^= h >> 29; // fold the seed's high bits in, so neighbouring seeds don't share low-bit patterns
        return h % 1_000_003UL / 1_000_003.0;
    }
}
