// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Factory crafting (Phase 0 foundations): a factory recipe turns cheaper, less-rare raw materials
/// into the same output as a base recipe, but only at a <c>factory_terminal</c> block (off the ship,
/// inside a spawned factory). Factory-made items must not be disassembled back through the cheaper
/// factory recipe (anti-exploit). These tests stand a terminal block up by hand — the spawned factory
/// structures arrive in a later phase.
/// </summary>
public sealed class FactoryCraftingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FactoryCraftingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_factory_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "factory"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "factory",
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // Note: crafting a roster recipe at a real factory terminal is covered end-to-end by
    // FactoryStructureTests (a spawned factory enforces its roster). These tests cover the data layer:
    // station gating without a terminal, factory recipes being unavailable aboard ship, and the
    // disassembly exclusion.

    [Fact]
    public void FactoryRecipe_Unavailable_WithoutTerminal()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Maker");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(40.5f, 50.5f, 40.5f); // no terminal nearby
            p.State.Inventory.Add("iron_ore", 6, 99);

            server.Craft("Maker", "factory_iron_plate", 1);

            Assert.Equal(0, p.State.Inventory.CountOf("iron_plate")); // station not available -> no craft
            Assert.Equal(6, p.State.Inventory.CountOf("iron_ore"));   // raw untouched
        }
    }

    [Fact]
    public void FactoryRecipe_NotAvailableAboardShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Aboard ship there is no factory module — factories are world structures, never on a ship.
            var p = server.AddLocalPlayer("Maker"); // aboard by default
            p.State.Inventory.Add("iron_ore", 6, 99);

            server.Craft("Maker", "factory_iron_plate", 1);

            Assert.Equal(0, p.State.Inventory.CountOf("iron_plate"));
        }
    }

    [Fact]
    public void FactoryMadeItem_DisassemblesViaBaseRecipe_NotFactoryRecipe()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // iron_plate has a base workshop recipe (iron_ingot x2) AND a factory recipe (iron_ore x6).
            // Disassembly must pick the base recipe and refund iron_ingot — never the cheaper factory raw.
            var p = server.AddLocalPlayer("Maker"); // aboard, has a workshop module
            p.State.Inventory.Add("iron_plate", 1, 99);

            server.Disassemble("Maker", "iron_plate");

            Assert.True(p.State.Inventory.CountOf("iron_ingot") >= 1); // base recipe inputs recovered
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));    // factory recipe was excluded
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
