// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Bandits: lone robbers on foot (hold-up protocol — demand ~35 % of the largest non-tool stacks,
/// comply → they leave, refuse/attack/silence → they fight), bandit-camp guards with persisted
/// "cleared" state, and bandit-ship ambushes in space (hail → pay or fight, hard-gated on rules
/// that let the player shoot back).
/// </summary>
public sealed class BanditTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BanditTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_bandit_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, Action<GameRules>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false, // seed-dependent worldgen camps stay out of these deterministic tests
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off; // no machines wandering into the hold-up
        config.Rules.Bandits = AlienActivity.Normal;
        configure?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>On-foot player with a hand-authored inventory: 100 iron ore, 40 copper ore, a machete.</summary>
    private static PlayerSession Robbable(SvGameServer server, string name)
    {
        var pilot = server.AddLocalPlayer(name);
        pilot.State.AboardShip = false;
        var inv = pilot.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, null);
        }

        inv.SetSlot(0, new ItemStack("iron_ore", 100));
        inv.SetSlot(1, new ItemStack("copper_ore", 40));
        inv.SetSlot(2, new ItemStack("machete", 1));
        return pilot;
    }

    private static void WalkUp(SvGameServer server, PlayerSession pilot)
    {
        server.SpawnBanditAtForTest(new Vector3f(
            pilot.State.Position.X + 3f, pilot.State.Position.Y, pilot.State.Position.Z + 3f), pilot.State.PlayerId);
        server.Tick(0.1); // one tick: the bandit is already in talking range + line of sight
    }

    // ---------------- The hold-up protocol (ground) ----------------

    [Fact]
    public void Demand_Is35PercentOfTheLargestNonToolStacks()
    {
        var server = Started("demand");
        var pilot = Robbable(server, "Mark");
        WalkUp(server, pilot);

        Assert.NotEqual(0, server.PendingBanditDemandIdForTest("Mark"));
        var items = server.PendingBanditDemandItemsForTest("Mark");
        Assert.Equal(2, items.Count); // the 1–2 largest stacks
        Assert.Equal(35, items.First(i => i.Item == "iron_ore").Count);   // ceil(100 * 0.35)
        Assert.Equal(14, items.First(i => i.Item == "copper_ore").Count); // ceil(40 * 0.35)
        Assert.DoesNotContain(items, i => i.Item == "machete"); // tools are never demanded
    }

    [Fact]
    public void Comply_HandsOverTheGoods_AndTheBanditLeavesPeacefully()
    {
        var server = Started("comply");
        var pilot = Robbable(server, "Mark");
        WalkUp(server, pilot);

        server.RespondBanditDemandForTest("Mark", comply: true);

        Assert.Equal(65, pilot.State.Inventory.CountOf("iron_ore"));
        Assert.Equal(26, pilot.State.Inventory.CountOf("copper_ore"));
        Assert.Equal(0, server.PendingBanditDemandIdForTest("Mark"));
        var bandit = Assert.Single(server.Bandits);
        Assert.False(bandit.Hostile);
        Assert.Equal(BanditPhase.Leaving, bandit.BanditPhase);
        // The stolen goods travel in its loot — killing it later would win them back.
        Assert.Contains(bandit.Loot, l => l.Item == "iron_ore" && l.Count == 35);
    }

    [Fact]
    public void Refuse_TurnsTheBanditHostile()
    {
        var server = Started("refuse");
        var pilot = Robbable(server, "Mark");
        WalkUp(server, pilot);

        server.RespondBanditDemandForTest("Mark", comply: false);

        Assert.Equal(100, pilot.State.Inventory.CountOf("iron_ore")); // nothing taken
        var bandit = Assert.Single(server.Bandits);
        Assert.True(bandit.Hostile);
        Assert.Equal(BanditPhase.Fighting, bandit.BanditPhase);
    }

    [Fact]
    public void Silence_PastTheDeadline_CountsAsRefusal()
    {
        var server = Started("silence");
        var pilot = Robbable(server, "Mark");
        WalkUp(server, pilot);
        Assert.NotEqual(0, server.PendingBanditDemandIdForTest("Mark"));

        for (int i = 0; i < 27; i++)
        {
            server.Tick(1.0); // ride out the 25 s ultimatum without answering
        }

        Assert.Equal(0, server.PendingBanditDemandIdForTest("Mark"));
        var bandit = Assert.Single(server.Bandits);
        Assert.True(bandit.Hostile);
    }

    [Fact]
    public void EmptyPockets_AreNotWorthRobbing()
    {
        var server = Started("broke");
        var pilot = server.AddLocalPlayer("Mark");
        pilot.State.AboardShip = false;
        var inv = pilot.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, null);
        }

        inv.SetSlot(0, new ItemStack("machete", 1)); // only a tool — exempt, so effectively empty
        WalkUp(server, pilot);

        Assert.Equal(0, server.PendingBanditDemandIdForTest("Mark"));
        var bandit = Assert.Single(server.Bandits);
        Assert.False(bandit.Hostile);
        Assert.Equal(BanditPhase.Leaving, bandit.BanditPhase);
    }

    [Fact]
    public void AttackingTheRobber_CountsAsRefusing()
    {
        var server = Started("preempt");
        var pilot = Robbable(server, "Mark");
        WalkUp(server, pilot);
        var bandit = Assert.Single(server.Bandits);

        server.AttackEntity("Mark", bandit.Id); // bare fists — it survives, but the talk is over

        Assert.True(bandit.Hostile);
        Assert.Equal(BanditPhase.Fighting, bandit.BanditPhase);
        Assert.Equal(0, server.PendingBanditDemandIdForTest("Mark")); // the demand UI was closed
        Assert.Equal(100, pilot.State.Inventory.CountOf("iron_ore"));
    }

    [Fact]
    public void KillingABandit_DropsItsLootToTheKiller()
    {
        var server = Started("loot");
        var pilot = Robbable(server, "Mark");
        WalkUp(server, pilot);
        var bandit = Assert.Single(server.Bandits);

        int plateBefore = pilot.State.Inventory.CountOf("iron_plate");
        for (int i = 0; i < 10 && server.Bandits.Count > 0; i++)
        {
            server.AttackEntity("Mark", bandit.Id);
        }

        Assert.Empty(server.Bandits);
        Assert.True(pilot.State.Inventory.CountOf("iron_plate") >= plateBefore + 2, "the robber's loot lands in the killer's pockets");
    }

    // ---------------- Camp guards + persisted cleared state ----------------

    [Fact]
    public void ClearingACamp_IsPersistedForever()
    {
        var server = Started("camp");
        var pilot = Robbable(server, "Raider");
        string key = server.SpawnBanditCampForTest(new Vector3f(
            pilot.State.Position.X + 20f, pilot.State.Position.Y, pilot.State.Position.Z), guards: 2);

        Assert.Equal(2, server.Bandits.Count);
        Assert.False(server.BanditCampClearedForTest(key));

        foreach (var guard in server.Bandits.ToList())
        {
            pilot.State.Position = guard.Position; // step up to each guard and put it down
            for (int i = 0; i < 10 && server.Bandits.Contains(guard); i++)
            {
                server.AttackEntity("Raider", guard.Id);
            }
        }

        Assert.Empty(server.Bandits);
        Assert.True(server.BanditCampClearedForTest(key));
        Assert.True(server.FeatureStampedForTest("banditcamp:" + key + ":cleared"), "cleared camps must survive a world reload");
    }

    [Fact]
    public void CampGenerator_ProducesGuardsAndStash_Deterministically()
    {
        var a = BlocksBeyondTheStars.WorldGeneration.BanditCampGenerator.Generate(1234, "stone", _content);
        var b = BlocksBeyondTheStars.WorldGeneration.BanditCampGenerator.Generate(1234, "stone", _content);

        Assert.Equal("camp", a.Tier);
        Assert.InRange(a.Markers.Count(m => m.Type == "bandit"), 3, 4); // fire + gate + bunk guards
        Assert.Equal(2, a.Markers.Count(m => m.Type == "loot"));        // the stash huts
        Assert.DoesNotContain(a.Markers, m => m.Type == "vendor");      // bandits don't run shops

        // Same seed → identical camp (the stamp pipeline depends on this).
        Assert.Equal(a.Markers.Count, b.Markers.Count);
        bool anyBlock = false;
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                    anyBlock |= a.Get(x, y, z) != 0;
                }

        Assert.True(anyBlock, "the camp must contain actual blocks");
    }

    // ---------------- Bandit ships (space) ----------------

    private SvGameServer SpaceServer(string world, Action<GameRules>? configure = null) => Started(world, r =>
    {
        r.FreeSpaceFlight = true;
        r.SpaceCombat = SpaceCombatMode.PvE;
        r.ShipWeapons = ShipWeaponMode.NpcsOnly;
        r.SpaceNpcEnemies = AlienActivity.Off; // no drones muddying the instance
        r.AlienUfos = AlienActivity.Off;
        configure?.Invoke(r);
    });

    /// <summary>A robbable pilot sitting in their ship (EnterSpace requires being aboard).</summary>
    private static PlayerSession AboardRobbable(SvGameServer server, string name)
    {
        var pilot = Robbable(server, name);
        pilot.State.AboardShip = true;
        return pilot;
    }

    private static void FlyIntoHailRange(SvGameServer server, string playerId)
    {
        for (int i = 0; i < 40 && server.PendingBanditDemandIdForTest(playerId) == 0; i++)
        {
            server.TickBanditShipsForTest(playerId, 1.0);
        }
    }

    [Fact]
    public void BanditShip_HailsBeforeItFights()
    {
        var server = SpaceServer("hail");
        AboardRobbable(server, "Pilot");
        server.EnterSpace("Pilot");
        server.SpawnBanditShipForTest("Pilot");

        var raider = server.BanditShipForTest("Pilot");
        Assert.NotNull(raider);
        Assert.False(raider!.Hostile); // it talks first

        FlyIntoHailRange(server, "Pilot");
        Assert.NotEqual(0, server.PendingBanditDemandIdForTest("Pilot"));
        Assert.False(server.BanditShipForTest("Pilot")!.Hostile); // still waiting for the answer
    }

    [Fact]
    public void BanditShip_PaidOff_TakesTheGoodsAndWarpsOut()
    {
        var server = SpaceServer("paid");
        var pilot = AboardRobbable(server, "Pilot");
        server.EnterSpace("Pilot");
        server.SpawnBanditShipForTest("Pilot");
        FlyIntoHailRange(server, "Pilot");

        server.RespondBanditDemandForTest("Pilot", comply: true);

        Assert.Equal(65, pilot.State.Inventory.CountOf("iron_ore"));
        for (int i = 0; i < 40 && server.BanditShipForTest("Pilot") is not null; i++)
        {
            server.TickBanditShipsForTest("Pilot", 1.0);
        }

        Assert.Null(server.BanditShipForTest("Pilot")); // gone for good
    }

    [Fact]
    public void BanditShip_Refused_TurnsHostile()
    {
        var server = SpaceServer("fight");
        AboardRobbable(server, "Pilot");
        server.EnterSpace("Pilot");
        server.SpawnBanditShipForTest("Pilot");
        FlyIntoHailRange(server, "Pilot");

        server.RespondBanditDemandForTest("Pilot", comply: false);

        var raider = server.BanditShipForTest("Pilot");
        Assert.NotNull(raider);
        Assert.True(raider!.Hostile);
    }

    [Fact]
    public void BanditShips_NeverSpawn_WhenThePlayerCannotShootBack()
    {
        // The unkillable-UFO lesson: with ship weapons off, no ambush may ever start.
        var server = SpaceServer("gated", r => r.ShipWeapons = ShipWeaponMode.Off);
        AboardRobbable(server, "Pilot");
        server.EnterSpace("Pilot");

        for (int i = 0; i < 200; i++)
        {
            server.TickBanditShipsForTest("Pilot", 1.0); // covers every possible warp-in delay
        }

        Assert.Null(server.BanditShipForTest("Pilot"));
        Assert.Equal(0, server.PendingBanditDemandIdForTest("Pilot"));
    }

    // ---------------- Localization ----------------

    [Fact]
    public void EveryBanditLocaleKey_ExistsInBothLanguages()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");

        var required = new List<string>
        {
            "ui.bandit.title", "ui.bandit.comply", "ui.bandit.refuse", "ui.bandit.countdown",
            "ui.bandit.hint", "ui.bandit.waiting", "ui.bandit.paid", "ui.bandit.refused",
            "ui.bandit.expired", "ui.bandit.fled", "ui.worldopt.bandits",
            "vega.brief.bandits", "vega.sys.bandit_sector", "vega.sys.bandit_hail",
            "vega.sys.bandit_region", "vega.sys.bandit_camp_near",
        };
        for (int i = 1; i <= 3; i++)
        {
            required.Add("bandit.line.holdup" + i);
            required.Add("bandit.line.hail" + i);
        }

        foreach (var key in required)
        {
            Assert.True(en.ContainsKey(key), $"missing EN locale key: {key}");
            Assert.True(de.ContainsKey(key), $"missing DE locale key: {key}");
        }
    }
}
