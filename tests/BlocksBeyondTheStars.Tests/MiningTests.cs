// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Block hardness + drill tiers: soft blocks break in one hit, hard ones take several, and a
/// powerful drill mines a whole area at once.</summary>
public sealed class MiningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MiningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_mining_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "mining"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "mining", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void SoftBlock_BreaksInOneHit_WithBasicDrill()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f); // basic_drill is starter slot 0
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("mud")!.NumericId); // hardness 0.6

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir, "Mud should break in a single basic-drill hit.");
        }
    }

    [Fact]
    public void HardBlock_NeedsSeveralHits_WithBasicDrill()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("iron_ore")!.NumericId); // hardness 4.0 (B5), power 1

            for (int i = 0; i < 3; i++)
            {
                server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
                Assert.False(server.World.GetBlock(pos).IsAir, "Iron ore must not break in 3 hits or fewer.");
            }

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z); // 4th hit reaches hardness 4.0
            Assert.True(server.World.GetBlock(pos).IsAir, "Iron ore should break after a few hits.");
        }
    }

    [Fact]
    public void PowerfulDrill_MinesAnArea_AtOnce()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1)); // tier 3, power 4, radius 1
            p.State.SelectedHotbarSlot = 6;

            var stone = _content.GetBlock("stone")!.NumericId;
            var center = new Vector3i(0, 64, 0);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        server.World.SetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), stone);
                    }

            server.MineBlock("Miner", center.X, center.Y, center.Z);

            // The centre + its whole 3x3x3 neighbourhood are cleared in one go.
            int solid = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!server.World.GetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz)).IsAir)
                        {
                            solid++;
                        }
                    }

            Assert.Equal(0, solid);
        }
    }

    [Fact]
    public void Stone_RequiresDrill_NotMineableByHand()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.SelectedHotbarSlot = 5; // an empty slot → bare hands (no tool)
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId);

            for (int i = 0; i < 10; i++)
            {
                server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            }

            Assert.False(server.World.GetBlock(pos).IsAir, "Stone is a hard material — bare hands must not break it.");

            p.State.SelectedHotbarSlot = 0; // the starter basic drill
            for (int i = 0; i < 10; i++)
            {
                server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            }

            Assert.True(server.World.GetBlock(pos).IsAir, "Stone should break with a drill.");
        }
    }

    [Fact]
    public void WoodLog_RequiresDrill_NotMineableByHand()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.SelectedHotbarSlot = 5; // an empty slot → bare hands (no tool)
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("wood_log")!.NumericId); // hardness 1.6

            for (int i = 0; i < 6; i++)
            {
                server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            }

            Assert.False(server.World.GetBlock(pos).IsAir, "Wood logs need a tool — bare hands must not break them.");

            p.State.SelectedHotbarSlot = 0; // the starter basic drill
            for (int i = 0; i < 3; i++)
            {
                server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            }

            Assert.True(server.World.GetBlock(pos).IsAir, "Wood should break with a drill.");
        }
    }

    [Fact]
    public void PoweredDrill_DrainsSuitEnergy_PerSwing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("titanium_drill", 1)); // energyPerUse 0.5, power 2
            p.State.SelectedHotbarSlot = 6;
            p.State.SuitEnergy = 100f;
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("iron_ore")!.NumericId); // hardness 4.0 → 2 swings at power 2

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);
            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir, "Iron ore should break in two titanium-drill swings.");
            Assert.Equal(99f, p.State.SuitEnergy, 3); // 2 swings × 0.5 energy (#796)
        }
    }

    [Fact]
    public void PoweredDrill_RejectsSwing_WhenSuitEnergyEmpty()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("titanium_drill", 1)); // energyPerUse 0.5
            p.State.SelectedHotbarSlot = 6;
            p.State.SuitEnergy = 0.2f; // below the per-swing cost
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("mud")!.NumericId); // soft — would break in one paid swing

            server.MineBlockOnce("Miner", pos.X, pos.Y, pos.Z);

            Assert.False(server.World.GetBlock(pos).IsAir, "An empty suit must reject the swing — no progress for free.");
            Assert.Equal(0.2f, p.State.SuitEnergy, 3);
        }
    }

    [Fact]
    public void EnergyFreeDrills_KeepMining_AtZeroSuitEnergy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("diamond_drill", 1)); // tier 3, power 3.2, no energy cost
            p.State.SelectedHotbarSlot = 6;
            p.State.SuitEnergy = 0f;
            var pos = new Vector3i(0, 64, 0);
            server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId); // hardness 6.1 → 2 swings at power 3.2

            server.MineBlock("Miner", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir, "The diamond drill needs no energy — its whole niche (#796).");
            Assert.Equal(0f, p.State.SuitEnergy, 3);
        }
    }

    [Fact]
    public void AreaMining_LeavesBlocks_AboveTheToolsTier()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // No tier-1 area drill exists in the shipped data (the mining_beam is max-tier), so give this
            // test's private content copy one: the starter drill with a 3×3×3 sweep (#797).
            _content.GetItem("basic_drill")!.Tool!.MiningRadius = 1;

            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f); // starter basic_drill is slot 0
            var center = new Vector3i(0, 64, 0);
            var tier1Side = new Vector3i(1, 64, 0);
            var tier2Side = new Vector3i(-1, 64, 0);
            server.World.SetBlock(center, _content.GetBlock("stone")!.NumericId);
            server.World.SetBlock(tier1Side, _content.GetBlock("iron_ore")!.NumericId);      // tier 1 — swept
            server.World.SetBlock(tier2Side, _content.GetBlock("titanium_ore")!.NumericId);  // tier 2 — beyond the drill

            server.MineBlock("Miner", center.X, center.Y, center.Z);

            Assert.True(server.World.GetBlock(center).IsAir, "The centre block should break normally.");
            Assert.True(server.World.GetBlock(tier1Side).IsAir, "A same-tier neighbour is swept by area mining.");
            Assert.False(server.World.GetBlock(tier2Side).IsAir,
                "Area mining must not break ore above the tool's own tier (#797).");
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
