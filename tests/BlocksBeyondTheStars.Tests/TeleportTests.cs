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

/// <summary>
/// Suit teleporter: recalls the player to their ship (respawn point) if they carry the device, are
/// charged and off cooldown. Without the device it does nothing; a second use is blocked until the
/// cooldown elapses.
/// </summary>
public sealed class TeleportTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TeleportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tp"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "tp", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Teleport_RecallsToShip_WithDevice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.Position = new Vector3f(200, 64, 200); // far away
            p.State.AboardShip = false;
            p.State.SuitEnergy = 100f;
            p.State.Inventory.Add("suit_teleporter", 1, 1);

            server.TeleportToShip("Spacer");

            Assert.Equal(p.State.RespawnPoint.X, p.State.Position.X);
            Assert.Equal(p.State.RespawnPoint.Z, p.State.Position.Z);
            Assert.True(p.State.AboardShip);
            Assert.True(p.State.SuitEnergy < 100f); // energy spent
        }
    }

    [Fact]
    public void Teleport_DoesNothing_WithoutDevice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.Position = new Vector3f(200, 64, 200);

            server.TeleportToShip("Spacer");

            Assert.Equal(200f, p.State.Position.X); // unchanged
        }
    }

    [Fact]
    public void Teleport_SendsRespawnSnap_NotJustAStateUpdate()
    {
        // The recall must ride the RespawnNotice snap channel: a plain PlayerStateUpdate position is
        // discarded by the client, whose next MoveIntent then reverts the teleport server-side (#414 N17).
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tpsnap"));
        using (repo)
        {
            var link = new LoopbackLink();
            var st = new LoopbackServerTransport(link);
            var client = new LoopbackClientTransport(link);
            var config = new ServerConfig { WorldName = "tpsnap", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            client.Connect("loopback", 0);
            client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Spacer" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            var p = server.Sessions[1];
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.Position = new Vector3f(200, 64, 200);
            p.State.AboardShip = false;
            p.State.SuitEnergy = 100f;
            p.State.Inventory.Add("suit_teleporter", 1, 1);

            RespawnNotice? snap = null;
            client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is RespawnNotice r) snap = r; };

            server.TeleportToShip(p.State.PlayerId);
            client.Poll();

            Assert.NotNull(snap);
            Assert.Equal(5f, snap!.X);
            Assert.Equal(70f, snap.Y);
            Assert.False(snap.Died); // a recall, not a death — no death feedback on the client
        }
    }

    [Fact]
    public void Teleport_OnCooldown_UntilItRecharges()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Spacer");
            p.State.RespawnPoint = new Vector3f(5, 70, 5);
            p.State.AboardShip = false;
            p.State.SuitEnergy = 100f;
            p.State.Inventory.Add("suit_teleporter", 1, 1);

            server.TeleportToShip("Spacer"); // first use OK
            p.State.Position = new Vector3f(200, 64, 200); // walk away again

            server.TeleportToShip("Spacer"); // still on cooldown
            Assert.Equal(200f, p.State.Position.X); // not recalled

            server.Tick(31.0); // cooldown (30s) elapses
            server.TeleportToShip("Spacer");
            Assert.Equal(p.State.RespawnPoint.X, p.State.Position.X); // recalled again
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
