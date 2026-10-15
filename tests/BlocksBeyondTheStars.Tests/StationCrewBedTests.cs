// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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

/// <summary>
/// The crew of a player-built station finds a bed anywhere on the station (#2657): every bed of the station in a room
/// with air is a candidate, not only those within eight blocks of the post; the post keepers choose before the
/// civilians; a bed in the post's own room comes first (the air engine names the rooms — a stand-in here); and a bed
/// built while the crew is aboard is taken.
/// </summary>
public sealed class StationCrewBedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_crewbeds_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The internal wall between the two rooms (build z).</summary>
    private const int WallZ = 3;

    /// <summary>The hull's far end (build z).</summary>
    private const int EndZ = 26;

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static void Edit(SvGameServer server, string playerId, string id, int x, int y, int z, string item)
        => server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

    /// <summary>
    /// A long sealed hull around the core: cells x −2…2, y −2…1, z −2…<see cref="EndZ"/> (a 3 × 2 corridor inside), split
    /// by a wall at z = <see cref="WallZ"/> into room B (z −1…2, the core's) and room A (z 4…25, with the airlock in its
    /// +X wall at z 10). The trading post stands on room A's floor at <paramref name="postZ"/>.
    /// </summary>
    private static string BuildTwoRoomHull(SvGameServer server, PlayerSession pilot, int postZ)
    {
        string playerId = pilot.State.PlayerId;
        server.EnterSpace(playerId);
        pilot.State.InEva = true;
        pilot.State.InstantBuild = true;
        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int x = -2; x <= 2; x++)
            for (int y = -2; y <= 1; y++)
                for (int z = -2; z <= EndZ; z++)
                {
                    bool shell = Math.Abs(x) == 2 || y == -2 || y == 1 || z == -2 || z == EndZ;
                    bool wall = z == WallZ && Math.Abs(x) < 2 && y > -2 && y < 1;
                    bool doorway = x == 2 && y == -1 && z == 10;
                    if ((shell || wall) && !doorway)
                    {
                        Edit(server, playerId, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, playerId, id, 2, -1, 10, "door_slide");
        Edit(server, playerId, id, 1, -1, postZ, "station_vendor");
        Assert.True(server.StationIsBoardableForTest(id));
        return id;
    }

    /// <summary>Build cell → interior-world cell (origin (8,64,8) minus the −2 build minimum).</summary>
    private static Vector3i World(int x, int y, int z) => new(x + 10, y + 66, z + 10);

    /// <summary>A two-cell bed on a room's floor, its head at the build cell and the foot one toward +Z.</summary>
    private Vector3i PutBed(SvGameServer server, int x, int z)
    {
        var head = World(x, -1, z);
        var bed = _content.GetBlock("bed")!.NumericId;
        int desc = ShapeCode.Pack((int)BlockShape.BedHead, 0);
        server.World.SetBlock(head, bed, shape: desc, owner: "Owner");
        server.World.SetBlock(new Vector3i(head.X, head.Y, head.Z + 1), bed, shape: FurnitureShapes.BedPartnerDescriptor(desc), owner: "Owner");
        return head;
    }

    private static void Board(SvGameServer server, string playerId, string stationId)
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

    private static void Tick(SvGameServer server, PlayerSession pilot, Vector3f at, double seconds)
    {
        for (int i = 0; i < (int)(seconds / 0.5); i++)
        {
            server.SetLocalDayFractionForTest(0.45, 0f);
            pilot.State.Position = at;
            pilot.State.Oxygen = 100f;
            pilot.State.Health = 100f;
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void ABedTwentyBlocksAway_InAnotherSealedRoom_IsFound_AndABedBuiltLater_IsTaken()
    {
        var server = NewServer("crewbeds_far", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildTwoRoomHull(server, pilot, postZ: 24);
            Board(server, "Owner", id);
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");

            // The only bed: in room B, 24 blocks from the post at the far end of room A, behind a wall.
            var far = PutBed(server, -1, 0);
            var inA = new Vector3f(World(0, -1, 20).X + 0.5f, World(0, -1, 20).Y + 0.03f, World(0, -1, 20).Z + 0.5f);
            Tick(server, pilot, inA, 3);
            Assert.True(server.StationCellSealedForTest(id, new Vector3i(far.X, far.Y + 1, far.Z)), "room B holds air");

            var crew = server.NpcFurnitureForTest;
            Assert.Equal(far, crew.Single(n => n.Role == "vendor").Bed); // the post keeper chooses first
            Assert.Single(crew, n => n.Bed is not null);

            // A bed built while the crew is aboard: a civilian takes it.
            var later = PutBed(server, 0, 14);
            Tick(server, pilot, inA, 2);
            Assert.Contains(server.NpcFurnitureForTest, n => n.Role != "vendor" && n.Bed == later);
            Assert.Equal(far, server.NpcFurnitureForTest.Single(n => n.Role == "vendor").Bed); // and nobody moved
        }
    }

    [Fact]
    public void TheCrew_PrefersABedInItsPostsRoom_OverANearerOneNextDoor()
    {
        var server = NewServer("crewbeds_room", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Owner");
            string id = BuildTwoRoomHull(server, pilot, postZ: 5);
            Board(server, "Owner", id);

            // The air engine's room ids (a stand-in): room B in front of the inner wall, room A behind it.
            int wallWorldZ = World(0, 0, WallZ).Z;
            server.SetAirRoomIdForTest(c => c.Z < wallWorldZ ? 1 : c.Z > wallWorldZ ? 2 : null);

            var nextDoor = PutBed(server, -1, 0);  // room B: about five blocks from the post, through the wall
            var sameRoom = PutBed(server, -1, 22); // room A: seventeen blocks down the corridor
            var inA = new Vector3f(World(0, -1, 12).X + 0.5f, World(0, -1, 12).Y + 0.03f, World(0, -1, 12).Z + 0.5f);
            Tick(server, pilot, inA, 3);

            var crew = server.NpcFurnitureForTest;
            Assert.Equal(sameRoom, crew.Single(n => n.Role == "vendor").Bed);
            Assert.Contains(crew, n => n.Bed == nextDoor); // the other bed still houses somebody
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // a locked temp file must not fail the run
        }
    }
}
