// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2514: an NPC station's protection belongs to its own void world. Every station is stamped at the same origin in its
/// <c>station:&lt;id&gt;</c> world, and the check used to test that box on whatever world was active — after one docking,
/// natural ground on every planet answered "This station is protected".
/// </summary>
public sealed class StationProtectionScopeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationProtectionScopeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_station_scope_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "scope"));
        var config = new ServerConfig
        {
            WorldName = "scope",
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent },
        };
        config.Rules.FreeSpaceFlight = true;

        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>Records every decoded outbound message so the test can read the reject reasons.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private static List<string> RejectionsTo(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason).ToList();

    [Fact]
    public void AfterDockingAndLeaving_TheStationBoxProtectsNothingOnAPlanet_ButStillDoesAboard()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string home = pilot.CurrentLocationId;
            pilot.State.Inventory.Add("stone", 16, 64);
            var stone = _content.GetBlock("stone")!.NumericId;
            var dirt = _content.GetBlock("dirt")!.NumericId;

            // Dock at a station, exactly like a player flying up to one.
            server.EnterSpace("Pilot");
            var contact = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.SpaceStation);
            server.ShipMove("Pilot", contact.Position.X, contact.Position.Y, contact.Position.Z - 8f);
            server.BoardStation("Pilot", contact.Id);
            Assert.True(server.InStation("Pilot"));

            var origin = server.StationStampOriginForTest(contact.Id);
            var spot = new Vector3i(origin.X + 1, 65, origin.Z + 1);
            var beside = new Vector3f(spot.X + 2.5f, spot.Y, spot.Z + 0.5f);

            // Aboard, the station's own box is still protected — a hull cell can't be mined, an open cell not built in.
            pilot.State.Position = beside;
            server.World.SetBlock(spot, stone);
            transport.Sent.Clear();
            server.MineBlockOnce("Pilot", spot.X, spot.Y, spot.Z);
            Assert.Contains("@srv.protect.station", RejectionsTo(transport, pilot));
            Assert.False(server.World.GetBlock(spot).IsAir);

            server.World.SetBlock(spot, BlockId.Air);
            transport.Sent.Clear();
            server.PlaceBlock("Pilot", spot.X, spot.Y, spot.Z, "stone");
            Assert.Contains("@srv.protect.station", RejectionsTo(transport, pilot));
            Assert.True(server.World.GetBlock(spot).IsAir);

            // Undock and land on another planet of the system: the same coordinates are ordinary ground there.
            server.LeaveStation("Pilot");
            Assert.True(server.InSpace("Pilot"));
            var system = server.Galaxy.FindBody(home)!.SystemId;
            var dest = server.Galaxy.Systems.First(s => s.Id == system).Bodies
                .First(b => b.Id != home && b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType));
            server.LandOnBody("Pilot", dest.Id);
            Assert.Equal(dest.Id, pilot.CurrentLocationId);
            Assert.Equal(dest.Id, server.World.LocationId);
            Assert.False(server.InSpace("Pilot"));

            pilot.State.AboardShip = false;
            pilot.State.Position = beside;
            server.World.SetBlock(spot, dirt);
            transport.Sent.Clear();
            server.MineBlock("Pilot", spot.X, spot.Y, spot.Z);
            Assert.DoesNotContain("@srv.protect.station", RejectionsTo(transport, pilot));
            Assert.True(server.World.GetBlock(spot).IsAir, "natural ground on a planet must be minable after a docking");

            transport.Sent.Clear();
            server.PlaceBlock("Pilot", spot.X, spot.Y, spot.Z, "stone");
            Assert.DoesNotContain("@srv.protect.station", RejectionsTo(transport, pilot));
            Assert.Equal(stone.Value, server.World.GetBlock(spot).Value);
        }
    }
}
