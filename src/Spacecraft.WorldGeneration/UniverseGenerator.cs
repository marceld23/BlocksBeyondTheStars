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

    // System-space flight layout (the star at the origin). Planar disc; tuned so adjacent planets are a
    // short minute-or-so cruise apart at the ship's flight speed. Used by the system-scale flight layer.
    private const float Tau = 6.2831853f;
    private const float BaseOrbit = 420f;   // first planet's orbit radius
    private const float OrbitStep = 520f;   // extra radius per planet outward
    private const float OrbitJitter = 140f; // random radial wobble so orbits aren't perfectly spaced
    private const float MoonOrbit = 90f;    // first moon's radius around its planet
    private const float MoonStep = 55f;     // extra radius per moon

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
                // Orbit on a planar disc around the star: outer planets sit further out, at a seeded angle.
                // Positions come from a SEPARATE hash (not rng) so the body type/name sequence — and thus
                // existing universes — stay byte-identical.
                float pAngle = Hash01(i, p, 101) * Tau;
                float pRadius = BaseOrbit + p * OrbitStep + (Hash01(i, p, 102) - 0.5f) * OrbitJitter;
                planet.SystemX = pRadius * System.MathF.Cos(pAngle);
                planet.SystemZ = pRadius * System.MathF.Sin(pAngle);
                system.Bodies.Add(planet);

                int moons = rng.Range(_desc.MoonsPerPlanetMin, _desc.MoonsPerPlanetMax);
                for (int m = 0; m < moons; m++)
                {
                    float mAngle = Hash01(i, p, 200 + m) * Tau;
                    float mRadius = MoonOrbit + m * MoonStep;
                    system.Bodies.Add(new CelestialBody
                    {
                        Id = $"{planet.Id}-m{m}",
                        Name = $"{planet.Name}{(char)('a' + m)}",
                        Kind = CelestialKind.Moon,
                        PlanetType = PickPlanetType(rng),
                        SystemId = system.Id,
                        SystemX = planet.SystemX + mRadius * System.MathF.Cos(mAngle),
                        SystemZ = planet.SystemZ + mRadius * System.MathF.Sin(mAngle),
                    });
                }
            }

            if (rng.NextDouble() < _desc.AsteroidFields.Probability())
            {
                var (ax, az) = DiscPoint(i, planets, 301);
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-a0", Name = $"{system.Name} Belt", Kind = CelestialKind.AsteroidField, SystemId = system.Id, SystemX = ax, SystemZ = az });
            }

            if (rng.NextDouble() < _desc.SpaceStations.Probability())
            {
                var (sx, sz) = DiscPoint(i, planets, 302);
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-st", Name = $"{system.Name} Station", Kind = CelestialKind.SpaceStation, SystemId = system.Id, SystemX = sx, SystemZ = sz });
            }

            if (rng.NextDouble() < _desc.Wrecks.Probability())
            {
                var (wx, wz) = DiscPoint(i, planets, 303);
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-w", Name = $"Wreck near {system.Name}", Kind = CelestialKind.Wreck, SystemId = system.Id, SystemX = wx, SystemZ = wz });
            }

            galaxy.Systems.Add(system);
        }

        return galaxy;
    }

    /// <summary>A 0..1 deterministic value from a separate hash (does not disturb the body-generation rng).</summary>
    private float Hash01(long a, long b, long c)
        => (float)((Noise.Hash(_seed, a, b, c) >> 11) * (1.0 / 9007199254740992.0));

    /// <summary>A seeded point on the system disc (out to roughly the outermost planet's orbit).</summary>
    private (float X, float Z) DiscPoint(int systemIndex, int planets, int salt)
    {
        float angle = Hash01(systemIndex, salt, 1) * Tau;
        float radius = BaseOrbit + Hash01(systemIndex, salt, 2) * (planets * OrbitStep + OrbitStep);
        return (radius * System.MathF.Cos(angle), radius * System.MathF.Sin(angle));
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
