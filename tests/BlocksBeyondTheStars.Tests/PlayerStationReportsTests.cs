// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo, IServerTransport? transport = null, bool placeStarterShip = false,
        IGameLogger? logger = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = placeStarterShip };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo, logger);
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
            Assert.Contains(transport.Messages(), m => m is SpaceShipDesign d && d.Id == id && d.Kind == "station");

            // Land (the instance is torn down with its last pilot) and launch again: the station's voxel hull
            // must arrive again, not only its dock contact — before this only asteroids were re-sent.
            server.LeaveSpace("Owner");
            transport.Sent.Clear();
            server.EnterSpace("Owner");

            Assert.Contains(server.SpaceEntitiesFor("Owner"), e => e.Id == id); // the contact is back …
            Assert.Contains(transport.Messages(), m => m is SpaceShipDesign d && d.Id == id && d.Kind == "station"); // … and so is the hull
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

    [Fact]
    public void TheCrew_KeepsTheStationClock_RestsAtStationNight_AndStaysInTheRoom()
    {
        // #1867: a station has no sun, but its crew keeps the station clock (no longitude on a deck) — without a bunk
        // in the room they rest where they stand at station night, and back on duty by day. Nobody leaves the air.
        var server = NewServer("crewnight", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot, vendorItem: "station_vendor");
            BoardOwnStation(server, "Owner", id);
            var inside = new Vector3f(10.5f, 65.03f, 10.5f);
            Assert.Equal(server.LocalDayFractionForTest(0f), server.LocalDayFractionForTest(5000f), 6); // one clock aboard

            for (int i = 0; i < 20; i++)
            {
                server.SetLocalDayFractionForTest(0.9, 0f);
                pilot.State.Position = inside;
                server.TickForTest(0.5);
            }

            var vendor = server.NpcSnapshots.First(n => n.Role == "vendor");
            Assert.Equal("npc.activity.resting", server.NpcRoutineForTest(vendor.Id).Activity);
            foreach (var npc in server.NpcSnapshots)
            {
                Assert.True(server.StationCellSealedForTest(id, Feet(npc.Pos)), $"{npc.Role} #{npc.Id} left the room at night: {npc.Pos}");
            }

            for (int i = 0; i < 20; i++)
            {
                server.SetLocalDayFractionForTest(0.45, 0f);
                pilot.State.Position = inside;
                server.TickForTest(0.5);
            }

            Assert.Equal(string.Empty, server.NpcRoutineForTest(vendor.Id).Activity);
        }
    }

    // ---------------- #1775: the crew stays inside the hull ----------------

    private static Vector3i Feet(Vector3f pos)
        => new((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z));

    [Fact]
    public void Crew_StandsOnTheFloor_InsideTheSealedRoom()
    {
        // Lyxette (v2026.9.5): "Hier läuft einer außerhalb der Eisenmauer herum" — the filler crew was homed at
        // the post ± 2 blocks with no standable and no air check, so a post beside the hull put a settler inside
        // the wall or beyond it, and the post keeper's feet sat inside the vendor block itself (#1775).
        var server = NewServer("crewfloor", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot, vendorItem: "station_vendor");
            BoardOwnStation(server, "Owner", id);

            var crew = server.NpcSnapshots;
            Assert.Equal(3, crew.Count); // the vendor + the small tier's two civilians
            foreach (var npc in crew)
            {
                var feet = Feet(npc.Pos);
                Assert.True(server.StationCellSealedForTest(id, feet), $"{npc.Role} #{npc.Id} stands at {feet}, outside the sealed room");
                Assert.True(server.World.GetBlock(feet).IsAir, $"{npc.Role} #{npc.Id} stands inside a block at {feet}");
                Assert.False(server.World.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)).IsAir, $"{npc.Role} #{npc.Id} floats at {feet}");
                Assert.Equal(feet, Feet(npc.Home));
            }

            Assert.Equal(3, crew.Select(n => Feet(n.Pos)).Distinct().Count()); // nobody shares a cell
        }
    }

    [Fact]
    public void Crew_NeverWalksOutOfTheRoom_AndIsSetBackHome_WhenOutside()
    {
        var server = NewServer("crewwalk", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot, vendorItem: "station_vendor");
            BoardOwnStation(server, "Owner", id);
            var inside = new Vector3f(10.5f, 65.03f, 10.5f);

            // A minute of strolling in a 3×3 room with a door in its east wall: everyone is still in the pocket.
            TickAt(server, pilot, inside, halfSeconds: 120);
            foreach (var npc in server.NpcSnapshots)
            {
                Assert.True(server.StationCellSealedForTest(id, Feet(npc.Pos)), $"{npc.Role} #{npc.Id} left the room: {npc.Pos}");
            }

            // A crew member that somehow ends up beyond the hull (a legacy spawn, a wall built around it) is back
            // home on the next tick instead of strolling in the vacuum.
            var vendor = server.NpcSnapshots.First(n => n.Role == "vendor");
            server.MoveNpcForTest(vendor.Id, new Vector3f(10.5f + 2 + 6, 65f, 10.5f));
            TickAt(server, pilot, inside, halfSeconds: 2);
            var back = server.NpcSnapshots.First(n => n.Id == vendor.Id);
            Assert.Equal(Feet(vendor.Home), Feet(back.Pos));
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
            Assert.Contains(transport.Messages(), m => m is ServerMessage sm && sm.Text == "@srv.station.staff_needs_air");
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

    [Fact]
    public void TwoBoardedStations_EachKeepTheirOwnStaffingRhythm()
    {
        // #2235: the re-staffing throttle was one field for the server, advanced inside every occupied world's tick.
        // With two boarded stations it ran at double speed and the world that crossed the threshold first took every
        // turn — the other station never re-checked its posts, so a breached room kept its vendor forever.
        var server = NewServer("twostations", out var repo);
        using (repo)
        {
            var ann = server.AddLocalPlayer("Ann");
            var bob = server.AddLocalPlayer("Bob");
            string annStation = BuildSealedBox(server, ann, vendorItem: "station_vendor");
            string bobStation = BuildSealedBox(server, bob, vendorItem: "station_vendor");
            BoardOwnStation(server, "Ann", annStation);
            BoardOwnStation(server, "Bob", bobStation);
            Assert.True(server.ResidentWorldCount >= 2);

            foreach (string station in new[] { annStation, bobStation })
            {
                Assert.True(server.ActivateWorldForTest("station:" + station));
                Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
                server.World.SetBlock(BoxWorld(0, 2, 0), BlockId.Air); // both rooms leak now
            }

            var annAt = ann.State.Position;
            var bobAt = bob.State.Position;
            for (int i = 0; i < 12; i++)
            {
                ann.State.Position = annAt;
                bob.State.Position = bobAt;
                server.TickForTest(0.5);
            }

            foreach (string station in new[] { annStation, bobStation })
            {
                Assert.True(server.ActivateWorldForTest("station:" + station));
                Assert.DoesNotContain(server.NpcSnapshots, n => n.Role == "vendor");
            }
        }
    }

    // ---------------- #1473: sealed-volume air ----------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SealedStation_Breathes_AHoleLeaks_AndAForceFieldPlugsIt(bool starvedClock)
    {
        var transport = new RecordingTransport();
        var server = NewServer("air", out var repo, transport);
        using (repo)
        {
            // #2870: the air budget spent before the oxygen check asks (a busy runner) — the check that starts the refill
            // still runs a fixed minimum of it, so the hole and the plug count on that very tick.
            if (starvedClock)
            {
                server.AirTickBudgetMsForTest = 0;
            }

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
            Assert.Empty(HullWarnings(transport));

            // Knock the top centre block out: the pocket reaches the void → helmet on, and a one-shot warning that names the
            // hole (#2654) — where the room meets the void, at the hole's height.
            var hole = new Vector3i(10, 68, 10);
            server.World.SetBlock(hole, BlockId.Air);
            float holed = pilot.State.Oxygen = 80f;
            TickAt(server, pilot, spawn, halfSeconds: 8);
            Assert.True(pilot.State.Oxygen < holed, $"Oxygen should drain once the hull has a hole (was {pilot.State.Oxygen}).");
            var warning = Assert.Single(HullWarnings(transport));
            Assert.Contains("at 10/68/10, 3 blocks above you", warning, StringComparison.Ordinal);

            // A force-field block in the gap is airtight: the pocket seals again.
            server.World.SetBlock(hole, _content.GetBlock("force_field")!.NumericId);
            float plugged = pilot.State.Oxygen = 50f;
            TickAt(server, pilot, spawn, halfSeconds: 8);
            Assert.True(pilot.State.Oxygen > plugged, $"Oxygen should refill once the hole is plugged (was {pilot.State.Oxygen}).");
        }
    }

    /// <summary>The station hull warnings sent: the located leak line (#2654) or the general fallback.</summary>
    private static List<string> HullWarnings(RecordingTransport transport)
        => transport.Messages().OfType<ServerMessage>().Select(m => m.Text)
            .Where(t => t == "@station_air_lost" || t.Contains("this room leaks", StringComparison.Ordinal)).ToList();

    [Fact]
    public void AStationHallPastTheOld64000Cells_Breathes()
    {
        // #2652: a closed station hall of 67 240 cells counted as breached under the 64 000-cell budget (#1559). Now it
        // breathes, and a hole in its roof still leaks — named where it is.
        var transport = new RecordingTransport();
        var server = NewServer("bighall", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            Edit(server, "Owner", id, 60, 45, 60, "iron_wall"); // a far corner block: the build box now spans world 8…70 / 64…111
            BoardOwnStation(server, "Owner", id);

            // The hall (interior world x 14…54, y 66…105, z 14…54) is built straight into the interior world.
            var iron = _content.GetBlock("iron_wall")!.NumericId;
            for (int x = 13; x <= 55; x++)
                for (int y = 65; y <= 106; y++)
                    for (int z = 13; z <= 55; z++)
                    {
                        bool shell = x == 13 || x == 55 || y == 65 || y == 106 || z == 13 || z == 55;
                        if (shell)
                        {
                            server.World.SetBlock(new Vector3i(x, y, z), iron);
                        }
                        else if (x % 16 == 0 && y % 16 == 0 && z % 16 == 0)
                        {
                            server.World.GetBlock(new Vector3i(x, y, z)); // a boarder inside streams these chunks in
                        }
                    }

            var inHall = new Vector3i(30, 66, 30);
            Assert.True(server.StationCellSealedForTest(id, inHall), "the closed hall holds air");
            int size = server.StationPocketSizeForTest(id, inHall);
            Assert.True(size > 64000, $"one sealed pocket of the whole hall ({size} cells)");

            var at = new Vector3f(30.5f, 66.03f, 30.5f);
            float before = pilot.State.Oxygen = 50f;
            TickAt(server, pilot, at);
            Assert.True(pilot.State.Oxygen > before, $"oxygen should refill in the big hall (was {pilot.State.Oxygen})");

            server.World.SetBlock(new Vector3i(30, 106, 30), BlockId.Air);
            TickAt(server, pilot, at, halfSeconds: 8);
            Assert.False(server.StationCellSealedForTest(id, inHall), "a hole to the void still leaks");
            var warning = Assert.Single(HullWarnings(transport));
            Assert.Contains("at 30/106/30, 40 blocks above you", warning, StringComparison.Ordinal);
        }
    }

    /// <summary>Keeps the server's warnings, so a test can count how often the air brake was logged.</summary>
    private sealed class RecordingLog : IGameLogger
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Warnings = new();

        public void Info(string message) { }
        public void Warn(string message) => Warnings.Enqueue(message);
        public void Error(string message) { }
    }

    [Fact]
    public void AStationRoomPastTheBrake_KeepsItsLastAir_AndTheBrakeIsLoggedOnce()
    {
        // #2649 review: a station room the brake stopped read as "no air", nothing was logged, and the stall came back with
        // every backstop. Now the room keeps its last known air, the brake is logged once, and a backstop refill does not
        // fill it again.
        var log = new RecordingLog();
        var server = NewServer("stationbrake", out var repo, logger: log);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            BoardOwnStation(server, "Owner", id);
            var inside = BoxWorld(1, -1, 0);
            Assert.True(server.StationCellSealedForTest(id, inside));

            server.SetAirBrakeForTest(maxCells: 10, maxSpans: 1_000); // the 26-cell room is past this brake now
            server.World.SetBlock(BoxWorld(-1, 1, -1), _content.GetBlock("iron_wall")!.NumericId); // a block set: a refill
            Assert.True(server.StationCellSealedForTest(id, inside), "the braked room keeps its last known air");
            Assert.True(server.StationAirBrakedForTest(id));

            server.AdvanceUptimeForTest(10); // the backstop: another refill
            Assert.True(server.StationCellSealedForTest(id, inside));
            Assert.Single(log.Warnings, w => w.StartsWith("Station air", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AStationBreachBehindADoor_EmptiesOnlyThatRoom_AndOnlyItsPostLosesItsKeeper()
    {
        // #2653: doors are bulkheads on stations too. Room 1 (the core's box) keeps a vendor, room 2 behind the slide door
        // keeps the mission board; a hole in room 2 sends only its quartermaster away.
        var server = NewServer("bulkheadstation", out var repo, new RecordingTransport());
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot, vendorItem: "station_vendor");
            for (int x = 3; x <= 6; x++)
                for (int y = -2; y <= 2; y++)
                    for (int z = -2; z <= 2; z++)
                    {
                        if (x == 6 || Math.Abs(y) == 2 || Math.Abs(z) == 2)
                        {
                            Edit(server, "Owner", id, x, y, z, "iron_wall"); // room 2: x 3…5 beyond the box's door
                        }
                    }

            Edit(server, "Owner", id, 4, -1, 0, "mission_board");
            BoardOwnStation(server, "Owner", id);
            var room1 = BoxWorld(-1, -1, 0);
            var room2 = BoxWorld(4, 0, 1);
            Assert.True(server.StationCellSealedForTest(id, room1));
            Assert.True(server.StationCellSealedForTest(id, room2));
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
            Assert.Contains(server.NpcSnapshots, n => n.Role == "quartermaster");

            server.World.SetBlock(BoxWorld(4, 2, 0), BlockId.Air); // a hole in room 2's roof
            var at = new Vector3f(room1.X + 0.5f, room1.Y + 0.03f, room1.Z + 0.5f);
            TickAt(server, pilot, at, halfSeconds: 12);
            Assert.False(server.StationCellSealedForTest(id, room2), "room 2 leaks");
            Assert.True(server.StationCellSealedForTest(id, room1), "room 1 behind the door keeps its air");
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
            Assert.DoesNotContain(server.NpcSnapshots, n => n.Role == "quartermaster");
        }
    }

    // ---------------- #1773: the build west of the origin ----------------

    /// <summary>Builds a second sealed 5×5×5 iron shell ON FOOT (the write-back path) west of the origin: x −6…−2,
    /// y 64…68, z 8…12, with a 3-tall doorway at x −6 / z 10 that <paramref name="doorItem"/> fills (left open when
    /// null). Interior x −5…−3, y 65…67, z 9…11 — nothing connects it to the seed hull.</summary>
    private static void BuildWestRoomOnFoot(SvGameServer server, PlayerSession pilot, string? doorItem = "door_energy")
    {
        var p = pilot.State;
        p.Inventory.Add("iron_wall", 200, 999);
        p.Inventory.Add("door_energy", 2, 16);
        p.Position = new Vector3f(-3.5f, 65.03f, 10.5f); // inside the future room, every shell cell within reach
        for (int x = -6; x <= -2; x++)
            for (int y = 64; y <= 68; y++)
                for (int z = 8; z <= 12; z++)
                {
                    bool shell = x == -6 || x == -2 || y == 64 || y == 68 || z == 8 || z == 12;
                    bool doorway = x == -6 && z == 10 && y >= 65 && y <= 67;
                    if (shell && !doorway)
                    {
                        server.PlaceBlock("Owner", x, y, z, "iron_wall");
                    }
                }

        if (doorItem != null)
        {
            server.PlaceBlock("Owner", -6, 65, 10, doorItem);
        }
    }

    [Fact]
    public void Room_WestOfTheOrigin_Breathes_AndItsDoorSeals()
    {
        // Lyxette (v2026.9.5): "immer noch angeblich undichte Räume" — oxygen full at x 2.75, draining at x −0.8
        // inside one closed iron/glass room with energy doors. Every block WRITE canonicalises x into [0, circ), so
        // a wall built at x −5 reached the cell grid at x ≈ 5947: the box never grew west, the pocket fill read
        // x < 0 as the void, and the door there never matched its own column (#1773).
        var transport = new RecordingTransport();
        var server = NewServer("westroom", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            BoardOwnStation(server, "Owner", id);
            BuildWestRoomOnFoot(server, pilot);

            // The grid holds the room where it was built — west of the origin, not a lap east of it.
            var cells = server.StationCellsForTest(id);
            Assert.True(cells.ContainsKey(new Vector3i(-6 - 8 - 2, 64 - 64 - 2, 8 - 8 - 2)), "the west wall must be a cell west of the seed hull");
            Assert.DoesNotContain(cells.Keys, c => c.X > 100);

            var inside = new Vector3f(-3.5f, 65.03f, 10.5f);
            Assert.True(server.StationCellSealedForTest(id, new Vector3i(-4, 65, 10)), "the west room is a sealed pocket");
            float o2 = pilot.State.Oxygen = 50f;
            TickAt(server, pilot, inside);
            Assert.True(pilot.State.Oxygen > o2, $"Oxygen should refill in the sealed west room (was {pilot.State.Oxygen}).");
            Assert.Empty(HullWarnings(transport));
            Assert.False(server.FloatingOutsideStationForTest("Owner"), "the west room lies inside the gravity volume");

            // The fill really sees the west walls: a hole in the roof leaks, and the warning names it west of the origin.
            server.World.SetBlock(new Vector3i(-4, 68, 10), BlockId.Air);
            float holed = pilot.State.Oxygen = 80f;
            TickAt(server, pilot, inside, halfSeconds: 8);
            Assert.True(pilot.State.Oxygen < holed, $"Oxygen should drain once the west roof has a hole (was {pilot.State.Oxygen}).");
            var warning = Assert.Single(HullWarnings(transport));
            Assert.Contains("at -4/68/10, 3 blocks above you", warning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Room_WestOfTheOrigin_WithoutItsDoor_Leaks()
    {
        // The counter-check for the door column: the same room with the doorway left open is a hull leak.
        var server = NewServer("westroom-open", out var repo, new RecordingTransport());
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            BoardOwnStation(server, "Owner", id);
            BuildWestRoomOnFoot(server, pilot, doorItem: null);
            Assert.False(server.StationCellSealedForTest(id, new Vector3i(-4, 65, 10)), "an open doorway west of the origin is a hole");
        }
    }

    [Fact]
    public void PhantomEastCells_MoveBackWest_OnTheNextServerStart()
    {
        // A save written between #1558 and #1773 holds a west wing as cells a whole lap east (x ≈ 5947+). The next
        // server start re-stamps the station and moves them back to where they were built, so the box and the air follow.
        var iron = _content.GetBlock("iron_wall")!.NumericId;
        var home = new Vector3i(-6 - 8 - 2, 65 - 64 - 2, 10 - 8 - 2); // where a wall at world x −6 belongs (cell = world − origin + stamp min)
        Vector3i phantom;                                             // where it used to land: a lap east
        string id;
        {
            var s1 = NewServer("phantom", out var repo1, new RecordingTransport());
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildSealedBox(s1, pilot);
                BoardOwnStation(s1, "Owner", id);
                int circ = s1.World.Circumference; // the void world's lap the legacy cells sit on
                Assert.True(circ > 0);
                phantom = new Vector3i(home.X + circ, home.Y, home.Z);

                // Inject the legacy shape into the live grid, then let a real on-foot edit persist the whole build.
                ((Dictionary<Vector3i, BlockId>)s1.StationCellsForTest(id))[phantom] = iron;
                pilot.State.Inventory.Add("iron_wall", 4, 99);
                pilot.State.Position = new Vector3f(10.5f, 65.03f, 10.5f);
                s1.PlaceBlock("Owner", 11, 67, 11, "iron_wall");
                Assert.True(s1.StationCellsForTest(id).ContainsKey(phantom));
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("phantom", out var repo2, new RecordingTransport());
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            BoardOwnStation(s2, "Owner", id);
            var cells = s2.StationCellsForTest(id);
            Assert.False(cells.ContainsKey(phantom), "the phantom east cell must be gone");
            Assert.Equal(iron, cells[home]);
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
            Assert.True(s2.InStation("Owner"), "#1925: a player saved aboard their own station rejoins on it");
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

    // ---------------- #1493: rows keyed by the launch placeholder are re-keyed on load ----------------

    [Fact]
    public void PreUpgradeStationRow_KeyedByThePlanetTypePlaceholder_IsFoundAgainAfterARestart()
    {
        string id;
        string host;
        string placeholder;
        {
            var s1 = NewServer("rekey", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildLineStation(s1, pilot);
                host = s1.StationHostBodyForTest(id);
                var body = s1.Galaxy.FindBody(host);
                Assert.NotNull(body);
                s1.LeaveSpace("Owner");
                repo1.Flush();

                // A row written before #1480 carried the launch instance's RAW key — for a ship that never landed
                // anywhere else that is the planet-TYPE placeholder, which is no body of the galaxy.
                placeholder = body!.PlanetType!;
                Assert.Null(s1.Galaxy.FindBody(placeholder));
                var row = repo1.ListSpaceStructures().Single(r => r.Id == id);
                row.Location = placeholder;
                repo1.SaveSpaceStructure(row);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("rekey", out var repo2);
        using (repo2)
        {
            Assert.Equal(host, s2.StationHostBodyForTest(id)); // re-keyed on load…
            Assert.Equal(host, repo2.ListSpaceStructures().Single(r => r.Id == id).Location); // …and re-saved once

            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            s2.EnterSpace("Owner");
            Assert.True(s2.SpaceEntitiesFor("Owner").Any(e => e.Id == id),
                $"the pre-upgrade station must float in its orbit again (contacts: {string.Join(", ", s2.SpaceEntitiesFor("Owner").Select(e => e.Id))})");
        }
    }

    // ---------------- #1493: the spawn is judged after the top-up and never carves the build ----------------

    [Fact]
    public void RestoredBuildAtTheSpawnColumn_MovesTheSpawn_InsteadOfCarvingTheBuild()
    {
        var core = BoxWorld(0, 0, 0); // the core sits two above the floor — exactly the old first-boarding pad's head cell
        string id;
        ushort coreBlock;
        {
            var s1 = NewServer("padspawn", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildSealedBox(s1, pilot);
                coreBlock = s1.StationCellsForTest(id)[new Vector3i(0, 0, 0)].Value;
                BoardOwnStation(s1, "Owner", id);
                Assert.Equal(coreBlock, s1.World.GetBlock(core).Value); // #2606: the first boarding cuts no pad any more

                // A save from before #2606: its first boarding cut the pad through the centre column — two air cells,
                // a raw world write that was never written back into the build.
                s1.World.SetBlock(core, BlockId.Air);
                s1.World.SetBlock(BoxWorld(0, -1, 0), BlockId.Air);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("padspawn", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            BoardOwnStation(s2, "Owner", id);

            // The top-up put the core back (its cell is still part of the build) — and the spawn moved off it.
            Assert.Equal(coreBlock, s2.World.GetBlock(core).Value);
            var feet = new Vector3i((int)Math.Floor(pilot.State.Position.X), (int)Math.Floor(pilot.State.Position.Y), (int)Math.Floor(pilot.State.Position.Z));
            Assert.NotEqual((core.X, core.Z), (feet.X, feet.Z));
            Assert.True(s2.World.GetBlock(feet).IsAir && s2.World.GetBlock(new Vector3i(feet.X, feet.Y + 1, feet.Z)).IsAir,
                $"the spawn must be free at {feet}");
            Assert.False(s2.World.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)).IsAir, "…and stand on a floor");

            // Nothing of the build was carved: every cell of the grid is in the world.
            foreach (var kv in s2.StationCellsForTest(id))
            {
                Assert.Equal(kv.Value.Value, s2.World.GetBlock(BoxWorld(kv.Key.X, kv.Key.Y, kv.Key.Z)).Value);
            }
        }
    }

    [Fact]
    public void TractorBeam_DeclaresThePassiveRange_TheServerUses()
    {
        var beam = _content.GetShipModule("tractor_beam");
        Assert.NotNull(beam);
        Assert.Equal(16.0, beam!.Stats["tractor_range"]);
    }

    // ---------------- Player reports 2026-09-12 (Lyxette): the roof spawn, the doorway "leak", the sapling ----------------

    /// <summary>A sealed 25×5×5 hall (cells x −2…22, y/z −2…2) with its floor row plated over AND a 1-high crawl gap
    /// up to x = 19, so the only standable cells INSIDE sit at the far +X end — while the roof's outer face above
    /// the box centre is standable too (support below, vacuum above) and NEARER. Before #1833 the spawn search
    /// took the roof.</summary>
    private static string BuildLongHallWithABlockedCentre(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int x = -2; x <= 22; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue; // the core
                    }

                    bool shell = x == -2 || x == 22 || Math.Abs(y) == 2 || Math.Abs(z) == 2;
                    bool doorway = x == 22 && y == -1 && z == 0;
                    bool packed = (y == -1 || y == 1) && x <= 19; // floor and ceiling rows built over → 1-high gap only
                    if ((shell && !doorway) || packed)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, playerId, id, 22, -1, 0, "door_slide");
        Assert.True(server.StationIsBoardableForTest(id));
        return id;
    }

    [Fact]
    public void Docking_NeverSpawnsOnTheRoof_WhenTheHullHoldsAir()
    {
        string id;
        {
            var s1 = NewServer("roofspawn", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildLongHallWithABlockedCentre(s1, pilot);
                BoardOwnStation(s1, "Owner", id);

                // A save from before #2606: its first boarding cut the pad through the packed centre column (box centre
                // x = 10) — a raw world write the next start's top-up restores.
                s1.World.SetBlock(BoxWorld(10, -1, 0), BlockId.Air);
                s1.World.SetBlock(BoxWorld(10, 0, 0), BlockId.Air);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("roofspawn", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            BoardOwnStation(s2, "Owner", id); // the top-up restores the packed centre; the core's own column is packed too

            var feet = new Vector3i((int)Math.Floor(pilot.State.Position.X), (int)Math.Floor(pilot.State.Position.Y), (int)Math.Floor(pilot.State.Position.Z));
            Assert.Equal(BoxWorld(0, -1, 0).Y, feet.Y); // on the deck (world y 65) — not on the roof (y 69) right above the core
            Assert.True(feet.X >= BoxWorld(20, 0, 0).X, $"the spawn moved to the free end of the hall, not up: {feet}");
            Assert.True(s2.StationPocketSizeForTest(id, feet) > 0, "…and it holds air");
        }
    }

    [Fact]
    public void StandingInTheDoorway_OrInThePond_KeepsTheRoomSealed()
    {
        var s = NewServer("doorair", out var repo);
        using (repo)
        {
            var pilot = s.AddLocalPlayer("Owner");
            string id = BuildSealedBox(s, pilot);
            BoardOwnStation(s, "Owner", id);

            var inside = BoxWorld(1, -1, 0);
            var door = BoxWorld(2, -1, 0); // the slide door in the +X wall
            Assert.True(s.StationPocketSizeForTest(id, inside) > 0, "the box holds air");
            Assert.True(s.StationPocketSizeForTest(id, door) > 0, "the doorway is part of that pocket, not a hole in the hull (#1836)");

            var water = _content.GetBlock("water")!.NumericId;
            s.World.SetBlock(inside, water);
            Assert.True(s.StationPocketSizeForTest(id, inside) > 0, "a pond cell is breathed from its head cell, not a wall (#1836)");
        }
    }

    /// <summary>A sealed 5×5×11 shaft of a hall (cells −2…2 in x/z, y −2…8): room for a trunk of five and its crown.</summary>
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

    [Fact]
    public void ASapling_GrowsIntoATree_InsideASealedStationHall()
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
            Assert.Equal(_content.GetBlock("flora_sapling")!.NumericId.Value, s.World.GetBlock(cell).Value);

            s.Tick(200.0); // > SaplingGrowSeconds — before #1835 the crown probe failed forever on a void world
            Assert.Equal(_content.GetBlock("wood_log")!.NumericId.Value, s.World.GetBlock(cell).Value);
        }
    }

    // ---------------- #2606 (Screelit, 2026-10-14): arrive beside the core; no phantom ship decor ----------------

    /// <summary>A non-convex station like Screelit's "Blumenhall": the sealed core room (<see cref="BuildSealedBox"/>,
    /// cells −2…2), a second sealed room at cells x/z 18…22, and an open gangway between them that runs along x = 0,
    /// then along z = 20 — an L, so the bounding-box centre's bottom layer (cells 10, −2, 10 → world 20, 64, 20) is the
    /// empty space between the wings.</summary>
    private static string BuildNonConvexStation(SvGameServer server, PlayerSession pilot)
    {
        string id = BuildSealedBox(server, pilot);
        string playerId = pilot.State.PlayerId;
        for (int x = 18; x <= 22; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = 18; z <= 22; z++)
                {
                    if (x == 18 || x == 22 || Math.Abs(y) == 2 || z == 18 || z == 22)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        for (int z = 3; z <= 20; z++)
        {
            Edit(server, playerId, id, 0, -2, z, "iron_wall"); // the gangway out of the core room …
        }

        for (int x = 1; x <= 17; x++)
        {
            Edit(server, playerId, id, x, -2, 20, "iron_wall"); // … round the corner to the far room
        }

        return id;
    }

    /// <summary>The boarder stands on the deck of a <see cref="BuildSealedBox"/> core room, right beside the core, in
    /// its sealed air.</summary>
    private static void AssertArrivedBesideTheCore(SvGameServer server, string stationId, PlayerSession pilot)
    {
        var feet = Feet(pilot.State.Position);
        var core = BoxWorld(0, 0, 0);
        Assert.Equal(BoxWorld(0, -1, 0).Y, feet.Y); // the core room's deck
        Assert.True(Math.Abs(feet.X - core.X) + Math.Abs(feet.Z - core.Z) == 1, $"expected the cell beside the core {core}, got {feet}");
        Assert.True(server.StationPocketSizeForTest(stationId, feet) > 0, $"the arrival {feet} must hold air");
    }

    /// <summary>The old arrival spot stays empty space: no 3×3 pad at the box centre's bottom layer, in the world or
    /// in the build.</summary>
    private static void AssertNoPadAtTheBoxCentre(SvGameServer server, string stationId)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var cell = new Vector3i(10 + dx, -2, 10 + dz);
                Assert.True(server.World.GetBlock(BoxWorld(cell.X, cell.Y, cell.Z)).IsAir, $"no pad may be cut at cell {cell}");
                Assert.False(server.StationCellsForTest(stationId).ContainsKey(cell));
            }
    }

    [Fact]
    public void NonConvexStation_ArrivesBesideTheCore_WithoutAPad_OnEveryBoarding_AndAfterARestart()
    {
        // Screelit (v2026.10.14): "wurde ich nicht beim Stationskern gespawnt, sondern irgendwo außerhalb der Station".
        // The arrival was the bounding-box centre's bottom layer, with a 3×3 iron pad cut there on the first boarding —
        // on a station of two wings and a gangway that is the vacuum between them (#2606).
        string id;
        {
            var s1 = NewServer("nonconvex", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildNonConvexStation(s1, pilot);
                BoardOwnStation(s1, "Owner", id);
                AssertArrivedBesideTheCore(s1, id, pilot);
                AssertNoPadAtTheBoxCentre(s1, id);

                // The interior is refreshed and the arrival judged on every boarding (#2607): a second boarding (the station
                // world was unloaded behind the leaver, so the search runs on freshly loaded chunks) lands on the same spot.
                var first = Feet(pilot.State.Position);
                s1.LeaveStation("Owner");
                Assert.False(s1.InStation("Owner"));
                BoardOwnStation(s1, "Owner", id);
                Assert.Equal(first, Feet(pilot.State.Position));
                AssertNoPadAtTheBoxCentre(s1, id);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("nonconvex", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            BoardOwnStation(s2, "Owner", id);
            AssertArrivedBesideTheCore(s2, id, pilot);
            AssertNoPadAtTheBoxCentre(s2, id);
        }
    }

    [Fact]
    public void ASaveWhoseOldPadWasFoldedIntoTheBuild_StillArrivesBesideTheCore()
    {
        // Screelit's own station: its first boarding cut the iron pad at the box centre (a raw world write), and the next
        // server start folded those blocks into the build (#1559). The old rule then found the pad standable and kept
        // him arriving on it — outside the hull — on every boarding after.
        var iron = _content.GetBlock("iron_wall")!.NumericId;
        string id;
        {
            var s1 = NewServer("oldpad", out var repo1);
            using (repo1)
            {
                var pilot = s1.AddLocalPlayer("Owner");
                id = BuildNonConvexStation(s1, pilot);
                BoardOwnStation(s1, "Owner", id);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        s1.World.SetBlock(BoxWorld(10 + dx, -2, 10 + dz), iron); // the pre-#2606 first-boarding pad
                    }

                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = NewServer("oldpad", out var repo2);
        using (repo2)
        {
            var pilot = s2.AddLocalPlayer("Owner");
            pilot.State.AboardShip = true;
            BoardOwnStation(s2, "Owner", id);
            Assert.Equal(iron, s2.StationCellsForTest(id)[new Vector3i(10, -2, 10)]); // the pad is part of the build now …
            AssertArrivedBesideTheCore(s2, id, pilot);                               // … and the boarder still arrives at the core
        }
    }

    /// <summary>The sealed core room (<see cref="BuildSealedBox"/>) and a second, closed room beside it at cells x 8…12,
    /// with five cells of vacuum between the two.</summary>
    private static string BuildTwoRooms(SvGameServer server, PlayerSession pilot)
    {
        string id = BuildSealedBox(server, pilot);
        for (int x = 8; x <= 12; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 2; z++)
                {
                    if (x == 8 || x == 12 || Math.Abs(y) == 2 || Math.Abs(z) == 2)
                    {
                        Edit(server, pilot.State.PlayerId, id, x, y, z, "iron_wall");
                    }
                }

        return id;
    }

    [Fact]
    public void ACoreRoomWithAHole_SendsTheBoarderToTheNearestSealedRoom_ElseOntoItsOwnDeck()
    {
        var server = NewServer("holedcore", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildTwoRooms(server, pilot);
            BoardOwnStation(server, "Owner", id);
            AssertArrivedBesideTheCore(server, id, pilot); // both rooms hold air: the core's own room wins

            // A hole in the core room's roof, right above the core: the next boarding takes the other sealed room.
            var roof = BoxWorld(0, 2, 0);
            server.MineBlock("Owner", roof.X, roof.Y, roof.Z);
            Assert.True(server.World.GetBlock(roof).IsAir);
            server.LeaveStation("Owner");
            BoardOwnStation(server, "Owner", id);
            var feet = Feet(pilot.State.Position);
            Assert.True(feet.Y == BoxWorld(0, -1, 0).Y && feet.X >= BoxWorld(9, 0, 0).X && feet.X <= BoxWorld(11, 0, 0).X,
                $"expected the deck of the sealed second room, got {feet}");
            Assert.True(server.StationPocketSizeForTest(id, feet) > 0, "… which holds air");

            // That room leaks as well: no air anywhere → the standable cell nearest the core, the deck beside it — not the
            // roof's outer face and not the top of the core under the hole, both standable too (#1833).
            var roof2 = BoxWorld(10, 2, 0);
            server.MineBlock("Owner", roof2.X, roof2.Y, roof2.Z);
            Assert.True(server.World.GetBlock(roof2).IsAir);
            server.LeaveStation("Owner");
            BoardOwnStation(server, "Owner", id);
            feet = Feet(pilot.State.Position);
            var core = BoxWorld(0, 0, 0);
            Assert.Equal(BoxWorld(0, -1, 0).Y, feet.Y);
            Assert.True(Math.Abs(feet.X - core.X) + Math.Abs(feet.Z - core.Z) == 1, $"expected the deck beside the core {core}, got {feet}");
        }
    }

    [Fact]
    public void BoardingAStation_ClearsTheShipDecor_AndUndockingBringsItBack()
    {
        // Screelit: "wo Terminals – diese Hologramm-Dinger – aus dem Schiff waren". The client draws the ship's room decor
        // from the last ship-stations list it got, and boarding a station sent none — the cockpit's hologram map and the
        // terminals of the ship floated inside the station at the ship's coordinates (#2606).
        var transport = new RecordingTransport();
        var server = NewServer("decor", out var repo, transport, placeStarterShip: true);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            int mark = transport.Sent.Count;
            BoardOwnStation(server, "Owner", id);
            var aboard = transport.Messages().Skip(mark).OfType<ShipStations>().ToList();
            Assert.NotEmpty(aboard);
            Assert.Empty(aboard[^1].Stations); // no ship in a station world: the decor and its prompts go

            mark = transport.Sent.Count;
            server.LeaveStation("Owner");
            var undocked = transport.Messages().Skip(mark).OfType<ShipStations>().ToList();
            Assert.NotEmpty(undocked);
            Assert.Contains(undocked[^1].Stations, s => s.Type == "cockpit"); // the parked ship's own list is back
        }
    }

    [Fact]
    public void WithoutAParkedShip_TheShipDecorList_IsSentEmpty_InsteadOfNotAtAll()
    {
        // The other half of #2606: where no ship is parked the server used to send nothing at all, so whatever list the
        // client held last (the previous world's ship) stayed — an empty list is the "no ship here" answer.
        var transport = new RecordingTransport();
        var server = NewServer("noshipdecor", out var repo, transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildSealedBox(server, pilot);
            BoardOwnStation(server, "Owner", id);
            int mark = transport.Sent.Count;
            server.LeaveStation("Owner"); // back to a body without a parked ship (this server places none)
            var sent = transport.Messages().Skip(mark).OfType<ShipStations>().ToList();
            Assert.NotEmpty(sent);
            Assert.All(sent, m => Assert.Empty(m.Stations));
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
