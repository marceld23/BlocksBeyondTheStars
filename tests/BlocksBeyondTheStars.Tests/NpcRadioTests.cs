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

            // The camp sits near the quartermaster's settlement — the #1158 proximity gate must not be
            // what silences the earlier steps.
            var qmHome = server.NpcSnapshots.First(n => n.Role == "quartermaster").Home;
            server.SpawnBanditCampForTest(new Vector3f(qmHome.X + 60, qmHome.Y, qmHome.Z), 2);
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

    private string EnglishLine(string key)
        => _content.CreateLocalizer(Shared.Localization.GameLocale.English).Get(key);

    [Fact]
    public void ACampFarFromTheSettlement_DoesNotTriggerItsQuartermaster()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Ranger");
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);
            p.State.Position = server.NpcSnapshots.First(n => n.Role == "quartermaster").Home;
            p.State.NpcMemory[server.NpcKeyForTest("Ranger", "quartermaster")!] =
                new NpcRelationship { Name = "Q", Role = "quartermaster", Value = 20 };

            // A camp 600 blocks straight up is far outside the 400-block worry radius (#1158) — vertical so
            // no world-wrap can fold the distance back into range.
            server.SpawnBanditCampForTest(new Vector3f(p.State.Position.X, p.State.Position.Y + 600, p.State.Position.Z), 2);
            server.SkipNpcCallCooldownsForTest("Ranger");
            server.ScanNpcRadioForTest("Ranger");
            server.Tick(0.1);
            client.Poll();
            Assert.DoesNotContain(calls, c => c.Text == EnglishLine("npc.call.camp"));
        }
    }

    [Fact]
    public void ClearingACalledCamp_PaysTheFriendshipBonus_AndAThanks()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Hero");
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);
            p.State.Position = server.NpcSnapshots.First(n => n.Role == "quartermaster").Home;
            string npcKey = server.NpcKeyForTest("Hero", "quartermaster")!;
            p.State.NpcMemory[npcKey] = new NpcRelationship { Name = "Q", Role = "quartermaster", Value = 20 };

            server.SpawnBanditCampForTest(new Vector3f(p.State.Position.X + 60, p.State.Position.Y, p.State.Position.Z), 2);
            server.SkipNpcCallCooldownsForTest("Hero");
            server.ScanNpcRadioForTest("Hero");
            server.Tick(0.1);
            client.Poll();
            Assert.Contains(calls, c => c.IsNpcCall && c.Text == EnglishLine("npc.call.camp"));
            int before = p.State.NpcMemory[npcKey].Value;

            // Put down every guard — the last one clears the camp and triggers the gratitude (#1158).
            foreach (var guard in server.Bandits.ToList())
            {
                p.State.Position = guard.Position;
                for (int i = 0; i < 10 && server.Bandits.Contains(guard); i++)
                {
                    server.AttackEntity("Hero", guard.Id);
                }
            }

            server.Tick(0.1);
            client.Poll();
            Assert.Equal(before + 5, p.State.NpcMemory[npcKey].Value); // mission weight 3 + gratitude 2
            Assert.Contains(calls, c => c.IsNpcCall && c.Text == EnglishLine("npc.call.camp_thanks"));
        }
    }

    [Fact]
    public void RaidersInTheSystem_TriggerAKnownQuartermastersWarning()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Pilot");
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);
            p.State.Position = server.NpcSnapshots.First(n => n.Role == "quartermaster").Home; // key seam needs reach
            p.State.NpcMemory[server.NpcKeyForTest("Pilot", "quartermaster")!] =
                new NpcRelationship { Name = "Q", Role = "quartermaster", Value = 20 };

            string home = p.CurrentLocationId;
            server.EnterSpace("Pilot");
            server.SpawnRaiderShipForTest("Pilot");
            p.CurrentLocationId = home; // pin the body id — reach + system lookup key off it

            server.SkipNpcCallCooldownsForTest("Pilot");
            server.ScanNpcRadioForTest("Pilot");
            server.Tick(0.1);
            client.Poll();
            var call = Assert.Single(calls);
            Assert.True(call.IsNpcCall);
            string template = EnglishLine("npc.call.raider");
            Assert.StartsWith(template.Substring(0, template.IndexOf("{0}", StringComparison.Ordinal)), call.Text);
        }
    }

    [Fact]
    public void AFoodDeliveryOnAKnownBoard_TriggersTheShortageCall()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var lines = CaptureChat(client);
            JoinAndDrain(server, client, "Courier");
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);
            string settlement = server.BoardSettlementNameForTest();
            string npcKey = server.SettlementLocationKeyForTest(settlement) + ":quartermaster";
            p.State.NpcMemory[npcKey] = new NpcRelationship { Name = "Q", Role = "quartermaster", Value = 20 };

            server.AddSettlementBoardMissionForTest(settlement, new Shared.Missions.MissionDefinition
            {
                Id = "food_test",
                Title = "Pantry run",
                Objectives = { new Shared.Missions.MissionObjective { Type = Shared.Missions.MissionObjectiveType.Deliver, Target = "berries", Required = 5 } },
            });

            server.SkipNpcCallCooldownsForTest("Courier");
            server.ScanNpcRadioForTest("Courier");
            server.Tick(0.1);
            client.Poll();
            Assert.Contains(lines, c => c.IsNpcCall && c.Text == EnglishLine("npc.call.food"));
        }
    }

    [Fact]
    public void AKnownVendor_SharesTheirStoryThread_OverTheRadio_Once()
    {
        var server = StartedWithBoard(out var repo, out var link);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var calls = CaptureChat(client);
            JoinAndDrain(server, client, "Listener");
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);

            // Only the VENDOR is known (the settler-legend thread lives with them: known + knowledge 1,
            // which a fresh join satisfies) — the quartermaster stays a stranger so no camp/food call
            // interferes.
            string settlement = server.BoardSettlementNameForTest();
            string vendorKey = server.SettlementLocationKeyForTest(settlement) + ":vendor";
            p.State.NpcMemory[vendorKey] = new NpcRelationship { Name = "Mira", Role = "vendor", Value = 20 };

            server.SkipNpcCallCooldownsForTest("Listener");
            server.ScanNpcRadioForTest("Listener");
            server.Tick(0.1);
            client.Poll();
            var call = Assert.Single(calls);
            Assert.True(call.IsNpcCall);
            Assert.Contains(p.State.Milestones, m => m.StartsWith("npcthread:", StringComparison.Ordinal));

            // Told once per player: the next scan stays silent (cadence aside, the thread is burnt).
            server.SkipNpcCallCooldownsForTest("Listener");
            server.ScanNpcRadioForTest("Listener");
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
