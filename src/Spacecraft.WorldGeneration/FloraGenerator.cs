using System.Collections.Generic;
using System.Linq;
using Spacecraft.Shared.Definitions;

namespace Spacecraft.WorldGeneration;

/// <summary>
/// Deterministically derives a planet's roster of <see cref="FloraSpecies"/> from the world seed + planet —
/// the flora counterpart to <see cref="CreatureGenerator"/>. Each entry in the fixed archetype catalogue
/// (<see cref="FloraCatalog"/>) becomes a named, edible-or-toxic species for this world, so two worlds that
/// both grow the "bush" archetype name and classify it differently. Combined with the planet's uniform flora
/// hue (server-side <c>FloraColor</c>) and the coined names, each world's plant life reads as its own flora.
/// Same seed + planet → the same roster, so nothing needs storing.
/// </summary>
public static class FloraGenerator
{
    public static IReadOnlyList<FloraSpecies> GenerateRoster(PlanetType planet, long worldSeed)
    {
        var list = new List<FloraSpecies>();
        if (planet.FloraDensity <= 0)
        {
            return list; // barren worlds grow nothing to name
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ 0x5EEDF10A;
        const long golden = unchecked((long)0x9E3779B97F4A7C15UL);

        int i = 0;
        foreach (var archetype in FloraCatalog.All)
        {
            long s = unchecked(planetSeed ^ ((long)i * golden));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(new FloraSpecies
            {
                Id = "fl" + i,
                Name = NameGenerator.Flora(rng),
                BlockKey = archetype.Key,
                Toxic = rng.NextDouble() < 0.3,  // most flora is benign; a notable minority is toxic
                Aquatic = archetype.Aquatic,
                Active = rng.NextDouble() < 0.6, // only a subset of forms grows on any given world
            });
            i++;
        }

        EnsureCoverage(list);
        return list;
    }

    /// <summary>Force-activates the minimum flora so no part of the world goes bare: every land host surface
    /// keeps at least one active land species, and the seas keep at least one active aquatic species.</summary>
    private static void EnsureCoverage(List<FloraSpecies> roster)
    {
        // Every host surface used by a land archetype must have an active species, or that surface grows nothing.
        var landHosts = new HashSet<string>();
        foreach (var sp in FloraCatalog.All)
        {
            if (!sp.Aquatic)
            {
                foreach (var h in sp.Hosts)
                {
                    landHosts.Add(h);
                }
            }
        }

        foreach (var host in landHosts)
        {
            bool covered = roster.Any(r => r.Active && !r.Aquatic && HostsFor(r.BlockKey).Contains(host));
            if (!covered)
            {
                var pick = roster.FirstOrDefault(r => !r.Aquatic && HostsFor(r.BlockKey).Contains(host));
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
