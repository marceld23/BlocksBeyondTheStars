// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ship AI companion "VEGA": the cold open for a newcomer (the prologue), the basic training it presents — since #2469 the
/// tutorial story pack (<c>data/stories/tutorial/</c>), so these tests drive the pack through the real handlers: the first
/// step at the join, mining and eating counting towards their steps, Creative skipping the research step, veterans
/// migrated silently, the Settings button's skip and restart — plus the context tips and the memory-fragment story arc
/// (redeemed aboard, knowledge reward, Mk3 parts at the arc's end). The pack runtime itself is covered by
/// <c>StoryPackRuntimeTests</c>, the walkthroughs by <c>TutorialPackTests</c>.
/// </summary>
public sealed class ShipAiTests : IDisposable
{
    private const string Tutorial = SvGameServer.OfficialTutorialPackId;
    private const string FirstStart = "story.tutorial.b1.walk.start";
    private const string FirstChip = "story.tutorial.b1.walk.chip";

    private readonly string _root;
    private readonly GameContent _content;

    public ShipAiTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_vega_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ServerConfig Config() => new()
    {
        WorldName = "vega",
        Seed = 123456,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false, // bare terrain at the spawn column (mining test digs straight down)
    };

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    /// <summary>Joins over the loopback as a current client (<c>packs1</c>) and drains the replies.</summary>
    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1 },
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    /// <summary>Sends one message and lets the server handle it.</summary>
    private static void SendAndTick(SvGameServer server, LoopbackClientTransport client, object message)
    {
        client.Send(NetCodec.Encode(message), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    /// <summary>Skips the basic training's chapters before <paramref name="chapterId"/> as the Journal does.</summary>
    private static void SkipChaptersBefore(SvGameServer server, LoopbackClientTransport client, string chapterId)
    {
        foreach (string chapter in new[] { "b1", "b2", "b3", "b4", "b5", "b6" }.TakeWhile(c => c != chapterId))
        {
            SendAndTick(server, client, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipChapter, ChapterId = chapter });
        }
    }

    /// <summary>Collects every VEGA line the client receives (handler must be attached before joining).</summary>
    private static List<ShipAiLine> CaptureVega(LoopbackClientTransport client)
    {
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };
        return lines;
    }

    [Fact]
    public void NewPlayer_GetsTheBasicTraining_AndMiningCountsTowardsTheMiningStep()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Rookie");

        // The basic training starts at the join with its first step — no old onboarding chain, no old intro lines.
        Assert.Contains(lines, l => l.LineKey == FirstStart && l.PackId == Tutorial);
        Assert.Equal(FirstChip, lines.Last().ObjectiveKey);
        Assert.Equal(ObjectiveSources.Tutorial, lines.Last().ObjectiveSource);
        Assert.True(lines.Last().TutorialActive);
        Assert.DoesNotContain(lines, l => l.LineKey.StartsWith("vega.intro.", StringComparison.Ordinal) || l.LineKey.StartsWith("vega.s.", StringComparison.Ordinal));
        var milestones = server.MilestonesForTest("Rookie");
        Assert.Contains(Shared.Story.StoryPackKeys.Started(Tutorial), milestones);
        Assert.Contains("vega:intro", milestones); // double-written: older builds read "the intro was shown"

        // On to part 3 (the Journal's "skip part"), then mine three nearby blocks through the real handler.
        SkipChaptersBefore(server, client, "b3");
        Assert.Equal("story.tutorial.b3.mine.chip", lines.Last().ObjectiveKey);
        var session = server.Sessions[1];
        int px = (int)Math.Floor(session.State.Position.X);
        int pz = (int)Math.Floor(session.State.Position.Z);
        int topY = (int)Math.Ceiling(session.State.Position.Y);
        int mined = 0;
        for (int dx = -2; dx <= 2 && mined < 3; dx++)
            for (int dz = -2; dz <= 2 && mined < 3; dz++)
                for (int y = topY; y > topY - 7 && mined < 3; y--)
                {
                    var pos = new Vector3i(px + dx, y, pz + dz);
                    if (server.World.GetBlock(pos).IsAir)
                    {
                        continue;
                    }

                    for (int hit = 0; hit < 12 && !server.World.GetBlock(pos).IsAir; hit++)
                    {
                        client.Send(NetCodec.Encode(new MineBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z }),
                            DeliveryMode.ReliableOrdered);
                        server.Tick(0.1);
                    }

                    if (server.World.GetBlock(pos).IsAir)
                    {
                        mined++;
                    }
                }

        Assert.Equal(3, mined);
        client.Poll();

        milestones = server.MilestonesForTest("Rookie");
        Assert.Contains(Shared.Story.StoryPackKeys.StepDone(Tutorial, "b3.1"), milestones);
        Assert.Contains("vega:stage:mine", milestones); // the legacy key, double-written for older builds
        Assert.Contains(lines, l => l.LineKey == "story.tutorial.b3.iron.start");
        Assert.Equal("story.tutorial.b3.iron.chip", lines.Last().ObjectiveKey);
    }

    [Fact]
    public void NewPlayer_GetsThePrologue_BeforeTheFirstLesson_WhenAStoryIsActive()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo); // default pack active
        server.Start();
        JoinAndDrain(server, client, "Dreamer");

        // All three prologue pages arrive as Kind 4 (full-screen overlay), strictly before the first lesson.
        var keys = lines.Where(l => !string.IsNullOrEmpty(l.LineKey)).Select(l => l.LineKey).ToList();
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.1" && l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.2" && l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.3" && l.Kind == 4);
        Assert.True(keys.IndexOf("vega.prologue.3") < keys.IndexOf(FirstStart),
            "the prologue frames the premise BEFORE VEGA's first lesson");

        // Once per player: the cutscene ledger's seen key of the prologue (synthesis §5.3) keeps a rejoin quiet.
        Assert.Contains(Shared.Story.StoryPackKeys.CutsceneSeen(StoryRegistry.DefaultStoryId, Shared.Story.StoryCutsceneIds.Prologue),
            server.MilestonesForTest("Dreamer"));
    }

    [Fact]
    public void SandboxWithoutStory_SkipsThePrologue_ButStillRunsTheTutorial()
    {
        var config = Config();
        config.Rules.StoryId = "none"; // the world opted out of narrative

        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Sandboxer");

        Assert.DoesNotContain(lines, l => l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == FirstStart); // the tutorial itself is unaffected
    }

    [Fact]
    public void Join_SendsTheVegaJournalSnapshot_AndSkipRefreshesIt()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var journals = new List<VegaJournal>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is VegaJournal j)
            {
                journals.Add(j);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Reader");

        // The join snapshot carries the vega:* milestones (the tutorial's start double-wrote the intro key) — and only
        // those plus the tutorial's done steps (none yet).
        Assert.NotEmpty(journals);
        Assert.Contains("vega:intro", journals.Last().Milestones);
        Assert.DoesNotContain(journals.Last().Milestones, m => !m.StartsWith("vega:", StringComparison.Ordinal));

        // Skipping re-sends the snapshot at once (with the legacy graduation keys older builds read).
        SendAndTick(server, client, new SkipOnboardingIntent());
        Assert.Contains("vega:stage:mine", journals.Last().Milestones);
        Assert.Contains("vega:stage:land", journals.Last().Milestones);
    }

    [Fact]
    public void NewPlayer_StartsWithFood_AndEatingCompletesTheEatStep()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Eater");

        // Startvorrat (option A): a fresh pilot can eat from minute one — berries to hand, rations pre-loaded
        // into the suit dispenser so the low-hunger auto-feed safety net works before any are crafted.
        var p = server.Sessions[1].State;
        Assert.True(p.Inventory.CountOf("berries") > 0, "new players start with berries to eat");
        Assert.True(p.RationStore.CountOf("emergency_ration") > 0, "suit dispenser is pre-loaded with rations");

        // Part 2 (staying alive): the eat step waits behind the read step, then eating one berry completes it.
        SkipChaptersBefore(server, client, "b2");
        Assert.Equal("story.tutorial.b2.eat.chip", lines.Last().ObjectiveKey);
        SendAndTick(server, client, new ConsumeItemIntent { ItemKey = "berries" });

        var milestones = server.MilestonesForTest("Eater");
        Assert.Contains(Shared.Story.StoryPackKeys.StepDone(Tutorial, "b2.2"), milestones);
        Assert.Contains("vega:stage:eat", milestones);
        Assert.Contains(Shared.Story.StoryPackKeys.StepDone(Tutorial, "b2.1"), milestones); // the read step closed with it
        Assert.NotEqual("story.tutorial.b2.eat.chip", lines.Last().ObjectiveKey); // the training moved on
    }

    [Fact]
    public void CreativeWorld_RunsTheTutorial_AndSkipsOnlyTheResearchStep()
    {
        // #1461: a creative world grants every blueprint at join, which used to read as "a veteran save" and silently
        // skipped the whole tutorial — a school browser session has no persistent save, so every session was a first
        // session without a single lesson. Since #2469 the research step is skipped there (world:all_blueprints) and the
        // rest of the basic training runs.
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var config = Config();
        config.CreativeUnlockAllBlueprints = true;
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Kid");

        Assert.Contains(lines, l => l.LineKey == FirstStart);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.veteran");
        var kid = server.Sessions[1];
        Assert.False(kid.Join.Veteran);

        SkipChaptersBefore(server, client, "b5");
        server.RaiseGameEventForTest(kid, Shared.Story.GameEventKeys.ScanPerformed, "flora"); // the scan step
        SendAndTick(server, client, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipStep, StepId = "b5.2" });

        var milestones = server.MilestonesForTest("Kid");
        Assert.Contains(Shared.Story.StoryPackKeys.StepSkipped(Tutorial, "b5.3"), milestones); // nothing to research here
        Assert.Contains("vega:stage:unlock", milestones);
        Assert.DoesNotContain(lines, l => l.LineKey == "story.tutorial.b5.research.start");
        Assert.Contains(Shared.Story.StoryPackKeys.ChapterDone(Tutorial, "b5"), milestones);
    }

    [Fact]
    public void VeteranSave_IsMigratedSilently_WithOneLine()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        repo.Initialize();

        // A save that has clearly played before VEGA's lessons existed: knowledge earned, no onboarding state at all — on a
        // VEGA world, so an older build's story catch-up gave them B0 (#2470: a player without any story key would be a
        // newcomer to the story and get the prologue).
        var vet = new PlayerState { PlayerId = "Vet", Name = "Vet", KnowledgePoints = 12 };
        vet.Milestones.Add(StoryPackKeys.Beat(StoryRegistry.DefaultStoryId, 0));
        repo.SavePlayer(vet);

        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Vet");

        var milestones = server.MilestonesForTest("Vet");
        Assert.Contains(Shared.Story.StoryPackKeys.Done(Tutorial), milestones);
        Assert.Contains(Shared.Story.StoryPackKeys.ChapterSkipped(Tutorial, "b1"), milestones);
        Assert.Contains("vega:stage:mine", milestones); // the legacy graduation, for older builds
        Assert.Contains("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.veteran");
        Assert.DoesNotContain(lines, l => l.PackId == Tutorial && l.LineKey.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Kind == 4); // no prologue for a veteran
        Assert.StartsWith("story.obj.", lines.Last().ObjectiveKey); // the story objective, not a lesson (#1110)
    }

    [Fact]
    public void SkipOnboardingIntent_SkipsTheBasicTraining()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Skipper");

        SendAndTick(server, client, new SkipOnboardingIntent());

        var milestones = server.MilestonesForTest("Skipper");
        Assert.Contains(Shared.Story.StoryPackKeys.Done(Tutorial), milestones);
        Assert.All(new[] { "b1", "b2", "b3", "b4", "b5", "b6" },
            ch => Assert.Contains(Shared.Story.StoryPackKeys.ChapterSkipped(Tutorial, ch), milestones));
        Assert.Contains("vega:stage:trade", milestones);
        Assert.Contains("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.skip");
        Assert.DoesNotContain(lines, l => l.LineKey == "story.tutorial.graduated"); // skipping graduates nobody
        Assert.False(lines.Last().TutorialActive);
        Assert.StartsWith("story.obj.", lines.Last().ObjectiveKey); // post-tutorial the chip shows the story objective (#1110)
    }

    [Fact]
    public void Restart_AfterSkip_RunsTheTutorialAgain_WithoutThePrologue()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Returner");

        SendAndTick(server, client, new SkipOnboardingIntent());
        Assert.Contains(Shared.Story.StoryPackKeys.Done(Tutorial), server.MilestonesForTest("Returner"));

        // The way back: restart replays part 1 (keys stay — gates and conditions keep holding — the replay walks again).
        lines.Clear();
        SendAndTick(server, client, new SkipOnboardingIntent { Restart = true });

        var milestones = server.MilestonesForTest("Returner");
        Assert.Contains(Shared.Story.StoryPackKeys.ChapterReplay(Tutorial, "b1"), milestones);
        Assert.Contains(lines, l => l.LineKey == FirstStart);
        Assert.DoesNotContain(lines, l => l.Kind == 4); // never the prologue (E17)
        Assert.Equal(FirstChip, lines.Last().ObjectiveKey); // back at lesson one
        Assert.True(lines.Last().TutorialActive);
    }

    [Fact]
    public void MemoryFragments_RedeemAboard_PacedWithKnowledgeReward()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Archivist");

        var p = server.Sessions[1].State;
        Assert.True(p.AboardShip, "players start aboard the ship");
        int knowledgeBefore = p.KnowledgePoints;
        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 2));

        // The advisor poll runs at 1 Hz and redemption is paced (~6 s apart): tick well past both.
        for (int i = 0; i < 10; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();

        var milestones = server.MilestonesForTest("Archivist");
        Assert.Contains("vega:mem:1", milestones);
        Assert.Contains("vega:mem:2", milestones);
        Assert.Equal(0, p.Inventory.CountOf("ai_memory_fragment"));
        Assert.Equal(knowledgeBefore + 6, p.KnowledgePoints); // +3 per restored fragment
        Assert.Contains(lines, l => l.LineKey == "vega.mem.1" && l.Kind == 2);
        Assert.Contains(lines, l => l.LineKey == "vega.mem.2" && l.Kind == 2);
    }

    /// <summary>#1104: the arc's finale hands over the Mk3 core's research MATERIALS, not the blueprint — the
    /// 200-KP threshold and the cockpit still gate the actual research.</summary>
    [Fact]
    public void TenthFragment_CompletesTheArc_AndHandsOverTheMk3Parts()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Historian");

        var p = server.Sessions[1].State;
        for (int beat = 1; beat <= 9; beat++)
        {
            p.Milestones.Add("vega:mem:" + beat); // nine beats already restored on earlier worlds
        }

        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 1));
        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();

        Assert.Contains("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.DoesNotContain("ai_core_mk3", p.UnlockedBlueprints); // still has to be researched
        foreach (var part in _content.GetBlueprint("ai_core_mk3")!.UnlockCost)
        {
            Assert.True(p.Inventory.CountOf(part.Item) + server.Ship.Cargo.CountOf(part.Item) >= part.Count, part.Item);
        }

        Assert.Contains(lines, l => l.LineKey == "vega.mem.10" && l.Kind == 2);
        Assert.Contains(lines, l => l.LineKey == "vega.sys.mk3bp");
    }

    /// <summary>With no room for the Mk3 parts the last fragment waits (nothing consumed, one warning line);
    /// making room lets VEGA finish reading it.</summary>
    [Fact]
    public void TenthFragment_WaitsWhileTheHoldIsFull_ThenPaysOut()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Historian");

        var p = server.Sessions[1].State;
        for (int beat = 1; beat <= 9; beat++)
        {
            p.Milestones.Add("vega:mem:" + beat);
        }

        // Every backpack slot but the fragment's holds a full stack of an unrelated material, and the hold too.
        int max = _content.MaxStackOf("stone");
        for (int i = 0; i < p.Inventory.SlotCount; i++)
        {
            p.Inventory.SetSlot(i, new ItemStack("stone", max));
        }

        for (int i = 0; i < server.Ship.Cargo.SlotCount; i++)
        {
            server.Ship.Cargo.SetSlot(i, new ItemStack("stone", max));
        }

        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 1));
        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();
        Assert.DoesNotContain("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.Equal(1, p.Inventory.CountOf("ai_memory_fragment"));                 // not consumed
        Assert.Single(lines.Where(l => l.LineKey == "vega.sys.mk3parts_full"));   // said once, not per tick

        // Make room → the fragment is read and the parts arrive.
        for (int i = 0; i < server.Ship.Cargo.SlotCount; i++)
        {
            server.Ship.Cargo.SetSlot(i, null);
        }

        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();
        Assert.Contains("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.Contains(lines, l => l.LineKey == "vega.sys.mk3bp");
    }

    [Fact]
    public void AiCoreTier_FollowsTheBuiltModules_InPlayerState()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var states = new List<PlayerStateUpdate>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is PlayerStateUpdate u)
            {
                states.Add(u);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Engineer");

        Assert.Equal(1, states.Last().AiCoreTier); // bare VEGA

        var session = server.Sessions[1];
        session.Ships[session.ActiveShipId].Modules.Add("ai_core_mk2");
        // Trigger a fresh authoritative state send (stealth toggle succeeds with a suit WORN, #2110).
        TestGear.Wear(session.State, "stealth_suit");
        client.Send(NetCodec.Encode(new ToggleStealthIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Equal(2, states.Last().AiCoreTier);
    }

    [Fact]
    public void Milestones_PersistAcrossReload()
    {
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config(), _content, serverTransport, repo);
            server.Start();
            JoinAndDrain(server, client, "Saver");
            client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            server.Stop();
        }

        SqliteWorldRepositoryReset();

        using (var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "vega")))
        {
            repo2.Initialize();
            var loaded = repo2.LoadPlayer("Saver");
            Assert.NotNull(loaded);
            Assert.Contains(Shared.Story.StoryPackKeys.Done(Tutorial), loaded!.Milestones);
            Assert.Contains("vega:intro", loaded.Milestones);
            Assert.Contains("vega:stage:land", loaded.Milestones);
        }
    }

    // --- Context tips (#1077): throttled, repeatable situational advice ---

    /// <summary>Ticks the server one simulated second at a time (the advisor poll is 1 Hz), sending a
    /// harmless keep-alive every half minute so the silent-session sweep (90 s) never drops the player.</summary>
    private static void TickSeconds(SvGameServer server, LoopbackClientTransport client, int seconds, Action? eachSecond = null)
    {
        for (int i = 0; i < seconds; i++)
        {
            if (i % 30 == 0)
            {
                client.Send(NetCodec.Encode(new SelectHotbarIntent { Slot = 0 }), DeliveryMode.ReliableOrdered);
            }

            eachSecond?.Invoke();
            server.Tick(1.0);
        }

        client.Poll();
    }

    [Fact]
    public void LampOffTip_WaitsForDwellAndCadence_RepeatsAfterCooldown_AndRetiresOnceLearned()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Nightwalker");

        var session = server.Sessions[1];
        var p = session.State;
        p.AboardShip = false; // on foot on the surface (no starter ship in this config)
        TestGear.Wear(p, "suit_lamp");
        p.Milestones.Add(StoryPackKeys.StepDone(Tutorial, "b2.6")); // #2469: past the tutorial's lamp step, which holds this tip
        server.ResetVegaProbeForTest("Nightwalker");
        // A rocky night is cold and a long night makes hungry — retire the other safety hints up front so
        // only the deliberately triggered O2 hint competes with the lamp tip for the cadence.
        foreach (var other in new[] { "cold", "heat", "hunger", "energy", "medkit" })
        {
            p.Milestones.Add("vega:hint:" + other + "#done");
        }

        lines.Clear();

        // Ticks with the world clock pinned to deep night (a full day is only 600 s here).
        float oxygenPin = 100f;
        // Health is pinned too: since the 2-D pad nudge (#1618) the home pad sits on a colder column, where a
        // rocky night's exposure kills within the 157 s this test ticks — cold is not what it exercises.
        void Night(int seconds) => TickSeconds(server, client, seconds, () => { server.SetDayFractionForTest(0.95); p.Oxygen = oxygenPin; p.Hunger = 100f; p.Health = 100f; });

        // The join-quiet minute: nothing yet, however long the lamp stays off.
        Night(30);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.hint.lamp_off");

        // A first SAFETY hint ignores the cadence entirely (the old teaching moment) …
        oxygenPin = 10f;
        Night(2);
        Assert.Contains(lines, l => l.LineKey == "vega.hint.o2" && l.Kind == 1);
        Assert.Contains("vega:hint:o2", server.MilestonesForTest("Nightwalker"));
        int afterO2 = lines.Count;
        oxygenPin = 100f;

        // … and it starts the 120 s cadence: the lamp tip (dwell 8 s long satisfied) has to wait for it.
        Night(100);
        var diag = server.VegaTipCandidatesForTest("Nightwalker");
        Assert.True(diag.Candidates.Contains("lamp_off"),
            $"lamp_off should be a candidate at night with a lamp carried and off — candidates: [{string.Join(",", diag.Candidates)}], solidAbove={diag.SolidAbove}, lightNear={diag.LightNear}, aboard={diag.Aboard}");
        Assert.DoesNotContain(lines.Skip(afterO2), l => l.LineKey == "vega.hint.lamp_off");
        Night(25);
        var first = Assert.Single(lines, l => l.LineKey == "vega.hint.lamp_off");
        Assert.Equal(1, first.Kind); // first occurrence = advisor kind (goes to the tips log)
        Assert.Contains("vega:hint:lamp_off", server.MilestonesForTest("Nightwalker"));

        // Still dark, lamp still off: the per-tip cooldown (10 min) keeps VEGA quiet …
        Night(60);
        Assert.Single(lines, l => l.LineKey == "vega.hint.lamp_off");

        // … and once it is over the tip repeats (dwell again) as a Kind-5 context repeat with a #2 marker.
        server.SkipVegaTipCooldownsForTest("Nightwalker");
        Night(12);
        var st = server.VegaTipStateForTest("Nightwalker", "lamp_off");
        Assert.True(lines.Count(l => l.LineKey == "vega.hint.lamp_off") == 2,
            $"expected the repeat — count={st.Count} cooldownUntil={st.CooldownUntil} readyAt={st.ReadyAt} since={st.Since} uptime={st.Uptime}; lines=[{string.Join(",", lines.Select(l => l.LineKey + ":" + l.Kind))}]");
        Assert.Equal(5, lines.Last(l => l.LineKey == "vega.hint.lamp_off").Kind);
        Assert.Contains("vega:hint:lamp_off#2", server.MilestonesForTest("Nightwalker"));

        // The player switches the lamp on right after the tip: learned → retired for this save.
        client.Send(NetCodec.Encode(new SetLampIntent { On = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.True(server.LampOnForTest("Nightwalker"));
        Assert.Contains("vega:hint:lamp_off#done", server.MilestonesForTest("Nightwalker"));

        // Lamp off again, cooldown over, still night — VEGA never brings it up again.
        client.Send(NetCodec.Encode(new SetLampIntent { On = false }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.SkipVegaTipCooldownsForTest("Nightwalker");
        Night(15);
        Assert.Equal(2, lines.Count(l => l.LineKey == "vega.hint.lamp_off"));
    }

    [Fact]
    public void VitalsHint_RepeatsWithCooldown_UpToTheCap_AndTheSnapshotCarriesTheMarkers()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var journals = new List<VegaJournal>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is VegaJournal j)
            {
                journals.Add(j);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Gasper");
        var p = server.Sessions[1].State;
        p.AboardShip = false;

        int Fired() => lines.Count(l => l.LineKey == "vega.hint.energy");
        void Starve(int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                p.SuitEnergy = 5f;
                server.Tick(1.0);
            }

            client.Poll();
        }

        Starve(2);
        Assert.Equal(1, Fired());
        Starve(5);
        Assert.Equal(1, Fired()); // inside the 15-min cooldown: silent

        for (int repeat = 2; repeat <= 4; repeat++)
        {
            server.SkipVegaTipCooldownsForTest("Gasper");
            Starve(2);
            Assert.Equal(repeat, Fired());
            Assert.Equal(5, lines.Last(l => l.LineKey == "vega.hint.energy").Kind);
            Assert.Contains("vega:hint:energy#" + repeat, server.MilestonesForTest("Gasper"));
        }

        server.SkipVegaTipCooldownsForTest("Gasper");
        Starve(3);
        Assert.Equal(4, Fired()); // the cap: never a fifth time

        // The join snapshot carries the repeat markers verbatim (the client-side journal filters them —
        // covered by VegaTextTests.JournalKeys_IgnoresContextTipRepeatMarkers).
        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        var snapshot = journals.Last().Milestones;
        Assert.Contains("vega:hint:energy", snapshot);
        Assert.Contains("vega:hint:energy#2", snapshot);
    }

    [Fact]
    public void ContextTips_ProbeFindsRareOre_AndOpportunityTipsWaitForTheTutorialGate()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        // No Guardian machines on this world: a scan-drone that spawns 35–50 blocks out reaches the player within
        // the 75 s quiet window and shoots them dead (2/s), and the respawn puts them aboard — where the ore tip
        // can never fire. It only ever passed because the fauna spawner's cave probe happened to LOAD the drone's
        // path columns (#1719 stopped that), and a loaded rocky column reads as a wall to a machine (#1482).
        var config = Config();
        config.Rules.PlanetEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Prospector");
        var session = server.Sessions[1];
        var p = session.State;
        p.AboardShip = false;
        server.SetDayFractionForTest(0.5); // daylight — no lamp talk

        // Plant an exposed vein of the rarest ore of the starter world two blocks in front of the player.
        int px = (int)Math.Floor(p.Position.X), py = (int)Math.Floor(p.Position.Y), pz = (int)Math.Floor(p.Position.Z);
        var gold = _content.GetBlock("gold_ore")!;
        server.World.SetBlock(new Vector3i(px + 2, py, pz), gold.NumericId);
        server.World.SetBlock(new Vector3i(px + 2, py, pz + 1), gold.NumericId);
        server.ResetVegaProbeForTest("Prospector");

        // Fresh save: the tutorial's opportunityTips gate (eat + mine + craft) is closed — the tips stay quiet even past
        // the join-quiet minute (#2469: the same moment the old "scan" stage marked).
        TickSeconds(server, client, 75);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.hint.rare_ore_near");
        Assert.False(server.TutorialGateForTest(session, StoryGateNames.OpportunityTips));

        // Skip the basic training → the gate opens and the same situation produces the tip, naming the ore.
        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.SkipVegaTipCooldownsForTest("Prospector");
        server.ResetVegaProbeForTest("Prospector");
        TickSeconds(server, client, 15);
        var tip = Assert.Single(lines, l => l.LineKey == "vega.hint.rare_ore_near");
        Assert.False(string.IsNullOrEmpty(tip.LineArg));
        Assert.Contains("vega:hint:rare_ore_near", server.MilestonesForTest("Prospector"));

        // Mining an ore right after the tip counts as learned.
        server.VegaTipLearnedForTest("Prospector", "rare_ore_near");
        Assert.Contains("vega:hint:rare_ore_near#done", server.MilestonesForTest("Prospector"));
    }

    /// <summary>#1594: a first-time player 110 m from the hull did not know the blue compass blip was the ship.
    /// On foot and far from the landed ship VEGA says so once the dwell has passed; aboard, or near the hull,
    /// the tip is not a candidate, and walking back right after the tip retires it for the save.</summary>
    [Fact]
    public void ShipFarTip_FiresOnFootFarFromTheLandedShip_NotAboard_AndRetiresOnWalkingBack()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var config = Config();
        config.PlaceStarterShip = true; // a parked hull to be far from
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Wanderer");

        var session = server.Sessions[1];
        var p = session.State;
        var tank = p.RespawnPoint; // the ship's heal tank — the point the compass blip and the tip measure against
        var bounds = server.LandedShipBoundsForTest(p.PlayerId);
        Assert.NotEqual(Vector3i.Zero, bounds.Size);
        foreach (var other in new[] { "o2", "cold", "heat", "hunger", "energy", "medkit" })
        {
            p.Milestones.Add("vega:hint:" + other + "#done"); // only the deliberately triggered tip competes
        }

        p.Milestones.Add(StoryPackKeys.StepDone(Tutorial, "b2.5")); // #2469: past the tutorial's way-home step, which holds this tip

        // Just outside the hull (a few metres past its X extent): on foot but close — not a candidate.
        var near = new Vector3f(bounds.Origin.X + bounds.Size.X + 3f, tank.Y, tank.Z);
        p.AboardShip = false;
        p.Position = near;
        Assert.DoesNotContain("ship_far", server.VegaTipCandidatesForTest("Wanderer").Candidates);

        // 200 m out on foot: a candidate — unless the player is aboard.
        var far = new Vector3f(tank.X + 200f, tank.Y, tank.Z);
        p.Position = far;
        Assert.Contains("ship_far", server.VegaTipCandidatesForTest("Wanderer").Candidates);
        p.AboardShip = true;
        Assert.DoesNotContain("ship_far", server.VegaTipCandidatesForTest("Wanderer").Candidates);
        p.AboardShip = false;

        // The 30 s dwell: nothing after 20 s, the advisor line (Kind 1, first occurrence) after 40 s.
        server.SkipVegaTipCooldownsForTest("Wanderer");
        lines.Clear();
        void Walk(int seconds) => TickSeconds(server, client, seconds, () =>
        {
            p.Position = far;
            p.AboardShip = false;
            p.Oxygen = 100f;
            p.Hunger = 100f;
        });

        Walk(20);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.hint.ship_far");
        Walk(20);
        Assert.Contains(lines, l => l.LineKey == "vega.hint.ship_far" && l.Kind == 1);
        Assert.Contains("vega:hint:ship_far", server.MilestonesForTest("Wanderer"));

        // Walking back to the hull right after the tip is the reaction VEGA hoped for: retired for this save.
        p.Position = near;
        server.Tick(1.0);
        Assert.Contains("vega:hint:ship_far#done", server.MilestonesForTest("Wanderer"));
    }

    [Fact]
    public void TierGateTip_ArmsOnARefusedSwing_ThenNamesTheBlockAndTheTool()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Rookie");

        var session = server.Sessions[1];
        var p = session.State;
        p.AboardShip = false;
        // Retire the vitals hints: they outrank an equipment tip and would win every cadence slot here.
        foreach (string other in new[] { "o2", "energy", "hunger", "cold", "heat", "medkit" })
        {
            p.Milestones.Add("vega:hint:" + other + "#done");
        }

        // A Machine Housing within reach, and the starter Basic Drill (tier 1) that cannot open it.
        int px = (int)Math.Floor(p.Position.X);
        int pz = (int)Math.Floor(p.Position.Z);
        var pos = new Vector3i(px, (int)Math.Floor(p.Position.Y) - 1, pz);
        server.World.SetBlock(pos, _content.GetBlock("machine_block")!.NumericId);

        Assert.DoesNotContain("tier_gate", server.VegaTipCandidatesForTest("Rookie").Candidates);

        server.MineBlockOnce("Rookie", pos.X, pos.Y, pos.Z);

        Assert.False(server.World.GetBlock(pos).IsAir, "A tier-1 drill must not open a tier-2 block.");
        // Collecting candidates must NOT disarm the tip — it runs every tick, but only one tip fires per
        // cadence slot, so a losing slot used to swallow the teaching moment entirely.
        Assert.Contains("tier_gate", server.VegaTipCandidatesForTest("Rookie").Candidates);
        Assert.Contains("tier_gate", server.VegaTipCandidatesForTest("Rookie").Candidates);

        TickSeconds(server, client, 200, () => { p.Oxygen = 100f; p.Hunger = 100f; p.Health = 100f; });

        var tip = Assert.Single(lines, l => l.LineKey == "vega.hint.tier_gate");
        Assert.Equal(1, tip.Kind); // first occurrence = advisor line, appended to the tips log
        // The two {n} slots travel packed in one LineArg (VegaText.ArgSeparator on the client side).
        Assert.Equal(new[] { "Machine Housing", "Titanium Drill" }, tip.LineArg.Split('\u001f'));
        Assert.Contains("vega:hint:tier_gate", server.MilestonesForTest("Rookie"));

        // Spoken once = disarmed: idling on afterwards must not repeat it off the same stale arming.
        TickSeconds(server, client, 200, () => { p.Oxygen = 100f; p.Hunger = 100f; p.Health = 100f; });
        Assert.Single(lines, l => l.LineKey == "vega.hint.tier_gate");
    }

    private static void SqliteWorldRepositoryReset()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
