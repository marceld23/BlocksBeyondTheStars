// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Places the GIANT SHIP WRECKS drifting in space (#3614) — huge authored ships (<see cref="GiantShipRules"/>), abandoned long
/// ago (everyone got away), that nobody can build. Pure and seed-stable, run by the server right after the derelict stations
/// (<see cref="DerelictPlacer"/>) at the very end of the galaxy build, with its own salt (<c>"giantwreck:"</c>), so no body
/// the generator made moves and a save from before the feature simply gains its wrecks at its next start. The rules:
/// <list type="bullet">
/// <item>Only the procedural systems of the FIXED prefix are candidates (<c>sys0 … sys(fixedCount-1)</c>): never the story's
/// finale system, never a grown system.</item>
/// <item>The start system always holds exactly one (body <c>derelict:giant-&lt;sys&gt;-0</c>) unless the option is Off.</item>
/// <item>Every other fixed system rolls <c>Frequency.Probability() × </c><see cref="GiantShipRules.SpaceOddsScale"/> (Normal:
/// about one system in seventeen) and then holds one (<see cref="GiantShipRules.MaxPerSystem"/> at most).</item>
/// <item>A giant wreck is a <see cref="CelestialKind.SpaceStation"/> body whose id carries the derelict prefix
/// (<see cref="GiantShipRules.IdPrefix"/>), so every derelict rule — no air, nobody home, no trade, falling out is a
/// spacewalk — holds for it; a coined, language-neutral ship name (the localized kind "Giant ship wreck" stands beside it in
/// every UI); a star-map spot beside a seeded planet, moon or asteroid field, clear of the other bodies.</item>
/// </list>
/// The seed a wreck is ruined with (<see cref="BuildSeed"/>) is a pure function of the world seed and the id too. Which ship it
/// is (<see cref="PickDesign"/>) is a function of the seed, the id AND the designs it may still choose from: since #3764 a
/// fresh pick avoids the designs the world's other wrecks already are (<see cref="Untaken"/>), so an unbuilt wreck's ship
/// depends on which wrecks were built — and pinned — before it. The server pins the design the first time it builds the
/// wreck; from then on it never changes.
/// </summary>
public static class GiantWreckPlacer
{
    /// <summary>The id prefix of every giant wreck body: <see cref="GiantShipRules.IdPrefix"/>.</summary>
    public const string IdPrefix = GiantShipRules.IdPrefix;

    /// <summary>System-disc distance from the body a giant wreck drifts beside (further out than a derelict's 500).</summary>
    private const float DriftOrbit = 700f;

    /// <summary>Clear space a giant wreck's map spot keeps from every planet, moon, asteroid field and station body.</summary>
    private const float Clearance = 300f;

    /// <summary>The body id of a system's <paramref name="index"/>-th giant wreck: <c>derelict:giant-&lt;sys&gt;-&lt;n&gt;</c>.</summary>
    public static string BodyId(string systemId, int index) => IdPrefix + systemId + "-" + index;

    /// <summary>
    /// Adds the giant wreck bodies to the fixed systems. Returns how many were placed. Idempotent: a system that already
    /// holds a giant wreck is left alone.
    /// </summary>
    /// <param name="systems">The galaxy's systems (bodies are appended in place).</param>
    /// <param name="seed">The world seed.</param>
    /// <param name="frequency">The world option <see cref="WorldDescription.GiantWrecks"/>; Off places none at all.</param>
    /// <param name="fixedCount">How many procedural systems the galaxy started with (<c>WorldDescription.StarSystemCount</c>).</param>
    /// <param name="startSystemId">The system of the start body — it always holds one; null = no guarantee.</param>
    /// <param name="accept">Whether a wreck with this id may be placed (the server: a design exists for it, or it is pinned
    /// already); null accepts every one. A refused one is simply not added.</param>
    public static int Place(IReadOnlyList<StarSystem> systems, long seed, Frequency frequency, int fixedCount, string? startSystemId,
        Func<string, bool>? accept = null)
    {
        if (frequency == Frequency.Off)
        {
            return 0;
        }

        double odds = Math.Min(0.9, frequency.Probability() * GiantShipRules.SpaceOddsScale);
        int placed = 0;
        foreach (var system in systems)
        {
            int index = FixedIndex(system.Id);
            if (index < 0 || index >= fixedCount || system.Bodies.Count == 0 || HasGiantWreck(system))
            {
                continue;
            }

            int count = system.Id == startSystemId ? 1 : CountFor(seed, index, odds); // the start system: the guaranteed one
            for (int k = 0; k < count; k++)
            {
                string id = BodyId(system.Id, k);
                if (accept is not null && !accept(id))
                {
                    continue;
                }

                var (x, z, near) = PositionIn(system, seed, index, k);
                system.Bodies.Add(new CelestialBody
                {
                    Id = id,
                    Name = NameFor(seed, id),
                    Kind = CelestialKind.SpaceStation,
                    SystemId = system.Id,
                    SystemX = x,
                    SystemZ = z,
                    ParentId = near, // the body it drifts beside — the sky aboard (like a derelict's)
                });
                placed++;
            }
        }

        return placed;
    }

    /// <summary>A coined, language-neutral ship name ("Talvenor") — the localized kind stands beside it in every UI.</summary>
    public static string NameFor(long seed, string bodyId)
    {
        var rng = new DeterministicRandom(WorldGenerator.StableHash("giantwreck:name:" + bodyId) ^ seed);
        return NameGenerator.Ship(rng);
    }

    /// <summary>The seed a giant wreck is ruined and decayed with — a pure function of the world seed and the id.</summary>
    public static long BuildSeed(long worldSeed, string bodyId)
        => unchecked((long)StableHashing.Mixed("giantwreck:build:" + bodyId, worldSeed));

    /// <summary>
    /// The ship a giant wreck is: a weighted pick (the templates' <see cref="StructureTemplate.Weight"/>, at least 1 each) by a
    /// hash of the world seed and the id, over <paramref name="candidates"/> in key order (so the order the content lists them
    /// in never matters). Null when there is none.
    /// </summary>
    public static StructureTemplate? PickDesign(IReadOnlyList<StructureTemplate> candidates, long seed, string bodyId)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var sorted = new List<StructureTemplate>(candidates);
        sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        int total = 0;
        foreach (var t in sorted)
        {
            total += Math.Max(1, t.Weight);
        }

        int at = (int)(StableHashing.Unit("giantwreck:design:" + bodyId, seed) * total);
        foreach (var t in sorted)
        {
            at -= Math.Max(1, t.Weight);
            if (at < 0)
            {
                return t;
            }
        }

        return sorted[sorted.Count - 1];
    }

    /// <summary>
    /// #3764 (Justus: "in every system I have a wreck I already know from the first one"): the ships a wreck may still be
    /// when <paramref name="taken"/> names the designs other wrecks of the world already are — every candidate not taken,
    /// or all of them once none is left. Each wreck used to pick on its own, so two wrecks of one world were the same ship
    /// about one time in four; with this the weights only decide among the free designs.
    /// </summary>
    public static IReadOnlyList<StructureTemplate> Untaken(IReadOnlyList<StructureTemplate> candidates, ICollection<string>? taken)
    {
        if (taken is null || taken.Count == 0)
        {
            return candidates;
        }

        var free = new List<StructureTemplate>();
        foreach (var t in candidates)
        {
            if (!taken.Contains(t.Key))
            {
                free.Add(t);
            }
        }

        return free.Count > 0 ? free : candidates;
    }

    /// <summary>How many giant wrecks a fixed system other than the start system rolls: none most of the time, then one
    /// (up to <see cref="GiantShipRules.MaxPerSystem"/>).</summary>
    private static int CountFor(long seed, int index, double odds)
    {
        if (Hash01(seed, "roll", index) >= odds)
        {
            return 0;
        }

        return 1 + (int)(Hash01(seed, "count", index) * GiantShipRules.MaxPerSystem) % GiantShipRules.MaxPerSystem;
    }

    private static bool HasGiantWreck(StarSystem system)
    {
        foreach (var b in system.Bodies)
        {
            if (GiantShipRules.IsGiantWreckId(b.Id))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The map spot: beside a seeded planet / moon / asteroid field at <see cref="DriftOrbit"/> (widening twice), at a
    /// seeded angle, turned to the next of twelve angles while another body or station is too close. A system without a solid
    /// body parks it on a ring round the star. Cosmetic for the star map — in flight the wreck floats at a fixed spot of every
    /// orbit instance of its system, sized to its hull.</summary>
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
            float r = 1100f + k * 400f;
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
                float room = RoomAt(system, host.Id, x, z);
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

    /// <summary>The distance from a map spot to the nearest other planet, moon, asteroid field or station body.</summary>
    private static float RoomAt(StarSystem system, string hostId, float x, float z)
    {
        float room = float.MaxValue;
        foreach (var b in system.Bodies)
        {
            bool obstacle = b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
                or CelestialKind.SpaceStation;
            if (!obstacle || b.Id == hostId)
            {
                continue;
            }

            float dx = x - b.SystemX, dz = z - b.SystemZ;
            room = Math.Min(room, (float)Math.Sqrt(dx * dx + dz * dz));
        }

        return room;
    }

    private static int FixedIndex(string systemId)
        => systemId.Length > 3 && systemId.StartsWith("sys", StringComparison.Ordinal)
           && int.TryParse(systemId.Substring(3), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int i)
            ? i
            : -1;

    /// <summary>A uniform roll in [0, 1) per (salt, system index) — its own salt, never the generator's or the derelicts'.</summary>
    private static double Hash01(long seed, string salt, int index)
        => StableHashing.Unit("giantwreck:" + salt + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), seed);
}
