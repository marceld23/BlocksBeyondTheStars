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
            // Default: every selectable planet type at its own SpawnWeight (special bodies such as landable
            // asteroids are Selectable=false so they never appear as ordinary system planets). The per-type
            // weight lets common worlds dominate while exotic ones stay rare without a world-description override.
            foreach (var key in content.Planets.Keys)
            {
                if (content.GetPlanet(key) is { Selectable: true } p)
                {
                    list.Add((key, System.Math.Max(1, p.SpawnWeight)));
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

            // A few large, landable asteroids per system: walkable "asteroid" worlds you can land on with the
            // ship or on an EVA, each sized deterministically by its id (CircumferenceFor → Asteroid class). The
            // small mineable rocks spawn separately as space entities at any body. (One rng draw, like the old
            // single-belt gate, so existing systems' stations/wrecks downstream stay put.)
            int asteroidCount = 2 + (rng.NextDouble() < 0.5 ? 1 : 0); // 2 or 3
            for (int a = 0; a < asteroidCount; a++)
            {
                var (ax, az) = DiscPoint(i, planets, 310 + a);
                system.Bodies.Add(new CelestialBody
                {
                    Id = $"{system.Id}-a{a}",
                    Name = $"{system.Name} Asteroid {a + 1}",
                    Kind = CelestialKind.AsteroidField,
                    PlanetType = "asteroid",
                    SystemId = system.Id,
                    SystemX = ax,
                    SystemZ = az,
                });
            }

            if (rng.NextDouble() < _desc.SpaceStations.Probability())
            {
                var (sx, sz) = DiscPoint(i, planets, 302);
                (sx, sz) = SeparateFromBodies(system, sx, sz); // never park a station inside a planet/moon/asteroid (B29)
                system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-st", Name = $"{system.Name} Station", Kind = CelestialKind.SpaceStation, SystemId = system.Id, SystemX = sx, SystemZ = sz });
            }

            if (rng.NextDouble() < _desc.Wrecks.Probability())
            {
                var (wx, wz) = DiscPoint(i, planets, 303);
                (wx, wz) = SeparateFromBodies(system, wx, wz); // a wreck shouldn't clip a body either
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

    /// <summary>Nudges a free-floating body (station/wreck) out of any planet/moon/asteroid it would otherwise
    /// spawn inside, by pushing it radially away from each overlapped body until clear. The clearances are in
    /// system-disc units, sized so the body never visually clips another in the compact flight view (where
    /// system units are scaled down ~0.16×): a planet needs the widest berth, a small asteroid the least.</summary>
    private static (float X, float Z) SeparateFromBodies(StarSystem system, float x, float z)
    {
        for (int iter = 0; iter < 24; iter++)
        {
            bool moved = false;
            foreach (var b in system.Bodies)
            {
                // Only avoid the solid, sized bodies — not other free-floaters (placed before this one anyway).
                if (b.Kind == CelestialKind.SpaceStation || b.Kind == CelestialKind.Wreck)
                {
                    continue;
                }

                float dx = x - b.SystemX, dz = z - b.SystemZ;
                float dist = System.MathF.Sqrt(dx * dx + dz * dz);
                float need = BodyClearance(b.Kind);
                if (dist < need)
                {
                    float nx, nz;
                    if (dist > 0.001f)
                    {
                        nx = dx / dist; nz = dz / dist;
                    }
                    else
                    {
                        float r = System.MathF.Sqrt(x * x + z * z); // co-located → shove outward from the star
                        nx = r > 0.001f ? x / r : 1f;
                        nz = r > 0.001f ? z / r : 0f;
                    }

                    x = b.SystemX + nx * need;
                    z = b.SystemZ + nz * need;
                    moved = true;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        return (x, z);
    }

    /// <summary>Minimum spawn distance (system-disc units) a free-floater must keep from a body of this kind.</summary>
    private static float BodyClearance(CelestialKind kind) => kind switch
    {
        // Generous berths so a station never visually clips a body in the flight view (B50: stations were still
        // sticking in planets) — the rendered body radius can exceed the old margins, so give extra room.
        CelestialKind.Planet => 470f,
        CelestialKind.Moon => 290f,
        CelestialKind.AsteroidField => 215f,
        _ => 160f,
    };

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
