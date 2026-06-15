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
/// Hover speeders (craftable single-seat surface vehicles). Deploying consumes the item and spawns a server
/// entity; boarding bonds the driver and driving drains its energy cell; collisions + wildlife dent the hull and
/// destroy it at zero (losing the item); packing it up returns the item; and a deployed speeder is persisted in
/// the player blob so it survives a reload.
/// </summary>
public sealed class SpeederTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpeederTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_speeder_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "speeder"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "speeder", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Player(SvGameServer server, string name, Vector3f at)
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = at;
        p.State.Inventory.Add("speeder", 1, 1);
        p.State.SuitEnergy = 100f;
        return p;
    }

    [Fact]
    public void Deploy_SpawnsEntity_ConsumesItem_AndRecordsIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));

            string id = server.DeploySpeederForTest("Pilot");

            Assert.NotEqual(string.Empty, id);
            Assert.Equal(1, server.SpeederCount);
            Assert.Equal(0, p.State.Inventory.CountOf("speeder")); // the item became the vehicle
            Assert.Single(p.State.DeployedSpeeders);
            Assert.Equal(server.ActiveLocationId, p.State.DeployedSpeeders[0].HomeBodyId);
        }
    }

    [Fact]
    public void Board_BondsDriver_AndDriving_DrainsEnergy()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));
            string id = server.DeploySpeederForTest("Pilot");

            // Stand on the speeder, then board it.
            var pos = server.SpeederSnapshots.Single().Pos;
            p.State.Position = pos;
            server.EnterSpeederForTest("Pilot", id);
            Assert.Equal("Pilot", server.SpeederSnapshots.Single().DriverId);
            Assert.Equal(id, p.State.InSpeeder);

            float fuelBefore = server.SpeederSnapshots.Single().Fuel;
            float fuelAfter = server.DriveSpeederStepForTest("Pilot", new Vector3f(pos.X + 40, pos.Y, pos.Z));
            Assert.True(fuelAfter < fuelBefore); // covering ground spent the energy cell
        }
    }

    [Fact]
    public void Exit_ParksTheSpeeder_AndClearsTheDriver()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));
            string id = server.DeploySpeederForTest("Pilot");
            var pos = server.SpeederSnapshots.Single().Pos;
            p.State.Position = pos;
            server.EnterSpeederForTest("Pilot", id);

            p.State.Position = new Vector3f(pos.X + 25, pos.Y, pos.Z + 5); // drive somewhere
            server.DriveSpeederStepForTest("Pilot", p.State.Position);
            server.ExitSpeederForTest("Pilot");

            Assert.Equal(string.Empty, p.State.InSpeeder);
            var s = server.SpeederSnapshots.Single();
            Assert.Equal(string.Empty, s.DriverId);
            Assert.Equal(p.State.Position.X, s.Pos.X, 2); // parked where the player got out
        }
    }

    [Fact]
    public void Stow_RemovesEntity_AndReturnsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));
            string id = server.DeploySpeederForTest("Pilot");
            p.State.Position = server.SpeederSnapshots.Single().Pos; // within reach

            server.StowSpeederForTest("Pilot", id);

            Assert.Equal(0, server.SpeederCount);
            Assert.Empty(p.State.DeployedSpeeders);
            Assert.Equal(1, p.State.Inventory.CountOf("speeder")); // packed back into the item
        }
    }

    [Fact]
    public void Collision_DentsHull_AndAHardEnoughCrash_DestroysIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));
            string id = server.DeploySpeederForTest("Pilot");
            var pos = server.SpeederSnapshots.Single().Pos;
            p.State.Position = pos;
            server.EnterSpeederForTest("Pilot", id);

            float hullBefore = server.SpeederSnapshots.Single().Hull;
            server.ImpactSpeederForTest("Pilot", id, 20f); // a moderate prang
            Assert.True(server.SpeederSnapshots.Single().Hull < hullBefore);

            server.ImpactSpeederForTest("Pilot", id, 999f); // a lethal smash
            Assert.Equal(0, server.SpeederCount);
            Assert.Empty(p.State.DeployedSpeeders); // the item is lost on destruction
            Assert.Equal(string.Empty, p.State.InSpeeder); // and the driver is ejected
        }
    }

    [Fact]
    public void Refuel_TopsUpTheEnergyCell_AndConsumesACell()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));
            string id = server.DeploySpeederForTest("Pilot");
            var pos = server.SpeederSnapshots.Single().Pos;
            p.State.Position = pos;
            server.EnterSpeederForTest("Pilot", id);

            // Burn a lot of energy by driving far in short hops, then refuel.
            for (int i = 1; i <= 20; i++)
            {
                server.DriveSpeederStepForTest("Pilot", new Vector3f(pos.X + i * 30, pos.Y, pos.Z));
            }

            float drained = server.SpeederSnapshots.Single().Fuel;
            p.State.Inventory.Add("energy_cell_1", 1, 99);
            server.RefuelSpeederForTest("Pilot", id);

            Assert.True(server.SpeederSnapshots.Single().Fuel > drained);
            Assert.Equal(0, p.State.Inventory.CountOf("energy_cell_1")); // the cell was spent
        }
    }

    [Fact]
    public void DeployedSpeeder_SurvivesAReload()
    {
        var server = NewServer(out var repo1);
        using (repo1)
        {
            var p = Player(server, "Pilot", new Vector3f(10, 64, 10));
            server.DeploySpeederForTest("Pilot");
            Assert.Equal(1, server.SpeederCount);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");          // reloads the persisted player blob
            server2.ReconcileSpeedersForTest("Pilot"); // materialise their deployed speeders on this world
            Assert.Equal(1, server2.SpeederCount);
            Assert.Equal("Pilot", server2.SpeederSnapshots.Single().OwnerId);
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
