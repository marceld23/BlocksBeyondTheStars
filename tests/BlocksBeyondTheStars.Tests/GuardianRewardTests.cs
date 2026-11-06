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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The reward for the end (#3294): winning the finale puts every player of the world — online and offline — on a roster;
/// each gets ONE owner-locked crate with a robot core in front of them the next time they stand on foot on a loaded world.
/// Nobody who first joins after the win gets one; a blocked spot hints once and, after a minute, hands the core over into
/// the backpack — never lost; a second win gives nothing more.
/// </summary>
public sealed class GuardianRewardTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_reward_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer Started(string name, RecordingTransport transport, bool starterShip = false)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = starterShip,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A stone floor around <see cref="Centre"/> with two cells of air above it, high in the sky — open ground
    /// nothing else claims.</summary>
    private void Floor(SvGameServer server, int radius = 4)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -radius; x <= radius; x++)
        {
            for (int z = -radius; z <= radius; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y, Centre.Z + z), BlockId.Air);
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + 1, Centre.Z + z), BlockId.Air);
            }
        }
    }

    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        StandOnTheFloor(p);
        return p;
    }

    private static void StandOnTheFloor(PlayerSession p)
    {
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        p.State.Yaw = 0f; // facing +Z
    }

    private static List<StoredContainer> CratesOf(SvGameServer server, string owner)
        => server.Containers.Where(c => c.Owner == owner).ToList();

    [Fact]
    public void APlayerOnFoot_GetsOneLockedCrateWithARobotCore_InFrontOfThem_Once()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_once", transport);
        Floor(server);
        var ada = OnFoot(server, "Ada");

        server.MarkGuardianDefeatedForTest();
        Assert.Contains("Ada", server.GuardianRewardRosterForTest);
        server.TickOwnerCratesForTest();

        var crate = Assert.Single(CratesOf(server, "Ada"));
        Assert.Equal("crate", crate.Kind);
        Assert.Equal(new[] { ("robot_core", 1) }, crate.Items.Select(s => (s.Item, s.Count)));
        Assert.Equal(_content.GetBlock("crate")!.NumericId, server.World.GetBlock(crate.Position));
        Assert.Equal(Centre.Y, crate.Position.Y); // on the floor
        Assert.InRange(Math.Abs(crate.Position.X - Centre.X) + Math.Abs(crate.Position.Z - Centre.Z), 2, 6);
        Assert.True(crate.Position.Z > Centre.Z); // in front of her
        Assert.True(server.HasStoryMilestoneForTest("Ada", SvGameServer.GuardianRewardMilestone));
        Assert.DoesNotContain("Ada", server.GuardianRewardRosterForTest);

        server.TickOwnerCratesForTest();
        Assert.Single(CratesOf(server, "Ada")); // once per player
        Assert.Equal(0, ada.State.Inventory.CountOf("robot_core"));
    }

    [Fact]
    public void AnOfflinePlayer_GetsItOnTheNextJoin_ButSomeoneWhoJoinsAfterTheWin_DoesNot()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_offline", transport);
        Floor(server);
        OnFoot(server, "Ada"); // keeps the world loaded
        server.AddLocalPlayer("Ben");
        server.DisconnectLocalPlayerForTest("Ben"); // saved, offline at the win

        server.MarkGuardianDefeatedForTest();
        Assert.Contains("Ben", server.GuardianRewardRosterForTest);
        server.TickOwnerCratesForTest();
        Assert.Empty(CratesOf(server, "Ben")); // nobody to hand it to yet

        var ben = server.AddLocalPlayer("Ben");
        StandOnTheFloor(ben);
        ben.State.Position = new Vector3f(Centre.X - 2.5f, Centre.Y, Centre.Z - 2.5f); // not on Ada's spot
        var cleo = OnFoot(server, "Cleo"); // first joins after the win
        cleo.State.Position = new Vector3f(Centre.X + 2.5f, Centre.Y, Centre.Z - 2.5f);
        server.TickOwnerCratesForTest();

        Assert.Single(CratesOf(server, "Ben"));
        Assert.Empty(CratesOf(server, "Cleo"));
        Assert.DoesNotContain("Cleo", server.GuardianRewardRosterForTest);
    }

    [Fact]
    public void APlayerNotOnFoot_Waits_AndGetsItOnceTheyStepOut()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_wait", transport);
        Floor(server);
        var ada = OnFoot(server, "Ada");
        ada.State.InEva = true; // out on a spacewalk — not on foot

        server.MarkGuardianDefeatedForTest();
        server.TickOwnerCratesForTest();
        Assert.Empty(CratesOf(server, "Ada"));
        Assert.Contains("Ada", server.GuardianRewardRosterForTest);

        ada.State.InEva = false;
        server.TickOwnerCratesForTest();
        Assert.Single(CratesOf(server, "Ada"));
    }

    [Fact]
    public void APlayerAboardTheirParkedShip_GetsItAfterSteppingOut()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_aboard", transport, starterShip: true);
        var ada = server.AddLocalPlayer("Ada");
        Assert.True(ada.State.AboardShip);
        Floor(server);

        server.MarkGuardianDefeatedForTest();
        server.TickOwnerCratesForTest();
        Assert.Empty(CratesOf(server, "Ada"));

        StandOnTheFloor(ada);
        server.TickOwnerCratesForTest();
        Assert.Single(CratesOf(server, "Ada"));
    }

    [Fact]
    public void NoFreeSpot_HintsOnce_ThenTheCoreGoesIntoTheBackpack_AfterAMinute()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_blocked", transport);
        var ada = server.AddLocalPlayer("Ada");
        ada.State.AboardShip = false;
        ada.State.Position = new Vector3f(Centre.X + 0.5f, 240f, Centre.Z + 0.5f); // high in the open sky: no ground in reach
        for (int i = 0; i < ada.State.Inventory.SlotCount; i++)
        {
            ada.State.Inventory.SetSlot(i, null);
        }

        server.MarkGuardianDefeatedForTest();
        server.TickOwnerCratesForTest();
        server.TickOwnerCratesForTest();
        Assert.Empty(CratesOf(server, "Ada"));
        Assert.Single(transport.LineKeys(ada), k => k == "vega.owner_crate.no_spot"); // one hint, not one per try
        Assert.Equal(0, ada.State.Inventory.CountOf("robot_core"));

        server.AdvanceUptimeForTest(SvGameServer.OwnerCrateBackpackAfterSeconds + 1);
        server.TickOwnerCratesForTest();
        Assert.Equal(1, ada.State.Inventory.CountOf("robot_core"));
        Assert.Contains("@srv.owner_crate.into_backpack", transport.TextsTo(ada));
        Assert.True(server.HasStoryMilestoneForTest("Ada", SvGameServer.GuardianRewardMilestone));
        Assert.DoesNotContain("Ada", server.GuardianRewardRosterForTest);
    }

    [Fact]
    public void AFullBackpack_KeepsItWaiting_NeverLost()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_full", transport);
        var ada = server.AddLocalPlayer("Ada");
        ada.State.AboardShip = false;
        ada.State.Position = new Vector3f(Centre.X + 0.5f, 240f, Centre.Z + 0.5f);
        for (int i = 0; i < ada.State.Inventory.SlotCount; i++)
        {
            ada.State.Inventory.SetSlot(i, new ItemStack("stone", 1024));
        }

        server.MarkGuardianDefeatedForTest();
        server.TickOwnerCratesForTest();
        server.AdvanceUptimeForTest(SvGameServer.OwnerCrateBackpackAfterSeconds + 1);
        server.TickOwnerCratesForTest();

        Assert.Equal(0, ada.State.Inventory.CountOf("robot_core"));
        Assert.Contains("Ada", server.GuardianRewardRosterForTest); // still owed — the next free spot or slot takes it
        Assert.False(server.HasStoryMilestoneForTest("Ada", SvGameServer.GuardianRewardMilestone));
    }

    [Fact]
    public void AStorySwitch_AndASecondWin_GiveNothingMore()
    {
        var transport = new RecordingTransport();
        var server = Started("reward_second", transport);
        Floor(server);
        OnFoot(server, "Ada");
        server.MarkGuardianDefeatedForTest();
        server.TickOwnerCratesForTest();
        Assert.Single(CratesOf(server, "Ada"));

        server.SetActiveStoryForTest("none");
        server.SetActiveStoryForTest("vega_protocol");
        server.SnapshotGuardianRewardRosterForTest(); // what a second win does
        server.TickOwnerCratesForTest();

        Assert.Single(CratesOf(server, "Ada"));
        Assert.DoesNotContain("Ada", server.GuardianRewardRosterForTest);
    }

    [Fact]
    public void TheRoster_SurvivesARestart()
    {
        var transport = new RecordingTransport();
        var paths = new SaveGamePaths(_root, "reward_restart");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var server = new SvGameServer(new ServerConfig
            {
                WorldName = "reward_restart",
                Seed = 4242,
                StartPlanet = "rocky",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
                ViewDistanceChunks = 1,
            }, _content, transport, repo);
            server.Start();
            var ada = server.AddLocalPlayer("Ada");
            ada.State.InEva = true;
            server.MarkGuardianDefeatedForTest();
            server.TickOwnerCratesForTest();
        }

        SqliteConnection.ClearAllPools();
        var restarted = Started("reward_restart", new RecordingTransport());
        Assert.Contains("Ada", restarted.GuardianRewardRosterForTest);
    }
}
