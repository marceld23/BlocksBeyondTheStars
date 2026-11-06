// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The friendly hunter robot (#3295): crafted from the finale's robot core, switched on as a gadget, a companion that roams
/// to every world with a fixed bond, never eats and cannot be scanned; it shoots hostile machines (never players), and a
/// knock-out leaves an owner-locked crate with its parts — one craft rebuilds it.
/// </summary>
public sealed class RobotCompanionTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_robot_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner) Started(string name, Action<ServerConfig>? configure = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();

        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -6; x <= 6; x++)
        {
            for (int z = -6; z <= 6; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 3; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        var owner = server.AddLocalPlayer("Ada");
        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        for (int i = 0; i < owner.State.Inventory.SlotCount; i++)
        {
            owner.State.Inventory.SetSlot(i, null);
        }

        owner.State.Inventory.Add(SvGameServer.HunterRobotItem, 1, 1);
        return (server, transport, owner);
    }

    /// <summary>Switches the robot on and stands it beside its owner (the spawn ring's ground probe is not under test).</summary>
    private static CombatEntity Deployed(SvGameServer server, PlayerSession owner)
    {
        Assert.True(server.DeployRobotForTest("Ada"));
        var robot = server.RobotEntityForTest("Ada")!;
        robot.Position = new Vector3f(Centre.X + 1.5f, Centre.Y, Centre.Z + 0.5f);
        return robot;
    }

    private static List<string> Rejections(RecordingTransport t, PlayerSession who)
        => t.To<ActionRejected>(who).Select(r => r.Reason).ToList();

    // ---------------- content ----------------

    [Fact]
    public void TheRecipe_BuildsTheRobotFromTheCoreAndTitaniumOre_AtTheWorkshop()
    {
        var recipe = _content.Recipes.Values.Single(r => r.Outputs.Any(o => o.Item == SvGameServer.HunterRobotItem));
        Assert.Equal(CraftingStation.Workshop, recipe.Station);
        Assert.Contains(recipe.Inputs, i => i.Item == SvGameServer.RobotCoreItem && i.Count == 1);
        Assert.Contains(recipe.Inputs, i => i.Item == "titanium_ore" && i.Count == 8);
        Assert.Equal(1, _content.GetItem(SvGameServer.HunterRobotItem)!.MaxStack);
        Assert.Equal(ToolKind.Gadget, _content.GetItem(SvGameServer.HunterRobotItem)!.Tool!.Kind);
        Assert.Equal(ItemCategory.Component, _content.GetItem(SvGameServer.RobotCoreItem)!.Category);
    }

    // ---------------- switch on / off ----------------

    [Fact]
    public void UsingTheItem_SwitchesTheRobotOn_AsARoamingCompanionWithAFullBond()
    {
        var (server, transport, owner) = Started("robot_deploy");

        var robot = Deployed(server, owner);

        Assert.Equal(0, owner.State.Inventory.CountOf(SvGameServer.HunterRobotItem));
        var tc = Assert.Single(owner.State.TamedCreatures);
        Assert.Equal(TamedCreature.RobotKind, tc.Kind);
        Assert.True(tc.Roams);
        Assert.Equal(100, tc.Bond);
        Assert.Equal(CreatureBodyPlan.Robot, tc.Species.BodyPlan);
        Assert.Equal("Ada", robot.OwnerId);
        Assert.Contains("@srv.robot.deployed:Robo", transport.TextsTo(owner));

        var wire = server.NetCreatureForTest(robot.Id); // the client draws it from the plan alone — no new message
        Assert.Equal("Robot", wire.BodyPlan);
        Assert.Equal("Ada", wire.OwnerId);
        Assert.False(wire.Hostile);
    }

    [Fact]
    public void ASecondRobot_IsRefused_AndTheItemIsKept()
    {
        var (server, transport, owner) = Started("robot_second");
        Deployed(server, owner);
        owner.State.Inventory.Add(SvGameServer.HunterRobotItem, 1, 1);

        Assert.False(server.DeployRobotForTest("Ada"));

        Assert.Equal(1, owner.State.Inventory.CountOf(SvGameServer.HunterRobotItem));
        Assert.Single(owner.State.TamedCreatures);
        Assert.Contains("@srv.robot.already_active", Rejections(transport, owner));
    }

    [Fact]
    public void PoweringDown_PutsTheItemBack()
    {
        var (server, transport, owner) = Started("robot_off");
        Deployed(server, owner);
        string id = owner.State.TamedCreatures.Single().Id;

        server.ReleaseCompanionForTest("Ada", id);

        Assert.Equal(1, owner.State.Inventory.CountOf(SvGameServer.HunterRobotItem));
        Assert.Empty(owner.State.TamedCreatures);
        Assert.Null(server.RobotEntityForTest("Ada"));
        Assert.Contains("@srv.robot.powered_down:Robo", transport.TextsTo(owner));
    }

    // ---------------- a companion, but a robot ----------------

    [Fact]
    public void TheRobot_FollowsItsOwnerToAnyWorld_WhereAnAnimalStaysAtHome()
    {
        var (server, _, owner) = Started("robot_roams");
        Deployed(server, owner);
        var tc = owner.State.TamedCreatures.Single();
        tc.HomeBodyId = "some-other-body"; // switched on elsewhere

        server.ReconcileCompanionsForTest();

        Assert.NotNull(server.RobotEntityForTest("Ada")); // it came along
        var pet = new TamedCreature
        {
            Id = "pet",
            HomeBodyId = "some-other-body",
            Name = "Flöckchen",
            SpeciesId = server.SpeciesRoster.First().Id,
            Species = server.SpeciesRoster.First(),
            SizeScale = 1f,
            Bond = 50,
        };
        owner.State.TamedCreatures.Add(pet);
        server.ReconcileCompanionsForTest();
        Assert.DoesNotContain(server.CompanionEntitiesForTest("Ada"), c => c.CompanionId == "pet");
    }

    [Fact]
    public void TheRobot_DoesNotEat_AndCannotBeScanned()
    {
        var (server, transport, owner) = Started("robot_food");
        Deployed(server, owner);
        owner.State.Inventory.Add("forage_bait", 3, 99);
        var tc = owner.State.TamedCreatures.Single();

        server.FeedCompanionForTest("Ada", tc.Id);

        Assert.Equal(3, owner.State.Inventory.CountOf("forage_bait"));
        Assert.Equal(100, tc.Bond);
        Assert.Contains("@srv.robot.no_food:Robo", Rejections(transport, owner));

        var scan = server.ScanSubject("Ada", "creature", SvGameServer.RobotSpeciesId);
        Assert.Equal("ui.scan.robot", scan.InfoKey);
    }

    // ---------------- it fights gently ----------------

    [Fact]
    public void TheRobot_ShootsAHostileMachine_ButNeverItsOwner()
    {
        var (server, transport, owner) = Started("robot_fight");
        var robot = Deployed(server, owner);
        server.SpawnPlanetEnemyAtForTest(new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 6.5f), damagePerSecond: 0f);
        var enemy = server.PlanetEnemies.Single();
        float full = enemy.Hull;

        server.TickRobotCompanionsForTest();

        Assert.True(enemy.Hull < full, "the robot should have hit the machine");
        Assert.Contains(transport.Messages().OfType<SentryShot>(), s => s.TargetId == enemy.Id);
        Assert.DoesNotContain(transport.Messages().OfType<SentryShot>(), s => s.TargetId == owner.State.PlayerId);
        Assert.Equal(100f, owner.State.Health);
        Assert.Equal(robot.HullMax, robot.Hull); // a harmless machine never hurts it

        for (int i = 0; i < 20 && server.PlanetEnemies.Count > 0; i++)
        {
            server.TickRobotCompanionsForTest();
        }

        Assert.Empty(server.PlanetEnemies); // finished, credited to its owner like a sentry kill
    }

    [Fact]
    public void OnAPeacefulWorld_TheRobotHoldsItsFire()
    {
        var (server, transport, owner) = Started("robot_peaceful", c => c.Rules.PlanetEnemies = AlienActivity.Off);
        Deployed(server, owner);
        server.SpawnPlanetEnemyAtForTest(new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 6.5f));

        server.TickRobotCompanionsForTest();

        Assert.Empty(transport.Messages().OfType<SentryShot>());
    }

    // ---------------- knock-out ----------------

    [Fact]
    public void AKnockOut_LeavesAnOwnerLockedCrateWithTheCoreAndTheParts()
    {
        var (server, transport, owner) = Started("robot_ko");
        var robot = Deployed(server, owner);
        var ben = server.AddLocalPlayer("Ben");
        ben.State.AboardShip = false;
        ben.State.Position = owner.State.Position;
        server.SpawnPlanetEnemyAtForTest(new Vector3f(robot.Position.X, Centre.Y, robot.Position.Z + 3f), damagePerSecond: 400f);

        for (int i = 0; i < 6 && server.RobotEntityForTest("Ada") is not null; i++)
        {
            server.TickRobotCompanionsForTest();
            server.AdvanceUptimeForTest(0.5);
        }

        Assert.Null(server.RobotEntityForTest("Ada")); // knocked out in the fight it started
        Assert.Contains(transport.Messages().OfType<CreatureDefeated>(), d => d.Id == robot.Id); // the sparks
        var crate = Assert.Single(server.Containers.Where(c => c.Owner == "Ada"));
        Assert.Equal(1, crate.Items.Where(s => s.Item == SvGameServer.RobotCoreItem).Sum(s => s.Count));
        Assert.Equal(8, crate.Items.Where(s => s.Item == "titanium_ore").Sum(s => s.Count));
        Assert.Empty(owner.State.TamedCreatures); // the crate is out — the record is settled

        server.LootContainer("Ben", crate.Id);
        Assert.Equal(0, ben.State.Inventory.CountOf(SvGameServer.RobotCoreItem));
        server.LootContainer("Ada", crate.Id);
        Assert.Equal(1, owner.State.Inventory.CountOf(SvGameServer.RobotCoreItem));
    }

    [Fact]
    public void AHurtRobot_HealsAfterTenQuietSeconds()
    {
        var (server, _, owner) = Started("robot_heal");
        var robot = Deployed(server, owner);
        server.TickRobotCompanionsForTest(); // starts its clock
        robot.Hull = robot.HullMax * 0.5f;
        robot.RobotLastFightAt = 0;

        server.AdvanceUptimeForTest(SvGameServer.RobotHealAfterSeconds + 1);
        server.TickRobotCompanionsForTest();

        Assert.Equal(robot.HullMax, robot.Hull);
    }
}
