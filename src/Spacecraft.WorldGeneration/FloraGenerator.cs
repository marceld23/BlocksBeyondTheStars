using System.Collections.Generic;
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
                Toxic = rng.NextDouble() < 0.3, // most flora is benign; a notable minority is toxic
                Aquatic = archetype.Aquatic,
            });
            i++;
        }

        return list;
    }
}
