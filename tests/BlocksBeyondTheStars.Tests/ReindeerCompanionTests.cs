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
/// Rudolpho, the alien reindeer with the lantern nose (#3322): the star bell from door 24 of the Advent calendar calls him —
/// one per player, a roaming companion outside the pet cap with a fixed bond, who never eats and never fights. "Send home"
/// turns him back into the bell. He shares the authored-companion path with the hunter robot (#3295), and his record keeps
/// its kind across a save (#3314).
/// </summary>
public sealed class ReindeerCompanionTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_reindeer_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Server(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
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

        return server;
    }

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner) Started(string name)
    {
        var transport = new RecordingTransport();
        var server = Server(name, transport);
        var owner = server.AddLocalPlayer("Ada");
        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        for (int i = 0; i < owner.State.Inventory.SlotCount; i++)
        {
            owner.State.Inventory.SetSlot(i, null);
        }

        owner.State.Inventory.Add(SvGameServer.StarBellItem, 1, 1);
        return (server, transport, owner);
    }

    private static List<string> Rejections(RecordingTransport t, PlayerSession who)
        => t.To<ActionRejected>(who).Select(r => r.Reason).ToList();

    [Fact]
    public void TheStarBell_IsAGadgetThatStacksToOne()
    {
        var bell = _content.GetItem(SvGameServer.StarBellItem)!;
        Assert.Equal(ToolKind.Gadget, bell.Tool!.Kind);
        Assert.Equal(1, bell.MaxStack);
        Assert.Equal("item.star_bell.name", bell.NameKey);
    }

    [Fact]
    public void RingingTheBell_CallsRudolpho_ARoamingCompanionWithAFullBond()
    {
        var (server, transport, owner) = Started("reindeer_deploy");

        Assert.True(server.DeployReindeerForTest("Ada"));

        Assert.Equal(0, owner.State.Inventory.CountOf(SvGameServer.StarBellItem));
        var tc = Assert.Single(owner.State.TamedCreatures);
        Assert.Equal(TamedCreature.ReindeerKind, tc.Kind);
        Assert.Equal("Rudolpho", tc.Name);
        Assert.True(tc.Roams);
        Assert.Equal(100, tc.Bond);
        Assert.Equal(CreatureBodyPlan.Reindeer, tc.Species.BodyPlan);
        Assert.True(tc.Species.Glows);
        Assert.False(tc.Species.Hostile);
        Assert.Contains("@srv.reindeer.deployed:Rudolpho", transport.TextsTo(owner));

        var rudolpho = server.ReindeerEntityForTest("Ada")!;
        var wire = server.NetCreatureForTest(rudolpho.Id); // the client draws antlers and nose from the plan alone
        Assert.Equal("Reindeer", wire.BodyPlan);
        Assert.Equal("Ada", wire.OwnerId);
        Assert.False(wire.Hostile);
    }

    [Fact]
    public void ASecondBell_IsRefused_AndKept_ButTheRobotMayWalkBesideHim()
    {
        var (server, transport, owner) = Started("reindeer_second");
        Assert.True(server.DeployReindeerForTest("Ada"));
        owner.State.Inventory.Add(SvGameServer.StarBellItem, 1, 1);

        Assert.False(server.DeployReindeerForTest("Ada"));

        Assert.Equal(1, owner.State.Inventory.CountOf(SvGameServer.StarBellItem));
        Assert.Single(owner.State.TamedCreatures);
        Assert.Contains("@srv.reindeer.already_active", Rejections(transport, owner));

        owner.State.Inventory.Add(SvGameServer.HunterRobotItem, 1, 1); // one of each kind
        Assert.True(server.DeployRobotForTest("Ada"));
        Assert.Equal(2, owner.State.TamedCreatures.Count);
    }

    [Fact]
    public void SendingHimHome_GivesTheBellBack()
    {
        var (server, transport, owner) = Started("reindeer_home");
        Assert.True(server.DeployReindeerForTest("Ada"));
        string id = owner.State.TamedCreatures.Single().Id;

        server.ReleaseCompanionForTest("Ada", id);

        Assert.Equal(1, owner.State.Inventory.CountOf(SvGameServer.StarBellItem));
        Assert.Empty(owner.State.TamedCreatures);
        Assert.Null(server.ReindeerEntityForTest("Ada"));
        Assert.Contains("@srv.reindeer.sent_home:Rudolpho", transport.TextsTo(owner));
    }

    [Fact]
    public void Rudolpho_TakesNoPetsPlace_BesideAFullSetOfSix()
    {
        var (server, _, owner) = Started("reindeer_cap");
        var species = server.SpeciesRoster.First();
        for (int i = 0; i < 6; i++)
        {
            owner.State.TamedCreatures.Add(new TamedCreature
            {
                Id = "pet" + i,
                HomeBodyId = server.ActiveLocationId,
                Name = "Pet " + i,
                SpeciesId = species.Id,
                Species = species,
                SizeScale = 1f,
                Bond = 50,
            });
        }

        server.ReconcileCompanionsForTest();
        Assert.Equal(6, server.CompanionEntitiesForTest("Ada").Count);

        Assert.True(server.DeployReindeerForTest("Ada"));
        server.ReconcileCompanionsForTest();

        Assert.Equal(7, server.CompanionEntitiesForTest("Ada").Count); // the six pets and Rudolpho beside them
        Assert.NotNull(server.ReindeerEntityForTest("Ada"));
    }

    [Fact]
    public void Rudolpho_FollowsHisFriendToAnyWorld()
    {
        var (server, _, owner) = Started("reindeer_roams");
        Assert.True(server.DeployReindeerForTest("Ada"));
        owner.State.TamedCreatures.Single().HomeBodyId = "some-other-body"; // called elsewhere

        server.ReconcileCompanionsForTest();

        Assert.NotNull(server.ReindeerEntityForTest("Ada"));
    }

    [Fact]
    public void Rudolpho_NeverEats_IsNotAResearchSubject_AndNeverFights()
    {
        var (server, transport, owner) = Started("reindeer_gentle");
        Assert.True(server.DeployReindeerForTest("Ada"));
        var rudolpho = server.ReindeerEntityForTest("Ada")!;
        rudolpho.Position = new Vector3f(Centre.X + 1.5f, Centre.Y, Centre.Z + 0.5f);
        owner.State.Inventory.Add("forage_bait", 3, 99);
        var tc = owner.State.TamedCreatures.Single();

        server.FeedCompanionForTest("Ada", tc.Id);
        Assert.Equal(3, owner.State.Inventory.CountOf("forage_bait"));
        Assert.Equal(100, tc.Bond);
        Assert.Contains("@srv.reindeer.no_food:Rudolpho", Rejections(transport, owner));

        Assert.Equal("ui.scan.reindeer", server.ScanSubject("Ada", "creature", SvGameServer.ReindeerSpeciesId).InfoKey);

        server.SpawnPlanetEnemyAtForTest(new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 6.5f), damagePerSecond: 0f);
        var enemy = server.PlanetEnemies.Single();
        float full = enemy.Hull;
        for (int i = 0; i < 4; i++)
        {
            server.TickRobotCompanionsForTest();
            server.AdvanceUptimeForTest(0.5);
        }

        Assert.Equal(full, enemy.Hull);
        Assert.Empty(transport.Messages().OfType<SentryShot>());
    }

    /// <summary>#3314 through the real save: Rudolpho stays a roaming reindeer after a restart, his bond never fades on the
    /// join that charges the days away, and he appears on the join world at once.</summary>
    [Fact]
    public void AfterARestart_HeIsStillRudolpho_WithHisFullBond_BesideHisFriend()
    {
        var (server, _, owner) = Started("reindeer_restart");
        Assert.True(server.DeployReindeerForTest("Ada"));
        var before = owner.State.TamedCreatures.Single();
        before.LastFedUtc = 1;
        before.TamedAtUtc = 1; // "never fed" for half a century — an animal's bond would sink to the floor
        before.HomeBodyId = "some-other-body";
        server.Stop();
        _repos[0].Dispose();
        _repos.Clear();
        SqliteConnection.ClearAllPools();

        var transport = new RecordingTransport();
        var again = Server("reindeer_restart", transport);
        again.HandlePayloadForTest(91, NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Ada" }));

        var tc = Assert.Single(again.Sessions[91].State.TamedCreatures);
        Assert.Equal(TamedCreature.ReindeerKind, tc.Kind);
        Assert.True(tc.Roams);
        Assert.Equal(100, tc.Bond);
        Assert.Equal(CreatureBodyPlan.Reindeer, tc.Species.BodyPlan);
        Assert.NotNull(again.ReindeerEntityForTest("Ada"));
    }
}
