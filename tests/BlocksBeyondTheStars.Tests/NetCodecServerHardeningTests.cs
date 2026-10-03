// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.


using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// End-to-end server hardening tests.
///
/// These tests deliberately drive the same transport event used by the real server:
///
///     transport payload -> GameServer.OnPayload -> NetCodec.Decode -> session validation / flood gate -> Dispatch -> message handler
///
/// The purpose is to verify that codec-level safety survives the transition into the actual GameServer dispatch path.
/// </summary>
public sealed class NetCodecServerHardeningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public NetCodecServerHardeningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_netcodec_server_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>
    /// Minimal transport harness that raises exactly the events consumed by
    /// GameServer.OnPayload and records messages sent by the server.
    /// </summary>
    private sealed class DrivenTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Connect(int id) => ClientConnected?.Invoke(id);
        public void Disconnect(int id) => ClientDisconnected?.Invoke(id);
        public void Receive(int id, object message) => PayloadReceived?.Invoke(id, NetCodec.Encode(message));
        public void ReceiveRaw(int id, byte[] payload) => PayloadReceived?.Invoke(id, payload);
        public void Start(int port) { }
        public void Poll() { }
        public void Stop() { }
        public void Dispose() { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } message) { Sent.Add((connectionId, message)); }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } message) { Sent.Add((int.MinValue, message)); }
        }
    }

    private SvGameServer NewServer(string name, DrivenTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);

        var config = new ServerConfig { WorldName = name, Seed = 1, StartPlanet = "rocky", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static void Join(DrivenTransport transport, int connectionId, string playerName = "Pilot")
    {
        transport.Connect(connectionId);
        transport.Receive(connectionId, new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, ProtocolVersion = Protocol.Version, PlayerName = playerName });
    }

    // -------------------------------------------------------------------------
    // E2E Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MalformedPayload_DoesNotEscapeOnPayload()
    {
        var transport = new DrivenTransport();
        var server = NewServer("malformed_payload", transport);

        try
        {
            Join(transport, 1);
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is JoinAccepted);
            transport.Sent.Clear();

            // This is deliberately not a valid encoded message.
            var malformed = new byte[] { 0xFF, 0xFF, 0xFF, 0x00, 0x42 };
            var exception = Record.Exception(() => transport.ReceiveRaw(1, malformed));

            Assert.Null(exception);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void GameplayIntent_FromUnjoinedConnection_IsIgnored()
    {
        var transport = new DrivenTransport();
        var server = NewServer("unjoined_gameplay", transport);

        try
        {
            transport.Connect(1);
            transport.Receive(1, new MoveItemIntent { FromSlot = -999999, ToSlot = -999999 });

            // No gameplay response should be generated because the connection never completed the join path.
            Assert.Empty(transport.Sent);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void HostileMoveItemIntent_IsRejectedThroughServerDispatch()
    {
        var transport = new DrivenTransport();
        var server = NewServer("hostile_move_item", transport);

        try
        {
            Join(transport, 1);
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is JoinAccepted);
            transport.Sent.Clear();

            transport.Receive(1, new MoveItemIntent { FromSlot = -999999, ToSlot = -999999 });
            Assert.Empty(transport.Sent);

            // Server must still be alive and dispatching messages after receiving the hostile intent.
            transport.Receive(1, new RequestStarMap());
            Assert.NotEmpty(transport.Sent);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void MoveItem_InvalidDestination_IsRejectedThroughServerDispatch()
    {
        var transport = new DrivenTransport();
        var server = NewServer("invalid_move_destination", transport);

        try
        {
            Join(transport, 1);
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is JoinAccepted);
            transport.Sent.Clear();

            // Use a valid-looking source but an invalid destination.
            transport.Receive(1, new MoveItemIntent { FromSlot = 0, ToSlot = -2 });
            Assert.Empty(transport.Sent);

            transport.Receive(1, new RequestStarMap());
            Assert.NotEmpty(transport.Sent);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void MoveItem_InvalidSource_IsRejectedThroughServerDispatch()
    {
        var transport = new DrivenTransport();
        var server = NewServer("invalid_move_source", transport);

        try
        {
            Join(transport, 1);
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is JoinAccepted);
            transport.Sent.Clear();

            transport.Receive(1, new MoveItemIntent { FromSlot = -1, ToSlot = 1 });
            Assert.Empty(transport.Sent);

            transport.Receive(1, new RequestStarMap());
            Assert.NotEmpty(transport.Sent);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void MoveItem_NegativeOneDestination_RemainsValidStowCommand()
    {
        var transport = new DrivenTransport();
        var server = NewServer("valid_stow", transport);

        try
        {
            Join(transport, 1);
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is JoinAccepted);
            transport.Sent.Clear();

            // -1 explicitly means "stow into backpack"; verify it processes an inventory update.
            transport.Receive(1, new MoveItemIntent { FromSlot = 0, ToSlot = -1 });
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is InventoryUpdate);
            transport.Sent.Clear();

            // Server remains functional after the stow.
            transport.Receive(1, new RequestStarMap());
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is StarMapData);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void RepeatedHostileMoveItemIntents_DoNotWedgeServerDispatch()
    {
        var transport = new DrivenTransport();
        var server = NewServer("repeated_hostile_move", transport);

        try
        {
            Join(transport, 1);
            Assert.Contains(transport.Sent, entry => entry.Conn == 1 && entry.Msg is JoinAccepted);
            transport.Sent.Clear();

            for (var i = 0; i < 20; i++)
            {
                transport.Receive(1, new MoveItemIntent { FromSlot = int.MinValue, ToSlot = int.MaxValue });
            }

            Assert.Empty(transport.Sent);

            transport.Receive(1, new RequestStarMap());
            Assert.NotEmpty(transport.Sent);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void JoinRequest_OnAlreadyJoinedConnection_IsDropped()
    {
        var transport = new DrivenTransport();
        var server = NewServer("e2e_rejoin", transport);

        try
        {
            Join(transport, 1);
            Assert.Single(transport.Sent.Where(entry => entry.Conn == 1 && entry.Msg is JoinAccepted));
            transport.Sent.Clear();

            transport.Receive(1, new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, ProtocolVersion = Protocol.Version, PlayerName = "Pilot" });
            Assert.Empty(transport.Sent);

            transport.Receive(1, new RequestStarMap());
            Assert.NotEmpty(transport.Sent);
        }
        finally
        {
            server.Stop();
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Cleanup must never hide the actual test result.
        }
    }
}
