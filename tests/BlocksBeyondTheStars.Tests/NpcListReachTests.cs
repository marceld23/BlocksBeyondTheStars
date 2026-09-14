// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1884: every player receives only the NPCs within their streaming radius (+ two chunks), not every NPC of
/// the world five times a second.</summary>
public sealed class NpcListReachTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_npcreach_" + Guid.NewGuid().ToString("N"));
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(long seed, string planet, LoopbackServerTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + "_" + seed));
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "reach_" + planet + "_" + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        }, Content, transport, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AFarPlayer_ReceivesNoneOfASettlementsNpcs_AndWalkingUpBringsThemIn()
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var link = new LoopbackLink();
            using var transport = new LoopbackServerTransport(link);
            using var client = new LoopbackClientTransport(link);
            var server = Start(seed, "jungle", transport, out var repo);
            using (repo)
            {
                if (!server.HasSettlement || server.SettlementRuined || server.NpcCount == 0)
                {
                    continue;
                }

                var lists = new List<NpcList>();
                client.PayloadReceived += payload =>
                {
                    if (NetCodec.Decode(payload) is NpcList m)
                    {
                        lists.Add(m);
                    }
                };
                client.Connect("loopback", 0);
                client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Walker" }), DeliveryMode.ReliableOrdered);
                server.Tick(0.1);
                client.Poll();
                var session = server.Sessions.Values.Single();
                session.State.AboardShip = false;

                var npc = server.NpcSnapshots[0];
                double circumference = server.World.Circumference;

                // Half a world away: nothing of this settlement is on the wire.
                session.State.Position = new Vector3f((float)(npc.Home.X + circumference / 2), npc.Home.Y, npc.Home.Z);
                Assert.DoesNotContain(npc.Id, server.NpcIdsSentToForTest(session));
                lists.Clear();
                for (int i = 0; i < 6; i++)
                {
                    server.TickForTest(0.1);
                }

                client.Poll();
                Assert.NotEmpty(lists);
                Assert.DoesNotContain(lists[^1].Npcs, n => n.Id == npc.Id);

                // At the NPC's door: it is in the list again.
                session.State.Position = npc.Home;
                Assert.Contains(npc.Id, server.NpcIdsSentToForTest(session));
                lists.Clear();
                for (int i = 0; i < 6; i++)
                {
                    server.TickForTest(0.1);
                }

                client.Poll();
                Assert.NotEmpty(lists);
                Assert.Contains(lists[^1].Npcs, n => n.Id == npc.Id);
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with NPCs found across 80 seeds.");
    }

    [Fact]
    public void TwoPlayersAtDifferentPlaces_ReceiveDifferentLists()
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            using var transport = new LoopbackServerTransport(new LoopbackLink());
            var server = Start(seed, "jungle", transport, out var repo);
            using (repo)
            {
                if (!server.HasSettlement || server.SettlementRuined || server.NpcCount == 0)
                {
                    continue;
                }

                var npc = server.NpcSnapshots[0];
                var near = server.AddLocalPlayer("Near");
                var far = server.AddLocalPlayer("Far");
                near.State.Position = npc.Home;
                far.State.Position = new Vector3f((float)(npc.Home.X + server.World.Circumference / 2), npc.Home.Y, npc.Home.Z);

                Assert.Contains(npc.Id, server.NpcIdsSentToForTest(near));
                Assert.DoesNotContain(npc.Id, server.NpcIdsSentToForTest(far));
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement with NPCs found across 80 seeds.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // a locked temp file must not fail the test run
        }
    }
}
