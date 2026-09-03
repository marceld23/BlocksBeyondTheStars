// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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
/// The gravity volume of a player-built station (#1485): inside the stamped box + margin a boarder walks;
/// outside it the suit floats (the on-foot zero-g the client already knows from building above a planet's
/// atmosphere), with a one-shot hint; far beyond it he is pulled back to the pad instead of falling for ever.
/// </summary>
public sealed class StationGravityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationGravityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stgrav_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<object> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add(m);
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo, IServerTransport transport)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static void Edit(SvGameServer server, string playerId, string id, int x, int y, int z, string item)
        => server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

    /// <summary>Deploys a core and builds a sealed 5×5×5 iron shell around it (cells −2…2) with a slide door;
    /// then boards it. The box lands at world (8…12, 64…68, 8…12), the pad at (10.5, 65, 10.5).</summary>
    private static string BuildAndBoard(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    bool shell = Math.Abs(x) == 2 || Math.Abs(y) == 2 || Math.Abs(z) == 2;
                    bool doorway = x == 2 && y == -1 && z == 0;
                    if (shell && !doorway)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, playerId, id, 2, -1, 0, "door_slide");
        Assert.True(server.StationIsBoardableForTest(id));

        var contact = server.SpaceEntitiesFor(playerId).First(e => e.Id == id);
        server.ShipMove(playerId, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation(playerId, id);
        Assert.True(server.InStation(playerId));
        return id;
    }

    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int ticks = 4)
    {
        for (int i = 0; i < ticks; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void Boarder_Walks_InsideTheBox_AndFloats_Outside_WithAHint()
    {
        var transport = new RecordingTransport();
        var server = NewServer("gravity", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);

            var deck = new Vector3f(10.5f, 65f, 10.5f);
            TickAt(server, pilot, deck);
            Assert.False(server.FloatingOutsideStationForTest("Owner"), "on the deck you walk");

            // Twelve blocks beside the hull: past the box + its 8-block margin → the suit floats, and the
            // player is told once.
            var outside = new Vector3f(10.5f + 2 + 12, 65f, 10.5f);
            TickAt(server, pilot, outside);
            Assert.True(server.FloatingOutsideStationForTest("Owner"), "beyond the gravity volume you float");
            Assert.Single(transport.Sent.Where(m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g"));

            // Back onto the deck: walking again, no second hint.
            TickAt(server, pilot, deck);
            Assert.False(server.FloatingOutsideStationForTest("Owner"));
            TickAt(server, pilot, outside);
            Assert.Single(transport.Sent.Where(m => m is ServerMessage sm && sm.Text == "@srv.station.zero_g"));
        }
    }

    [Fact]
    public void Boarder_DriftingFarBelow_IsPulledBackToThePad()
    {
        var transport = new RecordingTransport();
        var server = NewServer("drift", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            BuildAndBoard(server, pilot);

            // A hundred blocks under the station: beyond box + margin + rescue distance → back on the pad.
            pilot.State.Position = new Vector3f(10.5f, 65f - 100f, 10.5f);
            server.RunVoidRescueForTest();

            Assert.True(pilot.State.Position.Y > 60f, $"expected the pad, got {pilot.State.Position}");
            Assert.False(server.FloatingOutsideStationForTest("Owner"));
            Assert.Contains(transport.Sent, m => m is RespawnNotice r && r.Reason == "@srv.station.drifted_back");
            Assert.True(server.InStation("Owner"), "the rescue keeps him aboard, it does not undock him");
        }
    }
}
