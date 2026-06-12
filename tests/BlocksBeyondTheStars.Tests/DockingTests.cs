using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Ship docking handshake, rule gating and lifecycle (M18 / `anf_space_flight.md` §13).</summary>
public sealed class DockingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DockingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_dock_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>
    /// Builds a started server with two joined players ("Alice", "Bob") and a docking module
    /// on the (shared) ship, under the given docking rule.
    /// </summary>
    private SvGameServer NewServer(DockingMode mode, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, mode.ToString()));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var config = new ServerConfig { WorldName = mode.ToString(), Seed = 1, AutoSaveIntervalMinutes = 9999 };
        config.Rules.ShipDocking = mode;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        // Per-player ships: give each player's own ship a docking module.
        foreach (var s in server.Sessions.Values)
        {
            s.Ships[s.ActiveShipId].Modules.Add("docking_module");
        }

        return server;
    }

    [Fact]
    public void RequestThenAccept_DocksBothPlayers()
    {
        var server = NewServer(DockingMode.RequestRequired, out var repo);
        using (repo)
        {
            server.RequestDock("Alice", "Bob");
            Assert.False(server.AreDocked("Alice", "Bob")); // pending handshake, not docked yet

            server.RespondDock("Bob", "Alice", accept: true);

            Assert.True(server.AreDocked("Alice", "Bob"));
            Assert.True(server.AreDocked("Bob", "Alice")); // symmetric
            Assert.True(server.HasGuestAccess("Alice", "Bob"));
        }
    }

    [Fact]
    public void Reject_DoesNotDock()
    {
        var server = NewServer(DockingMode.RequestRequired, out var repo);
        using (repo)
        {
            server.RequestDock("Alice", "Bob");
            server.RespondDock("Bob", "Alice", accept: false);

            Assert.False(server.AreDocked("Alice", "Bob"));
        }
    }

    [Fact]
    public void Off_RejectsDocking()
    {
        var server = NewServer(DockingMode.Off, out var repo);
        using (repo)
        {
            server.RequestDock("Alice", "Bob");
            Assert.False(server.AreDocked("Alice", "Bob"));

            // Even an explicit response cannot dock when the rule is Off (no pending request).
            server.RespondDock("Bob", "Alice", accept: true);
            Assert.False(server.AreDocked("Alice", "Bob"));
        }
    }

    [Fact]
    public void Free_AutoDocksWithoutHandshake()
    {
        var server = NewServer(DockingMode.Free, out var repo);
        using (repo)
        {
            server.RequestDock("Alice", "Bob");
            Assert.True(server.AreDocked("Alice", "Bob"));
        }
    }

    [Fact]
    public void Undock_DissolvesDocking()
    {
        var server = NewServer(DockingMode.Free, out var repo);
        using (repo)
        {
            server.RequestDock("Alice", "Bob");
            Assert.True(server.AreDocked("Alice", "Bob"));

            server.Undock("Alice");

            Assert.False(server.AreDocked("Alice", "Bob"));
            Assert.False(server.AreDocked("Bob", "Alice"));
        }
    }

    [Fact]
    public void MissingDockingModule_RejectsRequest()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "nomodule"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        var config = new ServerConfig { WorldName = "nomodule", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        config.Rules.ShipDocking = DockingMode.Free;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        // Note: no docking_module built on the ship.
        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");

        server.RequestDock("Alice", "Bob");
        Assert.False(server.AreDocked("Alice", "Bob"));
    }

    [Fact]
    public void Disconnect_UndocksRemainingPlayer()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "disc"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "disc", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        config.Rules.ShipDocking = DockingMode.Free;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        // Alice joins over the (networked) loopback transport; Bob is a local session.
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Alice" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.AddLocalPlayer("Bob");
        // Per-player ships: give each joined player's own ship a docking module.
        foreach (var s in server.Sessions.Values)
        {
            if (s.Joined && s.Ships.TryGetValue(s.ActiveShipId, out var sh))
            {
                sh.Modules.Add("docking_module");
            }
        }

        server.RequestDock("Alice", "Bob");
        Assert.True(server.AreDocked("Alice", "Bob"));

        // Alice disconnects; the server must undock Bob.
        client.Disconnect();
        server.Tick(0.1);

        Assert.False(server.AreDocked("Bob", "Alice"));
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
