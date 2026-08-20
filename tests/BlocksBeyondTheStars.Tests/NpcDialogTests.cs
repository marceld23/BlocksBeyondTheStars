// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// NPC dialogues (#1127) — the reserved "item 15" Dialog backend finally wired — and the story pack's
/// authored characters (#1128). The graph walker is fully server-authoritative: stage gates, once-flags,
/// choice persistence (milestones) and consequences (standing / fragment / gift / later radio call) all
/// live server-side; the client only renders resolved text and returns an index. Works with AiLevel Off.
/// </summary>
public sealed class NpcDialogTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public NpcDialogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_npcdialog_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer NewServer(string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
            AiLevel = AiLevel.Off,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>An inhabited settlement holding an NPC that satisfies <paramref name="pick"/>, the player
    /// standing at them. Scans seeds — deterministic: whichever seed qualifies first always qualifies.</summary>
    private SvGameServer StartedAt(
        Func<SvGameServer, (int Id, string Name, string Role, string CharacterId)?> pick,
        out SqliteWorldRepository repo,
        out BlocksBeyondTheStars.GameServer.PlayerSession player,
        out int npcId,
        out long seed)
    {
        for (seed = 1; seed <= 200; seed++)
        {
            var server = NewServer($"dlg_{seed}", seed, out repo);
            if (server.HasSettlement && !server.SettlementRuined && pick(server) is { } npc)
            {
                player = server.AddLocalPlayer("Friend");
                player.State.Position = server.NpcSnapshots.First(n => n.Id == npc.Id).Home;
                npcId = npc.Id;
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("no qualifying settlement NPC found across 200 seeds");
    }

    private static (int Id, string Name, string Role, string CharacterId)? FirstNpc(SvGameServer s, Func<(int Id, string Name, string Role, string CharacterId), bool> where)
        => s.NpcRosterForTest().Where(where).Cast<(int, string, string, string)?>().FirstOrDefault();

    // ---------------- The graph walker ----------------

    [Fact]
    public void TalkingToASettler_WalksTheDialogue_AndPersistsTheChoice()
    {
        var server = StartedAt(s => FirstNpc(s, n => n.Role == "settler" && n.CharacterId.Length == 0),
            out var repo, out var player, out int npcId, out _);
        using (repo)
        {
            var p = player.State;
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("settler_neighbours", 0), server.ActiveDialogForTest(p.PlayerId));

            server.ChooseDialogForTest(p.PlayerId, 0); // "how is life here?" → standing:1 and the dialogue ends
            Assert.Null(server.ActiveDialogForTest(p.PlayerId));

            // The decision persisted, the dormant Dialog interaction finally has a producer, and the
            // standing moved (+1 interaction weight, +1 consequence).
            Assert.Contains("dialogflag:settler_neighbours:0:0", p.Milestones);
            var rel = Assert.Single(p.NpcMemory.Values.Where(r => r.Role == "settler"));
            Assert.Contains(rel.Log, e => e.Kind == NpcInteractionKind.Dialog);
            Assert.Equal(2, rel.Value);

            // Not once-per-player: talking again reopens it.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("settler_neighbours", 0), server.ActiveDialogForTest(p.PlayerId));
            server.Stop();
        }
    }

    [Fact]
    public void VendorSmalltalk_IsGatedByRelationshipStage()
    {
        var server = StartedAt(s => FirstNpc(s, n => n.Role == "vendor" && n.CharacterId.Length == 0),
            out var repo, out var player, out int npcId, out _);
        using (repo)
        {
            var p = player.State;

            // A stranger gets the ordinary greeting, no dialogue.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Null(server.ActiveDialogForTest(p.PlayerId));

            // Known → the smalltalk opens.
            string key = server.NpcKeyForTest(p.PlayerId, "vendor")!;
            p.NpcMemory[key] = new NpcRelationship { Role = "vendor", Value = 20 };
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("vendor_smalltalk", 0), server.ActiveDialogForTest(p.PlayerId));
            server.Stop();
        }
    }

    // ---------------- Authored characters (#1128) ----------------

    [Fact]
    public void TheElder_ClaimsSettlerSlots_TalksHerOwnDialogue_AndIsDoneOncePerSave()
    {
        var server = StartedAt(s => FirstNpc(s, n => n.CharacterId == "elder"),
            out var repo, out var player, out int npcId, out long seed);
        using (repo)
        {
            var p = player.State;
            var elder = server.NpcRosterForTest().First(n => n.Id == npcId);
            Assert.Equal("Yara Senn", elder.Name); // the authored name, not a coined one

            // Her authored dialogue wins over the generic settler smalltalk.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("elder_keepsake", 0), server.ActiveDialogForTest(p.PlayerId));

            // "Another time" ends the talk but must NOT burn the once-flag (keepOpen).
            server.ChooseDialogForTest(p.PlayerId, 1);
            Assert.Null(server.ActiveDialogForTest(p.PlayerId));
            Assert.DoesNotContain("dialog:elder_keepsake:done", p.Milestones);

            // Take the story path this time: node 1, then the written page — a real story fragment.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            server.ChooseDialogForTest(p.PlayerId, 0);
            Assert.Equal(("elder_keepsake", 1), server.ActiveDialogForTest(p.PlayerId));
            var (_, fragmentsBefore, _, _, _, _) = server.StorySnapshot;
            server.ChooseDialogForTest(p.PlayerId, 0);
            var (_, fragmentsAfter, _, _, _, _) = server.StorySnapshot;
            Assert.Equal(fragmentsBefore + 1, fragmentsAfter);
            Assert.Contains("dialog:elder_keepsake:done", p.Milestones);

            // She remembers GLOBALLY — the memory sits under her character key, not a settlement key.
            Assert.True(p.NpcMemory.ContainsKey("char:elder"));

            // Done once per save: she falls back to the generic settler smalltalk now.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("settler_neighbours", 0), server.ActiveDialogForTest(p.PlayerId));
            server.Stop();

            // …and the once-flag survives a restart (it rides the player's milestones).
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var again = NewServer($"dlg_{seed}", seed, out var repo2);
            using (repo2)
            {
                var back = again.AddLocalPlayer("Friend");
                Assert.Contains("dialog:elder_keepsake:done", back.State.Milestones);
                Assert.True(back.State.NpcMemory.ContainsKey("char:elder"));
                again.Stop();
            }
        }
    }

    [Fact]
    public void TheEldersToolPath_HandsOverTheGift()
    {
        var server = StartedAt(s => FirstNpc(s, n => n.CharacterId == "elder"),
            out var repo, out var player, out int npcId, out _);
        using (repo)
        {
            var p = player.State;
            server.TalkToNpcForTest(p.PlayerId, npcId);
            server.ChooseDialogForTest(p.PlayerId, 0); // hear the story
            int before = p.Inventory.CountOf("iron_plate");
            server.ChooseDialogForTest(p.PlayerId, 1); // take the tools instead of the page
            Assert.Equal(before + 10, p.Inventory.CountOf("iron_plate"));
            server.Stop();
        }
    }

    // ---------------- Content consistency ----------------

    [Fact]
    public void EngineAndPackDialogues_Declare_AndEveryTextResolves()
    {
        Assert.True(_content.Dialogs.Count >= 2, "engine dialogs.json should carry the settler + vendor talks");

        var pack = _content.Stories["vega_protocol"];
        Assert.Equal(2, pack.Characters.Count);
        Assert.All(pack.Characters, c => Assert.NotEqual("quartermaster", string.Join(",", c.Roles))); // item-13 names stay
        Assert.Equal(2, pack.Dialogs.Count);

        var en = _content.CreateLocalizer(Shared.Localization.GameLocale.English);
        var de = _content.CreateLocalizer(Shared.Localization.GameLocale.German);
        foreach (var d in _content.Dialogs.Concat(pack.Dialogs))
        {
            foreach (var node in d.Nodes)
            {
                Assert.True(en.Has(node.PromptKey) && de.Has(node.PromptKey), $"missing '{node.PromptKey}'");
                foreach (var c in node.Choices)
                {
                    Assert.True(en.Has(c.TextKey) && de.Has(c.TextKey), $"missing '{c.TextKey}'");
                    Assert.True(en.Has(c.ResponseKey) && de.Has(c.ResponseKey), $"missing '{c.ResponseKey}'");
                    Assert.InRange(c.Next, -1, d.Nodes.Count - 1); // the walker validates too — data should be clean

                    var parts = c.Consequence.Split(':');
                    switch (parts[0])
                    {
                        case "fragment":
                            Assert.Contains(pack.Fragments, f => f.Key == parts[1]);
                            break;
                        case "gift":
                            Assert.NotNull(_content.GetItem(parts[1]));
                            break;
                        case "radio":
                            Assert.True(en.Has(parts[1]) && de.Has(parts[1]), $"missing radio line '{parts[1]}'");
                            break;
                    }
                }
            }
        }
    }
}
