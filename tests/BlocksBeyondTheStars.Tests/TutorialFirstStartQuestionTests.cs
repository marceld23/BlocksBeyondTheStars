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
/// #2499 — VEGA's buttons. A newcomer whose client draws them (<see cref="ClientFeatures.Choices1"/>) is asked once, right
/// after part 1's welcome card and before the first lesson's line, whether they want the basic training: [Let's go] (the
/// continue control — the training simply runs) or [I know the game — skip] (the ordinary <c>skipPack</c> intent, answered
/// with "the training waits in your Journal"). Never for an older client, a veteran, a rejoin, a restart or another tutorial
/// rule. Course offers carry [Later] / [Start now] for such a client — the Journal's own two actions — and stay the plain
/// line for everyone else.
/// </summary>
public sealed class TutorialFirstStartQuestionTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;
    private const string V = StoryRegistry.DefaultStoryId;
    private const string FirstLesson = "story.tutorial.b1.walk.start";
    private static readonly string[] BasicTraining = { "b1", "b2", "b3", "b4", "b5", "b6" };

    /// <summary>The shipped content, loaded once for the class (every test boots its own server and save on it).</summary>
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialFirstStartQuestionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_first_question_" + Guid.NewGuid().ToString("N"));
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

        public List<ShipAiLine> Lines(PlayerSession who, string key) => To<ShipAiLine>(who).Where(l => l.LineKey == key).ToList();

        public List<(string Pack, string Id)> Plays(PlayerSession who) => To<CutscenePlay>(who).Select(c => (c.PackId, c.CutsceneId)).ToList();
    }

    /// <summary>A world with the VEGA story and free flight, on <paramref name="policy"/>. <paramref name="repo"/>: a save
    /// prepared by the test (a veteran).</summary>
    private (SvGameServer Server, RecordingTransport Transport) World(string world, TutorialPolicy policy = TutorialPolicy.Auto, SqliteWorldRepository? repo = null)
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
        repo ??= Repo(world);
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        return (server, transport);
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>The real join path. <paramref name="choices"/> false: a client without buttons (headless, an older build);
    /// <paramref name="packs"/> false: a released (legacy) client.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool choices = true, bool packs = true)
    {
        var features = new List<string>();
        if (packs)
        {
            features.Add(ClientFeatures.Packs1);
            features.Add(ClientFeatures.Cutscenes1);
        }

        if (choices)
        {
            features.Add(ClientFeatures.Choices1);
        }

        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = features.ToArray(),
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

    private static (string Label, string Action, string Pack, string Chapter) Decode(string entry)
    {
        Assert.True(ShipAiChoices.TryDecode(entry, out string label, out string action, out string pack, out string chapter), entry);
        return (label, action, pack, chapter);
    }

    private static void AssertTrainingSkipped(PlayerSession p)
    {
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.All(BasicTraining, c => Assert.True(Has(p, StoryPackKeys.ChapterSkipped(T, c)), c));
        Assert.DoesNotContain("graduate", p.State.Achievements);
    }

    // ---------------- The question ----------------

    [Fact]
    public void ANewcomer_IsAskedOnce_AfterTheWelcomeCard_BeforeTheFirstLesson_AndTheTrainingRunsMeanwhile()
    {
        var (server, transport) = World("asked");
        var p = Join(server, 1, "Rookie");

        var ask = Assert.Single(transport.Lines(p, SvGameServer.AskSkipLine));
        Assert.Equal(T, ask.PackId);
        Assert.Equal(0, ask.Kind);
        Assert.Equal(2, ask.Choices.Length);
        Assert.Equal((SvGameServer.ChoiceLetsGo, ShipAiChoices.Continue, T, string.Empty), Decode(ask.Choices[0])); // the default
        Assert.Equal((SvGameServer.ChoiceKnowGame, StoryPackActions.SkipPack, T, string.Empty), Decode(ask.Choices[1]));

        // The welcome card is committed before the question (it goes out once the client is in the world, and the panel waits
        // for it); the question comes before the first lesson's line, which waits behind it.
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.welcome")));
        var keys = transport.LineKeys(p);
        Assert.True(keys.IndexOf(SvGameServer.AskSkipLine) < keys.IndexOf(FirstLesson), string.Join(", ", keys));
        Settle(server, p);
        Assert.Contains((T, "tut.welcome"), transport.Plays(p));

        // Doing nothing is "Let's go": nothing waits for the answer — the first lesson is on the chip and counts.
        Assert.Equal("b1.1", server.ChipStepForTest(p, T));
        server.RaiseGameEventForTest(p, GameEventKeys.MoveWalked, string.Empty, 8);
        server.TickStoryPacksForTest(p, 5);
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "b1.1")));
        Assert.Single(transport.Lines(p, SvGameServer.AskSkipLine)); // asked once
        Assert.DoesNotContain(transport.LineKeys(p), k => k == SvGameServer.SkippedHintLine);
    }

    [Fact]
    public void AClientWithoutButtons_IsNeverAsked_AndHearsTheTrainingAsBefore()
    {
        var (server, transport) = World("no_buttons");
        var p = Join(server, 1, "Headless", choices: false);
        var old = Join(server, 2, "Released", choices: false, packs: false);

        Assert.Empty(transport.Lines(p, SvGameServer.AskSkipLine));
        Assert.Contains(FirstLesson, transport.LineKeys(p));
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.Choices.Length > 0);
        Assert.DoesNotContain(transport.To<ShipAiLine>(old), l => l.Choices.Length > 0 || l.LineKey == SvGameServer.AskSkipLine);
    }

    [Fact]
    public void ARejoin_IsNotAFirstStart()
    {
        var (server, transport) = World("rejoin");
        var p = Join(server, 1, "Back", choices: false); // the training started on a client without buttons
        Assert.Equal("b1.1", server.ChipStepForTest(p, T));

        server.DisconnectLocalPlayerForTest(p.State.PlayerId);
        transport.Sent.Clear();
        var again = Join(server, 2, "Back");
        Assert.Equal("b1.1", server.ChipStepForTest(again, T));
        Assert.Empty(transport.Lines(again, SvGameServer.AskSkipLine));
    }

    [Fact]
    public void AVeteran_IsNotAsked()
    {
        var repo = Repo("veteran");
        repo.Initialize();
        repo.SavePlayer(new PlayerState { PlayerId = "Vet", Name = "Vet", KnowledgePoints = 5 }); // earlier play, before VEGA's lessons
        var (server, transport) = World("veteran", repo: repo);
        var p = Join(server, 1, "Vet");

        Assert.False(p.Join.Newcomer);
        Assert.Empty(transport.Lines(p, SvGameServer.AskSkipLine));
    }

    [Theory]
    [InlineData(TutorialPolicy.Courses)]
    [InlineData(TutorialPolicy.OptIn)]
    [InlineData(TutorialPolicy.Off)]
    public void AnotherTutorialRule_NeverAsks_NotEvenAStartOnRequest(TutorialPolicy policy)
    {
        var (server, transport) = World("rule_" + policy, policy);
        var p = Join(server, 1, "Rookie");
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartPack }); // opt-in: "start" from the Journal
        server.TickStoryPacksForTest(p, 3);

        Assert.Empty(transport.Lines(p, SvGameServer.AskSkipLine));
        if (policy == TutorialPolicy.OptIn)
        {
            Assert.Equal("b1.1", server.ChipStepForTest(p, T)); // the player asked for it — no question
        }
    }

    [Fact]
    public void ARestart_AfterTheSkip_IsNotAskedAgain()
    {
        var (server, transport) = World("restart");
        var p = Join(server, 1, "Undecided");
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        AssertTrainingSkipped(p);

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.RestartPack }); // "Start basic training"
        Assert.Equal("b1.1", server.ChipStepForTest(p, T));
        Assert.Single(transport.Lines(p, SvGameServer.AskSkipLine));
        Assert.Equal(2, transport.LineKeys(p).Count(k => k == FirstLesson)); // the lesson again, without a question
    }

    // ---------------- The answer ----------------

    [Fact]
    public void SkippingFromTheQuestion_SkipsTheTraining_AndVegaSaysWhereItWaits_BeforeTheStoryGoesOn()
    {
        var (server, transport) = World("skip");
        var p = Join(server, 1, "Pro");
        int before = transport.To<ShipAiLine>(p).Count;

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });

        AssertTrainingSkipped(p);
        Assert.Null(server.ChipStepForTest(p, T));
        var after = transport.To<ShipAiLine>(p).Skip(before).ToList();
        var hint = Assert.Single(after, l => l.LineKey == SvGameServer.SkippedHintLine);
        Assert.Equal(T, hint.PackId);
        Assert.Empty(hint.Choices);
        int hintAt = after.IndexOf(hint);
        Assert.DoesNotContain(after.Take(hintAt), l => l.LineKey.StartsWith("story.vega.", StringComparison.Ordinal)); // the hint first
        Assert.True(Has(p, StoryPackKeys.Started(V))); // the story starts as after any skip

        // A second skip (the Journal) has nothing left to answer.
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.Single(transport.Lines(p, SvGameServer.SkippedHintLine));
    }

    [Fact]
    public void TheSkip_GetsThroughAHeldWorld()
    {
        var (server, transport) = World("held");
        var p = Join(server, 1, "Solo");
        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });

        AssertTrainingSkipped(p);
        Assert.Single(transport.Lines(p, SvGameServer.SkippedHintLine));
        Assert.True(server.IsPaused);
    }

    [Fact]
    public void AJournalSkip_WithoutTheQuestion_GetsNoHint()
    {
        var (server, transport) = World("journal_skip");
        var p = Join(server, 1, "Headless", choices: false);
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });

        AssertTrainingSkipped(p);
        Assert.Empty(transport.Lines(p, SvGameServer.SkippedHintLine));
    }

    [Fact]
    public void ASkipAfterTheTrainingIsThrough_GetsNoHint()
    {
        var (server, transport) = World("late_skip");
        var p = Join(server, 1, "Done");
        foreach (string chapter in BasicTraining)
        {
            p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, chapter)); // played through, the question never answered
        }

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.Empty(transport.Lines(p, SvGameServer.SkippedHintLine));
    }

    // ---------------- Course offers ----------------

    [Fact]
    public void ACourseOffer_CarriesLaterAndStartNow_ForAClientWithButtons_AndStaysThePlainLineForOthers()
    {
        var (server, transport) = World("offers", TutorialPolicy.Courses);
        var p = Join(server, 1, "Buttons");
        var plain = Join(server, 2, "Plain", choices: false);
        foreach (var who in new[] { p, plain })
        {
            who.VegaTipLive = new[] { "data_cache_near" }; // a data cache in reach: the research course's moment
            for (int i = 0; i < 200 && !Has(who, StoryPackKeys.ChapterOffered(T, "c2")); i++)
            {
                server.TickStoryPacksForTest(who);
            }
        }

        var offer = Assert.Single(transport.Lines(p, SvGameServer.ChapterOfferChoiceLine));
        Assert.Equal("Knowledge and research", offer.LineArg);
        Assert.Equal(T, offer.PackId);
        Assert.Equal(2, offer.Choices.Length);
        Assert.Equal((SvGameServer.ChoiceLater, StoryPackActions.DeclineChapter, T, "c2"), Decode(offer.Choices[0])); // the default
        Assert.Equal((SvGameServer.ChoiceStartNow, StoryPackActions.StartChapter, T, "c2"), Decode(offer.Choices[1]));
        Assert.Empty(transport.Lines(p, SvGameServer.ChapterOfferLine));

        var old = Assert.Single(transport.Lines(plain, SvGameServer.ChapterOfferLine));
        Assert.Equal("Knowledge and research", old.LineArg);
        Assert.Empty(old.Choices);
        Assert.Empty(transport.Lines(plain, SvGameServer.ChapterOfferChoiceLine));

        // [Start now] is the Journal's Start; [Later] the Journal's Later.
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = "c2" });
        Assert.True(Has(p, StoryPackKeys.ChapterActive(T, "c2")));
        Deliver(server, plain, new StoryPackIntent { PackId = T, Action = StoryPackActions.DeclineChapter, ChapterId = "c2" });
        Assert.True(Has(plain, StoryPackKeys.ChapterDeclined(T, "c2")));
    }

    // ---------------- Wire ----------------

    [Fact]
    public void TheChoices_RoundTrip_AndDefaultToNone()
    {
        Assert.Empty(new ShipAiLine().Choices);
        string entry = ShipAiChoices.Encode("ui.vega.choice.later", StoryPackActions.DeclineChapter, T, "c2");
        Assert.Equal("ui.vega.choice.later\u001FdeclineChapter\u001Ftutorial\u001Fc2", entry);
        var line = Assert.IsType<ShipAiLine>(NetCodec.Decode(NetCodec.Encode(new ShipAiLine
        {
            LineKey = SvGameServer.AskSkipLine,
            Choices = new[] { ShipAiChoices.Encode(SvGameServer.ChoiceLetsGo, ShipAiChoices.Continue, T), entry },
        })));
        Assert.Equal((SvGameServer.ChoiceLetsGo, ShipAiChoices.Continue, T, string.Empty), Decode(line.Choices[0]));
        Assert.Equal(("ui.vega.choice.later", StoryPackActions.DeclineChapter, T, "c2"), Decode(line.Choices[1]));

        // A separator inside a part never splits it; a garbled entry does not decode.
        Assert.Equal(("ab", "cd", "ef", string.Empty), Decode(ShipAiChoices.Encode("a\u001Fb", "cd", "ef")));
        Assert.False(ShipAiChoices.TryDecode("only-a-label", out _, out _, out _, out _));
        Assert.False(ShipAiChoices.TryDecode("\u001FskipPack\u001Ftutorial", out _, out _, out _, out _));

        var join = Assert.IsType<JoinRequest>(NetCodec.Decode(NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            Features = new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1, ClientFeatures.Choices1 },
        })));
        Assert.Contains("choices1", join.Features);
    }

    [Fact]
    public void TheNewKeys_AreInEnglishAndGerman()
    {
        var en = Content.CreateLocalizer(BlocksBeyondTheStars.Shared.Localization.GameLocale.English);
        var de = Content.CreateLocalizer(BlocksBeyondTheStars.Shared.Localization.GameLocale.German);
        foreach (string key in new[]
        {
            SvGameServer.AskSkipLine, SvGameServer.SkippedHintLine, SvGameServer.ChapterOfferChoiceLine,
            SvGameServer.ChoiceLetsGo, SvGameServer.ChoiceKnowGame, SvGameServer.ChoiceStartNow, SvGameServer.ChoiceLater,
            "ui.vega.choice.hold",
        })
        {
            Assert.True(en.Has(key), key);
            Assert.True(de.Has(key), key);
            Assert.NotEqual(en.Get(key), de.Get(key));
        }
    }
}
