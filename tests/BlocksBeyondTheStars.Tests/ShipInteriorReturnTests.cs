// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
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
/// Leaving the ship interior in space without landing (the helm, the airlock). Two player reports:
/// "DAS NICHTS! (schon wieder)" (#2117) — after a walkabout inside, a landing on the same body showed no world,
/// because the client was never told the body was current again and dropped its chunks as "the world we just
/// left"; and "WO?" (#2118) — the ship came back at the launch point instead of where it floated.
/// </summary>
public sealed class ShipInteriorReturnTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipInteriorReturnTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shipintret_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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
            if (NetCodec.Decode(payload) is { } m) { Sent.Add(m); }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) { Sent.Add(m); }
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(string name, RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off; // nothing shoots the ship while we walk around
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    [Theory]
    [InlineData(false)] // the helm
    [InlineData(true)]  // out through the airlock
    public void LeavingTheInterior_AnnouncesTheBody_AndALandingThereStreamsChunksTheClientKeeps(bool eva)
    {
        var transport = new RecordingTransport();
        var server = NewServer(eva ? "intret_eva" : "intret_helm", transport, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string home = pilot.CurrentLocationId;

            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.StartsWith("shipint:", pilot.CurrentLocationId);

            transport.Sent.Clear();
            if (eva)
            {
                server.StartEvaFromShip("Pilot");
            }
            else
            {
                server.ExitShipToFlight("Pilot");
            }

            // The body under the flight view is current again — the client is told, like after undocking.
            var reset = transport.Sent.OfType<WorldReset>().LastOrDefault();
            Assert.NotNull(reset);
            Assert.NotEqual("ship_interior", reset!.PlanetType);
            Assert.Equal(home, pilot.CurrentLocationId);
            Assert.True(server.InSpace("Pilot"));

            if (eva)
            {
                return; // an EVA can't land on a planet; the announcement above is the whole fix for it
            }

            // Land on the same body: every chunk that follows carries the world id the client last heard of.
            transport.Sent.Clear();
            server.LandOnBody("Pilot", home);
            for (int i = 0; i < 20; i++)
            {
                server.Tick(0.05);
            }

            Assert.False(server.InSpace("Pilot"));
            var chunks = transport.Sent.OfType<ChunkDataMessage>().ToList();
            Assert.NotEmpty(chunks);
            int announced = transport.Sent.OfType<WorldReset>().LastOrDefault()?.WorldId ?? reset.WorldId;
            Assert.All(chunks, c => Assert.Equal(announced, c.WorldId));
        }
    }

    [Fact]
    public void TakingTheHelmAgain_PutsTheShipBackWhereItFloated_AndTellsTheFlightView()
    {
        var transport = new RecordingTransport();
        var server = NewServer("intret_pose", transport, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.ShipMove("Pilot", 200f, 10f, -50f, 90f); // fly away from the launch point, turned east

            server.EnterShipInterior("Pilot");
            transport.Sent.Clear();
            server.ExitShipToFlight("Pilot");

            var state = transport.Sent.OfType<SpaceState>().Last();
            Assert.True(state.SkipLaunch);
            Assert.True(state.HasResumePose);
            Assert.Equal(200f, state.ResumeX, 3);
            Assert.Equal(10f, state.ResumeY, 3);
            Assert.Equal(-50f, state.ResumeZ, 3);
            Assert.Equal(90f, state.ResumeYaw, 3);
        }
    }

    [Fact]
    public void BoardingFromASpacewalk_RemembersTheShip_NotTheSuit()
    {
        var transport = new RecordingTransport();
        var server = NewServer("intret_evaboard", transport, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.ShipMove("Pilot", 120f, 0f, 40f, 180f); // the ship floats here …

            pilot.State.InEva = true;                       // … the pilot climbs out and drifts off in the suit
            server.ShipMove("Pilot", 126f, 2f, 43f, 15f);

            server.EnterShipInterior("Pilot");              // back in through the hatch
            transport.Sent.Clear();
            server.ExitShipToFlight("Pilot");

            var state = transport.Sent.OfType<SpaceState>().Last();
            Assert.True(state.HasResumePose);
            Assert.Equal(120f, state.ResumeX, 3);
            Assert.Equal(40f, state.ResumeZ, 3);
            Assert.Equal(180f, state.ResumeYaw, 3);
        }
    }

    [Fact]
    public void ANormalLaunch_CarriesNoResumePose()
    {
        var transport = new RecordingTransport();
        var server = NewServer("intret_launch", transport, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            transport.Sent.Clear();
            server.EnterSpace("Pilot");

            var state = transport.Sent.OfType<SpaceState>().Last();
            Assert.False(state.HasResumePose);
            Assert.False(state.SkipLaunch);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
