using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Task 5 Stage 3c — placeable doors. A player builds a hinge/slide door; it reuses the existing door entity
/// system (no solid block — it fills the air cell), persists across reloads, toggles on interact, and returns
/// the item when mined.
/// </summary>
public sealed class PlaceableDoorTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlaceableDoorTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_door_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "door"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "door", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the target cells are empty
        p.State.Inventory.Add("door_hinge", 1, 99);
        p.State.Inventory.Add("door_slide", 1, 99);
        return p;
    }

    [Fact]
    public void Place_RegistersAndPersists_Toggles_AndMineReturnsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server);

            server.PlaceBlock("Builder", 1, 200, 0, "door_hinge");
            server.PlaceBlock("Builder", 3, 200, 0, "door_slide");

            Assert.Equal(2, server.DoorCount);                                       // both registered as entities
            Assert.True(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir);       // a door fills air, not a solid block
            Assert.Equal(2, repo.ListDoors(server.ActiveLocationId).Count);          // both persisted
            Assert.Equal(0, p.State.Inventory.CountOf("door_hinge"));                // the block was consumed

            var hinge = server.DoorSnapshots.Single(d => d.Kind == "hinge");
            Assert.False(hinge.Open);
            server.InteractDoorForTest(p, hinge.Id);                                 // E at the door
            Assert.True(server.DoorSnapshots.Single(d => d.Kind == "hinge").Open);   // toggled open

            server.MineBlockOnce("Builder", 1, 200, 0);                              // mine the hinge door away
            Assert.Equal(1, server.DoorCount);
            Assert.DoesNotContain(server.DoorSnapshots, d => d.Kind == "hinge");
            Assert.Equal(1, p.State.Inventory.CountOf("door_hinge"));                // returned to the miner
            Assert.Single(repo.ListDoors(server.ActiveLocationId));                  // and forgotten from the save
        }
    }

    [Fact]
    public void PlayerDoors_SurviveAReload()
    {
        string loc;
        var server = NewServer(out var repo1);
        using (repo1)
        {
            Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "door_slide");
            loc = server.ActiveLocationId;
            Assert.Equal(1, server.DoorCount);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // A fresh server on the same save reloads the persisted player door (no markers placed it).
        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            Assert.Equal(loc, server2.ActiveLocationId);
            Assert.Contains(server2.DoorSnapshots, d => d.Kind == "slide");
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
