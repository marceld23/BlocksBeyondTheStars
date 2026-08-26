// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Flowing fluids: water/lava spread down + sideways with level decay (World systems).</summary>
public sealed class FluidTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FluidTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fluid_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fluid"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "fluid", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void WaterAndLava_AreMineable_OnlyByTheMiningBeam()
    {
        // Fluids can be cleared now — but only with the mining beam (a tier-3 drill), not the starter drill.
        foreach (var key in new[] { "water", "lava" })
        {
            var def = _content.GetBlock(key)!;
            Assert.True(def.Mineable, $"{key} should be mineable.");
            Assert.Equal(ToolKind.Drill, def.RequiredTool);
            Assert.Equal(3, def.MinToolTier); // basic (1) + titanium (2) drills can't touch it
        }
    }

    [Fact]
    public void Water_BasicDrillCannot_MiningBeamCan()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var water = _content.GetBlock("water")!.NumericId.Value;
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 132f, 0.5f);

            var pos = new Vector3i(0, 130, 0); // high in the air column, isolated
            server.World.SetBlock(pos, new BlockId(water));

            // Starter basic drill (slot 0) — too weak for a fluid.
            server.MineBlock("Miner", pos.X, pos.Y, pos.Z);
            Assert.Equal(water, server.World.GetBlock(pos).Value);

            // Mining beam — clears it.
            p.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1));
            p.State.SelectedHotbarSlot = 6;
            server.MineBlock("Miner", pos.X, pos.Y, pos.Z);
            Assert.True(server.World.GetBlock(pos).IsAir, "The mining beam should clear water.");
        }
    }

    [Fact]
    public void WaterBody_RefillsAMinedHole()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId;
            int y0 = 60;

            // A stone basin (floor at y0) filled with a 9x9x3 body of static "sea" water (y0+1..y0+3).
            for (int x = -4; x <= 4; x++)
                for (int z = -4; z <= 4; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y0, z), stone);
                    for (int y = y0 + 1; y <= y0 + 3; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), water);
                    }
                }

            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, y0 + 5f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1));
            p.State.SelectedHotbarSlot = 6;

            // Punch a hole in the middle of the body (beam radius clears a 3x3x3 pocket).
            var hole = new Vector3i(0, y0 + 2, 0);
            server.MineBlock("Miner", hole.X, hole.Y, hole.Z);
            Assert.True(server.World.GetBlock(hole).IsAir, "Mining should open the hole first.");

            // The surrounding body flows back in over a few fluid steps.
            for (int i = 0; i < 12; i++)
            {
                server.Tick(0.3);
            }

            Assert.Equal(water.Value, server.World.GetBlock(hole).Value);
        }
    }

    [Fact]
    public void Fluid_DoesNotFlowIntoAShipInterior()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fluidship"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig { WorldName = "fluidship", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();

            var p = server.AddLocalPlayer("Pilot"); // spawns inside the ship
            server.Tick(0.1);
            Assert.True(p.State.AboardShip, "the player should spawn inside the ship");

            // Find two stacked interior-air cells in the cabin (a source on top, the target just below it).
            var floor = p.State.Position.ToBlock();
            Vector3i? src = null, below = null;
            for (int dy = 0; dy <= 8 && src == null; dy++)
            {
                var top = new Vector3i(floor.X, floor.Y + dy + 1, floor.Z);
                var bot = new Vector3i(floor.X, floor.Y + dy, floor.Z);
                if (server.ShipInteriorContainsCellForTest(top.X, top.Y, top.Z) && server.World.GetBlock(top).IsAir
                    && server.ShipInteriorContainsCellForTest(bot.X, bot.Y, bot.Z) && server.World.GetBlock(bot).IsAir)
                {
                    src = top;
                    below = bot;
                }
            }

            Assert.NotNull(src);
            Assert.NotNull(below);

            server.PlaceFluidSource("water", src!.Value.X, src.Value.Y, src.Value.Z); // seed water in the cabin
            for (int i = 0; i < 12; i++) server.Tick(0.3);

            // The sim must refuse to flow the source down through the interior cell below it — the cabin stays dry.
            Assert.True(server.World.GetBlock(below!.Value).IsAir, "fluid must not flow down into the ship interior");
        }
    }

    [Fact]
    public void Water_FlowsDownIntoAir()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // High in the air column above the surface = guaranteed empty cells.
            var src = new Vector3i(0, 130, 0);
            var below = new Vector3i(0, 129, 0);
            Assert.True(server.World.GetBlock(below).IsAir);

            server.PlaceFluidSource("water", src.X, src.Y, src.Z);
            server.Tick(0.3); // > fluid interval → one flow step

            Assert.False(server.World.GetBlock(below).IsAir); // water fell down
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(below).Value);
        }
    }

    [Fact]
    public void Water_SpreadsSidewaysOnFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Build a small solid floor in the air, place a source on it, let it spread.
            var stone = _content.GetBlock("stone")!.NumericId;
            int y = 130;
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y - 1, z), stone);
                }

            server.PlaceFluidSource("water", 0, y, 0);
            for (int i = 0; i < 6; i++)
            {
                server.Tick(0.3); // several flow steps
            }

            // Water should have reached a side cell on the floor.
            var side = new Vector3i(2, y, 0);
            Assert.Equal(_content.GetBlock("water")!.NumericId.Value, server.World.GetBlock(side).Value);
        }
    }

    [Fact]
    public void Water_PouringOverACliff_DoesNotHangInTheAir()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId.Value;
            int yTop = 140;

            // A short plateau ledge (x 0..3) high in the air, and a catch floor 20 blocks below (x 3..12).
            for (int x = 0; x <= 3; x++) server.World.SetBlock(new Vector3i(x, yTop - 1, 0), stone);
            for (int x = 3; x <= 12; x++) server.World.SetBlock(new Vector3i(x, yTop - 20, 0), stone);

            server.PlaceFluidSource("water", 0, yTop, 0); // source on the plateau
            for (int i = 0; i < 40; i++) server.Tick(0.3);

            // The old bug: water spread sideways at plateau height across the void, leaving a sheet floating in
            // the air. It must instead pour straight down at the lip — so cells well past the edge stay empty.
            Assert.True(server.World.GetBlock(new Vector3i(6, yTop, 0)).IsAir, "no water should hang at plateau height past the edge");
            Assert.True(server.World.GetBlock(new Vector3i(8, yTop, 0)).IsAir, "no floating shelf far out over the drop");

            // ...and the waterfall must actually reach the catch floor and pool on it.
            Assert.Equal(water, server.World.GetBlock(new Vector3i(4, yTop - 19, 0)).Value);
        }
    }

    [Fact]
    public void FlowingWater_Recedes_WhenItsSourceIsRemoved()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId.Value;
            int y = 138;

            // A flat floor in the air; a source on it spreads out into a thin sheet of flowing water.
            for (int x = -1; x <= 6; x++) server.World.SetBlock(new Vector3i(x, y - 1, 0), stone);

            server.PlaceFluidSource("water", 0, y, 0);
            for (int i = 0; i < 10; i++) server.Tick(0.3);
            Assert.Equal(water, server.World.GetBlock(new Vector3i(3, y, 0)).Value); // it flowed out first

            // Cut the source. The orphaned flowing cells have nothing feeding them any more, so they dry up.
            server.RemoveBlockForTest(0, y, 0);
            for (int i = 0; i < 40; i++) server.Tick(0.3);

            for (int x = 1; x <= 6; x++)
            {
                Assert.True(server.World.GetBlock(new Vector3i(x, y, 0)).IsAir,
                    $"flowing water at x={x} should have receded after the source was cut");
            }
        }
    }

    [Fact]
    public void FlowingWater_Recedes_AfterAServerRestart()
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId.Value;
        int y = 138;

        // Session 1: a source on a floor in the air spreads a thin sheet of flowing water.
        var server1 = Started(out var repo1);
        for (int x = -1; x <= 6; x++) server1.World.SetBlock(new Vector3i(x, y - 1, 0), stone);
        server1.PlaceFluidSource("water", 0, y, 0);
        for (int i = 0; i < 10; i++) server1.Tick(0.3);
        Assert.Equal(water, server1.World.GetBlock(new Vector3i(3, y, 0)).Value); // it flowed out
        repo1.Dispose(); // simulate a server restart on the same save

        // Session 2 on the same save: the sheet's cells must come back as FLOWING (tracked), not as sources.
        // The old bug (#657): levels were memory-only, so after a reload every flowing cell was untracked —
        // a permanent full source — and cutting the original source no longer dried anything up.
        var server2 = Started(out var repo2);
        using (repo2)
        {
            Assert.Equal(water, server2.World.GetBlock(new Vector3i(3, y, 0)).Value); // sheet survived the reload

            server2.RemoveBlockForTest(0, y, 0); // cut the source
            for (int i = 0; i < 40; i++) server2.Tick(0.3);

            for (int x = 1; x <= 6; x++)
            {
                Assert.True(server2.World.GetBlock(new Vector3i(x, y, 0)).IsAir,
                    $"flowing water at x={x} should have receded after the restart + source cut");
            }
        }
    }

    /// <summary>Two isolated cells high in the air column, a player in reach with the given items.</summary>
    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, params (string Item, int Count)[] items)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 132f, 0.5f);
        foreach (var (item, count) in items)
        {
            p.State.Inventory.Add(item, count, 64);
        }

        return p;
    }

    [Fact]
    public void WaterPlacedOntoALavaPool_QuenchesItToObsidian()
    {
        // Lyxette (2026-08-26): water placed onto lava simply replaced it (the #851 displace rule) and the two
        // sat side by side forever — the #477 contact rule only fired for FLOWING fluid. A lava SOURCE that
        // water lands on is quenched to obsidian in place; no water remains (#1284).
        var server = Started(out var repo);
        using (repo)
        {
            var lava = _content.GetBlock("lava")!.NumericId.Value;
            var obsidian = _content.GetBlock("obsidian")!.NumericId.Value;
            var pool = new Vector3i(0, 130, 0);
            var other = new Vector3i(1, 130, 0);
            server.World.SetBlock(pool, new BlockId(lava));
            server.World.SetBlock(other, new BlockId(lava));
            Builder(server, ("water", 4));

            server.PlaceBlock("Builder", pool.X, pool.Y, pool.Z, "water");

            Assert.Equal(obsidian, server.World.GetBlock(pool).Value);
            Assert.Equal(lava, server.World.GetBlock(other).Value); // the pool next to it was never touched by water
        }
    }

    [Fact]
    public void WaterPlacedBesideLava_HardensTheLavaToObsidian_AndStaysWater()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var lava = _content.GetBlock("lava")!.NumericId.Value;
            var water = _content.GetBlock("water")!.NumericId.Value;
            var obsidian = _content.GetBlock("obsidian")!.NumericId.Value;
            var lavaPos = new Vector3i(1, 130, 0);
            var waterPos = new Vector3i(0, 130, 0);
            server.World.SetBlock(lavaPos, new BlockId(lava));
            Builder(server, ("water", 4));

            server.PlaceBlock("Builder", waterPos.X, waterPos.Y, waterPos.Z, "water");

            Assert.Equal(obsidian, server.World.GetBlock(lavaPos).Value);
            Assert.Equal(water, server.World.GetBlock(waterPos).Value);
        }
    }

    [Fact]
    public void AWokenLavaPool_BesideWorldgenWater_CrustsToObsidian()
    {
        // Worldgen adjacency (cave lake against a lava pocket): nothing is placed, but the moment either cell is
        // woken — here the water, as mining next to it would — the lava side hardens.
        var server = Started(out var repo);
        using (repo)
        {
            var lava = _content.GetBlock("lava")!.NumericId.Value;
            var water = _content.GetBlock("water")!.NumericId.Value;
            var obsidian = _content.GetBlock("obsidian")!.NumericId.Value;
            var lavaPos = new Vector3i(0, 130, 0);
            var waterPos = new Vector3i(1, 130, 0);
            server.World.SetBlock(lavaPos, new BlockId(lava));
            server.World.SetBlock(waterPos, new BlockId(water));

            server.RegisterFluidSource(waterPos); // wake it
            for (int i = 0; i < 4; i++)
            {
                server.TickForTest(0.3);
            }

            Assert.Equal(obsidian, server.World.GetBlock(lavaPos).Value);
            Assert.Equal(water, server.World.GetBlock(waterPos).Value);
        }
    }

    [Fact]
    public void AFlowingLavaTongue_ReachingWater_CoolsToBasalt()
    {
        // Option (b): only a SOURCE makes obsidian — a flowing tongue that meets water cools to basalt.
        var server = Started(out var repo);
        using (repo)
        {
            var lava = _content.GetBlock("lava")!.NumericId.Value;
            var water = _content.GetBlock("water")!.NumericId.Value;
            var basalt = _content.GetBlock("basalt")!.NumericId.Value;
            var stone = _content.GetBlock("stone")!.NumericId.Value;

            // A floor, a lava source at one end, water two cells away: the tongue flows one cell and touches it.
            for (int x = -1; x <= 3; x++)
            {
                server.World.SetBlock(new Vector3i(x, 129, 0), new BlockId(stone));
            }

            var source = new Vector3i(0, 130, 0);
            var gap = new Vector3i(1, 130, 0);
            var pond = new Vector3i(2, 130, 0);
            server.World.SetBlock(source, new BlockId(lava));
            server.World.SetBlock(pond, new BlockId(water));
            server.RegisterFluidSource(source);

            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.3);
            }

            Assert.Equal(basalt, server.World.GetBlock(gap).Value); // the entering flow, not the source, solidified
            Assert.Equal(water, server.World.GetBlock(pond).Value);
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
