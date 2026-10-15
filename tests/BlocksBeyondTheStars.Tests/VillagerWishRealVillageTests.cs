// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2601 personal wishes in REAL generated settlements (a seed loop, like the resident suites): every settlement shows at
/// most its lane count of wishes (a hamlet one, every larger place two), only on villagers who may wish (plain settlers,
/// gardeners, craftsmen, innkeepers — never a trader, quartermaster, profession, guardian or authored character), the
/// coin is deterministic per world and player, and an accepted wish survives a restart (its definition from the mission
/// store, the lane from the player save) and is still talked about afterwards.
/// <para>Each server start bakes a world — one seed known to qualify, two starts in all.</para>
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)]
public sealed class VillagerWishRealVillageTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_wishes_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo, out NpcLifeWorld.RecordingTransport transport)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "wishes_" + seed));
        transport = new NpcLifeWorld.RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = "wishes_" + seed,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceIntercityRail = false,
            PlaceRuins = false,
            PlaceRailRuins = false,
            PlaceBanditCamps = false,
            PlaceMonuments = false,
            PlaceFactories = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceChests = false,
            PlaceWrecks = false,
            AiLevel = AiLevel.Off,
        };
        var server = new SvGameServer(config, Content.Value, transport, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void RealVillages_ShowTheirLanes_OnlyOnVillagersWhoMayWish_AWishSurvivesARestart()
    {
        for (long seed = 1; seed <= 30; seed++)
        {
            var server = Start(seed, out var repo, out _);
            var lanes = server.WishLanesForTest("Friend");
            if (lanes.Count == 0)
            {
                server.Stop();
                repo.Dispose();
                continue;
            }

            string missionId;
            int giverId;
            string giverKey;
            using (repo)
            {
                // Lanes per settlement: a hamlet one, every larger place two — never more, each on another villager.
                var tiers = server.SettlementNamesForTest.Zip(server.SettlementTiersForTest, (n, t) => (n, t))
                    .GroupBy(x => x.n).ToDictionary(g => g.Key, g => g.First().t);
                foreach (var group in lanes.GroupBy(l => l.Settlement))
                {
                    int max = tiers[group.Key] == "hamlet" ? 1 : 2;
                    Assert.InRange(group.Count(), 1, max);
                    Assert.Equal(group.Count(), group.Select(l => l.GiverKey).Distinct().Count());
                }

                // Only villagers who may wish carry one.
                var roster = server.NpcRosterForTest().ToDictionary(n => n.Id);
                var people = server.PersonKeysForTest.ToDictionary(n => n.Id);
                foreach (var lane in lanes)
                {
                    var who = people[lane.GiverId];
                    Assert.Equal("settler", who.Role);
                    Assert.Contains(who.Job, new[] { string.Empty, "gardener", "craftsman", "innkeeper" });
                    Assert.Null(NpcProfessions.ByJob(who.Job));
                    Assert.Equal(string.Empty, roster[lane.GiverId].CharacterId);
                    Assert.Equal(lane.GiverKey, who.PersonKey);
                    Assert.False(lane.Held);
                }

                // Deterministic per world and player.
                Assert.Equal(lanes, server.WishLanesForTest("Friend"));

                // Take the first wish in person.
                var player = server.AddLocalPlayer("Friend");
                player.State.AboardShip = false;
                var first = lanes[0];
                player.State.Position = server.NpcSnapshots.First(n => n.Id == first.GiverId).Home;
                Assert.Contains(first.GiverId, server.WishMarksForTest(player.State.PlayerId).Offers);
                server.TalkToNpcForTest(player.State.PlayerId, first.GiverId);
                Assert.Equal(("wish", 0), server.ActiveDialogForTest(player.State.PlayerId));
                server.ChooseDialogForTest(player.State.PlayerId, 0);
                Assert.Equal(new[] { first.MissionId }, server.HeldWishesForTest(player.State.PlayerId));
                missionId = first.MissionId;
                giverKey = first.GiverKey;
                giverId = first.GiverId;
                server.Stop();
            }

            // A restart keeps the wish: the definition from the mission store, the lane and the row from the save.
            var again = Start(seed, out var repo2, out var transport2);
            using (repo2)
            {
                var player = again.AddLocalPlayer("Friend");
                player.State.AboardShip = false;
                Assert.Equal(new[] { missionId }, again.HeldWishesForTest(player.State.PlayerId));
                Assert.NotEqual(string.Empty, again.MissionGiverName(missionId));
                var held = Assert.Single(again.WishLanesForTest("Friend"), l => l.Held);
                Assert.Equal(giverKey, held.GiverKey);
                Assert.Equal(giverId, held.GiverId); // the same villager (spawn order is deterministic)

                // Talking to them is about the wish (with an empty bag: the reminder), not small talk.
                for (int i = 0; i < player.State.Inventory.SlotCount; i++)
                {
                    player.State.Inventory.SetSlot(i, null);
                }

                player.State.Position = again.NpcSnapshots.First(n => n.Id == held.GiverId).Home;
                again.TalkToNpcForTest(player.State.PlayerId, held.GiverId);
                var said = transport2.Sent.Where(s => s.Conn == player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();
                Assert.True(said.End);
                Assert.Contains("no hurry", said.Text, StringComparison.Ordinal);
                again.Stop();
            }

            return;
        }

        throw new Xunit.Sdk.XunitException("no jungle seed in 1..30 has a settlement with wishes");
    }
}
