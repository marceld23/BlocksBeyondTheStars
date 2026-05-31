using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Spacecraft.Shared.State;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class MissionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_mission_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer server, LoopbackClientTransport client, SqliteWorldRepository repo) Start(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return (server, client, repo);
    }

    [Fact]
    public void MineMission_TracksProgressAsBlocksAreMined()
    {
        var (server, client, repo) = Start("mine");
        using (repo)
        {
            client.Send(NetCodec.Encode(new AcceptMissionIntent { MissionId = "first_iron" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            var p = server.Sessions[1].State;
            int baseY = (int)System.Math.Floor(p.Position.Y) - 1;
            var oreId = _content.GetBlock("iron_ore")!.NumericId;

            for (int x = 0; x < 3; x++)
            {
                var pos = new Vector3i(x, baseY, 0);
                server.World.SetBlock(pos, oreId);
                client.Send(NetCodec.Encode(new MineBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z }), DeliveryMode.ReliableOrdered);
            }

            server.Tick(0.1);

            var pr = p.Missions.Single(m => m.MissionId == "first_iron");
            Assert.Equal(MissionStatus.Active, pr.Status);
            Assert.Equal(3, pr.ObjectiveProgress[0]); // 3 of 10 iron ore mined
        }
    }

    [Fact]
    public void DeliverMission_ConsumesItems_AndPaysReward()
    {
        var (server, client, repo) = Start("deliver");
        using (repo)
        {
            var p = server.Sessions[1].State;
            p.Inventory.Add("cable", 5, 99);

            client.Send(NetCodec.Encode(new AcceptMissionIntent { MissionId = "supply_cables" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Send(NetCodec.Encode(new TurnInMissionIntent { MissionId = "supply_cables" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.Equal(0, p.Inventory.CountOf("cable"));          // delivered (consumed)
            Assert.Equal(1, p.Inventory.CountOf("data_fragment"));  // reward granted
            // repeatable -> progress removed so it can be accepted again
            Assert.DoesNotContain(p.Missions, m => m.MissionId == "supply_cables");
        }
    }

    [Fact]
    public void DeliverMission_Rejected_WhenObjectivesIncomplete()
    {
        var (server, client, repo) = Start("incomplete");
        using (repo)
        {
            MissionResult? result = null;
            client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is MissionResult r) result = r; };

            client.Send(NetCodec.Encode(new AcceptMissionIntent { MissionId = "supply_cables" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Send(NetCodec.Encode(new TurnInMissionIntent { MissionId = "supply_cables" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Poll();

            Assert.NotNull(result);
            Assert.False(result!.Success); // no cables delivered
        }
    }

    [Fact]
    public void PlayerCreatedMission_DepositsReward_AndPaysOutOnTurnIn()
    {
        var (server, client, repo) = Start("created");
        using (repo)
        {
            var p = server.Sessions[1].State;
            p.Inventory.Add("iron_plate", 1, 99); // reward to deposit
            p.Inventory.Add("iron_ore", 3, 99);   // to satisfy a Collect objective

            var create = new CreateMissionIntent
            {
                Title = "Gather ore",
                Description = "Collect some iron ore.",
                Objectives = new[] { new NetMissionObjective { Type = "Collect", Target = "iron_ore", Required = 3 } },
                Rewards = new[] { new NetReward { Item = "iron_plate", Count = 1 } },
            };

            string? createdId = null;
            client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is MissionResult r && r.Success && r.MissionId.StartsWith("pm_")) createdId = r.MissionId; };

            client.Send(NetCodec.Encode(create), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Poll();

            Assert.NotNull(createdId);
            Assert.Equal(0, p.Inventory.CountOf("iron_plate")); // deposited into the reward depot

            // Same player accepts and completes (Collect is satisfied by the 3 iron ore on hand).
            client.Send(NetCodec.Encode(new AcceptMissionIntent { MissionId = createdId! }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Send(NetCodec.Encode(new TurnInMissionIntent { MissionId = createdId! }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.Equal(1, p.Inventory.CountOf("iron_plate")); // paid out from the depot
            Assert.Equal(3, p.Inventory.CountOf("iron_ore"));   // Collect does not consume
        }
    }

    [Fact]
    public void MissionProgress_PersistsThroughRepository()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "persist"));
        repo.Initialize();

        var player = new PlayerState { PlayerId = "p1", Name = "p1" };
        player.Missions.Add(new MissionProgress
        {
            MissionId = "first_iron",
            Status = MissionStatus.Active,
            ObjectiveProgress = new List<int> { 4 },
        });
        repo.SavePlayer(player);

        var loaded = repo.LoadPlayer("p1")!;
        var pr = Assert.Single(loaded.Missions);
        Assert.Equal("first_iron", pr.MissionId);
        Assert.Equal(4, pr.ObjectiveProgress[0]);
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
