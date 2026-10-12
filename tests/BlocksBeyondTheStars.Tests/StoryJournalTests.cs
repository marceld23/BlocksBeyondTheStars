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
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2471 — step 12 of the story packs (#2459): the Journal and the objective tracker. The two new messages (tag 299
/// <see cref="ObjectiveList"/>, tag 300 <see cref="StoryPackStates"/>) round-trip and go only to <c>packs1</c> clients — on the
/// join, after the player's own action, on request, and otherwise coalesced (at most once a second, a progress tick never
/// re-sends the stories); the server builds the Journal's view (a tutorial in progress, offers, skipped chapters, the world
/// story before and after its start, locked chapters with what they need); the objective list follows the chip order with
/// one row per story and marks the pin; every <see cref="StoryPackIntent"/> action is validated and served through a held
/// world; a started player's join catch-up collapses several missed beats into one line (B09); the world story's non-beat
/// narrator lines reach the Journal's notes; and the dock lesson's marker points at the nearest station in space and landed
/// (B6.4).
/// </summary>
public sealed class StoryJournalTests : IDisposable
{
    private const string R = StoryPackFixtures.Runtime;
    private const string T = StoryPackFixtures.Tutorial;

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public StoryJournalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_journal_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>Records every server send so a test can assert who was told what.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }

        public List<T> To<T>(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<T>().ToList();

        public List<ShipAiLine> Lines(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).ToList();
    }

    private SvGameServer Boot(string world, RecordingTransport transport, GameContent content, Action<ServerConfig>? configure = null)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            MaxPlayers = 6,
            PlaceStarterShip = false,
        };
        configure?.Invoke(config);
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A world with the VEGA story, the tutorial test pack and the side test pack.</summary>
    private (SvGameServer Server, RecordingTransport Transport) TutorialWorld(string world, Action<ServerConfig>? configure = null)
    {
        var transport = new RecordingTransport();
        return (Boot(world, transport, StoryPackFixtures.ContentWith(T, R), configure), transport);
    }

    /// <summary>A world without a world story and only the side test pack.</summary>
    private (SvGameServer Server, RecordingTransport Transport) SideWorld(string world)
    {
        var transport = new RecordingTransport();
        return (Boot(world, transport, StoryPackFixtures.ContentWith(R), c => c.Rules.StoryId = StoryRegistry.NoneStoryId), transport);
    }

    private static PlayerSession Join(SvGameServer server, int conn, string name, bool packs = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = packs ? new[] { ClientFeatures.Packs1 } : Array.Empty<string>(),
        }));
        return server.Sessions[conn];
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Act(SvGameServer server, PlayerSession who, string pack, string action, string chapter = "", string step = "")
        => Deliver(server, who, new StoryPackIntent { PackId = pack, Action = action, ChapterId = chapter, StepId = step });

    private static void Event(SvGameServer server, PlayerSession p, string key, string arg = "", int amount = 1)
        => server.RaiseGameEventForTest(p, key, arg, amount);

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static NetStoryPack PackIn(StoryPackStates states, string id) => Assert.Single(states.Packs, p => p.Id == id);

    private static NetStoryChapter ChapterIn(NetStoryPack pack, string id) => Assert.Single(pack.Chapters, c => c.Id == id);

    // ---------------- Wire ----------------

    [Fact]
    public void TheJournalMessages_RoundTrip_AndTheNewFieldsDefaultEmpty()
    {
        var states = Assert.IsType<StoryPackStates>(NetCodec.Decode(NetCodec.Encode(new StoryPackStates
        {
            TrackedPackId = T,
            TutorialPolicy = "OptIn",
            Packs = new[]
            {
                new NetStoryPack
                {
                    Id = T, Kind = "tutorial", Scope = "player", NameKey = "n", ShortNameKey = "s", DescriptionKey = "d",
                    Enabled = true, Started = true, Done = false, OptedOut = false, Tracked = true, Startable = false, Blocked = false,
                    MandatoryRunning = true, Badge = false,
                    Chapters = new[] { new NetStoryChapter { Id = "t3", TitleKey = "tk", SummaryKey = "sk", State = StoryChapterStates.Locked, Optional = true, Skippable = true, RequiresKeys = new[] { "blueprint.x.name" } } },
                    Steps = new[] { new NetStoryStep { Id = "t1.1", ChapterId = "t1", ChipKey = "c", StartKey = "st", MoreGuide = "welcome", Tier = "must", Progress = 1, Target = 2, Index = 1, Count = 3, Skippable = true, Extra = false, OnChip = true } },
                },
            },
        })));
        Assert.Equal(T, states.TrackedPackId);
        Assert.Equal("OptIn", states.TutorialPolicy);
        var pack = Assert.Single(states.Packs);
        Assert.True(pack.Tracked && pack.MandatoryRunning && pack.Started);
        Assert.Equal(new[] { "blueprint.x.name" }, Assert.Single(pack.Chapters).RequiresKeys);
        var step = Assert.Single(pack.Steps);
        Assert.Equal(("t1.1", 1, 2, 1, 3, "welcome", true), (step.Id, step.Progress, step.Target, step.Index, step.Count, step.MoreGuide, step.OnChip));

        var list = Assert.IsType<ObjectiveList>(NetCodec.Decode(NetCodec.Encode(new ObjectiveList
        {
            Items = new[] { new NetObjective { Source = ObjectiveSources.Tutorial, PackId = T, StepId = "t1.1", Key = "k", Arg = "a", Progress = 1, Target = 2, Pinned = true } },
        })));
        var row = Assert.Single(list.Items);
        Assert.Equal((ObjectiveSources.Tutorial, T, "t1.1", "k", "a", 1, 2, true), (row.Source, row.PackId, row.StepId, row.Key, row.Arg, row.Progress, row.Target, row.Pinned));

        var line = Assert.IsType<ShipAiLine>(NetCodec.Decode(NetCodec.Encode(new ShipAiLine { SpaceWaypointId = "sys0-st-local" })));
        Assert.Equal("sys0-st-local", line.SpaceWaypointId);
        Assert.Equal(string.Empty, new ShipAiLine().SpaceWaypointId);
        var story = Assert.IsType<StoryStateMessage>(NetCodec.Decode(NetCodec.Encode(new StoryStateMessage { NoteKeys = new[] { "story.vega.guardian_revealed" } })));
        Assert.Equal(new[] { "story.vega.guardian_revealed" }, story.NoteKeys);
        Assert.Empty(new StoryStateMessage().NoteKeys);
        Assert.Equal(3, ObjectiveList.MaxItems);
    }

    [Fact]
    public void TheJoin_PushesBothMessages_ToACurrentClient_AndNothingToAnOlderOne()
    {
        var (server, transport) = TutorialWorld("join_push");
        var current = Join(server, 1, "Current");
        var older = Join(server, 2, "Older", packs: false);

        Assert.Single(transport.To<StoryPackStates>(current));
        Assert.Single(transport.To<ObjectiveList>(current));
        Assert.Empty(transport.To<StoryPackStates>(older));
        Assert.Empty(transport.To<ObjectiveList>(older));
    }

    // ---------------- The Journal's view ----------------

    [Fact]
    public void TheStates_ShowATutorialInProgress_TheWaitingWorldStory_AndTheSidePack()
    {
        var (server, _) = TutorialWorld("states_progress");
        var p = Join(server, 1, "Pilot");
        var states = server.StoryPackStatesForTest(p);

        Assert.Equal(new[] { T, StoryRegistry.DefaultStoryId, R }, states.Packs.Select(x => x.Id).ToArray()); // tutorial, world, others
        Assert.Equal("Auto", states.TutorialPolicy);

        var tutorial = PackIn(states, T);
        Assert.Equal(("tutorial", "player", "story.tutorial_test.name", "story.tutorial_test.short"),
            (tutorial.Kind, tutorial.Scope, tutorial.NameKey, tutorial.ShortNameKey));
        Assert.True(tutorial.Enabled && tutorial.Started && tutorial.MandatoryRunning);
        Assert.False(tutorial.Done || tutorial.Badge || tutorial.OptedOut || tutorial.Blocked);
        Assert.Equal(StoryChapterStates.Active, ChapterIn(tutorial, "t1").State);
        Assert.Equal(StoryChapterStates.Upcoming, ChapterIn(tutorial, "t2").State);
        Assert.Equal(StoryChapterStates.Available, ChapterIn(tutorial, "t3").State); // opt-in, no requirement
        Assert.True(ChapterIn(tutorial, "t3").Optional);
        var step = Assert.Single(tutorial.Steps);
        Assert.Equal(("t1.1", "t1", "story.tutorial_test.t11.chip", 1, 3, 2, true, false),
            (step.Id, step.ChapterId, step.ChipKey, step.Index, step.Count, step.Target, step.OnChip, step.Extra));

        var world = PackIn(states, StoryRegistry.DefaultStoryId);
        Assert.Equal(StoryRegistry.ScopeWorld, world.Scope);
        Assert.Equal("story.vega.short", world.ShortNameKey);
        Assert.False(world.Started); // held behind the basic training
        Assert.True(world.Startable); // "Start the story now"
        Assert.Empty(world.Chapters); // the arc stays on the story state

        var side = PackIn(states, R);
        Assert.True(side.Started && side.Enabled);
        Assert.Equal(StoryChapterStates.Active, ChapterIn(side, "a").State);
    }

    [Fact]
    public void TheStates_ShowSkippedChapters_AnOffer_ADecline_AndNoBadgeForASkip()
    {
        var (server, _) = TutorialWorld("states_offer");
        var p = Join(server, 1, "Pilot");
        Act(server, p, T, StoryPackActions.SkipPack);
        server.TickStoryPacksForTest(p); // the deep dive is offered once the basics are through

        var tutorial = PackIn(server.StoryPackStatesForTest(p), T);
        Assert.Equal(StoryChapterStates.Skipped, ChapterIn(tutorial, "t1").State);
        Assert.Equal(StoryChapterStates.Skipped, ChapterIn(tutorial, "t2").State);
        Assert.Equal(StoryChapterStates.Offered, ChapterIn(tutorial, "t3").State);
        Assert.True(tutorial.Done);
        Assert.False(tutorial.Badge); // skipped, not played
        Assert.False(tutorial.MandatoryRunning);

        Act(server, p, T, StoryPackActions.DeclineChapter, "t3");
        Assert.Equal(StoryChapterStates.Declined, ChapterIn(PackIn(server.StoryPackStatesForTest(p), T), "t3").State);
        Act(server, p, T, StoryPackActions.StartChapter, "t3");
        Assert.Equal(StoryChapterStates.Active, ChapterIn(PackIn(server.StoryPackStatesForTest(p), T), "t3").State);
    }

    [Fact]
    public void ABadge_IsEarnedByPlayingTheMandatoryPartThrough()
    {
        var (server, _) = TutorialWorld("states_badge");
        var p = Join(server, 1, "Pilot");
        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Event(server, p, GameEventKeys.BuildAny);
        Event(server, p, GameEventKeys.CraftAny);
        Event(server, p, GameEventKeys.EatAny);

        var tutorial = PackIn(server.StoryPackStatesForTest(p), T);
        Assert.True(tutorial.Done);
        Assert.True(tutorial.Badge);
        Assert.Equal(StoryChapterStates.Done, ChapterIn(tutorial, "t1").State);
    }

    [Fact]
    public void TheWorldStory_IsStartedAndPinnable_AfterTheTrackStarts()
    {
        var (server, _) = TutorialWorld("states_vega");
        var p = Join(server, 1, "Pilot");
        Act(server, p, StoryRegistry.DefaultStoryId, StoryPackActions.StartPack);

        var world = PackIn(server.StoryPackStatesForTest(p), StoryRegistry.DefaultStoryId);
        Assert.True(world.Started);
        Assert.False(world.Startable);
        Assert.False(world.Done || world.Badge);
        Act(server, p, StoryRegistry.DefaultStoryId, StoryPackActions.Track);
        Assert.True(PackIn(server.StoryPackStatesForTest(p), StoryRegistry.DefaultStoryId).Tracked);
    }

    [Fact]
    public void ALockedChapter_NamesWhatItNeeds()
    {
        var content = StoryPackFixtures.ContentWith(T, R);
        var tutorial = content.Stories[T];
        var deepDive = tutorial.Chapters.Single(c => c.Id == "t3");
        deepDive.Requires.Add("blueprint:terrain_scanner");
        deepDive.Requires.Add("chapter:tutorial_test:t2");
        var transport = new RecordingTransport();
        var server = Boot("states_locked", transport, content);
        var p = Join(server, 1, "Pilot");

        var t3 = ChapterIn(PackIn(server.StoryPackStatesForTest(p), T), "t3");
        Assert.Equal(StoryChapterStates.Locked, t3.State);
        Assert.Contains("story.tutorial_test.ch.t2", t3.RequiresKeys);
        Assert.Contains(content.Blueprints["terrain_scanner"].NameKey, t3.RequiresKeys);
    }

    [Fact]
    public void AnOptInWorld_ListsTheTutorialAsStartable_AndAnOffWorldAsBlocked()
    {
        var (optIn, _) = TutorialWorld("states_optin", c => c.Rules.TutorialPolicy = TutorialPolicy.OptIn);
        var a = Join(optIn, 1, "A");
        var tutorial = PackIn(optIn.StoryPackStatesForTest(a), T);
        Assert.False(tutorial.Started || tutorial.Enabled || tutorial.Blocked);
        Assert.True(tutorial.Startable);

        var (off, _) = TutorialWorld("states_off", c => c.Rules.TutorialPolicy = TutorialPolicy.Off);
        var b = Join(off, 1, "B");
        var blocked = PackIn(off.StoryPackStatesForTest(b), T);
        Assert.True(blocked.Blocked);
        Assert.False(blocked.Startable || blocked.Enabled);
        Assert.Equal("Off", off.StoryPackStatesForTest(b).TutorialPolicy);
    }

    // ---------------- The objective list ----------------

    [Fact]
    public void TheObjectiveList_FollowsTheChipOrder_OneRowPerStory_AndMarksThePin()
    {
        var (server, _) = TutorialWorld("list_order");
        var p = Join(server, 1, "Pilot");

        var list = server.ObjectiveListForTest(p);
        Assert.Equal(new[] { T, R }, list.Items.Select(i => i.PackId).ToArray()); // basic training first, the side story after
        Assert.Equal(server.ObjectiveKeyForTest("Pilot"), list.Items[0].Key); // the first row is the chip
        Assert.Equal(ObjectiveSources.Tutorial, list.Items[0].Source);
        Assert.Equal("t1.1", list.Items[0].StepId);
        Assert.Equal(2, list.Items[0].Target);
        Assert.All(list.Items, i => Assert.False(i.Pinned));

        Act(server, p, R, StoryPackActions.Track);
        list = server.ObjectiveListForTest(p);
        Assert.Equal(new[] { R, T }, list.Items.Select(i => i.PackId).ToArray()); // a pin beats basic training (rank 2)
        Assert.True(list.Items[0].Pinned);
        Assert.False(list.Items[1].Pinned);

        // With the training skipped and the story started, VEGA's lead comes in — labelled with the world story's id.
        Act(server, p, R, StoryPackActions.Untrack);
        Act(server, p, T, StoryPackActions.SkipPack);
        list = server.ObjectiveListForTest(p);
        Assert.Equal(StoryRegistry.DefaultStoryId, list.Items[0].PackId);
        Assert.StartsWith(StoryLeads.ObjPrefix, list.Items[0].Key); // #2475: the chapter lead's key
        Assert.Equal(StoryRegistry.DefaultStoryId, server.ObjectivePackForTest("Pilot"));
        Assert.True(list.Items.Length <= ObjectiveList.MaxItems);
        Assert.Equal(list.Items.Length, list.Items.Select(i => i.PackId).Distinct().Count());
    }

    // ---------------- Coalescing ----------------

    [Fact]
    public void AProgressTick_SendsTheObjectiveList_NeverTheStates_AndAtMostOnceASecond()
    {
        var (server, transport) = TutorialWorld("coalesce");
        var p = Join(server, 1, "Pilot");
        server.AdvanceUptimeForTest(5);
        server.TickJournalForTest(); // whatever the join left behind
        server.AdvanceUptimeForTest(5);
        transport.Sent.Clear();

        Event(server, p, GameEventKeys.MineAny); // t1.1: 1 of 2
        Assert.True(server.JournalDirtyForTest(p));
        Assert.Empty(transport.To<ObjectiveList>(p)); // nothing per event — the tick sends
        server.TickJournalForTest();
        var list = Assert.Single(transport.To<ObjectiveList>(p));
        Assert.Equal(1, list.Items[0].Progress);
        Assert.Empty(transport.To<StoryPackStates>(p)); // a progress tick alone never re-sends the stories

        transport.Sent.Clear();
        Event(server, p, GameEventKeys.MineAny); // t1.1 done → t1.2 active: the stories change
        server.TickJournalForTest();
        Assert.Empty(transport.To<StoryPackStates>(p)); // less than a second after the last push
        Assert.True(server.JournalDirtyForTest(p));
        server.AdvanceUptimeForTest(1.1);
        server.TickJournalForTest();
        var states = Assert.Single(transport.To<StoryPackStates>(p));
        Assert.Equal("t1.2", Assert.Single(PackIn(states, T).Steps, s => s.OnChip).Id);
        Assert.False(server.JournalDirtyForTest(p));

        transport.Sent.Clear();
        server.AdvanceUptimeForTest(5);
        server.TickJournalForTest();
        Assert.Empty(transport.To<StoryPackStates>(p)); // nothing changed, nothing sent
        Assert.Empty(transport.To<ObjectiveList>(p));
    }

    [Fact]
    public void APlayersOwnAction_IsAnsweredAtOnce_AndARequestSendsBothMessages()
    {
        var (server, transport) = TutorialWorld("action_push");
        var p = Join(server, 1, "Pilot");
        transport.Sent.Clear();

        Act(server, p, T, StoryPackActions.SkipStep, step: "t1.1");
        var states = Assert.Single(transport.To<StoryPackStates>(p)); // no tick needed
        Assert.DoesNotContain(PackIn(states, T).Steps, s => s.Id == "t1.1");

        transport.Sent.Clear();
        Act(server, p, string.Empty, StoryPackActions.RequestStates);
        Assert.Single(transport.To<StoryPackStates>(p));
        Assert.Single(transport.To<ObjectiveList>(p));
    }

    // ---------------- Every action, validated ----------------

    [Fact]
    public void OptOut_AndOptIn_SwitchThePack_AndOptInStartsAnOptInWorldsTutorial()
    {
        var (server, _) = TutorialWorld("opt");
        var p = Join(server, 1, "Pilot");
        Act(server, p, T, StoryPackActions.OptOut);
        Assert.True(Has(p, StoryPackKeys.OptOut(T)));
        Assert.True(PackIn(server.StoryPackStatesForTest(p), T).OptedOut);
        Assert.NotEqual(T, server.ObjectivePackForTest("Pilot")); // off the chip
        Act(server, p, T, StoryPackActions.OptIn);
        Assert.False(Has(p, StoryPackKeys.OptOut(T)));
        Assert.Equal(T, server.ObjectivePackForTest("Pilot"));

        var (optIn, _) = TutorialWorld("opt_in_world", c => c.Rules.TutorialPolicy = TutorialPolicy.OptIn);
        var q = Join(optIn, 1, "Q");
        Assert.False(Has(q, StoryPackKeys.Started(T)));
        Act(optIn, q, T, StoryPackActions.OptIn); // the Settings' "Tutorial: on"
        Assert.True(Has(q, StoryPackKeys.Started(T)));
        Assert.True(PackIn(optIn.StoryPackStatesForTest(q), T).Enabled);
    }

    [Fact]
    public void RestartPack_ReplaysTheMandatoryChapters_AndIsRefusedWhileTheWorldHasTheTutorialOff()
    {
        var (server, _) = TutorialWorld("restart_pack");
        var p = Join(server, 1, "Pilot");
        Act(server, p, T, StoryPackActions.SkipPack);
        Act(server, p, T, StoryPackActions.RestartPack);
        var tutorial = PackIn(server.StoryPackStatesForTest(p), T);
        Assert.Equal(StoryChapterStates.Replay, ChapterIn(tutorial, "t1").State);
        Assert.Equal("t1.1", Assert.Single(tutorial.Steps, s => s.OnChip).Id);

        var (off, _) = TutorialWorld("restart_off", c => c.Rules.TutorialPolicy = TutorialPolicy.Off);
        var q = Join(off, 1, "Q");
        Act(off, q, T, StoryPackActions.RestartPack);
        Assert.False(Has(q, StoryPackKeys.Started(T)));
    }

    [Fact]
    public void SkipStep_IsServedForARunningStep_AndRefusedWhileThePackIsOff()
    {
        var (server, _) = TutorialWorld("skip_step");
        var p = Join(server, 1, "Pilot");
        Act(server, p, T, StoryPackActions.OptOut);
        Act(server, p, T, StoryPackActions.SkipStep, step: "t1.1");
        Assert.False(Has(p, StoryPackKeys.StepSkipped(T, "t1.1"))); // switched off: nothing to skip

        Act(server, p, T, StoryPackActions.OptIn);
        Act(server, p, T, StoryPackActions.SkipStep, step: "t1.1");
        Assert.True(Has(p, StoryPackKeys.StepSkipped(T, "t1.1")));
        Act(server, p, T, StoryPackActions.SkipStep, step: "nope");
        Act(server, p, "no_such_pack", StoryPackActions.SkipStep, step: "t1.2");
        Assert.False(Has(p, StoryPackKeys.StepSkipped(T, "t1.2")));
    }

    [Fact]
    public void SkipChapter_IsRefusedForAFinishedChapter_AndDecline_ForAMandatoryOne()
    {
        var (server, _) = TutorialWorld("skip_chapter");
        var p = Join(server, 1, "Pilot");
        Act(server, p, T, StoryPackActions.DeclineChapter, "t1");
        Assert.False(Has(p, StoryPackKeys.ChapterDeclined(T, "t1")));
        Act(server, p, T, StoryPackActions.SkipChapter, "t2"); // not reached yet — only a running part is skipped
        Assert.False(Has(p, StoryPackKeys.ChapterSkipped(T, "t2")));
        Act(server, p, T, StoryPackActions.SkipChapter, "t1");
        Assert.True(Has(p, StoryPackKeys.ChapterSkipped(T, "t1")));
        int before = p.State.Milestones.Count;
        Act(server, p, T, StoryPackActions.SkipChapter, "t1"); // through: nothing left to skip
        Assert.Equal(before, p.State.Milestones.Count);
    }

    [Fact]
    public void RestartChapter_StartsARunningChapterOver_AndItCompletesForReal()
    {
        var (server, transport) = TutorialWorld("restart_running");
        var p = Join(server, 1, "Pilot");
        Event(server, p, GameEventKeys.MineAny, amount: 2); // t1.1 done, t1.2 runs
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "t1.1")));
        transport.Sent.Clear();

        Act(server, p, T, StoryPackActions.RestartChapter, "t1");
        Assert.True(Has(p, StoryPackKeys.ChapterReplay(T, "t1")));
        Assert.Equal("t1.1", server.ChipStepForTest(p, T)); // from the first step again
        Assert.Contains(transport.Lines(p), l => l.LineKey == "story.tutorial_test.t11.start");
        Assert.Equal(StoryChapterStates.Replay, ChapterIn(PackIn(server.StoryPackStatesForTest(p), T), "t1").State);

        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Event(server, p, GameEventKeys.BuildAny); // t1.2 — first done inside the replay
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "t1.2")));
        Event(server, p, GameEventKeys.CraftAny);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "t1"))); // the chapter completes for real
        Assert.False(Has(p, StoryPackKeys.ChapterReplay(T, "t1")));
        Assert.Equal("t2.1", server.ChipStepForTest(p, T));
    }

    [Fact]
    public void ReplayChapter_IsOnlyForAFinishedChapter()
    {
        var (server, _) = TutorialWorld("replay_chapter");
        var p = Join(server, 1, "Pilot");
        Act(server, p, T, StoryPackActions.ReplayChapter, "t1"); // running — nothing to replay
        Assert.False(Has(p, StoryPackKeys.ChapterReplay(T, "t1")));
        Act(server, p, T, StoryPackActions.SkipChapter, "t1");
        Act(server, p, T, StoryPackActions.ReplayChapter, "t1");
        Assert.True(Has(p, StoryPackKeys.ChapterReplay(T, "t1")));
        Assert.True(Has(p, StoryPackKeys.ChapterSkipped(T, "t1"))); // its key stays: conditions keep holding
    }

    [Fact]
    public void StartPack_StartsAnOptInTutorial_AndUnknownWordsChangeNothing()
    {
        var (server, _) = TutorialWorld("start_pack", c => c.Rules.TutorialPolicy = TutorialPolicy.OptIn);
        var p = Join(server, 1, "Pilot");
        int before = p.State.Milestones.Count;
        Act(server, p, T, "explode");
        Act(server, p, T, string.Empty);
        Assert.Equal(before, p.State.Milestones.Count);
        Act(server, p, T, StoryPackActions.StartPack);
        Assert.True(Has(p, StoryPackKeys.Started(T)));
    }

    // ---------------- The pause whitelist (dispatch level) ----------------

    [Theory]
    [InlineData(StoryPackActions.Track)]
    [InlineData(StoryPackActions.Untrack)]
    [InlineData(StoryPackActions.OptOut)]
    [InlineData(StoryPackActions.OptIn)]
    [InlineData(StoryPackActions.SkipPack)]
    [InlineData(StoryPackActions.StartPack)]
    [InlineData(StoryPackActions.RestartPack)]
    [InlineData(StoryPackActions.SkipStep)]
    [InlineData(StoryPackActions.SkipChapter)]
    [InlineData(StoryPackActions.RestartChapter)]
    [InlineData(StoryPackActions.ReplayChapter)]
    [InlineData(StoryPackActions.StartChapter)]
    [InlineData(StoryPackActions.DeclineChapter)]
    [InlineData(StoryPackActions.RequestStates)]
    [InlineData(StoryPackActions.ReplayCutscene)]
    public void WhileHeld_EveryJournalAction_IsServed(string action)
    {
        var (server, transport) = SideWorld("held_" + action);
        var p = Join(server, 1, "Solo");
        if (action is StoryPackActions.ReplayChapter or StoryPackActions.Untrack)
        {
            Act(server, p, R, action == StoryPackActions.ReplayChapter ? StoryPackActions.SkipChapter : StoryPackActions.Track, "a");
        }

        if (action is StoryPackActions.StartChapter or StoryPackActions.DeclineChapter)
        {
            p.State.Milestones.Add(StoryPackKeys.ChapterOffered(R, "c")); // an offered deep dive
        }

        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);
        transport.Sent.Clear();
        string tracked = p.State.TrackedStoryId;
        string chapter = action is StoryPackActions.StartChapter or StoryPackActions.DeclineChapter ? "c" : "a";
        Act(server, p, R, action, chapter, "a.1");

        bool served = action switch
        {
            StoryPackActions.Track => p.State.TrackedStoryId == R,
            StoryPackActions.Untrack => tracked == R && p.State.TrackedStoryId.Length == 0,
            StoryPackActions.OptOut => Has(p, StoryPackKeys.OptOut(R)),
            StoryPackActions.SkipPack => Has(p, StoryPackKeys.Done(R)),
            StoryPackActions.SkipStep => Has(p, StoryPackKeys.StepSkipped(R, "a.1")),
            StoryPackActions.SkipChapter => Has(p, StoryPackKeys.ChapterSkipped(R, "a")),
            StoryPackActions.RestartChapter or StoryPackActions.ReplayChapter => Has(p, StoryPackKeys.ChapterReplay(R, "a")),
            StoryPackActions.StartChapter => Has(p, StoryPackKeys.ChapterActive(R, "c")),
            StoryPackActions.DeclineChapter => Has(p, StoryPackKeys.ChapterDeclined(R, "c")),
            StoryPackActions.RequestStates => transport.To<StoryPackStates>(p).Count == 1,
            // Already on / already started / a scene never seen: nothing to flip here — what these do is covered by the action
            // tests above; the whitelist itself is the one line `StoryPackIntent => true` that the cases above prove.
            _ => true,
        };
        Assert.True(served, $"'{action}' was dropped while the world was held");
        Assert.True(server.IsPaused);
    }

    // ---------------- B09: the join catch-up of a started player ----------------

    /// <summary>A started VEGA player who missed some beats while offline: the beats are revealed while their keys are taken
    /// away again (the server cannot tell the difference), then the story's join hook runs.</summary>
    private static PlayerSession StartedPlayerWhoMissed(SvGameServer server, int conn, string name, bool packs, int beats)
    {
        var p = Join(server, conn, name, packs);
        server.StartStoryTrackForTest(p);
        while (server.StorySnapshot.BeatsRevealed < beats + 1)
        {
            server.AdminAdvanceStory(1);
        }

        for (int i = 1; i <= beats; i++)
        {
            p.State.Milestones.Remove(StoryPackKeys.Beat(StoryRegistry.DefaultStoryId, i));
        }

        return p;
    }

    [Fact]
    public void SeveralMissedBeats_AreOneLine_AndLandInTheJournal()
    {
        var (server, transport) = TutorialWorld("catchup_many");
        var p = StartedPlayerWhoMissed(server, 1, "Pilot", packs: true, beats: 3);
        int missed = Enumerable.Range(1, server.StorySnapshot.BeatsRevealed - 1).Count(i => !Has(p, StoryPackKeys.Beat(StoryRegistry.DefaultStoryId, i)));
        Assert.True(missed >= 3);
        transport.Sent.Clear();

        server.StoryOnJoinForTest(p);
        var lines = transport.Lines(p);
        var news = Assert.Single(lines, l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.Equal(missed.ToString(System.Globalization.CultureInfo.InvariantCulture), news.LineArg);
        Assert.DoesNotContain(lines, l => l.Kind == 2 && l.LineKey.StartsWith("story.vega.beat", StringComparison.Ordinal));
        Assert.All(Enumerable.Range(1, missed), i => Assert.True(Has(p, StoryPackKeys.Beat(StoryRegistry.DefaultStoryId, i))));
        var state = transport.To<StoryStateMessage>(p).Last();
        Assert.Contains(missed, state.HeardBeats); // the Journal's chapter list has them
    }

    [Fact]
    public void OneMissedBeat_StillSpeaks_AndAnOlderClientGetsEveryLine()
    {
        var (server, transport) = TutorialWorld("catchup_one");
        var one = StartedPlayerWhoMissed(server, 1, "One", packs: true, beats: 1);
        transport.Sent.Clear();
        server.StoryOnJoinForTest(one);
        Assert.DoesNotContain(transport.Lines(one), l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.Contains(transport.Lines(one), l => l.Kind == 2 && l.LineKey.StartsWith("story.vega.beat", StringComparison.Ordinal));

        var older = StartedPlayerWhoMissed(server, 2, "Older", packs: false, beats: 3);
        transport.Sent.Clear();
        server.StoryOnJoinForTest(older);
        Assert.DoesNotContain(transport.Lines(older), l => l.LineKey == StoryNarratorLines.JournalNews);
        Assert.True(transport.Lines(older).Count(l => l.Kind == 2 && l.LineKey.StartsWith("story.vega.beat", StringComparison.Ordinal)) >= 3);
    }

    // ---------------- The world story's notes ----------------

    [Fact]
    public void TheGuardianReveal_IsANote_OfAStartedPlayersStoryState()
    {
        var (server, _) = TutorialWorld("notes");
        var p = Join(server, 1, "Pilot");
        server.StartStoryTrackForTest(p);
        Assert.DoesNotContain("story.vega.guardian_revealed", server.StoryStateForTest(p.State.PlayerId)!.NoteKeys);
        server.AdminRevealFinale();
        Assert.True(server.IsGuardianSystemRevealedForTest);
        Assert.Contains("story.vega.guardian_revealed", server.StoryStateForTest(p.State.PlayerId)!.NoteKeys);
    }

    // ---------------- B6.4: the dock lesson's marker ----------------

    [Fact]
    public void TheStationMarker_FindsTheNearestStation_LandedAndInFlight()
    {
        var transport = new RecordingTransport();
        var server = Boot("station_marker", transport, StoryPackFixtures.ContentWith(), c => c.Rules.FreeSpaceFlight = true);
        var p = server.AddLocalPlayer("Flyer");
        string landed = Assert.IsType<string>(server.NearestStationForTest(p)); // landed: the system's station
        Assert.NotEmpty(landed);

        p.State.AboardShip = true;
        server.EnterSpace(p.State.PlayerId);
        Assert.True(server.InSpace(p.State.PlayerId));
        var stations = server.SpaceEntitiesFor(p.State.PlayerId).Where(e => e.Kind == CombatEntityKind.SpaceStation).Select(e => e.Id).ToList();
        Assert.NotEmpty(stations);
        Assert.Contains(server.NearestStationForTest(p), stations);
    }

    [Fact]
    public void TheDockLesson_SetsTheStationAsTheFlightTarget_AfterTheMarkerTime()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var transport = new RecordingTransport();
        var server = Boot("dock_marker", transport, content, c => c.Rules.FreeSpaceFlight = true);
        var p = server.AddLocalPlayer("Kid");
        p.State.Role = PlayerRole.Admin;
        server.TickStoryPacksForTest(p);
        Deliver(server, p, new AdminCommandIntent { Command = "tutorial", StringArg = "chapter b6" });
        foreach (string step in new[] { "b6.1", "b6.2", "b6.3" })
        {
            Act(server, p, SvGameServer.OfficialTutorialPackId, StoryPackActions.SkipStep, step: step);
        }

        Assert.Equal("b6.4", server.ChipStepForTest(p, SvGameServer.OfficialTutorialPackId));
        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 361);

        var marked = Assert.Single(transport.Lines(p), l => l.LineKey == StoryNarratorLines.StepMarkedStation);
        Assert.Equal(server.NearestStationForTest(p), marked.SpaceWaypointId);
        Assert.False(string.IsNullOrEmpty(marked.LineArg)); // the station's name
        Assert.Equal(SvGameServer.OfficialTutorialPackId, marked.PackId);
    }
}
