// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Places the derelict stations of a galaxy (#2632) — broken old boxy stations nobody lives on any more. Pure and
/// seed-stable, run by the server at the very END of the galaxy build (after the wormholes, the debris fields and the
/// start system's guaranteed station), so the generator's layout, every regression pin and the start station stay
/// exactly as they were; a save from before the feature simply gains its derelicts. The rules:
/// <list type="bullet">
/// <item>Only the procedural systems of the FIXED prefix are candidates (<c>sys0 … sys(fixedCount-1)</c>): the story's
/// finale system never holds one, and a growing galaxy never gains or moves one.</item>
/// <item>The start system always holds at least one (Justus: "at least once in a new world") unless the option is Off.</item>
/// <item>Any other system holds one RARELY — the option's probability × <see cref="OddsScale"/> — and then one to three.</item>
/// <item>A derelict is a <see cref="CelestialKind.SpaceStation"/> body (it is boarded like one) with its own id prefix
/// <see cref="IdPrefix"/>, so every trading-station rule can tell it apart (<see cref="IsDerelictId"/>). The galaxy's
/// one story derelict, "The Long Quiet" (<c>derelict:longquiet</c>), shares the prefix but is not placed here.</item>
/// </list>
/// Every roll uses its own salt (<c>"derelict:"</c>), never the generator's system stream.
/// </summary>
public static class DerelictPlacer
{
    /// <summary>The id prefix of every derelict station body (and of its interior world, <c>station:derelict:…</c>).</summary>
    public const string IdPrefix = "derelict:";

    /// <summary>The per-system odds are the option's probability times this — Normal (0.4) gives a derelict in about one
    /// system of eight, Frequent (0.75) in one of four or five. "Rarely" by decision (maintainer, 2026-10-15).</summary>
    public const double OddsScale = 0.3;

    /// <summary>The most derelicts one system ever holds.</summary>
    public const int MaxPerSystem = 3;

    /// <summary>System-disc distance from the body a derelict drifts beside (the station orbit of the generator).</summary>
    private const float DriftOrbit = 500f;

    /// <summary>Clear space a derelict's map position keeps from every planet, moon, asteroid and station body.</summary>
    private const float Clearance = 300f;

    /// <summary>True for a derelict station's id (a body id or a structure id) — the generated ones and The Long Quiet.</summary>
    public static bool IsDerelictId(string? id) => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

    /// <summary>The body id of a system's <paramref name="index"/>-th derelict.</summary>
    public static string BodyId(string systemId, int index) => IdPrefix + systemId + "-" + index;

    /// <summary>Adds the derelict bodies to the fixed systems. Returns how many were placed. Idempotent: a system that
    /// already holds derelicts is left alone (a second pass over the same galaxy places nothing).</summary>
    /// <param name="systems">The galaxy's systems (bodies are appended in place).</param>
    /// <param name="seed">The world seed.</param>
    /// <param name="frequency">The world option <see cref="WorldDescription.Derelicts"/>; Off places none at all.</param>
    /// <param name="fixedCount">How many procedural systems the galaxy started with (<c>WorldDescription.StarSystemCount</c>).</param>
    /// <param name="startSystemId">The system of the start body — it always holds at least one; null = no guarantee.</param>
    public static int Place(IReadOnlyList<StarSystem> systems, long seed, Frequency frequency, int fixedCount, string? startSystemId)
    {
        if (frequency == Frequency.Off)
        {
            return 0;
        }

        double odds = Math.Min(0.9, frequency.Probability() * OddsScale);
        int placed = 0;
        foreach (var system in systems)
        {
            int index = FixedIndex(system.Id);
            if (index < 0 || index >= fixedCount || system.Bodies.Count == 0 || HasDerelict(system))
            {
                continue;
            }

            int count = CountFor(seed, index, odds);
            if (system.Id == startSystemId)
            {
                count = Math.Max(1, count); // the guaranteed one
            }

            for (int k = 0; k < count; k++)
            {
                string id = BodyId(system.Id, k);
                var (x, z, near) = PositionIn(system, seed, index, k);
                system.Bodies.Add(new CelestialBody
                {
                    Id = id,
                    Name = NameFor(seed, id),
                    Kind = CelestialKind.SpaceStation,
                    SystemId = system.Id,
                    SystemX = x,
                    SystemZ = z,
                    // The body it drifts beside, for the sky aboard (#1856) — only the Moon pass of the client's space view
                    // reads ParentId, so a station carrying one is inert there (like a player station's).
                    ParentId = near,
                });
                placed++;
            }
        }

        return placed;
    }

    /// <summary>How many derelicts a fixed system rolls (0 most of the time; then 1, sometimes 2, rarely 3).</summary>
    private static int CountFor(long seed, int index, double odds)
    {
        if (Hash01(seed, "roll", index) >= odds)
        {
            return 0;
        }

        double extra = Hash01(seed, "count", index);
        return extra < 0.07 ? MaxPerSystem : extra < 0.25 ? 2 : 1;
    }

    /// <summary>A coined proper name with an old station designation ("Kiravon IV") — language-neutral, the localized
    /// kind ("Derelict station") stands beside it in every UI.</summary>
    public static string NameFor(long seed, string bodyId)
    {
        var rng = new DeterministicRandom(WorldGenerator.StableHash("derelict:name:" + bodyId) ^ seed);
        return NameGenerator.Star(rng) + " " + NameGenerator.Roman(rng.Range(2, 9));
    }

    private static bool HasDerelict(StarSystem system)
    {
        foreach (var b in system.Bodies)
        {
            if (IsDerelictId(b.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The map spot: beside a seeded planet / moon / asteroid body at the station orbit, at a seeded angle,
    /// turned to the next of twelve angles while another body or station is too close. A system without a solid body
    /// parks it on a ring around the star. Cosmetic for the star map — in flight the derelict floats at a fixed spot
    /// of every orbit instance of its system, like the NPC stations.</summary>
    private static (float X, float Z, string Near) PositionIn(StarSystem system, long seed, int index, int k)
    {
        var solids = new List<CelestialBody>();
        foreach (var b in system.Bodies)
        {
            if (b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField)
            {
                solids.Add(b);
            }
        }

        double angle0 = Hash01(seed, "angle:" + k, index) * Math.PI * 2.0;
        if (solids.Count == 0)
        {
            float r = 900f + k * 400f;
            return ((float)(Math.Cos(angle0) * r), (float)(Math.Sin(angle0) * r), string.Empty);
        }

        solids.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); // body order is not a contract
        var host = solids[(int)(Hash01(seed, "host:" + k, index) * solids.Count) % solids.Count];
        float bestX = host.SystemX, bestZ = host.SystemZ, bestRoom = -1f;
        foreach (float radius in new[] { DriftOrbit, DriftOrbit * 1.6f, DriftOrbit * 2.4f })
        {
            for (int step = 0; step < 12; step++)
            {
                double angle = angle0 + step * (Math.PI * 2.0 / 12.0);
                float x = host.SystemX + (float)(Math.Cos(angle) * radius);
                float z = host.SystemZ + (float)(Math.Sin(angle) * radius);
                float room = float.MaxValue;
                foreach (var b in system.Bodies)
                {
                    bool obstacle = b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
                        or CelestialKind.SpaceStation;
                    if (!obstacle || b.Id == host.Id)
                    {
                        continue;
                    }

                    float dx = x - b.SystemX, dz = z - b.SystemZ;
                    room = Math.Min(room, (float)Math.Sqrt(dx * dx + dz * dz));
                }

                if (room >= Clearance)
                {
                    return (x, z, host.Id);
                }

                if (room > bestRoom)
                {
                    bestRoom = room;
                    bestX = x;
                    bestZ = z;
                }
            }
        }

        return (bestX, bestZ, host.Id);
    }

    private static int FixedIndex(string systemId)
        => systemId.Length > 3 && systemId.StartsWith("sys", StringComparison.Ordinal)
           && int.TryParse(systemId.Substring(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int i)
            ? i
            : -1;

    private static double Hash01(long seed, string salt, int index)
    {
        // SplitMix64's finalizer over (salt hash, seed): a typed small seed ("42") must roll as freely as a 64-bit one —
        // a plain XOR left the seed in the low bits only, and seeds 1–200 never placed a single extra derelict.
        unchecked
        {
            ulong z = (ulong)WorldGenerator.StableHash("derelict:" + salt + ":" + index) ^ ((ulong)seed * 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }
    }
}
