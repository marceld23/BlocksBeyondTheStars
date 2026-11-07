// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
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
/// The robot's gear (#3398/#3399, Justus' "Robot upgrade": "a jetpack or weapons that it uses — just NO armour!"): a weapon or a
/// jetpack held while E is pressed at the own robot goes to it, a second one swaps, "take back" returns it (never into a full
/// backpack), the power-down and the knock-out crate return it too; the weapon sets the robot's damage, reach and tracer, and
/// the jetpack flies it up to an owner above it — never into blocks or a parked hull.
/// </summary>
public sealed class RobotGearTests : IDisposable
{
    private const string Ada = "Ada";
    private const string Laser = "laser_pistol";
    private const string Machete = "machete";
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_robot_gear_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string name, bool ship = false)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = ship,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        }, _content, new RecordingTransport(), repo);
        server.Start();
        return server;
    }

    /// <summary>A stone floor with ten blocks of air over it, Ada on foot in the middle with an empty backpack and her robot
    /// switched on beside her.</summary>
    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner, CombatEntity Robot) Started(string name)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        }, _content, transport, repo);
        server.Start();

        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -8; x <= 8; x++)
        {
            for (int z = -8; z <= 8; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 11; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        var owner = server.AddLocalPlayer(Ada);
        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        for (int i = 0; i < owner.State.Inventory.SlotCount; i++)
        {
            owner.State.Inventory.SetSlot(i, null);
        }

        owner.State.Inventory.Add(SvGameServer.HunterRobotItem, 1, 1);
        Assert.True(server.DeployRobotForTest(Ada));
        var robot = server.RobotEntityForTest(Ada)!;
        robot.Position = new Vector3f(Centre.X + 1.5f, Centre.Y, Centre.Z + 0.5f);
        return (server, transport, owner, robot);
    }

    private static TamedCreature RobotOf(PlayerSession owner) => owner.State.TamedCreatures.Single();

    /// <summary>Puts one <paramref name="item"/> into hotbar <paramref name="slot"/> and presses E at the robot with it.</summary>
    private static void Give(SvGameServer server, PlayerSession who, string item, int slot, string? companionId = null)
    {
        who.State.Inventory.SetSlot(slot, new ItemStack(item, 1));
        server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new GiveCompanionGearIntent
        {
            CompanionId = companionId ?? RobotOf(who).Id,
            Slot = slot,
        }));
    }

    private static void TakeBack(SvGameServer server, PlayerSession who, string piece)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new TakeCompanionGearIntent { CompanionId = RobotOf(who).Id, Piece = piece }));

    private static void FillBackpack(PlayerSession who)
    {
        for (int i = 0; i < who.State.Inventory.SlotCount; i++)
        {
            who.State.Inventory.SetSlot(i, who.State.Inventory.Slots[i] ?? new ItemStack("stone", 1024));
        }
    }

    // ---------------- give, swap, take back ----------------

    [Fact]
    public void AWeaponHeldAtTheRobot_GoesToIt_AndItShowsIt()
    {
        var (server, transport, owner, robot) = Started("gear_give");

        Give(server, owner, Laser, slot: 2);

        Assert.Null(owner.State.Inventory.Slots[2]);
        Assert.Equal(Laser, RobotOf(owner).GearWeapon);
        Assert.Equal(Laser, robot.RobotWeapon);
        Assert.Equal(Laser, server.NetCreatureForTest(robot.Id).GearWeapon); // the client draws it in its hand
        Assert.Equal(Laser, transport.To<CompanionList>(owner).Last().Companions.Single().GearWeapon); // the tab lists it
        Assert.Contains("@srv.robot.weapon_given:Robo", transport.TextsTo(owner));
    }

    [Fact]
    public void ASecondWeapon_Swaps_AndTheFirstComesBack()
    {
        var (server, _, owner, _) = Started("gear_swap");
        Give(server, owner, Laser, slot: 2);

        Give(server, owner, Machete, slot: 3);

        Assert.Equal(Machete, RobotOf(owner).GearWeapon);
        Assert.Equal(1, owner.State.Inventory.CountOf(Laser));
        Assert.Equal(0, owner.State.Inventory.CountOf(Machete));
    }

    [Fact]
    public void TakingBack_ReturnsThePiece_ButNeverIntoAFullBackpack()
    {
        var (server, transport, owner, robot) = Started("gear_take");
        Give(server, owner, Laser, slot: 2);
        Give(server, owner, RobotGear.JetpackItem, slot: 3);
        Assert.Equal(RobotGear.JetpackItem, RobotOf(owner).GearJetpack);
        Assert.True(robot.RobotJetpack);

        FillBackpack(owner);
        TakeBack(server, owner, RobotGear.WeaponPiece);
        Assert.Equal(Laser, RobotOf(owner).GearWeapon); // refused — the weapon stays with the robot
        Assert.Contains("@inventory_full", transport.To<ActionRejected>(owner).Select(r => r.Reason));

        owner.State.Inventory.SetSlot(5, null);
        TakeBack(server, owner, RobotGear.WeaponPiece);
        Assert.Equal(string.Empty, RobotOf(owner).GearWeapon);
        Assert.Equal(1, owner.State.Inventory.CountOf(Laser));
        Assert.Equal(string.Empty, robot.RobotWeapon);
        Assert.Contains("@srv.robot.gear_back:Robo", transport.TextsTo(owner));
    }

    [Fact]
    public void OnlyTheOwnRobot_TakesGear_AndOnlyAWeaponOrAJetpack()
    {
        var (server, _, owner, _) = Started("gear_refused");
        var ben = server.AddLocalPlayer("Ben");
        ben.State.AboardShip = false;
        ben.State.Position = owner.State.Position;
        string adasRobot = RobotOf(owner).Id;

        ben.State.Inventory.SetSlot(1, new ItemStack(Laser, 1));
        server.HandlePayloadForTest(ben.ConnectionId, NetCodec.Encode(new GiveCompanionGearIntent { CompanionId = adasRobot, Slot = 1 }));
        Assert.Equal(1, ben.State.Inventory.CountOf(Laser)); // Ada's robot takes nothing from Ben
        Assert.Equal(string.Empty, RobotOf(owner).GearWeapon);

        Give(server, owner, "stone", slot: 4); // not a weapon, not a jetpack — and never armour
        Assert.Equal(1, owner.State.Inventory.CountOf("stone"));
        Assert.Equal(string.Empty, RobotOf(owner).GearWeapon);
        Assert.Equal(string.Empty, RobotOf(owner).GearJetpack);
    }

    [Fact]
    public void TakingBack_WorksWhileASingleplayerWorldIsHeldPaused()
    {
        var (server, _, owner, _) = Started("gear_paused");
        Give(server, owner, Laser, slot: 2);
        server.PauseForTest(owner, true);
        Assert.True(server.IsPaused); // the Tab menu holds the world

        TakeBack(server, owner, RobotGear.WeaponPiece);

        Assert.Equal(1, owner.State.Inventory.CountOf(Laser));
    }

    // ---------------- nothing is ever lost ----------------

    [Fact]
    public void PoweringDown_ReturnsTheGear_WithTheRobot()
    {
        var (server, _, owner, _) = Started("gear_power_down");
        Give(server, owner, Laser, slot: 2);
        Give(server, owner, RobotGear.JetpackItem, slot: 3);

        server.ReleaseCompanionForTest(Ada, RobotOf(owner).Id);

        Assert.Empty(owner.State.TamedCreatures);
        Assert.Equal(1, owner.State.Inventory.CountOf(SvGameServer.HunterRobotItem));
        Assert.Equal(1, owner.State.Inventory.CountOf(Laser));
        Assert.Equal(1, owner.State.Inventory.CountOf(RobotGear.JetpackItem));
    }

    [Fact]
    public void AKnockOutCrate_HoldsTheGearToo()
    {
        var (server, _, owner, robot) = Started("gear_ko");
        Give(server, owner, RobotGear.JetpackItem, slot: 3);
        server.SpawnPlanetEnemyAtForTest(new Vector3f(robot.Position.X, Centre.Y, robot.Position.Z + 3f), damagePerSecond: 400f);

        for (int i = 0; i < 6 && server.RobotEntityForTest(Ada) is not null; i++)
        {
            server.TickRobotCompanionsForTest();
            server.AdvanceUptimeForTest(0.5);
        }

        Assert.Null(server.RobotEntityForTest(Ada));
        var crate = Assert.Single(server.Containers.Where(c => c.Owner == Ada));
        Assert.Equal(1, crate.Items.Where(s => s.Item == RobotGear.JetpackItem).Sum(s => s.Count));
        Assert.Equal(1, crate.Items.Where(s => s.Item == SvGameServer.RobotCoreItem).Sum(s => s.Count));
    }

    // ---------------- the weapon ----------------

    [Fact]
    public void TheWeapon_SetsDamageReachAndTracerColour()
    {
        var (server, _, owner, _) = Started("gear_stats");
        var bare = server.RobotWeaponStatsForTest(Ada);
        Assert.Equal((6f, 14f, 0), bare); // the sentry's shot without a weapon

        Give(server, owner, Laser, slot: 2);
        var laser = server.RobotWeaponStatsForTest(Ada);
        Assert.Equal(0.6f * 35f, laser.Damage, 3);
        Assert.Equal(RobotGear.MaxReach, laser.Reach); // 30 clamped to 20
        Assert.Equal(0xFF4A3D, laser.TracerRgb);

        Give(server, owner, Machete, slot: 3);
        var machete = server.RobotWeaponStatsForTest(Ada);
        Assert.Equal(6f, machete.Damage); // 60 % of 7 is less than its own shot
        Assert.Equal(RobotGear.MinReach, machete.Reach); // 3.5 → 4: it hits what stands close
    }

    [Fact]
    public void WithALaser_TheRobotHitsHarder_InTheLasersColour_AndWithAMachete_OnlyWhatStandsClose()
    {
        var (server, transport, owner, robot) = Started("gear_fight");
        Give(server, owner, Laser, slot: 2);
        server.SpawnPlanetEnemyAtForTest(new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 6.5f), damagePerSecond: 0f);
        var enemy = server.PlanetEnemies.Single();
        float full = enemy.Hull;

        server.TickRobotCompanionsForTest();

        Assert.Equal(full - (0.6f * 35f), enemy.Hull, 3);
        Assert.Contains(transport.Messages().OfType<SentryShot>(), s => s.TargetId == enemy.Id && s.ColorRgb == 0xFF4A3D);

        Give(server, owner, Machete, slot: 3); // the machete reaches 4 blocks — the machine stands about 6 away
        float before = enemy.Hull;
        server.AdvanceUptimeForTest(1.0);
        server.TickRobotCompanionsForTest();
        Assert.Equal(before, enemy.Hull);
        Assert.Equal(robot.HullMax, robot.Hull);
    }

    // ---------------- the jetpack ----------------

    /// <summary>A 3×3 stone platform whose top is six blocks over the floor, three blocks east of Ada; she stands on it.</summary>
    private static Vector3f OwnerOnPlatform(SvGameServer server, PlayerSession owner, GameContent content)
    {
        var stone = content.GetBlock("stone")!.NumericId;
        for (int x = 3; x <= 5; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + 5, Centre.Z + z), stone);
            }
        }

        owner.State.Position = new Vector3f(Centre.X + 4.5f, Centre.Y + 6, Centre.Z + 0.5f);
        return owner.State.Position;
    }

    private static bool InsideBlocks(SvGameServer server, Vector3f p)
    {
        var feet = new Vector3i((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z));
        return server.World.GetBlock(feet) != BlockId.Air || server.World.GetBlock(new Vector3i(feet.X, feet.Y + 1, feet.Z)) != BlockId.Air;
    }

    [Fact]
    public void WithoutAJetpack_TheRobotStaysDown()
    {
        var (server, _, owner, robot) = Started("gear_nojet");
        OwnerOnPlatform(server, owner, _content);

        for (int i = 0; i < 50; i++)
        {
            server.MoveCompanionsForTest(0.1);
        }

        Assert.False(robot.RobotFlying);
        Assert.True(robot.Position.Y < Centre.Y + 2f, $"without a jetpack it cannot get up there (at {robot.Position})");
    }

    /// <summary>The spot beside the owner is picked from a bearing seeded by the companion's id — each id here flies to another
    /// side of the platform (over its edge, along it, past its corner).</summary>
    [Theory]
    [InlineData("robot_a")]
    [InlineData("robot_b")]
    [InlineData("robot_c")]
    [InlineData("robot_d")]
    [InlineData("robot_e")]
    [InlineData("robot_f")]
    public void WithAJetpack_TheRobotFliesUpToItsOwner_WithinSeconds_AndNeverThroughBlocks(string companionId)
    {
        var (server, _, owner, robot) = Started("gear_jet_" + companionId);
        RobotOf(owner).Id = companionId; // the record and its body keep matching — only the seed of its bearing changes
        robot.CompanionId = companionId;
        Give(server, owner, RobotGear.JetpackItem, slot: 3);
        var up = OwnerOnPlatform(server, owner, _content);
        bool flameOnTheWire = false;
        var track = new List<string>();

        for (int i = 0; i < 50; i++)
        {
            server.MoveCompanionsForTest(0.1);
            track.Add($"{i}:{robot.Position}{(robot.RobotFlying ? "F" : string.Empty)}");
            flameOnTheWire |= (server.NetCreatureForTest(robot.Id).GearFlags & RobotGear.FlagFlying) != 0;
            Assert.False(InsideBlocks(server, robot.Position), $"step {i}: the robot is inside a block at {robot.Position}");
        }

        Assert.True(flameOnTheWire, "the client should have been told it flies (the flame)");
        Assert.True(robot.Position.Y >= up.Y - 0.5f,
            $"it should be up with its owner (at {robot.Position}, owner at {up}); its track: {string.Join(" ", track)}");
        float dx = robot.Position.X - up.X, dz = robot.Position.Z - up.Z;
        Assert.True(dx * dx + dz * dz <= 3f * 3f, $"beside its owner (at {robot.Position})");
        Assert.False(robot.RobotFlying); // landed on the platform, it walks again
        Assert.Equal(RobotGear.FlagJetpack, server.NetCreatureForTest(robot.Id).GearFlags); // wears it; the flame is out
    }

    [Fact]
    public void WithAJetpack_TheRobotNeverFliesIntoAParkedHull()
    {
        var server = NewServer("gear_hull", ship: true);
        var owner = server.AddLocalPlayer(Ada);
        var (origin, size) = server.LandedShipBoundsForTest(Ada);
        owner.State.AboardShip = false;
        var roof = new Vector3f(origin.X + (size.X * 0.5f), origin.Y + size.Y, origin.Z + (size.Z * 0.5f));
        owner.State.Position = roof;
        owner.State.TamedCreatures.Add(new TamedCreature
        {
            Id = "robo",
            Kind = TamedCreature.RobotKind,
            Roams = true,
            Following = true,
            SpeciesId = SvGameServer.RobotSpeciesId,
            Species = SvGameServer.RobotSpecies(),
            Name = "Robo",
            Bond = 100,
            SizeScale = 1f,
            GearJetpack = RobotGear.JetpackItem,
        });
        server.ReconcileCompanionsForTest(); // it appears beside the ship
        var robot = Assert.Single(server.CompanionEntitiesForTest(Ada));
        Assert.True(robot.RobotJetpack);
        float startY = robot.Position.Y;
        float highest = startY;

        for (int i = 0; i < 80; i++)
        {
            server.MoveCompanionsForTest(0.1);
            highest = Math.Max(highest, robot.Position.Y);
            Assert.False(robot.RobotFlying && server.EntityBlockedByShipForTest(robot.Position), $"step {i}: inside the hull at {robot.Position}");
        }

        Assert.True(highest > startY + 2f, $"it should have flown up towards the roof (start {startY}, highest {highest})");
    }

    // ---------------- the wire ----------------

    [Fact]
    public void TheGearIntentsAndFields_RoundTripOnBothPaths()
    {
        var give = new GiveCompanionGearIntent { CompanionId = "tc", Slot = 4 };
        var take = new TakeCompanionGearIntent { CompanionId = "tc", Piece = RobotGear.JetpackPiece };
        foreach (var payload in new[] { NetCodec.Encode(give), NetCodec.EncodeJson(give) })
        {
            Assert.Equal(4, Assert.IsType<GiveCompanionGearIntent>(NetCodec.Decode(payload)).Slot);
        }

        foreach (var payload in new[] { NetCodec.Encode(take), NetCodec.EncodeJson(take) })
        {
            Assert.Equal(RobotGear.JetpackPiece, Assert.IsType<TakeCompanionGearIntent>(NetCodec.Decode(payload)).Piece);
        }

        var list = Assert.IsType<CreatureList>(NetCodec.Decode(NetCodec.Encode(new CreatureList
        {
            Creatures = new[] { new NetCreature { GearWeapon = Laser, GearFlags = RobotGear.FlagJetpack | RobotGear.FlagFlying } },
        })));
        var creature = Assert.Single(list.Creatures);
        Assert.Equal(Laser, creature.GearWeapon);
        Assert.Equal(3, creature.GearFlags);
        var shot = Assert.IsType<SentryShot>(NetCodec.Decode(NetCodec.Encode(new SentryShot { ColorRgb = 0xFF4A3D })));
        Assert.Equal(0xFF4A3D, shot.ColorRgb);
        var roster = Assert.IsType<CompanionList>(NetCodec.Decode(NetCodec.Encode(new CompanionList
        {
            Companions = new[] { new NetCompanion { GearWeapon = Laser, GearJetpack = RobotGear.JetpackItem } },
        })));
        Assert.Equal(RobotGear.JetpackItem, roster.Companions[0].GearJetpack);
        Assert.Equal(333, (int)NetCodec.RegisteredMessageTags[typeof(GiveCompanionGearIntent)]);
        Assert.Equal(334, (int)NetCodec.RegisteredMessageTags[typeof(TakeCompanionGearIntent)]);
    }
}
