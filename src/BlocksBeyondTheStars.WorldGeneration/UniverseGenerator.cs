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
public sealed partial class UniverseGenerator
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
    private readonly GameContent _content;
    private readonly List<(string key, int weight)> _planetWeights;
    private readonly List<(string key, int weight)> _gen1Weights;
    private readonly List<(string key, int weight)> _asteroidWeights;
    private readonly string? _startPlanetType;

    /// <param name="startPlanetType">The planet type the server will start the world on — it picks the first planet of
    /// that type anywhere in the galaxy (<c>GameServer.LoadWorld</c>, often an outermost orbit). From generation 23 the
    /// gas-giant pass (#2437) never retypes that planet, so a seed's start world is the same with and without the new rolls.
    /// Null or an unknown type protects nothing; the server passes <c>WorldMetadata.DefaultPlanetType</c>, which is
    /// persisted, so every load re-derives the same galaxy.</param>
    public UniverseGenerator(long seed, WorldDescription description, GameContent content, string? startPlanetType = null)
    {
        _seed = seed;
        _desc = description;
        _content = content;
        _startPlanetType = startPlanetType;
        _planetWeights = BuildPlanetWeights(description, content);
        _gen1Weights = BuildGenerationWeights(description, content);
        _asteroidWeights = BuildAsteroidWeights(content);
    }

    /// <summary>Share of the eligible planets and moons (outside the start system) a generation-1 galaxy retypes
    /// into the generation-1 planet types (#1649) — the classic layout stays byte-identical, some worlds are new kinds.</summary>
    private const int Gen1RetypeChance = 46; // of 256 ≈ 18 %

    /// <summary>#2343: a generation-21 galaxy retypes a quarter of its eligible bodies — the spectacle package added four
    /// types to a pool ~23 types already share, and at 18 % a weight-5 type showed in about every second galaxy. An older
    /// description keeps 46/256, so every galaxy created before regenerates byte for byte.</summary>
    private const int Gen21RetypeChance = 64; // of 256 = 25 %

    private int RetypeChance => _desc.TerrainGeneration >= WorldDescription.SpectacleGeneration ? Gen21RetypeChance : Gen1RetypeChance;

    /// <summary>The generation-gated types (#1649: <see cref="PlanetType.MinTerrainGeneration"/> &gt; 0) this
    /// description may roll, weighted like the classic table (player override, else spawn weight × exotic).</summary>
    private static List<(string, int)> BuildGenerationWeights(WorldDescription desc, GameContent content)
    {
        var list = new List<(string, int)>();
        foreach (var key in content.Planets.Keys)
        {
            if (content.GetPlanet(key) is not { Selectable: true } p || p.MinTerrainGeneration <= 0 || p.MinTerrainGeneration > desc.TerrainGeneration)
            {
                continue;
            }

            int weight = desc.PlanetTypeFrequencies.TryGetValue(key, out var freq)
                ? freq.Weight()
                : p.Exotic ? FrequencyExtensions.ExoticWeight(p.SpawnWeight, desc.ExoticWorlds, desc.TerrainGeneration) // #3313
                : System.Math.Max(0, p.SpawnWeight);
            if (weight > 0)
            {
                list.Add((key, weight));
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    /// <summary>Retypes a share of the planets and moons into the generation-1 types (#1649) AFTER the classic
    /// generation ran: the layout, ids, names and every rng draw stay exactly as on a classic galaxy (the
    /// weighted classic roll never sees the new types), only the retyped bodies read as new kinds. The start
    /// system and the galaxy's first breathable planet (the server's start-body rule) are left alone, so the
    /// start world and its neighbourhood are what they always were. Deterministic per body from the seed.</summary>
    private void ApplyGenerationTypes(Galaxy galaxy)
    {
        if (_gen1Weights.Count == 0)
        {
            return;
        }

        CelestialBody? firstBreathable = null;
        foreach (var b in galaxy.AllBodies())
        {
            if (b.Kind == CelestialKind.Planet && b.PlanetType is { } t && _content.GetPlanet(t) is { } p
                && string.Equals(p.Atmosphere, "breathable", System.StringComparison.OrdinalIgnoreCase))
            {
                firstBreathable = b;
                break;
            }
        }

        int total = 0;
        foreach (var (_, w) in _gen1Weights)
        {
            total += w;
        }

        // Once-per-galaxy types (2026-09, Titas): the first roll in the ORIGINAL systems keeps it; any other roll of it — a
        // second body, a moon, a system a growing galaxy appended later — re-picks from the table without those types,
        // with the same hash, so the draw stays deterministic and the first N systems re-derive byte for byte.
        var repeatable = _gen1Weights.FindAll(e => _content.GetPlanet(e.key)?.OncePerGalaxy != true);
        int repeatableTotal = 0;
        foreach (var (_, w) in repeatable)
        {
            repeatableTotal += w;
        }

        var seenOnce = new HashSet<string>(System.StringComparer.Ordinal);
        var kept = _desc.TerrainGeneration >= WorldDescription.SpectacleGeneration ? FirstPlanetOfEachType(galaxy) : null;

        for (int si = 1; si < galaxy.Systems.Count; si++)
        {
            var system = galaxy.Systems[si];
            for (int bi = 0; bi < system.Bodies.Count; bi++)
            {
                var body = system.Bodies[bi];
                if ((body.Kind != CelestialKind.Planet && body.Kind != CelestialKind.Moon) || ReferenceEquals(body, firstBreathable)
                    || kept?.Contains(body) == true)
                {
                    continue;
                }

                ulong h = Noise.Hash(_seed ^ 0x6E1A7, si, bi, 0x1649);
                if ((h & 0xFF) >= (ulong)RetypeChance)
                {
                    continue;
                }

                int roll = (int)((h >> 8) % (ulong)total) + 1;
                string? picked = null;
                foreach (var (key, w) in _gen1Weights)
                {
                    roll -= w;
                    if (roll <= 0)
                    {
                        picked = key;
                        break;
                    }
                }

                if (picked != null && _content.GetPlanet(picked)?.OncePerGalaxy == true
                    && (body.Kind != CelestialKind.Planet || si >= _desc.StarSystemCount || !seenOnce.Add(picked)))
                {
                    picked = null;
                    if (repeatableTotal > 0)
                    {
                        int again = (int)((h >> 8) % (ulong)repeatableTotal) + 1;
                        foreach (var (key, w) in repeatable)
                        {
                            again -= w;
                            if (again <= 0)
                            {
                                picked = key;
                                break;
                            }
                        }
                    }
                }

                if (picked != null)
                {
                    body.PlanetType = picked;
                }
            }
        }

        ApplyGasGiants(galaxy, firstBreathable);
        ApplyGuarantees(galaxy, firstBreathable, kept);
    }

    /// <summary>#2343: the first planet of every type the classic roll produced, in the server's start-pick order
    /// (<see cref="Galaxy.AllBodies"/>). A generation-21 galaxy retypes a quarter of its bodies, and at that share a
    /// rarely rolled type — the player's chosen start type among them — was wiped from one galaxy in four; the server
    /// then forces the type onto the galaxy's first planet, which moves the start world to a body that was never meant
    /// to be one. Keeping each type's first planet makes "the first planet of the start type" the body the classic
    /// layout rolled. Only consulted on generation 21 and later, so every older galaxy regenerates byte for byte.</summary>
    private static HashSet<CelestialBody> FirstPlanetOfEachType(Galaxy galaxy)
    {
        var kept = new HashSet<CelestialBody>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var body in galaxy.AllBodies())
        {
            if (body.Kind == CelestialKind.Planet && body.PlanetType is { Length: > 0 } type && seen.Add(type))
            {
                kept.Add(body);
            }
        }

        return kept;
    }

    /// <summary>#2343: every <see cref="PlanetType.GuaranteedOnce"/> type this description may roll stands on at least
    /// one planet of the ORIGINAL systems. Runs last, so it never undoes the gas giant or a once-per-galaxy landmark: when
    /// the retype pass rolled none, the eligible planet with the smallest hash (outside the start system, not the first
    /// breathable one, not a body already carrying a landmark or a guaranteed type, not a type's first planet) takes it.
    /// Deterministic from the seed; a grown galaxy's appended systems are never looked at, so they change nothing.</summary>
    private void ApplyGuaranteedTypes(Galaxy galaxy, CelestialBody? firstBreathable, HashSet<CelestialBody>? kept)
    {
        int original = System.Math.Min(galaxy.Systems.Count, System.Math.Max(0, _desc.StarSystemCount));
        foreach (var (key, _) in _gen1Weights)
        {
            if (_content.GetPlanet(key) is not { GuaranteedOnce: true })
            {
                continue;
            }

            bool present = false;
            for (int si = 0; si < original && !present; si++)
            {
                foreach (var body in galaxy.Systems[si].Bodies)
                {
                    present |= body.Kind == CelestialKind.Planet && body.PlanetType == key;
                }
            }

            if (present)
            {
                continue;
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
                    if (current is { OncePerGalaxy: true } || current is { GuaranteedOnce: true } || current is { SpawnWeight: 0 })
                    {
                        continue; // a landmark, another guaranteed type, or the gas giant — never taken away
                    }

                    ulong h = Noise.Hash(_seed ^ 0x6A4A21, si, bi, (int)(WorldGenerator.StableHash(key) & 0x7FFFFFFF));
                    if (h < bestHash)
                    {
                        bestHash = h;
                        best = body;
                    }
                }
            }

            if (best is not null)
            {
                best.PlanetType = key;
            }
        }
    }

    /// <summary>#2112 (generation 18, Marcel's placement): the <see cref="SystemArchetype.LoneGiant"/> system's one planet —
    /// "the gas giant fantasy" the archetype was written for — BECOMES the gas giant, and the outermost planet of any other
    /// system rolls one in <see cref="GasGiantOuterOrbitChance"/>; both get rings in about three cases of five. Runs after
    /// the generic retype (the gas giant's own spawn weight is 0, so that roll never picks it), touches only the type and
    /// the ring seed of the chosen body, and never the start system, the first breathable planet or a once-per-galaxy
    /// landmark — and from generation 23 never the planet the server will pick as the start (the first planet of the type
    /// handed to the constructor, which may sit on any system's outermost orbit). An older galaxy (a description below
    /// generation 18) keeps every body's type.</summary>
    private void ApplyGasGiants(Galaxy galaxy, CelestialBody? firstBreathable)
    {
        if (_desc.TerrainGeneration < WorldDescription.GasGiantGeneration || _content.GetPlanet(GasGiantKey) is null)
        {
            return;
        }

        // #2437 (generation 23): a gas giant in almost every system — Justus flew his whole galaxy and found none, because
        // the one-in-six roll had hit only the unnamed catalogue system he skipped. Every system with room for one now
        // puts a giant on its outermost orbit with a high chance, and a big system may hang a second one on the orbit
        // inside it. A galaxy pinned below generation 23 keeps the old roll bit for bit.
        bool perSystem = _desc.TerrainGeneration >= WorldDescription.GasGiantPerSystemGeneration;

        // The server starts on the first planet of the configured type anywhere in the galaxy; with a giant on most
        // outermost orbits that planet would be eaten in a good share of seeds (the start would move to the next one of
        // its type, or vanish into the retype fallback). Found before any roll of this pass, protected throughout it.
        CelestialBody? startPick = null;
        if (perSystem && _startPlanetType is { Length: > 0 } startType && _content.GetPlanet(startType) is not null)
        {
            startPick = galaxy.AllBodies().FirstOrDefault(b => b.Kind == CelestialKind.Planet && b.PlanetType == startType);
        }

        for (int si = 1; si < galaxy.Systems.Count; si++)
        {
            var system = galaxy.Systems[si];
            var archetype = _desc.SystemVariance ? SystemArchetypes.ForIndex(_seed, si) : SystemArchetype.Standard;
            if (archetype == SystemArchetype.LoneGiant)
            {
                MakeGasGiant(system.Bodies.Find(b => b.Kind == CelestialKind.Planet), firstBreathable, startPick, si);
                continue;
            }

            var planets = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            if (!perSystem)
            {
                ulong h = Noise.Hash(_seed ^ 0x6A5, si, 0, 0x2112);
                if ((h & 0xFF) < (ulong)GasGiantOuterOrbitChance && planets.Count > 0)
                {
                    MakeGasGiant(planets[planets.Count - 1], firstBreathable, startPick: null, si); // the outermost orbit
                }

                continue;
            }

            // Generation 23: a system of one planet keeps it (there has to be a world to land on); from two planets up the
            // outermost orbit rolls high, and from four planets up the orbit inside it rolls a second giant. The rolls read
            // their own hash bits, so the ring draw below stays what it was for a giant that would have rolled before.
            if (planets.Count < GasGiantMinPlanetsPerSystem)
            {
                continue;
            }

            if ((Noise.Hash(_seed ^ 0x6A5, si, 3, 0x2437) & 0xFF) < (ulong)GasGiantOuterOrbitChancePerSystem)
            {
                MakeGasGiant(planets[planets.Count - 1], firstBreathable, startPick, si);
            }

            if (planets.Count >= GasGiantSecondMinPlanets
                && (Noise.Hash(_seed ^ 0x6A5, si, 4, 0x2437) & 0xFF) < (ulong)GasGiantSecondOrbitChance)
            {
                MakeGasGiant(planets[planets.Count - 2], firstBreathable, startPick, si);
            }
        }
    }

    /// <summary>Retypes <paramref name="planet"/> into the gas giant (and rolls its rings) unless it is the galaxy's first
    /// breathable planet, the server's start pick (<paramref name="startPick"/>, generation 23) or a once-per-galaxy
    /// landmark — those keep their type whatever the orbit rolled.</summary>
    private void MakeGasGiant(CelestialBody? planet, CelestialBody? firstBreathable, CelestialBody? startPick, int si)
    {
        if (planet is null || ReferenceEquals(planet, firstBreathable) || ReferenceEquals(planet, startPick)
            || planet.PlanetType == GasGiantKey
            || _content.GetPlanet(planet.PlanetType ?? string.Empty)?.OncePerGalaxy == true)
        {
            return;
        }

        planet.PlanetType = GasGiantKey;
        if (planet.RingSeed == 0 && (Noise.Hash(_seed ^ 0x6A5, si, 1, 0x2112) & 0xFF) < (ulong)GasGiantRingChance)
        {
            planet.RingSeed = 1 + (int)(Noise.Hash(_seed ^ 0x6A5, si, 2, 0x2112) % 999_999UL);
        }
    }

    /// <summary>The gas giant's type key (#2112).</summary>
    public const string GasGiantKey = "gas_giant";

    /// <summary>Generations 18–22: one system in six (of the ones that are not a lone giant) puts a gas giant on its outermost
    /// orbit; ~60 % ring.</summary>
    private const int GasGiantOuterOrbitChance = 40;  // of 256
    private const int GasGiantRingChance = 154;       // of 256

    /// <summary>Generation 23 (#2437): a system needs this many planets before its outermost orbit may become the gas giant —
    /// a one-planet system keeps its landable world.</summary>
    public const int GasGiantMinPlanetsPerSystem = 2;

    /// <summary>Generation 23 (#2437): the outermost orbit of an eligible system becomes the gas giant in about 85 % of systems.</summary>
    public const int GasGiantOuterOrbitChancePerSystem = 218; // of 256 ≈ 85 %

    /// <summary>Generation 23 (#2437): a system of this many planets or more rolls a second giant on the orbit inside the outermost.</summary>
    public const int GasGiantSecondMinPlanets = 4;

    /// <summary>Generation 23 (#2437): the second giant's chance, about 35 %.</summary>
    public const int GasGiantSecondOrbitChance = 90;  // of 256 ≈ 35 %

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
        var list = new List<(string, int)>();
        foreach (var key in content.Planets.Keys)
        {
            if (content.GetPlanet(key) is not { Selectable: true } p)
            {
                continue; // service types stay out — even when an override names them explicitly
            }

            if (p.MinTerrainGeneration > 0)
            {
                continue; // #1649: generation-gated types never enter the classic weighted roll — ApplyGenerationTypes retypes a share of the bodies afterwards, so every layout stays byte-identical
            }

            int weight;
            if (desc.PlanetTypeFrequencies.TryGetValue(key, out var freq))
            {
                weight = freq.Weight(); // the player's explicit per-type choice; exotic scaling doesn't re-apply
            }
            else
            {
                weight = p.Exotic
                    ? FrequencyExtensions.ExoticWeight(p.SpawnWeight, desc.ExoticWorlds, desc.TerrainGeneration) // #3313
                    : System.Math.Max(0, p.SpawnWeight);
            }

            if (weight > 0)
            {
                list.Add((key, weight));
            }
        }

        return list;
    }

    /// <summary>#3308 (generation 33, KuBa-W): every guaranteed ASTEROID type (<see cref="PlanetType.GuaranteedOnce"/> on an
    /// "asteroid…" key the description's generation opens) becomes exactly one asteroid field of the ORIGINAL systems — the one
    /// outside the first system with the smallest hash of the type's own salt (the first system's asteroids only when no other
    /// system has one, e.g. a one-system galaxy). It ignores the "Exotic worlds" slider (Marcel: guaranteed, no slider). The
    /// classic family draw never sees such a type (its spawn weight is 0) and the planet retype pass never looks at asteroid
    /// fields, so one guaranteed asteroid is never placed twice; appended systems of a growing galaxy are never candidates.
    /// Below generation 33 a no-op, so every older galaxy regenerates byte for byte.</summary>
    private void ApplyGuaranteedAsteroids(Galaxy galaxy)
    {
        if (_desc.TerrainGeneration < WorldDescription.KubaWGeneration)
        {
            return;
        }

        int original = System.Math.Min(galaxy.Systems.Count, System.Math.Max(0, _desc.StarSystemCount));
        var keys = _content.Planets.Values
            .Where(p => p.GuaranteedOnce && WorldConstants.IsAsteroidType(p.Key) && p.MinTerrainGeneration <= _desc.TerrainGeneration)
            .Select(p => p.Key)
            .OrderBy(k => k, System.StringComparer.Ordinal)
            .ToList();
        foreach (string key in keys)
        {
            var body = GuaranteedAsteroidSlot(galaxy, original, key, firstSystem: 1)
                       ?? GuaranteedAsteroidSlot(galaxy, original, key, firstSystem: 0);
            if (body is not null)
            {
                body.PlanetType = key;
            }
        }
    }

    /// <summary>The asteroid field of systems <paramref name="firstSystem"/>..<paramref name="original"/>−1 with the smallest
    /// hash for <paramref name="key"/> that does not already carry a guaranteed type, or null when there is none.</summary>
    private CelestialBody? GuaranteedAsteroidSlot(Galaxy galaxy, int original, string key, int firstSystem)
    {
        CelestialBody? best = null;
        ulong bestHash = ulong.MaxValue;
        int salt = (int)(WorldGenerator.StableHash("guaranteed-asteroid:" + key) & 0x7FFFFFFF);
        for (int si = firstSystem; si < original; si++)
        {
            var bodies = galaxy.Systems[si].Bodies;
            for (int bi = 0; bi < bodies.Count; bi++)
            {
                var body = bodies[bi];
                if (body.Kind != CelestialKind.AsteroidField
                    || (body.PlanetType is { } t && _content.GetPlanet(t) is { GuaranteedOnce: true }))
                {
                    continue;
                }

                ulong h = Noise.Hash(_seed ^ 0x3306A5, si, bi, salt);
                if (h < bestHash)
                {
                    bestHash = h;
                    best = body;
                }
            }
        }

        return best;
    }

    /// <summary>A 0..1 deterministic value from a separate hash (does not disturb the body-generation rng).</summary>
    private float Hash01(long a, long b, long c)
        => (float)((Noise.Hash(_seed, a, b, c) >> 11) * (1.0 / 9007199254740992.0));

    /// <summary>Seeded orbit direction: mostly prograde (+1), ~20% retrograde (-1), so a system mixes both.</summary>
    private float OrbitSign(long a, long b, long c) => Hash01(a, b, c) < 0.2f ? -1f : 1f;

    /// <summary>Planet types that ring more often (#596) — icy/crystalline worlds, because real
    /// planetary rings are mostly water ice.</summary>
    private static bool IsRingProneType(string? planetType)
        => planetType is "ice" or "tundra" or "crystal" or "crystal_living";

    // Belt geometry (#683). Radial jitter is kept well under half an orbit step so a belt member can
    // never wander into a neighbouring lane; the angular slots guarantee the flight view's pair gap
    // by construction (the separation pass would otherwise shove a member radially OFF the belt).
    private const float BeltRadialJitter = 120f;   // full jitter span (±60) around the belt radius
    private const float MinInnerBeltGap = 620f;    // an inner belt needs this much room between two planet orbits

    /// <summary>The system's 1–2 belt annulus radii (#683). The outer belt always exists, one orbit
    /// step beyond the outermost planet (the main-belt/Kuiper-belt reading). Big systems (5+ planets,
    /// enough asteroids for two rings) may roll a second, INNER belt — but only into a gap between two
    /// adjacent planet orbits wide enough that a belt rock can never crowd a planet even when their
    /// angles line up.</summary>
    private List<float> BeltRadii(int systemIndex, StarSystem system, int planets, int asteroidCount)
    {
        var orbitRadii = new List<float>();
        foreach (var b in system.Bodies)
        {
            if (b.Kind == CelestialKind.Planet)
            {
                orbitRadii.Add(System.MathF.Sqrt(b.SystemX * b.SystemX + b.SystemZ * b.SystemZ));
            }
        }

        orbitRadii.Sort();
        float outermost = orbitRadii.Count > 0 ? orbitRadii[orbitRadii.Count - 1] : BaseOrbit;
        var radii = new List<float>
        {
            outermost + OrbitStep + (Hash01(systemIndex, 800, 1) - 0.5f) * BeltRadialJitter,
        };

        if (planets >= 5 && asteroidCount >= 4 && Hash01(systemIndex, 800, 2) < 0.5f)
        {
            float bestGap = 0f, bestMid = 0f;
            for (int k = 1; k < orbitRadii.Count; k++)
            {
                float gap = orbitRadii[k] - orbitRadii[k - 1];
                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestMid = (orbitRadii[k] + orbitRadii[k - 1]) * 0.5f;
                }
            }

            if (bestGap >= MinInnerBeltGap)
            {
                radii.Add(bestMid);
            }
        }

        return radii;
    }

    /// <summary>A seeded point on one of the system's belt annuli (#683). Members go round-robin over
    /// the belts; within one belt they occupy evenly spaced angular slots with a small seeded wobble
    /// (≤¼ slot), so even the densest belt keeps at least half a slot of arc between neighbours —
    /// comfortably above the flight view's required clear gap at any belt radius.</summary>
    private (float X, float Z) BeltPoint(int systemIndex, int asteroidIndex, int asteroidCount, List<float> radii)
    {
        int belt = asteroidIndex % radii.Count;
        int slot = asteroidIndex / radii.Count;
        int slots = (asteroidCount - belt + radii.Count - 1) / radii.Count; // members on THIS belt
        float slotArc = Tau / System.Math.Max(1, slots);
        float angle = Hash01(systemIndex, 810 + belt, 1) * Tau
            + (slot + (Hash01(systemIndex, 820 + asteroidIndex, 1) - 0.5f) * 0.5f) * slotArc;
        float radius = radii[belt] + (Hash01(systemIndex, 820 + asteroidIndex, 2) - 0.5f) * BeltRadialJitter;
        return (radius * System.MathF.Cos(angle), radius * System.MathF.Sin(angle));
    }

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
                // Only avoid the solid, sized bodies — not other free-floaters (placed before this one anyway). A debris
                // field (#2353) is a free-floater placed AFTER the generator; treating it as an obstacle here would move
                // the start system's late-added station in an existing save.
                if (b.Kind == CelestialKind.SpaceStation || b.Kind == CelestialKind.Wreck || b.Kind == CelestialKind.DebrisField)
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

    /// <summary>LEGACY name pattern ("Veyra-42"). No longer displayed anywhere — but still CALLED, and
    /// its result discarded, because its three rng draws sit in front of every planet-count/type draw:
    /// removing them would regenerate every existing universe with different bodies (pinned by
    /// GalaxyLayoutRegressionTests). The tables must keep their sizes for the same reason.</summary>
    private static string MakeName(DeterministicRandom rng)
    {
        string a = NamePrefixes[rng.Range(0, NamePrefixes.Length - 1)];
        string b = NameSuffixes[rng.Range(0, NameSuffixes.Length - 1)];
        int number = rng.Range(1, 99);
        return $"{a}{b}-{number}";
    }

    /// <summary>Picks the system's naming registry (#678): mostly coined proper names, a quarter
    /// catalog designations, some two-part region names and — in archetype-varied space — the rare
    /// name that already tells you what kind of system you are entering. Returns whether the catalog
    /// style won, because catalog systems letter their planets ("HX-113 b") instead of Roman numerals.</summary>
    private static (string Name, bool Catalog) MakeSystemName(DeterministicRandom nameRng, SystemArchetype archetype, HashSet<string> used)
    {
        double style = nameRng.NextDouble();
        if (style < 0.25)
        {
            return (Unique(used, () => NameGenerator.Catalog(nameRng)), true);
        }

        if (style < 0.40)
        {
            return (Unique(used, () => NameGenerator.Region(nameRng)), false);
        }

        if (style < 0.45)
        {
            // Null for archetypes without a registry of their own (Standard & co) → coined fallback.
            // Small curated pools — the numbered Unique fallback keeps even a pirate-heavy galaxy sane.
            string? flavored = NameGenerator.ArchetypeRegion(nameRng, archetype);
            if (flavored is not null)
            {
                return (used.Add(flavored) ? flavored : Unique(used, () => NameGenerator.ArchetypeRegion(nameRng, archetype)!), false);
            }
        }

        return (Unique(used, () => NameGenerator.Star(nameRng)), false);
    }

    /// <summary>Draws until the name is galaxy-unique; after that (tiny curated pools, huge galaxies)
    /// falls back to a numbered variant. Deterministic — the draw closure only pulls from the name rng.</summary>
    private static string Unique(HashSet<string> used, System.Func<string> draw)
    {
        string candidate = string.Empty;
        for (int attempt = 0; attempt < 24; attempt++)
        {
            candidate = draw();
            if (used.Add(candidate))
            {
                return candidate;
            }
        }

        for (int n = 2; ; n++)
        {
            string numbered = $"{candidate} {n}";
            if (used.Add(numbered))
            {
                return numbered;
            }
        }
    }

    /// <summary>Renames a planet and carries the rename into its lettered moons and any attributive
    /// "<planet> Station" already placed over it, so the family stays consistent.</summary>
    private static void RenameWithMoons(StarSystem system, CelestialBody planet, string newName)
    {
        string old = planet.Name;
        planet.Name = newName;
        foreach (var b in system.Bodies)
        {
            if (b.Kind == CelestialKind.Moon && b.ParentId == planet.Id && b.Name.StartsWith(old, System.StringComparison.Ordinal))
            {
                b.Name = newName + b.Name.Substring(old.Length); // keep the "-a" tail
            }
            else if (b.Kind == CelestialKind.SpaceStation && b.Name == $"{old} Station")
            {
                b.Name = $"{newName} Station";
            }
        }
    }

    /// <summary>Fixed names (2026-09, Titas): every body whose FINAL type carries <see cref="PlanetType.FixedName"/> is called
    /// exactly that — lettered moons and an attributive station follow. The server calls it after the per-save type pins,
    /// so a body only ever takes the name of the type it really is. Idempotent.</summary>
    public static void ApplyFixedNames(Galaxy galaxy, GameContent content)
    {
        foreach (var system in galaxy.Systems)
        {
            foreach (var body in system.Bodies)
            {
                // #3308: an asteroid field of a fixed-name type (KuBa-W) is named too; no older type is an asteroid with a fixed name.
                if (body.Kind is CelestialKind.Planet or CelestialKind.AsteroidField
                    && content.GetPlanet(body.PlanetType ?? string.Empty) is { FixedName.Length: > 0 } p
                    && body.Name != p.FixedName)
                {
                    RenameWithMoons(system, body, p.FixedName);
                }
            }
        }
    }
}
