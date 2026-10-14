// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// #2561: doors keep water out. A door is an entity standing in an air cell, and the fluid automaton only asked "is it
/// air?" — so water flowed straight through a shut door and flooded a tester's base, and nothing could plug the gap
/// (no block may be built into a doorway). Now every doorway — open or shut — is a wall to the flow, to the settle of
/// still water, and to the drain stream; and water an older save left in a doorway dries up when the step wakes it.
/// </summary>
public sealed class WatertightDoorTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly ushort _water;
    private readonly BlockId _stone;

    public WatertightDoorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_doorwater_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        _water = _content.GetBlock("water")!.NumericId.Value;
        _stone = _content.GetBlock("stone")!.NumericId;
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
            // a test host still holding the save is no reason to fail the run
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "doorwater")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        // No generated structures: the door under test is the only one, and the boot stays quick.
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceFactories = false,
            PlaceChests = false,
            PlaceBanditCamps = false,
            PlaceMonuments = false,
            PlaceDataCubes = false,
            PlaceIntercityRail = false,
            PlaceRailRuins = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static void Run(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.3)
        {
            server.Tick(0.3);
        }
    }

    private bool IsWater(SvGameServer server, int x, int y, int z) => server.World.GetBlock(new Vector3i(x, y, z)).Value == _water;

    private int CountWater(SvGameServer server, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        int n = 0;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (IsWater(server, x, y, z))
                    {
                        n++;
                    }

        return n;
    }

    // ---------------- a corridor with a door ----------------

    /// <summary>The doorway's floor cell; the corridor runs along x from Xd − 4 to Xd + 10.</summary>
    private const int Xd = 300, Y = 130, Zc = 340;

    /// <summary>A closed one-wide, three-high stone corridor along x with the doorway at x = Xd. Every cell around it is
    /// set, so the generated terrain does not matter.</summary>
    private void Corridor(SvGameServer server, SqliteWorldRepository repo)
    {
        repo.RunInTransaction(() =>
        {
            for (int x = Xd - 5; x <= Xd + 11; x++)
                for (int z = Zc - 1; z <= Zc + 1; z++)
                    for (int y = Y - 1; y <= Y + 3; y++)
                    {
                        bool open = z == Zc && y >= Y && y <= Y + 2 && x > Xd - 5 && x < Xd + 11;
                        server.World.SetBlock(new Vector3i(x, y, z), open ? BlockId.Air : _stone);
                    }
        });
    }

    /// <summary>A player hangs a wooden door in a doorway (the placement path a right-click takes).</summary>
    private static BlocksBeyondTheStars.GameServer.PlayerSession HangDoor(SvGameServer server, int x, int y, int z, Vector3f standAt)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = standAt;
        p.State.Inventory.Add("door_wood", 1, 16);
        server.PlaceBlock("Builder", x, y, z, "door_wood");
        Assert.True(server.IsFluidDoorCellForTest(x, y, z), "the door should hang in the doorway");
        Assert.True(server.IsFluidDoorCellForTest(x, y + 2, z), "a door fills its whole three-high opening");
        return p;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaterBesideADoor_NeverEntersTheDoorway_AndTheRoomBehindStaysDry(bool open)
    {
        var server = Started(out var repo);
        using (repo)
        {
            Corridor(server, repo);
            var builder = HangDoor(server, Xd, Y, Zc, new Vector3f(Xd + 2.5f, Y, Zc + 0.5f));
            if (open)
            {
                // An open door is still a doorway, not a drain: one rule, whatever the door's state.
                int id = server.DoorSnapshots.Single(d => (int)Math.Floor(d.Pos.X) == Xd && (int)Math.Floor(d.Pos.Z) == Zc).Id;
                server.InteractDoorForTest(builder, id);
                Assert.True(server.DoorSnapshots.Single(d => d.Id == id).Open, "the door should be open");
            }

            server.PlaceFluidSource("water", Xd - 2, Y, Zc); // a placed block: endless, it never runs dry
            Run(server, 3);

            Assert.True(IsWater(server, Xd - 1, Y, Zc), "the water runs up to the door");
            for (int y = Y; y <= Y + 2; y++)
            {
                Assert.False(IsWater(server, Xd, y, Zc), $"no water in the doorway (y={y})");
            }

            Assert.Equal(0, CountWater(server, Xd, Xd + 10, Y, Y + 2, Zc, Zc)); // the room behind the door stays dry
        }
    }

    [Fact]
    public void AFlowFedOnlyThroughADoorway_RecedesAfterTheUpdate()
    {
        // What the old rules left in a save: a placed source outside, a tongue running through the shut door into the room.
        string location;
        var server = Started(out var repo, "oldsave");
        using (repo)
        {
            Corridor(server, repo);
            HangDoor(server, Xd, Y, Zc, new Vector3f(Xd + 2.5f, Y, Zc + 0.5f));
            server.PlaceFluidSource("water", Xd - 2, Y, Zc);
            location = server.World.LocationId;
            repo.RunInTransaction(() =>
            {
                void Flow(int x, byte level)
                {
                    var cell = new Vector3i(x, Y, Zc);
                    server.World.SetBlock(cell, new BlockId(_water));
                    repo.SaveFluidCell(location, cell, level, falling: false);
                }

                Flow(Xd - 3, 7);
                Flow(Xd - 1, 7);
                Flow(Xd, 6); // in the doorway
                for (int i = 1; i <= 5; i++)
                {
                    Flow(Xd + i, (byte)(6 - i));
                }

                // Still water a settle could have moved into the opening above it (untracked).
                server.World.SetBlock(new Vector3i(Xd, Y + 1, Zc), new BlockId(_water));
            });
        }

        var reloaded = Started(out var repo2, "oldsave");
        using (repo2)
        {
            Run(reloaded, 4);

            for (int y = Y; y <= Y + 2; y++)
            {
                Assert.False(IsWater(reloaded, Xd, y, Zc), $"the doorway dries up (y={y})");
            }

            Assert.Equal(0, CountWater(reloaded, Xd, Xd + 10, Y, Y + 2, Zc, Zc)); // the tongue in the room lost its feed
            Assert.DoesNotContain(repo2.ListFluidCells(location),
                row => row.WorldPosition.X >= Xd && row.WorldPosition.X <= Xd + 10 && row.WorldPosition.Y == Y && row.WorldPosition.Z == Zc);
            Assert.True(IsWater(reloaded, Xd - 2, Y, Zc), "the placed source outside stays");
            Assert.True(IsWater(reloaded, Xd - 1, Y, Zc), "and keeps feeding the water in front of the door");
        }
    }

    // ---------------- still water ----------------

    private const int Bx = 300, By = 130, Bz = 300, Size = 7, Depth = 3;

    /// <summary>A closed 7x7x3 stone basin in the air (two-cell floor, walls one cell thick) filled the way the generator
    /// fills water — untracked cells above the sea level, so finite.</summary>
    private void Basin(SvGameServer server, SqliteWorldRepository repo)
    {
        repo.RunInTransaction(() =>
        {
            for (int x = Bx - 1; x <= Bx + Size; x++)
                for (int z = Bz - 1; z <= Bz + Size; z++)
                {
                    server.World.SetBlock(new Vector3i(x, By - 1, z), _stone);
                    server.World.SetBlock(new Vector3i(x, By - 2, z), _stone);
                    bool wall = x == Bx - 1 || x == Bx + Size || z == Bz - 1 || z == Bz + Size;
                    for (int y = By; y < By + Depth; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), wall ? _stone : new BlockId(_water));
                    }
                }
        });
    }

    [Fact]
    public void AStillPond_DoesNotSettleThroughADoor_ButDoesOnceTheDoorIsTakenDown()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Basin(server, repo);
            int wall = Bx + Size, zc = Bz + 3;
            // A closed room east of the basin, its floor two cells below the pond's bottom, and a three-high doorway through
            // the east wall into it — set straight into the world, so nothing wakes the pond yet.
            repo.RunInTransaction(() =>
            {
                for (int x = wall + 1; x <= wall + 5; x++)
                    for (int z = zc - 2; z <= zc + 2; z++)
                        for (int y = By - 3; y <= By + 3; y++)
                        {
                            bool room = x <= wall + 4 && z > zc - 2 && z < zc + 2 && y >= By - 2 && y <= By + 2;
                            server.World.SetBlock(new Vector3i(x, y, z), room ? BlockId.Air : _stone);
                        }

                for (int y = By; y < By + Depth; y++)
                {
                    server.World.SetBlock(new Vector3i(wall, y, zc), BlockId.Air);
                }
            });
            HangDoor(server, wall, By, zc, new Vector3f(wall + 2.5f, By, zc + 0.5f));

            int full = Size * Size * Depth;
            Assert.True(server.TrySettleForTest(wall - 1, By + Depth - 1, zc)); // the pond is touched beside the door
            Assert.Equal(0, server.SettleJobCountForTest); // nowhere to go: the doorway is a wall
            Run(server, 2);
            Assert.Equal(full, CountWater(server, Bx, wall - 1, By, By + Depth - 1, Bz, Bz + Size - 1));
            Assert.Equal(0, CountWater(server, wall, wall + 4, By - 2, By + 2, zc - 1, zc + 1));

            // Take the door down: the pond settles into the room — the door was all that held it.
            server.MineBlockOnce("Builder", wall, By, zc);
            Assert.False(server.IsFluidDoorCellForTest(wall, By, zc), "the door is gone");
            Run(server, 8);
            int room = CountWater(server, wall, wall + 4, By - 2, By + 2, zc - 1, zc + 1);
            Assert.True(room > 0, "with the door gone the pond runs into the room");
            Assert.Equal(full, room + CountWater(server, Bx, wall - 1, By, By + Depth - 1, Bz, Bz + Size - 1)); // none made or lost
        }
    }

    // ---------------- weather snow ----------------

    [Fact]
    public void WeatherSnow_IsNeverLaidOnAFluidSurface()
    {
        var server = Started(out var repo);
        using (repo)
        {
            const int x = 320, z = 320, ground = 129;
            void Column(BlockId top)
            {
                repo.RunInTransaction(() =>
                {
                    server.World.SetBlock(new Vector3i(x, ground - 1, z), _stone);
                    server.World.SetBlock(new Vector3i(x, ground, z), top);
                    for (int y = ground + 1; y <= ground + 60; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                    }
                });
            }

            Column(_stone);
            Assert.True(server.SnowSurfaceForTest(x, ground + 1, z, out var cell), "snow settles on open stone");
            Assert.Equal(new Vector3i(x, ground + 1, z), cell);

            Column(new BlockId(_water));
            Assert.False(server.SnowSurfaceForTest(x, ground + 1, z, out _), "no drift floats on a pond");

            Column(_content.GetBlock("lava")!.NumericId);
            Assert.False(server.SnowSurfaceForTest(x, ground + 1, z, out _), "nor on lava");
        }
    }
}
