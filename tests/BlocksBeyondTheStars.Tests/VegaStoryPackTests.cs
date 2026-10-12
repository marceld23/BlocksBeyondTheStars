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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2470 — step 11 of the story packs (#2459): the VEGA story as its own pack. The manifest (chapters, start rule, intro,
/// insights) and its built-in mirror; the prologue as the pack's intro cutscene (once, never on a tutorial restart, the old
/// pages for an older client, a "none" world switched to VEGA); the start rule of the story track; what is held until it
/// starts and what is not; the track start (held beats heard with their knowledge now, the recap, only the current
/// chapter's opening, the Guardian reveal, the ending); a multiplayer latecomer and an arcade newcomer's personal chapter 1;
/// the one-time migration P1–P9; the legacy memory arc in a "none" world; the chip before the start; the pure chapter
/// arithmetic and the validator's world-pack chapter rules.
/// </summary>
public sealed class VegaStoryPackTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string Tutorial = SvGameServer.OfficialTutorialPackId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public VegaStoryPackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vega_pack_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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

        public List<string> LineKeys(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();

        public List<string> Plays(PlayerSession who) => To<CutscenePlay>(who).Select(c => c.CutsceneId).ToList();
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null,
        SqliteWorldRepository? repo = null, GameContent? content = null)
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
        configure?.Invoke(config);
        var server = new SvGameServer(config, content ?? _content, transport, repo ?? Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>A save that already holds a player (an older build's state).</summary>
    private SqliteWorldRepository SaveWith(string world, string name, IEnumerable<string> milestones, int knowledge = 0)
    {
        var repo = Repo(world);
        repo.Initialize();
        var state = new PlayerState { PlayerId = name, Name = name, KnowledgePoints = knowledge };
        state.Milestones.UnionWith(milestones);
        repo.SavePlayer(state);
        return repo;
    }

    /// <summary>The real join path. <paramref name="cutscenes"/>: a current client (it plays cutscenes); without, an older
    /// client of the packs era. <paramref name="installId"/>: an arcade guest.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool cutscenes = true, string installId = "")
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            InstallId = installId,
            Features = cutscenes ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>The client is in the world and the cutscene queue had its tick.</summary>
    private static void Ready(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
    }

    /// <summary>#2474: the cooldown runs out <paramref name="times"/> times, each followed by a queue tick — every queued
    /// scene (and the lines it holds) goes out.</summary>
    private static void Drain(SvGameServer server, PlayerSession p, int times = 4)
    {
        for (int i = 0; i < times; i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(p);
        }
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static string Beat(int n) => StoryPackKeys.Beat(V, n);

    private static readonly string Started = StoryPackKeys.Started(V);

    private static readonly string PrologueSeen = StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Prologue);

    private static readonly string PrologueSent = StoryPackKeys.CutsceneSent(V, StoryCutsceneIds.Prologue);

    private static IEnumerable<string> AllStages() => StoryLegacy.StageIds.Select(id => StoryLegacy.StagePrefix + id).Append(StoryLegacy.IntroMilestone);

    private static IEnumerable<string> Beats(int upTo) => Enumerable.Range(0, upTo + 1).Select(Beat);

    // Milestones (×2 points each) the world needs to reach a beat's threshold at Normal density.
    private const int ToB1 = 3;
    private const int ToB5 = 25;
    private const int ToB8 = 52;

    // ---------------- The manifest ----------------

    [Fact]
    public void ThePack_DeclaresItsChapters_StartRule_Intro_AndInsights()
    {
        var vega = _content.Stories[V];
        Assert.Equal(StoryRegistry.ScopeWorld, StoryRegistry.EffectiveScope(vega));
        Assert.Equal("story.vega.short", vega.ShortNameKey);
        Assert.Equal(StoryCutsceneIds.Prologue, vega.IntroCutscene);
        Assert.Equal(new[] { "chapter:tutorial:b6" }, vega.Start.After);
        Assert.True(vega.Start.EarlyOptIn);
        Assert.Equal(new[] { "awakening", "signature", "service", "guardian", "verdict", "what_you_are", "sleeps", "core", "resolution" },
            vega.Chapters.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2, 5, 6, 9, 11, 12, -1 }, vega.Chapters.Select(c => c.FromBeat));
        Assert.Equal(StoryChapterEvents.GuardianDefeated, vega.Chapters[^1].FromEvent);
        Assert.All(vega.Chapters, c => Assert.Equal("story.vega.ch." + c.Id, c.TitleKey));
        Assert.Equal(new[] { "companion_ward", "shape_noncube" }, vega.Insights.Select(i => i.Event));

        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        Assert.Equal(new[] { "Awakening", "A Familiar Signature", "The Service", "The Guardian", "The Verdict", "What You Are", "It Still Sleeps", "The Guardian Core", "Resolution" },
            vega.Chapters.Select(c => en.Get(c.TitleKey)));
        Assert.Equal(new[] { "Erwachen", "Eine vertraute Signatur", "Der Dienst", "Der Wächter", "Das Urteil", "Was du bist", "Er schläft noch", "Der Wächterkern", "Auflösung" },
            vega.Chapters.Select(c => de.Get(c.TitleKey)));
        Assert.False(en.Get(SvGameServer.StoryCallLine).StartsWith("[", StringComparison.Ordinal));
        Assert.False(de.Get(SvGameServer.StoryCallLine).StartsWith("[", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePrologueScene_ShowsTheNarration_ThenVegasPages_EndingWithTheCall()
    {
        var prologue = _content.Stories[V].FindCutscene(StoryCutsceneIds.Prologue)!;
        Assert.Equal(CutsceneTiers.Scene, prologue.Tier);
        Assert.Equal("awakening", prologue.Chapter);
        var captions = prologue.Shots.SelectMany(s => s.Captions).ToList();
        Assert.Equal(new[] { "story.vega.ch.awakening", "vega.prologue.1", "vega.prologue.2", "vega.prologue.3", "story.vega.beat00", SvGameServer.StoryCallLine },
            captions.Select(c => c.Key));
        Assert.Equal(CutsceneCaptionStyles.Speaker, captions[^1].Style);
        Assert.Equal(CutsceneCaptionStyles.Speaker, captions[^2].Style);
        Assert.Equal(CutsceneCameras.Orbit, prologue.Shots[1].Camera.Type); // the camera moves on its own around the ship
        Assert.Contains("story.vega.beat00", prologue.ReplaceLineKeys);
        Assert.Contains(SvGameServer.StoryCallLine, prologue.ReplaceLineKeys);
    }

    [Fact]
    public void ThePrologueKeys_LiveInThePackLocale_WithTheirNamesKept()
    {
        string core = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", "en.json"));
        string pack = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "locales", "en.json"));
        foreach (string key in new[] { "vega.prologue.1", "vega.prologue.2", "vega.prologue.3" })
        {
            Assert.DoesNotContain("\"" + key + "\"", core, StringComparison.Ordinal);
            Assert.Contains("\"" + key + "\"", pack, StringComparison.Ordinal);
        }
    }

    // ---------------- The prologue ----------------

    [Fact]
    public void ANewcomer_GetsThePrologueScene_NotTheOldPages_AndB0CountsAsHeard()
    {
        var (server, transport) = World("prologue_scene");
        var p = Join(server, 1, "Dreamer");

        Assert.True(Has(p, PrologueSent));
        Assert.True(Has(p, Beat(0))); // VEGA's first words are the prologue's speaker page
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.Kind == 4);
        Assert.DoesNotContain("story.vega.beat00", transport.LineKeys(p)); // no second "VEGA wakes"

        Ready(server, p);
        var play = Assert.Single(transport.To<CutscenePlay>(p), c => c.CutsceneId == StoryCutsceneIds.Prologue);
        Assert.Equal(V, play.PackId);
        Assert.False(play.Replay);
        Assert.False(Has(p, Started)); // the story itself waits for the basic training
    }

    [Fact]
    public void AnOlderClient_GetsTheFiveFullScreenPages_BeforeTheFirstLesson()
    {
        var (server, transport) = World("prologue_pages");
        var p = Join(server, 1, "Oldtimer", cutscenes: false);

        var pages = transport.To<ShipAiLine>(p).Where(l => l.Kind == 4).ToList();
        Assert.Equal(5, pages.Count);
        Assert.Equal(new[] { "vega.prologue.1", "vega.prologue.2", "vega.prologue.3", "story.vega.beat00", string.Empty }, pages.Select(l => l.LineKey));
        Assert.Equal(_content.CreateLocalizer(GameLocale.English).Get(SvGameServer.StoryCallLine), pages[^1].Text); // the call, composed
        var keys = transport.LineKeys(p);
        Assert.True(keys.IndexOf("story.vega.beat00") < keys.IndexOf("story.tutorial.b1.walk.start"), "the cold open comes first");
        Assert.True(Has(p, PrologueSeen));
        Assert.Empty(transport.Plays(p));
    }

    [Fact]
    public void ThePrologue_PlaysOnce_AndNeverOnATutorialRestart()
    {
        var (server, transport) = World("prologue_once");
        var p = Join(server, 1, "Once", cutscenes: false);
        Assert.Equal(5, transport.To<ShipAiLine>(p).Count(l => l.Kind == 4));

        Deliver(server, p, new SkipOnboardingIntent { Restart = true }); // the tutorial again
        server.StoryOnJoinForTest(p);                                     // and the story's join hook again
        Assert.Equal(5, transport.To<ShipAiLine>(p).Count(l => l.Kind == 4));
    }

    [Fact]
    public void ANoneWorld_PlaysNoPrologue_AndStartsNoTrack()
    {
        var (server, transport) = World("prologue_none", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Sandboxer", cutscenes: false);
        server.SkipTutorialForTest(p);

        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.Kind == 4);
        Assert.False(Has(p, Started));
        Assert.False(Has(p, StoryPackKeys.Migrated(V))); // P6: nothing migrated, so a later story still meets them fresh
        Assert.Null(server.StoryTrackStartReasonForTest(p));
    }

    [Fact]
    public void ANoneWorldSwitchedToVega_PlaysThePrologue_ToThePlayersOnline()
    {
        var (server, transport) = World("prologue_switch", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Settler", cutscenes: false);
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.Kind == 4);

        server.SetActiveStoryForTest(V);

        Assert.Equal(5, transport.To<ShipAiLine>(p).Count(l => l.Kind == 4));
        Assert.True(Has(p, PrologueSeen));
        Assert.True(Has(p, Beat(0)));
        Assert.False(Has(p, Started)); // the basic training still runs
        Assert.Equal("awakening", server.WorldChapterIdForTest);
    }

    // ---------------- The start rule ----------------

    [Fact]
    public void TheTrack_WaitsForTheBasicTraining_AndStartsWhenItIsSkipped()
    {
        var (server, _) = World("start_training");
        var p = Join(server, 1, "Rookie");
        Assert.False(Has(p, Started));
        Assert.Null(server.StoryTrackStartReasonForTest(p));

        server.SkipTutorialForTest(p);

        Assert.True(Has(p, Started));
        Assert.Equal("signature", server.TrackChapterIdForTest(p)); // max(1, world): the world is still at B0
    }

    [Theory]
    [InlineData(TutorialPolicy.Off)]
    [InlineData(TutorialPolicy.OptIn)]
    public void WithoutATutorialForThePlayer_TheTrackStartsAtTheJoin(TutorialPolicy policy)
    {
        var (server, _) = World("start_" + policy, c => c.Rules.TutorialPolicy = policy);
        var p = Join(server, 1, "Pilot");

        Assert.True(Has(p, PrologueSent)); // the cold open still comes first
        Assert.True(Has(p, Started));
    }

    [Fact]
    public void AnOldSavePlayer_WhoHeardABeat_StartsAtTheJoin()
    {
        var repo = SaveWith("start_heard", "Veteran", Beats(2).Concat(new[] { StoryLegacy.IntroMilestone, "vega:stage:mine" }));
        var (server, transport) = World("start_heard", repo: repo);
        server.AdminAdvanceStory(7); // the world at B2
        var p = Join(server, 1, "Veteran");

        Assert.True(Has(p, Started));
        Assert.True(Has(p, PrologueSeen)); // P2: the old pages count as the prologue — offered in the Story tab, never forced
        Assert.DoesNotContain(StoryCutsceneIds.Prologue, transport.Plays(p));
        Assert.False(Has(p, PrologueSent));
    }

    [Fact]
    public void AVeteranFromBeforeTheLessons_StartsAtTheJoin()
    {
        var repo = SaveWith("start_veteran", "Vet", Array.Empty<string>(), knowledge: 12);
        var (server, _) = World("start_veteran", repo: repo);
        var p = Join(server, 1, "Vet");

        Assert.True(Has(p, StoryPackKeys.Done(Tutorial))); // the tutorial's migration skipped the training
        Assert.True(Has(p, Started));
    }

    [Fact]
    public void StartTheStoryNow_StartsTheTrack_EvenMidTraining_AndThroughAHeldWorld()
    {
        var (server, _) = World("start_request");
        var p = Join(server, 1, "Eager");
        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);

        Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.StartPack });

        Assert.True(Has(p, Started));
        Assert.Contains(p.State.StepProgress.Keys, k => k.StartsWith(Tutorial + ":", StringComparison.Ordinal)); // the lessons go on
    }

    [Fact]
    public void InTheFinaleSystem_TheTrackStarts()
    {
        var (server, _) = World("start_finale");
        server.AdminRevealFinale();
        var p = Join(server, 1, "Lost");
        Assert.False(Has(p, Started));

        p.CurrentLocationId = SvGameServer.GuardianCoreBodyId;
        Assert.True(server.TryStartStoryTrackForTest(p));
        Assert.True(Has(p, Started));
    }

    // ---------------- Held content ----------------

    [Fact]
    public void BeatsAndTheirKnowledge_AreHeld_UntilTheTrackStarts()
    {
        var (server, transport) = World("held_beats");
        var started = Join(server, 1, "Crew");
        server.SkipTutorialForTest(started);
        var rookie = Join(server, 2, "Rookie");
        int knowledge = rookie.State.KnowledgePoints;

        server.AdminAdvanceStory(ToB1);
        Drain(server, started); // #2474: the line waits with its beat card
        Drain(server, rookie);

        Assert.True(Has(started, Beat(1)));
        Assert.Contains("story.vega.beat01", transport.LineKeys(started));
        Assert.Contains("b01", transport.Plays(started));
        Assert.False(Has(rookie, Beat(1)));
        Assert.Equal(knowledge, rookie.State.KnowledgePoints);
        Assert.DoesNotContain("story.vega.beat01", transport.LineKeys(rookie));
        Assert.DoesNotContain("b01", transport.Plays(rookie));
    }

    [Fact]
    public void Insights_AreHeld_AndFireAtTheNextOccasionAfterTheStart()
    {
        var (server, _) = World("held_insight");
        server.AdminAdvanceStory(ToB5); // B5: the Guardian's mandate is known
        var p = Join(server, 1, "Ranger");

        Assert.False(server.RevealCompanionWardInsightForTest("Ranger"));
        Assert.False(Has(p, "story:insight:companion-ward")); // not consumed

        server.SkipTutorialForTest(p);
        Assert.True(server.RevealCompanionWardInsightForTest("Ranger"));
        Assert.True(Has(p, "story:insight:companion-ward"));
    }

    [Fact]
    public void TheGuardianReveal_IsHeld_AndTheTrackStartSpeaksIt()
    {
        var (server, transport) = World("held_reveal");
        var p = Join(server, 1, "Rookie");
        server.AdminRevealFinale();

        Assert.DoesNotContain("story.vega.guardian_revealed", transport.LineKeys(p));
        Assert.False(Has(p, StoryPackKeys.Prefix(V) + "finale:revealed"));
        Assert.DoesNotContain(p.State.PlayerId, server.FinaleAudienceForTest()); // the hack and duel broadcasts skip them

        server.SkipTutorialForTest(p);
        Drain(server, p); // #2474: the line waits with chapter 7's set piece, which shows it
        Assert.Contains("story.vega.guardian_revealed", transport.LineKeys(p)); // the jump generator line, at the start
        Assert.Contains("ch7.open", transport.Plays(p));
        Assert.Contains(p.State.PlayerId, server.FinaleAudienceForTest());
    }

    [Fact]
    public void TheLiveEnding_WaitsForAPlayersTrackStart()
    {
        var (server, transport) = World("held_ending");
        var crew = Join(server, 1, "Crew");
        server.SkipTutorialForTest(crew);
        var rookie = Join(server, 2, "Rookie");
        server.AdminRevealFinale();

        server.WinDuelForTest();

        Assert.Single(transport.To<StoryResolved>(crew));
        Assert.Contains("story.vega.finale_resolved", transport.LineKeys(crew));
        Assert.Empty(transport.To<StoryResolved>(rookie));
        Assert.DoesNotContain("story.vega.finale_resolved", transport.LineKeys(rookie));

        server.SkipTutorialForTest(rookie);
        Assert.Single(transport.To<StoryResolved>(rookie)); // deferred to the start
        Assert.True(Has(rookie, StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Resolution)));
    }

    [Fact]
    public void RelayInsights_GoToStartedPlayers_AndReachTheOthersLogSilently()
    {
        var (server, transport) = World("held_relay");
        var crew = Join(server, 1, "Crew");
        server.SkipTutorialForTest(crew);
        var rookie = Join(server, 2, "Rookie");

        server.SpeakRelayInsightForTest("relay");

        Assert.Contains("vega.relay.first", transport.LineKeys(crew));
        Assert.DoesNotContain("vega.relay.first", transport.LineKeys(rookie));
        Assert.Empty(server.StoryStateForTest("Rookie")!.RelayInsightKeys);

        server.SkipTutorialForTest(rookie);
        Assert.DoesNotContain("vega.relay.first", transport.LineKeys(rookie)); // not spoken …
        Assert.Contains("vega.relay.first", server.StoryStateForTest("Rookie")!.RelayInsightKeys); // … but in the log
    }

    [Fact]
    public void RelayInsights_StaySilentWithoutAStory()
    {
        var (server, transport) = World("relay_none", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Builder");
        server.SpeakRelayInsightForTest("relay");
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("vega.relay.", StringComparison.Ordinal));

        server.SetActiveStoryForTest(V); // the once-guard was not spent: a story that comes later still hears it
        server.SkipTutorialForTest(p);
        server.SpeakRelayInsightForTest("relay");
        Assert.Contains("vega.relay.first", transport.LineKeys(p));
    }

    // ---------------- The track start ----------------

    [Fact]
    public void AMultiplayerLatecomer_HearsNothingDuringTraining_ThenARecap_AndTheCurrentChapter()
    {
        var (server, transport) = World("latecomer");
        var crew = Join(server, 1, "Crew");
        server.SkipTutorialForTest(crew);
        server.AdminAdvanceStory(ToB8); // the crew is in chapter 4 (B8)
        var rookie = Join(server, 2, "Rookie");
        int knowledge = rookie.State.KnowledgePoints;
        Ready(server, rookie);

        server.AdminAdvanceStory(11); // B9 while the newcomer trains
        Assert.True(Has(crew, Beat(9)));
        Assert.DoesNotContain(transport.LineKeys(rookie), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
        Assert.False(server.StoryStateForTest("Rookie")!.Started);
        Assert.True(server.StoryStateForTest("Rookie")!.ShowCrewChapter);

        transport.Sent.Clear();
        server.SkipTutorialForTest(rookie);

        Assert.All(Enumerable.Range(1, 9), n => Assert.True(Has(rookie, Beat(n))));
        Assert.Equal(knowledge + 9, rookie.State.KnowledgePoints); // the held knowledge, credited now
        Assert.Equal("what_you_are", server.TrackChapterIdForTest(rookie));
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(rookie);
        var recap = Assert.Single(transport.To<CutscenePlay>(rookie), c => c.CutsceneId == StoryCutsceneIds.Recap);
        Assert.Equal(new[] { "story.vega.ch.signature", "story.vega.ch.service", "story.vega.ch.guardian", "story.vega.ch.verdict" },
            LineText.SplitArgs(recap.Arg)); // titles of the chapters before the current one
        // #2474: only the current chapter's scene follows the recap at once — it shows B9 itself, so no beat line is spoken
        Assert.Equal(new[] { StoryCutsceneIds.Recap, "ch5.open" }, transport.To<CutscenePlay>(rookie).Where(c => c.PackId == V).Select(c => c.CutsceneId));
        Assert.DoesNotContain(transport.LineKeys(rookie), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
        var state = server.StoryStateForTest("Rookie")!;
        Assert.True(state.Started);
        Assert.Equal("what_you_are", state.Chapter);
        Assert.Equal("what_you_are", state.WorldChapter);
        Assert.False(state.ShowCrewChapter);
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest("Rookie"));
    }

    [Fact]
    public void AnArcadeNewcomer_GetsAPersonalChapterOne_AndTheCrewsChapterAsAStatusLine()
    {
        var (server, transport) = World("arcade");
        server.AdminAdvanceStory(ToB8);
        var guest = Join(server, 1, "Guest", installId: "arcadeguest0001");
        Ready(server, guest);
        transport.Sent.Clear();

        server.SkipTutorialForTest(guest);
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(guest);

        Assert.All(Enumerable.Range(1, 8), n => Assert.True(Has(guest, Beat(n))));
        Assert.DoesNotContain(StoryCutsceneIds.Recap, transport.Plays(guest)); // the recap only on demand (Story tab)
        Assert.Equal(new[] { "story.vega.beat01" }, transport.LineKeys(guest).Where(k => k.StartsWith("story.vega.beat", StringComparison.Ordinal)));
        var state = server.StoryStateForTest("Guest")!;
        Assert.True(state.ShowCrewChapter);
        Assert.Equal("verdict", state.WorldChapter);
    }

    [Fact]
    public void ABoundChapterOpening_FollowsTheRecapAtOnce_AndCarriesTheBeat()
    {
        // #2474: chapter 4's real opening card.
        var (server, transport) = World("bound_opening");
        server.AdminAdvanceStory(ToB8);
        var p = Join(server, 1, "Rookie");
        Ready(server, p);
        Deliver(server, p, new CutsceneSeenIntent { PackId = V, Ids = new[] { StoryCutsceneIds.Prologue } });
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        transport.Sent.Clear();

        server.SkipTutorialForTest(p);
        server.TickCutscenesForTest(p);

        Assert.Equal(new[] { StoryCutsceneIds.Recap, "ch4.open" }, transport.Plays(p)); // chained: no 90 s gap
        Assert.DoesNotContain("story.vega.beat08", transport.LineKeys(p)); // the scene shows the chapter's moment
    }

    [Fact]
    public void ALiveBeat_ThatOpensAChapter_PlaysItsOpeningBeforeTheLine()
    {
        var (server, transport) = World("live_opening");
        var p = Join(server, 1, "Crew");
        server.SkipTutorialForTest(p);

        server.AdminAdvanceStory(7); // B2 opens chapter 2
        Drain(server, p); // #2474: the line waits with its scene

        Assert.True(Has(p, StoryPackKeys.CutsceneSent(V, "ch2.open")));
        var sent = transport.Sent.Where(x => x.Conn == p.ConnectionId).Select(x => x.Msg).ToList();
        int scene = sent.FindIndex(m => m is CutscenePlay c && c.CutsceneId == "ch2.open");
        int line = sent.FindIndex(m => m is ShipAiLine l && l.LineKey == "story.vega.beat02");
        Assert.True(scene >= 0 && line > scene, $"scene at {scene}, line at {line}"); // the client claims the line for the scene
    }

    [Fact]
    public void OnAWonWorld_ANonHostedLatecomer_GetsTheRecap_ThenTheEnding()
    {
        var (server, transport) = World("won_latecomer");
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();
        var p = Join(server, 1, "Late");
        Assert.Empty(transport.To<StoryResolved>(p)); // not at the door (N4)

        server.SkipTutorialForTest(p);

        Assert.Single(transport.To<StoryResolved>(p));
        Assert.Equal("resolution", server.TrackChapterIdForTest(p));
        Assert.Contains(p.CutsceneQueue, q => q.Id == StoryCutsceneIds.Recap
                                              && LineText.SplitArgs(q.Arg).Contains("story.vega.ch.core"));
    }

    // ---------------- Migration P1–P9 ----------------

    [Fact]
    public void P1_MidTutorial_CountsThePrologueAsSeen_AndWaitsForTheTraining()
    {
        var repo = SaveWith("p1", "Midway", new[] { StoryLegacy.IntroMilestone, "vega:stage:mine", "vega:stage:craft", Beat(0) });
        var (server, transport) = World("p1", repo: repo);
        var p = Join(server, 1, "Midway");

        Assert.True(Has(p, PrologueSeen));
        Assert.True(Has(p, StoryPackKeys.Migrated(V)));
        Assert.DoesNotContain(StoryCutsceneIds.Prologue, transport.Plays(p));
        Assert.False(Has(p, PrologueSent));
        Assert.False(Has(p, Started));
        Assert.True(Has(p, StoryPackKeys.Started(Tutorial))); // the lessons go on
    }

    [Fact]
    public void P2_TutorialDoneMidStory_Starts_WithoutARecap()
    {
        var repo = SaveWith("p2", "Explorer", AllStages().Concat(Beats(3)));
        var (server, transport) = World("p2", repo: repo);
        server.AdminAdvanceStory(12); // the world at B3
        var p = Join(server, 1, "Explorer");
        Ready(server, p);

        Assert.True(Has(p, Started));
        Assert.Equal("service", server.TrackChapterIdForTest(p));
        Assert.DoesNotContain(StoryCutsceneIds.Recap, transport.Plays(p)); // they lived through it
        Assert.DoesNotContain(p.CutsceneQueue, q => q.Id == StoryCutsceneIds.Recap);
    }

    [Fact]
    public void P4_FinaleRevealed_StartsInChapterSeven_WithTheRevealLine()
    {
        var repo = SaveWith("p4", "Seeker", AllStages().Concat(Beats(12)));
        var (server, transport) = World("p4", repo: repo);
        server.AdminRevealFinale();
        var p = Join(server, 1, "Seeker");
        Ready(server, p);
        Drain(server, p); // #2474: the reveal line waits with chapter 7's set piece

        Assert.True(Has(p, Started));
        Assert.Equal("core", server.TrackChapterIdForTest(p));
        Assert.Contains("story.vega.guardian_revealed", transport.LineKeys(p));
        Assert.Equal(ObjectiveSources.Finale, server.ObjectiveSourceForTest("Seeker"));
    }

    [Fact]
    public void P5_Resolved_AnEndingWatchedBefore_CountsAsWatched()
    {
        var repo = SaveWith("p5", "Hero", AllStages().Concat(Beats(12)).Append(StoryPackKeys.Prefix(V) + "resolved"));
        var (server, transport) = World("p5", repo: repo);
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();
        var p = Join(server, 1, "Hero");

        Assert.True(Has(p, StoryPackKeys.CutsceneSeen(V, StoryCutsceneIds.Resolution)));
        Assert.Empty(transport.To<StoryResolved>(p));
        Assert.Equal("resolution", server.TrackChapterIdForTest(p));
    }

    [Fact]
    public void P5_Resolved_AB12HearerWhoMissedTheEnding_GetsIt_WithTheAchievement()
    {
        var repo = SaveWith("p5b", "Away", AllStages().Concat(Beats(12)));
        var (server, transport) = World("p5b", repo: repo);
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();
        var p = Join(server, 1, "Away");

        Assert.Single(transport.To<StoryResolved>(p));
        Assert.True(p.State.AchievementCounters.TryGetValue(AchievementCounters.StoryFinale, out int n) && n > 0);
    }

    [Fact]
    public void P7_OldMemories_StayAsTheyAre_AndTheMigrationIsIdempotent()
    {
        var keys = AllStages().Concat(Beats(1)).Concat(new[] { "vega:mem:1", "vega:mem:2", "vega:mem:mk3parts" }).ToList();
        var repo = SaveWith("p7", "Keeper", keys);
        var (server, _) = World("p7", repo: repo);
        server.AdminAdvanceStory(ToB1);
        var p = Join(server, 1, "Keeper");

        Assert.All(new[] { "vega:mem:1", "vega:mem:2", "vega:mem:mk3parts" }, k => Assert.True(Has(p, k)));
        var before = new HashSet<string>(p.State.Milestones);
        server.MigrateStoryTrackForTest(p);
        Assert.Equal(before, p.State.Milestones);
    }

    [Fact]
    public void P9_PlayersTheOldBugSkipped_GetTheRefresherOfferOnce()
    {
        var repo = SaveWith("p9", "Skipped", AllStages().Append(Beat(0)));
        var (server, transport) = World("p9", repo: repo);
        var p = Join(server, 1, "Skipped");

        Assert.Equal(1, transport.LineKeys(p).Count(k => k == SvGameServer.TutorialRefresherLine));
        Assert.True(Has(p, Started)); // the training counts as done (all stages)
    }

    // ---------------- The legacy memory arc, the chip ----------------

    [Fact]
    public void TheMemoryArc_ReadsAsNeutralArchiveLines_InANoneWorld_WithTheSameKnowledge()
    {
        var (server, transport) = World("mem_none", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Archivist");
        p.State.AboardShip = true;
        int knowledge = p.State.KnowledgePoints;
        p.State.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 2));
        for (int i = 0; i < 10; i++)
        {
            server.Tick(1.1);
        }

        Assert.Contains("vega:mem:1", p.State.Milestones);
        Assert.Contains("vega:mem:2", p.State.Milestones);
        Assert.Equal(knowledge + 6, p.State.KnowledgePoints);
        var keys = transport.LineKeys(p);
        Assert.Contains("vega.mem.archive.1", keys);
        Assert.Contains("vega.mem.archive.2", keys);
        Assert.DoesNotContain(keys, k => k is "vega.mem.1" or "vega.mem.2");
    }

    [Fact]
    public void TheChip_ShowsNoStoryBeforeTheTrackStarts_NotOnThePrologueNorBetweenLessons()
    {
        var (server, transport) = World("chip");
        var p = Join(server, 1, "Rookie", cutscenes: false);
        server.SkipTutorialForTest(p); // the training through — the story takes the chip only now
        var lines = transport.To<ShipAiLine>(p);
        int start = lines.FindIndex(l => l.ObjectiveSource == ObjectiveSources.Story);
        Assert.True(start > 0);
        Assert.All(lines.Take(start), l => Assert.False(l.ObjectiveKey.StartsWith("story.obj.", StringComparison.Ordinal)));
        Assert.All(lines.Where(l => l.Kind == 4), l => Assert.NotEqual(ObjectiveSources.Story, l.ObjectiveSource));
    }

    [Fact]
    public void TheStoryReport_ShowsThePlayersTrack()
    {
        var (server, transport) = World("report");
        var admin = Join(server, 1, "Admin");
        admin.State.Role = PlayerRole.Admin;
        Deliver(server, admin, new AdminCommandIntent { Command = "story_status" });
        Assert.Contains(transport.To<ServerMessage>(admin), m => m.Text.Contains("track of Admin=waiting", StringComparison.Ordinal));
    }

    // ---------------- The recap chain ----------------

    [Fact]
    public void TheRecap_LeadsStraightIntoThePacksNextScene()
    {
        var (server, transport) = World("recap_chain");
        var p = Join(server, 1, "Viewer", cutscenes: false); // the prologue as pages, so no scene is sent yet
        p.ClientFeatures.Add(ClientFeatures.Cutscenes1);
        Ready(server, p);

        Assert.True(server.PlayRecapForTest(p, V, new[] { "story.vega.ch.signature" }));
        Assert.True(server.PlayCutsceneForTest(p, V, "ch1.open"));

        Assert.Equal(new[] { StoryCutsceneIds.Recap, "ch1.open" }, transport.Plays(p)); // no 90 s cooldown in between
    }

    // ---------------- Chapters (pure) ----------------

    [Theory]
    [InlineData(1, false, false, 0)]   // B0 revealed
    [InlineData(2, false, false, 1)]   // B1
    [InlineData(5, false, false, 2)]   // B4: still "The Service"
    [InlineData(6, false, false, 3)]   // B5
    [InlineData(9, false, false, 4)]   // B8
    [InlineData(10, false, false, 5)]  // B9
    [InlineData(13, true, false, 7)]   // B12 + the Guardian revealed
    [InlineData(13, true, true, 8)]    // won
    public void TheWorldsChapter_FollowsTheBeats(int beatsRevealed, bool revealed, bool defeated, int expected)
        => Assert.Equal(expected, StoryChapters.WorldChapterIndex(_content.Stories[V], beatsRevealed, revealed, defeated));

    [Fact]
    public void APlayersChapter_IsAtLeastChapterOne_AndBeatsBelongToTheirChapter()
    {
        var vega = _content.Stories[V];
        Assert.Equal(1, StoryChapters.TrackChapterIndex(vega, 0));
        Assert.Equal(4, StoryChapters.TrackChapterIndex(vega, 4));
        Assert.Equal(new[] { 0, 1, 2, 2, 2, 3, 4, 4, 4, 5, 5, 6, 7 }, Enumerable.Range(0, 13).Select(b => StoryChapters.ChapterIndexOfBeat(vega, b)));
        Assert.Equal("service", StoryChapters.OpenedByBeat(vega, 2)?.Id);
        Assert.Null(StoryChapters.OpenedByBeat(vega, 3));
    }

    // (The built-in fallback mirrors every manifest field: StoryPackLoadTests.Data_pack_matches_the_builtin_fallback_no_drift.)

    // ---------------- The validator's world-pack chapter rules ----------------

    [Theory]
    [InlineData(13, "", "fromBeat")]               // beyond the arc
    [InlineData(-1, "comet_seen", "fromEvent")]   // an unknown arc event
    public void TheValidator_RefusesAWorldChapterThatCannotBeReached(int fromBeat, string fromEvent, string path)
    {
        var def = System.Text.Json.JsonSerializer.Deserialize<StoryDefinition>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "story.json")), ContentLoader.JsonOptions)!;
        def.Cutscenes.AddRange(_content.Stories[V].Cutscenes);
        def.Chapters.Add(new StoryChapter { Id = "extra", TitleKey = "story.vega.ch.core", FromBeat = fromBeat, FromEvent = fromEvent });
        var issues = StoryPackValidator.Validate(def, StoryPackValidationTests.Context());
        Assert.Contains(issues, i => i.Severity == StoryPackIssueSeverity.Error && i.Path.EndsWith(path, StringComparison.Ordinal));
    }

    [Fact]
    public void TheValidator_RefusesChaptersOutOfBeatOrder()
    {
        var def = System.Text.Json.JsonSerializer.Deserialize<StoryDefinition>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "story.json")), ContentLoader.JsonOptions)!;
        def.Cutscenes.AddRange(_content.Stories[V].Cutscenes);
        def.Chapters.Insert(3, new StoryChapter { Id = "back", TitleKey = "story.vega.ch.core", FromBeat = 1 });
        var issues = StoryPackValidator.Validate(def, StoryPackValidationTests.Context());
        Assert.Contains(issues, i => i.Severity == StoryPackIssueSeverity.Error && i.Path == "chapters[3].fromBeat");
    }
}
