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
/// #2498 — the world rule "Short courses only" (<see cref="TutorialPolicy.Courses"/>) and the course suggestions on a skip. A
/// newcomer on such a world starts the tutorial pack with its basic training skipped silently (no welcome card, no line, no
/// achievement): the prologue leads straight into VEGA's chapter 1, the graduation's suggested courses wait in the Journal and
/// every other course comes at its moment, after the usual gap. "Skip all — I know the game" now suggests the courses too (the
/// flight courses had no other way to be offered). A live switch leaves a running training alone; an older client is not
/// stuck (its story starts, it hears no training lines); "Start basic training" afterwards runs the training, and played
/// through, its parts are done — not skipped.
/// </summary>
public sealed class TutorialCoursesOnlyTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;
    private const string V = StoryRegistry.DefaultStoryId;
    private static readonly string[] BasicTraining = { "b1", "b2", "b3", "b4", "b5", "b6" };

    /// <summary>The graduation suggests c3, c4 and c1; a skip marks the two flight courses only — c1 has its own moment (the
    /// tool-tier wall), where VEGA offers it.</summary>
    private static readonly string[] Suggested = { "c3", "c4" };

    /// <summary>The shipped content, loaded once for the class (every test boots its own server and save on it).</summary>
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialCoursesOnlyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_courses_only_" + Guid.NewGuid().ToString("N"));
    }

    private static GameContent Content => Shipped.Value;

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

        public List<TMsg> To<TMsg>(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<TMsg>().ToList();

        public List<string> LineKeys(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();

        public List<(string Pack, string Id)> Plays(PlayerSession who) => To<CutscenePlay>(who).Select(c => (c.PackId, c.CutsceneId)).ToList();

        public List<ShipAiLine> Offers(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey == SvGameServer.ChapterOfferLine).ToList();
    }

    /// <summary>A world with the VEGA story and free flight (the pilot course needs it), on <paramref name="policy"/>.</summary>
    private (SvGameServer Server, RecordingTransport Transport) World(string world, TutorialPolicy policy = TutorialPolicy.Courses)
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
        config.Rules.FreeSpaceFlight = true;
        config.Rules.TutorialPolicy = policy;
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path. <paramref name="packs"/> false: a released (legacy) client without <c>packs1</c>.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool packs = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = packs ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : Array.Empty<string>(),
        }));
        return server.Sessions[conn];
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    /// <summary>The client is in the world, its prologue went out and was watched, and the cooldown ran out a few times —
    /// everything queued has gone.</summary>
    private static void Settle(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        server.TickCutscenesForTest(p);
        Deliver(server, p, new CutsceneSeenIntent { PackId = V, Ids = new[] { StoryCutsceneIds.Prologue } });
        for (int i = 0; i < 4; i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(p);
        }
    }

    private static StoryDefinition Pack => Content.Stories[T];

    private static StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    private static NetStoryChapter ChapterState(SvGameServer server, PlayerSession p, string chapter)
        => server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Single(c => c.Id == chapter);

    /// <summary>Plays the step that owns the tutorial's chip — raising the event its real handler raises, or reporting its UI
    /// flag (TutorialPackTests' walker) — until no step owns the chip.</summary>
    private static List<string> Walk(SvGameServer server, PlayerSession p, int maxSteps = 80)
    {
        var played = new List<string>();
        for (int i = 0; i < maxSteps; i++)
        {
            string? id = server.ChipStepForTest(p, T);
            if (id is null)
            {
                break;
            }

            var step = StepDef(id);
            string evt = step.Objective.Event;
            if (evt.EndsWith("*", StringComparison.Ordinal))
            {
                evt = GameEventKeys.All.First(k => GameEventKeys.Matches(evt, k));
            }

            string arg = (step.Objective.Arg ?? string.Empty).Split('|')[0];
            if (GameEventKeys.IsUiEvent(evt))
            {
                Deliver(server, p, new UiTutorialEvent { Id = evt.Substring(GameEventKeys.UiPrefix.Length) });
            }
            else
            {
                server.RaiseGameEventForTest(p, evt, arg, step.Objective.Required);
            }

            played.Add(id);
            server.TickStoryPacksForTest(p);
        }

        return played;
    }

    /// <summary>The basic training skipped as a whole, nothing granted, the pack stamped done, the gates open.</summary>
    private static void AssertTrainingSkipped(SvGameServer server, PlayerSession p)
    {
        Assert.True(Has(p, StoryPackKeys.Started(T)));
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.All(BasicTraining, c => Assert.True(Has(p, StoryPackKeys.ChapterSkipped(T, c)), c));
        Assert.All(BasicTraining, c => Assert.False(Has(p, StoryPackKeys.ChapterDone(T, c)), c));
        Assert.DoesNotContain("graduate", p.State.Achievements);
        Assert.False(p.State.AchievementCounters.ContainsKey("tutorial:graduated"));
        Assert.Null(server.ChipStepForTest(p, T));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.Banter));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips));
    }

    // ---------------- A newcomer on a "Short courses only" world ----------------

    [Fact]
    public void ANewcomer_SkipsTheBasicTrainingSilently_AndThePrologueLeadsIntoChapterOne()
    {
        var (server, transport) = World("newcomer");
        var p = Join(server, 1, "Rookie");

        AssertTrainingSkipped(server, p);
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.tutorial.", StringComparison.Ordinal)); // no b1 line, no send-off
        Assert.Empty(transport.Offers(p));
        Assert.False(transport.To<ShipAiLine>(p).Last().TutorialActive);

        // The story track starts at the join, for the training's reason (its start rule "chapter:tutorial:b6" holds).
        Assert.True(Has(p, StoryPackKeys.Started(V)));
        Assert.Equal(SvGameServer.StoryTrackReasons.Training, server.StoryTrackStartReasonForTest(p));

        // The graduation's suggestions wait in the Journal — Start / Later and the badge, without a line.
        Assert.All(Suggested, c => Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, c)), c));
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, p, "c3").State);
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, p, "c4").State);
        Assert.Equal(StoryChapterStates.Available, ChapterState(server, p, "c2").State); // its moment has not come
        Assert.Equal(StoryChapterStates.Available, ChapterState(server, p, "c1").State); // left to its own moment, the tier wall
        Assert.All(BasicTraining, c => Assert.Equal(StoryChapterStates.Skipped, ChapterState(server, p, c).State));

        // The cards: VEGA's prologue, then chapter 1 — never the welcome card or another card of the training.
        Settle(server, p);
        var plays = transport.Plays(p);
        int prologue = plays.IndexOf((V, StoryCutsceneIds.Prologue));
        int chapterOne = plays.IndexOf((V, "ch1.open"));
        Assert.True(prologue >= 0 && chapterOne > prologue, string.Join(", ", plays));
        Assert.DoesNotContain(plays, c => c.Pack == T);
        Assert.False(Has(p, StoryPackKeys.CutsceneSent(T, "tut.welcome")));
    }

    [Fact]
    public void ACourse_IsOfferedAtItsMoment_OnlyAfterTheGapTheSuggestionsStarted()
    {
        var (server, transport) = World("moment");
        var p = Join(server, 1, "Rookie");
        double suggestedAt = p.LastChapterOfferAt;
        Assert.False(double.IsNegativeInfinity(suggestedAt)); // the suggestions are this moment's offer

        p.VegaTipLive = new[] { "data_cache_near" }; // a data cache in reach: the research course's moment
        server.TickStoryPacksForTest(p);
        Assert.Empty(transport.Offers(p));
        while (transport.Offers(p).Count == 0 && p.StoryPackPlaySeconds < suggestedAt + 200)
        {
            server.TickStoryPacksForTest(p);
        }

        var research = Assert.Single(transport.Offers(p));
        Assert.Equal("Knowledge and research", research.LineArg);
        Assert.True(p.LastChapterOfferAt - suggestedAt >= 90, $"offered {p.LastChapterOfferAt - suggestedAt} s after the suggestions");
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, "c2")));
    }

    [Fact]
    public void StartBasicTraining_RunsTheTraining_AndPlayedThroughItsPartsAreDone_WithTheBadge()
    {
        var (server, transport) = World("start_training");
        var p = Join(server, 1, "Curious");
        AssertTrainingSkipped(server, p);

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.RestartPack }); // "Start basic training"
        Assert.Equal("b1.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.b1.walk.start", transport.LineKeys(p));

        Walk(server, p);
        Assert.Null(server.ChipStepForTest(p, T));
        Assert.Contains("graduate", p.State.Achievements); // the end of the training is a graduation
        Assert.All(BasicTraining, c => Assert.True(Has(p, StoryPackKeys.ChapterDone(T, c)), c)); // learnt now, not skipped
        Assert.All(BasicTraining, c => Assert.False(Has(p, StoryPackKeys.ChapterSkipped(T, c)), c));
        var tutorial = server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T);
        Assert.True(tutorial.Badge);
        Assert.All(tutorial.Chapters.Where(c => !c.Optional), c => Assert.Equal(StoryChapterStates.Done, c.State));
    }

    // ---------------- "Skip all" ----------------

    [Fact]
    public void SkipAll_OnAnAutoWorld_SuggestsTheFlightCourses_Silently()
    {
        var (server, transport) = World("skip_all", TutorialPolicy.Auto);
        var p = Join(server, 1, "Pro");
        Assert.Equal("b1.1", server.ChipStepForTest(p, T)); // Auto: the training runs

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });

        AssertTrainingSkipped(server, p);
        Assert.All(Suggested, c => Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, c)), c)); // c3/c4 were never offered before
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, p, "c3").State);
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, p, "c4").State);
        Assert.False(Has(p, StoryPackKeys.ChapterOffered(T, "c1"))); // the workshop course waits for its moment
        Assert.Empty(transport.Offers(p)); // silently: no line
        Assert.DoesNotContain("story.tutorial.graduated", transport.LineKeys(p));

        server.TickStoryPacksForTest(p, 60);
        Assert.Empty(transport.Offers(p)); // and the next offer waits the usual gap
    }

    // ---------------- The rule switched live ----------------

    [Fact]
    public void ALiveSwitch_LeavesARunningTrainingAlone_AndSkipsSilentlyForWhoeverHasNotStarted()
    {
        var (server, transport) = World("live_switch", TutorialPolicy.OptIn);
        var admin = server.AddLocalPlayer("Admin");
        admin.State.Role = PlayerRole.Admin;
        var trainee = Join(server, 1, "Trainee");
        var waiting = Join(server, 2, "Waiting");
        Deliver(server, trainee, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartPack });
        Assert.Equal("b1.1", server.ChipStepForTest(trainee, T));
        Assert.False(Has(waiting, StoryPackKeys.Started(T))); // on request: it never started for the other

        transport.Sent.Clear();
        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "policy courses" });
        Assert.Contains(transport.To<ServerMessage>(admin), m => m.Text == "@srv.admin.tutorial_policy:Courses");
        Assert.Equal(TutorialPolicy.Courses, server.TutorialPolicyForTest);

        // The trainee keeps the training: nothing skipped, the same step on the chip.
        Assert.Equal("b1.1", server.ChipStepForTest(trainee, T));
        Assert.All(BasicTraining, c => Assert.False(Has(trainee, StoryPackKeys.ChapterSkipped(T, c)), c));
        Assert.False(Has(trainee, StoryPackKeys.Done(T)));
        Assert.True(Walk(server, trainee).Count > 0); // and can go on with it

        // The other one gets the courses without the training — silently.
        AssertTrainingSkipped(server, waiting);
        Assert.All(Suggested, c => Assert.True(Has(waiting, StoryPackKeys.ChapterOffered(T, c)), c));
        Assert.DoesNotContain(transport.LineKeys(waiting), k => k.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.To<CutscenePlay>(waiting), c => c.PackId == T);
    }

    // ---------------- An older client ----------------

    /// <summary>A released client (no <c>packs1</c>) never stamps a part done by itself — on a "Short courses only" world it
    /// gets the silent skip too: the old onboarding reads finished (the nine legacy keys), the VEGA story starts, VEGA may
    /// chat, and it hears none of the old training lines (no intro, no send-off).</summary>
    [Fact]
    public void AnOlderClient_IsNotStuck_ItsStoryStarts_AndItHearsNoTrainingLines()
    {
        var (server, transport) = World("legacy");
        var p = Join(server, 1, "Old", packs: false);

        AssertTrainingSkipped(server, p);
        Assert.All(StoryLegacy.StageIds, s => Assert.True(Has(p, StoryLegacy.StagePrefix + s), s));
        Assert.True(Has(p, StoryLegacy.IntroMilestone));
        Assert.True(Has(p, StoryPackKeys.Started(V)));
        Assert.Equal(SvGameServer.StoryTrackReasons.Training, server.StoryTrackStartReasonForTest(p));

        var keys = transport.LineKeys(p);
        Assert.DoesNotContain(keys, k => StoryLegacy.IntroLines.Contains(k));
        Assert.DoesNotContain(StoryLegacy.DoneLine, keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.ObjectiveKey.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.False(transport.To<ShipAiLine>(p).Last().TutorialActive);
    }
}
