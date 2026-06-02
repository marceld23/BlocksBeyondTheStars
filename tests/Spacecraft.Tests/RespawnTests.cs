using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class RespawnTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public RespawnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_respawn_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer server, LoopbackClientTransport client, SqliteWorldRepository repo) Start(GameRules rules, string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = rules, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return (server, client, repo);
    }

    [Fact]
    public void Death_RespawnsAtHealTank_AndKeepsInventory_WhenConfigured()
    {
        var rules = new GameRules { KeepInventoryOnDeath = true }; // Survival, oxygen on
        var (server, _, repo) = Start(rules, "keep");
        using (repo)
        {
            var p = server.Sessions[1].State;
            p.Inventory.Add("iron_ore", 10, 99);
            p.AboardShip = false;
            p.Health = 0f;

            server.Tick(0.1); // death detected -> respawn

            Assert.Equal(100f, p.Health);
            Assert.True(p.AboardShip);
            Assert.Equal(p.RespawnPoint.X, p.Position.X);
            Assert.Equal(p.RespawnPoint.Y, p.Position.Y);
            Assert.Equal(10, p.Inventory.CountOf("iron_ore")); // kept
            Assert.Empty(repo.ListContainers("rocky"));        // no salvage capsule
        }
    }

    [Fact]
    public void Death_DropsSalvageCapsule_ButKeepsTools_WhenPenaltyNormal()
    {
        var rules = new GameRules { DeathPenalty = DeathPenalty.Normal, KeepInventoryOnDeath = false };
        var (server, _, repo) = Start(rules, "salvage");
        using (repo)
        {
            var p = server.Sessions[1].State;
            p.Inventory.Add("iron_ore", 10, 99); // material -> should be salvaged
            // basic_drill is already in slot 0 from the starter kit (a tool -> kept)
            p.AboardShip = false;
            p.Health = 0f;

            server.Tick(0.1);

            Assert.Equal(100f, p.Health);
            Assert.Equal(0, p.Inventory.CountOf("iron_ore")); // dropped
            Assert.Equal(1, p.Inventory.CountOf("basic_drill")); // tool kept

            var capsules = repo.ListContainers(server.ActiveLocationId); // keyed by body id, not planet type
            Assert.Single(capsules);
            Assert.Equal("salvage_capsule", capsules[0].Kind);
            Assert.Contains(capsules[0].Items, s => s.Item == "iron_ore" && s.Count == 10);
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
