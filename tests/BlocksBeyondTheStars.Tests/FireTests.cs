// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

/// <summary>Fire (item 30): lava/torch/weapon ignition, spread + burn down to ash, water/rain/stamping it out.</summary>
namespace BlocksBeyondTheStars.Tests;

public sealed class FireTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FireTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fire_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string world = "fire")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        return Started(repo, world);
    }

    /// <summary>Starts a server on an existing repository — so a test can stop one and reopen the same save.</summary>
    private SvGameServer Started(SqliteWorldRepository repo, string world)
    {
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;

    /// <summary>Selects the hotbar slot holding the item, so the server's "what is in the hand" checks see it.</summary>
    private static void Hold(BlocksBeyondTheStars.GameServer.PlayerSession session, string itemKey)
    {
        var inv = session.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is { IsEmpty: false } stack && stack.Item == itemKey)
            {
                session.State.SelectedHotbarSlot = i;
                return;
            }
        }

        Assert.Fail($"'{itemKey}' is not in the inventory");
    }

    private static void Tick(SvGameServer server, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            server.Tick(0.25);
        }
    }

    [Fact]
    public void Fire_BlocksAndItems_Exist()
    {
        Assert.NotNull(_content.GetBlock("fire"));
        Assert.NotNull(_content.GetBlock("ash"));
        Assert.False(_content.GetBlock("fire")!.Solid);     // fire is non-solid (walk through it)
        Assert.False(_content.GetBlock("fire")!.Mineable);  // can't be mined
        Assert.True(_content.GetBlock("fire")!.Emission > 0); // glows
    }

    [Fact]
    public void Ignited_Flora_BurnsDownToAsh()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(120, 80, 120);
            server.World.SetBlock(pos, _content.GetBlock("flora_bush")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value); // caught fire

            Tick(server, 20); // ~5 s — past the burn time
            Assert.Equal(Id("ash"), server.World.GetBlock(pos).Value); // burned out to ash
        }
    }

    [Fact]
    public void Fire_SpreadsToAdjacentFlammable()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = new Vector3i(140, 80, 140);
            var b = new Vector3i(141, 80, 140);
            server.World.SetBlock(a, _content.GetBlock("tree_leaves")!.NumericId);
            server.World.SetBlock(b, _content.GetBlock("tree_leaves")!.NumericId);

            server.IgniteForTest(a.X, a.Y, a.Z);
            Tick(server, 6); // spread is a per-step roll now (#791) — it lands well inside the burn time
            var v = server.World.GetBlock(b).Value;
            Assert.True(v == Id("fire") || v == Id("ash"), $"fire should spread to the neighbour (got {v})");
        }
    }

    [Fact]
    public void Water_ExtinguishesFire()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(160, 80, 160);
            server.World.SetBlock(pos, _content.GetBlock("flora_fern")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value);

            // A water block right next to the flame douses it back to air (not ash).
            server.World.SetBlock(new Vector3i(pos.X + 1, pos.Y, pos.Z), _content.GetBlock("water")!.NumericId);
            Tick(server, 1);
            Assert.True(server.World.GetBlock(pos).IsAir, "water should extinguish fire to air");
        }
    }

    [Fact]
    public void Lava_IgnitesAdjacentFlora()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var flora = new Vector3i(180, 80, 180);
            server.World.SetBlock(flora, _content.GetBlock("flora_vine")!.NumericId);
            server.PlaceFluidSource("lava", flora.X + 1, flora.Y, flora.Z); // lava right beside it

            Tick(server, 4); // the active lava ignites its flammable neighbour
            var v = server.World.GetBlock(flora).Value;
            Assert.True(v == Id("fire") || v == Id("ash"), $"lava should ignite the flora (got {v})");
        }
    }

    // ---------------- Flammability is data-driven (#785) ----------------

    /// <summary>The old rule keyed off the "flora_" prefix, so a pine's or palm's canopy never burned while
    /// underwater kelp did. The flag in data fixes both directions.</summary>
    [Fact]
    public void FlammabilityIsDataDriven_CanopiesBurn_AquaticPlantsDont()
    {
        Assert.True(_content.GetBlock("pine_needles")!.Flammable);
        Assert.True(_content.GetBlock("palm_frond")!.Flammable);
        Assert.True(_content.GetBlock("mushroom_cap")!.Flammable);
        Assert.True(_content.GetBlock("wood_log")!.Flammable);
        Assert.True(_content.GetBlock("tree_leaves")!.Flammable);

        Assert.False(_content.GetBlock("flora_kelp")!.Flammable);
        Assert.False(_content.GetBlock("flora_seagrass")!.Flammable);
        Assert.False(_content.GetBlock("flora_coral")!.Flammable);
        Assert.False(_content.GetBlock("flora_lily")!.Flammable);

        // Ground cover stays non-flammable, or a brush fire runs away across a whole biome.
        Assert.False(_content.GetBlock("grass")!.Flammable);
        Assert.False(_content.GetBlock("alien_grass")!.Flammable);
        Assert.False(_content.GetBlock("mycelium")!.Flammable);
        Assert.False(_content.GetBlock("fire")!.Flammable);
        Assert.False(_content.GetBlock("ash")!.Flammable);
    }

    [Fact]
    public void PineNeedles_Burn_ButKelpDoesNot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var needles = new Vector3i(200, 80, 200);
            var kelp = new Vector3i(220, 80, 220);
            server.World.SetBlock(needles, _content.GetBlock("pine_needles")!.NumericId);
            server.World.SetBlock(kelp, _content.GetBlock("flora_kelp")!.NumericId);

            server.IgniteForTest(needles.X, needles.Y, needles.Z);
            server.IgniteForTest(kelp.X, kelp.Y, kelp.Z);

            Assert.Equal(Id("fire"), server.World.GetBlock(needles).Value);
            Assert.Equal(Id("flora_kelp"), server.World.GetBlock(kelp).Value); // never catches
        }
    }

    // ---------------- Persistence (#784) ----------------

    /// <summary>A fire caught mid-burn by a restart must keep burning down. Before the burn timers were
    /// persisted the block survived as an edit while its timer did not, leaving a permanent, inert flame
    /// that never became ash yet still burned anyone standing in it.</summary>
    [Fact]
    public void BurningCells_KeepBurningDown_AfterAServerRestart()
    {
        var pos = new Vector3i(300, 80, 300);
        var paths = new SaveGamePaths(_root, "fire_restart");

        using (var repo = new SqliteWorldRepository(paths))
        {
            var server = Started(repo, "fire_restart");
            server.World.SetBlock(pos, _content.GetBlock("flora_bush")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value);
            Assert.Equal(1, server.BurningCellCount);
            server.SaveAllForTest();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo = new SqliteWorldRepository(paths))
        {
            var server = Started(repo, "fire_restart");
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value); // the flame is still there…
            Assert.Equal(1, server.BurningCellCount);                   // …and it is still tracked

            Tick(server, 20);
            Assert.Equal(Id("ash"), server.World.GetBlock(pos).Value); // so it resolves instead of hanging
            Assert.Equal(0, server.BurningCellCount);
        }
    }

    /// <summary>The saved row goes away with the flame, so a burnt-out world doesn't accumulate rows.</summary>
    [Fact]
    public void BurntOutCells_LeaveNoPersistedRow()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(320, 80, 320);
            server.World.SetBlock(pos, _content.GetBlock("flora_fern")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.NotEmpty(repo.ListFireCells(server.World.LocationId));

            Tick(server, 20);
            Assert.Empty(repo.ListFireCells(server.World.LocationId));
        }
    }

    // ---------------- Protection (#787) ----------------

    /// <summary>Settlements never catch fire — a village greenhouse is a wooden frame full of crops, and
    /// before this it burned down from a splash of lava (and would have been trivial to torch).</summary>
    [Fact]
    public void SettlementBlocks_NeverCatchFire()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fire_village"));
        using (repo)
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var server = new SvGameServer(
                new ServerConfig
                {
                    WorldName = "fire_village",
                    Seed = 1,
                    AutoSaveIntervalMinutes = 9999,
                    PlaceStarterShip = false,
                    PlaceSettlements = true, // we need a real settlement to test its protection
                },
                _content,
                st,
                repo);
            server.Start();

            var cell = server.ProtectedSettlementCellForTest();
            Assert.True(cell.HasValue, "expected this world to generate a settlement");

            // Put something thoroughly flammable inside the protected volume — a greenhouse crop bed is
            // exactly this: wood and plants, standing in a village.
            var pos = cell!.Value;
            server.World.SetBlock(pos, _content.GetBlock("wood_log")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);

            Assert.Equal(Id("wood_log"), server.World.GetBlock(pos).Value); // untouched
            Assert.Equal(0, server.BurningCellCount);
        }
    }

    // ---------------- Torch + stamping out (#786, #790) ----------------

    [Fact]
    public void SwingingATorch_SetsFlammableBlocksAlight()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(400, 80, 400);
            p.State.Inventory.Add("torch", 4, _content.MaxStackOf("torch"));
            Hold(p, "torch");

            var bush = new Vector3i(402, 80, 400);
            server.World.SetBlock(bush, _content.GetBlock("flora_bush")!.NumericId);
            server.MineBlockOnce("Justus", bush.X, bush.Y, bush.Z);

            Assert.Equal(Id("fire"), server.World.GetBlock(bush).Value);
            Assert.Equal(4, p.State.Inventory.CountOf("torch")); // a torch is a burning stick, not a match
        }
    }

    /// <summary>Bare hands still harvest the plant — the torch is what changes the swing's meaning.</summary>
    [Fact]
    public void WithoutATorch_TheSameSwingHarvestsThePlant()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(420, 80, 420);

            var bush = new Vector3i(422, 80, 420);
            server.World.SetBlock(bush, _content.GetBlock("flora_bush")!.NumericId);
            server.MineBlock("Justus", bush.X, bush.Y, bush.Z);

            Assert.True(server.World.GetBlock(bush).IsAir);
            Assert.NotEqual(Id("fire"), server.World.GetBlock(bush).Value);
        }
    }

    [Fact]
    public void HittingAFlame_StampsItOut()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(440, 80, 440);

            var pos = new Vector3i(442, 80, 440);
            server.World.SetBlock(pos, _content.GetBlock("flora_fern")!.NumericId);
            server.IgniteForTest(pos.X, pos.Y, pos.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(pos).Value);

            server.MineBlockOnce("Justus", pos.X, pos.Y, pos.Z);

            Assert.True(server.World.GetBlock(pos).IsAir, "hitting fire should put it out");
            Assert.Equal(0, server.BurningCellCount);
        }
    }

    // ---------------- Weapon ignition (#788) ----------------

    [Fact]
    public void EnergyWeapons_Ignite_KineticOnesDont()
    {
        Assert.True(_content.GetItem("laser_pistol")!.Tool!.Ignites);
        Assert.True(_content.GetItem("plasma_blaster")!.Tool!.Ignites);
        Assert.False(_content.GetItem("gauss_pistol")!.Tool!.Ignites);
        Assert.False(_content.GetItem("scrap_pistol")!.Tool!.Ignites);
        Assert.False(_content.GetItem("machete")!.Tool!.Ignites);
    }

    [Fact]
    public void ShootingAPlant_WithALaser_SetsItAlight_AndCostsSuitEnergy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(460, 80, 460);
            p.State.SuitEnergy = 50f;
            p.State.Inventory.Add("laser_pistol", 1, _content.MaxStackOf("laser_pistol"));
            Hold(p, "laser_pistol");

            var tree = new Vector3i(470, 80, 460); // 10 blocks out — inside the laser's 30-block range
            server.World.SetBlock(tree, _content.GetBlock("wood_log")!.NumericId);
            server.ShootBlockForTest("Justus", tree.X, tree.Y, tree.Z);

            Assert.Equal(Id("fire"), server.World.GetBlock(tree).Value);
            Assert.Equal(49f, p.State.SuitEnergy, 3); // one shot's worth of suit energy
        }
    }

    [Fact]
    public void ShootingBeyondTheWeaponsRange_IgnitesNothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(480, 80, 480);
            p.State.SuitEnergy = 50f;
            p.State.Inventory.Add("laser_pistol", 1, _content.MaxStackOf("laser_pistol"));
            Hold(p, "laser_pistol");

            var far = new Vector3i(580, 80, 480); // 100 blocks out — far past the laser's reach
            server.World.SetBlock(far, _content.GetBlock("wood_log")!.NumericId);
            server.ShootBlockForTest("Justus", far.X, far.Y, far.Z);

            Assert.Equal(Id("wood_log"), server.World.GetBlock(far).Value);
        }
    }

    [Fact]
    public void AKineticPistol_DoesNotIgnite()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(500, 80, 500);
            p.State.Inventory.Add("gauss_pistol", 1, _content.MaxStackOf("gauss_pistol"));
            Hold(p, "gauss_pistol");

            var tree = new Vector3i(505, 80, 500);
            server.World.SetBlock(tree, _content.GetBlock("wood_log")!.NumericId);
            server.ShootBlockForTest("Justus", tree.X, tree.Y, tree.Z);

            Assert.Equal(Id("wood_log"), server.World.GetBlock(tree).Value);
        }
    }

    // ---------------- Rain (#789) ----------------

    /// <summary>A storm puts out what it can reach. The fire under a roof keeps burning in the same storm —
    /// that pair is the whole point of the sky check.</summary>
    [Fact]
    public void AStorm_DousesSkyExposedFire_ButNotFireUnderARoof()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // High above any terrain, so the open cell genuinely sees the sky and the roofed one genuinely
            // doesn't — the sky scan is what separates the two.
            var open = new Vector3i(600, 600, 600);
            var sheltered = new Vector3i(620, 600, 620);
            server.World.SetBlock(open, _content.GetBlock("flora_fern")!.NumericId);
            server.World.SetBlock(sheltered, _content.GetBlock("flora_fern")!.NumericId);
            server.World.SetBlock(new Vector3i(sheltered.X, sheltered.Y + 2, sheltered.Z),
                _content.GetBlock("stone")!.NumericId); // a roof two blocks up

            server.IgniteForTest(open.X, open.Y, open.Z);
            server.IgniteForTest(sheltered.X, sheltered.Y, sheltered.Z);
            Assert.Equal(Id("fire"), server.World.GetBlock(open).Value);
            Assert.Equal(Id("fire"), server.World.GetBlock(sheltered).Value);

            server.SetWeatherForTest("storm");
            Tick(server, 3);

            Assert.True(server.World.GetBlock(open).IsAir, "a storm should douse an open fire");
            Assert.Equal(Id("fire"), server.World.GetBlock(sheltered).Value); // shielded by the roof
        }
    }

    /// <summary>Rain soaks the fuel too: while it falls on an open cell, nothing lights there at all.</summary>
    [Fact]
    public void WhileItStorms_OpenVegetationWontIgnite()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pos = new Vector3i(640, 600, 640); // open sky
            server.World.SetBlock(pos, _content.GetBlock("flora_bush")!.NumericId);

            server.SetWeatherForTest("storm");
            server.IgniteForTest(pos.X, pos.Y, pos.Z);

            Assert.Equal(Id("flora_bush"), server.World.GetBlock(pos).Value);
        }
    }

    // ---------------- Spread limits (#791) ----------------

    /// <summary>Fire stops passing the flame on past its hop cap, so one arson event can't consume an
    /// unbroken canopy end to end. The strip is far longer than the cap; the far end must survive.</summary>
    [Fact]
    public void Fire_StopsSpreadingPastTheHopCap()
    {
        var server = Started(out var repo);
        using (repo)
        {
            const int length = 40; // well past FireMaxSpreadHops (16)
            var start = new Vector3i(700, 80, 700);
            for (int i = 0; i < length; i++)
            {
                server.World.SetBlock(new Vector3i(start.X + i, start.Y, start.Z), _content.GetBlock("tree_leaves")!.NumericId);
            }

            server.IgniteForTest(start.X, start.Y, start.Z);
            // 15 s: long enough for the whole strip to burn through if nothing stopped it, short enough that
            // the world's own weather can't roll over to rain (it only re-rolls every 25 s) and douse it.
            Tick(server, 60);

            var farEnd = new Vector3i(start.X + length - 1, start.Y, start.Z);
            Assert.Equal(Id("tree_leaves"), server.World.GetBlock(farEnd).Value);
            Assert.Equal(0, server.BurningCellCount); // and the fire is out, not creeping on
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
