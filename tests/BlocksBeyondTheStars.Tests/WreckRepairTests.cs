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
/// Repairing crashed ship wrecks into owned ships: the server validates each repaired cell against
/// the intact hull mask, consumes matching block items, and only allows claiming once complete.
/// </summary>
public sealed class WreckRepairTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WreckRepairTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_wreckrepair_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer StartedWithWreck(out SqliteWorldRepository repo)
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            repo = new SqliteWorldRepository(new SaveGamePaths(_root, "repair_" + seed));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = "repair_" + seed,
                Seed = seed,
                StartPlanet = "rocky",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = true,
            };

            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            if (server.HasWreck)
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No wreck found across 80 seeds.");
    }

    [Fact]
    public void RepairWreck_RequiresMatchingBlockItem()
    {
        var server = StartedWithWreck(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Mechanic");
            var cell = server.WreckRepairCells().First();
            player.State.Position = new Vector3f(cell.Pos.X + 0.5f, cell.Pos.Y + 0.5f, cell.Pos.Z + 0.5f);
            player.State.Inventory.Add("dirt", 1, 99);

            bool repaired = server.RepairWreck("Mechanic", cell.Pos.X, cell.Pos.Y, cell.Pos.Z, "dirt");

            Assert.False(repaired);
            Assert.NotEqual(_content.GetBlock(cell.BlockKey)!.NumericId, server.World.GetBlock(cell.Pos));
        }
    }

    [Fact]
    public void RepairWreck_ConsumesItem_AndRestoresHullCell()
    {
        var server = StartedWithWreck(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Mechanic");
            var cell = server.WreckRepairCells().First();
            string item = ItemForBlock(cell.BlockKey);
            player.State.Position = new Vector3f(cell.Pos.X + 0.5f, cell.Pos.Y + 0.5f, cell.Pos.Z + 0.5f);
            player.State.Inventory.Add(item, 1, 99);

            int before = server.WreckRepairRemaining;
            bool repaired = server.RepairWreck("Mechanic", cell.Pos.X, cell.Pos.Y, cell.Pos.Z, item);

            Assert.True(repaired);
            Assert.Equal(before - 1, server.WreckRepairRemaining);
            Assert.Equal(0, player.State.Inventory.CountOf(item));
            Assert.Equal(_content.GetBlock(cell.BlockKey)!.NumericId, server.World.GetBlock(cell.Pos));
        }
    }

    [Fact]
    public void ClaimWreck_RejectedUntilFullyRepaired()
    {
        var server = StartedWithWreck(out var repo);
        using (repo)
        {
            var before = server.OwnedShips.Count;
            var result = server.ClaimWreck("Pilot");

            Assert.False(result.Ok);
            Assert.Equal(before, server.OwnedShips.Count);
        }
    }

    [Fact]
    public void ClaimWreck_AddsOwnedShip_WhenFullyRepaired()
    {
        var server = StartedWithWreck(out var repo);
        using (repo)
        {
            var player = server.AddLocalPlayer("Pilot");
            foreach (var cell in server.WreckRepairCells().ToList())
            {
                string item = ItemForBlock(cell.BlockKey);
                player.State.Position = new Vector3f(cell.Pos.X + 0.5f, cell.Pos.Y + 0.5f, cell.Pos.Z + 0.5f);
                player.State.Inventory.Add(item, 1, 99);
                Assert.True(server.RepairWreck("Pilot", cell.Pos.X, cell.Pos.Y, cell.Pos.Z, item));
            }

            int before = server.OwnedShips.Count;
            var result = server.ClaimWreck("Pilot");

            Assert.True(result.Ok);
            Assert.True(server.WreckClaimed);
            Assert.Equal(before + 1, server.OwnedShips.Count);
            Assert.StartsWith("wreck_", result.ShipId);
            Assert.True(server.SwitchShip(result.ShipId));
        }
    }

    private string ItemForBlock(string blockKey)
    {
        foreach (var item in _content.Items.Values)
        {
            if (item.PlacesBlock == blockKey)
            {
                return item.Key;
            }
        }

        throw new Xunit.Sdk.XunitException($"No item places block '{blockKey}'.");
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
