// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3639: where each framework civilization lives — a PURE function of the seed over the galaxy's FIXED systems
/// (<c>sys0 … sys(N−1)</c>; frontier growth appends systems and so never moves a home). Per civilization, in file order:
/// the candidates (planets and moons of the scope's types and atmospheres, its conditions, its start-system rule, never a
/// type that keeps only its own structures, never a body an earlier civilization took), the number of homes rolled from the
/// scope's count and weights, then the candidates in the order of a hash of the civilization and the body, taking at most
/// <see cref="CivilizationScope.PerSystem"/> per system. The server pins the answer once (<c>WorldMetadata.CivilizationHomes</c>);
/// a later change to a civilization file or the content never moves a home of an existing world.
/// </summary>
public static class CivilizationHomes
{
    /// <summary>Civilization key → its home body ids (an empty list = no home in this galaxy), for every framework
    /// civilization the world's <paramref name="generation"/> stamps.</summary>
    public static Dictionary<string, List<string>> Pick(Galaxy galaxy, GameContent content, long seed, int fixedSystems, int generation,
        string? startSystemId)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var civ in content.Civilizations)
        {
            if (!civ.IsFramework || civ.MinTerrainGeneration > generation)
            {
                continue;
            }

            var candidates = Candidates(galaxy, content, civ, fixedSystems, startSystemId, taken)
                .OrderBy(b => StableHashing.Mixed("civ:" + civ.Key + ":home:" + b.Id, seed))
                .ThenBy(b => b.Id, StringComparer.Ordinal)
                .ToList();
            int want = RollCount(civ.Scope, seed, civ.Key);
            var homes = new List<string>();
            var perSystem = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var body in candidates)
            {
                if (homes.Count >= want)
                {
                    break;
                }

                perSystem.TryGetValue(body.SystemId, out int inSystem);
                if (civ.Scope.PerSystem > 0 && inSystem >= civ.Scope.PerSystem)
                {
                    continue;
                }

                perSystem[body.SystemId] = inSystem + 1;
                homes.Add(body.Id);
                taken.Add(body.Id);
            }

            result[civ.Key] = homes;
        }

        return result;
    }

    /// <summary>How many homes a civilization asks for: its count range, weighted by its count weights (equal without),
    /// rolled from a hash of the seed and the civilization.</summary>
    public static int RollCount(CivilizationScope scope, long seed, string civilization)
    {
        var (min, max) = CivilizationRules.RangeOf(scope.Count);
        min = Math.Max(0, min);
        max = Math.Min(CivilizationRules.MaxHomes, Math.Max(min, max));
        if (min == max)
        {
            return min;
        }

        var weights = scope.CountWeights is { Count: > 0 } w && w.Count == max - min + 1 ? w : null;
        double total = 0;
        for (int n = min; n <= max; n++)
        {
            total += weights is null ? 1 : Math.Max(0, weights[n - min]);
        }

        double roll = StableHashing.Unit("civ:" + civilization + ":count", seed) * total;
        for (int n = min; n <= max; n++)
        {
            roll -= weights is null ? 1 : Math.Max(0, weights[n - min]);
            if (roll < 0)
            {
                return n;
            }
        }

        return max;
    }

    /// <summary>The bodies a civilization may call home: planets and moons of the fixed systems that pass its scope.</summary>
    public static List<CelestialBody> Candidates(Galaxy galaxy, GameContent content, CivilizationDefinition civ, int fixedSystems,
        string? startSystemId, ISet<string>? taken = null)
    {
        var list = new List<CelestialBody>();
        int fixedCount = Math.Max(1, fixedSystems);
        foreach (var system in galaxy.Systems)
        {
            if (!StarSystemIds.IsStartRolled(system.Id, fixedCount) || !StartRuleHolds(civ.Scope.StartSystem, system.Id == startSystemId))
            {
                continue;
            }

            foreach (var body in system.Bodies)
            {
                if (taken?.Contains(body.Id) != true && Fits(body, content, civ.Scope))
                {
                    list.Add(body);
                }
            }
        }

        return list;
    }

    private static bool StartRuleHolds(string rule, bool isStart)
        => string.Equals(rule, CivilizationRules.StartNever, StringComparison.OrdinalIgnoreCase) ? !isStart
            : !string.Equals(rule, CivilizationRules.StartOnly, StringComparison.OrdinalIgnoreCase) || isStart;

    /// <summary>Whether a body passes a civilization's scope: kind, type, atmosphere, conditions.</summary>
    public static bool Fits(CelestialBody body, GameContent content, CivilizationScope scope)
    {
        if ((body.Kind != CelestialKind.Planet && body.Kind != CelestialKind.Moon) || string.IsNullOrEmpty(body.PlanetType)
            || content.GetPlanet(body.PlanetType!) is not { } planet || planet.RestrictStructures)
        {
            return false;
        }

        if (scope.PlanetTypes.Count > 0 && !scope.PlanetTypes.Contains(planet.Key, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (scope.Atmospheres.Count > 0 && !scope.Atmospheres.Contains(planet.Atmosphere, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string condition in scope.Conditions)
        {
            if (!ConditionHolds(condition, body, planet))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConditionHolds(string condition, CelestialBody body, PlanetType planet) => condition switch
    {
        CivilizationRules.ConditionNotAsteroid => !WorldConstants.IsAsteroidType(planet.Key),
        CivilizationRules.ConditionNotGas => !planet.IsGasWorld,
        CivilizationRules.ConditionNotVoid => !planet.Void,
        CivilizationRules.ConditionPlanet => body.Kind == CelestialKind.Planet,
        CivilizationRules.ConditionMoon => body.Kind == CelestialKind.Moon,
        _ => true,
    };
}
