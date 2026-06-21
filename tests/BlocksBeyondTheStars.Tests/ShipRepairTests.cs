using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Own-ship repair (docs/developer/SHIP_REPAIR.md): the numeric hull is bought back with a metal plating item,
/// and EVA-carved hull cells are refilled with their design block. Material-only, partial when short.
/// </summary>
public sealed class ShipRepairTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipRepairTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shiprepair_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shiprepair"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "shiprepair",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void RepairShipAll_RestoresHull_AndConsumesPlates()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Host");
            var (_, max) = server.ShipHullForTest("Host");
            server.SetShipHullForTest("Host", max - 30f); // dent 30 → ceil(30/10) = 3 plates
            player.State.Inventory.Add("iron_plate", 10, 99);

            Assert.True(server.RepairShipForTest("Host", new RepairShipIntent { Mode = "all" }));

            var (hull, hullMax) = server.ShipHullForTest("Host");
            Assert.True(Math.Abs(hullMax - hull) < 0.01f, $"hull {hull} should be back at max {hullMax}");
            Assert.Equal(7, player.State.Inventory.CountOf("iron_plate")); // 10 − 3 spent
        }
    }

    [Fact]
    public void RepairShipAll_IsPartial_WhenShortOnPlates()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Host");
            var (_, max) = server.ShipHullForTest("Host");
            server.SetShipHullForTest("Host", max - 50f); // needs 5 plates
            player.State.Inventory.Add("iron_plate", 2, 99); // only 2 → +20 hull

            Assert.True(server.RepairShipForTest("Host", new RepairShipIntent { Mode = "all" }));

            var (hull, hullMax) = server.ShipHullForTest("Host");
            Assert.True(Math.Abs((hullMax - 30f) - hull) < 0.01f, $"hull {hull} should be max−30 ({hullMax - 30f})");
            Assert.Equal(0, player.State.Inventory.CountOf("iron_plate"));
        }
    }

    [Fact]
    public void RepairShipCell_RefillsCarvedHull_AndConsumesItem()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Host");
            var hole = server.CarveFirstShipCellForTest("Host");
            Assert.Equal(1, server.ShipRepairMissingCellsForTest("Host"));

            player.State.Inventory.Add(hole.Item, 1, 99);
            Assert.True(server.RepairShipForTest("Host",
                new RepairShipIntent { Mode = "cell", X = hole.X, Y = hole.Y, Z = hole.Z }));

            Assert.Equal(0, server.ShipRepairMissingCellsForTest("Host"));
            Assert.Equal(0, player.State.Inventory.CountOf(hole.Item));
        }
    }

    [Fact]
    public void RepairShipAll_RefillsCarvedHull_AndHull_Together()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Host");
            var (_, max) = server.ShipHullForTest("Host");
            server.SetShipHullForTest("Host", max - 10f); // 1 plate of hull
            var hole = server.CarveFirstShipCellForTest("Host");
            player.State.Inventory.Add("iron_plate", 5, 99);
            player.State.Inventory.Add(hole.Item, 5, 99); // covers the cell (may also be iron_plate)

            Assert.True(server.RepairShipForTest("Host", new RepairShipIntent { Mode = "all" }));

            var (hull, hullMax) = server.ShipHullForTest("Host");
            Assert.True(Math.Abs(hullMax - hull) < 0.01f);
            Assert.Equal(0, server.ShipRepairMissingCellsForTest("Host"));
        }
    }

    [Fact]
    public void RepairShipAll_FreeBuild_RepairsWithoutMaterials()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Host");
            player.State.InstantBuild = true;
            var (_, max) = server.ShipHullForTest("Host");
            server.SetShipHullForTest("Host", max - 40f);
            server.CarveFirstShipCellForTest("Host");

            Assert.True(server.RepairShipForTest("Host", new RepairShipIntent { Mode = "all" }));

            var (hull, hullMax) = server.ShipHullForTest("Host");
            Assert.True(Math.Abs(hullMax - hull) < 0.01f);
            Assert.Equal(0, server.ShipRepairMissingCellsForTest("Host"));
        }
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
}
