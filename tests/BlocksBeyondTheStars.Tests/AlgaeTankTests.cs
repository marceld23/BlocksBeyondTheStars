// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The algae tank: a placeable base food machine that grows edible rations from plain water
/// (deliberately cheap — food production is meant to be EASY for now). It reuses the crafting +
/// station-gating system like the workbench/detoxifier: standing next to a placed tank enables the
/// grow recipe; there is no ship-module counterpart because life support aboard already sates hunger.
/// </summary>
public sealed class AlgaeTankTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AlgaeTankTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_algae_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "algae"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "algae", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AlgaeRecipes_AreWired()
    {
        // Growing: water only, at the algae-tank station, yields edible rations.
        var grow = _content.Recipes["algae_ration"];
        Assert.Equal(CraftingStation.AlgaeTank, grow.Station);
        Assert.Equal("water", Assert.Single(grow.Inputs).Item);
        Assert.Equal("algae_ration", grow.Outputs[0].Item);
        Assert.True(grow.Outputs[0].Count >= 2);

        // Building the tank: cheap workshop parts, no blueprint gate (early-game food machine).
        var build = _content.Recipes["algae_tank"];
        Assert.Equal(CraftingStation.Workshop, build.Station);
        Assert.True(string.IsNullOrEmpty(build.RequiredBlueprint));

        // The ration is real food, and ice gives a second easy water source next to snow.
        var ration = _content.GetItem("algae_ration");
        Assert.NotNull(ration);
        Assert.True(ration!.ConsumeHunger > 0f);
        Assert.Equal("water", _content.Recipes["water_ice"].Outputs[0].Item);

        // #1203: with bio-refining the tank is a refinery, not a ration dispenser — every new recipe is gated.
        foreach (var key in new[] { "biofuel_algae", "fiber_algae", "polymer_algae" })
        {
            Assert.Equal(CraftingStation.AlgaeTank, _content.Recipes[key].Station);
            Assert.Equal("bio_refining", _content.Recipes[key].RequiredBlueprint);
        }
    }

    [Fact]
    public void AlgaeTank_IsPlaceable_AsAStationBlock()
    {
        var item = _content.GetItem("algae_tank");
        Assert.NotNull(item);
        Assert.Equal("algae_tank", item!.PlacesBlock);
        Assert.NotNull(_content.GetBlock("algae_tank"));
        Assert.Contains(_content.Recipes.Values, r => r.Outputs.Any(o => o.Item == "algae_tank"));
    }

    [Fact]
    public void AlgaeTank_GrowsRations_FromWater_NearThePlacedTank()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Farmer");
            p.State.AboardShip = false;          // standing on a world, not aboard the ship
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.Add("water", 2, 99);

            // No tank nearby → the grow recipe is gated off.
            server.Craft("Farmer", "algae_ration");
            Assert.Equal(0, p.State.Inventory.CountOf("algae_ration"));

            // Place a tank next to the player → water turns into rations here.
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("algae_tank")!.NumericId);
            server.Craft("Farmer", "algae_ration");
            Assert.Equal(1, p.State.Inventory.CountOf("water"));       // one water consumed
            Assert.Equal(2, p.State.Inventory.CountOf("algae_ration")); // two rations grown
        }
    }

    [Fact]
    public void AlgaeTank_IsNotAvailable_AboardShip()
    {
        // No ship-module counterpart: aboard the ship life support sates hunger, so the grow
        // recipe must stay unavailable there even with water in the inventory.
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            p.State.AboardShip = true;
            p.State.Inventory.Add("water", 1, 99);

            server.Craft("Pilot", "algae_ration");

            Assert.Equal(1, p.State.Inventory.CountOf("water"));
            Assert.Equal(0, p.State.Inventory.CountOf("algae_ration"));
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
