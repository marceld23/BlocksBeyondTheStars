using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.State;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class AdminCheatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AdminCheatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_cheat_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer server, LoopbackClientTransport client) Start(GameRules rules, string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = rules };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Creator" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return (server, client);
    }

    [Fact]
    public void FirstPlayer_BecomesWorldAdmin()
    {
        var (server, _) = Start(new GameRules { AdminCheats = true }, "wa");
        Assert.Equal(PlayerRole.WorldAdmin, server.Sessions[1].State.Role);
    }

    [Fact]
    public void GiveItem_Works_ForAdmin_WhenCheatsEnabled()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true }; // survival + cheats on
        var (server, client) = Start(rules, "give");
        var p = server.Sessions[1].State;

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "give_item", StringArg = "titanium_plate", IntArg = 5 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(5, p.Inventory.CountOf("titanium_plate"));
    }

    [Fact]
    public void Cheat_Rejected_WhenCheatsDisabled()
    {
        var rules = new GameRules { AdminCheats = false }; // cheats off
        var (server, client) = Start(rules, "nocheat");

        ActionRejected? rejected = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is ActionRejected r) rejected = r; };

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "give_item", StringArg = "titanium_plate", IntArg = 5 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Equal(0, server.Sessions[1].State.Inventory.CountOf("titanium_plate"));
        Assert.NotNull(rejected);
    }

    [Fact]
    public void Cheat_Rejected_ForNonAdmin()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "nonadmin");

        // Demote the player to a regular player, simulating a non-admin client.
        server.Sessions[1].State.Role = PlayerRole.Player;

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "give_item", StringArg = "titanium_plate", IntArg = 5 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(0, server.Sessions[1].State.Inventory.CountOf("titanium_plate"));
    }

    [Fact]
    public void Teleport_ToLocation_MovesPlayer()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "tp");

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "teleport_to_location", X = 100, Y = 70, Z = -50 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var pos = server.Sessions[1].State.Position;
        Assert.Equal(100f, pos.X);
        Assert.Equal(70f, pos.Y);
        Assert.Equal(-50f, pos.Z);
    }

    [Fact]
    public void GodMode_PreventsDeath()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "god");
        var p = server.Sessions[1].State;

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "godmode" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        p.AboardShip = false;
        p.Health = 0f;
        server.Tick(0.1);

        Assert.Equal(100f, p.Health); // invulnerable, restored rather than dead
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
