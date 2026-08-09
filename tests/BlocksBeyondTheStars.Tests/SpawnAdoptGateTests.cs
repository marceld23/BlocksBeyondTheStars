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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The join-time spawn-adoption gate (#865). A client that has not yet processed the server's spawn
/// keeps streaming its pre-snap pose — the scene-default transform near the world origin — and the
/// server used to trust it, overwriting the freshly computed ship/pad spawn and leaving new players
/// entombed in the origin column for the void rescue to dig into a random cave (the root cause of
/// the #834 lockout). These tests drive the full network intent path a real client uses.
/// </summary>
public sealed class SpawnAdoptGateTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpawnAdoptGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sag_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private ServerConfig Config() => new()
    {
        WorldName = "sag",
        Seed = 123456,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false, // bare terrain — the gate is about the position, not the ship stamp
    };

    [Fact]
    public void Join_PreSnapGhostPose_IsDropped_UntilTheClientAdoptsTheSpawn()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sag"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Fresh" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var session = server.Sessions[1];
        var spawn = session.State.Position;

        // The race, replayed exactly: before ever seeing the server's spawn, the client reports its
        // scene-default pose from a column far away (the real-world capture was the origin column,
        // dragged 10,000 blocks off the placed spawn). The gate must drop it — spawn stands.
        int circ = server.World.Circumference;
        float ghostX = (float)WorldConstants.WrapX(spawn.X + circ / 2f, circ); // maximally far, wrap-aware
        client.Send(NetCodec.Encode(new MoveIntent { X = ghostX, Y = 30f, Z = spawn.Z }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(spawn.X, session.State.Position.X, 3);
        Assert.Equal(spawn.Y, session.State.Position.Y, 3);
        Assert.Equal(spawn.Z, session.State.Position.Z, 3);

        // The client processes the spawn and reports from it (what the snapped controller sends):
        // that first nearby report is accepted and clears the gate.
        client.Send(NetCodec.Encode(new MoveIntent { X = spawn.X + 1f, Y = spawn.Y, Z = spawn.Z }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(spawn.X + 1f, session.State.Position.X, 3);

        // From here on movement is trusted as before — a far report (lapping the world) goes through.
        client.Send(NetCodec.Encode(new MoveIntent { X = ghostX, Y = spawn.Y, Z = spawn.Z }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(ghostX, session.State.Position.X, 3);
    }

    [Fact]
    public void Join_ReportNearTheSpawn_IsAcceptedImmediately()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sag2"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);

        var config = Config();
        config.WorldName = "sag2";
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Settler" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var session = server.Sessions[1];
        var spawn = session.State.Position;

        // A snapped client settling a few blocks (falling onto the pad, stepping off the hatch) must
        // never be rejected — the very first report inside the adopt radius passes straight through.
        client.Send(NetCodec.Encode(new MoveIntent { X = spawn.X + 2f, Y = spawn.Y - 3f, Z = spawn.Z + 2f }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(spawn.X + 2f, session.State.Position.X, 3);
        Assert.Equal(spawn.Y - 3f, session.State.Position.Y, 3);
        Assert.Equal(spawn.Z + 2f, session.State.Position.Z, 3);
    }

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore Windows file-lock cleanup races
        }
    }
}
