// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The start-system guarantees the server applies right after it picks the start body: the start planet's size floor,
/// rings and proper name, the start system's station and its jump ores. Each is deterministic from the seed and the body
/// ids alone, so every load re-derives the same result.
/// </summary>
public sealed partial class UniverseGenerator
{
    /// <summary>Start-planet size floor (#2691, generation 26): a seeded size draws from the whole planet band, and a
    /// small start world would thin out the settlements, structures and fauna a new player meets first. When the start
    /// planet's natural size (archetype bias included) is below <see cref="WorldConstants.StartPlanetMinCircumference"/>,
    /// its size bias is set so it lands on <see cref="WorldConstants.StartPlanetCircumferenceFor"/> — a seeded draw from
    /// the upper part of the band, so start worlds still differ. A twin start planet takes its twin along (#549). Called by
    /// the server right after it picks the start body, like <see cref="EnsureStartPlanetRings"/>; the bias is not
    /// persisted, every load re-derives it. A no-op below generation 26, for anything that is not a planet-class body,
    /// and for a start planet that is roomy already.</summary>
    public static void EnsureStartPlanetSize(StarSystem? system, CelestialBody start, long seed, WorldDescription description)
    {
        int generation = description.TerrainGeneration;
        var cls = WorldConstants.SizeClassFor(start.Kind, start.PlanetType ?? string.Empty);
        if (start.Kind != CelestialKind.Planet || cls != WorldConstants.WorldSizeClass.Planet
            || !WorldConstants.HasSeededSizes(generation)
            || WorldConstants.CircumferenceFor(start.Id, cls, start.SizeBias, seed, generation) >= WorldConstants.StartPlanetMinCircumference)
        {
            return;
        }

        int target = WorldConstants.StartPlanetCircumferenceFor(start.Id, seed);
        start.SizeBias = WorldConstants.BiasToward(start.Id, cls, target, seed, generation);
        if (system is null || SystemArchetypes.For(seed, system.Id, description) != SystemArchetype.TwinWorlds)
        {
            return;
        }

        var twins = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
        if (twins.Count == 2)
        {
            var other = ReferenceEquals(twins[0], start) ? twins[1] : twins[0];
            int size = WorldConstants.CircumferenceFor(start.Id, cls, start.SizeBias, seed, generation);
            other.SizeBias = WorldConstants.BiasToward(other.Id, WorldConstants.WorldSizeClass.Planet, size, seed, generation);
        }
    }

    /// <summary>Start-planet ring guarantee (#596): the world you spawn on always carries rings — the
    /// band across the sky is the feature's first impression, and playtesting showed a ring-less start
    /// reads as "the feature doesn't exist". Called by the server right after it picks the start body
    /// (only the server knows the pick — type overrides and retype fallbacks decide it at load time).
    /// Deterministic from the body id alone, so every restart re-derives the same ring; a no-op for
    /// planets that already rolled rings naturally, and for anything that isn't a planet.</summary>
    public static void EnsureStartPlanetRings(CelestialBody start)
    {
        if (start.Kind != CelestialKind.Planet || start.RingSeed != 0)
        {
            return;
        }

        int h = 17;
        foreach (char c in start.Id)
        {
            h = h * 31 + c;
        }

        start.RingSeed = 1 + (h & 0x7fffffff) % 999_999;
    }

    /// <summary>#1924 ("none of my worlds had a space station"): the start system always has a REAL station — on the map,
    /// on the radar, a mission and trade stop for VEGA's "dock at a station" lesson. The per-system roll leaves ~60 % of
    /// systems without one, and the old home guarantees only covered <c>sys0</c>, while the start planet is the first
    /// planet of the start type anywhere. A system that rolled a station is left alone; otherwise one hangs over the
    /// start planet, named after it, at an angle hashed from the system id alone — so every restart re-derives the same
    /// station and no other generator draw moves. Returns the added station, or null.</summary>
    public static CelestialBody? EnsureStartSystemStation(StarSystem system, CelestialBody start)
    {
        if (start.Kind != CelestialKind.Planet || start.SystemId != system.Id
            || system.Bodies.Any(b => b.Kind == CelestialKind.SpaceStation))
        {
            return null;
        }

        int h = 23;
        foreach (char c in system.Id)
        {
            h = h * 31 + c;
        }

        float ang = (float)(new DeterministicRandom(h * 2654435761L + 1924).NextDouble() * Tau);
        float sx = start.SystemX + StationOrbit * System.MathF.Cos(ang);
        float sz = start.SystemZ + StationOrbit * System.MathF.Sin(ang);
        (sx, sz) = SeparateFromBodies(system, sx, sz);
        var station = new CelestialBody
        {
            Id = $"{system.Id}-st", // the id a rolled first station would have had
            Name = $"{start.Name} Station",
            Kind = CelestialKind.SpaceStation,
            SystemId = system.Id,
            SystemX = sx,
            SystemZ = sz,
        };
        system.Bodies.Add(station);
        return station;
    }

    /// <summary>The ores reactor fuel is made of — the jump generator, the way out of the start system, needs that fuel.</summary>
    public static readonly string[] JumpOres = { "lead_ore", "uranium_ore" };

    /// <summary>#2988 (Lyxette 2026-10-28, "Blei? Woher, wenn ich es nicht abbauen kann?", generation 31): the start system
    /// carries the <see cref="JumpOres"/> on some body. While an ore grows on no body of the system (by its type's ore table
    /// at this generation), one other body of it — a planet, a moon or an asteroid field that is not visited, no landmark,
    /// guaranteed type, city world or gas giant, and carries no jump ore yet — takes the ordinary type of its own family
    /// (planet types for planets and moons, asteroid types for asteroid fields) carrying the most of the missing ores; the
    /// body whose type covers the most wins, the smallest seed hash breaks a tie. Called by the server right after it picks
    /// the start body and before it pins the body types (#468), for a world whose types are not pinned yet: the pins keep the
    /// result from then on. The retyped type is never the start type (the start body lacks that ore), so the start pick
    /// stays where it is. Returns the retyped bodies; a no-op below generation 31.</summary>
    public static List<CelestialBody> EnsureStartSystemJumpOres(StarSystem system, CelestialBody start, long seed,
        WorldDescription description, GameContent content)
    {
        var retyped = new List<CelestialBody>();
        int generation = description.TerrainGeneration;
        if (generation < WorldDescription.StartSystemJumpOresGeneration || start.SystemId != system.Id)
        {
            return retyped;
        }

        for (int round = 0; round < JumpOres.Length; round++)
        {
            var missing = JumpOres.Where(ore => !system.Bodies.Any(b => GrowsJumpOre(content, b.PlanetType, ore, generation))).ToList();
            if (missing.Count == 0 || JumpOreCandidate(system, start, seed, content, missing, generation) is not { } pick)
            {
                break;
            }

            pick.Body.PlanetType = pick.Type;
            retyped.Add(pick.Body);
        }

        return retyped;
    }

    private static bool GrowsJumpOre(GameContent content, string? type, string ore, int generation)
        => type is not null && content.GetPlanet(type) is { } p && p.Ores.Exists(o => o.Block == ore && o.GrowsAt(generation));

    /// <summary>The ordinary type of a family (spawnable, not exotic, no landmark or city world, open at this generation)
    /// carrying the most of <paramref name="missing"/> — the highest spawn weight, then the key, breaks a tie — with how many
    /// it carries; null when none carries any.</summary>
    private static (string Key, int Count)? JumpOreType(GameContent content, List<string> missing, int generation, bool asteroid)
        => content.Planets.Values
            .Where(p => p.SpawnWeight > 0 && !p.Exotic && !p.OncePerGalaxy && !p.GuaranteedOnce && p.CityWorld.Length == 0
                        && p.MinTerrainGeneration <= generation && WorldConstants.IsAsteroidType(p.Key) == asteroid)
            .Select(p => (Type: p, Count: missing.Count(ore => GrowsJumpOre(content, p.Key, ore, generation))))
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count).ThenByDescending(c => c.Type.SpawnWeight).ThenBy(c => c.Type.Key, System.StringComparer.Ordinal)
            .Select(c => ((string Key, int Count)?)(c.Type.Key, c.Count))
            .FirstOrDefault();

    /// <summary>The body of <paramref name="system"/> a missing jump ore goes to, with its new type — see
    /// <see cref="EnsureStartSystemJumpOres"/>. Null when no body may take one.</summary>
    private static (CelestialBody Body, string Type)? JumpOreCandidate(StarSystem system, CelestialBody start, long seed,
        GameContent content, List<string> missing, int generation)
    {
        (CelestialBody Body, string Type)? best = null;
        int bestCount = 0;
        ulong bestHash = ulong.MaxValue;
        for (int bi = 0; bi < system.Bodies.Count; bi++)
        {
            var body = system.Bodies[bi];
            var current = body.PlanetType is { } t ? content.GetPlanet(t) : null;
            if (body.Kind is not (CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField)
                || ReferenceEquals(body, start) || body.Status == GenerationStatus.Visited
                || current is null || current.OncePerGalaxy || current.GuaranteedOnce || current.SpawnWeight == 0 || current.CityWorld.Length > 0
                || JumpOres.Any(ore => GrowsJumpOre(content, current.Key, ore, generation))
                || JumpOreType(content, missing, generation, WorldConstants.IsAsteroidType(current.Key)) is not { } type)
            {
                continue; // a landmark, a guaranteed type, a city world, the gas giant, a jump-ore carrier — or no type fits
            }

            ulong h = Noise.Hash(seed ^ 0x2988, bi, 0, (int)(WorldGenerator.StableHash(system.Id) & 0x7FFFFFFF));
            if (type.Count > bestCount || (type.Count == bestCount && h < bestHash))
            {
                bestCount = type.Count;
                bestHash = h;
                best = (body, type.Key);
            }
        }

        return best;
    }

    /// <summary>Start-planet proper name (#678): the world you spawn on is a landmark — it deserves a
    /// real name, not "Tharion II". Called by the server right after it picks (and possibly retypes)
    /// the start body, mirroring <see cref="EnsureStartPlanetRings"/>. Deterministic from the body id
    /// alone, so every restart re-derives the same name; a no-op for planets that are already
    /// proper-named (rings/giant/twins/Hub capital) and for anything that isn't a planet. Lettered
    /// moons and an attributive station follow the rename.</summary>
    public static void EnsureStartPlanetProperName(StarSystem system, CelestialBody start)
    {
        if (start.Kind != CelestialKind.Planet
            || !start.Name.StartsWith(system.Name + " ", System.StringComparison.Ordinal))
        {
            return; // designations are always "<system> <numeral/letter>"; anything else is already proper
        }

        int h = 17;
        foreach (char c in start.Id)
        {
            h = h * 31 + c;
        }

        var rng = new DeterministicRandom(h * 2654435761L + 97);
        string name = NameGenerator.PlanetProper(rng, start.PlanetType);
        while (system.Bodies.Any(b => b.Name == name))
        {
            name = NameGenerator.PlanetProper(rng, start.PlanetType); // in-system clashes only; galaxy-wide ones are harmless
        }

        RenameWithMoons(system, start, name);
    }
}
