using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Peaceful NPC trader traffic — ambient ships that warp in, fly, dock and depart. They are
/// invulnerable, non-targetable scenery rendered through the remote-ship path.</summary>
public sealed class SpaceTraderTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpaceTraderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_trader_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.Off; // traders are independent of combat
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TrafficLevel_IsDeterministicPerSystem()
    {
        var server = NewServer("traffic", out var repo);
        using (repo)
        {
            var sys = server.Galaxy.Systems.First().Id;
            var a = server.TrafficLevelForTest(sys);
            var b = server.TrafficLevelForTest(sys);
            Assert.Equal(a, b); // stable from seed + system id, so no persistence is needed
            Assert.Contains(a, new[] { "None", "Rare", "Often" });
        }
    }

    [Fact]
    public void EmptySystemId_HasNoTraffic()
    {
        var server = NewServer("notraffic", out var repo);
        using (repo)
        {
            Assert.Equal("None", server.TrafficLevelForTest(string.Empty));
        }
    }

    [Fact]
    public void FreshInstance_IsNotInstantlyBusy()
    {
        var server = NewServer("schedule", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));
            Assert.Equal(0, server.TraderCountForTest("Pilot")); // first arrival is scheduled out, not on entry
        }
    }

    [Fact]
    public void SpawnTrader_AddsAFlyingTrader_ThatIsNeverATargetableEntity()
    {
        var server = NewServer("spawn", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            int entitiesBefore = server.SpaceEntitiesFor("Pilot").Count();
            Assert.True(server.SpawnTraderForTest("Pilot"));
            Assert.True(server.TraderCountForTest("Pilot") >= 1);

            // Peaceful + invulnerable by design: a trader is NEVER a combat entity (can't be locked or shot)
            // and never hostile — it only ever rides the remote-ship pose path.
            var entities = server.SpaceEntitiesFor("Pilot");
            Assert.Equal(entitiesBefore, entities.Count());
            Assert.DoesNotContain(entities, e => e.Hostile);
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
