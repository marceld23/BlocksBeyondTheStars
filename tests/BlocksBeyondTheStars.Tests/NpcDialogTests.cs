// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
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
[Collection(RealTimeSensitiveCollection.Name)] // #1786: 7 s locally, billed 131 s in the parallel queue on a PR shard (#1362 pattern)
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

    /// <summary>#2601: a plain settler without a personal wish for the test player ("Friend") — a wish opens before every
    /// other dialogue, so the smalltalk tests talk to somebody who has none.</summary>
    private static (int Id, string Name, string Role, string CharacterId)? SettlerWithoutWish(SvGameServer s)
    {
        var wishGivers = s.WishGiverIdsForTest("Friend");
        return FirstNpc(s, n => n.Role == "settler" && n.CharacterId.Length == 0 && !wishGivers.Contains(n.Id));
    }

    private static (int Id, string Name, string Role, string CharacterId)? FirstNpc(SvGameServer s, Func<(int Id, string Name, string Role, string CharacterId), bool> where)
        => s.NpcRosterForTest().Where(where).Cast<(int, string, string, string)?>().FirstOrDefault();

    // ---------------- The graph walker ----------------

    [Fact]
    public void TalkingToASettler_WalksTheDialogue_AndPersistsTheChoice()
    {
        var server = StartedAt(SettlerWithoutWish,
            out var repo, out var player, out int npcId, out _);
        using (repo)
        {
            var p = player.State;
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("settler_neighbours", 0), server.ActiveDialogForTest(p.PlayerId));

            server.ChooseDialogForTest(p.PlayerId, 0); // "how is life here?" → standing:1 and the dialogue ends
            Assert.Null(server.ActiveDialogForTest(p.PlayerId));

            // The decision persisted, the dormant Dialog interaction finally has a producer, and the
            // standing moved (#2598: the first meeting lifts a villager to 3, +1 interaction weight, +1 consequence).
            Assert.Contains("dialogflag:settler_neighbours:0:0", p.Milestones);
            var rel = Assert.Single(p.NpcMemory.Values.Where(r => r.Role == "settler"));
            Assert.Contains(rel.Log, e => e.Kind == NpcInteractionKind.Dialog);
            Assert.Equal(5, rel.Value);

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
            string key = server.NpcKeyForTest(p.PlayerId, "vendor")!;

            // #2673: a stranger gets the stage-free hello — before, Talk only showed the "want to trade?" bubble, and
            // nothing at all inside its cooldown. "Any news?" pays a little standing: talking slowly builds the friendship.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("vendor_hello", 0), server.ActiveDialogForTest(p.PlayerId));
            server.ChooseDialogForTest(p.PlayerId, 1);
            Assert.Null(server.ActiveDialogForTest(p.PlayerId));
            Assert.True(p.NpcMemory.TryGetValue(key, out var met) && met.Value > 0, "the hello's news answer pays no standing");

            // Known → the vendor first asks the regular's favour (#1212: the mission-offering dialogue wins while
            // takeable); "not today" sets it aside for the session and the smalltalk opens — the hello stays behind them.
            p.NpcMemory[key] = new NpcRelationship { Role = "vendor", Value = 20 };
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("vendor_favour_1", 0), server.ActiveDialogForTest(p.PlayerId));
            server.ChooseDialogForTest(p.PlayerId, 1);
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("vendor_smalltalk", 0), server.ActiveDialogForTest(p.PlayerId));
            server.Stop();
        }
    }

    // ---------------- #2673: Talk is never silent ----------------

    private static List<NpcGreeting> GreetingsFrom(NpcLifeWorld w, int npcId)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>()
            .Where(g => g.NpcId == npcId).ToList();

    [Fact]
    public void TradeThenTalk_InsideTheGreetingCooldown_StillOpensTheHello()
    {
        // The report: a new LAN world (a stranger to every trader), "Trade", close the screen, "Talk" → nothing at all.
        // "Trade" greets the vendor, which arms the 25 s greeting cooldown; the talk had nothing but that greeting.
        using var w = new NpcLifeWorld(_content, "talk_after_trade");
        var at = w.Player.State.Position;
        int vendor = w.Server.AddNpcForTest(new Vector3f(at.X + 2f, w.Feet, at.Z), role: "vendor");
        string pid = w.Player.State.PlayerId;

        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new NpcGreetIntent { Role = "vendor" }));
        Assert.Single(GreetingsFrom(w, vendor));

        w.Server.TalkToNpcForTest(pid, vendor);
        Assert.Equal(("vendor_hello", 0), w.Server.ActiveDialogForTest(pid));
        var panel = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();
        Assert.Equal(vendor, panel.NpcId);
        Assert.Equal(3, panel.Choices.Length);
        Assert.False(panel.End);
    }

    [Fact]
    public void ATalk_WithNoDialogue_IsNeverSilent_EvenInsideTheGreetingCooldown()
    {
        // A guardian off the G.D.S. world has no dialogue: every Talk answers with the greeting bubble, also the ones
        // inside the cooldown (the "Trade" greeting or the previous Talk armed it).
        using var w = new NpcLifeWorld(_content, "talk_never_silent");
        var at = w.Player.State.Position;
        int guard = w.Server.AddNpcForTest(new Vector3f(at.X + 2f, w.Feet, at.Z), role: "guardian");
        string pid = w.Player.State.PlayerId;

        for (int i = 1; i <= 3; i++)
        {
            w.Server.TalkToNpcForTest(pid, guard);
            Assert.Null(w.Server.ActiveDialogForTest(pid));
            Assert.Equal(i, GreetingsFrom(w, guard).Count);
        }

        // Each is a new bubble (its own id), so the client shows and voices every one.
        Assert.Equal(3, GreetingsFrom(w, guard).Select(g => g.GreetingId).Distinct().Count());
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

    [Fact]
    public void RepeatableSmalltalk_PaysItsRelationshipReward_OncePerCooldown()
    {
        var server = StartedAt(SettlerWithoutWish,
            out var repo, out var player, out int npcId, out _);
        using (repo)
        {
            var p = player.State;

            // First walk pays: the first meeting (#2598: lifts a villager to 3), +1 Dialog interaction, +1 standing.
            server.TalkToNpcForTest(p.PlayerId, npcId);
            server.ChooseDialogForTest(p.PlayerId, 0);
            var rel = Assert.Single(p.NpcMemory.Values.Where(r => r.Role == "settler"));
            Assert.Equal(5, rel.Value);

            // Spam-talking pays nothing more — the dialogue still opens and reads normally (#1162).
            server.TalkToNpcForTest(p.PlayerId, npcId);
            Assert.Equal(("settler_neighbours", 0), server.ActiveDialogForTest(p.PlayerId));
            server.ChooseDialogForTest(p.PlayerId, 0);
            Assert.Equal(5, rel.Value);

            // After the cooldown it pays again (a person's is 10 minutes, #2598) — the first meeting only once.
            server.SkipDialogRewardCooldownForTest(p.PlayerId);
            server.TalkToNpcForTest(p.PlayerId, npcId);
            server.ChooseDialogForTest(p.PlayerId, 0);
            Assert.Equal(7, rel.Value);
            server.Stop();
        }
    }

    [Fact]
    public void AuthoredCharacters_ClaimAtMostOneSlotPerPlace()
    {
        // Wherever a pack character is cast, their place must hold exactly ONE of them — a market with
        // several npc markers must not spawn a row of identical Yara Senns sharing one memory (#1150).
        // Scan seeds until a handful of claims were seen so multi-slot places get sampled too.
        //
        // FOUR claims, not six: each sampled seed casts exactly one character, so the claim target IS
        // the number of servers started — and a server start bakes a world, which generation 4
        // (#1715–#1724) made dear enough that six of them crossed the fast tier's 120 s budget on a CI
        // runner (127 s, measured on PR #1742; ~9.4 s per start locally, ~16 s there). Four still
        // samples several worlds; the seed ceiling is only a stop for a pathological run.
        int claims = 0;
        for (long seed = 1; seed <= 60 && claims < 4; seed++)
        {
            var server = NewServer($"cast_{seed}", seed, out var repo);
            using (repo)
            {
                foreach (var (characterId, place, count) in server.AuthoredCastingForTest())
                {
                    claims++;
                    Assert.True(count == 1, $"seed {seed}: {count}× '{characterId}' at '{place}'");
                }

                server.Stop();
            }
        }

        Assert.True(claims > 0, "no authored character was cast across the sampled seeds");
    }

    // ---------------- Content consistency ----------------

    [Fact]
    public void EveryNpcOfferedTradeOrTalk_HasADialogueAStrangerCanOpen()
    {
        // #2673: the client asks "Trade or talk?" at every NPC of the vendor role (VendorReach.VendorRole) — the classic
        // trader at a stall or a station post, a visiting or landed trader, a base's own trader, the trading professions,
        // the travelling merchant and the wilderness folk. Each needs a dialogue without a minimum stage, or "Talk" is
        // only the greeting bubble for a newcomer. The quartermaster too: away from the board, E talks to them.
        static bool ForStrangers(DialogDefinition d)
            => d.MinStage.Length == 0 && d.Character.Length == 0 && d.PlanetTypes.Count == 0 && !d.OncePerPlayer
               && d.Nodes.Count > 0 && d.Nodes[0].Choices.Count > 0;

        // The classic posts take the plain role dialogue, wherever they stand (a settlement, a station, a base).
        foreach (string role in new[] { "vendor", "quartermaster" })
        {
            Assert.True(_content.Dialogs.Any(d => ForStrangers(d) && d.Role == role && d.Job.Length == 0 && d.Home == DialogDefinition.HomeAny),
                $"the role '{role}' has no dialogue a stranger can open");
        }

        // A profession, a guest and the wilderness folk only ever take their own job's dialogues (PickDialog).
        var jobs = NpcProfessions.All.Where(p => p.Trades).Select(p => (Job: p.Job, Home: DialogDefinition.HomeAny))
            .Append((Job: "guest_merchant", Home: DialogDefinition.HomeAny))
            .Concat(_content.Lodges.SelectMany(l => new[] { (Job: l.Key, Home: DialogDefinition.HomeLodge), (Job: l.Key, Home: DialogDefinition.HomeWander) }))
            .ToList();
        Assert.Contains(jobs, j => j.Home == DialogDefinition.HomeLodge); // the lodge table loaded
        foreach (var (job, home) in jobs)
        {
            Assert.True(_content.Dialogs.Any(d => ForStrangers(d) && d.Job == job && (d.Home == home || d.Home == DialogDefinition.HomeAny)),
                $"the job '{job}' (home '{home}') has no dialogue a stranger can open");
        }

        // The hellos come after the regulars' talks, so a known face still gets the favours and the smalltalk first.
        var keys = _content.Dialogs.Select(d => d.Key).ToList();
        Assert.True(keys.IndexOf("vendor_hello") > keys.IndexOf("vendor_smalltalk"), "vendor_hello must follow vendor_smalltalk");
        Assert.True(keys.IndexOf("vendor_hello") > keys.IndexOf("vendor_favour_3"), "vendor_hello must follow the favours");
    }

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
