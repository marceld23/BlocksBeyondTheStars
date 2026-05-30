using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class UniverseTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public UniverseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_uni_" + Guid.NewGuid().ToString("N"));
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
