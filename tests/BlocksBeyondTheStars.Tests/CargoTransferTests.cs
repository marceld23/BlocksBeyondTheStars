// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Moving items between the personal inventory and the ship's cargo hold: per-item both ways, bulk "stow all"
/// (materials/components only — tools stay) and "take all", and the aboard-ship gate. Mirrors the storage-crate
/// deposit behaviour, but for the ship's own hold.
/// </summary>
public sealed class CargoTransferTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CargoTransferTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cargo_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cargo"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "cargo", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private BlocksBeyondTheStars.GameServer.PlayerSession AboardPilot(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Pilot");
        p.State.AboardShip = true; // in the cabin / cockpit → the cargo hold is reachable
        return p;
    }

    [Fact]
    public void StowAll_MovesMaterials_ButKeepsTools()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.Inventory.Add("iron_ore", 10, 99);
            int machetes = p.State.Inventory.CountOf("machete"); // starter kit carries one

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));        // loose material stowed
            Assert.Equal(10, server.Ship.Cargo.CountOf("iron_ore"));       // ...into the hold
            Assert.Equal(machetes, p.State.Inventory.CountOf("machete"));  // the tool stayed on the player
            Assert.Equal(0, server.Ship.Cargo.CountOf("machete"));
        }
    }

    [Fact]
    public void TakeAll_PullsEverythingBackOut()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            server.Ship.Cargo.Add("iron_ore", 12, 99);

            Assert.True(server.MoveCargoForTest("Pilot", toCargo: false, bulkAll: true));

            Assert.Equal(12, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void PerItem_MovesBothDirections()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.Inventory.Add("iron_ore", 6, 99);

            // Inventory → cargo for just that item.
            Assert.True(server.MoveCargoForTest("Pilot", toCargo: true, item: "iron_ore"));
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(6, server.Ship.Cargo.CountOf("iron_ore"));

            // ...and back again.
            Assert.True(server.MoveCargoForTest("Pilot", toCargo: false, item: "iron_ore"));
            Assert.Equal(6, p.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void NotAboard_IsRejected()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = AboardPilot(server);
            p.State.AboardShip = false; // walked off the ship onto the surface
            p.State.Inventory.Add("iron_ore", 5, 99);

            Assert.False(server.MoveCargoForTest("Pilot", toCargo: true, bulkAll: true));

            Assert.Equal(5, p.State.Inventory.CountOf("iron_ore")); // nothing moved
            Assert.Equal(0, server.Ship.Cargo.CountOf("iron_ore"));
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
