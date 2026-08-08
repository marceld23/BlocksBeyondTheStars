// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The low-tech furniture &amp; survival batch (#803-#809): the bed as the hand-tier heal-tank precursor
/// (home spawn + weak heal-only regen), the campfire as warmth + cooking station, the wood box as the
/// hand-tier container with few stack slots, and the furniture BlockShape forms (Table/Chair/Fence/
/// Sheet/Pot) that give every shapeable material its wooden/metal/stone variant for free.
/// </summary>
public sealed class LowTechFurnitureTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LowTechFurnitureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_lowtech_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "lowtech"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "lowtech", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Content_IsWired_AndHandTier()
    {
        // Every new block has a placing item that references it, and every recipe is HAND tier
        // (except cooking, which needs the campfire): reachable long before the blueprint economy.
        foreach (var key in new[] { "bed", "campfire", "wood_crate", "lantern", "rug", "flower_pot" })
        {
            Assert.NotNull(_content.GetBlock(key));
            var item = _content.GetItem(key);
            Assert.NotNull(item);
            Assert.Equal(key, item!.PlacesBlock);

            var recipe = _content.Recipes[key];
            Assert.Equal(CraftingStation.Hand, recipe.Station);
            Assert.True(string.IsNullOrEmpty(recipe.RequiredBlueprint), $"'{key}' must not be blueprint-gated");
        }

        // Cooked meat strictly dominates raw meat (no downside), and cooking happens at the campfire.
        var raw = _content.GetItem("creature_meat")!;
        var cooked = _content.GetItem("cooked_meat")!;
        Assert.True(cooked.ConsumeHunger > raw.ConsumeHunger, "cooked meat must sate more hunger than raw");
        Assert.True(cooked.ConsumeHealth >= raw.ConsumeHealth, "cooked meat must heal at least as much as raw");
        Assert.Equal(CraftingStation.Campfire, _content.Recipes["cooked_meat"].Station);
    }

    [Fact]
    public void Bed_HealsSlowly_ButNeverFeedsOrRecharges()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sleeper");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Health = 50f;
            p.State.SuitEnergy = 10f;

            // Baseline drift without a bed (passive atmosphere regen / hunger drain vary per world).
            server.TickForTest(2.0);
            float baselineGain = p.State.Health - 50f;

            // With a bed nearby the heal is clearly faster — but hunger and suit energy get NOTHING
            // (those stay the heal tank's value-add).
            p.State.Health = 50f;
            p.State.Hunger = 50f;
            server.World.SetBlock(new Vector3i(2, 64, 0), _content.GetBlock("bed")!.NumericId);
            server.TickForTest(2.0);

            Assert.True(p.State.Health - 50f > baselineGain + 1.5f,
                $"bed should add ~1.5 HP/s over the baseline (baseline {baselineGain}, got {p.State.Health - 50f})");
            Assert.True(p.State.Hunger <= 50f, $"a bed must not feed (hunger {p.State.Hunger})");
            Assert.Equal(10f, p.State.SuitEnergy, 3);
        }
    }

    [Fact]
    public void Bed_SetsHomeSpawn_AndRespawnWakesThere()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Camper");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("bed")!.NumericId);

            server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
            Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId), "E on a bed must arm the home spawn");
            var home = p.State.CustomSpawnPoint;

            // Die elsewhere, choose the home spawn: wake at the bed (the bed passes the anchor check
            // that used to require a heal tank).
            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: true);

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(home, p.State.Position);
            Assert.False(p.State.AboardShip);
        }
    }

    [Fact]
    public void Campfire_CooksMeat_OnlyAtTheFire()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Cook");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("creature_meat", 2, 20);

            // No campfire anywhere → the cooking recipe is unavailable.
            server.Craft(p.State.PlayerId, "cooked_meat");
            Assert.Equal(0, p.State.Inventory.CountOf("cooked_meat"));

            // A placed campfire next to the cook enables it.
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("campfire")!.NumericId);
            server.Craft(p.State.PlayerId, "cooked_meat");
            Assert.Equal(1, p.State.Inventory.CountOf("cooked_meat"));
            Assert.Equal(1, p.State.Inventory.CountOf("creature_meat"));
        }
    }

    [Fact]
    public void Campfire_Warmth_LiftsAColdReading_IntoTheComfortBand()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3f(0.5f, 64, 0.5f);

            // Ambient -30 °C with no source stays freezing …
            Assert.Equal(-30f, server.ApplyLocalSourcesForTest(pos, -30f), 3);

            // … but a campfire two cells away lifts it like open fire does (30 − 4·(dist−1) = 26 °C
            // at chebyshev distance 2 from the probe centre) — severity 0, i.e. cosy.
            server.World.SetBlock(new Vector3i(2, 64, 0), _content.GetBlock("campfire")!.NumericId);
            float warmed = server.ApplyLocalSourcesForTest(pos, -30f);
            Assert.True(warmed >= 20f, $"campfire should lift -30°C into the comfort band (got {warmed})");
            Assert.Equal(0f, SvGameServer.TemperatureSeverityFor(warmed));
        }
    }

    [Fact]
    public void WoodCrate_IsAContainer_WithFewStackSlots()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Boxer");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("wood_crate", 1, 64);

            server.PlaceBlock(p.State.PlayerId, 1, 64, 0, "wood_crate");
            // Worldgen pre-seeds ruin salvage containers — pick out OUR box by its crate kind.
            var box = Assert.Single(server.Containers, c => c.Kind == "crate");

            // Ten distinct loose materials in the pockets — the box accepts only its slot budget (8);
            // the remaining stacks stay with the player instead of vanishing.
            string[] ores =
            {
                "iron_ore", "copper_ore", "silicate", "carbon", "gold_ore",
                "silver_ore", "tin_ore", "zinc_ore", "lead_ore", "sulfur_ore",
            };
            foreach (var ore in ores)
            {
                p.State.Inventory.Add(ore, 5, 1024);
            }

            server.DepositToContainer(p.State.PlayerId, box.Id);

            // The cap holds (8 distinct stacks — the starter kit's own loose materials count too),
            // the overflow stays with the player, and nothing vanishes in between.
            Assert.Equal(8, box.Items.Count);
            Assert.True(ores.Any(o => p.State.Inventory.CountOf(o) > 0), "overflow ores must stay in the inventory");
            foreach (var ore in ores)
            {
                int inBox = box.Items.Where(s => s.Item == ore).Sum(s => s.Count);
                Assert.Equal(5, inBox + p.State.Inventory.CountOf(ore));
            }

            // Mining the box hands its stored stacks back (shared crate plumbing).
            server.MineBlock(p.State.PlayerId, 1, 64, 0);
            Assert.DoesNotContain(server.Containers, c => c.Kind == "crate");
            Assert.Equal(1, p.State.Inventory.CountOf("wood_crate"));
            Assert.Equal(10, ores.Sum(o => p.State.Inventory.CountOf(o) > 0 ? 1 : 0));
        }
    }

    [Fact]
    public void FurnitureShapes_RoundTripThroughTheDescriptor()
    {
        Assert.Equal(19, ShapeCode.Count);
        foreach (var shape in new[] { BlockShape.Table, BlockShape.Chair, BlockShape.Fence, BlockShape.Sheet, BlockShape.Pot })
        {
            int packed = ShapeCode.Pack(shape, 3, 4);
            Assert.Equal((int)shape, ShapeCode.ShapeOf(packed));
            Assert.Equal(3, ShapeCode.OrientationOf(packed));
            Assert.Equal(4, ShapeCode.UpFaceOf(packed));
            Assert.True(ShapeCode.IsValidShape((int)shape));
        }

        Assert.False(ShapeCode.IsValidShape(ShapeCode.Count)); // one past the end stays invalid
    }

    [Fact]
    public void FurnitureDefaults_StampOnPlace_AndDropThePlainItem()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Decorator");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            p.State.Inventory.Add("rug", 1, 64);

            // Placing a rug stamps its default Sheet form (it must read as a rug, not a cube) …
            server.PlaceBlock(p.State.PlayerId, 1, 64, 0, "rug");
            Assert.Equal((int)BlockShape.Sheet, ShapeCode.ShapeOf(server.World.GetShape(new Vector3i(1, 64, 0))));

            // … and mining it returns the PLAIN item key, so it stacks with freshly crafted rugs.
            server.MineBlock(p.State.PlayerId, 1, 64, 0);
            Assert.Equal(1, p.State.Inventory.CountOf("rug"));
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
