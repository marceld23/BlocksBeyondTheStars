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

/// <summary>
/// The detoxifier ship module: a craftable processing station that converts poisonous drops into
/// safe food (toxic_gland → creature_meat). It reuses the crafting + station-gating system, so the
/// conversion only works when the ship has the module installed.
/// </summary>
public sealed class DetoxifierTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public DetoxifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_detox_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "detox"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "detox",
            Seed = 5,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void DetoxifyRecipe_IsWired()
    {
        var recipe = _content.Recipes["detoxify_gland"];
        Assert.Equal("toxic_gland", recipe.Inputs[0].Item);
        Assert.Equal("creature_meat", recipe.Outputs[0].Item);
    }

    [Fact]
    public void Detoxifier_IsPlaceable_AsAStationBlock()
    {
        // The planetside counterpart to the ship module: a craftable, placeable block that enables the
        // detoxifier station on foot (GameServer.StationAvailable maps Detoxifier -> the "detoxifier" block).
        var item = _content.GetItem("detoxifier");
        Assert.NotNull(item);
        Assert.Equal("detoxifier", item!.PlacesBlock);
        Assert.NotNull(_content.GetBlock("detoxifier"));
        Assert.Contains(_content.Recipes.Values, r => r.Outputs.Any(o => o.Item == "detoxifier"));
    }

    [Fact]
    public void Detoxify_ConvertsPoisonToFood_WhenModuleInstalled()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Cook");
            p.Ships[p.ActiveShipId].Modules.Add("detoxifier"); // install on the player's own ship
            p.State.AboardShip = true;
            p.State.Inventory.Add("toxic_gland", 1, 20);

            server.Craft("Cook", "detoxify_gland");

            Assert.Equal(0, p.State.Inventory.CountOf("toxic_gland")); // poison consumed
            Assert.Equal(1, p.State.Inventory.CountOf("creature_meat")); // safe food produced
        }
    }

    [Fact]
    public void Detoxify_Fails_WithoutTheModule()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Cook"); // starter ship has no detoxifier
            p.State.AboardShip = true;
            p.State.Inventory.Add("toxic_gland", 1, 20);

            server.Craft("Cook", "detoxify_gland");

            Assert.Equal(1, p.State.Inventory.CountOf("toxic_gland")); // nothing happened
            Assert.Equal(0, p.State.Inventory.CountOf("creature_meat"));
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
