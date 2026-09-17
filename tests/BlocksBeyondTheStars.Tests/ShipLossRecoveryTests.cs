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
/// Losing the ship is a WORLD CHANGE for everyone who was aboard (#1945). A player report ("I crashed and I am
/// hanging in the air") came from a shoot-down after a walkabout inside the hull: the server moved the pilot with
/// two field writes, so the client kept the ship-interior world, dropped the arriving planet chunks and hovered
/// with no ground — and the ship was never parked again, so there was no way out either.
/// </summary>
public sealed class ShipLossRecoveryTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipLossRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shiploss_" + Guid.NewGuid().ToString("N"));
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
        config.Rules.SpaceCombat = SpaceCombatMode.PvE;
        config.Rules.SpaceNpcEnemies = AlienActivity.Normal; // two drones, 10 dps together
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>Flies the pilot into the drones and ticks until the ship is defeated.</summary>
    private static void FlyIntoTheDrones(SvGameServer server)
    {
        var drone = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.Drone);
        server.ShipMove("Pilot", drone.Position.X, drone.Position.Y, drone.Position.Z);
        server.Tick(30.0);
    }

    [Fact]
    public void AShipLostAfterAWalkaboutInside_LandsThePilotOnItsWorld_WithAWorldReset()
    {
        var transport = new RecordingTransport();
        var server = NewServer("lossinside", transport, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string home = pilot.CurrentLocationId;

            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");          // walk around inside the hull …
            Assert.True(server.InShipInterior("Pilot"));
            server.ExitShipToFlight("Pilot");           // … and take the helm again (no WorldReset by design)

            transport.Sent.Clear();
            FlyIntoTheDrones(server);

            Assert.False(server.InSpace("Pilot"));
            Assert.False(server.InShipInterior("Pilot"), "the walkabout ends with the ship");

            // The client is told about the new world, and where in it the player now is.
            Assert.Contains(transport.Sent, m => m is WorldReset);
            var notice = transport.Sent.OfType<RespawnNotice>().LastOrDefault();
            Assert.NotNull(notice);
            Assert.False(notice!.Died, "the pilot survived — no death flash");

            // Back on the ship's own body, not in the interior void world.
            Assert.Equal(home, pilot.CurrentLocationId);
            Assert.DoesNotContain("shipint:", pilot.CurrentLocationId);
            Assert.True(pilot.State.AboardShip);

            // And standing in the parked ship, not hovering: the pilot is where the notice put them.
            Assert.Equal(notice.X, pilot.State.Position.X, 3);
            Assert.Equal(notice.Y, pilot.State.Position.Y, 3);
            Assert.Equal(notice.Z, pilot.State.Position.Z, 3);
        }
    }

    [Fact]
    public void AShipLostInFlight_IsParkedAgain_SoThePilotCanWalkOut()
    {
        var transport = new RecordingTransport();
        var server = NewServer("lossflight", transport, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");               // the hull leaves the pad on launch
            FlyIntoTheDrones(server);

            Assert.False(server.InSpace("Pilot"));
            Assert.Contains("Pilot", server.PlacedHullOwnersForTest()); // the recovered ship stands on its pad
        }
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
            // a locked save file on Windows must not fail the test run
        }
    }
}
