// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1048 — a persisted stack larger than its item's max stack (a hand-edited or corrupted save) must never reach
/// the live state. The persistence layer stays content-agnostic, so the server clamps every loaded inventory
/// (player inventory, ration store, ship cargo) to <c>GameContent.MaxStackOf</c> right after loading and logs
/// what it clamped. Pinned here across a real save round-trip: seed a save with hostile counts, start a server
/// on it, join.
/// </summary>
public sealed class InventoryClampTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public InventoryClampTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_clamp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private sealed class CapturingLogger : IGameLogger
    {
        public List<string> Warnings { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) { }
    }

    private SvGameServer Start(string tag, out SqliteWorldRepository repo, IGameLogger log)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig
        {
            WorldName = tag,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo, log);
        server.Start();
        return server;
    }

    private static SqliteWorldRepository Seed(string root, string tag)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(root, tag));
        repo.Initialize();
        return repo;
    }

    [Fact]
    public void PlayerInventoryAndRationStore_AreClampedToEachItemsMaxStack_OnJoin()
    {
        // A max-stack-1 tool, a low-stack consumable, and a stack sitting exactly AT its cap (must stay untouched).
        // The tool is not wearable gear: since #2110 the join moves gear into the suit's equipment slots (a tank would
        // leave slot 0 empty), and this test is about the clamp, not the migration.
        const string tool = "basic_drill", consumable = "medpack", bulk = "stone", ration = "creature_meat";
        Assert.Equal(1, _content.MaxStackOf(tool));
        Assert.True(_content.MaxStackOf(consumable) < 999);
        Assert.True(_content.MaxStackOf(ration) < 5000);

        using (var seed = Seed(_root, "player"))
        {
            var pilot = new PlayerState { PlayerId = "Pilot", Name = "Pilot" };
            pilot.Inventory.SetSlot(0, new ItemStack(tool, int.MaxValue));
            pilot.Inventory.SetSlot(1, new ItemStack(consumable, 999));
            pilot.Inventory.SetSlot(2, new ItemStack(bulk, _content.MaxStackOf(bulk)));
            pilot.RationStore.SetSlot(0, new ItemStack(ration, 5000));
            seed.SavePlayer(pilot);
        }

        var log = new CapturingLogger();
        var server = Start("player", out var repo, log);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            var inv = session.State.Inventory;

            Assert.Equal(1, inv.Slots[0]!.Count);
            Assert.Equal(_content.MaxStackOf(consumable), inv.Slots[1]!.Count);
            Assert.Equal(_content.MaxStackOf(bulk), inv.Slots[2]!.Count);
            Assert.Equal(_content.MaxStackOf(ration), session.State.RationStore.Slots[0]!.Count);

            // Every clamp is logged with the item and the original count; the in-range stack is not.
            Assert.Contains(log.Warnings, w => w.Contains(tool) && w.Contains(int.MaxValue.ToString()));
            Assert.Contains(log.Warnings, w => w.Contains(consumable) && w.Contains("999"));
            Assert.Contains(log.Warnings, w => w.Contains("ration store") && w.Contains(ration) && w.Contains("5000"));
            Assert.DoesNotContain(log.Warnings, w => w.Contains($"'{bulk}'"));
        }
    }

    [Fact]
    public void LegacyShipCargo_IsClampedOnLoad()
    {
        // A save from before per-ship persistence (#848): the ship lives under the legacy single-ship key.
        const string item = "medpack";
        using (var seed = Seed(_root, "legacy"))
        {
            seed.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot" });
            var ship = new ShipState { ShipType = "hauler" };
            ship.Cargo.SetSlot(0, new ItemStack(item, int.MaxValue));
            seed.SaveShip("ship_Pilot", ship);
        }

        var log = new CapturingLogger();
        var server = Start("legacy", out var repo, log);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");

            Assert.Equal("hauler", server.Ship.ShipType);
            Assert.Equal(_content.MaxStackOf(item), server.Ship.Cargo.CountOf(item));
            Assert.Contains(log.Warnings, w => w.Contains("cargo") && w.Contains(item) && w.Contains(int.MaxValue.ToString()));
        }
    }

    [Fact]
    public void FleetShipCargo_IsClampedOnLoad()
    {
        const string item = "oxygen_tank_1";
        using (var seed = Seed(_root, "fleet"))
        {
            seed.SavePlayer(new PlayerState
            {
                PlayerId = "Pilot",
                Name = "Pilot",
                FleetShipIds = { "hauler_1" },
                ActiveShipId = "hauler_1",
            });
            var ship = new ShipState { ShipType = "hauler" };
            ship.Cargo.SetSlot(0, new ItemStack(item, 99));
            seed.SaveShip("ship_Pilot#hauler_1", ship);
        }

        var log = new CapturingLogger();
        var server = Start("fleet", out var repo, log);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");

            var hauler = server.OwnedShips["hauler_1"];
            Assert.Equal(1, hauler.Cargo.CountOf(item));
            Assert.Contains(log.Warnings, w => w.Contains("hauler_1") && w.Contains(item) && w.Contains("99"));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root))
            {
                System.IO.Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}
