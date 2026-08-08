// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Building under water (#851). A placed block DISPLACES water/lava instead of being refused as "not empty" —
/// without this you cannot build under water at all, because the aim march passes through fluids (they have no
/// collider) so the cell offered while swimming always holds water, and water only yields to a tier-3 beam.
/// The two placeables that genuinely can't take a fluid cell stay refused: doors (an entity in an air cell) and
/// torches (an open flame).
/// </summary>
public sealed class UnderwaterPlacementTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public UnderwaterPlacementTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_uwplace_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "uwplace"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "uwplace", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceSettlements = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private const int PoolY = 180; // high above the terrain: an isolated basin, nothing else in reach

    /// <summary>A stone basin filled with a 5x5x3 body of static "sea" water and a diver floating in it.</summary>
    private BlocksBeyondTheStars.GameServer.PlayerSession Diver(SvGameServer server)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId;
        for (int x = -2; x <= 2; x++)
        {
            for (int z = -2; z <= 2; z++)
            {
                server.World.SetBlock(new Vector3i(x, PoolY, z), stone);
                for (int y = PoolY + 1; y <= PoolY + 3; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), water);
                }
            }
        }

        var p = server.AddLocalPlayer("Diver");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, PoolY + 2f, 0.5f);
        return p;
    }

    [Fact]
    public void Block_PlacedIntoWater_DisplacesTheFluid()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Diver(server);
            p.State.Inventory.Add("stone", 4, _content.MaxStackOf("stone"));

            // The cell the client offers while swimming: the water cell in front of the seabed it aimed at.
            var target = new Vector3i(1, PoolY + 1, 1);
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(target).Value);

            server.PlaceBlock("Diver", target.X, target.Y, target.Z, "stone");

            Assert.Equal(_content.GetBlock("stone")!.NumericId.Value, server.World.GetBlock(target).Value);
            Assert.Equal(3, p.State.Inventory.CountOf("stone")); // exactly one was consumed
        }
    }

    [Fact]
    public void PlacedBlock_IsNotFloodedAgainByTheSurroundingWater()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Diver(server);
            p.State.Inventory.Add("stone", 4, _content.MaxStackOf("stone"));

            var target = new Vector3i(1, PoolY + 1, 1);
            server.PlaceBlock("Diver", target.X, target.Y, target.Z, "stone");

            // Displacing a cell wakes the body around it — it must settle around the new block, not swallow it.
            for (int i = 0; i < 12; i++)
            {
                server.Tick(0.3);
            }

            Assert.Equal(_content.GetBlock("stone")!.NumericId.Value, server.World.GetBlock(target).Value);
        }
    }

    [Fact]
    public void PlacedBlock_SurvivesAReload_WithoutTheFluidComingBack()
    {
        var server = NewServer(out var repo);
        var target = new Vector3i(1, PoolY + 1, 1);
        using (repo)
        {
            var p = Diver(server);
            p.State.Inventory.Add("stone", 4, _content.MaxStackOf("stone"));
            server.PlaceBlock("Diver", target.X, target.Y, target.Z, "stone");
            server.Stop();
        }

        // A stale flowing-level row for the displaced cell would reload as a fluid cell on top of the block.
        var server2 = NewServer(out var repo2);
        using (repo2)
        {
            Assert.Equal(_content.GetBlock("stone")!.NumericId.Value, server2.World.GetBlock(target).Value);
            for (int i = 0; i < 12; i++)
            {
                server2.Tick(0.3);
            }

            Assert.Equal(_content.GetBlock("stone")!.NumericId.Value, server2.World.GetBlock(target).Value);
            server2.Stop();
        }
    }

    [Fact]
    public void Door_IsStillRefusedInAFluidCell()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Diver(server);
            p.State.Inventory.Add("door_hinge", 1, 99);

            var target = new Vector3i(1, PoolY + 1, 1);
            server.PlaceBlock("Diver", target.X, target.Y, target.Z, "door_hinge");

            // A door is an entity in an AIR cell — the water would just flow back around it.
            Assert.Equal(0, server.DoorCount);
            Assert.Equal(1, p.State.Inventory.CountOf("door_hinge")); // refused before anything was consumed
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(target).Value);
        }
    }

    [Fact]
    public void Torch_IsStillRefusedUnderWater()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Diver(server);
            p.State.Inventory.Add("torch", 1, _content.MaxStackOf("torch"));

            var target = new Vector3i(1, PoolY + 1, 1);
            server.PlaceBlock("Diver", target.X, target.Y, target.Z, "torch");

            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(target).Value);
            Assert.Equal(1, p.State.Inventory.CountOf("torch")); // an open flame stays in the pack
        }
    }

    [Fact]
    public void Block_PlacedIntoLava_DisplacesItToo()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var lava = _content.GetBlock("lava")!.NumericId;
            var p = Diver(server);
            p.State.Inventory.Add("stone", 4, _content.MaxStackOf("stone"));

            var target = new Vector3i(1, PoolY + 1, 1);
            server.World.SetBlock(target, lava);
            server.PlaceBlock("Diver", target.X, target.Y, target.Z, "stone");

            Assert.Equal(_content.GetBlock("stone")!.NumericId.Value, server.World.GetBlock(target).Value);
        }
    }

    [Fact]
    public void SolidCell_IsStillRefused()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Diver(server);
            p.State.Inventory.Add("stone", 4, _content.MaxStackOf("stone"));

            var floor = new Vector3i(1, PoolY, 1); // the basin's stone floor — occupied, not a fluid
            server.PlaceBlock("Diver", floor.X, floor.Y, floor.Z, "stone");

            Assert.Equal(4, p.State.Inventory.CountOf("stone")); // nothing consumed, the cell was not empty
        }
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_root))
            {
                System.IO.Directory.Delete(_root, recursive: true);
            }
        }
        catch (System.IO.IOException)
        {
            // best effort — a locked save file must not fail the test run
        }
    }
}
