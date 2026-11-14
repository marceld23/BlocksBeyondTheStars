// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3640: the civilizations' guarantees — a galaxy of terrain generation 43 or later whose ORIGINAL systems hold no planet of
/// a framework civilization's <see cref="CivilizationGuarantee.PlanetTypes"/> (the Grand Technicians need an ocean world)
/// gets one: the eligible planet with the smallest hash takes the first listed type, by the rules of the guaranteed types
/// (#2343: outside the start system, not the first breathable planet, not a type's first planet, never a once-per-galaxy
/// landmark, another guaranteed type or the gas giant). Runs after <see cref="ApplyGuaranteedTypes"/>; an older galaxy is
/// never touched, a grown galaxy's appended systems are never looked at.
/// </summary>
public sealed partial class UniverseGenerator
{
    /// <summary>The guaranteed planet types, then (generation 43+) the civilizations' guarantees.</summary>
    private void ApplyGuarantees(Galaxy galaxy, CelestialBody? firstBreathable, HashSet<CelestialBody>? kept)
    {
        ApplyGuaranteedTypes(galaxy, firstBreathable, kept);
        if (_desc.TerrainGeneration < WorldDescription.CivilizationsGeneration)
        {
            return;
        }

        foreach (var civ in _content.Civilizations)
        {
            if (civ.IsFramework && civ.Guarantee.PlanetTypes.Count > 0 && civ.MinTerrainGeneration <= _desc.TerrainGeneration)
            {
                GuaranteeOneOf(galaxy, civ.Key, civ.Guarantee.PlanetTypes, firstBreathable, kept);
            }
        }
    }

    private void GuaranteeOneOf(Galaxy galaxy, string civ, List<string> types, CelestialBody? firstBreathable, HashSet<CelestialBody>? kept)
    {
        int original = System.Math.Min(galaxy.Systems.Count, System.Math.Max(0, _desc.StarSystemCount));
        for (int si = 0; si < original; si++)
        {
            foreach (var body in galaxy.Systems[si].Bodies)
            {
                if ((body.Kind == CelestialKind.Planet || body.Kind == CelestialKind.Moon) && body.PlanetType is { } t && types.Contains(t))
                {
                    return; // the galaxy has one already
                }
            }
        }

        string type = types.Find(t => _content.GetPlanet(t) is not null) ?? string.Empty;
        if (type.Length == 0)
        {
            return;
        }

        CelestialBody? best = null;
        ulong bestHash = ulong.MaxValue;
        for (int si = 1; si < original; si++)
        {
            var system = galaxy.Systems[si];
            for (int bi = 0; bi < system.Bodies.Count; bi++)
            {
                var body = system.Bodies[bi];
                if (body.Kind != CelestialKind.Planet || ReferenceEquals(body, firstBreathable) || kept?.Contains(body) == true)
                {
                    continue;
                }

                var current = body.PlanetType is { } t ? _content.GetPlanet(t) : null;
                if (current is { OncePerGalaxy: true } || current is { GuaranteedOnce: true } || current is { SpawnWeight: 0 }
                    || current is { RestrictStructures: true })
                {
                    continue; // a landmark world, a guaranteed type, the gas giant or a world that keeps only its own
                }

                ulong h = Noise.Hash(_seed ^ 0x43C1F1, si, bi, (int)(WorldGenerator.StableHash("civ:" + civ) & 0x7FFFFFFF));
                if (h < bestHash)
                {
                    bestHash = h;
                    best = body;
                }
            }
        }

        if (best is not null)
        {
            best.PlanetType = type;
        }
    }
}
