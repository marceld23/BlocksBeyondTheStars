// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The per-system layout pass of <see cref="Generate(int)"/>: one system after the other, each built in fixed steps —
/// header and name, planets (orbit, size, rings, name, moons), twin sizing, asteroids, stations, wreck. The steps share
/// the system's body rng and name rng (<see cref="SystemDraft"/>) and run in the order the draws were always made, so
/// every galaxy regenerates byte for byte (pinned by <c>GalaxyLayoutRegressionTests</c>).
/// </summary>
public sealed partial class UniverseGenerator
{
    public Galaxy Generate() => Generate(_desc.StarSystemCount);

    /// <summary>Generates the galaxy with an explicit system count — the growing-galaxy hook (#1123):
    /// a grown save regenerates with <c>StarSystemCount + GalaxyGrownSystems</c>. System i is a pure
    /// function of (seed, i) and the name registry is claimed in index order, so generating N+1 systems
    /// reproduces the first N byte-identically (the PREFIX property the growth feature relies on).</summary>
    public Galaxy Generate(int systemCount)
    {
        var galaxy = new Galaxy();
        int systems = System.Math.Max(0, systemCount);

        // Galaxy-wide name registry (#678): every system and proper-named body claims its name here so
        // no two read the same. The old scheme never deduped (two "Veyra-42"s were possible).
        var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < systems; i++)
        {
            galaxy.Systems.Add(GenerateSystem(galaxy, i, usedNames));
        }

        ApplyGenerationTypes(galaxy); // #1649: generation-1 planet types, layout untouched
        ApplyGuaranteedAsteroids(galaxy); // #3308 (generation 33): KuBa-W, once in every galaxy
        return galaxy;
    }

    /// <summary>The state one system's layout steps share: its index and archetype, the body rng and the name rng (both
    /// streams continue from step to step — their draw order is the layout), the galaxy-wide name registry, the naming
    /// style and the twin pair's first orbit and names.</summary>
    private sealed class SystemDraft
    {
        public SystemDraft(int index, SystemArchetype archetype, DeterministicRandom rng, DeterministicRandom nameRng,
            HashSet<string> usedNames, bool catalogStyle)
        {
            Index = index;
            Archetype = archetype;
            Rng = rng;
            NameRng = nameRng;
            UsedNames = usedNames;
            CatalogStyle = catalogStyle;
        }

        public int Index { get; }

        public SystemArchetype Archetype { get; }

        /// <summary>The body rng: planet/moon/asteroid counts, types, the station gate and the wreck roll.</summary>
        public DeterministicRandom Rng { get; }

        /// <summary>The name rng (#678, salt 700) — never the body rng, so richer names never shift a body.</summary>
        public DeterministicRandom NameRng { get; }

        public HashSet<string> UsedNames { get; }

        /// <summary>Catalog systems letter their planets ("HX-113 b") instead of Roman numerals.</summary>
        public bool CatalogStyle { get; }

        /// <summary>Planet ids that carry a coined proper name (not a designation).</summary>
        public HashSet<string> ProperNamed { get; } = new HashSet<string>();

        /// <summary>The first twin's orbit, so the second can sit beside it.</summary>
        public float FirstAngle { get; set; }

        public float FirstRadius { get; set; }

        /// <summary>Both twin names, coined at p==0 so the pair shares a stem (#678).</summary>
        public (string A, string B) TwinNames { get; set; } = (string.Empty, string.Empty);
    }

    /// <summary>One system, every step in the order its draws were always made.</summary>
    private StarSystem GenerateSystem(Galaxy galaxy, int i, HashSet<string> usedNames)
    {
        var rng = new DeterministicRandom((long)Noise.Hash(_seed, i, 1, 1));

        // The system's character class (#546). With SystemVariance off (every pre-variance save) this
        // is ALWAYS Standard, and every Standard roll below is the exact draw the legacy code made —
        // so old worlds regenerate byte-identically. Drawn from its own Hash01 salt (500), never rng.
        // Computed before the name because the naming registries flavor by archetype (#678).
        var archetype = _desc.SystemVariance ? SystemArchetypes.ForIndex(_seed, i) : SystemArchetype.Standard;

        // Naming (#678) draws from its OWN rng stream (salt 700), never the body rng — so richer
        // names can never shift planet counts/types of existing universes. The legacy MakeName
        // draws are burned in place to keep the body stream byte-identical (pinned by
        // GalaxyLayoutRegressionTests); its result is intentionally discarded.
        var nameRng = new DeterministicRandom((long)Noise.Hash(_seed, i, 7, 700));
        _ = MakeName(rng);
        var (systemName, catalogStyle) = MakeSystemName(nameRng, archetype, usedNames);
        var draft = new SystemDraft(i, archetype, rng, nameRng, usedNames, catalogStyle);

        var system = CreateSystem(galaxy, draft, systemName);
        int planets = RollPlanetCount(draft);
        for (int p = 0; p < planets; p++)
        {
            var planet = CreatePlanet(draft, system, p);
            NamePlanet(draft, system, planet, p);
            system.Bodies.Add(planet);
            AddMoons(draft, system, planet, p);
        }

        SizeTwins(draft, system);
        AddAsteroids(draft, system, planets);
        AddStations(draft, system);
        AddWreck(draft, system, planets);
        return system;
    }

    /// <summary>The system itself: id, name and its spot on the galaxy map (two body-rng draws, then the frontier
    /// override for a grown galaxy's appended systems).</summary>
    private StarSystem CreateSystem(Galaxy galaxy, SystemDraft draft, string systemName)
    {
        int i = draft.Index;
        var rng = draft.Rng;
        var system = new StarSystem
        {
            // Procedural systems live in the "sys{i}" id namespace; bodies are "sys{i}-…". The finale's
            // "guardian_finale" / "guardian_finale-core" ids are RESERVED and added separately on story
            // reveal — never here — so random world/station generation can never spawn the finale area
            // (guaranteed by UniverseTests.Procedural_generation_never_collides_with_the_reserved_finale_area).
            Id = StarSystemIds.Rolled(i),
            Origin = StarSystemOrigin.Rolled,
            Name = systemName,
            MapX = rng.NextFloat() * 1000f,
            MapY = rng.NextFloat() * 1000f,
        };

        // Frontier growth (#1123): systems appended beyond the description's fixed count sit OUTWARD
        // of the home system, not anywhere in the 1000² box — jumping the edge pushes the rim out. The
        // uniform draws above are kept (the body rng stream must stay byte-identical, and existing
        // fixed galaxies never reach this branch); only the stored values are overridden, from the
        // separate Hash01 stream (salt 900), so a restart reproduces the same spot.
        if (i >= System.Math.Max(0, _desc.StarSystemCount) && galaxy.Systems.Count > 0)
        {
            var home = galaxy.Systems[0];
            int growthIndex = i - System.Math.Max(0, _desc.StarSystemCount);
            float angle = Hash01(i, 1, 900) * 2f * (float)System.Math.PI;
            float radius = 750f + System.Math.Min(650f, growthIndex * 35f) + Hash01(i, 2, 900) * 120f;
            system.MapX = home.MapX + (float)System.Math.Cos(angle) * radius;
            system.MapY = home.MapY + (float)System.Math.Sin(angle) * radius;
        }

        return system;
    }

    /// <summary>How many planets the system has — the archetype's band, capped by the world-size slider.</summary>
    private int RollPlanetCount(SystemDraft draft)
    {
        var rng = draft.Rng;
        int planetCap = System.Math.Max(1, _desc.PlanetsPerSystemMax); // the world-size slider still caps
        return draft.Archetype switch
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
    }

    /// <summary>Planet <paramref name="p"/>: its type (one body-rng roll), its orbit, orbit period, size bias and rings
    /// (all from separate hashes, never the rng).</summary>
    private CelestialBody CreatePlanet(SystemDraft draft, StarSystem system, int p)
    {
        int i = draft.Index;
        var archetype = draft.Archetype;
        var planet = new CelestialBody
        {
            Id = $"{system.Id}-p{p}",
            Kind = CelestialKind.Planet,
            PlanetType = PickPlanetType(draft.Rng),
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
            pAngle = draft.FirstAngle + 0.25f + Hash01(i, 502, 1) * 0.25f;
            pRadius = draft.FirstRadius + 230f;
        }

        if (p == 0)
        {
            draft.FirstAngle = pAngle;
            draft.FirstRadius = pRadius;
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
        RollRings(i, p, planet);
        return planet;
    }

    /// <summary>Planetary rings (#596): a purely visual identity — some planets carry a Saturn-like
    /// ring system. Presence and style come from their own Hash01 salts (600/601; the 6xx
    /// series belongs to rings) so the body sequence — and thus every existing universe —
    /// stays byte-identical; existing worlds simply GAIN rings, retroactive by design.
    /// Big planets ring more often, icy/crystal ones too (real rings are mostly ice).
    /// Kept RARE on purpose (~11 % of planets; playtest: the first cut at 18 % base read
    /// as "every other planet") — the start planet's guaranteed ring covers the showcase.</summary>
    private void RollRings(int i, int p, CelestialBody planet)
    {
        float ringChance = planet.SizeBias > 0.3f ? 0.22f : 0.10f;
        if (IsRingProneType(planet.PlanetType))
        {
            ringChance = System.Math.Min(0.3f, ringChance * 1.5f);
        }

        if (Hash01(i, p, 600) < ringChance)
        {
            planet.RingSeed = 1 + (int)(Hash01(i, p, 601) * 999_999f);
        }
    }

    /// <summary>Naming (#678): designations by default — Roman numerals ("Tharion II"), or exoplanet
    /// letters in a catalog system ("HX-113 b") — while LANDMARK worlds carry a coined proper
    /// name flavored by their biome: the lone giant (the system IS that planet), ringed worlds
    /// (the sky band earns a real name) and twins (one stem, two endings). Assigned after the
    /// ring roll because rings decide landmark status. The start planet is proper-named later
    /// by the server via EnsureStartPlanetProperName (only it knows the pick).</summary>
    private static void NamePlanet(SystemDraft draft, StarSystem system, CelestialBody planet, int p)
    {
        var archetype = draft.Archetype;
        var nameRng = draft.NameRng;
        var usedNames = draft.UsedNames;
        if (archetype == SystemArchetype.TwinWorlds && p <= 1)
        {
            if (p == 0)
            {
                draft.TwinNames = NameGenerator.TwinPair(nameRng);
                while (!usedNames.Add(draft.TwinNames.A) || !usedNames.Add(draft.TwinNames.B))
                {
                    draft.TwinNames = NameGenerator.TwinPair(nameRng);
                }
            }

            planet.Name = p == 0 ? draft.TwinNames.A : draft.TwinNames.B;
            draft.ProperNamed.Add(planet.Id);
        }
        else if (archetype == SystemArchetype.LoneGiant || planet.RingSeed != 0)
        {
            planet.Name = Unique(usedNames, () => NameGenerator.PlanetProper(nameRng, planet.PlanetType));
            draft.ProperNamed.Add(planet.Id);
        }
        else
        {
            planet.Name = draft.CatalogStyle
                ? $"{system.Name} {(char)('b' + p)}"
                : $"{system.Name} {NameGenerator.Roman(p + 1)}";
        }
    }

    /// <summary>The moons of planet <paramref name="p"/>: the count (one body-rng roll), then per moon its name (name
    /// rng for a landmark world's moons) BEFORE its type (body rng) — the initializer order is the draw order.</summary>
    private void AddMoons(SystemDraft draft, StarSystem system, CelestialBody planet, int p)
    {
        int i = draft.Index;
        var rng = draft.Rng;
        var nameRng = draft.NameRng;
        int moonCap = System.Math.Max(0, _desc.MoonsPerPlanetMax);
        int moons = draft.Archetype switch
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
                // Moons of a proper-named landmark world get short coined names of their own
                // (Mars/Phobos/Deimos feel); designation planets keep lettered satellites (#678).
                Name = draft.ProperNamed.Contains(planet.Id)
                    ? Unique(draft.UsedNames, () => NameGenerator.Moon(nameRng))
                    : $"{planet.Name}-{(char)('a' + m)}",
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

    /// <summary>Twin Worlds (#549): size the second twin like the first — BiasToward inverts the size mapping,
    /// so both land on (almost) the same circumference despite their independent id hashes. Sized in THIS world's
    /// sizes (#2691: from generation 26 they are seeded by the world; older generations keep the classic ones).</summary>
    private void SizeTwins(SystemDraft draft, StarSystem system)
    {
        if (draft.Archetype != SystemArchetype.TwinWorlds)
        {
            return;
        }

        var twins = system.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
        if (twins.Count == 2)
        {
            int target = WorldConstants.CircumferenceFor(
                twins[0].Id, WorldConstants.WorldSizeClass.Planet, 0f, _seed, _desc.TerrainGeneration);
            twins[1].SizeBias = WorldConstants.BiasToward(
                twins[1].Id, WorldConstants.WorldSizeClass.Planet, target, _seed, _desc.TerrainGeneration);
        }
    }

    /// <summary>A few large, landable asteroids per system: walkable "asteroid" worlds you can land on with the
    /// ship or on an EVA, each sized deterministically by its id (CircumferenceFor → Asteroid class). The
    /// small mineable rocks spawn separately as space entities at any body. (One rng draw, like the old
    /// single-belt gate, so existing systems' stations/wrecks downstream stay put.)
    /// Each rock also rolls its own FAMILY (#515) — stony, metallic, icy, carbonaceous or crystalline —
    /// so a system's asteroids differ in surface, temperature and what they're worth mining.</summary>
    private void AddAsteroids(SystemDraft draft, StarSystem system, int planets)
    {
        int i = draft.Index;
        var rng = draft.Rng;
        var nameRng = draft.NameRng;
        int asteroidCount = draft.Archetype switch
        {
            SystemArchetype.LoneGiant => rng.Range(1, 2),
            SystemArchetype.Swarm => rng.Range(2, 4),
            SystemArchetype.Belt => rng.Range(5, 8), // the belt IS the system
            SystemArchetype.Desolate => rng.Range(0, 1),
            SystemArchetype.PirateHaven => rng.Range(3, 5), // cover for ambushes
            _ => 2 + (rng.NextDouble() < 0.5 ? 1 : 0), // Standard/Hub/Twin: 2 or 3, the legacy draw
        };
        // Belt layout (#683, worlds created with AsteroidBelts): the system's asteroids share 1–2
        // orbit annuli — a real belt — instead of scattering across the whole disc (which regularly
        // parked them inside a planet's orbit lane). Geometry comes from its own Hash01 salts
        // (the 8xx series belongs to belts), NEVER from rng, and the flag defaults off — so every
        // pre-belt save keeps the legacy DiscPoint scatter byte-identically.
        var beltRadii = _desc.AsteroidBelts ? BeltRadii(i, system, planets, asteroidCount) : null;
        for (int a = 0; a < asteroidCount; a++)
        {
            var (ax, az) = beltRadii is { Count: > 0 }
                ? BeltPoint(i, a, asteroidCount, beltRadii)
                : DiscPoint(i, planets, 310 + a);
            system.Bodies.Add(new CelestialBody
            {
                Id = $"{system.Id}-a{a}",
                // Coined rock names (#678) — locale-neutral; the map pairs them with the localized
                // "Asteroid field" kind label, so no English needs to live inside the name anymore.
                Name = Unique(draft.UsedNames, () => NameGenerator.Asteroid(nameRng)),
                Kind = CelestialKind.AsteroidField,
                PlanetType = PickAsteroidType(i, a),
                SystemId = system.Id,
                SystemX = ax,
                SystemZ = az,
                OrbitPeriodDays = OrbitSign(i, 350 + a, 1) * (0.6f + Hash01(i, 350 + a, 2) * 2.4f), // 0.6..3 d
                ParentId = string.Empty, // a free body on the disc → orbits the star
            });
        }
    }

    /// <summary>Stations: the per-world frequency gate decides whether a system has ANY station at all (Rare by
    /// default ⇒ most systems have none). When it fires the system gets 1–3, each hanging over a DISTINCT
    /// planet and named after it ("&lt;planet&gt; Station") so they're attributable. A second station is rare and
    /// a third very rare. Count + planet picks come from a SEPARATE hash (not rng), so adding stations never
    /// disturbs the wreck rng draw below — only the stations themselves change for existing universes.
    /// The archetype overrides the gate where it defines the system's character: a Hub always has
    /// stations, Desolate/Pirate space never does, a Lone Giant/Belt system tops out at one. The
    /// gate draw itself ALWAYS happens so Standard systems keep the legacy rng sequence.</summary>
    private void AddStations(SystemDraft draft, StarSystem system)
    {
        int i = draft.Index;
        var archetype = draft.Archetype;
        double stationGate = draft.Rng.NextDouble();
        bool anyStations = archetype switch
        {
            SystemArchetype.Hub => _desc.SpaceStations != Frequency.Off,
            SystemArchetype.Desolate or SystemArchetype.PirateHaven => false,
            _ => stationGate < _desc.SpaceStations.Probability(),
        };
        if (!anyStations)
        {
            return;
        }

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
            AddStation(draft, system, systemPlanets[order[s]], s);
        }
    }

    /// <summary>Station <paramref name="s"/> of the system, hanging over <paramref name="planet"/>.</summary>
    private void AddStation(SystemDraft draft, StarSystem system, CelestialBody planet, int s)
    {
        var nameRng = draft.NameRng;
        var usedNames = draft.UsedNames;

        // Hub capital (#678): the planet under a Hub's first station is the system's trade
        // heart — it earns a proper name, and its station a coined port name ("Port Halvek")
        // instead of the attributive default. Its lettered moons follow the rename.
        string stationName;
        if (draft.Archetype == SystemArchetype.Hub && s == 0)
        {
            if (draft.ProperNamed.Add(planet.Id))
            {
                RenameWithMoons(system, planet, Unique(usedNames, () => NameGenerator.PlanetProper(nameRng, planet.PlanetType)));
            }

            stationName = Unique(usedNames, () => NameGenerator.Port(nameRng));
        }
        else
        {
            stationName = $"{planet.Name} Station";
        }

        float ang = Hash01(draft.Index, 322, s) * Tau;
        float sx = planet.SystemX + StationOrbit * System.MathF.Cos(ang);
        float sz = planet.SystemZ + StationOrbit * System.MathF.Sin(ang);
        (sx, sz) = SeparateFromBodies(system, sx, sz); // never park a station inside a planet/moon/asteroid (B29)
        system.Bodies.Add(new CelestialBody
        {
            Id = s == 0 ? $"{system.Id}-st" : $"{system.Id}-st{s}", // keep legacy id for the first
            Name = stationName,
            Kind = CelestialKind.SpaceStation,
            SystemId = system.Id,
            SystemX = sx,
            SystemZ = sz,
        });
    }

    /// <summary>Wreck odds follow the archetype's story: lawless/derelict space is littered with them,
    /// patrolled hub space is mostly cleaned up. Standard multiplier 1.0 = the legacy chance.</summary>
    private void AddWreck(SystemDraft draft, StarSystem system, int planets)
    {
        var nameRng = draft.NameRng;
        double wreckChance = _desc.Wrecks.Probability() * draft.Archetype switch
        {
            SystemArchetype.Belt or SystemArchetype.Desolate => 1.5,
            SystemArchetype.PirateHaven => 2.0,
            SystemArchetype.Hub => 0.5,
            _ => 1.0,
        };
        if (draft.Rng.NextDouble() < System.Math.Min(0.95, wreckChance))
        {
            var (wx, wz) = DiscPoint(draft.Index, planets, 303);
            (wx, wz) = SeparateFromBodies(system, wx, wz); // a wreck shouldn't clip a body either
            // A wreck is a dead ship, so it carries a coined ship name (#678) — the old baked-in
            // English "Wreck near …" could never be localized; the kind label is the client's job.
            system.Bodies.Add(new CelestialBody { Id = $"{system.Id}-w", Name = Unique(draft.UsedNames, () => NameGenerator.Ship(nameRng)), Kind = CelestialKind.Wreck, SystemId = system.Id, SystemX = wx, SystemZ = wz });
        }
    }
}
