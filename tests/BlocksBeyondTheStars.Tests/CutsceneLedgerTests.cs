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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2467 — step 8 of the story packs: the server side of the cutscenes, driven by the <c>cutscene_test</c> fixture pack. A
/// scene is committed (<c>:sent</c>) and goes out only to a <c>cutscenes1</c> client once it is in the world; the receipt
/// stamps it seen (unknown ids ignored, "unsupported" counts); an older client gets the narrator line alone and the scene
/// counts as seen; an unconfirmed scene is offered again at the join (a world story's backlog as one recap card); the 90 s
/// cooldown spaces cards, banners never wait, chains and "now or never" scenes skip it; the step runtime plays the pack
/// intro, the chapter opener and closer (a template per chapter) and the step hooks in the right order, and nothing for a
/// veteran; replays change nothing; observers never get a scene; the receipt and the replay pass a held world.
/// </summary>
public sealed class CutsceneLedgerTests : IDisposable
{
    private const string C = StoryPackFixtures.Cutscenes;

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public CutsceneLedgerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cuts_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Records every server send so a test can assert who was told what, in order.</summary>
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

        public List<string> Plays(PlayerSession who) => To<CutscenePlay>(who).Select(p => p.CutsceneId).ToList();
    }

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null,
        GameContent? content = null)
    {
        var transport = new RecordingTransport();
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
        config.Rules.StoryId = StoryRegistry.NoneStoryId; // VEGA stays quiet; only the test pack talks
        configure?.Invoke(config);
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content ?? StoryPackFixtures.ContentWith(C), transport, repo);
        server.Start();
        return (server, transport);
    }

    private static PlayerSession Join(SvGameServer server, int conn, string name = "Pilot", bool cutscenes = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = cutscenes ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>The client is in the world (the join settle time passed) and the queue had its tick.</summary>
    private static void Ready(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
    }

    private static void Wait(SvGameServer server, PlayerSession p, double seconds)
    {
        server.AdvanceUptimeForTest(seconds);
        server.TickCutscenesForTest(p);
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static string Sent(string id) => StoryPackKeys.CutsceneSent(C, id);

    private static string Seen(string id) => StoryPackKeys.CutsceneSeen(C, id);

    // ---------------- First play: committed, sent once the client is in the world ----------------

    [Fact]
    public void ANewPlayer_GetsTheIntroAndTheChainedChapterCard_OnceTheClientIsInTheWorld()
    {
        var (server, transport) = World("intro");
        var p = Join(server, 1);

        Assert.Empty(transport.Plays(p)); // nothing during the join burst
        Assert.True(Has(p, Sent("welcome")));
        Assert.True(Has(p, Sent("strip@one")));
        Assert.False(Has(p, Seen("welcome")));
        Assert.Contains(transport.To<ShipAiLine>(p), l => l.LineKey == "story.cutscene_test.one1.start"); // the line goes out as before

        Ready(server, p);
        var plays = transport.To<CutscenePlay>(p);
        Assert.Equal(new[] { "welcome", "strip@one" }, plays.Select(x => x.CutsceneId)); // the chain lifts the cooldown
        Assert.All(plays, x => Assert.Equal(C, x.PackId));
        Assert.All(plays, x => Assert.False(x.Replay));
        Assert.All(plays, x => Assert.False(x.HasAnchor));
        Assert.Equal(string.Empty, plays[0].Arg);
        Assert.Equal("story.cutscene_test.ch.one", plays[1].Arg); // a chapter binding carries the chapter's title key

        server.TickCutscenesForTest(p);
        Assert.Equal(2, transport.Plays(p).Count); // sent once
    }

    [Fact]
    public void APositionReport_MarksTheClientInTheWorld_BeforeTheSettleTime()
    {
        var (server, transport) = World("moved");
        var p = Join(server, 1);
        server.TickCutscenesForTest(p);
        Assert.Empty(transport.Plays(p));

        var pos = p.State.Position;
        Deliver(server, p, new MoveIntent { X = pos.X, Y = pos.Y, Z = pos.Z });
        server.TickCutscenesForTest(p);
        Assert.Equal(new[] { "welcome", "strip@one" }, transport.Plays(p));
    }

    // ---------------- The receipt ----------------

    [Fact]
    public void TheReceipt_StampsSeen_AndIgnoresWhatThePackDoesNotDefine()
    {
        var (server, _) = World("receipt");
        var p = Join(server, 1);
        Ready(server, p);

        Deliver(server, p, new CutsceneSeenIntent
        {
            PackId = C,
            Ids = new[] { "welcome", "strip@one", "nowhere", "strip", "welcome@one", StoryCutsceneIds.Recap, "strip@bad id" },
            Skipped = true,
        });
        Assert.True(Has(p, Seen("welcome")));
        Assert.True(Has(p, Seen("strip@one")));
        Assert.False(Has(p, Seen("nowhere")));
        Assert.False(Has(p, Seen("strip")));        // a template only as an instance
        Assert.False(Has(p, Seen("welcome@one")));  // a plain scene never as one
        Assert.False(Has(p, Seen(StoryCutsceneIds.Recap)));
        Assert.False(Has(p, Seen("strip@bad id")));

        Deliver(server, p, new CutsceneSeenIntent { PackId = C, Ids = new[] { "big_moment" }, Unsupported = true });
        Assert.True(Has(p, Seen("big_moment"))); // a scene the client cannot play is not owed forever

        Deliver(server, p, new CutsceneSeenIntent { PackId = "no_such_pack", Ids = new[] { "welcome" } });
        Assert.DoesNotContain(p.State.Milestones, m => m.StartsWith("story:no_such_pack:", StringComparison.Ordinal));
    }

    // ---------------- Older clients ----------------

    [Fact]
    public void AClientWithoutCutscenes1_GetsTheLineAlone_AndTheSceneCountsAsSeen()
    {
        var (server, transport) = World("legacy");
        var p = Join(server, 1, cutscenes: false);
        Ready(server, p);

        Assert.Empty(transport.Plays(p));
        Assert.True(Has(p, Seen("welcome")));
        Assert.True(Has(p, Seen("strip@one")));
        Assert.False(Has(p, Sent("welcome")));
        Assert.Contains(transport.To<ShipAiLine>(p), l => l.LineKey == "story.cutscene_test.one1.start");
        Assert.False(server.PlayCutsceneForTest(p, C, "now"));
        Assert.False(server.PlayRecapForTest(p, C, new[] { "story.cutscene_test.ch.one" }));
        Assert.Empty(transport.Plays(p));
    }

    // ---------------- The runtime hooks ----------------

    [Fact]
    public void TheStepRuntime_PlaysItsBindings_BeforeTheirLines()
    {
        var (server, transport) = World("hooks");
        var p = Join(server, 1);
        Ready(server, p);
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        transport.Sent.Clear();

        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone");

        var order = transport.Sent.Where(x => x.Conn == p.ConnectionId)
            .Select(x => x.Msg switch
            {
                CutscenePlay play => "play:" + play.CutsceneId,
                ShipAiLine line when line.LineKey.StartsWith("story.cutscene_test.", StringComparison.Ordinal) => "line:" + line.LineKey,
                _ => null,
            })
            .Where(x => x is not null)
            .ToList();
        Assert.Equal(new[]
        {
            "play:first_mine",                     // onComplete, before the done line
            "line:story.cutscene_test.one1.done",
            "play:done@one",                       // the closer (a banner never waits)
            "play:strip@two",                      // the next opener — first_mine chains to the card
            "line:story.cutscene_test.two1.start", // big_moment (onStart, T3) waits for the cooldown
        }, order);
        Assert.Equal("story.cutscene_test.ch.one", transport.To<CutscenePlay>(p).Single(x => x.CutsceneId == "done@one").Arg);
        Assert.Equal("big_moment", Assert.Single(p.CutsceneQueue).Id);
        Assert.True(Has(p, Sent("big_moment")));

        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Assert.Equal("big_moment", transport.Plays(p).Last());
        Assert.Empty(p.CutsceneQueue);
    }

    [Fact]
    public void TheCooldown_SpacesCards_BannersNeverWait_NowOrNeverSkipsIt()
    {
        var (server, transport) = World("cooldown");
        var p = Join(server, 1);
        Ready(server, p);
        transport.Sent.Clear();

        server.RaiseGameEventForTest(p, GameEventKeys.MineAny, "stone");
        Assert.Equal(new[] { "done@one" }, transport.Plays(p)); // the banner at once, the cards wait
        Assert.Equal(new[] { "first_mine", "strip@two", "big_moment" }, p.CutsceneQueue.Select(q => q.Id));

        Assert.True(server.PlayCutsceneForTest(p, C, "now"));
        Assert.Equal("now", transport.Plays(p).Last()); // "now or never" — and it restarts the cooldown

        Wait(server, p, SvGameServer.CutsceneCooldownSeconds - 1);
        Assert.Equal(2, transport.Plays(p).Count);
        Wait(server, p, 1);
        Assert.Equal(new[] { "done@one", "now", "first_mine", "strip@two" }, transport.Plays(p)); // the chained card follows
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds - 1);
        Assert.Equal(4, transport.Plays(p).Count);
        Wait(server, p, 1);
        Assert.Equal("big_moment", transport.Plays(p).Last());
    }

    [Fact]
    public void AVeteran_GetsNoIntro_NoCards_AndNoBanners()
    {
        // Hunger off: both chapters complete by their autoCompleteIf evidence — the pack presents nothing.
        var (server, transport) = World("veteran", c => c.Rules.HungerConsumption = HungerConsumption.Off);
        var p = Join(server, 1);
        Ready(server, p);

        Assert.True(Has(p, StoryPackKeys.Done(C)));
        Assert.Empty(transport.Plays(p));
        Assert.DoesNotContain(p.State.Milestones, m => m.Contains(":cut:", StringComparison.Ordinal));
    }

    [Fact]
    public void SkippingThePack_PlaysNoIntro()
    {
        var (server, transport) = World("skip");
        var p = server.AddLocalPlayer("Solo"); // a local player's packs start with the first tick — not started yet
        p.ClientFeatures.Add(ClientFeatures.Cutscenes1);
        Assert.False(Has(p, StoryPackKeys.Started(C)));
        Deliver(server, p, new StoryPackIntent { PackId = C, Action = StoryPackActions.SkipPack });

        Assert.True(Has(p, StoryPackKeys.Done(C)));
        Assert.Empty(transport.Plays(p));
        Assert.False(Has(p, Sent("welcome")));
    }

    // ---------------- The join catch-up ----------------

    [Fact]
    public void AnUnconfirmedScene_IsOfferedAgainAtTheJoin_UntilTheClientConfirmsIt()
    {
        var (server, transport) = World("catchup");
        var p = Join(server, 1);
        Ready(server, p);
        Deliver(server, p, new CutsceneSeenIntent { PackId = C, Ids = new[] { "welcome" } });
        server.DisconnectLocalPlayerForTest("Pilot");
        transport.Sent.Clear();

        var again = Join(server, 2);
        Assert.Empty(transport.Plays(again)); // never during the join burst
        Ready(server, again);
        var play = Assert.Single(transport.To<CutscenePlay>(again));
        Assert.Equal("strip@one", play.CutsceneId);
        Assert.Equal("story.cutscene_test.ch.one", play.Arg); // the argument is derived again

        Deliver(server, again, new CutsceneSeenIntent { PackId = C, Ids = new[] { "strip@one" } });
        server.DisconnectLocalPlayerForTest("Pilot");
        transport.Sent.Clear();
        var third = Join(server, 3);
        Ready(server, third);
        Assert.Empty(transport.Plays(third));
    }

    [Fact]
    public void AnOwedScene_OfAPackSwitchedOff_WaitsUntilItIsOnAgain()
    {
        var (server, _) = World("catchup_optout");
        var p = Join(server, 1);
        Ready(server, p); // welcome and strip@one sent, never confirmed
        p.State.Milestones.Add(StoryPackKeys.OptOut(C));

        server.CutscenesOnJoinForTest(p);
        Assert.Empty(p.CutsceneQueue);
        Assert.False(Has(p, Seen("welcome"))); // still owed

        p.State.Milestones.Remove(StoryPackKeys.OptOut(C));
        server.CutscenesOnJoinForTest(p);
        Assert.Equal(new[] { "welcome", "strip@one" }, p.CutsceneQueue.Select(q => q.Id));
    }

    [Fact]
    public void AnOwedScene_CountsAsSeen_WhenThePlayerReturnsWithAnOlderClient()
    {
        var (server, transport) = World("catchup_legacy");
        var p = Join(server, 1);
        Assert.True(Has(p, Sent("welcome"))); // committed, still queued
        server.DisconnectLocalPlayerForTest("Pilot");

        var old = Join(server, 2, cutscenes: false);
        Ready(server, old);
        Assert.True(Has(old, Seen("welcome")));
        Assert.True(Has(old, Seen("strip@one")));
        Assert.Empty(transport.Plays(old));
    }

    [Fact]
    public void AWorldStoryBacklog_CollapsesIntoOneRecapCard()
    {
        // The VEGA data pack, given three chapters with an opener each (step 11 authors the real ones).
        var content = StoryPackFixtures.ContentWith(C);
        var vega = content.Stories[StoryRegistry.DefaultStoryId];
        foreach (string chapter in new[] { "c1", "c2", "c3" })
        {
            vega.Chapters.Add(new StoryChapter { Id = chapter, TitleKey = "story.vega_protocol.name." + chapter });
            vega.Cutscenes.Add(new CutsceneDefinition
            {
                Id = chapter + ".open",
                Chapter = chapter,
                Tier = CutsceneTiers.Card,
                Shots = { new CutsceneShot { Duration = 4 } },
            });
        }

        var (server, transport) = World("recap", c => c.Rules.StoryId = StoryRegistry.DefaultStoryId, content); // VEGA runs
        var p = Join(server, 1);
        Ready(server, p);
        Deliver(server, p, new CutsceneSeenIntent { PackId = C, Ids = new[] { "welcome", "strip@one" } });
        string V = StoryRegistry.DefaultStoryId;
        // #2470: the newcomer's join committed the prologue scene (sent, never confirmed) — it stays owed here and is never
        // folded into the recap (the cold open is no chapter line).
        Assert.DoesNotContain(StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Prologue), p.State.Milestones);
        foreach (string id in new[] { "c3.open", "c1.open", "c2.open", StoryCutsceneIds.Prologue })
        {
            p.State.Milestones.Add(StoryPackKeys.CutsceneSent(V, id));
        }

        transport.Sent.Clear();
        server.CutscenesOnJoinForTest(p);
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);

        var recap = Assert.Single(transport.To<CutscenePlay>(p));
        Assert.Equal(V, recap.PackId);
        Assert.Equal(StoryCutsceneIds.Recap, recap.CutsceneId);
        Assert.Equal(new[] { "story.vega_protocol.name.c1", "story.vega_protocol.name.c2", "story.vega_protocol.name.c3" },
            LineText.SplitArgs(recap.Arg)); // titles only, in chapter order
        Assert.All(new[] { "c1.open", "c2.open", "c3.open" }, id => Assert.True(Has(p, StoryPackKeys.CutsceneSeen(V, id))));
        Assert.False(Has(p, StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Recap))); // the recap is not in the ledger

        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Assert.Equal(StoryCutsceneIds.Prologue, transport.Plays(p).Last()); // a scene without a chapter is offered as itself
    }

    // ---------------- API: recap, anchor, unknown ids, replays ----------------

    [Fact]
    public void TheRecapAndTheAnchor_GoOnTheWire()
    {
        var (server, transport) = World("api");
        var p = Join(server, 1);
        Ready(server, p);
        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        transport.Sent.Clear();

        Assert.True(server.PlayRecapForTest(p, C, new[] { "story.cutscene_test.ch.one", "story.cutscene_test.ch.two" }));
        var recap = Assert.Single(transport.To<CutscenePlay>(p));
        Assert.Equal("story.cutscene_test.ch.one" + LineText.ArgSeparator + "story.cutscene_test.ch.two", recap.Arg);

        Wait(server, p, SvGameServer.CutsceneCooldownSeconds);
        Assert.True(server.PlayCutsceneForTest(p, C, "anchored", "x", new Vector3f(1f, 2f, 3f)));
        var anchored = transport.To<CutscenePlay>(p).Last();
        Assert.Equal("anchored", anchored.CutsceneId);
        Assert.Equal("x", anchored.Arg);
        Assert.True(anchored.HasAnchor);
        Assert.Equal((1f, 2f, 3f), (anchored.AnchorX, anchored.AnchorY, anchored.AnchorZ));

        Assert.False(server.PlayCutsceneForTest(p, C, "anchored")); // once per player
        Assert.False(server.PlayCutsceneForTest(p, C, "nowhere"));
        Assert.False(server.PlayCutsceneForTest(p, C, "strip"));     // a template only as an instance
        Assert.False(server.PlayCutsceneForTest(p, "no_such_pack", "welcome"));
        Assert.False(Has(p, Sent("nowhere")));
    }

    [Fact]
    public void AReplay_ResendsAShownScene_AndChangesNothing()
    {
        var (server, transport) = World("replay");
        var p = Join(server, 1);
        Ready(server, p);
        Deliver(server, p, new CutsceneSeenIntent { PackId = C, Ids = new[] { "welcome" } });
        transport.Sent.Clear();
        int keys = p.State.Milestones.Count;

        Deliver(server, p, new StoryPackIntent { PackId = C, Action = StoryPackActions.ReplayCutscene, CutsceneId = "welcome" });
        var replay = Assert.Single(transport.To<CutscenePlay>(p));
        Assert.True(replay.Replay);
        Assert.Equal("welcome", replay.CutsceneId);
        Assert.Equal(keys, p.State.Milestones.Count);

        Deliver(server, p, new StoryPackIntent { PackId = C, Action = StoryPackActions.ReplayCutscene, CutsceneId = "big_moment" });
        p.State.Milestones.Add(Seen("anchored"));
        Deliver(server, p, new StoryPackIntent { PackId = C, Action = StoryPackActions.ReplayCutscene, CutsceneId = "anchored" });
        Assert.Single(transport.To<CutscenePlay>(p)); // not shown yet (spoiler guard) / not replayable

        // Any installed pack replays, the world story included.
        p.State.Milestones.Add(StoryPackKeys.CutsceneSeen(StoryRegistry.DefaultStoryId, StoryCutsceneIds.Prologue));
        Deliver(server, p, new StoryPackIntent { PackId = StoryRegistry.DefaultStoryId, Action = StoryPackActions.ReplayCutscene, CutsceneId = StoryCutsceneIds.Prologue });
        Assert.Equal(StoryCutsceneIds.Prologue, transport.Plays(p).Last());
        Assert.True(transport.To<CutscenePlay>(p).Last().Replay);
    }

    // ---------------- Observers ----------------

    [Fact]
    public void AnObserver_NeverGetsAScene_AndCannotConfirmOne()
    {
        var (server, transport) = World("observer");
        var pilot = Join(server, 1, "Pilot");
        var watcher = Join(server, 2, "Watcher");
        Ready(server, pilot);
        server.TickCutscenesForTest(watcher);
        watcher.Spectating = true;
        transport.Sent.Clear();

        server.PlayCutsceneForListenersForTest(C, "now");
        Assert.Equal(new[] { "now" }, transport.Plays(pilot));
        Assert.Empty(transport.Plays(watcher));
        Assert.False(Has(watcher, Sent("now")));
        Assert.False(server.PlayCutsceneForTest(watcher, C, "big_moment"));

        Deliver(server, watcher, new CutsceneSeenIntent { PackId = C, Ids = new[] { "big_moment" } });
        Assert.False(Has(watcher, Seen("big_moment")));
    }

    // ---------------- The pause whitelist (dispatch level) ----------------

    [Fact]
    public void WhileHeld_TheReceiptAndAReplayAreServed()
    {
        var (server, transport) = World("held");
        var p = server.AddLocalPlayer("Solo");
        p.ClientFeatures.Add(ClientFeatures.Cutscenes1);
        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);
        transport.Sent.Clear();

        Deliver(server, p, new CutsceneSeenIntent { PackId = C, Ids = new[] { "big_moment" } });
        Assert.True(Has(p, Seen("big_moment")));

        Deliver(server, p, new StoryPackIntent { PackId = C, Action = StoryPackActions.ReplayCutscene, CutsceneId = "big_moment" });
        Assert.True(Assert.Single(transport.To<CutscenePlay>(p)).Replay);
        Assert.True(server.IsPaused);
    }

    // ---------------- #2468: the queue ticks for every joined player ----------------

    [Fact]
    public void APilotOnAnUnloadedWorld_StillGetsTheQueuedScenes_FromTheServerTick()
    {
        // A pilot cruising after an in-flight hyperjump occupies a body whose world is not loaded — TickShipAi never reaches
        // them (it runs per loaded world), and step 8's queue waited until the landing. The flush now runs for everyone.
        var (server, transport) = World("space");
        var p = Join(server, 1);
        Assert.Empty(transport.Plays(p));
        p.CurrentLocationId = "planet_never_loaded";

        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.Tick(0.1);
        Assert.Equal(new[] { "welcome", "strip@one" }, transport.Plays(p));
    }

    // ---------------- #2468: the admin preview ----------------

    [Fact]
    public void TheAdminPreview_PlaysAnySceneForTheAdminAlone_AndChangesNothing()
    {
        var (server, transport) = World("preview");
        var admin = Join(server, 1, "Admin"); // the world's creator
        var other = Join(server, 2, "Other");
        Ready(server, admin);
        server.TickCutscenesForTest(other);
        transport.Sent.Clear();
        int keys = admin.State.Milestones.Count;

        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = C + " big_moment" });
        var play = Assert.Single(transport.To<CutscenePlay>(admin));
        Assert.Equal((C, "big_moment"), (play.PackId, play.CutsceneId));
        Assert.True(play.Replay);
        Assert.True(play.HasAnchor);
        Assert.Equal(admin.State.Position.X, play.AnchorX);
        Assert.Equal("story.cutscene_test.ch.two", play.Arg); // the scene's chapter title key
        Assert.Contains(transport.To<ServerMessage>(admin), m => m.Text == "@srv.admin.cutscene_preview:" + C + "/big_moment");
        Assert.Equal(keys, admin.State.Milestones.Count);
        Assert.False(Has(admin, Sent("big_moment")));
        Assert.Empty(transport.Plays(other));

        // The other spellings: pack/id, a template without its owner (the first chapter's instance), a bare id (any pack
        // that defines it), the recap with every chapter title.
        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = C + "/welcome" });
        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = C + " strip" });
        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = StoryCutsceneIds.Prologue });
        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = C + " " + StoryCutsceneIds.Recap });
        var plays = transport.To<CutscenePlay>(admin);
        Assert.Equal(new[] { "big_moment", "welcome", "strip@one", StoryCutsceneIds.Prologue, StoryCutsceneIds.Recap },
            plays.Select(x => x.CutsceneId));
        Assert.Equal(StoryRegistry.DefaultStoryId, plays[3].PackId);
        Assert.Equal("story.cutscene_test.ch.one", plays[2].Arg);
        Assert.Equal(new[] { "story.cutscene_test.ch.one", "story.cutscene_test.ch.two" }, LineText.SplitArgs(plays[4].Arg));
        Assert.All(plays, x => Assert.True(x.Replay));
        Assert.Equal(keys, admin.State.Milestones.Count);
    }

    [Fact]
    public void TheAdminPreview_Refuses_Strangers_UnknownScenes_AndOlderClients()
    {
        var (server, transport) = World("preview_refusals");
        var admin = Join(server, 1, "Admin");
        var player = Join(server, 2, "Player");
        var oldAdmin = Join(server, 3, "OldAdmin", cutscenes: false);
        oldAdmin.State.Role = BlocksBeyondTheStars.Shared.State.PlayerRole.Admin;
        transport.Sent.Clear();

        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = "" });
        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = C + " nowhere" });
        Deliver(server, admin, new AdminCommandIntent { Command = "cutscene", StringArg = "no_such_pack welcome" });
        Deliver(server, player, new AdminCommandIntent { Command = "cutscene", StringArg = C + " welcome" });
        Deliver(server, oldAdmin, new AdminCommandIntent { Command = "cutscene", StringArg = C + " welcome" });

        Assert.Equal(new[] { "@srv.admin.cutscene_usage", "@srv.admin.cutscene_unknown:" + C + "/nowhere", "@srv.admin.cutscene_unknown:no_such_pack/welcome" },
            transport.To<ActionRejected>(admin).Select(r => r.Reason));
        Assert.Equal("@srv.admin.not_admin", Assert.Single(transport.To<ActionRejected>(player)).Reason);
        Assert.Equal("@srv.admin.cutscene_unsupported", Assert.Single(transport.To<ActionRejected>(oldAdmin)).Reason);
        Assert.Empty(transport.Sent.Select(x => x.Msg).OfType<CutscenePlay>());
    }

    // ---------------- #2468: the seen list for "Watch again" ----------------

    [Fact]
    public void TheStoryState_ListsTheConfirmedScenes_NotTheOwedOnes()
    {
        var (server, _) = World("seen_list");
        var p = Join(server, 1);
        Ready(server, p);
        Assert.Empty(server.StoryStateForTest(p.State.PlayerId)!.SeenCutscenes); // sent, not yet confirmed

        Deliver(server, p, new CutsceneSeenIntent { PackId = C, Ids = new[] { "welcome", "strip@one" } });
        p.State.Milestones.Add(StoryPackKeys.CutsceneSeen(StoryRegistry.DefaultStoryId, StoryCutsceneIds.Prologue));
        var state = server.StoryStateForTest(p.State.PlayerId)!;
        Assert.Equal(new[] { C + "/strip@one", C + "/welcome", "vega_protocol/prologue" }, state.SeenCutscenes);

        var back = Assert.IsType<StoryStateMessage>(NetCodec.Decode(NetCodec.Encode(state)));
        Assert.Equal(state.SeenCutscenes, back.SeenCutscenes);
    }

    [Theory]
    [InlineData("story:tutorial:cut:tut.welcome", true, "tutorial", "tut.welcome")]
    [InlineData("story:tutorial:cut:tut.strip@b3", true, "tutorial", "tut.strip@b3")]
    [InlineData("story:tutorial:cut:tut.welcome:sent", false, "", "")]
    [InlineData("story:vega_protocol:beat:3", false, "", "")]
    [InlineData("story:tutorial:ch:b1:active", false, "", "")]
    [InlineData("story:tutorial:cut:", false, "", "")]
    [InlineData("vega:intro", false, "", "")]
    public void TheSeenKey_ParsesBack_AndTheSentKeyDoesNot(string key, bool ok, string pack, string id)
    {
        Assert.Equal(ok, StoryPackKeys.TryParseCutsceneSeen(key, out string p, out string i));
        Assert.Equal((pack, id), (p, i));
    }
}
