// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
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
/// Server-authoritative multiplayer visibility across all three play contexts — planet surface, free space
/// flight, and station interiors. These headless tests lock in the AUTHORITATIVE replication (who-sees-what
/// messages + state) that the prior "code-complete but needs an in-engine two-player test" note referred to:
/// they prove the server broadcasts another player's PRESENCE and their WORLD CHANGES to a co-located second
/// player. (True in-Unity-engine rendering of those messages still needs a manual two-client check; that
/// cannot be asserted headlessly.)
/// </summary>
public sealed class MultiplayerVisibilityTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MultiplayerVisibilityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_mpvis_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>A transport that records every server send so a test can assert who received what message.</summary>
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

    private SvGameServer NewServer(string name, RecordingTransport transport, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    private readonly List<SqliteWorldRepository> _repos = new();

    // ---------------- Planet surface ----------------

    [Fact]
    public void PlanetSurface_PlayerSeesAnothersPresence_AndTheirBlockEdits()
    {
        var transport = new RecordingTransport();
        var server = NewServer("planet_vis", transport);

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");

        // Both stand on the same body, on foot.
        alice.State.AboardShip = false;
        bob.State.AboardShip = false;
        bob.State.Position = new Vector3f(6, 64, 4);

        // PRESENCE: a server tick past the presence interval cross-broadcasts each player to the other.
        transport.Sent.Clear();
        server.Tick(0.2);

        var bobToAlice = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .FirstOrDefault();
        Assert.NotNull(bobToAlice);
        Assert.Equal("Bob", bobToAlice!.Name);

        var aliceToBob = transport.Sent
            .Any(x => x.Conn == bob.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Alice");
        Assert.True(aliceToBob, "Alice's presence must reach Bob on the same world.");

        // BLOCK EDIT: Alice mines a block in her column; Bob (co-located) receives the BlockChanged broadcast.
        var target = FindMineableUnder(server, alice.State);
        Assert.True(target.HasValue, "expected a mineable block under Alice.");

        transport.Sent.Clear();
        server.MineBlock("Alice", target!.Value.X, target.Value.Y, target.Value.Z);

        bool bobSawEdit = transport.Sent.Any(x => x.Conn == bob.ConnectionId
            && x.Msg is BlockChanged bc && bc.X == target.Value.X && bc.Y == target.Value.Y && bc.Z == target.Value.Z
            && bc.Block == BlockId.AirValue);
        Assert.True(bobSawEdit, "Bob must receive Alice's block edit (mined cell → air) on the shared world.");
    }

    [Fact]
    public void PlanetSurface_PlayersOnDifferentBodies_DoNotSeeEachOthersEdits()
    {
        // The flip side of world-scoped visibility: an edit on planet A must NOT reach a player on planet B.
        var transport = new RecordingTransport();
        var server = NewServer("planet_iso", transport, c => c.Rules.FreeSpaceFlight = true);

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.State.AboardShip = false;

        // Send Bob to a different body IN THE SAME SYSTEM (no hyperjump needed) so the two no longer
        // share a world but the travel is not gated by a jump generator.
        var origin = server.Galaxy.FindBody(bob.CurrentLocationId);
        var otherBody = server.Galaxy.AllBodies()
            .First(b => b.Id != bob.CurrentLocationId
                && b.SystemId == origin!.SystemId
                && b.Kind is CelestialKind.Planet or CelestialKind.Moon
                && !string.IsNullOrEmpty(b.PlanetType));
        server.Travel("Bob", otherBody.Id);
        Assert.NotEqual(alice.CurrentLocationId, bob.CurrentLocationId);

        // Re-point the authoritative world cursor at Alice (the real OnPayload path serves the sender before
        // every intent; the MineBlock test helper does not, so do it here via a served no-op travel).
        server.Travel("Alice", alice.CurrentLocationId); // rejected "already there", but serves Alice first
        var target = FindMineableUnder(server, alice.State);
        Assert.True(target.HasValue);

        transport.Sent.Clear();
        server.MineBlock("Alice", target!.Value.X, target.Value.Y, target.Value.Z);

        bool bobSawEdit = transport.Sent.Any(x => x.Conn == bob.ConnectionId && x.Msg is BlockChanged);
        Assert.False(bobSawEdit, "A player on another body must not receive this world's block edits.");
    }

    // ---------------- Free space flight / orbit ----------------

    [Fact]
    public void Space_NewcomerIsSentOtherPilotsShip_AsARemoteSilhouette()
    {
        // G3/G4 remote-ship silhouette: entering a shared instance hands the newcomer every other pilot's
        // REAL voxel ship design (kind "ship_remote"), and hands the newcomer's ship to the others.
        var transport = new RecordingTransport();
        var server = NewServer("space_remote", transport, c => c.Rules.FreeSpaceFlight = true);

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");

        server.EnterSpace("Alice"); // alone first
        transport.Sent.Clear();
        server.EnterSpace("Bob"); // same body → same instance

        // Bob receives Alice's ship as a remote silhouette.
        bool bobGotAliceRemote = transport.Sent.Any(x => x.Conn == bob.ConnectionId
            && x.Msg is SpaceShipDesign d && d.Kind == "ship_remote" && d.Id == "ship:Alice");
        Assert.True(bobGotAliceRemote, "Bob must receive Alice's ship as a 'ship_remote' design.");

        // Alice receives Bob's ship as a remote silhouette (pushed to the players already in the instance).
        bool aliceGotBobRemote = transport.Sent.Any(x => x.Conn == alice.ConnectionId
            && x.Msg is SpaceShipDesign d && d.Kind == "ship_remote" && d.Id == "ship:Bob");
        Assert.True(aliceGotBobRemote, "Alice must receive Bob's ship as a 'ship_remote' design.");
    }

    [Fact]
    public void Space_PilotsSeeEachOtherAsNetSpacePlayers()
    {
        var transport = new RecordingTransport();
        var server = NewServer("space_poses", transport, c => c.Rules.FreeSpaceFlight = true);

        server.AddLocalPlayer("Alice");
        server.AddLocalPlayer("Bob");
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");

        server.ShipMove("Alice", 12f, 0f, 4f, 0.5f);
        server.ShipMove("Bob", -9f, 0f, 6f, 1.1f);

        var aliceSees = server.OtherSpacePlayers("Alice");
        var bobSees = server.OtherSpacePlayers("Bob");

        Assert.Contains(aliceSees, np => np.Name == "Bob");
        Assert.Contains(bobSees, np => np.Name == "Alice");
    }

    [Fact]
    public void Space_StructureEditOnOwnShip_IsBroadcastToTheOtherPilot()
    {
        var transport = new RecordingTransport();
        var server = NewServer("space_edit", transport, c => c.Rules.FreeSpaceFlight = true);

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        server.EnterSpace("Alice");
        server.EnterSpace("Bob"); // shared instance

        alice.State.InEva = true;        // hull editing is gated to a spacewalk
        alice.State.InstantBuild = true; // free placement for the test

        // Find a solid hull cell on Alice's ship.
        int sx = -1, sy = -1, sz = -1;
        for (int x = 0; x < 24 && sx < 0; x++)
            for (int y = 0; y < 24 && sx < 0; y++)
                for (int z = 0; z < 24 && sx < 0; z++)
                {
                    if (server.StructureBlockForTest("Alice", x, y, z) != 0) { sx = x; sy = y; sz = z; }
                }
        Assert.True(sx >= 0, "Alice's ship structure should have a solid cell.");

        transport.Sent.Clear();
        server.HandleStructureEditForTest("Alice",
            new StructureEditIntent { StructureId = "ship:Alice", X = sx, Y = sy, Z = sz, Mine = true });

        bool bobSawHullEdit = transport.Sent.Any(x => x.Conn == bob.ConnectionId
            && x.Msg is StructureBlockChanged sc && sc.StructureId == "ship:Alice"
            && sc.X == sx && sc.Y == sy && sc.Z == sz && sc.Block == BlockId.AirValue);
        Assert.True(bobSawHullEdit, "Bob must receive Alice's ship-hull edit (StructureBlockChanged) in the shared instance.");
    }

    // ---------------- Station interiors ----------------

    [Fact]
    public void StationInterior_TwoBoardersAreCoLocated_AndSeeEachOthersPresence()
    {
        var transport = new RecordingTransport();
        var server = NewServer("station_vis", transport, c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.World = new WorldDescription { SpaceStations = Frequency.Frequent };
            c.PlaceSettlements = false;
            c.PlaceWrecks = false;
        });

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");

        string stationId = BoardFirstStation(server, "Alice");
        server.EnterSpace("Bob");
        BoardSpecificStation(server, "Bob", stationId); // both board the SAME station

        Assert.True(server.InStation("Alice"));
        Assert.True(server.InStation("Bob"));

        // Co-location: both are now in the same station void world (same authoritative location id).
        Assert.StartsWith("station:", alice.CurrentLocationId);
        Assert.Equal(alice.CurrentLocationId, bob.CurrentLocationId);

        // Presence cross-broadcast inside the station world.
        transport.Sent.Clear();
        server.Tick(0.2);

        bool aliceSeesBob = transport.Sent.Any(x => x.Conn == alice.ConnectionId
            && x.Msg is PlayerPresence p && p.PlayerId == "Bob");
        bool bobSeesAlice = transport.Sent.Any(x => x.Conn == bob.ConnectionId
            && x.Msg is PlayerPresence p && p.PlayerId == "Alice");
        Assert.True(aliceSeesBob, "Alice must see Bob's presence aboard the same station.");
        Assert.True(bobSeesAlice, "Bob must see Alice's presence aboard the same station.");
    }

    [Fact]
    public void PlayerBuiltStation_TwoBoardersAreCoLocated_AndSeeEachOthersPresence()
    {
        var transport = new RecordingTransport();
        var server = NewServer("pstation_vis", transport, c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.PlaceSettlements = false;
            c.PlaceWrecks = false;
        });

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");

        // Both pilots share one space instance so the commissioned station's dock contact is visible to
        // both (a freshly commissioned in-session station lives in its instance + registry; an empty
        // instance is torn down on board, so keep Bob present while it stands).
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");

        // Alice deploys + builds out a station and commissions it (item 20 S4).
        alice.State.InEva = true;
        alice.State.InstantBuild = true;
        server.DeployStationCoreForTest("Alice");
        string id = server.OwnedStationIdForTest("Alice")!;
        for (int i = 1; i <= 11; i++)
        {
            server.HandleStructureEditForTest("Alice",
                new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
        }
        server.HandleStructureEditForTest("Alice",
            new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });
        Assert.True(server.StationIsBoardableForTest(id));

        // Player-built stations are private (owner + allies may board), so ally Bob with the owner Alice first —
        // this also covers that an ally can board another player's station.
        server.RequestAlliance("Alice", "Bob");
        server.RespondAlliance("Bob", "Alice", accept: true);
        Assert.True(server.AreAllied("Alice", "Bob"));

        // Both pilots board the same player-built station (Bob is still in the instance, so he sees the contact).
        BoardSpecificStation(server, "Alice", id);
        BoardSpecificStation(server, "Bob", id);

        Assert.True(server.InStation("Alice"));
        Assert.True(server.InStation("Bob"));
        Assert.Equal(alice.CurrentLocationId, bob.CurrentLocationId);

        transport.Sent.Clear();
        server.Tick(0.2);

        bool aliceSeesBob = transport.Sent.Any(x => x.Conn == alice.ConnectionId
            && x.Msg is PlayerPresence p && p.PlayerId == "Bob");
        bool bobSeesAlice = transport.Sent.Any(x => x.Conn == bob.ConnectionId
            && x.Msg is PlayerPresence p && p.PlayerId == "Alice");
        Assert.True(aliceSeesBob, "Alice must see Bob aboard the same player-built station.");
        Assert.True(bobSeesAlice, "Bob must see Alice aboard the same player-built station.");
    }

    // ---------------- Helpers ----------------

    /// <summary>Finds the topmost mineable block in a player's own column (matches the integration-test approach).</summary>
    private static Vector3i? FindMineableUnder(SvGameServer server, BlocksBeyondTheStars.Shared.State.PlayerState p)
    {
        int px = (int)System.Math.Floor(p.Position.X);
        int pz = (int)System.Math.Floor(p.Position.Z);
        int topY = (int)System.Math.Ceiling(p.Position.Y);
        for (int y = topY; y > topY - 14; y--)
        {
            var pos = new Vector3i(px, y, pz);
            var b = server.World.GetBlock(pos);
            if (!b.IsAir && server.World.Definition(b) is { Mineable: true })
            {
                return pos;
            }
        }

        return null;
    }

    /// <summary>Launches the player into space and boards the first station contact; returns its id.</summary>
    private static string BoardFirstStation(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == CombatEntityKind.SpaceStation);
        BoardSpecificStation(server, playerId, station.Id);
        return station.Id;
    }

    /// <summary>Flies the player up to a specific station contact and boards it (the board-range check is real).</summary>
    private static void BoardSpecificStation(SvGameServer server, string playerId, string stationId)
    {
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Id == stationId);
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(playerId, stationId);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var r in _repos)
            {
                r.Dispose();
            }

            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
