// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
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
/// #3139: a landing on a world the server first has to build must not close the flight before that world exists. The
/// server's transport flushes on its own thread, so a <c>SpaceClosed</c> sent before the seconds-long build of a cold world
/// reached the client long before the <c>WorldReset</c>: the client ended its descent and showed the player standing on the
/// planet they had just left. The spy transport looks at the server the moment the flight closes.
/// </summary>
public sealed class LandingWorldOrderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_landorder_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void LandingOnAColdWorld_ClosesTheFlightOnlyOnceTheWorldIsBuilt()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "landorder"));
        var link = new LoopbackLink();
        var spy = new SpyTransport(new LoopbackServerTransport(link));
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "landorder", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, spy, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var pilot = server.Sessions[1];
        var origin = server.Galaxy.FindBody(pilot.CurrentLocationId)!;
        var destination = server.Galaxy.AllBodies().First(b =>
            b.Kind == CelestialKind.Planet && b.SystemId == origin.SystemId && b.Id != origin.Id
            && !string.IsNullOrEmpty(b.PlanetType) && _content.GetPlanet(b.PlanetType!) is not null);
        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));
        Assert.False(server.WorldResidentForTest(destination.Id)); // a cold world: the server must build it first

        bool? builtWhenClosed = null;
        spy.Sent = message =>
        {
            if (message is SpaceClosed && builtWhenClosed is null)
            {
                builtWhenClosed = server.WorldResidentForTest(destination.Id);
            }
        };
        client.Send(NetCodec.Encode(new LeaveSpaceIntent { DestinationBodyId = destination.Id, PadIndex = -1 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.False(server.InSpace("Pilot"));
        Assert.Equal(destination.Id, pilot.CurrentLocationId);
        Assert.True(builtWhenClosed, "SpaceClosed went out before the destination world was built");
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
        catch (IOException)
        {
            // a SQLite handle may linger on Windows; the temp folder is cleaned by the OS later
        }
    }

    /// <summary>Hands everything to the loopback, but as encoded payloads only (no object lane), so each message the server
    /// sends passes through <see cref="Send"/> in order — and the test can look at the server right then.</summary>
    private sealed class SpyTransport : IServerTransport
    {
        private readonly LoopbackServerTransport _inner;

        public SpyTransport(LoopbackServerTransport inner)
        {
            _inner = inner;
            _inner.ClientConnected += id => ClientConnected?.Invoke(id);
            _inner.ClientDisconnected += id => ClientDisconnected?.Invoke(id);
            _inner.PayloadReceived += (id, payload) => PayloadReceived?.Invoke(id, payload);
        }

        public Action<object>? Sent { get; set; }

        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public void Start(int port) => _inner.Start(port);

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (Sent is { } sent && NetCodec.Decode(payload) is { } message)
            {
                sent(message);
            }

            _inner.Send(connectionId, payload, mode);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode) => _inner.Broadcast(payload, mode);

        public void Poll() => _inner.Poll();

        public void Stop() => _inner.Stop();

        public void Dispose() => _inner.Dispose();
    }
}
