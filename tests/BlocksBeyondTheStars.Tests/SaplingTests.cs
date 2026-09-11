// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Saplings that grow into trees (#1774, Lyxette's arboretum): a sapling is flora for the placement rules (soil
/// host, inside the hull on a station) but no catalog species — no world roster, no greenhouse — and its timer
/// grows a tree instead of regrowing the plant. Leaves drop themselves so a crown can be built by hand.
/// </summary>
public sealed class SaplingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SaplingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts-saplings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string name = "saplings")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A dirt tile at (50,49,50) with a clear column above it, the farmer beside it with saplings in the pack.</summary>
    private static (Vector3i Cell, PlayerSession Session) PlantSapling(SvGameServer server, GameContent content, string playerId)
    {
        var dirt = content.GetBlock("dirt")!.NumericId;
        var host = new Vector3i(50, 49, 50);
        var cell = new Vector3i(50, 50, 50);
        server.World.SetBlock(host, dirt);
        for (int y = 50; y <= 58; y++)
            for (int dx = -3; dx <= 3; dx++)
                for (int dz = -3; dz <= 3; dz++)
                {
                    server.World.SetBlock(new Vector3i(50 + dx, y, 50 + dz), Shared.Primitives.BlockId.Air);
                }

        var session = server.AddLocalPlayer(playerId);
        session.State.Inventory.Add("sapling", 4, 99);
        session.State.Position = new Vector3f(51.5f, 50f, 51.5f);
        server.PlaceBlock(playerId, cell.X, cell.Y, cell.Z, "sapling");
        Assert.Equal(content.GetBlock("flora_sapling")!.NumericId.Value, server.World.GetBlock(cell).Value);
        return (cell, session);
    }

    private static int CountLeavesAround(SvGameServer server, GameContent content, Vector3i cell)
    {
        var leaf = content.GetBlock("tree_leaves")!.NumericId.Value;
        int n = 0;
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = 0; dy <= 8; dy++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (server.World.GetBlock(new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz)).Value == leaf)
                    {
                        n++;
                    }
                }

        return n;
    }

    [Fact]
    public void Sapling_PlantsOnSoil_NotOnStone_AndIsNoCatalogSpecies()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var dirt = _content.GetBlock("dirt")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;
            server.World.SetBlock(new Vector3i(50, 49, 50), dirt);
            server.World.SetBlock(new Vector3i(56, 49, 50), stone);
            Assert.True(server.CanPlantFlora("flora_sapling", 50, 50, 50), "a sapling plants on dirt");
            Assert.False(server.CanPlantFlora("flora_sapling", 56, 50, 50), "a sapling does not plant on stone");
            Assert.DoesNotContain(Shared.Definitions.FloraCatalog.All, s => s.Key == "flora_sapling"); // no roster, no greenhouse
        }
    }

    [Fact]
    public void Sapling_GrowsIntoATree_AfterItsTimer()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (cell, _) = PlantSapling(server, _content, "Farmer");
            var log = _content.GetBlock("wood_log")!.NumericId.Value;

            server.Tick(60.0);
            Assert.Equal(_content.GetBlock("flora_sapling")!.NumericId.Value, server.World.GetBlock(cell).Value); // still young

            server.Tick(100.0); // > SaplingGrowSeconds in total
            Assert.Equal(log, server.World.GetBlock(cell).Value);
            Assert.Equal(log, server.World.GetBlock(new Vector3i(cell.X, cell.Y + 3, cell.Z)).Value); // a trunk of at least four
            Assert.True(CountLeavesAround(server, _content, cell) >= 12, "a grown tree carries a leafy crown");
        }
    }

    [Fact]
    public void Sapling_WaitsUnderACeiling_AndGrows_OnceItIsGone()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (cell, _) = PlantSapling(server, _content, "Farmer");
            var sapling = _content.GetBlock("flora_sapling")!.NumericId.Value;
            var stone = _content.GetBlock("stone")!.NumericId;
            var ceiling = new Vector3i(cell.X, cell.Y + 2, cell.Z);
            server.World.SetBlock(ceiling, stone);

            server.Tick(200.0);
            Assert.Equal(sapling, server.World.GetBlock(cell).Value); // no room for a trunk: still a sapling

            server.World.SetBlock(ceiling, Shared.Primitives.BlockId.Air);
            server.Tick(35.0); // the retry interval
            Assert.Equal(_content.GetBlock("wood_log")!.NumericId.Value, server.World.GetBlock(cell).Value);
        }
    }

    [Fact]
    public void PickedSapling_ComesBackToThePocket_AndNeverRegrows()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var (cell, session) = PlantSapling(server, _content, "Farmer");
            int before = session.State.Inventory.CountOf("sapling");
            server.MineBlock("Farmer", cell.X, cell.Y, cell.Z);
            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Equal(before + 1, session.State.Inventory.CountOf("sapling"));

            server.Tick(200.0);
            Assert.True(server.World.GetBlock(cell).IsAir, "a picked sapling does not regrow like a harvested plant");
        }
    }

    [Fact]
    public void Leaves_DropLeafBlocks_YouCanPlaceAgain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Farmer");
            session.State.Position = new Vector3f(51.5f, 50f, 51.5f);
            foreach (var key in new[] { "tree_leaves", "pine_needles", "palm_frond" })
            {
                var cell = new Vector3i(50, 51, 50);
                server.World.SetBlock(cell, _content.GetBlock(key)!.NumericId);
                int before = session.State.Inventory.CountOf(key);
                server.MineBlock("Farmer", cell.X, cell.Y, cell.Z);
                Assert.Equal(before + 1, session.State.Inventory.CountOf(key));
                Assert.NotNull(_content.GetItem(key));
                Assert.Equal(key, _content.GetItem(key)!.PlacesBlock);
            }
        }
    }

    [Fact]
    public void SaplingGrowth_SurvivesARestart()
    {
        Vector3i cell;
        {
            var s1 = Started(out var repo1, "sapling-restart");
            using (repo1)
            {
                cell = PlantSapling(s1, _content, "Farmer").Cell;
                s1.Tick(60.0);
                repo1.Flush();
            }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var s2 = Started(out var repo2, "sapling-restart");
        using (repo2)
        {
            s2.AddLocalPlayer("Farmer");
            s2.Tick(160.0); // the persisted row restarts its clock, like a harvest regrow does
            Assert.Equal(_content.GetBlock("wood_log")!.NumericId.Value, s2.World.GetBlock(cell).Value);
        }
    }
}
