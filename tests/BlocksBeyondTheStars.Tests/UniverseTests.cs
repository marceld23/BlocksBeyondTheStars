// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

public sealed class UniverseTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public UniverseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_uni_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private static IEnumerable<string> BodyKey(Galaxy g) =>
        g.AllBodies().Select(b => $"{b.Id}:{b.Kind}:{b.PlanetType}");

    [Fact]
    public void Universe_IsDeterministic_ForSameSeedAndDescription()
    {
        var desc = new WorldDescription { StarSystemCount = 6 };
        var a = new UniverseGenerator(42, desc, _content).Generate();
        var b = new UniverseGenerator(42, desc, _content).Generate();

        Assert.Equal(BodyKey(a), BodyKey(b));
        Assert.Equal(6, a.Systems.Count);
    }

    [Fact]
    public void BodyPositions_AreDeterministic_AndSpreadOut()
    {
        var desc = new WorldDescription { StarSystemCount = 4, PlanetsPerSystemMin = 3, PlanetsPerSystemMax = 5 };
        var a = new UniverseGenerator(42, desc, _content).Generate();
        var b = new UniverseGenerator(42, desc, _content).Generate();

        // Same seed → identical system-space coordinates.
        var pa = a.AllBodies().Select(x => (x.SystemX, x.SystemZ)).ToList();
        var pb = b.AllBodies().Select(x => (x.SystemX, x.SystemZ)).ToList();
        Assert.Equal(pa, pb);

        foreach (var sys in a.Systems)
        {
            var planets = sys.Bodies.Where(x => x.Kind == CelestialKind.Planet).ToList();
            // Every planet sits off the star, and no two planets share a position.
            Assert.All(planets, p => Assert.True(p.SystemX * p.SystemX + p.SystemZ * p.SystemZ > 1f));
            var distinct = planets.Select(p => (p.SystemX, p.SystemZ)).Distinct().Count();
            Assert.Equal(planets.Count, distinct);
        }
    }

    [Fact]
    public void OrbitParameters_AreDeterministic_InBandPerKind_AndParentedCorrectly()
    {
        var desc = new WorldDescription { StarSystemCount = 30, PlanetsPerSystemMin = 3, PlanetsPerSystemMax = 6, MoonsPerPlanetMin = 1, MoonsPerPlanetMax = 3 };
        var a = new UniverseGenerator(42, desc, _content).Generate();
        var b = new UniverseGenerator(42, desc, _content).Generate();

        // Same seed → identical orbital parameters (deterministic).
        var pa = a.AllBodies().Select(x => (x.OrbitPeriodDays, x.ParentId)).ToList();
        var pb = b.AllBodies().Select(x => (x.OrbitPeriodDays, x.ParentId)).ToList();
        Assert.Equal(pa, pb);

        foreach (var body in a.AllBodies())
        {
            float mag = System.MathF.Abs(body.OrbitPeriodDays);
            switch (body.Kind)
            {
                case CelestialKind.Planet:
                    Assert.InRange(mag, 6f, 40f);
                    Assert.Equal(string.Empty, body.ParentId); // orbits the star
                    break;
                case CelestialKind.Moon:
                    Assert.InRange(mag, 0.4f, 2.5f);
                    // A moon orbits its parent planet, which exists in the same system.
                    Assert.False(string.IsNullOrEmpty(body.ParentId));
                    Assert.Contains(a.AllBodies(), p => p.Id == body.ParentId && p.Kind == CelestialKind.Planet);
                    break;
                case CelestialKind.AsteroidField:
                    Assert.InRange(mag, 0.6f, 3f);
                    Assert.Equal(string.Empty, body.ParentId);
                    break;
            }
        }

        // Some bodies are retrograde (negative period) — the system mixes both directions.
        Assert.Contains(a.AllBodies(), x => x.OrbitPeriodDays < 0f);
        Assert.Contains(a.AllBodies(), x => x.OrbitPeriodDays > 0f);

        // Each system has its own rhythm: two systems' planet-period sets are not identical.
        var sys0 = a.Systems[0].Bodies.Where(x => x.Kind == CelestialKind.Planet).Select(x => x.OrbitPeriodDays).ToList();
        var sys1 = a.Systems[1].Bodies.Where(x => x.Kind == CelestialKind.Planet).Select(x => x.OrbitPeriodDays).ToList();
        Assert.NotEqual(sys0, sys1);
    }

    [Fact]
    public void StationsAndWrecks_NeverSpawnInsideABody()
    {
        // Generate plenty of systems so stations + wrecks actually appear, then assert each free-floater keeps
        // its kind-appropriate clearance from every planet/moon/asteroid (B29 — no station stuck in a moon).
        var desc = new WorldDescription { StarSystemCount = 60, PlanetsPerSystemMin = 2, PlanetsPerSystemMax = 5 };
        var galaxy = new UniverseGenerator(7, desc, _content).Generate();

        int floaters = 0;
        foreach (var sys in galaxy.Systems)
        {
            foreach (var f in sys.Bodies.Where(b => b.Kind is CelestialKind.SpaceStation or CelestialKind.Wreck))
            {
                floaters++;
                foreach (var body in sys.Bodies)
                {
                    if (body.Kind is CelestialKind.SpaceStation or CelestialKind.Wreck)
                    {
                        continue;
                    }

                    float dx = f.SystemX - body.SystemX, dz = f.SystemZ - body.SystemZ;
                    float dist = System.MathF.Sqrt(dx * dx + dz * dz);
                    float need = body.Kind switch
                    {
                        CelestialKind.Planet => 300f,
                        CelestialKind.Moon => 185f,
                        CelestialKind.AsteroidField => 150f,
                        _ => 110f,
                    };
                    Assert.True(dist >= need - 1f,
                        $"{f.Kind} {f.Id} only {dist:0} units from {body.Kind} {body.Id} (needs {need})");
                }
            }
        }

        Assert.True(floaters > 0, "expected some stations/wrecks to be generated");
    }

    [Fact]
    public void Stations_AreOnePerThreeMax_OverDistinctPlanets_NamedAfterThem()
    {
        // Frequent so most systems get stations, across many systems so the 1–3 spread shows up.
        var desc = new WorldDescription
        {
            StarSystemCount = 120,
            PlanetsPerSystemMin = 3,
            PlanetsPerSystemMax = 6,
            SpaceStations = Frequency.Frequent,
        };
        var galaxy = new UniverseGenerator(7, desc, _content).Generate();

        bool sawTwo = false, sawThree = false;
        foreach (var sys in galaxy.Systems)
        {
            var stations = sys.Bodies.Where(b => b.Kind == CelestialKind.SpaceStation).ToList();
            if (stations.Count == 0) continue;

            // Never more than three, and never more than the system has planets.
            int planetCount = sys.Bodies.Count(b => b.Kind == CelestialKind.Planet);
            Assert.InRange(stations.Count, 1, 3);
            Assert.True(stations.Count <= planetCount);
            if (stations.Count == 2) sawTwo = true;
            if (stations.Count == 3) sawThree = true;

            // Each station is named after a planet that exists in this system, and no two share a planet.
            var planetNames = sys.Bodies.Where(b => b.Kind == CelestialKind.Planet).Select(p => p.Name).ToHashSet();
            var claimedPlanets = new HashSet<string>();
            foreach (var st in stations)
            {
                Assert.EndsWith(" Station", st.Name);
                string planetName = st.Name[..^" Station".Length];
                Assert.Contains(planetName, planetNames);
                Assert.True(claimedPlanets.Add(planetName), $"two stations share planet {planetName}");
            }
        }

        // Multi-station systems are possible but rare; with 120 Frequent systems we expect to see both 2 and 3.
        Assert.True(sawTwo, "expected at least one system with two stations");
        Assert.True(sawThree, "expected at least one system with three stations");
    }

    [Fact]
    public void Procedural_generation_never_collides_with_the_reserved_finale_area()
    {
        // The finale system + body ids are RESERVED for the hand-built Guardian core (added to the galaxy only
        // when the story reveals it — never by the procedural generator). This proves that, across many seeds
        // and large galaxies, the random world/station generator can never emit a system or body that collides
        // with that reserved namespace — i.e. it can never "accidentally spawn the finale area".
        const string reserved = SvGameServer.GuardianFinaleSystemId; // "guardian_finale"
        foreach (long seed in new long[] { 1, 7, 42, 99, 1234, 2026, 555_555 })
        {
            var desc = new WorldDescription { StarSystemCount = 150, PlanetsPerSystemMin = 2, PlanetsPerSystemMax = 6 };
            var galaxy = new UniverseGenerator(seed, desc, _content).Generate();

            Assert.DoesNotContain(galaxy.Systems, s => s.Id == reserved || s.Id.StartsWith(reserved));
            Assert.DoesNotContain(galaxy.AllBodies(),
                b => b.Id == SvGameServer.GuardianCoreBodyId || b.Id.StartsWith(reserved) || b.SystemId == reserved);
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentUniverses()
    {
        var desc = new WorldDescription { StarSystemCount = 6 };
        var a = new UniverseGenerator(1, desc, _content).Generate();
        var b = new UniverseGenerator(2, desc, _content).Generate();
        Assert.NotEqual(BodyKey(a), BodyKey(b));
    }

    [Fact]
    public void PlanetTypeFrequencies_LayerOntoTheDataWeights()
    {
        // #471: a per-type override adjusts only ITS OWN row — the rest of the mix survives. (The old
        // behaviour replaced the entire weight table: one touched slider collapsed the galaxy to one type.)
        var desc = new WorldDescription
        {
            StarSystemCount = 30,
            PlanetsPerSystemMin = 3,
            PlanetsPerSystemMax = 5,
            PlanetTypeFrequencies = new Dictionary<string, Frequency> { ["ice"] = Frequency.Frequent },
        };

        var galaxy = new UniverseGenerator(7, desc, _content).Generate();
        var types = galaxy.AllBodies()
            .Where(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon)
            .Select(b => b.PlanetType)
            .ToList();

        Assert.NotEmpty(types);
        Assert.Contains("ice", types);                     // the boosted type is there…
        Assert.True(types.Distinct().Count() > 3,          // …and the galaxy did NOT collapse to it
            $"one touched slider must not collapse the mix (got {types.Distinct().Count()} distinct types)");
    }

    [Fact]
    public void PlanetTypeFrequency_Off_RemovesOnlyThatType()
    {
        // #471: sliding one type to Off retires just that type — it must NOT degenerate to all-rocky.
        var desc = new WorldDescription
        {
            StarSystemCount = 30,
            PlanetsPerSystemMin = 3,
            PlanetsPerSystemMax = 5,
            PlanetTypeFrequencies = new Dictionary<string, Frequency> { ["rocky"] = Frequency.Off },
        };

        var galaxy = new UniverseGenerator(7, desc, _content).Generate();
        var types = galaxy.AllBodies()
            .Where(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon)
            .Select(b => b.PlanetType)
            .ToList();

        Assert.NotEmpty(types);
        Assert.DoesNotContain("rocky", types);
        Assert.True(types.Distinct().Count() > 3, "the remaining mix must survive a single Off row");
    }

    [Fact]
    public void PlanetTypeFrequencies_NeverAdmitNonSelectableTypes()
    {
        // #471 (was PT/F2): the override path used to skip the Selectable filter — orbital_station
        // "planets" generated as pure void. The filter now applies to overrides too.
        var desc = new WorldDescription
        {
            StarSystemCount = 20,
            PlanetsPerSystemMin = 3,
            PlanetsPerSystemMax = 5,
            PlanetTypeFrequencies = new Dictionary<string, Frequency> { ["orbital_station"] = Frequency.Frequent },
        };

        var galaxy = new UniverseGenerator(42, desc, _content).Generate();
        var types = galaxy.AllBodies()
            .Where(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon)
            .Select(b => b.PlanetType)
            .ToList();

        Assert.NotEmpty(types);
        Assert.DoesNotContain("orbital_station", types);
    }

    [Fact]
    public void PlanetsPerSystem_StayWithinConfiguredRange()
    {
        var desc = new WorldDescription { StarSystemCount = 8, PlanetsPerSystemMin = 2, PlanetsPerSystemMax = 4 };
        var galaxy = new UniverseGenerator(99, desc, _content).Generate();
        foreach (var sys in galaxy.Systems)
        {
            int planets = sys.Bodies.Count(b => b.Kind == CelestialKind.Planet);
            Assert.InRange(planets, 2, 4);
        }
    }

    // --- Planetary rings (#596) ---

    [Fact]
    public void Rings_AreDeterministic_PlanetsOnly_AndReasonablyDistributed()
    {
        var desc = new WorldDescription { StarSystemCount = 120 };
        var a = new UniverseGenerator(42, desc, _content).Generate();
        var b = new UniverseGenerator(42, desc, _content).Generate();

        // Same seed → the same planets ring, with the same style seed.
        Assert.Equal(
            a.AllBodies().Select(x => (x.Id, x.RingSeed)),
            b.AllBodies().Select(x => (x.Id, x.RingSeed)));

        // Rings are a planet-only feature; moons/asteroids/stations/wrecks never carry one.
        Assert.All(
            a.AllBodies().Where(x => x.Kind != CelestialKind.Planet),
            x => Assert.Equal(0, x.RingSeed));

        // Some planets ring, most don't (base chance ~10-30 % depending on size/type; deliberately
        // rare — the guaranteed start-planet ring covers the showcase).
        var planets = a.AllBodies().Where(x => x.Kind == CelestialKind.Planet).ToList();
        int ringed = planets.Count(x => x.RingSeed != 0);
        Assert.InRange(ringed / (double)planets.Count, 0.05, 0.25);
        Assert.All(planets.Where(x => x.RingSeed != 0), x => Assert.InRange(x.RingSeed, 1, 1_000_000));
    }

    [Fact]
    public void Rings_StartPlanetGuarantee_RingsOnlyWhatItShould()
    {
        // A ring-less start planet gains a deterministic ring...
        var bare = new CelestialBody { Id = "sys5-p5", Kind = CelestialKind.Planet };
        UniverseGenerator.EnsureStartPlanetRings(bare);
        Assert.InRange(bare.RingSeed, 1, 1_000_000);
        int first = bare.RingSeed;
        var again = new CelestialBody { Id = "sys5-p5", Kind = CelestialKind.Planet };
        UniverseGenerator.EnsureStartPlanetRings(again);
        Assert.Equal(first, again.RingSeed); // ...the SAME ring on every server restart.

        // A natural ring stays untouched, and non-planets never ring.
        var ringed = new CelestialBody { Id = "sys0-p1", Kind = CelestialKind.Planet, RingSeed = 4242 };
        UniverseGenerator.EnsureStartPlanetRings(ringed);
        Assert.Equal(4242, ringed.RingSeed);
        var moon = new CelestialBody { Id = "sys0-p1-m0", Kind = CelestialKind.Moon };
        UniverseGenerator.EnsureStartPlanetRings(moon);
        Assert.Equal(0, moon.RingSeed);
    }

    // --- System archetype variance (#546/#549) ---

    private static WorldDescription VarianceDesc(int systems = 150) => new()
    {
        StarSystemCount = systems,
        SystemVariance = true,
        SpaceStations = Frequency.Rare,
    };

    [Fact]
    public void VarianceOff_LeavesEveryBodyUnbiased()
    {
        // A pre-variance save regenerates its galaxy with this exact path — no body may carry a size bias
        // (bias 0 keeps CircumferenceFor bit-identical, so existing terrain stays valid).
        var galaxy = new UniverseGenerator(42, new WorldDescription { StarSystemCount = 20 }, _content).Generate();
        Assert.All(galaxy.AllBodies(), b => Assert.Equal(0f, b.SizeBias));
    }

    [Fact]
    public void Variance_IsDeterministic_ForSameSeed()
    {
        var a = new UniverseGenerator(42, VarianceDesc(30), _content).Generate();
        var b = new UniverseGenerator(42, VarianceDesc(30), _content).Generate();
        Assert.Equal(BodyKey(a), BodyKey(b));
        Assert.Equal(
            a.AllBodies().Select(x => (x.SystemX, x.SystemZ, x.SizeBias)),
            b.AllBodies().Select(x => (x.SystemX, x.SystemZ, x.SizeBias)));
    }

    [Fact]
    public void Variance_EveryArchetypeAppears_AndShapesItsSystems()
    {
        var desc = VarianceDesc(150);
        var galaxy = new UniverseGenerator(7, desc, _content).Generate();
        var seen = new HashSet<SystemArchetype>();

        for (int i = 0; i < galaxy.Systems.Count; i++)
        {
            var sys = galaxy.Systems[i];
            var archetype = SystemArchetypes.ForIndex(7, i);
            seen.Add(archetype);

            var planets = sys.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            int stations = sys.Bodies.Count(b => b.Kind == CelestialKind.SpaceStation);
            int asteroids = sys.Bodies.Count(b => b.Kind == CelestialKind.AsteroidField);

            switch (archetype)
            {
                case SystemArchetype.LoneGiant:
                    Assert.Single(planets);
                    Assert.True(planets[0].SizeBias >= 0.6f, $"{sys.Id}: giant bias {planets[0].SizeBias}");
                    Assert.InRange(sys.Bodies.Count(b => b.Kind == CelestialKind.Moon), 4, 8);
                    Assert.InRange(stations, 0, 1);
                    break;
                case SystemArchetype.Swarm:
                    Assert.InRange(planets.Count, 6, 9);
                    Assert.All(planets, p => Assert.True(p.SizeBias < 0f, $"{p.Id}: swarm worlds run small"));
                    foreach (var p in planets)
                    {
                        Assert.InRange(sys.Bodies.Count(b => b.Kind == CelestialKind.Moon && b.ParentId == p.Id), 0, 1);
                    }

                    break;
                case SystemArchetype.Belt:
                    Assert.InRange(planets.Count, 1, 3);
                    Assert.InRange(asteroids, 5, 8);
                    Assert.InRange(stations, 0, 1);
                    break;
                case SystemArchetype.Hub:
                    Assert.InRange(planets.Count, 3, 5);
                    Assert.InRange(stations, 1, 3); // a hub ALWAYS has stations (SpaceStations is not Off)
                    break;
                case SystemArchetype.Desolate:
                    Assert.InRange(planets.Count, 1, 2);
                    Assert.Equal(0, stations);
                    Assert.InRange(asteroids, 0, 1);
                    break;
                case SystemArchetype.PirateHaven:
                    Assert.Equal(0, stations);
                    Assert.InRange(asteroids, 3, 5);
                    break;
                case SystemArchetype.TwinWorlds:
                    Assert.Equal(2, planets.Count);
                    int c0 = WorldConstants.CircumferenceFor(planets[0].Id, WorldConstants.WorldSizeClass.Planet, planets[0].SizeBias);
                    int c1 = WorldConstants.CircumferenceFor(planets[1].Id, WorldConstants.WorldSizeClass.Planet, planets[1].SizeBias);
                    Assert.True(System.Math.Abs(c0 - c1) <= WorldConstants.ChunkSize,
                        $"{sys.Id}: twins sized {c0} vs {c1}");
                    break;
            }
        }

        // 150 systems at the table weights: every archetype shows up.
        foreach (SystemArchetype a in System.Enum.GetValues<SystemArchetype>())
        {
            Assert.Contains(a, seen);
        }
    }

    [Fact]
    public void Variance_HomeSystem_NeverRollsDesolateOrPirateHaven()
    {
        // The start system must stay friendly: a fresh save needs reachable trade + no forced pirate space.
        for (long seed = 1; seed <= 300; seed++)
        {
            var archetype = SystemArchetypes.ForIndex(seed, 0);
            Assert.NotEqual(SystemArchetype.Desolate, archetype);
            Assert.NotEqual(SystemArchetype.PirateHaven, archetype);
        }
    }

    [Fact]
    public void Variance_RespectsTheWorldSizePlanetCap()
    {
        // The world-size slider still caps planet counts (a "Klein" world stays small); the Lone Giant's
        // moons are the one deliberate exception to the moon slider (its identity), capped at 8.
        var desc = new WorldDescription
        {
            StarSystemCount = 60,
            SystemVariance = true,
            PlanetsPerSystemMax = 4,
            MoonsPerPlanetMax = 2,
        };
        var galaxy = new UniverseGenerator(11, desc, _content).Generate();
        foreach (var sys in galaxy.Systems)
        {
            Assert.InRange(sys.Bodies.Count(b => b.Kind == CelestialKind.Planet), 1, 4);
            foreach (var planet in sys.Bodies.Where(b => b.Kind == CelestialKind.Planet))
            {
                Assert.InRange(sys.Bodies.Count(b => b.Kind == CelestialKind.Moon && b.ParentId == planet.Id), 0, 8);
            }
        }
    }

    [Fact]
    public void Variance_NeverCollidesWithTheReservedFinaleArea()
    {
        const string reserved = SvGameServer.GuardianFinaleSystemId;
        foreach (long seed in new long[] { 1, 42, 2026 })
        {
            var galaxy = new UniverseGenerator(seed, VarianceDesc(150), _content).Generate();
            Assert.DoesNotContain(galaxy.Systems, s => s.Id == reserved || s.Id.StartsWith(reserved));
            Assert.DoesNotContain(galaxy.AllBodies(),
                b => b.Id == SvGameServer.GuardianCoreBodyId || b.Id.StartsWith(reserved) || b.SystemId == reserved);
        }
    }

    [Fact]
    public void Archetypes_DriveTrafficAndPirateFlags_OnALiveServer()
    {
        // The runtime consumers resolve the archetype from the seed the same way the generator does:
        // a Desolate system has no trade, a Hub is always busy, a Pirate Haven is always pirate space.
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "arch"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);

        var config = new ServerConfig { WorldName = "arch", Seed = 11, AutoSaveIntervalMinutes = 9999 };
        config.World.StarSystemCount = 32; // ServerConfig's default description has SystemVariance = true
        config.PlaceBanditCamps = false;   // keep world startup light — we only probe the per-system gates

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        bool sawDesolate = false, sawHub = false, sawPirate = false;
        for (int i = 0; i < 32; i++)
        {
            switch (SystemArchetypes.ForIndex(11, i))
            {
                case SystemArchetype.Desolate:
                    sawDesolate = true;
                    Assert.Equal("None", server.TrafficLevelForTest($"sys{i}"));
                    break;
                case SystemArchetype.Hub:
                    sawHub = true;
                    Assert.Equal("Often", server.TrafficLevelForTest($"sys{i}"));
                    break;
                case SystemArchetype.PirateHaven:
                    sawPirate = true;
                    Assert.True(server.BanditSystemForTest($"sys{i}"), $"sys{i} must be pirate space");
                    break;
            }
        }

        Assert.True(sawDesolate && sawHub && sawPirate,
            $"seed 11 / 32 systems should roll all three probed archetypes (desolate {sawDesolate}, hub {sawHub}, pirate {sawPirate})");
    }

    [Fact]
    public void Server_BuildsGalaxy_MarksStartVisited_AndServesStarMap()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "uni"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        StarMapData? map = null;
        client.PayloadReceived += p => { if (NetCodec.Decode(p) is StarMapData m) map = m; };

        var config = new ServerConfig { WorldName = "uni", Seed = 5, AutoSaveIntervalMinutes = 9999 };
        config.World.StarSystemCount = 5;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        Assert.Equal(5, server.Galaxy.Systems.Count);
        var active = server.Galaxy.FindBody(server.Metadata.ActiveLocationId);
        Assert.NotNull(active);
        Assert.Equal(GenerationStatus.Visited, active!.Status);

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Send(NetCodec.Encode(new RequestStarMap()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(map);
        Assert.Equal(5, map!.Systems.Length);
        Assert.Equal(server.Metadata.ActiveLocationId, map.ActiveLocationId);
    }

    [Fact]
    public void StartLocationStatus_PersistsAcrossRestart()
    {
        var config = new ServerConfig { WorldName = "persist", Seed = 5, AutoSaveIntervalMinutes = 9999 };
        config.World.StarSystemCount = 4;
        string activeId;

        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "persist")))
        {
            var link = new LoopbackLink();
            using var st = new LoopbackServerTransport(link);
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            activeId = server.Metadata.ActiveLocationId;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "persist")))
        {
            repo2.Initialize();
            var statuses = repo2.LoadLocationStatuses();
            Assert.True(statuses.ContainsKey(activeId));
            Assert.Equal("Visited", statuses[activeId]);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
