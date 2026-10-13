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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Props that need something to stand on (#2532 — "the seed should disappear because there's no block under it"):
/// when a cell turns into air or a fluid, the standing plant / sapling / torch / lantern above it and the hanging plant
/// below it pop. Wild flora vanishes, a sapling and a torch drop their item, a pending regrow rooted in the cell is
/// cancelled, and a host that merely changes block (grass → dirt) keeps its plants. Everything is built high in the air
/// column (y ≥ 120) where the test world is empty.
/// </summary>
public sealed class PropSupportTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PropSupportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_propsupport_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "propsupport")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private BlockId Id(string key) => _content.GetBlock(key)!.NumericId;

    /// <summary>A stone floor at y=120 and a gardener standing on it within reach of the origin column.</summary>
    private SvGameServer Garden(out SqliteWorldRepository repo) => Garden(out repo, out _);

    private SvGameServer Garden(out SqliteWorldRepository repo, out BlocksBeyondTheStars.GameServer.PlayerSession gardener)
    {
        var server = Started(out repo);
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                server.World.SetBlock(new Vector3i(x, 120, z), Id("stone"));
            }

        gardener = server.AddLocalPlayer("Gardener");
        gardener.State.AboardShip = false;
        gardener.State.Position = new Vector3f(2.5f, 121f, 0.5f);
        return server;
    }

    private static int DroppedCount(SvGameServer server, string item)
        => server.DropPackets.SelectMany(p => p.Items).Where(s => s.Item == item).Sum(s => s.Count);

    [Fact]
    public void MiningTheGround_UnderAWildPlant_PopsIt_AndLeavesNothing()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(cell, Id("flora_plant"));

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(host).IsAir);
            Assert.True(server.World.GetBlock(cell).IsAir, "a plant whose ground was mined must not float");
            Assert.Equal(0, DroppedCount(server, "plant_fiber")); // wild flora just vanishes
            Assert.Empty(server.DropPackets);
        }
    }

    [Fact]
    public void MiningTheCeiling_OverAHangingPlant_PopsIt()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 124, 0);
            var cell = new Vector3i(0, 123, 0);
            server.World.SetBlock(host, Id("stone"));
            server.World.SetBlock(cell, Id("flora_hangkelp"));

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(host).IsAir);
            Assert.True(server.World.GetBlock(cell).IsAir, "a hanging plant whose ceiling was mined must not float");
        }
    }

    [Fact]
    public void MiningTheLeaf_OverAFruit_DropsTheFruit()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var leaf = new Vector3i(0, 124, 0);
            var cell = new Vector3i(0, 123, 0);
            server.World.SetBlock(leaf, Id("tree_leaves"));
            server.World.SetBlock(cell, Id("flora_fruit_round"));

            server.MineBlock("Gardener", leaf.X, leaf.Y, leaf.Z);

            Assert.True(server.World.GetBlock(cell).IsAir, "a fruit whose leaf was mined must not float");
            // The harvest a pick gives — the poisonous twin where this world's fruit is toxic. Kids keep their fruit.
            Assert.Equal(1, DroppedCount(server, "fruit_round") + DroppedCount(server, "toxic_fruit_round"));
        }
    }

    [Fact]
    public void MiningTheGround_UnderSeagrass_LetsItVanish()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(cell, Id("flora_seagrass"));

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Empty(server.DropPackets); // non-fruit wild flora just vanishes
        }
    }

    [Fact]
    public void MiningTheBlockAbove_AStandingPlant_LeavesIt()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var cell = new Vector3i(0, 121, 0); // on the stone floor
            server.World.SetBlock(cell, Id("flora_plant"));
            server.World.SetBlock(new Vector3i(0, 122, 0), Id("dirt"));

            server.MineBlock("Gardener", 0, 122, 0);

            Assert.Equal(Id("flora_plant").Value, server.World.GetBlock(cell).Value); // its ground is below, untouched
        }
    }

    [Fact]
    public void ASapling_DropsItsItem_WhenItsGroundIsMined_AndNeverGrows()
    {
        var server = Garden(out var repo, out var gardener);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            gardener.State.Inventory.Add("sapling", 1, 99);
            server.PlaceBlock("Gardener", cell.X, cell.Y, cell.Z, "sapling");
            Assert.Equal(Id("flora_sapling").Value, server.World.GetBlock(cell).Value);
            Assert.True(server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z) > 0, "a planted sapling starts its clock");

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Equal(1, DroppedCount(server, "sapling")); // the player's investment lies on the ground
            Assert.Equal(-1, server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z));
            Assert.DoesNotContain(repo.ListFloraRegrow(server.World.LocationId), r => r.WorldPosition.Equals(cell));
        }
    }

    [Fact]
    public void ASaplingWithoutGround_DoesNotGrowIntoAFloatingTree()
    {
        var server = Garden(out var repo, out var gardener);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            gardener.State.Inventory.Add("sapling", 1, 99);
            server.PlaceBlock("Gardener", cell.X, cell.Y, cell.Z, "sapling");

            server.World.SetBlock(host, BlockId.Air); // the ground goes by a path that wakes nothing
            Assert.False(server.TryGrowTreeForTest(cell.X, cell.Y, cell.Z));

            server.Tick(200.0); // > SaplingGrowSeconds
            Assert.NotEqual(Id("wood_log").Value, server.World.GetBlock(cell).Value);
            Assert.Equal(-1, server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z)); // the timer is forgotten, not retried
        }
    }

    [Fact]
    public void AStandingTorch_DropsItsItem_WhenItsBlockIsMined()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var lit = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(lit, Id("torch"));

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(lit).IsAir, "a torch must not float over the hole");
            Assert.Equal(1, DroppedCount(server, "torch"));
        }
    }

    [Fact]
    public void AnUnlitLantern_DropsTheLantern_WhenItsBlockGoes()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(cell, Id("lantern_off"));

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Equal(1, DroppedCount(server, "lantern"));
        }
    }

    [Fact]
    public void ATorchBesideAWall_StaysWhenTheBlockUnderItGoes()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var lit = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(lit, Id("torch"));
            server.World.SetBlock(new Vector3i(-1, 122, 0), Id("stone")); // the wall that can hold it

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.Equal(Id("torch").Value, server.World.GetBlock(lit).Value); // read as wall-mounted
            Assert.Equal(0, DroppedCount(server, "torch"));
        }
    }

    [Fact]
    public void GrassTurningToDirt_KeepsTheMeadow()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("grass"));
            server.World.SetBlock(cell, Id("flora_plant"));

            server.World.SetBlock(host, Id("dirt"));
            server.SupportRemovedForTest(host.X, host.Y, host.Z); // even if a path ran the hook on the changed cell

            Assert.Equal(Id("flora_plant").Value, server.World.GetBlock(cell).Value);
        }
    }

    [Fact]
    public void ALily_StaysOnItsWater()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var water = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(water, Id("water"));
            server.World.SetBlock(cell, Id("flora_lily"));

            server.SupportRemovedForTest(water.X, water.Y, water.Z);

            Assert.Equal(Id("flora_lily").Value, server.World.GetBlock(cell).Value); // water IS a lily's ground
        }
    }

    [Fact]
    public void APoppedKelp_LetsTheWaterFlowIntoItsCell()
    {
        var server = Started(out var repo);
        using (repo)
        {
            const int y0 = 120;
            // A stone basin (floor at y0, walls at ±5) holding a 9×9×3 body of still water. In its middle a dirt host,
            // walled in by stone on its sides so only the kelp's cell connects it to the water.
            for (int x = -5; x <= 5; x++)
                for (int z = -5; z <= 5; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y0, z), Id("stone"));
                    bool wall = x == -5 || x == 5 || z == -5 || z == 5;
                    for (int y = y0 + 1; y <= y0 + 3; y++)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), wall ? Id("stone") : Id("water"));
                    }
                }

            var host = new Vector3i(0, y0 + 1, 0);
            var kelp = new Vector3i(0, y0 + 2, 0);
            foreach (var side in new[] { new Vector3i(1, y0 + 1, 0), new Vector3i(-1, y0 + 1, 0), new Vector3i(0, y0 + 1, 1), new Vector3i(0, y0 + 1, -1) })
            {
                server.World.SetBlock(side, Id("stone"));
            }

            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(kelp, Id("flora_kelp"));
            var p = server.AddLocalPlayer("Diver");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(3.5f, y0 + 6f, 3.5f);

            server.RemoveBlockForTest(host.X, host.Y, host.Z); // the mining path's removal + wakes

            Assert.NotEqual(Id("flora_kelp").Value, server.World.GetBlock(kelp).Value);
            for (int i = 0; i < 20; i++)
            {
                server.Tick(0.3);
            }

            Assert.Equal(Id("water").Value, server.World.GetBlock(host).Value); // reached only through the kelp's cell
        }
    }

    [Fact]
    public void AHarvestedPlant_WhoseGroundIsMined_DoesNotRegrow_EvenWhenTheGroundComesBack()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(cell, Id("flora_plant"));

            server.MineBlock("Gardener", cell.X, cell.Y, cell.Z); // harvest → a regrow is scheduled
            Assert.True(server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z) > 0);

            server.MineBlock("Gardener", host.X, host.Y, host.Z); // dig the ground away
            Assert.Equal(-1, server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z)); // cancelled, not left to fail later
            Assert.DoesNotContain(repo.ListFloraRegrow(server.World.LocationId), r => r.WorldPosition.Equals(cell));

            server.World.SetBlock(host, Id("dirt")); // the ground is put back
            server.Tick(31.0); // > FloraRegrowSeconds
            Assert.True(server.World.GetBlock(cell).IsAir, "a cancelled regrow does not come back");
        }
    }

    [Fact]
    public void AHarvestedHangingPlant_WhoseCeilingIsMined_LosesItsRegrow()
    {
        var server = Garden(out var repo);
        using (repo)
        {
            var host = new Vector3i(0, 124, 0);
            var cell = new Vector3i(0, 123, 0);
            server.World.SetBlock(host, Id("stone"));
            server.World.SetBlock(cell, Id("flora_hangkelp"));

            server.MineBlock("Gardener", cell.X, cell.Y, cell.Z);
            Assert.True(server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z) > 0);

            server.MineBlock("Gardener", host.X, host.Y, host.Z);
            Assert.Equal(-1, server.FloraRegrowTimerForTest(cell.X, cell.Y, cell.Z));
        }
    }

    [Fact]
    public void AnAreaDrill_AimedAtTheGroundUnderAPlant_HarvestsThePlantFirst()
    {
        var server = Garden(out var repo, out var gardener);
        using (repo)
        {
            var host = new Vector3i(0, 121, 0);
            var cell = new Vector3i(0, 122, 0);
            server.World.SetBlock(host, Id("dirt"));
            server.World.SetBlock(cell, Id("flora_plant"));
            gardener.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1)); // an area tool
            gardener.State.SelectedHotbarSlot = 6;
            gardener.State.SuitEnergy = 100f;
            int fibreBefore = gardener.State.Inventory.CountOf("plant_fiber") + gardener.State.Inventory.CountOf("toxic_plant_fiber");

            server.MineBlock("Gardener", host.X, host.Y, host.Z);

            Assert.True(server.World.GetBlock(host).IsAir);
            Assert.True(server.World.GetBlock(cell).IsAir);
            int fibre = gardener.State.Inventory.CountOf("plant_fiber") + gardener.State.Inventory.CountOf("toxic_plant_fiber")
                + DroppedCount(server, "plant_fiber") + DroppedCount(server, "toxic_plant_fiber");
            Assert.True(fibre > fibreBefore, "the plant on the aimed block is harvested by the burst, not popped");
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2532 review: a prop that loses its block during a Crystal Net beat pops after the tick
    // ---------------------------------------------------------------------------------------------------

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = at;
        foreach (var key in items)
        {
            p.State.Inventory.Add(key, 64, 1024);
        }

        p.State.SuitEnergy = 100f;
        return p;
    }

    private static void Ticks(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    [Fact]
    public void ATorch_WhoseGroundAnAutoDrillBites_DropsAfterTheBeat()
    {
        var server = Started(out var repo);
        using (repo)
        {
            const int y = 200;
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                {
                    server.World.SetBlock(new Vector3i(x, y - 1, z), Id("dirt")); // natural ground (no player edit)
                }

            var p = Builder(server, new Vector3f(0.5f, y + 3, 0.5f), "auto_drill_1", "crate", "crystal_switch");
            server.PlaceBlock("Builder", 0, y, 0, "auto_drill_1");
            server.PlaceBlock("Builder", 1, y, 0, "crate");
            server.PlaceBlock("Builder", 0, y, 1, "crystal_switch");
            server.SetCrystalDeviceForTest(p, new Vector3i(0, y, 0), action: 2, mode: (int)AutoDrillMode.Everything);
            var torch = new Vector3i(-2, y, -2); // over the drill's first bite (the layer's corner), nothing beside it
            server.World.SetBlock(torch, Id("torch"));

            server.SetCrystalDeviceForTest(p, new Vector3i(0, y, 1), action: 0); // ON
            Ticks(server, 3.0);

            Assert.True(server.World.GetBlock(new Vector3i(-2, y - 1, -2)).IsAir, "the drill took the torch's ground");
            Assert.True(server.World.GetBlock(torch).IsAir, "the torch must not float over the drilled hole");
            Assert.Equal(1, DroppedCount(server, "torch"));
        }
    }

    [Fact]
    public void ALantern_OnARetractedBridgeDeck_Drops()
    {
        var server = Started(out var repo);
        using (repo)
        {
            const int y = 200;
            var p = Builder(server, new Vector3f(0, y + 3, 0), "bridge_motor", "crystal_switch");
            server.PlaceBlock("Builder", 0, y, 0, "crystal_switch");
            server.PlaceBlock("Builder", 1, y, 0, "bridge_motor", deviceDir: 1); // points +X
            server.SetCrystalDeviceForTest(p, new Vector3i(0, y, 0), action: 0);
            Ticks(server, 2.5);
            Assert.Equal(Id("bridge_deck").Value, server.World.GetBlock(new Vector3i(3, y, 0)).Value);

            var lantern = new Vector3i(3, y + 1, 0);
            server.World.SetBlock(lantern, Id("lantern"));
            server.SetCrystalDeviceForTest(p, new Vector3i(0, y, 0), action: 0); // OFF: the deck comes back in
            Ticks(server, 2.0);

            Assert.True(server.World.GetBlock(new Vector3i(3, y, 0)).IsAir);
            Assert.True(server.World.GetBlock(lantern).IsAir, "the lantern must not float where the deck was");
            Assert.Equal(1, DroppedCount(server, "lantern"));
        }
    }
}
