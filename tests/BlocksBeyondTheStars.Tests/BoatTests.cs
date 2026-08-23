// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
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
/// The boat (#1215): the water <c>Kind</c> of the speeder system. Launching needs a water column ahead and sets
/// the boat onto the waterline; it never drains a cell and cannot be refuelled; a driver who keeps reporting
/// the boat ashore is set back onto the last water pose; packing up returns the <c>boat</c> item; the kind
/// survives a reload; and a water-world start hands out a boat.
/// </summary>
public sealed class BoatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    // The stage: the pilot stands at (10.5, 80, 10.5) facing +Z. A stone pad fills the launch scan window
    // (2–5 blocks ahead, ±3 sideways, 6 down / 3 up), and the water tests flood a 5×4 pool in front of it.
    private static readonly Vector3f Stand = new Vector3f(10.5f, 80f, 10.5f);

    public BoatTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_boat_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string startPlanet = "varied")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "boat"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "boat", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, StartPlanet = startPlanet };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Pilot(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Pilot");
        p.State.Position = Stand;
        p.State.Yaw = 0f; // facing +Z
        p.State.Inventory.Add("boat", 1, 1);
        p.State.SuitEnergy = 100f;
        return p;
    }

    private void Fill(SvGameServer server, string block, int x0, int x1, int y0, int y1, int z0, int z1)
    {
        var id = block == "air" ? BlockId.Air : _content.GetBlock(block)!.NumericId;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), id);
                }
    }

    /// <summary>Dry land everywhere the launch scan can look (and under the stand): stone to y=79, air above.</summary>
    private void StonePad(SvGameServer server)
    {
        Fill(server, "stone", 4, 16, 73, 79, 6, 18);
        Fill(server, "air", 4, 16, 80, 84, 6, 18);
    }

    /// <summary>A two-deep pool right in front of the pilot: water at y=78..79 → waterline at y=80.</summary>
    private void Pool(SvGameServer server) => Fill(server, "water", 8, 12, 78, 79, 12, 15);

    [Fact]
    public void Content_BoatIsAWaterVehicle_WithAWorkshopRecipeAndNoBlueprint()
    {
        var boat = _content.GetItem("boat");
        Assert.NotNull(boat);
        Assert.NotNull(boat!.Vehicle);
        Assert.Equal("boat", boat.Vehicle!.Kind);
        Assert.Equal("water", boat.Vehicle.Medium);
        Assert.False(boat.Vehicle.Fuel);
        Assert.Equal("gadget", boat.Tool?.Kind.ToString().ToLowerInvariant()); // deploys through the gadget path

        var speeder = _content.GetItem("speeder")!.Vehicle;
        Assert.NotNull(speeder);
        Assert.Equal("speeder", speeder!.Kind);
        Assert.True(speeder.Fuel);

        var recipe = _content.GetRecipe("boat");
        Assert.NotNull(recipe);
        Assert.True(string.IsNullOrEmpty(recipe!.RequiredBlueprint)); // early-game: no blueprint gate
        Assert.Contains(recipe.Inputs, i => i.Item == "wood_log");
    }

    [Fact]
    public void Launch_OnDryLand_IsRefused_AndKeepsTheItem()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);

            string id = server.DeployVehicleForTest("Pilot", "boat");

            Assert.Equal(string.Empty, id);
            Assert.Equal(0, server.SpeederCount);
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
        }
    }

    [Fact]
    public void Launch_OntoWater_SetsTheBoatOnTheWaterline_WithNoCell()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);

            string id = server.DeployVehicleForTest("Pilot", "boat");

            Assert.NotEqual(string.Empty, id);
            var s = server.SpeederSnapshots.Single();
            Assert.Equal("boat", s.Kind);
            Assert.Equal(80.3f, s.Pos.Y, 2);            // waterline (top of the y=79 water cell) + 0.3
            Assert.InRange(s.Pos.Z, 12f, 16f);           // in the pool ahead, not at the pilot's feet
            Assert.Equal(0f, s.FuelMax);                 // no cell
            Assert.Equal(0, p.State.Inventory.CountOf("boat"));
            Assert.Equal("boat", p.State.DeployedSpeeders.Single().Kind);
        }
    }

    [Fact]
    public void Boat_NeverDrains_AndCannotBeRefuelled()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            var pos = server.SpeederSnapshots.Single().Pos;
            p.State.Position = pos;
            server.EnterSpeederForTest("Pilot", id);
            Assert.Equal(id, p.State.InSpeeder);

            // Cruise up and down the pool: the (non-existent) cell never moves.
            for (int i = 0; i < 10; i++)
            {
                server.DriveSpeederStepForTest("Pilot", new Vector3f(pos.X + (i % 2 == 0 ? 1.5f : -1.5f), pos.Y, pos.Z + (i % 3)));
            }

            var s = server.SpeederSnapshots.Single();
            Assert.Equal(0f, s.Fuel);
            Assert.Equal(0f, s.FuelMax);

            p.State.Inventory.Add("energy_cell_1", 1, 99);
            server.RefuelSpeederForTest("Pilot", id);
            Assert.Equal(1, p.State.Inventory.CountOf("energy_cell_1")); // refused — nothing to fill
            Assert.Equal(0f, server.SpeederSnapshots.Single().Fuel);
        }
    }

    [Fact]
    public void DrivingAshore_ForLongEnough_SnapsBackToTheLastWaterPose()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            var water = server.SpeederSnapshots.Single().Pos;
            p.State.Position = water;
            server.EnterSpeederForTest("Pilot", id);
            server.DriveSpeederStepForTest("Pilot", water); // registers the wet pose

            // Drive onto the stone pad behind the pilot's stand (no water within two cells under the hull).
            var land = new Vector3f(10.5f, 80.3f, 8.5f);
            for (int i = 0; i < 29; i++)
            {
                server.DriveSpeederStepForTest("Pilot", land);
            }

            Assert.Equal(land.Z, p.State.Position.Z, 2); // lenient: 29 dry reports are still the driver's business

            server.DriveSpeederStepForTest("Pilot", land);   // the 30th trips the rule
            Assert.Equal(water.X, p.State.Position.X, 2);
            Assert.Equal(water.Z, p.State.Position.Z, 2);    // back on the water, where the boat last floated
            Assert.Equal(water.Z, server.SpeederSnapshots.Single().Pos.Z, 2);
            Assert.Equal(id, p.State.InSpeeder);             // still aboard — nothing was destroyed or ejected
        }
    }

    [Fact]
    public void Stow_ReturnsTheBoatItem_NotASpeeder()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Pilot(server);
            StonePad(server);
            Pool(server);
            string id = server.DeployVehicleForTest("Pilot", "boat");
            p.State.Position = server.SpeederSnapshots.Single().Pos; // within reach

            server.StowSpeederForTest("Pilot", id);

            Assert.Equal(0, server.SpeederCount);
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
            Assert.Equal(0, p.State.Inventory.CountOf("speeder"));
        }
    }

    [Fact]
    public void DeployedBoat_SurvivesAReload_AsABoat()
    {
        string id;
        var server = NewServer(out var repo1);
        using (repo1)
        {
            Pilot(server);
            StonePad(server);
            Pool(server);
            id = server.DeployVehicleForTest("Pilot", "boat");
            Assert.NotEqual(string.Empty, id);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");
            server2.ReconcileSpeedersForTest("Pilot");
            Assert.Equal(1, server2.SpeederCount);
            Assert.Equal("boat", server2.VehicleKindForTest(id));
            Assert.Equal(0f, server2.SpeederSnapshots.Single().FuelMax);
        }
    }

    [Fact]
    public void WaterWorldStart_HandsOutABoat_OtherStartsDoNot()
    {
        var ocean = NewServer(out var repo1, startPlanet: "ocean");
        using (repo1)
        {
            Assert.True(ocean.StartBodyIsWaterWorldForTest);
            var p = ocean.AddLocalPlayer("Pilot");
            Assert.Equal(1, p.State.Inventory.CountOf("boat"));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        System.IO.Directory.Delete(_root, recursive: true);

        var varied = NewServer(out var repo2);
        using (repo2)
        {
            Assert.False(varied.StartBodyIsWaterWorldForTest);
            var p = varied.AddLocalPlayer("Pilot");
            Assert.Equal(0, p.State.Inventory.CountOf("boat"));
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
