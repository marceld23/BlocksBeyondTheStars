// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

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
    private const float StationOrbit = 500f; // a station hangs just beyond its planet's clearance (470) — "over" it

    private readonly long _seed;
    private readonly WorldDescription _desc;
    private readonly List<(string key, int weight)> _planetWeights;
    private readonly List<(string key, int weight)> _asteroidWeights;

    public UniverseGenerator(long seed, WorldDescription description, GameContent content)
    {
        _seed = seed;
        _desc = description;
        _planetWeights = BuildPlanetWeights(description, content);
        _asteroidWeights = BuildAsteroidWeights(content);
    }

    /// <summary>The landable-asteroid families and their relative frequency (#515). Every non-selectable
    /// "asteroid…" type in planets.json is one, weighted by its <c>spawnWeight</c> — so adding a family is a
    /// pure data change. Sorted by key so the draw is stable no matter how the content dictionary iterates.
    /// The world-creation planet-type sliders deliberately do NOT reach these: asteroid bodies exist per
    /// system regardless of which planet types the player enabled.</summary>
    private static List<(string, int)> BuildAsteroidWeights(GameContent content)
    {
        var list = new List<(string, int)>();
        foreach (var key in content.Planets.Keys)
        {
            if (!WorldConstants.IsAsteroidType(key) || content.GetPlanet(key) is not { Selectable: false } p)
            {
                continue;
            }

            int weight = System.Math.Max(0, p.SpawnWeight);
            if (weight > 0)
            {
                list.Add((key, weight));
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    private static List<(string, int)> BuildPlanetWeights(WorldDescription desc, GameContent content)
    {
        // Base: every selectable planet type at its own SpawnWeight (special bodies such as landable
        // asteroids are Selectable=false so they never appear as ordinary system planets). The per-type
        // weight lets common worlds dominate while exotic ones stay rare. The "exotic worlds" frequency
        // scales the exotic types' weights — Off removes them entirely, Frequent makes the strange the norm.
        //
        // Per-type overrides (UI sliders / --planet-types / BBS_* config) LAYER on top (#471): a touched
        // row replaces only ITS OWN weight. The old behaviour replaced the entire table the moment one
        // entry existed — a single dragged slider collapsed the whole galaxy to one type, sliding a type
        // to Off degenerated to all-rocky, and the ExoticWorlds slider silently died. Layering also
        // repairs already-saved worlds that carry such an override. Two hardening fixes ride along:
        // the override path now enforces Selectable too (it used to accept orbital_station & co. and
        // generated landless void "planets"), and an explicit spawnWeight of 0 in the data now really
        // retires a type (the old Math.Max(1, …) clamp silently promoted it back to ~0.7 %).
        double exotic = desc.ExoticWorlds switch
        {
            Frequency.Off => 0.0,
            Frequency.VeryRare => 0.34,
            Frequency.Rare => 0.6,
            Frequency.Frequent => 2.5,
            _ => 1.0,
        };

        var list = new List<(string, int)>();
        foreach (var key in content.Planets.Keys)
        {
            if (content.GetPlanet(key) is not { Selectable: true } p)
            {
                continue; // service types stay out — even when an override names them explicitly
            }

            int weight;
            if (desc.PlanetTypeFrequencies.TryGetValue(key, out var freq))
            {
                weight = freq.Weight(); // the player's explicit per-type choice; exotic scaling doesn't re-apply
            }
            else
            {
                weight = p.Exotic
                    ? (int)System.Math.Round(System.Math.Max(0, p.SpawnWeight) * exotic)
                    : System.Math.Max(0, p.SpawnWeight);
            }

            if (weight > 0)
            {
                list.Add((key, weight));
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
                // Procedural systems live in the "sys{i}" id namespace; bodies are "sys{i}-…". The finale's
                // "guardian_finale" / "guardian_finale-core" ids are RESERVED and added separately on story
                // reveal — never here — so random world/station generation can never spawn the finale area
                // (guaranteed by UniverseTests.Procedural_generation_never_collides_with_the_reserved_finale_area).
                Id = $"sys{i}",
                Name = MakeName(rng),
                MapX = rng.NextFloat() * 1000f,
                MapY = rng.NextFloat() * 1000f,
            };

            // The system's character class (#546). With SystemVariance off (every pre-variance save) this
            // is ALWAYS Standard, and every Standard roll below is the exact draw the legacy code made —
            // so old worlds regenerate byte-identically. Drawn from its own Hash01 salt (500), never rng.
            var archetype = _desc.SystemVariance ? SystemArchetypes.ForIndex(_seed, i) : SystemArchetype.Standard;
            int planetCap = System.Math.Max(1, _desc.PlanetsPerSystemMax); // the world-size slider still caps
            int moonCap = System.Math.Max(0, _desc.MoonsPerPlanetMax);

            int planets = archetype switch
            {
                SystemArchetype.LoneGiant => 1,
                SystemArchetype.Swarm => rng.Range(System.Math.Min(6, planetCap), System.Math.Min(9, planetCap)),
                SystemArchetype.Belt => rng.Range(1, System.Math.Min(3, planetCap)),
                SystemArchetype.Hub => rng.Range(System.Math.Min(3, planetCap), System.Math.Min(5, planetCap)),
                SystemArchetype.Desolate => rng.Range(1, System.Math.Min(2, planetCap)),
                SystemArchetype.PirateHaven => rng.Range(System.Math.Min(2, planetCap), System.Math.Min(4, planetCap)),
                SystemArchetype.TwinWorlds => System.Math.Min(2, planetCap),
                _ => rng.Range(_desc.PlanetsPerSystemMin, _desc.PlanetsPerSystemMax),
            };
            float firstAngle = 0f, firstRadius = 0f; // the first twin's orbit, so the second can sit beside it
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
                if (archetype == SystemArchetype.TwinWorlds && p == 1)
                {
                    // The second twin shares the first's orbit band, a nudge along the arc — visually a pair.
                    // The client's separation pass guarantees they never clip at render scale.
                    pAngle = firstAngle + 0.25f + Hash01(i, 502, 1) * 0.25f;
                    pRadius = firstRadius + 230f;
                }

                if (p == 0)
                {
                    firstAngle = pAngle;
                    firstRadius = pRadius;
                }

                planet.SystemX = pRadius * System.MathF.Cos(pAngle);
                planet.SystemZ = pRadius * System.MathF.Sin(pAngle);
                // Visual/phase orbit (does not move the t=0 coords above). Period seeded from a SEPARATE hash
                // so the body sequence stays byte-identical; signed for occasional retrograde variety. Short
                // enough that, at the default ~10-min day, the phase visibly drifts within a play session.
                planet.OrbitPeriodDays = OrbitSign(i, p, 130) * (6f + Hash01(i, p, 131) * 34f); // 6..40 d
                planet.ParentId = string.Empty; // orbits the star at the origin
                // Archetype size identity (#549): the lone giant is genuinely huge, swarm worlds run small.
                // Salt 501; bias 0 for everything else keeps the classic hashed size (and pre-variance saves).
                planet.SizeBias = archetype switch
                {
                    SystemArchetype.LoneGiant => 0.6f + Hash01(i, 501, p) * 0.4f,
                    SystemArchetype.Swarm => -(0.2f + Hash01(i, 501, p) * 0.3f),
                    _ => 0f,
                };
                system.Bodies.Add(planet);

                int moons = archetype switch
                {
                    SystemArchetype.LoneGiant => rng.Range(4, 8), // its identity — deliberately above the slider
                    SystemArchetype.Swarm => rng.NextDouble() < 0.8 ? 0 : 1,
                    SystemArchetype.Belt or SystemArchetype.PirateHaven => rng.Range(0, System.Math.Min(2, moonCap)),
                    SystemArchetype.Desolate => rng.Range(0, System.Math.Min(1, moonCap)),
                    SystemArchetype.TwinWorlds => rng.Range(System.Math.Min(1, moonCap), System.Math.Min(2, moonCap)),
                    _ => rng.Range(_desc.MoonsPerPlanetMin, _desc.MoonsPerPlanetMax), // Standard + Hub
                };
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
                        // Moons swing fast around their parent → a quick, lively phase cycle from the surface.
                        OrbitPeriodDays = OrbitSign(i, p, 240 + m) * (0.4f + Hash01(i, p, 260 + m) * 2.1f), // 0.4..2.5 d
                        ParentId = planet.Id, // orbit centred on the (also-moving) parent planet
                    });
                }
            }

            // Twin Worlds (#549): size the second twin like the first — BiasToward inverts the size mapping,
            // so both land on (almost) the same circumference despite their independent id hashes.
            if (archetype == SystemArchetype.TwinWorlds)
            {
                var twins = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
                if (twins.Count == 2)
                {
                    int target = WorldConstants.CircumferenceFor(twins[0].Id, WorldConstants.WorldSizeClass.Planet);
                    twins[1].SizeBias = WorldConstants.BiasToward(twins[1].Id, WorldConstants.WorldSizeClass.Planet, target);
                }
            }

            // A few large, landable asteroids per system: walkable "asteroid" worlds you can land on with the
            // ship or on an EVA, each sized deterministically by its id (CircumferenceFor → Asteroid class). The
            // small mineable rocks spawn separately as space entities at any body. (One rng draw, like the old
            // single-belt gate, so existing systems' stations/wrecks downstream stay put.)
            // Each rock also rolls its own FAMILY (#515) — stony, metallic, icy, carbonaceous or crystalline —
            // so a system's asteroids differ in surface, temperature and what they're worth mining.
            int asteroidCount = archetype switch
            {
                SystemArchetype.LoneGiant => rng.Range(1, 2),
                SystemArchetype.Swarm => rng.Range(2, 4),
                SystemArchetype.Belt => rng.Range(5, 8), // the belt IS the system
                SystemArchetype.Desolate => rng.Range(0, 1),
                SystemArchetype.PirateHaven => rng.Range(3, 5), // cover for ambushes
                _ => 2 + (rng.NextDouble() < 0.5 ? 1 : 0), // Standard/Hub/Twin: 2 or 3, the legacy draw
            };
            for (int a = 0; a < asteroidCount; a++)
            {
                var (ax, az) = DiscPoint(i, planets, 310 + a);
                system.Bodies.Add(new CelestialBody
                {
                    Id = $"{system.Id}-a{a}",
                    Name = $"{system.Name} Asteroid {a + 1}",
                    Kind = CelestialKind.AsteroidField,
                    PlanetType = PickAsteroidType(i, a),
                    SystemId = system.Id,
                    SystemX = ax,
                    SystemZ = az,
                    OrbitPeriodDays = OrbitSign(i, 350 + a, 1) * (0.6f + Hash01(i, 350 + a, 2) * 2.4f), // 0.6..3 d
                    ParentId = string.Empty, // a free body on the disc → orbits the star
                });
            }

            // Stations: the per-world frequency gate decides whether a system has ANY station at all (Rare by
            // default ⇒ most systems have none). When it fires the system gets 1–3, each hanging over a DISTINCT
            // planet and named after it ("<planet> Station") so they're attributable. A second station is rare and
            // a third very rare. Count + planet picks come from a SEPARATE hash (not rng), so adding stations never
            // disturbs the wreck rng draw below — only the stations themselves change for existing universes.
            // The archetype overrides the gate where it defines the system's character: a Hub always has
            // stations, Desolate/Pirate space never does, a Lone Giant/Belt system tops out at one. The
            // gate draw itself ALWAYS happens so Standard systems keep the legacy rng sequence.
            double stationGate = rng.NextDouble();
            bool anyStations = archetype switch
            {
                SystemArchetype.Hub => _desc.SpaceStations != Frequency.Off,
                SystemArchetype.Desolate or SystemArchetype.PirateHaven => false,
                _ => stationGate < _desc.SpaceStations.Probability(),
            };
            if (anyStations)
            {
                var systemPlanets = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();

                int stationCount = 1;
                if (Hash01(i, 320, 1) < 0.25f) stationCount = 2;                          // second station: ~25%
                if (stationCount == 2 && Hash01(i, 320, 2) < 0.30f) stationCount = 3;      // third: ~7.5% (very rare)
                if (archetype is SystemArchetype.LoneGiant or SystemArchetype.Belt)
                {
                    stationCount = 1; // sparse archetypes: at most a single outpost
                }

                stationCount = System.Math.Min(stationCount, systemPlanets.Count);        // can't exceed planets (one each)

                // Deterministic shuffle so each station sits over a different planet (no two share one).
                var order = new List<int>();
                for (int k = 0; k < systemPlanets.Count; k++) order.Add(k);
                for (int k = order.Count - 1; k > 0; k--)
                {
                    int j = System.Math.Min(k, (int)(Hash01(i, 321, k) * (k + 1)));
                    (order[k], order[j]) = (order[j], order[k]);
                }

                for (int s = 0; s < stationCount; s++)
                {
                    var planet = systemPlanets[order[s]];
                    float ang = Hash01(i, 322, s) * Tau;
                    float sx = planet.SystemX + StationOrbit * System.MathF.Cos(ang);
                    float sz = planet.SystemZ + StationOrbit * System.MathF.Sin(ang);
                    (sx, sz) = SeparateFromBodies(system, sx, sz); // never park a station inside a planet/moon/asteroid (B29)
                    system.Bodies.Add(new CelestialBody
                    {
                        Id = s == 0 ? $"{system.Id}-st" : $"{system.Id}-st{s}", // keep legacy id for the first
                        Name = $"{planet.Name} Station",
                        Kind = CelestialKind.SpaceStation,
                        SystemId = system.Id,
                        SystemX = sx,
                        SystemZ = sz,
                    });
                }
            }

            // Wreck odds follow the archetype's story: lawless/derelict space is littered with them,
            // patrolled hub space is mostly cleaned up. Standard multiplier 1.0 = the legacy chance.
            double wreckChance = _desc.Wrecks.Probability() * archetype switch
            {
                SystemArchetype.Belt or SystemArchetype.Desolate => 1.5,
                SystemArchetype.PirateHaven => 2.0,
                SystemArchetype.Hub => 0.5,
                _ => 1.0,
            };
            if (rng.NextDouble() < System.Math.Min(0.95, wreckChance))
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

    /// <summary>Seeded orbit direction: mostly prograde (+1), ~20% retrograde (-1), so a system mixes both.</summary>
    private float OrbitSign(long a, long b, long c) => Hash01(a, b, c) < 0.2f ? -1f : 1f;

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

    /// <summary>The asteroid FAMILY for one asteroid body (#515): stony, metallic, icy, carbonaceous or
    /// crystalline, weighted by each family's <c>spawnWeight</c> in planets.json. Drawn from a SEPARATE hash
    /// (never the system <c>rng</c>) so adding families leaves every existing system's stations and wrecks
    /// exactly where they were — only the asteroids themselves change.</summary>
    private string PickAsteroidType(int systemIndex, int asteroidIndex)
    {
        if (_asteroidWeights.Count == 0)
        {
            return "asteroid"; // the stony family is the guaranteed fallback
        }

        int total = 0;
        foreach (var (_, w) in _asteroidWeights)
        {
            total += w;
        }

        // Hash01 → [0,1); scale into the weight table. Salt 400+ is unused elsewhere in this generator.
        int roll = 1 + (int)(Hash01(systemIndex, 400 + asteroidIndex, 7) * total);
        foreach (var (key, w) in _asteroidWeights)
        {
            roll -= w;
            if (roll <= 0)
            {
                return key;
            }
        }

        return _asteroidWeights[_asteroidWeights.Count - 1].key;
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
