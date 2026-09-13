// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
/// The obtainable grass block (#1847): a player who wanted a green floor could only ever get dirt — grass
/// dropped dirt and had no item of its own. Now a mined grass block drops itself, the item places it back,
/// and a spade of dirt plus a handful of plant fibre grows a fresh block by hand.
/// </summary>
public sealed class GrassBlockTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GrassBlockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_grass_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void GrassItem_PlacesTheGrassBlock_AndStacksLikeDirt()
    {
        var grass = _content.GetItem("grass");
        Assert.NotNull(grass);
        Assert.Equal(ItemCategory.Block, grass!.Category);
        Assert.Equal("grass", grass.PlacesBlock);
        Assert.Equal("item.grass.name", grass.NameKey);
        Assert.Equal("item.grass.desc", grass.DescriptionKey);
        Assert.Equal(_content.GetItem("dirt")!.MaxStack, grass.MaxStack);
    }

    [Fact]
    public void GrassBlock_DropsItself_NotDirt()
    {
        var block = _content.GetBlock("grass");
        Assert.NotNull(block);
        var drop = Assert.Single(block!.Drops);
        Assert.Equal("grass", drop.Item);
        Assert.Equal(1, drop.Count);
    }

    [Fact]
    public void GrassRecipe_IsHandTier_DirtPlusFibre()
    {
        var recipe = _content.GetRecipe("grass");
        Assert.NotNull(recipe);
        Assert.Equal(CraftingStation.Hand, recipe!.Station);
        Assert.Null(recipe.RequiredBlueprint);
        Assert.Equal(2, recipe.Inputs.Count);
        Assert.Contains(recipe.Inputs, i => i.Item == "dirt" && i.Count == 1);
        Assert.Contains(recipe.Inputs, i => i.Item == "plant_fiber" && i.Count == 1);
        var output = Assert.Single(recipe.Outputs);
        Assert.Equal("grass", output.Item);
        Assert.Equal(1, output.Count);
    }

    [Fact]
    public void GrassBlock_RoundTrips_ThroughPlaceAndMine()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "grass"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "grass", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceSettlements = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        var p = server.AddLocalPlayer("Gardener");
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the target cell is empty
        p.State.Inventory.Add("grass", 1, _content.MaxStackOf("grass"));

        var cell = new Vector3i(1, 200, 0);
        server.PlaceBlock("Gardener", cell.X, cell.Y, cell.Z, "grass");
        Assert.Equal(_content.GetBlock("grass")!.NumericId.Value, server.World.GetBlock(cell).Value);
        Assert.Equal(0, p.State.Inventory.CountOf("grass"));

        for (int hit = 0; hit < 20 && !server.World.GetBlock(cell).IsAir; hit++)
        {
            server.MineBlockOnce("Gardener", cell.X, cell.Y, cell.Z);
        }

        Assert.True(server.World.GetBlock(cell).IsAir);
        Assert.Equal(1, p.State.Inventory.CountOf("grass")); // the turf comes back as turf, not as dirt
        Assert.Equal(0, p.State.Inventory.CountOf("dirt"));
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
