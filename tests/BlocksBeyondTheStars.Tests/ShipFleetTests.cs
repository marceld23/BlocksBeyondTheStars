// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Craftable ship types + owning multiple ships + switching the active one (ships.json).</summary>
public sealed class ShipFleetTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipFleetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fleet_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fleet"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fleet", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host"); // ships are per-player now — a player must exist to own one
        return server;
    }

    [Fact]
    public void ShipsContent_LoadsTypes()
    {
        Assert.True(_content.Ships.ContainsKey("starter"));
        Assert.True(_content.Ships.ContainsKey("hauler"));
        Assert.True(_content.Ships.ContainsKey("scout"));
    }

    [Fact]
    public void StartsWithOneActiveStarterShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Assert.Single(server.OwnedShips);
            Assert.Equal("default", server.ActiveShipId);
            Assert.Equal("starter", server.Ship.ShipType);
        }
    }

    [Fact]
    public void CraftShip_AddsToFleet_WhenUnlockedAndAffordable()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.UnlockedBlueprints.Add("ship_scout");
            pilot.State.Inventory.Add("titanium_plate", 25, 99);
            pilot.State.Inventory.Add("cable", 16, 99);
            pilot.State.Inventory.Add("energy_cell_1", 4, 99);
            pilot.State.Inventory.Add("glass", 6, 99);
            pilot.State.Inventory.Add("light_alloy", 6, 99);   // Task 5 Stage 4: scout now needs these
            pilot.State.Inventory.Add("circuit_board", 3, 99);

            var (ok, _) = server.CraftShip("Pilot", "scout");

            Assert.True(ok);
            Assert.Equal(2, server.OwnedShips.Count);
            Assert.Equal(0, pilot.State.Inventory.CountOf("titanium_plate")); // consumed
        }
    }

    [Fact]
    public void CraftShip_Rejected_WhenBlueprintLocked()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.Inventory.Add("titanium_plate", 99, 99);
            pilot.State.Inventory.Add("cable", 99, 99);
            pilot.State.Inventory.Add("energy_cell_1", 99, 99);
            pilot.State.Inventory.Add("glass", 99, 99);

            var (ok, _) = server.CraftShip("Pilot", "scout"); // ship_scout not unlocked

            Assert.False(ok);
            Assert.Single(server.OwnedShips);
        }
    }

    [Fact]
    public void SwitchShip_ChangesActiveAndStats()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true; // skip material cost
            pilot.State.UnlockedBlueprints.Add("ship_hauler");

            var (ok, id) = server.CraftShip("Pilot", "hauler");
            Assert.True(ok);

            Assert.True(server.SwitchShip(id));
            Assert.Equal(id, server.ActiveShipId);
            Assert.Equal("hauler", server.Ship.ShipType);
            Assert.Equal(170f, server.Ship.Hull); // hauler base hull
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
