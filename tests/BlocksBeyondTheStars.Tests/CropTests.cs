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
/// The cultivated crops as a SET (#1204): the berry (#627) plus grain and the mushroom bed. Every farmed species
/// must close the same loop — a block that drops its yield, a seed that places it, a hand recipe that sows the
/// seed from that yield, hosts the greenhouse beds are made of, harvest + regrow on the server, and a scanner
/// readout that calls it edible flora under its own name. Anything a future crop forgets fails here.
/// </summary>
public sealed class CropTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CropTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_crops_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "crops"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "crops", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>The primary yield of a crop: its first non-fibre drop.</summary>
    private string YieldOf(string cropKey)
        => _content.GetBlock(cropKey)!.Drops.First(d => d.Item != "plant_fiber").Item;

    [Fact]
    public void ThreeCrops_AreCultivated_AndSitAtTheCatalogTail()
    {
        var keys = FloraCatalog.CultivatedKeys();
        Assert.Equal(new[] { "flora_cropberry", "flora_cropgrain", "flora_cropshroom" }, keys);

        // Wild species ids are catalog indices — a crop above a wild species would rename that species on
        // every world, so the cultivated block must stay at the very end of the list.
        int firstCrop = FloraCatalog.All.ToList().FindIndex(sp => sp.Cultivated);
        Assert.True(FloraCatalog.All.Skip(firstCrop).All(sp => sp.Cultivated),
            "cultivated species must form the tail of FloraCatalog.All");
    }

    [Fact]
    public void EveryCrop_HasBlock_Seed_AndAHandRecipe_ClosingTheLoop()
    {
        foreach (var key in FloraCatalog.CultivatedKeys())
        {
            var block = _content.GetBlock(key);
            Assert.NotNull(block);
            Assert.Equal("flora", block!.Category);
            Assert.DoesNotContain(block.Drops, d => d.Item == "toxic_berries");
            Assert.True(FloraCatalog.IsCultivated(key));

            // A seed item places the crop …
            var seed = _content.Items.Values.SingleOrDefault(i => i.PlacesBlock == key);
            Assert.True(seed != null, $"{key} needs a seed item that places it");

            // … a HAND recipe sows it (no station between the first harvest and your own bed) …
            var recipe = _content.Recipes.Values.SingleOrDefault(r => r.Outputs.Any(o => o.Item == seed!.Key));
            Assert.True(recipe != null, $"{seed!.Key} needs a recipe");
            Assert.Equal(CraftingStation.Hand, recipe!.Station);

            // … and every input of that recipe is something the crop itself drops, so one harvest is enough.
            var drops = block.Drops.Select(d => d.Item).ToHashSet();
            foreach (var input in recipe.Inputs)
            {
                Assert.Contains(input.Item, drops);
            }
        }
    }

    [Fact]
    public void EveryCrop_RootsOnSoilAndOnTheHydroTray()
    {
        foreach (var sp in FloraCatalog.All.Where(s => s.Cultivated))
        {
            // Village beds are dirt, city + station beds are trays: a crop the generator can't plant on either
            // would leave some greenhouses bare.
            Assert.Contains("dirt", sp.Hosts);
            Assert.Contains("hydro_tray", sp.Hosts);
            foreach (var host in sp.Hosts)
            {
                Assert.True(_content.GetBlock(host)?.FloraHost == true, $"'{host}' must carry the FloraHost flag");
            }
        }
    }

    [Fact]
    public void Grain_IsEdibleRaw_AndMushroomYieldIsTheExistingCap()
    {
        var grain = _content.GetItem("grain");
        Assert.NotNull(grain);
        Assert.Equal(ItemCategory.Consumable, grain!.Category);
        Assert.True(grain.ConsumeHunger > 0, "raw grain should ease hunger a little (meals come later)");
        Assert.True(grain.ConsumeHunger < _content.GetItem("berries")!.ConsumeHunger, "raw grain is poorer food than berries");

        // The mushroom bed yields the giant-cap material that already existed, not a new item.
        Assert.Equal("mushroom_cap", YieldOf("flora_cropshroom"));
        Assert.Equal("grain", YieldOf("flora_cropgrain"));
    }

    [Fact]
    public void Crops_PlantOnTheirHosts_NotOnStone()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var dirt = _content.GetBlock("dirt")!.NumericId;
            var tray = _content.GetBlock("hydro_tray")!.NumericId;
            var mycelium = _content.GetBlock("mycelium")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(50, 49, 50), dirt);
            server.World.SetBlock(new Vector3i(52, 49, 50), tray);
            server.World.SetBlock(new Vector3i(54, 49, 50), mycelium);
            server.World.SetBlock(new Vector3i(56, 49, 50), stone);

            foreach (var key in FloraCatalog.CultivatedKeys())
            {
                Assert.True(server.CanPlantFlora(key, 50, 50, 50), $"{key} should plant on dirt");
                Assert.True(server.CanPlantFlora(key, 52, 50, 50), $"{key} should plant on a hydro tray");
                Assert.False(server.CanPlantFlora(key, 56, 50, 50), $"{key} must not plant on stone");
            }

            // Only the mushroom bed takes to fungal soil; the cereal wants earth.
            Assert.True(server.CanPlantFlora("flora_cropshroom", 54, 50, 50));
            Assert.False(server.CanPlantFlora("flora_cropgrain", 54, 50, 50));
        }
    }

    [Fact]
    public void EveryCrop_YieldsOnHarvest_AndRegrowsOnItsBed()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Farmer");
            session.State.Position = new Vector3f(100f, 100f, 100f);
            var dirt = _content.GetBlock("dirt")!.NumericId;

            int lane = 0;
            foreach (var key in FloraCatalog.CultivatedKeys())
            {
                var crop = _content.GetBlock(key)!.NumericId;
                var host = new Vector3i(102 + lane, 99, 100);
                var cell = new Vector3i(102 + lane, 100, 100);
                lane += 2;
                server.World.SetBlock(host, dirt);
                server.World.SetBlock(cell, crop);

                string yield = YieldOf(key);
                int before = session.State.Inventory.CountOf(yield);
                server.MineBlock(session.State.PlayerId, cell.X, cell.Y, cell.Z);
                Assert.True(server.World.GetBlock(cell).IsAir, $"{key} should be gone right after harvest");
                Assert.True(session.State.Inventory.CountOf(yield) > before, $"harvesting {key} should yield {yield}");
                Assert.Equal(0, session.State.Inventory.CountOf("toxic_berries"));

                server.Tick(31.0); // > FloraRegrowSeconds
                Assert.Equal(crop.Value, server.World.GetBlock(cell).Value);
            }
        }
    }

    /// <summary>A crop sits in no world roster, so before #1204 the scanner read it as a plain block with no
    /// classification. It is a plant the player is meant to eat: the readout must say flora + edible.</summary>
    [Fact]
    public void Scanner_ReadsACrop_AsEdibleFlora_UnderItsOwnName()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Scout");
            foreach (var key in FloraCatalog.CultivatedKeys())
            {
                var r = server.ScanSubject("Scout", "block", key);
                Assert.Equal("flora", r.Kind);
                Assert.Equal("ui.scan.threat.edible", r.ThreatKey);
                Assert.Equal(key, r.Subject); // the client resolves the block key to its localized name
                Assert.Contains(r.Drops, d => d.Item == YieldOf(key));
            }

            // Control: an ordinary block still reads as a block with no threat line.
            var stone = server.ScanSubject("Scout", "block", "stone");
            Assert.Equal("block", stone.Kind);
            Assert.Equal(string.Empty, stone.ThreatKey);
        }
    }
}
