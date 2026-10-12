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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2479 — step 18 of the story packs (#2459): the tutorial's deep dives of the parts "Research" and "Flying" — C2 Knowledge
/// and research, C3 Pilot, C4 Stations, trade and jobs, C5 Navigator (synthesis §3.4). Their shape (optional chapters, hard
/// prerequisites, the moment-of-need offers, the graduation's suggestions, the course card and the two firsts); the offers
/// (never during the basic training, at least 90 s apart, "Later" keeps a course startable); the Journal's offer path from
/// the buttons to a finished course; every new verb raised by its real gameplay handler; and a walk through every course
/// with the real handlers (docking, the market, a board job taken and handed in, the ship scanner, an asteroid, the ship's
/// interior and a spacewalk, a hyperjump with a fitted jump generator), also with the world rules that switch steps off.
/// </summary>
public sealed class TutorialDeepDivesTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialDeepDivesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_deep_dives_" + Guid.NewGuid().ToString("N"));
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

        public List<TMsg> To<TMsg>(PlayerSession who) => Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<TMsg>().ToList();

        public List<string> LineKeys(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();

        public List<string> Plays(PlayerSession who) => To<CutscenePlay>(who).Select(c => c.CutsceneId).ToList();

        public List<ShipAiLine> Offers(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey == SvGameServer.ChapterOfferLine).ToList();
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>A world with free flight (the flight courses need it) and, unless a test wants it, no world story — the
    /// courses are the tutorial's, and VEGA's chapters would only add cards and lines around them.</summary>
    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null)
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
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path as a current client (packs and cutscenes).</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 },
        }));
        return server.Sessions[conn];
    }

    /// <summary>A local player (the one the flight and station seams drive) past the basic training ("I know the game"), so
    /// every course is available; <paramref name="cutscenes"/> makes it a client that plays them.</summary>
    private static PlayerSession LocalPastTraining(SvGameServer server, string name, bool cutscenes = false)
    {
        var p = server.AddLocalPlayer(name);
        if (cutscenes)
        {
            p.ClientFeatures.Add(ClientFeatures.Cutscenes1);
        }

        server.TickStoryPacksForTest(p);
        server.SkipTutorialForTest(p);
        Assert.Contains(StoryPackKeys.Done(T), p.State.Milestones);
        return p;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Start(SvGameServer server, PlayerSession p, string chapter)
        => Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = chapter });

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static bool Done(PlayerSession p, string step) => Has(p, StoryPackKeys.StepDone(T, step));

    private static bool Skipped(PlayerSession p, string step) => Has(p, StoryPackKeys.StepSkipped(T, step));

    private static void Give(PlayerSession p, string item, int count) => p.State.Inventory.Add(item, count, 999);

    private StoryDefinition Pack => _content.Stories[T];

    private StoryChapter Chapter(string id) => Pack.Chapters.Single(c => c.Id == id);

    private StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    private CutsceneDefinition Card(string id) => Pack.FindCutscene(id)!;

    /// <summary>The client is in the world, and the 90 s cooldown ran out — queued cards go out.</summary>
    private static void Cooldown(SvGameServer server, PlayerSession p)
    {
        server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
        server.TickCutscenesForTest(p);
    }

    /// <summary>Plays the step that owns the tutorial's chip — raising the event its real handler raises (the handler tests
    /// below), or reporting its UI flag — while <paramref name="keepGoing"/> says so (TutorialPackTests' walker).</summary>
    private List<string> Walk(SvGameServer server, PlayerSession p, Func<string, bool>? keepGoing = null, int maxSteps = 80)
    {
        var played = new List<string>();
        for (int i = 0; i < maxSteps; i++)
        {
            string? id = server.ChipStepForTest(p, T);
            if (id is null || (keepGoing is not null && !keepGoing(id)))
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

    /// <summary>The basic training played through (a graduation, not a skip).</summary>
    private void Graduate(SvGameServer server, PlayerSession p)
    {
        Walk(server, p, id => id.StartsWith("b", StringComparison.Ordinal));
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.Contains("graduate", p.State.Achievements);
    }

    private static NetStoryChapter ChapterState(SvGameServer server, PlayerSession p, string chapter)
        => server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Single(c => c.Id == chapter);

    // ---------------- The courses ----------------

    [Fact]
    public void TheDeepDives_AreOptionalChapters_WithTheirPrerequisitesOffersAndCards()
    {
        var courses = Pack.Chapters.Where(c => c.Optional).ToList();
        Assert.Equal(new[] { "c1", "c2", "c3", "c4", "c5", "c6", "c7", "c17", "c8", "c14", "c9", "c10", "c11", "c12", "c13", "c15", "c16", "c18" },
            courses.Select(c => c.Id)); // #2480 added c1, c6, c7, c17; #2481 c8, c14; #2482 the part "More to discover"
        foreach (var c in courses)
        {
            Assert.Equal("tut.course", c.OpenCutscene);
            Assert.Equal("tut.done", c.CloseCutscene);
            Assert.NotEmpty(c.Requires); // never during the training's earlier parts by accident
            Assert.All(c.Steps, s => Assert.StartsWith(c.Id + ".", s.Id));
            Assert.All(c.Steps, s => Assert.Equal(string.Empty, s.LegacyKey)); // older builds never knew them
        }

        Assert.Equal(new[] { "chapter:tutorial:b5" }, Chapter("c2").Requires);
        Assert.Equal(new[] { "chapter:tutorial:b6", "world:free_flight" }, Chapter("c3").Requires);
        Assert.Equal(new[] { "chapter:tutorial:b6" }, Chapter("c4").Requires);
        Assert.Equal(new[] { "chapter:tutorial:c3" }, Chapter("c5").Requires); // the pilot course first (synthesis §3.2)
        Assert.Equal(new[] { "any:[world:data_cache_near,world:scanner_unknown]" }, Chapter("c2").OfferWhen);
        Assert.Empty(Chapter("c3").OfferWhen); // suggested at the graduation
        Assert.Empty(Chapter("c4").OfferWhen);
        Assert.Equal(new[] { "any:[chapter:tutorial:c3,world:jump_ready]" }, Chapter("c5").OfferWhen);
        Assert.Equal(new[] { "c3", "c4", "c1" }, Pack.Graduation.Suggest); // #2480: the workshop course third

        Assert.Equal("tut.first_dock", StepDef("c4.1").OnComplete.Cutscene);
        Assert.Equal("tut.first_jump", StepDef("c5.8").OnComplete.Cutscene);
        Assert.Empty(StoryPackValidator.Validate(Pack, StoryPackValidationTests.Context()).Where(i => i.Severity == StoryPackIssueSeverity.Error));
    }

    [Fact]
    public void EveryCourseStep_ListensToAnEventTheServerRaises_OrAFlagTheClientReports()
    {
        foreach (var step in Pack.Chapters.Where(c => c.Optional).SelectMany(c => c.Steps).Where(s => s.Kind != StoryStepKinds.Read))
        {
            if (step.Objective.IsMission)
            {
                Assert.True(_content.Missions.ContainsKey(step.Objective.Target), $"step {step.Id} bridges onto a missing mission"); // #2481: C8
                continue;
            }

            string evt = step.Objective.Event;
            bool known = GameEventKeys.IsUiEvent(evt)
                ? GameEventKeys.IsKnownUiEventId(evt.Substring(GameEventKeys.UiPrefix.Length))
                : GameEventKeys.All.Contains(evt);
            Assert.True(known, $"step {step.Id} listens to '{evt}', which nothing raises");
        }

        Assert.Contains(GameEventKeys.UiProgressOpenId, GameEventKeys.UiEvents);
    }

    [Fact]
    public void TheCourseCards_AreCardsAndFirsts_AtMostTwoPerCourse()
    {
        var dock = Card("tut.first_dock");
        Assert.Equal(CutsceneTiers.Card, dock.Tier);
        Assert.Equal(CutsceneArgSources.Body, dock.Arg); // "Docked" + the station's name
        Assert.False(dock.Options.CooldownExempt); // it waits for the station's loading veil: no "now or never"

        var jump = Card("tut.first_jump");
        Assert.Equal(CutsceneTiers.Card, jump.Tier);
        Assert.Equal(CutsceneArgSources.System, jump.Arg); // "New system" + its name
        Assert.True(jump.Options.CooldownExempt); // on the warp-in, like the finale's arrival card
        Assert.True(jump.Shots[0].Captions[0].At >= 2.5f, "the title waits for the warp");

        // A course is short: its course card and at most one first — never a card per step (synthesis §6.4).
        foreach (var course in Pack.Chapters.Where(c => c.Optional))
        {
            var cards = new[] { course.OpenCutscene }
                .Concat(course.Steps.SelectMany(s => new[] { s.OnStart.Cutscene, s.OnComplete.Cutscene }))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(Card)
                .Where(c => c.Tier >= CutsceneTiers.Card)
                .ToList();
            Assert.True(cards.Count <= 2, $"{course.Id} has {cards.Count} cards or scenes");
        }
    }

    // ---------------- Offers ----------------

    [Fact]
    public void NoOfferDuringTheTraining_TheGraduationSuggestsTheFlightCourses_AndOffersComeAtLeast90sApart()
    {
        var (server, transport) = World("offers");
        var p = Join(server, 1, "Rookie");

        // A data cache in reach during the training: VEGA's tip pass sees it, but no course is offered yet.
        Walk(server, p, id => !id.StartsWith("b6.", StringComparison.Ordinal)); // through part 5
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "b5")));
        p.VegaTipLive = new[] { "data_cache_near" };
        server.TickStoryPacksForTest(p, 120);
        Assert.Empty(transport.Offers(p));
        Assert.Equal(StoryChapterStates.Available, ChapterState(server, p, "c2").State); // startable from the Journal already

        // The graduation suggests the two flight courses — in the Journal, without a line.
        Walk(server, p);
        Assert.Contains("graduate", p.State.Achievements);
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, "c3")));
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, "c4")));
        Assert.False(Has(p, StoryPackKeys.ChapterOffered(T, "c2"))); // C2 waits for its own moment
        Assert.False(Has(p, StoryPackKeys.ChapterOffered(T, "c5"))); // C5 needs the pilot course
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, p, "c3").State);
        var navigator = ChapterState(server, p, "c5");
        Assert.Equal(StoryChapterStates.Locked, navigator.State);
        Assert.Equal(new[] { "story.tutorial.ch.c3" }, navigator.RequiresKeys); // "needs: Pilot"
        Assert.Empty(transport.Offers(p));

        // The data cache is still in reach: its course is offered — but not right on top of the graduation (its suggestions
        // count as this moment's offer).
        double graduatedAt = p.LastChapterOfferAt;
        Assert.True(graduatedAt > 0, "the graduation's suggestions start the offer gap");
        server.TickStoryPacksForTest(p);
        Assert.Empty(transport.Offers(p));
        while (transport.Offers(p).Count == 0 && p.StoryPackPlaySeconds < graduatedAt + 200)
        {
            server.TickStoryPacksForTest(p);
        }

        var research = Assert.Single(transport.Offers(p));
        Assert.Equal("Knowledge and research", research.LineArg);
        double researchAt = p.LastChapterOfferAt;
        Assert.True(researchAt - graduatedAt >= 90, $"offered {researchAt - graduatedAt} s after the graduation");

        // The pilot course done: the navigator is offered next — again only after the gap.
        Start(server, p, "c3");
        Walk(server, p);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c3")));
        while (transport.Offers(p).Count < 2 && p.StoryPackPlaySeconds < researchAt + 200)
        {
            server.TickStoryPacksForTest(p);
        }

        Assert.Equal(2, transport.Offers(p).Count);
        Assert.Equal("Navigator", transport.Offers(p)[1].LineArg);
        Assert.True(p.LastChapterOfferAt - researchAt >= 90, "two offers are at least 90 s apart");

        server.TickStoryPacksForTest(p, 300);
        Assert.Equal(2, transport.Offers(p).Count); // each course once
    }

    /// <summary>The Journal's offer path on the server (the buttons themselves — Client.Core's <c>JournalPackPage</c> — are
    /// pressed by a real client in <c>TutorialDeepDivesClientTests</c>): an offered course started, its steps, its end; a
    /// course put off with "Later" stays startable and is never offered again.</summary>
    [Fact]
    public void TheJournalsOfferPath_StartsACourseThatRunsToItsEnd_AndLaterKeepsItStartable()
    {
        var (server, transport) = World("journal_offer");
        var p = Join(server, 1, "Kid");
        server.AdvanceUptimeForTest(SvGameServer.CutsceneJoinSettleSeconds);
        Graduate(server, p);
        Cooldown(server, p);

        // The offered pilot course ("Start" / "Later" in the Journal) — "Start" is pressed.
        Assert.Equal(StoryChapterStates.Offered, ChapterState(server, p, "c3").State);
        Start(server, p, "c3");
        Assert.True(Has(p, StoryPackKeys.ChapterActive(T, "c3")));
        Assert.Equal("c3.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c3.waypoint.start", transport.LineKeys(p));
        Assert.Equal(StoryChapterStates.Active, ChapterState(server, p, "c3").State);
        var task = server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Steps.Single(s => s.OnChip);
        Assert.Equal(("c3.1", "story.tutorial.c3.waypoint.chip", 1, 5), (task.Id, task.ChipKey, task.Index, task.Count)); // "Step 1 of 5"
        Cooldown(server, p);
        var course = Assert.Single(transport.To<CutscenePlay>(p), c => c.CutsceneId == "tut.course@c3");
        Assert.StartsWith("story.tutorial.ch.c3", course.Arg); // "Short course" + the course's title
        var objective = server.ObjectiveListForTest(p).Items[0];
        Assert.Equal(("tutorial", "c3.1"), (objective.PackId, objective.StepId)); // the course owns the chip after the training

        // Its steps, then the course is done: the part banner, Replay in the Journal.
        var played = Walk(server, p);
        Assert.Equal(new[] { "c3.1", "c3.2", "c3.3", "c3.4", "c3.5" }, played);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c3")));
        Assert.False(Has(p, StoryPackKeys.ChapterActive(T, "c3")));
        Assert.Contains("tut.done@c3", transport.Plays(p));
        Assert.True(Skipped(p, "c3.6")); // target lock only where something shoots back
        Assert.Equal(StoryChapterStates.Done, ChapterState(server, p, "c3").State);
        Assert.Equal(1, p.State.AchievementCounters["tutorial:graduated"]); // a course pays nothing and is no second graduation

        // "Later" to the stations course: it stays in the Journal (startable), VEGA never offers it, and it starts later.
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.DeclineChapter, ChapterId = "c4" });
        Assert.Equal(StoryChapterStates.Declined, ChapterState(server, p, "c4").State);
        server.TickStoryPacksForTest(p, 200);
        Assert.DoesNotContain(transport.Offers(p), o => o.LineArg == "Stations, trade and jobs");
        Start(server, p, "c4");
        Assert.True(Has(p, StoryPackKeys.ChapterActive(T, "c4")));
        Assert.False(Has(p, StoryPackKeys.ChapterDeclined(T, "c4")));
        Assert.StartsWith("c4.", server.ChipStepForTest(p, T));

        // A replayed course shows no course card again: every card once per player.
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.ReplayChapter, ChapterId = "c3" });
        Walk(server, p, id => id.StartsWith("c3.", StringComparison.Ordinal));
        Cooldown(server, p);
        Assert.Equal(1, transport.Plays(p).Count(id => id == "tut.course@c3"));
        Assert.Equal(1, transport.Plays(p).Count(id => id == "tut.done@c3"));
    }

    // ---------------- The new verbs, raised by their real handlers ----------------

    /// <summary>A world with a wildcard listener (so the "only while a step listens" paths run) and one local player.</summary>
    private (SvGameServer Server, RecordingGameEventListener Events, PlayerSession Player) Verbs(string world, Action<ServerConfig>? configure = null)
    {
        var (server, _) = World(world, configure);
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = server.AddLocalPlayer("Doer");
        return (server, events, p);
    }

    [Fact]
    public void ADataQubeGame_TheTerrainScanner_AndTheAchievementsTab_AreReportedFromTheirHandlers()
    {
        var (server, events, p) = Verbs("verb_research");
        string id = p.State.PlayerId;

        server.ReportMinigameResultForTest(id, "blockfall", rating: 2);
        server.ReportMinigameResultForTest(id, "blockfall", rating: 2); // a replay pays nothing — but it was played
        Assert.Equal(2, events.Of(GameEventKeys.MinigamePlayed).Count(e => e.Arg == "blockfall"));
        server.ReportMinigameResultForTest(id, "blockfall", rating: 3, completed: false);
        Assert.Equal(2, events.Of(GameEventKeys.MinigamePlayed).Count()); // an abandoned run is no game played

        p.State.SuitEnergy = 0f;
        Give(p, "terrain_scanner", 1);
        server.UseGadgetForTest(id, "terrain_scanner", p.State.Position);
        Assert.Empty(events.Of(GameEventKeys.GadgetUsed)); // refused (no energy): nothing happened
        p.State.SuitEnergy = 100f;
        server.UseGadgetForTest(id, "terrain_scanner", p.State.Position);
        Assert.Equal("terrain_scanner", Assert.Single(events.Of(GameEventKeys.GadgetUsed)).Arg);

        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiProgressOpenId });
        Assert.Single(events.Of(GameEventKeys.UiProgressOpen));
    }

    [Fact]
    public void ThePlanetScan_AnAsteroid_TheInterior_AndASpacewalk_AreReportedFromTheirHandlers()
    {
        var (server, events, p) = Verbs("verb_pilot", c => c.Rules.AsteroidDestruction = AsteroidDestructionMode.MiningOnly);
        string id = p.State.PlayerId;
        server.Ship.Modules.Add("asteroid_breaker");
        server.EnterSpace(id);

        Deliver(server, p, new PlanetScanIntent { BodyId = string.Empty }); // the ship scanner's overview of the body below
        Assert.Equal(server.Galaxy.FindBody(p.CurrentLocationId)!.Id, Assert.Single(events.Of(GameEventKeys.PlanetScanned)).Arg);

        for (int i = 0; i < 24 && !events.Of(GameEventKeys.AsteroidDestroyed).Any(); i++)
        {
            var rock = server.SpaceEntitiesFor(id).FirstOrDefault(e => e.Kind == CombatEntityKind.Asteroid);
            Assert.NotNull(rock);
            server.FireWeapon(id, "asteroid_breaker", rock!.Id);
            server.TickForTest(2.0); // the weapon's cooldown
        }

        Assert.NotEmpty(events.Of(GameEventKeys.AsteroidDestroyed));

        Deliver(server, p, new SetEvaIntent { Active = true }); // from the flight view
        Assert.Equal(GameEventKeys.EvaFlight, Assert.Single(events.Of(GameEventKeys.EvaStarted)).Arg);
        Deliver(server, p, new SetEvaIntent { Active = true }); // already outside: no second start
        Assert.Single(events.Of(GameEventKeys.EvaStarted));
        Deliver(server, p, new SetEvaIntent { Active = false });

        server.EnterShipInterior(id);
        Assert.Single(events.Of(GameEventKeys.InteriorEntered));
        server.StartEvaFromShip(id); // out through the hatch
        Assert.Contains(events.Of(GameEventKeys.EvaStarted), e => e.Arg == GameEventKeys.EvaHatch);
    }

    [Fact]
    public void FloatingAboveTheAir_AMarker_APing_AndUndocking_AreReportedFromTheirHandlers()
    {
        var (server, events, p) = Verbs("verb_nav", c => c.World.SpaceStations = Frequency.Frequent);
        string id = p.State.PlayerId;

        var ground = p.State.Position;
        server.SetMarkerForTest(id, ground.X + 5f, ground.Y, ground.Z);
        server.PingForTest(id, ground.X + 2f, ground.Y, ground.Z);
        Assert.Equal(new[] { GameEventKeys.MarkerSetArg, GameEventKeys.MarkerPingArg }, events.Of(GameEventKeys.MarkerSet).Select(e => e.Arg));

        p.State.AboardShip = false;
        p.State.Position = new Vector3f(ground.X, (float)server.AtmosphereHeight + 10f, ground.Z);
        server.Tick(1.0);
        Assert.True(p.State.AboveAtmosphere);
        Assert.Single(events.Of(GameEventKeys.PlayerZeroG));
        p.State.Position = ground;
        server.Tick(1.0);

        p.State.AboardShip = true;
        server.EnterSpace(id);
        var station = server.SpaceEntitiesFor(id).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove(id, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(id, station.Id);
        Assert.Contains(events.Of(GameEventKeys.StationBoarded), e => e.Arg == station.Id);
        server.LeaveStation(id);
        Assert.Equal(station.Id, Assert.Single(events.Of(GameEventKeys.StationLeft)).Arg);
        server.LeaveStation(id); // not aboard any more: nothing to leave
        Assert.Single(events.Of(GameEventKeys.StationLeft));
    }

    [Fact]
    public void AWreckReached_AndASalvageCapsuleCollected_AreSalvageFound()
    {
        var (server, events, p) = Verbs("verb_salvage", c =>
        {
            c.Seed = 11;
            c.World.Wrecks = Frequency.Frequent;
            c.World.DebrisFields = Frequency.Frequent;
            c.Rules.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        });
        string id = p.State.PlayerId;
        server.Ship.Modules.Add("jump_generator"); // to park in a system that holds both, if home does not

        var system = server.Galaxy.Systems
            .OrderBy(s => s.Bodies.Any(b => b.Id == p.CurrentLocationId) ? 0 : 1)
            .First(s => s.Bodies.Any(b => b.Kind == CelestialKind.Wreck) && s.Bodies.Any(b => b.Kind == CelestialKind.DebrisField)
                        && s.Bodies.Any(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon && !string.IsNullOrEmpty(b.PlanetType)));
        var anchor = system.Bodies.First(b => b.Kind is CelestialKind.Planet or CelestialKind.Moon && !string.IsNullOrEmpty(b.PlanetType));
        if (p.CurrentLocationId != anchor.Id)
        {
            server.Travel(id, anchor.Id);
        }

        server.EnterSpace(id);
        var wreck = system.Bodies.First(b => b.Kind == CelestialKind.Wreck);
        var hull = server.SpaceEntitiesFor(id).First(e => e.Id == wreck.Id);
        server.ShipMove(id, hull.Position.X, hull.Position.Y, hull.Position.Z - 30f); // the approach IS the visit
        Assert.Contains(events.Of(GameEventKeys.SalvageFound), e => e.Arg == GameEventKeys.SalvageWreck);

        var capsule = server.SpaceEntitiesFor(id).First(e => e.Kind == CombatEntityKind.SalvageCapsule);
        server.ShipMove(id, capsule.Position.X + 4f, capsule.Position.Y, capsule.Position.Z); // the tractor pulls it in
        server.TickForTest(0.5);
        Assert.Contains(events.Of(GameEventKeys.SalvageFound), e => e.Arg == GameEventKeys.SalvageCapsule);
    }

    // ---------------- C2 Knowledge and research ----------------

    [Fact]
    public void TheResearchCourse_WalksThroughTheRealHandlers_WithTheReadStandInForTheTerrainScanner()
    {
        var (server, transport) = World("c2_walk");
        var p = LocalPastTraining(server, "Scholar");
        string id = p.State.PlayerId;
        p.State.UnlockedGames.Add("blockfall"); // a DataQube game in the arcade: the cube lesson can be done
        Start(server, p, "c2");
        Assert.Equal("c2.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c2.kinds.start", transport.LineKeys(p));

        foreach (string block in new[] { "stone", "dirt", "iron_ore" })
        {
            server.ScanSubject(id, "block", block); // three things never scanned before
        }

        Assert.True(Done(p, "c2.1"));
        Assert.Equal("c2.2", server.ChipStepForTest(p, T));

        var cache = new Vector3i(4, 60, 20);
        p.State.Position = new Vector3f(cache.X + 1.2f, cache.Y + 0.5f, cache.Z + 0.5f);
        server.World.SetBlock(cache, _content.GetBlock("data_cache")!.NumericId);
        server.MineBlock(id, cache.X, cache.Y, cache.Z);
        Assert.True(Done(p, "c2.2"));

        p.State.KnowledgePoints = 20;
        Give(p, "berries", 4);
        Give(p, "iron_plate", 1);
        server.UnlockBlueprint(id, "paint_tool"); // the Blueprints tab: knowledge + materials
        Assert.True(Done(p, "c2.3"));

        Assert.Equal("c2.4", server.ChipStepForTest(p, T));
        server.ReportMinigameResultForTest(id, "blockfall", rating: 1);
        Assert.True(Done(p, "c2.4"));

        // The rune stone only where one waits unread on this world.
        bool monument = server.WorldPredicateForTest(p, StoryWorldPredicates.MonumentUnread);
        Assert.Equal(!monument, Skipped(p, "c2.5"));
        if (monument)
        {
            server.RaiseGameEventForTest(p, GameEventKeys.ScanAny, "monument");
        }

        // No terrain scanner blueprint yet: the read stand-in names it, the course goes on.
        Assert.True(Skipped(p, "c2.6"));
        Assert.Contains("story.tutorial.c2.scanner.start_later", transport.LineKeys(p));
        Assert.Equal("c2.7", server.ChipStepForTest(p, T));
        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiProgressOpenId });
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c2")));
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "c2.6a"))); // the read step closed with the course
    }

    [Fact]
    public void TheResearchCourse_WithTheScannersBlueprint_UsesIt_AndCreativeSkipsTheResearch()
    {
        var (server, _) = World("c2_scanner");
        var p = LocalPastTraining(server, "Prospector");
        string id = p.State.PlayerId;
        p.State.UnlockedBlueprints.Add("terrain_scanner");
        Start(server, p, "c2");
        Walk(server, p, step => step != "c2.6");
        Assert.Equal("c2.6", server.ChipStepForTest(p, T));
        Assert.True(server.HintHeldByTutorialForTest(p, "scanner_idle")); // the step says it — the tip waits

        Give(p, "terrain_scanner", 1);
        p.State.SuitEnergy = 100f;
        server.UseGadgetForTest(id, "terrain_scanner", p.State.Position);
        Assert.True(Done(p, "c2.6"));
        Assert.False(Skipped(p, "c2.6a"));
        Assert.False(Has(p, StoryPackKeys.StepDone(T, "c2.6a"))); // the stand-in never ran

        var (creative, _) = World("c2_creative", c => c.CreativeUnlockAllBlueprints = true);
        var q = LocalPastTraining(creative, "Builder");
        Start(creative, q, "c2");
        Walk(creative, q);
        Assert.True(Has(q, StoryPackKeys.ChapterDone(T, "c2")));
        Assert.True(Skipped(q, "c2.3")); // every blueprint is handed out here: nothing to research
    }

    // ---------------- C3 Pilot ----------------

    [Fact]
    public void ThePilotCourse_WalksThroughTheRealHandlers_ScannerAsteroidInteriorAndSpacewalk()
    {
        var (server, transport) = World("c3_walk", c => c.Rules.AsteroidDestruction = AsteroidDestructionMode.MiningOnly);
        var p = LocalPastTraining(server, "Pilot");
        string id = p.State.PlayerId;
        server.Ship.Modules.Add("asteroid_breaker");
        Start(server, p, "c3");
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "c3.7")) == false && p.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(T, "c3.7"))); // the react step is armed

        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiWaypointSetId }); // a click on the flight chart
        Assert.True(Done(p, "c3.1"));

        server.EnterSpace(id);
        Deliver(server, p, new PlanetScanIntent { BodyId = string.Empty });
        Assert.True(Done(p, "c3.2"));

        Assert.True(server.HintHeldByTutorialForTest(p, "asteroid_near")); // the step teaches it — the tip waits
        for (int i = 0; i < 24 && !Done(p, "c3.3"); i++)
        {
            var rock = server.SpaceEntitiesFor(id).First(e => e.Kind == CombatEntityKind.Asteroid);
            server.FireWeapon(id, "asteroid_breaker", rock.Id);
            server.TickForTest(2.0);
        }

        Assert.True(Done(p, "c3.3"));
        server.EnterShipInterior(id);
        Assert.True(Done(p, "c3.4"));
        server.StartEvaFromShip(id);
        Assert.True(Done(p, "c3.5"));

        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c3")));
        Assert.True(Skipped(p, "c3.6")); // no space combat in this world: no target-lock line
        Assert.DoesNotContain("story.tutorial.c3.target.start", transport.LineKeys(p));
        Assert.True(p.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(T, "c3.7"))); // the zero-g moment stays armed
    }

    [Fact]
    public void ThePilotCourse_SkipsTheAsteroid_WhereAsteroidsCannotBeBroken_AndExplainsTargetLock_WhereEnemiesFly()
    {
        var (server, transport) = World("c3_rules", c =>
        {
            c.Rules.AsteroidDestruction = AsteroidDestructionMode.Off;
            c.Rules.SpaceCombat = SpaceCombatMode.PvE;
            c.Rules.SpaceNpcEnemies = AlienActivity.Normal;
        });
        var p = LocalPastTraining(server, "Pilot");
        Start(server, p, "c3");
        var played = Walk(server, p);

        Assert.DoesNotContain("c3.3", played);
        Assert.True(Skipped(p, "c3.3"));
        Assert.Contains("story.tutorial.c3.target.start", transport.LineKeys(p)); // a read step, spoken before the course closes
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c3")));
    }

    [Fact]
    public void FloatingAboveTheAir_DuringThePilotCourse_ExplainsItOnce_AndVegasHintStaysQuiet()
    {
        var (server, transport) = World("c3_zero_g");
        var p = LocalPastTraining(server, "Climber");
        Start(server, p, "c3");

        var ground = p.State.Position;
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(ground.X, (float)server.AtmosphereHeight + 10f, ground.Z);
        server.Tick(1.0);

        Assert.True(Done(p, "c3.7"));
        Assert.Contains("story.tutorial.c3.zero_g.start", transport.LineKeys(p));
        Assert.DoesNotContain("vega.hint.zero_g", transport.LineKeys(p)); // one teaching voice: the step's line counts as told
        Assert.Equal("c3.1", server.ChipStepForTest(p, T)); // a react step never blocks

        // Without the course the hint teaches it as before.
        var (plain, plainTransport) = World("c3_zero_g_plain");
        var q = LocalPastTraining(plain, "Climber");
        var start = q.State.Position;
        q.State.AboardShip = false;
        q.State.Position = new Vector3f(start.X, (float)plain.AtmosphereHeight + 10f, start.Z);
        plain.Tick(1.0);
        Assert.Contains("vega.hint.zero_g", plainTransport.LineKeys(q));
    }

    [Fact]
    public void WithoutFreeFlight_ThePilotAndNavigatorCourses_AreLocked_AndNeitherSuggestedNorStarted()
    {
        var (server, transport) = World("c3_noflight", c => c.Rules.FreeSpaceFlight = false);
        var p = Join(server, 1, "Walker");
        Graduate(server, p);

        Assert.False(Has(p, StoryPackKeys.ChapterOffered(T, "c3")));
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, "c4"))); // the stations course works at settlements
        Assert.Equal(StoryChapterStates.Locked, ChapterState(server, p, "c3").State);
        Start(server, p, "c3");
        Assert.False(Has(p, StoryPackKeys.ChapterActive(T, "c3")));
        Start(server, p, "c5");
        Assert.False(Has(p, StoryPackKeys.ChapterActive(T, "c5")));
        server.TickStoryPacksForTest(p, 200);
        Assert.DoesNotContain(transport.Offers(p), o => o.LineArg == "Navigator");
    }

    // ---------------- C4 Stations, trade and jobs ----------------

    /// <summary>A board job of <paramref name="ids"/> whose objectives a test can fulfil by hand (items and mined blocks).</summary>
    private static string DoableJob(SvGameServer server, IEnumerable<string> ids)
        => ids.First(m =>
        {
            var objectives = server.ObjectivesForTest(m);
            return objectives.Count > 0 && objectives.All(o => o.Type is MissionObjectiveType.Collect or MissionObjectiveType.Deliver or MissionObjectiveType.Mine);
        });

    private static void DoTheJob(SvGameServer server, PlayerSession p, string missionId)
    {
        foreach (var objective in server.ObjectivesForTest(missionId))
        {
            if (objective.Type == MissionObjectiveType.Mine)
            {
                for (int i = 0; i < objective.Required; i++)
                {
                    server.SimulateMineForTest(p, objective.Target);
                }
            }
            else
            {
                Give(p, objective.Target, objective.Required);
            }
        }
    }

    [Fact]
    public void TheStationsCourse_DocksTradesTakesAJobUndocks_AndHandsItIn_ThroughTheRealHandlers()
    {
        var (server, transport) = World("c4_station", c =>
        {
            c.World.SpaceStations = Frequency.Frequent;
            c.PlaceSettlements = false;
        });
        var p = LocalPastTraining(server, "Trader", cutscenes: true);
        string id = p.State.PlayerId;
        Start(server, p, "c4");
        Assert.Equal("c4.1", server.ChipStepForTest(p, T));
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.course@c4"))); // the course card, behind the training's welcome
        Cooldown(server, p);
        Assert.Contains("tut.course@c4", transport.Plays(p));

        server.EnterSpace(id);
        var station = server.SpaceEntitiesFor(id).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove(id, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(id, station.Id);
        Assert.True(Done(p, "c4.1"));
        Assert.True(Has(p, StoryPackKeys.CutsceneSent(T, "tut.first_dock"))); // committed — it waits behind the course card
        Cooldown(server, p);
        var dock = Assert.Single(transport.To<CutscenePlay>(p), c => c.CutsceneId == "tut.first_dock");
        Assert.Equal(server.Galaxy.FindBody(station.Id)!.Name, dock.Arg); // "Docked" + the station's name

        p.State.Position = server.SpaceStationMarkers.First(m => m.Type == "vendor").Pos;
        Give(p, "iron_ore", 5);
        server.Craft(id, "market_iron_to_titanium");
        Assert.True(Done(p, "c4.2"));

        string job = DoableJob(server, server.StationMissionIds);
        var board = server.SpaceStationMarkers.First(m => m.Type == "mission_board").Pos;
        p.State.Position = board;
        server.AcceptMission(id, job);
        Assert.True(Done(p, "c4.3"));

        server.LeaveStation(id);
        Assert.True(Done(p, "c4.4"));
        Assert.Equal("c4.5", server.ChipStepForTest(p, T));

        // Back to the board where it was taken: the job handed in finishes the course.
        server.ShipMove(id, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(id, station.Id);
        p.State.Position = board;
        DoTheJob(server, p, job);
        server.TurnInMission(id, job);
        Assert.Equal(MissionStatus.TurnedIn, p.State.Missions.Single(m => m.MissionId == job).Status);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c4")));
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "c4.1a")) == false && !Skipped(p, "c4.1a")); // the stand-in never ran
        Cooldown(server, p);
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.first_dock")); // once, the second docking shows nothing
    }

    [Fact]
    public void WithoutStationsOrFlight_TheStationsCourse_RunsAtASettlement_AndSkipsUndocking()
    {
        SvGameServer? server = null;
        RecordingTransport? transport = null;
        for (long seed = 1; seed <= 8 && server is null; seed++)
        {
            var (candidate, t) = World("c4_settlement_" + seed, c =>
            {
                c.Seed = seed;
                c.StartPlanet = "jungle";
                c.PlaceSettlements = true;
                c.World.SpaceStations = Frequency.Off;
                c.Rules.FreeSpaceFlight = false;
            });
            if (candidate.InhabitedSettlementCount > 0 && candidate.SettlementMissionIds.Count > 0)
            {
                (server, transport) = (candidate, t);
            }
        }

        Assert.NotNull(server);
        var p = LocalPastTraining(server!, "Villager");
        string id = p.State.PlayerId;
        Start(server!, p, "c4");

        Assert.True(Skipped(p, "c4.1")); // no station to reach
        Assert.Contains("story.tutorial.c4.dock.start_settlement", transport!.LineKeys(p));
        Assert.Equal("c4.2", server!.ChipStepForTest(p, T));

        var vendor = server.SettlementMarkers.First(m => NpcProfessions.IsTradeMarker(m.Type)).Pos;
        p.State.AboardShip = false;
        p.State.Position = vendor;
        Give(p, "iron_ore", 5);
        server.Craft(id, "market_iron_to_titanium");
        Assert.True(Done(p, "c4.2"));

        string job = DoableJob(server, server.SettlementMissionIds);
        p.State.Position = server.SettlementMarkers.First(m => m.Type == "mission_board").Pos;
        server.AcceptMission(id, job);
        Assert.True(Done(p, "c4.3"));
        Assert.True(Skipped(p, "c4.4")); // not docked: nothing to undock from
        Assert.Equal("c4.5", server.ChipStepForTest(p, T));

        DoTheJob(server, p, job);
        server.TurnInMission(id, job);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c4")));
        Assert.False(Has(p, StoryPackKeys.CutsceneSent(T, "tut.first_dock"))); // no docking, no docking card
    }

    // ---------------- C5 Navigator ----------------

    [Fact]
    public void TheNavigatorCourse_BuildsTheJumpDrive_AndHyperjumps_ThroughTheRealHandlers()
    {
        var (server, transport) = World("c5_walk", c =>
        {
            c.Rules.InstantTravel = true; // the map's one-step variant
            c.World.Wormholes = Frequency.Off;
            c.World.Wrecks = Frequency.Off;
            c.World.DebrisFields = Frequency.Off;
        });
        var p = LocalPastTraining(server, "Navigator", cutscenes: true);
        string id = p.State.PlayerId;
        p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c3")); // the pilot course is through
        Start(server, p, "c5");
        Assert.Equal("c5.1", server.ChipStepForTest(p, T));

        var ground = p.State.Position;
        server.SetMarkerForTest(id, ground.X + 4f, ground.Y, ground.Z, "camp");
        Assert.True(Done(p, "c5.1"));

        Assert.Contains("story.tutorial.c5.travel.start_instant", transport.LineKeys(p)); // instant travel: the variant line
        var home = server.Galaxy.FindBody(p.CurrentLocationId)!;
        var other = server.Galaxy.AllBodies().First(b => b.SystemId == home.SystemId && b.Id != home.Id
                                                          && b.Kind is CelestialKind.Planet or CelestialKind.Moon && !string.IsNullOrEmpty(b.PlanetType)
                                                          && _content.GetPlanet(b.PlanetType!) is not null);
        Assert.True(server.QuickTravelForTest(id, other.Id));
        Assert.True(Done(p, "c5.2"));

        p.State.AboardShip = true; // at the ship's workshop
        Assert.Contains("workshop", server.Ship.Modules);
        Give(p, "stone", 6);
        Give(p, "iron_plate", 4);
        server.Craft(id, "forge");
        Assert.True(Done(p, "c5.3"));

        server.Ship.Modules.Add("refinery"); // aboard, the refinery is the forge
        Give(p, "oil", 1);
        Give(p, "carbon", 1);
        server.Craft(id, "refine_oil_lubricant");
        Assert.True(Done(p, "c5.4"));

        p.State.KnowledgePoints = 100;
        foreach (string blueprint in new[] { "radar_array", "jump_generator" })
        {
            foreach (var cost in _content.Blueprints[blueprint].UnlockCost)
            {
                Give(p, cost.Item, cost.Count);
            }

            server.UnlockBlueprint(id, blueprint);
        }

        Assert.True(Done(p, "c5.5"));
        Assert.True(Done(p, "c5.6"));

        foreach (var cost in _content.GetShipModule("jump_generator")!.BuildCost)
        {
            Give(p, cost.Item, cost.Count);
        }

        Assert.True(server.BuildModuleForTest(id, "jump_generator"));
        Assert.True(Done(p, "c5.7"));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.JumpReady));
        Assert.True(server.HintHeldByTutorialForTest(p, "jump_ready")); // the step says it — the tip waits

        var target = server.Galaxy.Systems.First(s => s.Id != home.SystemId
            && s.Bodies.Any(b => !string.IsNullOrEmpty(b.PlanetType) && _content.GetPlanet(b.PlanetType!) is not null));
        transport.Sent.Clear();
        server.HyperjumpToSystem(id, target.Id);
        Assert.True(Done(p, "c5.8"));

        // The "New system" card: the system's name, sent after the warp-in's flight state (it lands on the warp).
        var sent = transport.Sent.Where(x => x.Conn == p.ConnectionId).Select(x => x.Msg).ToList();
        int warp = sent.FindIndex(m => m is SpaceState { Hyperjump: true });
        int card = sent.FindIndex(m => m is CutscenePlay c && c.CutsceneId == "tut.first_jump");
        Assert.True(warp >= 0 && card > warp, $"warp at {warp}, card at {card}");
        Assert.Equal(target.Name, ((CutscenePlay)sent[card]).Arg);

        // No wormholes, wrecks or debris fields in this world: those two lessons are skipped, the course is through.
        Assert.True(Skipped(p, "c5.9"));
        Assert.True(Skipped(p, "c5.10"));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c5")));
    }

    [Fact]
    public void TheNavigatorCourse_CountsAForgeAndAJumpDrive_TheyAlreadyHave()
    {
        var (server, _) = World("c5_evidence");
        var p = LocalPastTraining(server, "Veteran");
        p.State.Milestones.Add(StoryPackKeys.ChapterDone(T, "c3"));
        p.State.AchievementCounters[AchievementCounters.Craft("forge")] = 1; // crafted a forge long ago
        p.State.UnlockedBlueprints.Add("radar_array");
        p.State.UnlockedBlueprints.Add("jump_generator");
        server.Ship.Modules.Add("jump_generator");
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.ForgeReady));

        Start(server, p, "c5");
        var played = Walk(server, p);
        foreach (string known in new[] { "c5.3", "c5.5", "c5.6", "c5.7" })
        {
            Assert.DoesNotContain(known, played);
            Assert.True(Done(p, known), known); // evidence: done silently
        }

        Assert.Contains("c5.8", played);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c5")));
    }
}
