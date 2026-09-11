// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Deterministically derives a body's roster of <see cref="FloraSpecies"/> from the roster seed (the world
/// seed salted with the body, <see cref="WorldGenerator.RosterSeedFor"/>) + planet type — the flora
/// counterpart to <see cref="CreatureGenerator"/>. Each entry in the fixed archetype catalogue
/// (<see cref="FloraCatalog"/>) becomes a named, edible-or-toxic species for this world, so two worlds that
/// both grow the "bush" archetype name and classify it differently. Combined with the per-species colours
/// (<see cref="BlocksBeyondTheStars.Shared.World.FloraTints"/>, client-side from the same seed + body) and the
/// coined names, each world's plant life reads as its own flora. Same seed + body + planet → the same
/// roster, so nothing needs storing.
/// </summary>
public static class FloraGenerator
{
    /// <summary>This world's roster. <paramref name="terrainGeneration"/> is the world's
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.TerrainGeneration"/>: from
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.BiomeThemeRosterGeneration"/> the biome
    /// themes take part in the activation roll (#1715); older worlds keep the planet-theme-only roll — and
    /// with it every species they ever grew.</summary>
    public static IReadOnlyList<FloraSpecies> GenerateRoster(PlanetType planet, long worldSeed, int terrainGeneration = 0)
    {
        var list = new List<FloraSpecies>();
        if (planet.IsAirless || planet.FloraDensity <= 0)
        {
            return list; // airless (asteroids / airless moons+planets) + barren worlds grow nothing
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ 0x5EEDF10A;
        const long golden = unchecked((long)0x9E3779B97F4A7C15UL);

        // The world's flora theme biases WHICH forms grow: species whose climate tags match the theme are
        // common, off-theme species an occasional find — so a tropical world and a savanna world (both grass)
        // grow visibly different plant life. Coverage is enforced afterwards so no surface ever goes bare.
        //
        // #1715 (generation ≥ 4): the biome themes count too. A `varied` world is temperate with desert, swamp
        // and alpine biomes; under the planet-only roll its swamp drew from a pool thinned by the temperate
        // theme's 40 % — a wetland species that rolled inactive could never grow there, and PickWeight (which
        // does read the biome theme) cannot add back what was never activated. The union of preferred tags is
        // data-only, so the roster stays a pure function of (type, seed, generation).
        var planetTheme = FloraThemes.Resolve(planet.FloraTheme);
        FloraTag preferred = planetTheme.Preferred;
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.BiomeThemeRosterGeneration)
        {
            foreach (var biome in planet.Biomes)
            {
                if (!string.IsNullOrWhiteSpace(biome.FloraTheme))
                {
                    preferred |= FloraThemes.Resolve(biome.FloraTheme).Preferred;
                }
            }
        }

        // #1760 (generation ≥ 5): a strict planet theme (the flower fields) activates only its preferred tags.
        // No theme older than this wave is strict, so the roll below is unchanged for every existing world.
        bool strict = terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.AuthoredContentGeneration && planetTheme.Strict;

        int i = 0;
        foreach (var archetype in FloraCatalog.All)
        {
            // Farmed crops (#627) are not part of this world's plant life: they never grow wild, and above all
            // they must never take on a rolled species identity — a roster entry would let the toxic roll swap
            // their edible berries for toxic ones. The index still advances so the WILD species keep their
            // seed salt (and their "fl<i>" ids) no matter where a crop sits in the catalog.
            if (archetype.Cultivated)
            {
                i++;
                continue;
            }

            long s = unchecked(planetSeed ^ ((long)i * golden));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(new FloraSpecies
            {
                Id = "fl" + i,
                Name = NameGenerator.Flora(rng),
                BlockKey = archetype.Key,
                Toxic = rng.NextDouble() < 0.3,  // most flora is benign; a notable minority is toxic
                Aquatic = archetype.Aquatic,
                // A species of a later wave (#1756, MinGeneration) draws its roll like every other — the draw keeps
                // the rng stream identical — but stays inactive on a world whose generation predates it.
                Active = rng.NextDouble() < FloraThemes.ActivationChance(preferred, archetype.Tags, strict)
                    && archetype.MinGeneration <= terrainGeneration,
            });
            i++;
        }

        EnsureCoverage(list, planet, terrainGeneration, strict ? preferred : FloraTag.None);
        return list;
    }

    /// <summary>Force-activates the minimum flora so no part of the world goes bare: every land host surface
    /// keeps at least one active land species, and the seas keep at least one active aquatic species.</summary>
    private static void EnsureCoverage(List<FloraSpecies> roster, PlanetType planet, int terrainGeneration, FloraTag strictTags)
    {
        // Every host surface used by a land archetype must have an active species, or that surface grows nothing.
        // A hanging species (#1759) roots in a ceiling, never on a surface, so it neither needs cover nor gives it;
        // a species gated behind a later generation (#1756) must never be the pick that covers an older world; and
        // under a strict theme (#1760, strictTags ≠ None) only an on-theme species may be the pick — a bare patch
        // beats a bush on the flower planet.
        bool strict = strictTags != FloraTag.None;
        bool Eligible(string blockKey)
            => FloraCatalog.All.FirstOrDefault(s => s.Key == blockKey) is { } sp && !sp.Hanging && sp.MinGeneration <= terrainGeneration
               && (!strict || (sp.Tags & strictTags) != 0);

        var landHosts = new HashSet<string>();
        foreach (var sp in FloraCatalog.All)
        {
            if (sp.Cultivated || sp.Hanging)
            {
                continue; // a crop's host (a greenhouse bed / hydroponic tray) is no world surface — nothing to cover
            }

            if (!sp.Aquatic)
            {
                foreach (var h in sp.Hosts)
                {
                    landHosts.Add(h);
                }
            }
        }

        // A strict theme (#1760) covers only the surfaces this planet actually has: forcing a reed onto "mud" for
        // a world without mud would put an off-theme plant on the roster for nothing.
        if (strict)
        {
            var own = new HashSet<string> { planet.SurfaceBlock, planet.BeachBlock };
            foreach (var biome in planet.Biomes)
            {
                own.Add(biome.SurfaceBlock);
            }

            landHosts.IntersectWith(own);
        }

        foreach (var host in landHosts)
        {
            bool covered = roster.Any(r => r.Active && !r.Aquatic && HostsFor(r.BlockKey).Contains(host));
            if (!covered)
            {
                var pick = roster.FirstOrDefault(r => !r.Aquatic && Eligible(r.BlockKey) && HostsFor(r.BlockKey).Contains(host));
                if (pick != null)
                {
                    pick.Active = true;
                }
            }
        }

        // Keep the seas planted: at least one aquatic species active if any exist.
        if (roster.Any(r => r.Aquatic) && !roster.Any(r => r.Aquatic && r.Active))
        {
            roster.First(r => r.Aquatic).Active = true;
        }
    }

    private static IReadOnlyList<string> HostsFor(string blockKey)
        => FloraCatalog.All.FirstOrDefault(s => s.Key == blockKey)?.Hosts ?? System.Array.Empty<string>();
}
