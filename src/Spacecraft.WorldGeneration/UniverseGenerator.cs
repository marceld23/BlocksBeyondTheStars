using Spacecraft.Shared.Content;
using Spacecraft.Shared.World;

namespace Spacecraft.WorldGeneration;

/// <summary>
/// Deterministically builds a <see cref="Galaxy"/> from a world seed and a
/// <see cref="WorldDescription"/>. Because it is a pure function of (seed, description),
/// the layout is stable across runs — satisfying "generated once, then stable" without
/// storing the whole universe (only generation status and player changes are persisted).
/// </summary>
public sealed class UniverseGenerator
{
    private static readonly string[] NamePrefixes =
        { "Vey", "Kor", "Ark", "Nyx", "Tor", "Zel", "Hal", "Lum", "Dra", "Cas", "Or", "Pyr", "Sol", "Vor", "Eri" };

    private static readonly string[] NameSuffixes =
        { "ra", "on", "is", "ar", "ex", "ia", "us", "or", "an", "yn", "el", "os" };

    private readonly long _seed;
    private readonly WorldDescription _desc;
    private readonly List<(string key, int weight)> _planetWeights;

    public UniverseGenerator(long seed, WorldDescription description, GameContent content)
    {
        _seed = seed;
        _desc = description;
        _planetWeights = BuildPlanetWeights(description, content);
    }

    private static List<(string, int)> BuildPlanetWeights(WorldDescription desc, GameContent content)
    {
        var list = new List<(string, int)>();
        if (desc.PlanetTypeFrequencies.Count > 0)
        {
            foreach (var kv in desc.PlanetTypeFrequencies)
            {
                if (content.GetPlanet(kv.Key) is not null && kv.Value.Weight() > 0)
                {
                    list.Add((kv.Key, kv.Value.Weight()));
                }
            }
        }

        if (list.Count == 0)
        {
            // Default: every selectable planet type at Normal weight (special bodies such as
            // landable asteroids are excluded so they never appear as ordinary system planets).
            foreach (var key in content.Planets.Keys)
            {
                if (content.GetPlanet(key) is { Selectable: true })
                {
                    list.Add((key, Frequency.Normal.Weight()));
                }
            }
        }

        return list;
    }

    public Galaxy Generate()
    {
        var galaxy = new Galaxy();
        int systems = System.Math.Max(0, _desc.StarSystemCount);

        for (int i = 0; i < systems; i++)
        {
            var rng = new DeterministicRandom((long)Noise.Hash(_seed, i, 1, 1));
            var system = new StarSystem
            {
                Id = $"sys{i}",
                Name = MakeName(rng),
                MapX = rng.NextFloat() * 1000f,
                MapY = rng.NextFloat() * 1000f,
            };

            int planets = rng.Range(_desc.PlanetsPerSystemMin, _desc.PlanetsPerSystemMax);
            for (int p = 0; p < planets; p++)
            {
                var planet = new CelestialBody
                {
                    Id = $"{system.Id}-p{p}",
                    Name = $"{system.Name} {p + 1}",
                    Kind = CelestialKind.Planet,
                    PlanetType = PickPlanetType(rng),
                    SystemId = system.Id,
                };
                system.Bodies.Add(planet);

                int moons = rng.Range(_desc.MoonsPerPlanetMin, _desc.MoonsPerPlanetMax);
                for (int m = 0; m < moons; m++)
                {
                    system.Bodies.Add(new CelestialBody
                    {
                        Id = $"{planet.Id}-m{m}",
                        Name = $"{planet.Name}{(char)('a' + m)}",
                        Kind = CelestialKind.Moon,
                        PlanetType = PickPlanetType(rng),
                        SystemId = system.Id,
                    });
                }
            }

            if (rng.NextDouble() < _desc.AsteroidFields.Probability())
            {
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-a0", Name = $"{system.Name} Belt", Kind = CelestialKind.AsteroidField, SystemId = system.Id });
            }

            if (rng.NextDouble() < _desc.SpaceStations.Probability())
            {
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-st", Name = $"{system.Name} Station", Kind = CelestialKind.SpaceStation, SystemId = system.Id });
            }

            if (rng.NextDouble() < _desc.Wrecks.Probability())
            {
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-w", Name = $"Wreck near {system.Name}", Kind = CelestialKind.Wreck, SystemId = system.Id });
            }

            galaxy.Systems.Add(system);
        }

        return galaxy;
    }

    private string PickPlanetType(DeterministicRandom rng)
    {
        if (_planetWeights.Count == 0)
        {
            return "rocky";
        }

        int total = 0;
        foreach (var (_, w) in _planetWeights)
        {
            total += w;
        }

        int roll = rng.Range(1, total);
        foreach (var (key, w) in _planetWeights)
        {
            roll -= w;
            if (roll <= 0)
            {
                return key;
            }
        }

        return _planetWeights[0].key;
    }

    private static string MakeName(DeterministicRandom rng)
    {
        string a = NamePrefixes[rng.Range(0, NamePrefixes.Length - 1)];
        string b = NameSuffixes[rng.Range(0, NameSuffixes.Length - 1)];
        int number = rng.Range(1, 99);
        return $"{a}{b}-{number}";
    }
}
