// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Places the debris fields of a galaxy (#2353) — pure and seed-stable, run AFTER <see cref="UniverseGenerator"/>
/// and after <see cref="WormholePlacer"/>, so the galaxy layout, its regression pin and every wormhole end are
/// untouched. Like the wormholes:
/// <list type="bullet">
/// <item>Only the procedural systems of the FIXED prefix are candidates (<c>sys0 … sys(fixedCount-1)</c>): the story's
/// finale system is never one, and a growing galaxy never gains or moves a field.</item>
/// <item>At most one field per system, as a <see cref="CelestialKind.DebrisField"/> body with id <c>&lt;sys&gt;-d</c>.</item>
/// <item>The odds come from the world option (<see cref="Frequency"/>) times the system archetype's multiplier from
/// <c>data/space_salvage.json</c> — lawless and empty space is littered, patrolled hub space mostly cleaned up.</item>
/// <item>The field sits between the orbits, clear of every planet, moon and asteroid body.</item>
/// </list>
/// Every roll uses its own salt (<c>"debrisfield:"</c>), never the generator's system stream. The theme of a field is
/// rolled the same way (<see cref="ThemeFor"/>), so the server can derive it without storing anything.
/// </summary>
public static class DebrisFieldPlacer
{
    /// <summary>Clearance (system-disc units) a field keeps from a planet / moon / asteroid body. Wider than the
    /// generator's own station berth, so the fragments never clip a rendered planet in the compact flight view.</summary>
    public const float BodyClearance = 220f;

    /// <summary>Adds the debris-field bodies to the fixed systems that roll one. Returns how many were placed.</summary>
    /// <param name="archetypeOf">The archetype name of a system id (the server's <c>SystemArchetypeOf(...).ToString()</c>),
    /// for the odds multiplier; null = every system counts as Standard.</param>
    public static int Place(IReadOnlyList<StarSystem> systems, long seed, Frequency frequency, int fixedCount,
        SpaceSalvageDefinition settings, Func<string, string>? archetypeOf = null)
    {
        double odds = frequency.Probability();
        if (odds <= 0.0)
        {
            return 0;
        }

        int placed = 0;
        foreach (var system in systems)
        {
            int index = FixedIndex(system.Id);
            if (index < 0 || index >= fixedCount || system.Bodies.Count == 0)
            {
                continue;
            }

            if (HasField(system))
            {
                continue; // already placed (a second pass over the same galaxy, e.g. a test re-running the placer)
            }

            double chance = odds;
            string archetype = archetypeOf?.Invoke(system.Id) ?? "Standard";
            if (settings.Fields.ArchetypeOdds.TryGetValue(archetype, out double mul))
            {
                chance *= mul;
            }

            if (Hash01(seed, "roll", index) >= Math.Min(0.95, chance))
            {
                continue;
            }

            system.Bodies.Add(new CelestialBody
            {
                Id = system.Id + "-d",
                Name = NameGenerator.Ship(new DeterministicRandom(WorldGenerator.StableHash("debrisfield:name:" + system.Id) ^ seed)),
                Kind = CelestialKind.DebrisField,
                SystemId = system.Id,
                SystemX = 0f,
                SystemZ = 0f,
            });
            var body = system.Bodies[system.Bodies.Count - 1];
            var (x, z) = PositionIn(system, seed, index);
            body.SystemX = x;
            body.SystemZ = z;
            placed++;
        }

        return placed;
    }

    /// <summary>Whether a system may hold a debris field at all: only the procedural <c>sys*</c> systems.</summary>
    public static bool MayHoldDebrisField(string systemId) => WormholePlacer.MayHoldWormhole(systemId);

    /// <summary>The theme key of a field (seed-pure, from its body id): the theme weights, tilted by the system's
    /// archetype multipliers. Empty when the definition has no themes.</summary>
    public static string ThemeFor(long seed, string bodyId, string archetype, SpaceSalvageDefinition settings)
    {
        double total = 0.0;
        var weights = new List<(string Key, double Weight)>();
        foreach (var kv in settings.Themes)
        {
            double w = kv.Value.Weight;
            if (kv.Value.ArchetypeWeights.TryGetValue(archetype, out double mul))
            {
                w *= mul;
            }

            if (w > 0.0)
            {
                weights.Add((kv.Key, w));
                total += w;
            }
        }

        if (weights.Count == 0)
        {
            return string.Empty;
        }

        weights.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); // dictionary order is not a contract
        double roll = Hash01(seed, "theme:" + bodyId, 0) * total;
        foreach (var (key, weight) in weights)
        {
            roll -= weight;
            if (roll < 0.0)
            {
                return key;
            }
        }

        return weights[weights.Count - 1].Key;
    }

    private static bool HasField(StarSystem system)
    {
        foreach (var b in system.Bodies)
        {
            if (b.Kind == CelestialKind.DebrisField)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A spot between the orbits: a seeded angle at a seeded radius inside the outermost body's orbit, moved
    /// to the next of twelve candidate angles while a planet, moon or asteroid body is too close; when every angle is
    /// crowded, the one with the most room wins.</summary>
    private static (float X, float Z) PositionIn(StarSystem system, long seed, int index)
    {
        float outer = 0f;
        foreach (var body in system.Bodies)
        {
            if (!IsSolid(body.Kind))
            {
                continue;
            }

            float r = (float)Math.Sqrt(body.SystemX * body.SystemX + body.SystemZ * body.SystemZ);
            outer = Math.Max(outer, r);
        }

        // A one-body system has no "between": park the field at a fixed ring well outside that body.
        float radius = outer > BodyClearance * 2f
            ? outer * (0.35f + 0.55f * (float)Hash01(seed, "radius", index))
            : outer + BodyClearance * 1.5f;
        double angle0 = Hash01(seed, "angle", index) * Math.PI * 2.0;

        float bestX = 0f, bestZ = 0f, bestRoom = -1f;
        for (int k = 0; k < 12; k++)
        {
            double angle = angle0 + k * (Math.PI * 2.0 / 12.0);
            float x = (float)(Math.Cos(angle) * radius);
            float z = (float)(Math.Sin(angle) * radius);
            float room = float.MaxValue;
            foreach (var body in system.Bodies)
            {
                if (!IsSolid(body.Kind))
                {
                    continue;
                }

                float dx = x - body.SystemX, dz = z - body.SystemZ;
                room = Math.Min(room, (float)Math.Sqrt(dx * dx + dz * dz));
            }

            if (room >= BodyClearance)
            {
                return (x, z);
            }

            if (room > bestRoom)
            {
                bestRoom = room;
                bestX = x;
                bestZ = z;
            }
        }

        return (bestX, bestZ);
    }

    private static bool IsSolid(CelestialKind kind)
        => kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField;

    private static int FixedIndex(string systemId)
        => systemId.Length > 3 && systemId.StartsWith("sys", StringComparison.Ordinal)
           && int.TryParse(systemId.Substring(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int i)
            ? i
            : -1;

    private static double Hash01(long seed, string salt, int index)
    {
        ulong h = unchecked((ulong)(WorldGenerator.StableHash("debrisfield:" + salt + ":" + index) ^ seed));
        h ^= h >> 29;
        return h % 1_000_003UL / 1_000_003.0;
    }
}
