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
    public void PlanetTypeFrequencies_AreRespected()
    {
        // Only allow "ice" planets; no rocky/lava should appear.
        var desc = new WorldDescription
        {
            StarSystemCount = 10,
            PlanetsPerSystemMin = 3,
            PlanetsPerSystemMax = 5,
            PlanetTypeFrequencies = new Dictionary<string, Frequency> { ["ice"] = Frequency.Normal },
        };

        var galaxy = new UniverseGenerator(7, desc, _content).Generate();
        var planetTypes = galaxy.AllBodies()
            .Where(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon)
            .Select(b => b.PlanetType)
            .Distinct()
            .ToList();

        Assert.NotEmpty(planetTypes);
        Assert.All(planetTypes, t => Assert.Equal("ice", t));
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
