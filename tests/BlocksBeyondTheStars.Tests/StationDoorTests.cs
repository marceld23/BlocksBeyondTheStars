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
/// #2607, Screelit: "On the ground a door is a door — in space a door becomes a block!" A door built onto a player
/// station on a spacewalk is a real door now (a door record, like a ship's #2119), the station's inside is refreshed on
/// every boarding (outside work no longer waits for a restart), and E beside one of its doors boards through it.
/// </summary>
public sealed class StationDoorTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StationDoorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_stdoor_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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

    private static void Edit(SvGameServer server, string id, int x, int y, int z, string item)
        => server.HandleStructureEditForTest("Owner",
            new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

    private static void Mine(SvGameServer server, string id, int x, int y, int z)
        => server.HandleStructureEditForTest("Owner", new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = true });

    /// <summary>The sealed 5×5×5 iron shell of the station tests (cells −2…2, the core in the middle) with a door in the
    /// +X wall at (2, −1, 0), built on a spacewalk. <paramref name="hole"/> leaves one more shell cell open.</summary>
    private static string BuildBox(SvGameServer server, PlayerSession pilot, string doorItem = "door_slide", Vector3i? hole = null)
    {
        server.EnterSpace("Owner");
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest("Owner");
        string id = server.OwnedStationIdForTest("Owner")!;
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    var c = new Vector3i(x, y, z);
                    bool shell = Math.Abs(x) == 2 || Math.Abs(y) == 2 || Math.Abs(z) == 2;
                    if (shell && c != Door && c != hole)
                    {
                        Edit(server, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, id, Door.X, Door.Y, Door.Z, doorItem);
        return id;
    }

    private static readonly Vector3i Door = new(2, -1, 0);

    /// <summary>Build cell → interior-world cell: origin (8, 64, 8) minus the −2 build minimum.</summary>
    private static Vector3i BoxWorld(int x, int y, int z) => new(x + 10, y + 66, z + 10);

    private static Vector3i BoxWorld(Vector3i c) => BoxWorld(c.X, c.Y, c.Z);

    /// <summary>Docks by ship (no spacewalk) — the normal arrival next to the core.</summary>
    private static void DockByShip(SvGameServer server, PlayerSession pilot, string stationId)
    {
        if (!server.InSpace("Owner"))
        {
            server.EnterSpace("Owner");
        }

        pilot.State.InEva = false;
        var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == stationId);
        server.ShipMove("Owner", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation("Owner", stationId);
        Assert.True(server.InStation("Owner"));
    }

    /// <summary>Back outside on a spacewalk, floating beside the station.</summary>
    private static void OutOnASpacewalk(SvGameServer server, PlayerSession pilot, string stationId)
    {
        if (server.InStation("Owner"))
        {
            server.LeaveStation("Owner");
        }

        pilot.State.InEva = true;
        var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == stationId);
        server.ShipMove("Owner", contact.Position.X + 6f, contact.Position.Y, contact.Position.Z);
    }

    [Fact]
    public void ADoorOnAStationSpacewalk_IsADoorRecord_AndCommissionsTheStation()
    {
        var server = NewServer("record", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);

            Assert.False(server.StationCellsForTest(id).ContainsKey(Door), "the doorway stays air — no door block");
            var door = Assert.Single(server.StationDoorsForTest(id));
            Assert.Equal((Door, "slide", false), door); // in the +X wall: the wall runs along Z
            Assert.True(server.StationIsBoardableForTest(id), "a door record alone is the airlock");
        }
    }

    [Fact]
    public void AnOldDoorBlock_IsStillTheAirlock()
    {
        // Doors built as blocks before #2607 stay blocks (no migration) — and keep counting for the commission rule.
        var server = NewServer("oldblock", out var repo);
        using (repo)
        {
            var station = new SpaceStructure { Kind = "station" };
            Assert.False(server.StationHasAirlockForTest(station));
            station.Set(new Vector3i(0, 1, 0), _content.GetBlock("door_slide")!.NumericId);
            Assert.True(server.StationHasAirlockForTest(station));
        }
    }

    [Fact]
    public void ADoorRecord_IsAWorkingDoorAboard_AndTheRoomHoldsAir()
    {
        var server = NewServer("aboard", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot, "door_energy");
            DockByShip(server, pilot, id);

            var w = BoxWorld(Door);
            Assert.True(server.World.GetBlock(w).IsAir, "a doorway, not a door block");
            var hung = Assert.Single(server.DoorFits.Where(d => d.PlayerBuilt));
            Assert.Equal("energy", hung.Kind);
            Assert.Equal((w.X, w.Y, w.Z), ((int)Math.Floor(hung.Pos.X), (int)Math.Floor(hung.Pos.Y), (int)Math.Floor(hung.Pos.Z)));
            Assert.True(server.StationCellSealedForTest(id, BoxWorld(0, -1, 1)), "the door seals the room");
        }
    }

    [Fact]
    public void TheDoorRecord_SurvivesARestart_AsADoor()
    {
        string id;
        {
            var s1 = NewServer("restart", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildBox(s1, pilot);
                s1.SetStationNameForTest(pilot, id, "Blumenhall"); // #2607: the rename keeps the row whole
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("restart", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            Assert.Equal((Door, "slide", false), Assert.Single(s2.StationDoorsForTest(id)));
            Assert.False(s2.StationCellsForTest(id).ContainsKey(Door));

            DockByShip(s2, pilot, id);
            Assert.Single(s2.DoorFits.Where(d => d.PlayerBuilt));
            Assert.True(s2.StationCellSealedForTest(id, BoxWorld(0, -1, 1)));
        }
    }

    [Fact]
    public void ADoorTakenOutOnASpacewalk_IsGoneInsideToo()
    {
        var server = NewServer("takeout", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);
            DockByShip(server, pilot, id);
            Assert.Single(server.DoorFits.Where(d => d.PlayerBuilt));

            OutOnASpacewalk(server, pilot, id);
            pilot.State.InstantBuild = false;
            int before = pilot.State.Inventory.CountOf("door_slide");
            Mine(server, id, Door.X, Door.Y, Door.Z);

            Assert.Empty(server.StationDoorsForTest(id));
            Assert.Equal(before + 1, pilot.State.Inventory.CountOf("door_slide"));

            DockByShip(server, pilot, id);
            Assert.Empty(server.DoorFits.Where(d => d.PlayerBuilt));
            Assert.False(server.StationCellSealedForTest(id, BoxWorld(0, -1, 1)), "the doorway is open now");
        }
    }

    [Fact]
    public void NothingIsBuiltIntoAStationDoorway()
    {
        var transport = new RecordingTransport();
        var server = NewServer("doorway", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);
            Edit(server, id, Door.X, Door.Y, Door.Z, "iron_wall");

            Assert.False(server.StationCellsForTest(id).ContainsKey(Door));
            Assert.Contains(transport.Sent, m => m is ActionRejected r && r.Reason == "@srv.place.door_here");
        }
    }

    [Fact]
    public void TheStationDesign_CarriesItsDoors_AndItsOwner()
    {
        var transport = new RecordingTransport();
        var server = NewServer("design", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot, "door_energy");

            var design = transport.Sent.OfType<SpaceShipDesign>().Last(d => d.Id == id);
            Assert.Equal("station", design.Kind);
            Assert.Equal(new[] { Door.X }, design.DoorX);
            Assert.Equal(new[] { Door.Y }, design.DoorY);
            Assert.Equal(new[] { "energy" }, design.DoorKind);
            Assert.Equal(new[] { false }, design.DoorAxisX);
            Assert.Equal("Owner", design.OwnerId);
            Assert.Empty(design.HatchX); // a station has no design hatch
        }
    }

    [Fact]
    public void BuildingFromOutside_AfterABoarding_IsInsideOnTheNextBoarding()
    {
        // Screelit's station had no air: its inside was stamped once per server run, so the wall he closed from outside
        // after the first boarding was not there inside.
        var server = NewServer("outside", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            var roof = new Vector3i(0, 2, 0);
            string id = BuildBox(server, pilot, hole: roof);
            DockByShip(server, pilot, id);
            Assert.False(server.StationCellSealedForTest(id, BoxWorld(0, -1, 1)), "the roof is open");

            OutOnASpacewalk(server, pilot, id);
            Edit(server, id, roof.X, roof.Y, roof.Z, "iron_wall");

            DockByShip(server, pilot, id);
            Assert.False(server.World.GetBlock(BoxWorld(roof)).IsAir, "the wall built outside is there inside");
            Assert.True(server.StationCellSealedForTest(id, BoxWorld(0, -1, 1)), "and the room holds air now");
        }
    }

    [Fact]
    public void AWallMinedFromOutside_StaysGone_AcrossARestart()
    {
        // The interior learns the mined wall at once — before, the next stamp's absorb pass took it back from the world,
        // and a commissioned station did not even save the mined cell.
        var wall = new Vector3i(0, 2, 0);
        string id;
        {
            var s1 = NewServer("mined", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildBox(s1, pilot);
                DockByShip(s1, pilot, id);
                OutOnASpacewalk(s1, pilot, id);
                Mine(s1, id, wall.X, wall.Y, wall.Z);
                Assert.False(s1.StationCellsForTest(id).ContainsKey(wall));
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("mined", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true; // the save left them on the spacewalk; back at the helm to fly over
            pilot.State.InEva = false;
            Assert.False(s2.StationCellsForTest(id).ContainsKey(wall), "the row saved the mined cell");
            DockByShip(s2, pilot, id);
            Assert.True(s2.World.GetBlock(BoxWorld(wall)).IsAir, "the interior lost the wall too");
            Assert.False(s2.StationCellsForTest(id).ContainsKey(wall), "the absorb pass did not take it back");
        }
    }

    [Fact]
    public void AnOutsideEdit_ReachesABoarderInside_AtOnce()
    {
        // While someone is aboard, the interior world is loaded: the edit lands there live.
        var server = NewServer("live", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            var roof = new Vector3i(0, 2, 0);
            string id = BuildBox(server, pilot, hole: roof);

            var mate = server.AddLocalPlayer("Mate");
            mate.State.Role = BlocksBeyondTheStars.Shared.State.PlayerRole.Admin; // may board the owner's station
            server.Travel("Mate", id);
            Assert.True(server.InStation("Mate"));

            Edit(server, id, roof.X, roof.Y, roof.Z, "iron_wall");

            Assert.True(server.ActivateWorldForTest("station:" + id));
            Assert.False(server.World.GetBlock(BoxWorld(roof)).IsAir, "the boarder sees the new roof cell");

            // A closed room built from outside well past the old box (its inside 8 + cells beyond it): the station's
            // reach grows with it at once, so the boarder breathes there — not only after the next boarding.
            for (int x = 8; x <= 12; x++)
                for (int y = -2; y <= 2; y++)
                    for (int z = -2; z <= 2; z++)
                    {
                        if (x == 8 || x == 12 || Math.Abs(y) == 2 || Math.Abs(z) == 2)
                        {
                            Edit(server, id, x, y, z, "iron_wall");
                        }
                    }

            Assert.True(server.ActivateWorldForTest("station:" + id));
            Assert.True(server.StationCellSealedForTest(id, BoxWorld(10, 0, 0)), "the new room holds air for the boarder");
        }
    }

    [Fact]
    public void ACoreTenWallsAndADoor_StillCommission()
    {
        // A door is a record now, no cell — it still counts as the block its door block was.
        var server = NewServer("ten", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            server.EnterSpace("Owner");
            pilot.State.InEva = true;
            pilot.State.InstantBuild = true;
            server.DeployStationCoreForTest("Owner");
            string id = server.OwnedStationIdForTest("Owner")!;
            for (int i = 1; i <= 10; i++)
            {
                Edit(server, id, i, 0, 0, "iron_wall");
            }

            Assert.False(server.StationIsBoardableForTest(id)); // eleven blocks, no airlock yet
            Edit(server, id, 0, 1, 0, "door_slide");
            Assert.Single(server.StationDoorsForTest(id));
            Assert.True(server.StationIsBoardableForTest(id));
        }
    }

    [Fact]
    public void NoArrival_InsideADoorBesideTheCore()
    {
        // The nearest standable cell beside the core is (−1, −1, 0) — with a door in it, the arrival stood inside the
        // door's leaf (a door is no block to the standing test).
        var server = NewServer("doorbycore", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);
            Edit(server, id, -1, -1, 0, "door_hinge");
            Assert.Equal(2, server.StationDoorsForTest(id).Count);

            DockByShip(server, pilot, id);

            var feet = pilot.State.Position.ToBlock();
            Assert.NotEqual(BoxWorld(-1, -1, 0), feet);
            AssertBesideTheCore(server, id, feet);
        }
    }

    [Fact]
    public void BoardingAtTheDoor_NeverStopsInsideASecondDoor()
    {
        // An airlock: the outer door and right behind it a second one. Through the outer door the first cell in is the
        // inner door's — not an arrival; with no other spot behind it the boarder arrives by the core.
        var server = NewServer("airlock", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);
            Edit(server, id, 1, -1, 0, "door_hinge");

            var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == id);
            server.ShipMove("Owner", contact.Position.X + 3.5f, contact.Position.Y, contact.Position.Z);
            server.BoardStation("Owner", id);

            Assert.True(server.InStation("Owner"));
            var feet = pilot.State.Position.ToBlock();
            Assert.NotEqual(BoxWorld(1, -1, 0), feet);
            Assert.NotEqual(BoxWorld(Door), feet);
            AssertBesideTheCore(server, id, feet);
        }
    }

    [Fact]
    public void EAtADoor_AfterTheHullGrew_StillFindsIt_FromTheSpacewalksReach()
    {
        // A wing built past the old box moves the box centre the flight view draws the hull around: the design is
        // re-sent, so the client and the server agree where the door is. And the server's reach covers every door the
        // client aims at (up to 6 blocks to the cell face).
        var transport = new RecordingTransport();
        var server = NewServer("grown", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);

            transport.Sent.Clear();
            Edit(server, id, 3, -2, 0, "iron_wall");
            Edit(server, id, 4, -2, 0, "iron_wall");
            Assert.Contains(transport.Sent, m => m is SpaceShipDesign d && d.Id == id && d.X.Contains(4)); // re-centred

            // Box x −2…4 → centre 1.5: the doorway (2, −1, 0) opens at position + (1, 0, 0). The suit floats 5.5 out.
            var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == id);
            server.ShipMove("Owner", contact.Position.X + 6.5f, contact.Position.Y, contact.Position.Z);
            server.BoardStation("Owner", id);

            Assert.True(server.InStation("Owner"));
            var inside = BoxWorld(1, -1, 0);
            Assert.Equal(new Vector3f(inside.X + 0.5f, inside.Y, inside.Z + 0.5f), pilot.State.Position);
        }
    }

    /// <summary>The #2606 arrival: within two cells of the core, in its sealed room, in no doorway.</summary>
    private static void AssertBesideTheCore(SvGameServer server, string id, Vector3i feet)
    {
        var core = BoxWorld(0, 0, 0);
        Assert.True(Math.Abs(feet.X - core.X) <= 2 && Math.Abs(feet.Y - core.Y) <= 2 && Math.Abs(feet.Z - core.Z) <= 2,
            $"arrived at {feet}, not beside the core at {core}");
        Assert.True(server.StationCellSealedForTest(id, feet), "in the core's sealed room");
        Assert.DoesNotContain(server.DoorFits, d => d.PlayerBuilt && (int)Math.Floor(d.Pos.X) == feet.X
            && (int)Math.Floor(d.Pos.Z) == feet.Z && feet.Y >= (int)Math.Floor(d.Pos.Y) && feet.Y <= (int)Math.Floor(d.Pos.Y) + 2);
    }

    [Fact]
    public void ADoorBuiltAboard_BecomesARecord_SeenFromOutside()
    {
        var server = NewServer("insidedoor", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);
            DockByShip(server, pilot, id);

            // A door hung on foot in the room's corner (against two walls), the way a door is built aboard.
            pilot.State.Inventory.Add("door_energy", 2, 16);
            var inner = new Vector3i(-1, -1, 1);
            var stand = BoxWorld(0, -1, 0);
            pilot.State.Position = new Vector3f(stand.X + 0.5f, stand.Y + 0.03f, stand.Z + 0.5f);
            var w = BoxWorld(inner);
            server.PlaceBlock("Owner", w.X, w.Y, w.Z, "door_energy");
            Assert.Contains(server.DoorFits, d => d.PlayerBuilt && d.Kind == "energy" && (int)Math.Floor(d.Pos.X) == w.X);

            Assert.Contains(server.StationDoorsForTest(id), d => d.Cell == inner && d.Kind == "energy");

            // …and taken out aboard, it leaves the records again.
            server.MineBlockOnce("Owner", w.X, w.Y + 1, w.Z);
            Assert.DoesNotContain(server.StationDoorsForTest(id), d => d.Cell == inner);
        }
    }

    [Fact]
    public void EBesideAStationDoor_BoardsThroughIt_OnItsSealedInside()
    {
        var server = NewServer("atdoor", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);

            // The station floats with its cell box centred on its position: box −2…2 → centre 0.5, so the doorway
            // (2, −1, 0) opens at position + (2, 0, 0). The suit floats just outside it.
            var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == id);
            server.ShipMove("Owner", contact.Position.X + 3.5f, contact.Position.Y, contact.Position.Z);
            server.BoardStation("Owner", id);

            Assert.True(server.InStation("Owner"));
            var inside = BoxWorld(1, -1, 0); // one step in from the doorway, on the floor
            Assert.Equal(new Vector3f(inside.X + 0.5f, inside.Y, inside.Z + 0.5f), pilot.State.Position);
        }
    }

    [Fact]
    public void DockingByShip_StillArrivesBesideTheCore()
    {
        var server = NewServer("bycore", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot);
            DockByShip(server, pilot, id);

            var feet = pilot.State.Position.ToBlock();
            Assert.NotEqual(BoxWorld(1, -1, 0), feet); // not at the door
            AssertBesideTheCore(server, id, feet);
        }
    }

    [Fact]
    public void ADoorIntoTheVoid_FallsBackToTheCoreArrival()
    {
        // A door whose inside is no sealed room (the roof is open): the suit boards, but arrives by the core.
        var server = NewServer("voiddoor", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildBox(server, pilot, hole: new Vector3i(0, 2, 0));

            var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == id);
            server.ShipMove("Owner", contact.Position.X + 3.5f, contact.Position.Y, contact.Position.Z);
            server.BoardStation("Owner", id);

            Assert.True(server.InStation("Owner"));
            Assert.NotEqual(BoxWorld(1, -1, 0), pilot.State.Position.ToBlock());
        }
    }
}
