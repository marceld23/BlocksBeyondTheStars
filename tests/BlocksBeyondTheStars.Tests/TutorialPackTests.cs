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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2469 — step 10 of the story packs (#2459): the basic training as its own tutorial pack (<c>data/stories/tutorial/</c>),
/// replacing the hard-coded nine-stage VEGA onboarding. The pack's shape (six parts, the nine legacy keys once each, the
/// gates, the graduation); a fresh player playing it through to the graduation on every preset and start-world kind (a
/// station in the system or the trader stand-in, an unvisited world or the landing stand-in, no free flight) with no step
/// it cannot do; the gates the advisor systems read; skip, replay and the world's tutorial rule; an older client's legacy
/// mode; the one-time migration of the old onboarding state (mid-chain, a finished chain with the E30 refresher, a save from
/// before the lessons); the "one teaching voice" hold over VEGA's hints; the UI teaching flags (tag 298, through a held
/// world); and that every new teaching verb is raised by its real gameplay handler.
/// </summary>
public sealed class TutorialPackTests : IDisposable
{
    private const string Tutorial = SvGameServer.OfficialTutorialPackId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialPackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tutorial_" + Guid.NewGuid().ToString("N"));
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

        public List<string> LineKeys(PlayerSession who) => To<ShipAiLine>(who).Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();
    }

    private SvGameServer Boot(string world, RecordingTransport transport, Action<ServerConfig>? configure = null, SqliteWorldRepository? repo = null)
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
        repo ??= Repo(world);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>The real join path (prologue, the tutorial's migration and start) — as a current client by default.</summary>
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

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private StoryDefinition Pack => _content.Stories[Tutorial];

    private StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    /// <summary>
    /// Plays the basic training through: whatever step owns the chip, the walker does what that step asks — it raises the
    /// event the step listens to (the one its real gameplay handler raises, see the handler tests below), or reports the UI
    /// flag over the wire — until no step owns the chip any more. Returns the steps it played, in order.
    /// </summary>
    private List<string> WalkThrough(SvGameServer server, PlayerSession p, int maxSteps = 80)
    {
        var played = new List<string>();
        for (int i = 0; i < maxSteps; i++)
        {
            string? id = server.ChipStepForTest(p, Tutorial);
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

    /// <summary>The graduation: the pack done by play, the closing line, the "Graduate" achievement, the gates open.</summary>
    private static void AssertGraduated(SvGameServer server, RecordingTransport transport, PlayerSession p)
    {
        Assert.Null(server.ChipStepForTest(p, Tutorial));
        Assert.True(Has(p, StoryPackKeys.Done(Tutorial)));
        Assert.Contains("story.tutorial.graduated", transport.LineKeys(p));
        Assert.Contains("graduate", p.State.Achievements);
        Assert.Equal(1, p.State.AchievementCounters["tutorial:graduated"]);
        Assert.All(StoryLegacy.StageIds, s => Assert.True(Has(p, StoryLegacy.StagePrefix + s), s)); // older builds: a finished onboarding
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.Banter));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips));
        Assert.False(transport.To<ShipAiLine>(p).Last().TutorialActive);
    }

    // ---------------- The pack ----------------

    [Fact]
    public void ThePack_IsTheOfficialPlayerTutorial_WithSixPartsAndTheNineLegacyKeysOnce()
    {
        var pack = Pack;
        Assert.Equal(StoryRegistry.KindTutorial, pack.Kind);
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(pack));
        Assert.Equal(100, pack.Priority);
        // #2479: the six parts of the basic training, then the deep dives (optional chapters).
        Assert.Equal(new[] { "b1", "b2", "b3", "b4", "b5", "b6" }, pack.Chapters.Where(c => !c.Optional).Select(c => c.Id).ToArray());
        Assert.All(pack.Chapters.Where(c => c.Id.StartsWith("b", StringComparison.Ordinal)), c => Assert.False(c.Optional));
        Assert.All(pack.Chapters.Where(c => c.Id.StartsWith("c", StringComparison.Ordinal)), c => Assert.True(c.Optional));
        Assert.All(pack.Chapters, c => Assert.All(c.Steps, s => Assert.StartsWith(c.Id + ".", s.Id))); // b3.2 sits in b3, c17.1 in c17

        var legacy = pack.Chapters.SelectMany(c => c.Steps).Where(s => s.LegacyKey.Length > 0).Select(s => StoryLegacy.StageIdOf(s.LegacyKey)).ToList();
        Assert.Equal(StoryLegacy.StageIds.OrderBy(x => x), legacy.OrderBy(x => x));

        Assert.Equal(new[] { StoryGateNames.OpportunityTips, StoryGateNames.Banter, StoryGateNames.StationNear }.OrderBy(x => x), pack.Gates.Keys.OrderBy(x => x));
        Assert.Equal(180, pack.Escalate.HintAfter);
        Assert.Equal(360, pack.Escalate.MarkerAfter);
        Assert.Equal(600, pack.Escalate.OfferSkipAfter);
        Assert.Equal("story.tutorial.graduated", pack.Graduation.LineKey);
        Assert.Contains(_content.Achievements, a => a.Key == "graduate" && a.Counter == pack.Graduation.Counter && a.Rewards.Count == 0);
        Assert.Equal(new[] { "c3", "c4", "c1" }, pack.Graduation.Suggest); // #2479: the flight courses; #2480: the workshop course
    }

    [Fact]
    public void EveryStep_ListensToAnEventTheServerRaises_OrAFlagTheClientReports()
    {
        foreach (var step in Pack.Chapters.SelectMany(c => c.Steps).Where(s => s.Kind != StoryStepKinds.Read))
        {
            if (step.Objective.IsMission)
            {
                // #2481: a mission bridge (the crystal workshop course) listens to that mission's turn-in.
                Assert.True(_content.Missions.ContainsKey(step.Objective.Target), $"step {step.Id} bridges onto a missing mission");
                continue;
            }

            string evt = step.Objective.Event;
            bool known = GameEventKeys.IsUiEvent(evt)
                ? GameEventKeys.IsKnownUiEventId(evt.Substring(GameEventKeys.UiPrefix.Length))
                : evt.EndsWith("*", StringComparison.Ordinal)
                    ? GameEventKeys.All.Any(k => GameEventKeys.Matches(evt, k))
                    : GameEventKeys.All.Contains(evt);
            Assert.True(known, $"step {step.Id} listens to '{evt}', which nothing raises");
        }
    }

    [Fact]
    public void ThePremiseFlavour_IsOnlyAVariant_WhileTheVegaStoryRuns()
    {
        // Maintainer decision: VEGA presents the tutorial in neutral words; the amnesia premise only as a whole-line variant.
        var flavour = Pack.Chapters.SelectMany(c => c.Steps).SelectMany(s => s.Variants).Where(v => v.StartKey.EndsWith("_story", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(flavour);
        Assert.All(flavour, v => Assert.Equal("pack:vega_protocol:enabled", v.When));

        var transport = new RecordingTransport();
        var none = Boot("flavour_none", transport, c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(none, 1, "Sandboxer");
        WalkThrough(none, p, maxSteps: 2); // through walking and jumping to the "That's me, VEGA" line
        Assert.Contains("story.tutorial.b1.vega.start", transport.LineKeys(p));
        Assert.DoesNotContain("story.tutorial.b1.vega.start_story", transport.LineKeys(p));
    }

    // ---------------- Walkthroughs ----------------

    public static TheoryData<string> Presets() => new() { "default", "family", "peaceful-creative", "dangerous" };

    [Theory]
    [MemberData(nameof(Presets))]
    public void AFreshPlayer_GraduatesOnEveryPreset_WithNoStepThatCannotBeDone(string preset)
    {
        var transport = new RecordingTransport();
        var server = Boot("walk_" + preset, transport, c =>
        {
            if (ServerPresets.Get(preset) is { } rules)
            {
                c.Rules = rules;
            }

            c.Rules.FreeSpaceFlight = true;
        });
        var p = Join(server, 1, "Rookie");
        Assert.Equal("b1.1", server.ChipStepForTest(p, Tutorial));

        var played = WalkThrough(server, p);
        AssertGraduated(server, transport, p);
        Assert.Contains("b6.1", played); // free flight: the lift-off part is presented

        bool creative = ServerPresets.Get(preset)?.GameMode == GameMode.Creative;
        Assert.Equal(creative, Has(p, StoryPackKeys.StepSkipped(Tutorial, "b5.3"))); // nothing to research where all is handed out
        bool hunger = (ServerPresets.Get(preset) ?? new GameRules()).HungerConsumption != HungerConsumption.Off;
        Assert.Equal(!hunger, Has(p, StoryPackKeys.StepSkipped(Tutorial, "b2.2"))); // no eating lesson without hunger
        if (creative)
        {
            Assert.Contains("story.tutorial.b4.ingot.start_free", transport.LineKeys(p)); // "crafting is free here"
        }
    }

    [Fact]
    public void WithoutAStationInTheSystem_TheTraderStandsInForDocking()
    {
        var transport = new RecordingTransport();
        var server = Boot("walk_nostation", transport, c =>
        {
            c.World.SpaceStations = Frequency.Off;
            c.Rules.FreeSpaceFlight = true;
        });
        var p = Join(server, 1, "Rookie");
        var played = WalkThrough(server, p);

        AssertGraduated(server, transport, p);
        Assert.Contains("b6.4a", played);
        Assert.DoesNotContain("b6.4", played);
        Assert.DoesNotContain("b6.5", played); // the stand-in's trade counts for the trading step
        Assert.True(Has(p, StoryPackKeys.StepSkipped(Tutorial, "b6.4")));
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b6.5")));
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.StationNear)); // the docking step is through (skipped): the tip rests
    }

    [Fact]
    public void WithAStationInTheSystem_DockingIsTaught_AndStationNearIsOpenUntilThen()
    {
        var transport = new RecordingTransport();
        var server = Boot("walk_station", transport, c => c.Rules.FreeSpaceFlight = true);
        var p = Join(server, 1, "Rookie");
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.StationNear));
        var played = WalkThrough(server, p);

        AssertGraduated(server, transport, p);
        Assert.Contains("b6.4", played);
        Assert.DoesNotContain("b6.4a", played);
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b6.4")));
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.StationNear)); // the lesson is learnt: the tip rests
    }

    [Fact]
    public void WithEveryWorldOfTheSystemVisited_ALandingAnywhereStandsInForTheNewWorld()
    {
        var transport = new RecordingTransport();
        var server = Boot("walk_visited", transport, c => c.Rules.FreeSpaceFlight = true);
        var p = Join(server, 1, "Rookie");
        string system = server.Galaxy.FindBody(p.CurrentLocationId)!.SystemId;
        foreach (var body in server.Galaxy.AllBodies().Where(b => b.SystemId == system))
        {
            p.State.LandedBodies.Add(body.Id); // nothing left to discover here
        }

        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.NewWorldInSystem));
        var played = WalkThrough(server, p);

        AssertGraduated(server, transport, p);
        Assert.Contains("b6.7a", played);
        Assert.DoesNotContain("b6.7", played);
        Assert.True(Has(p, StoryLegacy.StagePrefix + "land")); // the skipped landing step still double-writes
    }

    [Fact]
    public void WithFreeFlightOff_TheFlightStepsAreSkipped_AndThePlayerStillGraduates()
    {
        var transport = new RecordingTransport();
        var server = Boot("walk_noflight", transport, c => c.Rules.FreeSpaceFlight = false);
        var p = Join(server, 1, "Rookie");
        var played = WalkThrough(server, p);

        AssertGraduated(server, transport, p);
        foreach (string step in new[] { "b6.1", "b6.2", "b6.3", "b6.4", "b6.7", "b6.7a", "b6.8" })
        {
            Assert.DoesNotContain(step, played);
            Assert.True(Has(p, StoryPackKeys.StepSkipped(Tutorial, step)), step);
        }

        Assert.Contains("b6.4a", played); // trading is possible on foot
    }

    // ---------------- Skip, replay, the world's rule ----------------

    [Fact]
    public void IKnowTheGame_SkipsEveryPart_GrantsNothing_AndGraduatesNobody()
    {
        var transport = new RecordingTransport();
        var server = Boot("skip", transport);
        var p = Join(server, 1, "Pro");
        Deliver(server, p, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipPack });

        Assert.True(Has(p, StoryPackKeys.Done(Tutorial)));
        Assert.All(Pack.Chapters.Where(c => !c.Optional), c => Assert.True(Has(p, StoryPackKeys.ChapterSkipped(Tutorial, c.Id))));
        Assert.DoesNotContain("story.tutorial.graduated", transport.LineKeys(p));
        Assert.DoesNotContain("graduate", p.State.Achievements);
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.Banter));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips));
    }

    [Fact]
    public void ARestartAfterASkip_PlayedThrough_Graduates_AndNeverReplaysThePrologue()
    {
        var transport = new RecordingTransport();
        var server = Boot("restart_walk", transport);
        var p = Join(server, 1, "Returner");
        Assert.Contains("vega.prologue.1", transport.LineKeys(p));
        Deliver(server, p, new SkipOnboardingIntent());

        transport.Sent.Clear();
        Deliver(server, p, new SkipOnboardingIntent { Restart = true });
        Assert.Equal("b1.1", server.ChipStepForTest(p, Tutorial));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.Banter)); // the keys stay: gates hold during a replay

        WalkThrough(server, p);
        Assert.Null(server.ChipStepForTest(p, Tutorial));
        Assert.Contains("story.tutorial.graduated", transport.LineKeys(p)); // the end of a full restart graduates
        Assert.Contains("graduate", p.State.Achievements);
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.Kind == 4); // never the prologue (E17)
        Assert.DoesNotContain(p.State.Milestones, m => m.Contains(":replay", StringComparison.Ordinal)); // every replay ended
    }

    [Fact]
    public void ReplayingOneEarlyPart_RunsItAgain_ButIsNoGraduation()
    {
        var transport = new RecordingTransport();
        var server = Boot("replay_part", transport);
        var p = Join(server, 1, "Student");
        WalkThrough(server, p);
        AssertGraduated(server, transport, p);

        transport.Sent.Clear();
        Deliver(server, p, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.RestartChapter, ChapterId = "b3" });
        Assert.Equal("b3.1", server.ChipStepForTest(p, Tutorial));
        Assert.Contains("story.tutorial.b3.mine.start", transport.LineKeys(p));
        WalkThrough(server, p);
        Assert.False(Has(p, StoryPackKeys.ChapterReplay(Tutorial, "b3")));
        Assert.DoesNotContain("story.tutorial.graduated", transport.LineKeys(p));
    }

    [Fact]
    public void TheWorldsTutorialRule_Off_OptIn_AndAPlayersOptOut()
    {
        // Off: nothing runs, every gate opens, the restart is refused.
        var offTransport = new RecordingTransport();
        var off = Boot("policy_off", offTransport, c => c.Rules.TutorialPolicy = TutorialPolicy.Off);
        var o = Join(off, 1, "Free");
        Assert.DoesNotContain(offTransport.LineKeys(o), k => k.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.Null(off.ChipStepForTest(o, Tutorial));
        Assert.True(off.TutorialGateForTest(o, StoryGateNames.Banter));
        Assert.True(off.TutorialGateForTest(o, StoryGateNames.OpportunityTips));
        Deliver(off, o, new SkipOnboardingIntent { Restart = true });
        Assert.Null(off.ChipStepForTest(o, Tutorial));

        // OptIn: waits for the player's own start.
        var inTransport = new RecordingTransport();
        var optIn = Boot("policy_optin", inTransport, c => c.Rules.TutorialPolicy = TutorialPolicy.OptIn);
        var q = Join(optIn, 1, "Curious");
        Assert.Null(optIn.ChipStepForTest(q, Tutorial));
        Deliver(optIn, q, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.StartPack });
        Assert.Equal("b1.1", optIn.ChipStepForTest(q, Tutorial));
        Assert.Contains("story.tutorial.b1.walk.start", inTransport.LineKeys(q));

        // A player's own "Tutorial: off" on an Auto world: the chip clears and the gates open.
        var autoTransport = new RecordingTransport();
        var auto = Boot("policy_optout", autoTransport);
        var r = Join(auto, 1, "Busy");
        Deliver(auto, r, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.OptOut });
        Assert.Null(auto.ChipStepForTest(r, Tutorial));
        Assert.True(auto.TutorialGateForTest(r, StoryGateNames.Banter));
        Assert.False(autoTransport.To<ShipAiLine>(r).Last().TutorialActive);
    }

    [Fact]
    public void TheGates_OpenWithEatingMiningAndCrafting_AndBanterOnlyAfterTheTraining()
    {
        var transport = new RecordingTransport();
        var server = Boot("gates", transport);
        var p = Join(server, 1, "Kid");
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips));
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.Banter));

        foreach (string step in new[] { "b2.2", "b3.1" })
        {
            p.State.Milestones.Add(StoryPackKeys.StepDone(Tutorial, step));
        }

        Assert.False(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips)); // crafting is still ahead
        p.State.Milestones.Add(StoryPackKeys.StepSkipped(Tutorial, "b4.1"));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips)); // done or skipped — today's moment
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.Banter));
    }

    // ---------------- Older clients ----------------

    [Fact]
    public void AnOlderClient_FollowsTheNineOldLessons_ThroughTheOldKeys()
    {
        var transport = new RecordingTransport();
        var server = Boot("legacy", transport, c => c.Rules.FreeSpaceFlight = true);
        var p = Join(server, 1, "Old", packs: false);

        var keys = transport.LineKeys(p);
        Assert.Contains("vega.intro.1", keys); // it hears the old intro, in its own 14 languages
        Assert.DoesNotContain(keys, k => k.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.Equal("vega.obj.eat", transport.To<ShipAiLine>(p).Last().ObjectiveKey); // b2.2 is the first old lesson in the new order

        var played = WalkThrough(server, p);
        Assert.Equal(StoryLegacy.StageIds.Count, played.Count); // exactly the nine old lessons, nothing it cannot read
        Assert.All(played, id => Assert.False(string.IsNullOrEmpty(StepDef(id).LegacyKey)));
        Assert.All(StoryLegacy.StageIds, s => Assert.True(Has(p, StoryLegacy.StagePrefix + s)));
        Assert.Contains("vega.s.mine.done", transport.LineKeys(p));
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.LineKey.StartsWith("story.tutorial.", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.ObjectiveKey.StartsWith("story.tutorial.", StringComparison.Ordinal)); // no pack chip it could not read
        Assert.False(Has(p, StoryPackKeys.Done(Tutorial))); // the new lessons wait for a current client
    }

    /// <summary>#2487: an older client never stamps a chapter done (it only passes on the old lessons), so the VEGA story —
    /// which waits for the basic training — never started for it, and VEGA never chatted. The nine old lessons are its whole
    /// training now: the story starts ("training through"), the banter gate opens — and no key is written, so the same
    /// player on a current client still gets the new lessons, with the story kept running.</summary>
    [Fact]
    public void AnOlderClient_ThroughItsNineOldLessons_StartsTheStory_AndACurrentClientLaterGetsTheNewLessons()
    {
        const string vega = StoryRegistry.DefaultStoryId;
        var transport = new RecordingTransport();
        var server = Boot("legacy_story", transport, c => c.Rules.FreeSpaceFlight = true);
        var p = Join(server, 1, "Old", packs: false);
        Assert.False(Has(p, StoryPackKeys.Started(vega))); // the story waits for the training
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.Banter));

        // All lessons but the last: still in training.
        var played = WalkThrough(server, p, maxSteps: StoryLegacy.StageIds.Count - 1);
        Assert.Equal(StoryLegacy.StageIds.Count - 1, played.Count);
        Assert.False(Has(p, StoryPackKeys.Started(vega)));
        Assert.Null(server.StoryTrackStartReasonForTest(p));
        Assert.True(transport.To<ShipAiLine>(p).Last().TutorialActive);

        // The ninth: the story track starts, for the training's reason; VEGA may chat.
        WalkThrough(server, p);
        Assert.Null(server.ChipStepForTest(p, Tutorial));
        Assert.True(Has(p, StoryPackKeys.Started(vega)));
        Assert.Equal(SvGameServer.StoryTrackReasons.Training, server.StoryTrackStartReasonForTest(p));
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.Banter));
        Assert.False(transport.To<ShipAiLine>(p).Last().TutorialActive);

        // Nothing written for it: the chapters with new lessons stay open, the pack is not done (b5 and b6 hold only old
        // lessons as MUST steps — they complete by play, as before).
        Assert.False(Has(p, StoryPackKeys.Done(Tutorial)));
        foreach (string open in new[] { "b1", "b2", "b3", "b4" })
        {
            Assert.False(Has(p, StoryPackKeys.ChapterDone(Tutorial, open)), open);
            Assert.False(Has(p, StoryPackKeys.ChapterSkipped(Tutorial, open)), open);
        }

        // The same player on a current client: the new lessons of the basic training are there, the story keeps running.
        server.DisconnectLocalPlayerForTest(p.State.PlayerId);
        var again = Join(server, 2, "Old");
        string? chip = server.ChipStepForTest(again, Tutorial);
        Assert.NotNull(chip);
        Assert.StartsWith("b", chip);
        Assert.True(string.IsNullOrEmpty(StepDef(chip!).LegacyKey)); // a new lesson, not one of the nine
        Assert.True(Has(again, StoryPackKeys.Started(vega)));
        Assert.False(server.TutorialGateForTest(again, StoryGateNames.Banter)); // a current client chats after graduating
    }

    // ---------------- Migration ----------------

    [Fact]
    public void APlayerInTheMiddleOfTheOldChain_KeepsWhatTheyLearnt_AndGoesOn()
    {
        var repo = Repo("mid_chain");
        repo.Initialize();
        var stored = new PlayerState { PlayerId = "Mid", Name = "Mid", KnowledgePoints = 5 };
        stored.Milestones.Add(StoryLegacy.IntroMilestone);
        stored.Milestones.Add(StoryPackKeys.Beat(StoryRegistry.DefaultStoryId, 0)); // an older build's story catch-up (#2470: not a newcomer to VEGA)
        foreach (string stage in new[] { "mine", "craft", "eat" })
        {
            stored.Milestones.Add(StoryLegacy.StagePrefix + stage);
        }

        stored.AchievementCounters[AchievementCounters.Mine("iron_ore")] = 6;
        repo.SavePlayer(stored);

        var transport = new RecordingTransport();
        var server = Boot("mid_chain", transport, repo: repo);
        var p = Join(server, 1, "Mid");

        Assert.True(Has(p, StoryPackKeys.Migrated(Tutorial)));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(Tutorial, "b1"))); // VEGA, menu and Codex were introduced; they walked and mined
        foreach (string step in new[] { "b2.2", "b3.1", "b3.2", "b4.1" })
        {
            Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, step)), step);
        }

        Assert.StartsWith("b2.", server.ChipStepForTest(p, Tutorial)); // the new survival lessons are next
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.tutorial.b1.", StringComparison.Ordinal));
        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.Kind == 4); // not a newcomer: no prologue
        Assert.DoesNotContain("vega.veteran", transport.LineKeys(p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFinishedOldChain_IsAFinishedTraining_AndThinEvidenceGetsOneRefresherOffer(bool richEvidence)
    {
        var repo = Repo("full_chain" + richEvidence);
        repo.Initialize();
        var stored = new PlayerState { PlayerId = "Done", Name = "Done", KnowledgePoints = 9 };
        stored.Milestones.Add(StoryLegacy.IntroMilestone);
        foreach (string stage in StoryLegacy.StageIds)
        {
            stored.Milestones.Add(StoryLegacy.StagePrefix + stage);
        }

        if (richEvidence)
        {
            stored.AchievementCounters[AchievementCounters.MineAny] = 300;
            stored.AchievementCounters[AchievementCounters.CraftAny] = 40;
            stored.AchievementCounters[AchievementCounters.ScanAny] = 25;
        }

        repo.SavePlayer(stored);
        var transport = new RecordingTransport();
        var server = Boot("full_chain" + richEvidence, transport, repo: repo);
        var p = Join(server, 1, "Done");

        Assert.True(Has(p, StoryPackKeys.Done(Tutorial)));
        Assert.All(Pack.Chapters.Where(c => !c.Optional), c => Assert.True(Has(p, StoryPackKeys.ChapterDone(Tutorial, c.Id))));
        Assert.Null(server.ChipStepForTest(p, Tutorial));
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.tutorial.b", StringComparison.Ordinal));
        Assert.DoesNotContain("story.tutorial.graduated", transport.LineKeys(p));
        Assert.DoesNotContain("graduate", p.State.Achievements); // a migration is no graduation
        Assert.Equal(richEvidence ? 0 : 1, transport.LineKeys(p).Count(k => k == SvGameServer.TutorialRefresherLine)); // E30

        // The offer is made once: the migration never runs again.
        server.DisconnectLocalPlayerForTest(p.State.PlayerId);
        transport.Sent.Clear();
        var again = Join(server, 2, "Done");
        Assert.DoesNotContain(SvGameServer.TutorialRefresherLine, transport.LineKeys(again));
    }

    [Fact]
    public void TheMigration_RunsOnce_AndLegacyKeysAreEvidenceOnly()
    {
        var transport = new RecordingTransport();
        var server = Boot("migrate_once", transport);
        var p = Join(server, 1, "Fresh");
        Assert.True(Has(p, StoryPackKeys.Migrated(Tutorial)));
        Assert.False(Has(p, StoryPackKeys.StepDone(Tutorial, "b1.5"))); // the pack's own start wrote vega:intro — no evidence

        p.State.Milestones.Add(StoryLegacy.StagePrefix + "land"); // an older build wrote it later
        server.TickStoryPacksForTest(p);
        Assert.False(Has(p, StoryPackKeys.StepDone(Tutorial, "b6.7"))); // read once, never as live state
    }

    [Fact]
    public void ACreativeWorldsHandOuts_AreNoVeteranEvidence()
    {
        var transport = new RecordingTransport();
        var server = Boot("creative_new", transport, c => c.CreativeUnlockAllBlueprints = true);
        var p = Join(server, 1, "Kid");
        Assert.False(p.Join.Veteran);
        Assert.Equal("b1.1", server.ChipStepForTest(p, Tutorial));
        Assert.DoesNotContain("vega.veteran", transport.LineKeys(p));
    }

    // ---------------- One teaching voice (G12) ----------------

    [Fact]
    public void AStepAhead_HoldsTheHintItTeaches_UntilItsLineIsShown_ThenTheHintCountsAsTold()
    {
        var transport = new RecordingTransport();
        var server = Boot("hold", transport);
        var p = Join(server, 1, "Kid");

        Assert.True(server.HintHeldByTutorialForTest(p, "lamp_off"));  // b2.6 is still ahead
        Assert.True(server.HintHeldByTutorialForTest(p, "night"));
        Assert.False(server.HintHeldByTutorialForTest(p, "invfull"));  // a react step speaks first when it fires, it holds nothing
        Assert.False(server.HintHeldByTutorialForTest(p, "hunger"));   // a safety tip is never held

        WalkThrough(server, p, maxSteps: 1); // walking …
        Deliver(server, p, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipChapter, ChapterId = "b1" });
        while (server.ChipStepForTest(p, Tutorial) is { } id && id != "b2.6")
        {
            WalkThrough(server, p, maxSteps: 1);
        }

        Assert.Contains("story.tutorial.b2.lamp.start", transport.LineKeys(p));
        Assert.True(Has(p, "vega:hint:night"));    // the once-hint's flag is burned by the step's line
        Assert.True(Has(p, "vega:hint:lamp_off")); // the tip's first telling is booked
        WalkThrough(server, p, maxSteps: 1);
        Assert.False(server.HintHeldByTutorialForTest(p, "lamp_off"));
    }

    [Fact]
    public void ASkippedStep_GivesItsHintBack_AndNoHintIsHeldForAnOlderClientOrWithTheTutorialOff()
    {
        var transport = new RecordingTransport();
        var server = Boot("hold_release", transport);
        var p = Join(server, 1, "Kid");
        Deliver(server, p, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipPack });
        Assert.False(server.HintHeldByTutorialForTest(p, "lamp_off"));
        Assert.False(Has(p, "vega:hint:lamp_off")); // given back untold

        var old = Join(server, 2, "Old", packs: false);
        Assert.False(server.HintHeldByTutorialForTest(old, "lamp_off")); // b2.6 never runs for it

        var off = Boot("hold_off", new RecordingTransport(), c => c.Rules.TutorialPolicy = TutorialPolicy.Off);
        var q = Join(off, 1, "Free");
        Assert.False(off.HintHeldByTutorialForTest(q, "lamp_off"));
    }

    // ---------------- The hold list (#2549, gates.holdHints) ----------------

    /// <summary>Plays the training one step at a time (<see cref="WalkThrough"/>) until <paramref name="stop"/> owns the
    /// chip — a step id, or a chapter prefix such as "b3.".</summary>
    private void PlayUntil(SvGameServer server, PlayerSession p, string stop)
    {
        for (int i = 0; i < 80; i++)
        {
            string? id = server.ChipStepForTest(p, Tutorial);
            Assert.NotNull(id);
            if (id == stop || (stop.EndsWith('.') && id!.StartsWith(stop, StringComparison.Ordinal)))
            {
                return;
            }

            WalkThrough(server, p, maxSteps: 1);
        }

        Assert.Fail($"the training never reached {stop}");
    }

    [Fact]
    public void TheHoldList_KeepsAdvancedOnceHintsBack_WithoutSpendingThem_UntilTheTrainingReachesTheirTopic()
    {
        Assert.Equal(new[] { "crystal_mined", "poi", "tier_gate" }, Pack.Gates.HoldHints.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { StoryGateNames.OpportunityTips, StoryGateNames.Banter, StoryGateNames.StationNear }.OrderBy(x => x),
            Pack.Gates.Keys.OrderBy(x => x)); // the hold list is no gate of its own

        var transport = new RecordingTransport();
        var server = Boot("hold_list", transport);
        var p = Join(server, 1, "Kid");
        Assert.True(server.TutorialHoldsHintForTest(p, "crystal_mined"));
        Assert.True(server.TutorialHoldsHintForTest(p, "poi"));
        Assert.True(server.TutorialHoldsHintForTest(p, "tier_gate"));
        Assert.False(server.TutorialHoldsHintForTest(p, "invfull")); // not on the list

        // The first crystal and a ruin in part 1 (the report: "Crystal Net, blueprints …" before she could walk back to the
        // ship): VEGA keeps both for later — the once-flags stay unset.
        server.ShipAiHintOnceForTest(p, "crystal_mined");
        server.ShipAiHintOnceForTest(p, "poi");
        Assert.DoesNotContain("vega.hint.crystal_mined", transport.LineKeys(p));
        Assert.DoesNotContain("vega.hint.poi", transport.LineKeys(p));
        Assert.False(Has(p, "vega:hint:crystal_mined"));
        Assert.False(Has(p, "vega:hint:poi"));

        // Part 2 through: part 3 runs and explains the tool-tier wall itself, so that tip is released; the crystal and the
        // ruins wait for part 5.
        PlayUntil(server, p, "b3.");
        Assert.False(server.TutorialHoldsHintForTest(p, "tier_gate"));
        Assert.True(server.TutorialHoldsHintForTest(p, "crystal_mined"));
        Assert.True(server.TutorialHoldsHintForTest(p, "poi"));

        // Part 5 through: the next crystal and the next ruin get their line after all.
        PlayUntil(server, p, "b6.");
        Assert.False(server.TutorialHoldsHintForTest(p, "crystal_mined"));
        Assert.False(server.TutorialHoldsHintForTest(p, "poi"));
        server.ShipAiHintOnceForTest(p, "crystal_mined");
        server.ShipAiHintOnceForTest(p, "poi");
        Assert.Single(transport.LineKeys(p), k => k == "vega.hint.crystal_mined");
        Assert.Single(transport.LineKeys(p), k => k == "vega.hint.poi");
        Assert.True(Has(p, "vega:hint:crystal_mined"));
        Assert.True(Has(p, "vega:hint:poi"));
    }

    [Fact]
    public void TheHoldList_HoldsNothing_WhereTheTrainingDoesNotRun()
    {
        // A world without the tutorial: today's behaviour — the first crystal gets its line at once.
        var offTransport = new RecordingTransport();
        var off = Boot("hold_list_off", offTransport, c => c.Rules.TutorialPolicy = TutorialPolicy.Off);
        var free = Join(off, 1, "Free");
        Assert.False(off.TutorialHoldsHintForTest(free, "crystal_mined"));
        off.ShipAiHintOnceForTest(free, "crystal_mined");
        Assert.Contains("vega.hint.crystal_mined", offTransport.LineKeys(free));
        Assert.True(Has(free, "vega:hint:crystal_mined"));

        // "Short courses only": the training is skipped silently at the start.
        var courses = Boot("hold_list_courses", new RecordingTransport(), c => c.Rules.TutorialPolicy = TutorialPolicy.Courses);
        var student = Join(courses, 1, "Student");
        Assert.False(courses.TutorialHoldsHintForTest(student, "poi"));

        // A player who skipped the training, one who switched it off, and an older client (its nine legacy lessons never mark
        // the chapters the hold list asks for).
        var transport = new RecordingTransport();
        var server = Boot("hold_list_skip", transport);
        var skipper = Join(server, 1, "Skipper");
        Deliver(server, skipper, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipPack });
        Assert.False(server.TutorialHoldsHintForTest(skipper, "poi"));
        server.ShipAiHintOnceForTest(skipper, "poi");
        Assert.Contains("vega.hint.poi", transport.LineKeys(skipper));

        var optedOut = Join(server, 2, "NoThanks");
        Assert.True(server.TutorialHoldsHintForTest(optedOut, "tier_gate"));
        Deliver(server, optedOut, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.OptOut });
        Assert.False(server.TutorialHoldsHintForTest(optedOut, "tier_gate"));

        var old = Join(server, 3, "Old", packs: false);
        Assert.False(server.TutorialHoldsHintForTest(old, "crystal_mined"));
    }

    [Fact]
    public void TheToolTierTip_WaitsForPart3_WithoutBeingSpent_AndPart3TeachesTheNextWall()
    {
        // The same refused swing in part 1 and on a world without the tutorial.
        (SvGameServer Server, RecordingTransport Transport, PlayerSession Player) HitTheWall(string world, TutorialPolicy policy)
        {
            var transport = new RecordingTransport();
            var server = Boot(world, transport, c => c.Rules.TutorialPolicy = policy);
            server.SetDayFractionForTest(0.5);
            var p = Join(server, 1, "Digger");
            p.State.AboardShip = false;
            foreach (string vital in new[] { "o2", "energy", "hunger", "cold", "heat", "medkit" })
            {
                p.State.Milestones.Add("vega:hint:" + vital + "#done"); // the vitals outrank an equipment tip
            }

            Swing(server, p);
            Assert.Contains("tier_gate", server.VegaTipCandidatesForTest(p.State.PlayerId).Candidates); // armed by the swing
            for (int i = 0; i < 6 && !transport.LineKeys(p).Contains("vega.hint.tier_gate"); i++)
            {
                server.SkipVegaTipCooldownsForTest(p.State.PlayerId);
                server.Tick(1.1);
            }

            return (server, transport, p);
        }

        void Swing(SvGameServer server, PlayerSession p)
        {
            var pos = new Vector3i((int)Math.Floor(p.State.Position.X), (int)Math.Floor(p.State.Position.Y) - 1, (int)Math.Floor(p.State.Position.Z));
            server.World.SetBlock(pos, _content.GetBlock("machine_block")!.NumericId);
            server.MineBlockOnce(p.State.PlayerId, pos.X, pos.Y, pos.Z); // the starter drill cannot open a tier-2 block
        }

        var (_, freeTransport, free) = HitTheWall("tier_hold_off", TutorialPolicy.Off);
        Assert.Contains("vega.hint.tier_gate", freeTransport.LineKeys(free)); // today's behaviour without the training

        var (kidServer, kidTransport, kid) = HitTheWall("tier_hold_kid", TutorialPolicy.Auto);
        Assert.DoesNotContain("vega.hint.tier_gate", kidTransport.LineKeys(kid));
        Assert.False(Has(kid, "vega:hint:tier_gate")); // not spent
        Assert.DoesNotContain("tier_gate", kidServer.VegaTipCandidatesForTest(kid.State.PlayerId).Candidates); // the arming is dropped

        // Part 3 runs: the next wall is the tutorial's own tool-tier moment (b3.3), which tells the tip.
        PlayUntil(kidServer, kid, "b3.");
        kid.State.AboardShip = false;
        Swing(kidServer, kid);
        Assert.Contains("story.tutorial.b3.tier.start", kidTransport.LineKeys(kid));
        Assert.True(Has(kid, StoryPackKeys.StepDone(Tutorial, "b3.3")));
    }

    // ---------------- UI teaching flags (tag 298) ----------------

    [Fact]
    public void AUiFlag_CompletesTheStepThatWaitsForIt_EvenWhileTheWorldIsHeld()
    {
        var transport = new RecordingTransport();
        var server = Boot("ui_held", transport);
        var p = Join(server, 1, "Solo");
        foreach (string step in new[] { "b1.1", "b1.2", "b1.4" })
        {
            Deliver(server, p, new StoryPackIntent { PackId = Tutorial, Action = StoryPackActions.SkipStep, StepId = step });
        }

        Assert.Equal("b1.5", server.ChipStepForTest(p, Tutorial));
        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);

        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiMenuOpenId }); // the pause menu holds the world (#2462 B12)
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b1.5")));
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b1.3"))); // the read step closed with it
        Assert.Equal("b1.6", server.ChipStepForTest(p, Tutorial));
        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiCodexOpenId });
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b1.6")));
        Assert.True(server.IsPaused);
    }

    [Fact]
    public void AnUnknownUiFlag_IsIgnored_TheSameFlagIsRateLimited_AndAFlagNobodyWaitsForDoesNothing()
    {
        var transport = new RecordingTransport();
        var server = Boot("ui_limits", transport);
        var p = Join(server, 1, "Kid");
        var seen = new RecordingGameEventListener();
        server.AddGameEventListener("ui:*", seen);

        Deliver(server, p, new UiTutorialEvent { Id = "self_destruct" });
        Assert.Empty(seen.Events);

        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiMapOpenId });
        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiMapOpenId }); // the same instant: one counts
        Assert.Single(seen.Of(GameEventKeys.UiMapOpen));

        Deliver(server, p, new UiTutorialEvent { Id = GameEventKeys.UiWaypointSetId });
        Assert.Single(seen.Of(GameEventKeys.UiWaypointSet));
        Assert.Equal("b1.1", server.ChipStepForTest(p, Tutorial)); // nothing waited for those: no step moved
        Assert.DoesNotContain(p.State.Milestones, m => m.StartsWith("ui:", StringComparison.Ordinal)); // never stored
    }

    [Fact]
    public void TheUiFlag_RoundTrips_UnderTag298()
    {
        var decoded = Assert.IsType<UiTutorialEvent>(NetCodec.Decode(NetCodec.Encode(new UiTutorialEvent { Id = "menu_open" })));
        Assert.Equal("menu_open", decoded.Id);
        Assert.Equal(string.Empty, new UiTutorialEvent().Id);
    }

    // ---------------- The new teaching verbs, raised by their real handlers ----------------

    /// <summary>A server with a wildcard listener (so the "only while a step listens" measuring runs) and one local player.</summary>
    private (SvGameServer Server, RecordingGameEventListener Events, PlayerSession Player) Verbs(string world, Action<ServerConfig>? configure = null)
    {
        var server = Boot(world, new RecordingTransport(), configure);
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = Join(server, 1, "Doer");
        return (server, events, p);
    }

    [Fact]
    public void Walking_Jumping_AndCrouching_AreReportedFromTheMoveHandler()
    {
        var (server, events, p) = Verbs("verb_walk");
        var start = p.State.Position;
        for (int i = 1; i <= 10; i++)
        {
            Deliver(server, p, new MoveIntent { X = start.X + i, Y = start.Y, Z = start.Z });
        }

        Assert.True(events.Of(GameEventKeys.MoveWalked).Sum(e => e.Amount) >= 8, "ten one-metre steps are at least eight metres");

        Deliver(server, p, new MoveIntent { X = start.X + 10, Y = start.Y, Z = start.Z + 500 }); // a teleport-sized jump never counts
        int before = events.Of(GameEventKeys.MoveWalked).Sum(e => e.Amount);
        Assert.True(before <= 10);

        Deliver(server, p, new MoveIntent { X = start.X + 10, Y = start.Y, Z = start.Z + 500, Crouching = true });
        Assert.Contains(events.Of(GameEventKeys.MoveJump), e => e.Arg == GameEventKeys.MoveCrouchArg);
    }

    [Fact]
    public void TheQuickBar_TheBackpack_TheLampAndGear_AreReportedFromTheirHandlers()
    {
        var (server, events, p) = Verbs("verb_items");
        string id = p.State.PlayerId;

        Deliver(server, p, new SelectHotbarIntent { Slot = 1 });
        Assert.Contains(events.Of(GameEventKeys.HotbarSelect), e => e.Arg == "2");
        Deliver(server, p, new SelectHotbarIntent { Slot = 1 }); // no change, no event
        Assert.Single(events.Of(GameEventKeys.HotbarSelect));

        int filled = Enumerable.Range(0, p.State.Inventory.SlotCount).First(i => p.State.Inventory.Slots[i] is { IsEmpty: false });
        server.MoveItemForTest(id, filled, 30);
        Assert.Single(events.Of(GameEventKeys.ItemMoved));

        Deliver(server, p, new SetLampIntent { On = true });
        Assert.Contains(events.Of(GameEventKeys.LampToggled), e => e.Arg == "on");

        int lampSlot = Enumerable.Range(0, p.State.Equipment.SlotCount).First(i => p.State.Equipment.Slots[i] is { IsEmpty: false } s && s.Item == "suit_lamp");
        server.UnequipItemForTest(id, lampSlot);
        int lampInPack = Enumerable.Range(0, p.State.Inventory.SlotCount).First(i => p.State.Inventory.Slots[i] is { IsEmpty: false } s && s.Item == "suit_lamp");
        server.EquipItemForTest(id, lampInPack);
        Assert.Contains(events.Of(GameEventKeys.GearEquipped), e => e.Arg == "suit_lamp");
    }

    [Fact]
    public void AQuickBarResync_MovesTheServersSlot_ButIsNoPick()
    {
        // #2525: the client re-sends the slot it shows after every join and world change. The server mines with its own
        // copy, so it adopts the slot — but a rejoin is no pick, and must not tick off the tutorial's quick-bar step.
        var (server, events, p) = Verbs("verb_resync");
        p.State.SelectedHotbarSlot = 4; // the slot saved at the quit

        Deliver(server, p, new SelectHotbarIntent { Slot = 0, Resync = true });

        Assert.Equal(0, p.State.SelectedHotbarSlot);
        Assert.Empty(events.Of(GameEventKeys.HotbarSelect));

        Deliver(server, p, new SelectHotbarIntent { Slot = 2 }); // the player's own pick still counts
        Assert.Contains(events.Of(GameEventKeys.HotbarSelect), e => e.Arg == "3");
    }

    [Fact]
    public void AFullBackpack_TheHold_AndARespawn_AreReportedFromTheirHandlers()
    {
        var (server, events, p) = Verbs("verb_survival", c => c.PlaceStarterShip = true);
        string id = p.State.PlayerId;

        p.State.AboardShip = true;
        p.State.Inventory.SetSlot(20, new ItemStack("stone", 5));
        Assert.True(server.MoveCargoForTest(id, toCargo: true, item: "stone"));
        Assert.Contains(events.Of(GameEventKeys.CargoStowed), e => e.Arg == "stone");

        int max = _content.MaxStackOf("stone");
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            if (p.State.Inventory.Slots[i] is null or { IsEmpty: true })
            {
                p.State.Inventory.SetSlot(i, new ItemStack("stone", max));
            }
        }

        server.Tick(1.1);
        Assert.NotEmpty(events.Of(GameEventKeys.InventoryFull));

        server.KillPlayerForTest(p, "test");
        Assert.NotEmpty(events.Of(GameEventKeys.PlayerRespawned));
    }

    [Fact]
    public void TheSuitsRation_IsReportedWhenItFeedsAStarvingPlayer()
    {
        var (server, events, p) = Verbs("verb_ration"); // no starter ship: on foot, no life support
        p.State.AboardShip = false;
        p.State.Hunger = 1f;
        server.Tick(1.0);
        Assert.Contains(events.Of(GameEventKeys.SuitAutoFeed), e => e.Arg == "emergency_ration");
    }

    [Fact]
    public void WalkingBackToTheShip_AndCarryingADataFragment_AreNoticedOnlyWhileAStepWaits()
    {
        var (server, events, p) = Verbs("verb_state", c => c.PlaceStarterShip = true);
        var tank = p.State.RespawnPoint;

        // #2550: a listener on the bus is not enough — only the player's own waiting step lets the walk count.
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(tank.X + 60f, tank.Y, tank.Z);
        server.TutorialStateEventsForTest(p);
        p.State.AboardShip = true;
        server.TutorialStateEventsForTest(p);
        Assert.Empty(events.Of(GameEventKeys.ShipReturned));

        PlayUntil(server, p, "b2.5");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(tank.X + 60f, tank.Y, tank.Z);
        server.TutorialStateEventsForTest(p); // far …
        Assert.Empty(events.Of(GameEventKeys.ShipReturned));
        p.State.AboardShip = true;
        server.TutorialStateEventsForTest(p); // … and back aboard
        Assert.Contains(events.Of(GameEventKeys.ShipReturned), e => e.Arg == "walk");

        p.State.Inventory.SetSlot(25, new ItemStack("data_fragment", 1));
        server.TutorialStateEventsForTest(p);
        Assert.Contains(events.Of(GameEventKeys.DataFound), e => e.Arg == "fragment");
    }

    [Fact]
    public void TheWalkBackStep_MeasuresLikeTheCompass_SaysFarEnoughOnce_AndCompletesBackAtTheShip()
    {
        // #2550: a player stood 30 m out for 18 minutes — the 40 was hidden, the server measured the heal tank in 3-D while
        // the compass showed the flat distance to the ship, and nothing said "far enough".
        var transport = new RecordingTransport();
        var server = Boot("walk_back", transport, c => c.PlaceStarterShip = true);
        var p = Join(server, 1, "Kid");
        PlayUntil(server, p, "b2.5");
        Assert.Contains("story.tutorial.b2.home.start", transport.LineKeys(p));

        var anchor = server.ShipAnchorOf(p.State.PlayerId);
        float ax = anchor.X + 0.5f, az = anchor.Z + 0.5f; // the point the compass's ship blip sits on (ShipPlacement)
        p.State.AboardShip = false;

        // "Ship N m": flat — a hill does not count — and the short way round the world's seam.
        p.State.Position = new Vector3f(ax + 30f, anchor.Y + 40f, az);
        Assert.Equal(30.0, server.ShipCompassDistanceForTest(p)!.Value, 3);
        p.State.Position = new Vector3f(ax + server.World.Circumference - 25f, anchor.Y, az);
        Assert.Equal(25.0, server.ShipCompassDistanceForTest(p)!.Value, 3);

        // 39.4 m — the compass (Mathf.RoundToInt) reads "Ship 39 m" — 30 m up a hill (about 49 m in a straight line): not far
        // enough yet.
        p.State.Position = new Vector3f(ax + 39.4f, anchor.Y + 30f, az);
        server.TutorialStateEventsForTest(p);
        Assert.DoesNotContain("story.tutorial.b2.home.far", transport.LineKeys(p));

        // 39.6 m: the compass reads "Ship 40 m", so it counts — VEGA says so once, however long the player stays out there.
        p.State.Position = new Vector3f(ax, anchor.Y, az + 39.6f);
        server.TutorialStateEventsForTest(p);
        server.TutorialStateEventsForTest(p);
        Assert.Single(transport.LineKeys(p), k => k == "story.tutorial.b2.home.far");
        Assert.Equal("b2.5", server.ChipStepForTest(p, Tutorial));

        // Halfway back is not back, nor is 10.6 m ("Ship 11 m"); 10.4 m ("Ship 10 m") is.
        p.State.Position = new Vector3f(ax + 20f, anchor.Y, az);
        server.TutorialStateEventsForTest(p);
        p.State.Position = new Vector3f(ax + 10.6f, anchor.Y, az);
        server.TutorialStateEventsForTest(p);
        Assert.False(Has(p, StoryPackKeys.StepDone(Tutorial, "b2.5")));
        p.State.Position = new Vector3f(ax + 10.4f, anchor.Y, az);
        server.TutorialStateEventsForTest(p);
        Assert.True(Has(p, StoryPackKeys.StepDone(Tutorial, "b2.5")));
        Assert.Single(transport.LineKeys(p), k => k == "story.tutorial.b2.home.far");
    }

    [Fact]
    public void AWalkBeforeTheWalkBackStepActivates_NeverCounts_NotUnderAnotherPlayersStepNorBeforeASkip()
    {
        // #2550: the event bus is shared — the walk-back check ran for everyone while ANY player's step waited, and a "far"
        // noted then, or before a skip, later completed the step at the ship without a walk and without the line.
        var transport = new RecordingTransport();
        var server = Boot("walk_back_mp", transport, c => c.PlaceStarterShip = true);
        var host = Join(server, 1, "Host");
        PlayUntil(server, host, "b2.5"); // the host's step listens for ship:returned …
        var kid = Join(server, 2, "Kid");
        Assert.NotEqual("b2.5", server.ChipStepForTest(kid, Tutorial));

        // … while the kid, still in part 1, walks 60 m out.
        var anchor = server.ShipAnchorOf(kid.State.PlayerId);
        float ax = anchor.X + 0.5f, az = anchor.Z + 0.5f;
        kid.State.AboardShip = false;
        kid.State.Position = new Vector3f(ax + 60f, anchor.Y, az);
        server.TutorialStateEventsForTest(kid);
        Assert.False(kid.TutorialFarFromShip);

        // A flag left over from before (another step, a skip) is reset when the step activates.
        kid.TutorialFarFromShip = true;
        PlayUntil(server, kid, "b2.5");
        Assert.False(kid.TutorialFarFromShip);
        kid.State.AboardShip = false;
        kid.State.Position = new Vector3f(ax + 5f, anchor.Y, az);
        server.TutorialStateEventsForTest(kid);
        Assert.False(Has(kid, StoryPackKeys.StepDone(Tutorial, "b2.5"))); // standing at the ship is no walk back
        Assert.DoesNotContain("story.tutorial.b2.home.far", transport.LineKeys(kid));

        // The real walk still counts.
        kid.State.Position = new Vector3f(ax + 45f, anchor.Y, az);
        server.TutorialStateEventsForTest(kid);
        kid.State.Position = new Vector3f(ax + 5f, anchor.Y, az);
        server.TutorialStateEventsForTest(kid);
        Assert.True(Has(kid, StoryPackKeys.StepDone(Tutorial, "b2.5")));
        Assert.Single(transport.LineKeys(kid), k => k == "story.tutorial.b2.home.far");
    }

    [Fact]
    public void ATierRefusal_AFlight_AndANewWorld_AreReportedFromTheirHandlers()
    {
        var server = Boot("verb_world", new RecordingTransport(), c => c.Rules.FreeSpaceFlight = true);
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = server.AddLocalPlayer("Flyer"); // the local player the space-flight seams drive
        string id = p.State.PlayerId;
        p.State.AboardShip = false;

        var pos = new Vector3i((int)Math.Floor(p.State.Position.X), (int)Math.Floor(p.State.Position.Y) - 1, (int)Math.Floor(p.State.Position.Z));
        server.World.SetBlock(pos, _content.GetBlock("machine_block")!.NumericId);
        server.MineBlockOnce(id, pos.X, pos.Y, pos.Z); // the starter drill cannot open a tier-2 block
        Assert.Contains(events.Of(GameEventKeys.MineRefused), e => e.Arg == "machine_block");

        var home = server.Galaxy.FindBody(p.CurrentLocationId)!;
        var other = server.Galaxy.AllBodies().First(b => b.SystemId == home.SystemId && b.Id != home.Id
                                                          && b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField);
        server.MarkArrivedOnBodyForTest(p, other.Id);
        Assert.Contains(events.Of(GameEventKeys.VisitWorld), e => e.Arg == other.Id);

        p.State.AboardShip = true; // a launch starts aboard
        server.EnterSpace(id);
        Assert.NotEmpty(events.Of(GameEventKeys.SpaceEnter));
        server.ShipMove(id, 0f, 0f, 0f);
        server.ShipMove(id, 30f, 0f, 0f);
        server.ShipMove(id, 30f, 0f, 400f); // a warp-sized jump never counts
        Assert.Equal(30, events.Of(GameEventKeys.FlightFlown).Sum(e => e.Amount));
    }
}
