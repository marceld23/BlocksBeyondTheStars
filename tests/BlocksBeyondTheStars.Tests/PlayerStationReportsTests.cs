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
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player-built station fixes from Lyxette's 2026-09-02 reports: the hull design reaches re-entering pilots
/// (#1470), the deploy sequence survives a restart (#1478), filler crew needs a crew space (#1472), the
/// interior breathes only inside a sealed pocket (#1473), and the tractor range comes from data (#1477).
/// </summary>
public sealed class PlayerStationReportsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlayerStationReportsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pstrep_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Deploys a core and builds the placeables test's thin line hull (core + 11 walls + door). Not
    /// sealed — just enough to commission. Leaves the player on an EVA beside the station.</summary>
    private static string BuildLineStation(SvGameServer server, PlayerSession pilot, string? vendorItem = null)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int i = 1; i <= 11; i++)
        {
            Edit(server, playerId, id, i, 0, 0, "iron_wall");
        }

        Edit(server, playerId, id, 0, 1, 0, "door_slide");
        if (vendorItem != null)
        {
            Edit(server, playerId, id, 1, 1, 0, vendorItem);
        }

        Assert.True(server.StationIsBoardableForTest(id));
        return id;
    }

    /// <summary>Deploys a core and builds a SEALED 5×5×5 iron shell around it (cells −2…2, the core at the
    /// centre), with a slide door in one wall — the smallest hull whose interior should hold air.</summary>
    private static string BuildSealedBox(SvGameServer server, PlayerSession pilot, string? vendorItem = null)
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
                    bool doorway = x == 2 && y == -1 && z == 0; // left open for the airlock (placing needs an empty cell)
                    if (shell && !doorway)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, playerId, id, 2, -1, 0, "door_slide"); // the airlock fills the doorway in the +X wall
        if (vendorItem != null)
        {
            Edit(server, playerId, id, 1, -1, 0, vendorItem); // a post on the floor inside the sealed room
        }

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

    /// <summary>Ticks with the player pinned at <paramref name="at"/> — long enough to cover the sealed-volume
    /// recompute interval (1.5 s) at least once.</summary>
    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int halfSeconds = 6)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    // ---------------- #1470: the hull design is re-sent on re-entry ----------------

    [Fact]
    public void StationDesign_IsResent_WhenSpaceIsReEntered()
    {
        var transport = new RecordingTransport();
        var server = NewServer("resend", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildLineStation(server, pilot);
            Assert.Contains(transport.Sent, m => m is SpaceShipDesign d && d.Id == id && d.Kind == "station");

            // Land (the instance is torn down with its last pilot) and launch again: the station's voxel hull
            // must arrive again, not only its dock contact — before this only asteroids were re-sent.
            server.LeaveSpace("Owner");
            transport.Sent.Clear();
            server.EnterSpace("Owner");

            Assert.Contains(server.SpaceEntitiesFor("Owner"), e => e.Id == id); // the contact is back …
            Assert.Contains(transport.Sent, m => m is SpaceShipDesign d && d.Id == id && d.Kind == "station"); // … and so is the hull
        }
    }

    // ---------------- #1478: the deploy sequence survives a restart ----------------

    [Fact]
    public void StationSequence_IsSeededFromPersistedIds_AfterRestart()
    {
        string first;
        {
            var s1 = NewServer("seq", out var repo1);
            using (repo1)
            {
                var owner = s1.AddLocalPlayer("Owner");
                first = BuildLineStation(s1, owner);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("seq", out var repo2);
        using (repo2)
        {
            Assert.True(s2.StationIsBoardableForTest(first));
            var owner = s2.AddLocalPlayer("Owner");
            s2.EnterSpace("Owner");
            owner.State.InEva = true;
            owner.State.InstantBuild = true;
            s2.DeployStationCoreForTest("Owner");

            // The new core must NOT reuse the restored station's id (same entity, boardable + interior key).
            var ids = s2.OwnedStationIdsForTest("Owner");
            Assert.Equal(2, ids.Count);
            Assert.Contains(first, ids);
            Assert.Single(ids.Where(i => i != first));
        }
    }

    // ---------------- #1472: filler crew needs a crew space ----------------

    [Fact]
    public void FreshPlayerStation_HasNoFillerCrew()
    {
        var server = NewServer("nocrew", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildLineStation(server, pilot);
            BoardOwnStation(server, "Owner", id);
            Assert.Empty(server.NpcSnapshots);
        }
    }

    [Fact]
    public void ATradingPost_InASealedRoom_BringsTheVendorAndTheFillerCrew()
    {
        var server = NewServer("crew", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot, vendorItem: "station_vendor");
            BoardOwnStation(server, "Owner", id);

            var roles = server.NpcSnapshots.Select(n => n.Role).ToList();
            Assert.Contains("vendor", roles);
            Assert.Equal(2, roles.Count(r => r == "settler")); // the "small" tier's two civilians, now around the post
        }
    }

    // ---------------- #1487: nobody staffs a post that is open to space ----------------

    [Fact]
    public void ATradingPost_InTheVacuum_StaysUnstaffed_AndTheBoarderIsTold()
    {
        var transport = new RecordingTransport();
        var server = NewServer("vacuumpost", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildLineStation(server, pilot, vendorItem: "station_vendor"); // a line of walls: no room, no air
            BoardOwnStation(server, "Owner", id);

            Assert.Empty(server.NpcSnapshots);
            Assert.Contains(transport.Sent, m => m is ServerMessage sm && sm.Text == "@srv.station.staff_needs_air");
        }
    }

    [Fact]
    public void ATradingPost_LosesItsCrew_WhenTheRoomIsBreached_AndGetsItBack_WhenPlugged()
    {
        var server = NewServer("breachpost", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot, vendorItem: "station_vendor");
            BoardOwnStation(server, "Owner", id);
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");

            // Knock the top centre block out: the room leaks → the next staffing check clears the post.
            var hole = BoxWorld(0, 2, 0);
            server.World.SetBlock(hole, BlockId.Air);
            TickAt(server, pilot, pilot.State.Position, halfSeconds: 12);
            Assert.DoesNotContain(server.NpcSnapshots, n => n.Role == "vendor");

            // Plug it with a force field: sealed again → the vendor is back at the post.
            server.World.SetBlock(hole, _content.GetBlock("force_field")!.NumericId);
            TickAt(server, pilot, pilot.State.Position, halfSeconds: 12);
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
        }
    }

    // ---------------- #1473: sealed-volume air ----------------

    [Fact]
    public void SealedStation_Breathes_AHoleLeaks_AndAForceFieldPlugsIt()
    {
        var transport = new RecordingTransport();
        var server = NewServer("air", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            BoardOwnStation(server, "Owner", id);
            Assert.False(server.AtmosphereBreathable, "a player station's deck is not free air");

            // Build cell (x,y,z) → world (x+10, y+66, z+10): origin (8,64,8) minus the −2 build minimum.
            var spawn = new Vector3f(10.5f, 65.03f, 10.5f);
            Assert.True(server.StationCellSealedForTest(id, new Vector3i(10, 65, 10)));

            float inside = pilot.State.Oxygen = 50f;
            TickAt(server, pilot, spawn);
            Assert.True(pilot.State.Oxygen > inside, $"Oxygen should refill inside the sealed hull (was {pilot.State.Oxygen}).");
            Assert.DoesNotContain(transport.Sent, m => m is ServerMessage sm && sm.Text == "@station_air_lost");

            // Knock the top centre block out: the pocket reaches the void → helmet on, and a one-shot warning.
            var hole = new Vector3i(10, 68, 10);
            server.World.SetBlock(hole, BlockId.Air);
            float holed = pilot.State.Oxygen = 80f;
            TickAt(server, pilot, spawn, halfSeconds: 8);
            Assert.True(pilot.State.Oxygen < holed, $"Oxygen should drain once the hull has a hole (was {pilot.State.Oxygen}).");
            Assert.Single(transport.Sent.Where(m => m is ServerMessage sm && sm.Text == "@station_air_lost"));

            // A force-field block in the gap is airtight: the pocket seals again.
            server.World.SetBlock(hole, _content.GetBlock("force_field")!.NumericId);
            float plugged = pilot.State.Oxygen = 50f;
            TickAt(server, pilot, spawn, halfSeconds: 8);
            Assert.True(pilot.State.Oxygen > plugged, $"Oxygen should refill once the hole is plugged (was {pilot.State.Oxygen}).");
        }
    }

    [Fact]
    public void OpenHull_NeverBreathes_ButAnNpcStationStillDoes()
    {
        var server = NewServer("openhull", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildLineStation(server, pilot); // a line of walls is no room at all
            BoardOwnStation(server, "Owner", id);

            float before = pilot.State.Oxygen = 80f;
            TickAt(server, pilot, pilot.State.Position, halfSeconds: 8);
            Assert.True(pilot.State.Oxygen < before, $"An open hull must not breathe (was {pilot.State.Oxygen}).");
        }
    }

    // ---------------- #1477: the tractor range comes from data ----------------

    // ---------------- #1480: a player station is a contact only in the orbit it was built in ----------------

    [Fact]
    public void PlayerStation_IsNotAContact_InAnotherOrbitOfTheSameSystem()
    {
        var server = NewServer("orbit", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildLineStation(server, pilot);
            server.LeaveSpace("Owner");

            var here = server.Galaxy.FindBody(server.StationHostBodyForTest(id))!; // a real body id, not the launch placeholder
            var system = server.Galaxy.Systems.First(s => s.Id == here.SystemId);
            var other = system.Bodies.First(b => b.Id != here.Id && !string.IsNullOrEmpty(b.PlanetType));
            server.Travel("Owner", other.Id);
            Assert.Equal(other.Id, pilot.CurrentLocationId);

            server.EnterSpace("Owner");
            Assert.DoesNotContain(server.SpaceEntitiesFor("Owner"), e => e.Id == id); // not "versetzt" into the next orbit

            // …and back home it is exactly where it was built.
            server.LeaveSpace("Owner");
            server.Travel("Owner", here.Id);
            server.EnterSpace("Owner");
            Assert.Contains(server.SpaceEntitiesFor("Owner"), e => e.Id == id);
        }
    }

    // ---------------- #1481: interior edits survive a restart ----------------

    [Fact]
    public void InteriorEdits_AreWrittenBackToTheStation_AndSurviveARestart()
    {
        var glass = _content.GetBlock("glass")!.NumericId;
        var roof = BoxWorld(0, 2, 0);      // the top centre wall block
        var shelf = BoxWorld(0, 0, 1);     // an interior air cell
        string id;
        {
            var s1 = NewServer("interior", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildSealedBox(s1, pilot);
                BoardOwnStation(s1, "Owner", id);

                s1.MineBlock("Owner", roof.X, roof.Y, roof.Z);
                Assert.True(s1.World.GetBlock(roof).IsAir);
                s1.PlaceBlock("Owner", shelf.X, shelf.Y, shelf.Z, "glass");
                Assert.Equal(glass.Value, s1.World.GetBlock(shelf).Value);

                // The cell grid follows the world: the roof cell is gone, the glass is a cell of the build now.
                var cells = s1.StationCellsForTest(id);
                Assert.False(cells.ContainsKey(new Vector3i(0, 2, 0)));
                Assert.Equal(glass.Value, cells[new Vector3i(0, 0, 1)].Value);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("interior", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true; // the save left him boarded on the station; launch from the ship again
            s2.EnterSpace("Owner");
            Assert.True(s2.SpaceEntitiesFor("Owner").Any(e => e.Id == id),
                $"the restored station must float in its host orbit again (host '{s2.StationHostBodyForTest(id)}', pilot at '{pilot.CurrentLocationId}', contacts: {string.Join(", ", s2.SpaceEntitiesFor("Owner").Select(e => e.Id))})");
            BoardOwnStation(s2, "Owner", id);

            // Before: the restart re-stamped the original cells and put the roof block back over the hole.
            Assert.True(s2.World.GetBlock(roof).IsAir, "a mined interior block must stay mined after a restart");
            Assert.Equal(glass.Value, s2.World.GetBlock(shelf).Value);
        }
    }

    [Fact]
    public void TractorBeam_DeclaresThePassiveRange_TheServerUses()
    {
        var beam = _content.GetShipModule("tractor_beam");
        Assert.NotNull(beam);
        Assert.Equal(16.0, beam!.Stats["tractor_range"]);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
