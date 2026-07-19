// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
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

        // Fresh players spawn on their own (far-apart) landing pads; docking now requires standing
        // together (#426 S17 — the client only offers the K prompt within InteractRange anyway), so
        // co-locate them like real dock partners before each scenario.
        Colocate(server);

        return server;
    }

    /// <summary>Puts every joined player at the first session's position (the real pre-dock situation:
    /// the K prompt only appears for a player standing next to you).</summary>
    private static void Colocate(SvGameServer server)
    {
        var anchor = server.Sessions.Values.First(s => s.Joined);
        foreach (var s in server.Sessions.Values)
        {
            if (s.Joined)
            {
                s.State.Position = anchor.State.Position;
                s.CurrentLocationId = anchor.CurrentLocationId;
            }
        }
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

    /// <summary>Places one player <paramref name="dy"/> blocks straight above their current position —
    /// vertical, so no torus wrap can shrink the distance regardless of the test world's circumference.</summary>
    private static void MoveVertically(SvGameServer server, string playerId, float dy)
    {
        var s = server.Sessions.Values.First(x => x.State.PlayerId == playerId);
        var p = s.State.Position;
        s.State.Position = new Vector3f(p.X, p.Y + dy, p.Z);
    }

    [Fact]
    public void FarApart_RejectsDocking()
    {
        var server = NewServer(DockingMode.Free, out var repo);
        using (repo)
        {
            MoveVertically(server, "Bob", 200f); // way past DockRange (#426 S17)
            server.RequestDock("Alice", "Bob");
            Assert.False(server.AreDocked("Alice", "Bob"));
        }
    }

    [Fact]
    public void DifferentWorld_RejectsDocking_EvenAtOverlappingCoordinates()
    {
        var server = NewServer(DockingMode.Free, out var repo);
        using (repo)
        {
            // Positions are world-local: leave them numerically identical, move only the world.
            var bob = server.Sessions.Values.First(x => x.State.PlayerId == "Bob");
            bob.CurrentLocationId = "some-other-planet";

            server.RequestDock("Alice", "Bob");
            Assert.False(server.AreDocked("Alice", "Bob")); // #426 S17: same-world is required
        }
    }

    [Fact]
    public void MovedAwayBetweenRequestAndAccept_RejectsTheAccept()
    {
        var server = NewServer(DockingMode.RequestRequired, out var repo);
        using (repo)
        {
            server.RequestDock("Alice", "Bob"); // close together — request goes through

            MoveVertically(server, "Alice", 200f); // requester flies off before Bob accepts
            server.RespondDock("Bob", "Alice", accept: true);

            Assert.False(server.AreDocked("Alice", "Bob")); // #426 S17: re-checked at accept time
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
        Colocate(server); // rule out the proximity gate — this test is about the missing module

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

        Colocate(server);
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
