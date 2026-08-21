// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// NPC radio calls (#1119): an acquainted quartermaster calls about an uncleared bandit camp — but only
/// through a radio the player actually carries, only at known+ standing, only within the preference, and
/// never twice inside the cooldown. Plus the roster/preference plumbing (#1118).
/// </summary>
public sealed class NpcRadioTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NpcRadioTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_npcradio_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo, out LoopbackLink link)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "radio_" + seed));
        link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var config = new ServerConfig
        {
            WorldName = "radio_" + seed,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A world whose settlement offers board missions (so a quartermaster NPC exists).</summary>
    private SvGameServer StartedWithBoard(out SqliteWorldRepository repo, out LoopbackLink link)
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var server = Start(seed, out repo, out link);
            if (server.SettlementMissionIds.Count > 0 && server.NpcSnapshots.Any(n => n.Role == "quartermaster"))
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No settlement with a quartermaster found across 60 seeds.");
    }

    private static List<ChatMessage> CaptureChat(LoopbackClientTransport client)
    {
        var lines = new List<ChatMessage>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ChatMessage m)
            {
                lines.Add(m);
            }
        };
        return lines;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    [Fact]
    public void KnownQuartermaster_CallsAboutACamp_OnceWithinTheCooldown()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Scout");
            var p = server.Sessions[1];

            p.State.Inventory.Add("comm_radio", 1, 99);
            p.State.Position = server.NpcSnapshots.First(n => n.Role == "quartermaster").Home; // key seam needs reach
            p.State.NpcMemory[server.NpcKeyForTest("Scout", "quartermaster")!] =
                new NpcRelationship { Name = "Q", Role = "quartermaster", Value = 20 }; // known

            server.SpawnBanditCampForTest(new Vector3f(p.State.Position.X + 60, p.State.Position.Y, p.State.Position.Z), 2);
            server.SkipNpcCallCooldownsForTest("Scout");

            server.ScanNpcRadioForTest("Scout");
            server.Tick(0.1);
            client.Poll();
            var call = Assert.Single(calls);
            Assert.StartsWith("📻 ", call.Sender);
            Assert.False(string.IsNullOrWhiteSpace(call.Text));

            // The same camp never calls twice inside the cooldown (and the global cadence holds too).
            server.ScanNpcRadioForTest("Scout");
            server.Tick(0.1);
            client.Poll();
            Assert.Single(calls);
        }
    }

    [Fact]
    public void NoRadio_OrStranger_OrOptOut_MeansNoCall()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Quiet");
            var p = server.Sessions[1];

            server.SpawnBanditCampForTest(new Vector3f(p.State.Position.X + 60, p.State.Position.Y, p.State.Position.Z), 2);
            server.SkipNpcCallCooldownsForTest("Quiet");

            // No radio: silence.
            server.ScanNpcRadioForTest("Quiet");
            server.Tick(0.1);
            client.Poll();
            Assert.Empty(calls);

            // Radio, but a stranger: silence.
            p.State.Inventory.Add("comm_radio", 1, 99);
            server.ScanNpcRadioForTest("Quiet");
            server.Tick(0.1);
            client.Poll();
            Assert.Empty(calls);

            // Known, but calls are off: silence.
            p.State.Position = server.NpcSnapshots.First(n => n.Role == "quartermaster").Home; // key seam needs reach
            p.State.NpcMemory[server.NpcKeyForTest("Quiet", "quartermaster")!] =
                new NpcRelationship { Name = "Q", Role = "quartermaster", Value = 20 };
            p.State.NpcCallsMode = NpcCallsMode.Off;
            server.ScanNpcRadioForTest("Quiet");
            server.Tick(0.1);
            client.Poll();
            Assert.Empty(calls);

            // Preference back to all: the call goes out.
            p.State.NpcCallsMode = NpcCallsMode.All;
            server.ScanNpcRadioForTest("Quiet");
            server.Tick(0.1);
            client.Poll();
            Assert.Single(calls);
        }
    }

    [Fact]
    public void DialogPromisedCall_ReachesAStranger_AndSurvivesADeferral()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Newcomer");
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);

            // The dialogue promised a call while the player is still a stranger to the NPC. The join quiet
            // period blocks the first attempt — the one-shot must be DEFERRED, never lost (#1149).
            server.QueueDialogRadioForTest(p.State.PlayerId, "char:sel", "Sel-9", "Somewhere",
                p.CurrentLocationId, "npc.call.board");
            server.TickDialogRadioForTest();
            server.Tick(0.1);
            client.Poll();
            Assert.Empty(calls);
            Assert.Equal(1, server.DialogRadioPendingForTest);

            // Once the retry is due and the gates are open, the call reaches the player even though there
            // is no relationship entry at all — the dialogue itself was the personal contact.
            server.SkipNpcCallCooldownsForTest("Newcomer");
            server.Tick(61.0); // past the retry delay
            server.TickDialogRadioForTest();
            server.Tick(0.1);
            client.Poll();
            var call = Assert.Single(calls);
            Assert.Equal("📻 Sel-9 (Somewhere)", call.Sender);
            Assert.False(string.IsNullOrWhiteSpace(call.Text));
            Assert.Equal(0, server.DialogRadioPendingForTest);

            // The player's own preference is a HARD gate: "missions only" drops a promised chit-chat call
            // for good instead of retrying it forever.
            p.State.NpcCallsMode = NpcCallsMode.MissionsOnly;
            server.QueueDialogRadioForTest(p.State.PlayerId, "char:sel", "Sel-9", "Somewhere",
                p.CurrentLocationId, "npc.call.board");
            server.TickDialogRadioForTest();
            server.Tick(0.1);
            client.Poll();
            Assert.Single(calls);
            Assert.Equal(0, server.DialogRadioPendingForTest);
        }
    }

    [Fact]
    public void CallPreference_AndKnownRoster_RoundTripThroughTheirIntents()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "radio_intents"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var rosters = new List<KnownNpcList>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is KnownNpcList m)
            {
                rosters.Add(m);
            }
        };
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "radio_intents",
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        }, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Talker");

        var session = server.Sessions[1];
        client.Send(NetCodec.Encode(new SetNpcCallsIntent { Mode = 1 }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Equal(NpcCallsMode.MissionsOnly, session.State.NpcCallsMode);

        session.State.NpcMemory["settle_1:vendor"] = new NpcRelationship { Name = "Mira", Role = "vendor", Place = "Neudorf", Value = 45 };
        client.Send(NetCodec.Encode(new RequestKnownNpcsIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var roster = Assert.Single(rosters);
        var person = Assert.Single(roster.People);
        Assert.Equal("Mira", person.Name);
        Assert.Equal("npc.role.vendor", person.RoleKey);
        Assert.Equal("npc.stage.trusted", person.StageKey);
        Assert.Equal("Neudorf", person.Place);
    }

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }
}
