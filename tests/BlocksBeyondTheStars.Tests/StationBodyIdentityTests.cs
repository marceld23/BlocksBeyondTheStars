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
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1856: a boarded station's world is keyed <c>station:&lt;id&gt;</c>, which the galaxy never matched —
/// so stations stayed "Uncharted", carried no system name and the star map's "you are here" pointed nowhere.
/// #1857: blocks the SERVER grows aboard a player station (a sapling's tree) never reached its cell grid.</summary>
public sealed class StationBodyIdentityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationBodyIdentityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stbody_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A save whose home system carries a real (galaxy) station — the boarding tests' configuration.</summary>
    private SvGameServer StartedWithStations(string name, out SqliteWorldRepository repo, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Flies up to the first station contact that is a galaxy body (not the synthesised fallback) and docks.</summary>
    private static CelestialBody DockAtGalaxyStation(SvGameServer server, string playerId)
    {
        if (!server.InSpace(playerId))
        {
            server.EnterSpace(playerId);
        }

        var contact = server.SpaceEntitiesFor(playerId)
            .First(e => e.Kind == CombatEntityKind.SpaceStation && server.Galaxy.FindBody(e.Id) is not null);
        server.ShipMove(playerId, contact.Position.X, contact.Position.Y, contact.Position.Z - 8f);
        server.BoardStation(playerId, contact.Id);
        Assert.True(server.InStation(playerId));
        return server.Galaxy.FindBody(contact.Id)!;
    }

    [Fact]
    public void DockingAtASystemStation_MarksItsBodyVisited_AndResolvesItsSystemName()
    {
        var server = StartedWithStations("dock", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var before = server.SpaceEntitiesFor("Pilot") // the contact exists before docking, still uncharted
                .Select(e => server.Galaxy.FindBody(e.Id)).FirstOrDefault(b => b is { Kind: CelestialKind.SpaceStation });
            Assert.NotNull(before);
            Assert.NotEqual(GenerationStatus.Visited, before!.Status);

            var station = DockAtGalaxyStation(server, "Pilot");
            Assert.Equal("station:" + station.Id, pilot.CurrentLocationId);

            Assert.Equal(GenerationStatus.Visited, station.Status); // charted — no more "Uncharted" on the map
            Assert.Equal(station.Id, server.ResolveLocationBodyIdForTest(pilot.CurrentLocationId));

            var system = server.Galaxy.Systems.Single(s => s.Id == station.SystemId);
            var (systemName, bodyName) = server.LocationNamesForTest(pilot.CurrentLocationId);
            Assert.Equal(system.Name, systemName); // was "" — the weather hashed the star colour from it
            Assert.Equal(station.Name, bodyName);

            // Persisted like a landing: a restart reads the status back from the repository.
            Assert.Equal(nameof(GenerationStatus.Visited), repo.LoadLocationStatuses()[station.Id]);
        }
    }

    [Fact]
    public void StarMapAboardAStation_PointsAtTheStationBody_AndTheWorldResetNamesItsSystem()
    {
        var transport = new RecordingTransport();
        var server = StartedWithStations("map", out var repo, transport);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            transport.Sent.Clear();
            var station = DockAtGalaxyStation(server, "Pilot");
            var system = server.Galaxy.Systems.Single(s => s.Id == station.SystemId);

            var map = transport.Sent.OfType<StarMapData>().Last();
            Assert.Equal(station.Id, map.ActiveLocationId); // a body id the client can match — "you are here"
            Assert.Contains(station.Id, map.LandedBodyIds);
            Assert.Contains(station.SystemId, map.KnownSystemIds);
            Assert.Equal("Visited", map.Systems.SelectMany(s => s.Bodies).Single(b => b.Id == station.Id).Status);

            var reset = transport.Sent.OfType<WorldReset>().Last();
            Assert.Equal(system.Name, reset.SystemName); // was string.Empty
            Assert.Equal(station.Name, reset.PlanetName);
        }
    }

    [Fact]
    public void LocationNames_ForAPlainBodyId_AreUnchanged()
    {
        var server = StartedWithStations("plain", out var repo);
        using (repo)
        {
            var home = server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!;
            var system = server.Galaxy.Systems.Single(s => s.Id == home.SystemId);
            Assert.Equal((system.Name, home.Name), server.LocationNamesForTest(home.Id));
            Assert.Equal(home.Id, server.ResolveLocationBodyIdForTest(home.Id));
            Assert.Equal(string.Empty, server.ResolveLocationBodyIdForTest("station:nowhere")); // unknown ids stay unresolved
        }
    }

    // ---------------- #1857: server-grown blocks reach the player station's cell grid ----------------

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static void Edit(SvGameServer server, string playerId, string id, int x, int y, int z, string item)
        => server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

    /// <summary>A sealed 5×5×11 shaft of a hall (cells −2…2 in x/z, y −2…8): room for a trunk of five and its crown —
    /// the #1835 sapling test's hull.</summary>
    private static string BuildTallSealedBox(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 8; y++)
                for (int z = -2; z <= 2; z++)
                {
                    bool shell = Math.Abs(x) == 2 || Math.Abs(z) == 2 || y == -2 || y == 8;
                    bool doorway = x == 2 && y == -1 && z == 0;
                    if (shell && !doorway)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, playerId, id, 2, -1, 0, "door_slide");
        Assert.True(server.StationIsBoardableForTest(id));
        return id;
    }

    /// <summary>Build cell → interior-world cell for the sealed box: origin (8,64,8) minus the −2 build minimum.</summary>
    private static Vector3i BoxWorld(int x, int y, int z) => new(x + 10, y + 66, z + 10);

    private static void BoardOwnStation(SvGameServer server, string playerId, string stationId)
    {
        if (!server.InSpace(playerId))
        {
            server.EnterSpace(playerId);
        }

        var contact = server.SpaceEntitiesFor(playerId).First(e => e.Id == stationId);
        server.ShipMove(playerId, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation(playerId, stationId);
        Assert.True(server.InStation(playerId));
    }

    [Fact]
    public void ATreeGrownFromASapling_AboardAPlayerStation_ReachesItsCellGrid()
    {
        var s = NewServer("sapling", out var repo);
        using (repo)
        {
            var pilot = s.AddLocalPlayer("Owner");
            string id = BuildTallSealedBox(s, pilot);
            BoardOwnStation(s, "Owner", id);

            var soil = BoxWorld(1, -1, 1);
            var cell = BoxWorld(1, 0, 1);
            pilot.State.Inventory.Add("dirt", 1, 99);
            pilot.State.Inventory.Add("sapling", 1, 99);
            s.PlaceBlock("Owner", soil.X, soil.Y, soil.Z, "dirt");
            s.PlaceBlock("Owner", cell.X, cell.Y, cell.Z, "sapling");
            ushort sapling = _content.GetBlock("flora_sapling")!.NumericId.Value;
            ushort log = _content.GetBlock("wood_log")!.NumericId.Value;
            ushort leaves = _content.GetBlock("tree_leaves")!.NumericId.Value;
            Assert.Equal(sapling, s.StationCellsForTest(id)[new Vector3i(1, 0, 1)].Value); // the player's edit did reach the grid

            s.Tick(200.0); // > SaplingGrowSeconds — the tree grows (#1835)
            Assert.Equal(log, s.World.GetBlock(cell).Value);

            var cells = s.StationCellsForTest(id);
            Assert.Equal(log, cells[new Vector3i(1, 0, 1)].Value); // #1857: the trunk replaced the sapling in the grid…
            Assert.Equal(log, cells[new Vector3i(1, 1, 1)].Value);
            Assert.Contains(cells, kv => kv.Value.Value == leaves); // …and the crown is there too
            Assert.Equal(
                Enumerable.Range(0, 20).Count(dy => s.World.GetBlock(BoxWorld(1, dy, 1)).Value == log),
                cells.Count(kv => kv.Key.X == 1 && kv.Key.Z == 1 && kv.Value.Value == log)); // trunk height matches the world

            // The grid is what the row persists — a restart's re-stamp reads the tree from here.
            var row = repo.ListSpaceStructures().Single(r => r.Id == id);
            Assert.True(row.Blocks.Length > 0);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
