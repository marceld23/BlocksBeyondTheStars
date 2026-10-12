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
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2481 — step 20 of the story packs (#2459): the tutorial's deep dives of the part "Crystal net" — C8 Crystal workshop, a
/// bridge onto VEGA's six radio jobs <c>crystal_workshop_1..6</c> (not moved, not copied, never offered twice, their rewards
/// kept), and C14 Factory and automation (synthesis §3.2/§3.4/§3.7) — plus B55: the follow-up after a job of that chain is
/// VEGA's own line, not a coined quartermaster on the radio. Their shape; the offers (the comm radio, a factory nearby, the
/// fabricator blueprint, never to a player who did every job); the real chain walked through the course (a real circuit for
/// the first job, the jobs' rewards, the first-circuit banner once); a running chain adopted; a declined course leaving the
/// chain as it was with VEGA's follow-up (also composed for an older client); the factory course through the real handlers
/// (the terminal's roster, a claim, a fabricator) with its prerequisites and world-rule variants.
/// </summary>
public sealed class TutorialCrystalCoursesTests : IDisposable
{
    private const string T = SvGameServer.OfficialTutorialPackId;

    private static readonly string[] Jobs =
    {
        "crystal_workshop_1", "crystal_workshop_2", "crystal_workshop_3", "crystal_workshop_4", "crystal_workshop_5", "crystal_workshop_6",
    };

    private static readonly string[] JobBlueprints = { "crystal_conduit", "crystal_sound", "crystal_sensors", "crystal_logic", "fabricator", "crystal_mechanics" };

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public TutorialCrystalCoursesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_crystal_courses_" + Guid.NewGuid().ToString("N"));
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

        public List<ShipAiLine> FollowUps(PlayerSession who)
            => To<ShipAiLine>(who).Where(l => l.LineKey is SvGameServer.VegaChainNextLine or SvGameServer.VegaChainNextResearchLine).ToList();
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>A world without a world story (the courses are the tutorial's) and without a starter ship (research is open
    /// everywhere, blocks go into the world).</summary>
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
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceFactories = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path; <paramref name="current"/> = a client with packs and cutscenes, else an older build.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool current = true)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = current ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : Array.Empty<string>(),
        }));
        return server.Sessions[conn];
    }

    /// <summary>A local player past the basic training ("I know the game"); <paramref name="cutscenes"/> makes it a client
    /// that plays cutscenes.</summary>
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
        p.StoryPackPlaySeconds += 90; // #2498: the skip suggests the flight courses silently — an offer; the next waits the gap
        p.State.Milestones.Add(StoryPackKeys.ChapterOffered(T, "c15")); // #2482: these players stand together — "Together" has its own tests
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

    private static void Unlock(PlayerSession p, params string[] blueprints)
    {
        foreach (string bp in blueprints)
        {
            p.State.UnlockedBlueprints.Add(bp);
        }
    }

    private StoryDefinition Pack => _content.Stories[T];

    private StoryChapter Chapter(string id) => Pack.Chapters.Single(c => c.Id == id);

    private StoryStep StepDef(string id) => Pack.Chapters.SelectMany(c => c.Steps).Single(s => s.Id == id);

    private CutsceneDefinition Card(string id) => Pack.FindCutscene(id)!;

    private static NetStoryChapter ChapterState(SvGameServer server, PlayerSession p, string chapter)
        => server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == T).Chapters.Single(c => c.Id == chapter);

    /// <summary>A workshop job the way a player does it — taken in the Missions tab (the real chain rule), its circuit worked
    /// (CrystalNet2Tests builds those; here the job's objective is marked met), handed in through the real turn-in.</summary>
    private static void HandIn(SvGameServer server, PlayerSession p, string job)
    {
        string id = p.State.PlayerId;
        server.AcceptMission(id, job);
        var row = p.State.Missions.Single(m => m.MissionId == job);
        Assert.Equal(MissionStatus.Active, row.Status);
        for (int i = 0; i < row.ObjectiveProgress.Count; i++)
        {
            row.ObjectiveProgress[i] = 1;
        }

        server.TurnInMission(id, job);
        Assert.Equal(MissionStatus.TurnedIn, row.Status);
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>A row of cells well above the ground around x 0..4, z 0..2; the player hovers above it (the Crystal Net tests'
    /// set-up: the cells are empty and in reach).</summary>
    private static int AirRow(SvGameServer server, PlayerSession p)
    {
        int top = int.MinValue;
        for (int x = -1; x <= 5; x++)
            for (int z = -1; z <= 3; z++)
            {
                top = Math.Max(top, SurfaceTopY(server, x, z));
            }

        int y = top + 12;
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, y + 3, 0.5f);
        return y;
    }

    private static void Ticks(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    // ---------------- The courses ----------------

    [Fact]
    public void TheCrystalCourses_BridgeOntoTheWorkshopJobs_WithTheirPrerequisitesMomentsBannerAndAbsorbedHints()
    {
        var c8 = Chapter("c8");
        var c14 = Chapter("c14");
        Assert.True(c8.Optional && c14.Optional);
        Assert.Equal(new[] { "chapter:tutorial:b5", "blueprint:comm_radio" }, c8.Requires);
        Assert.Equal(new[] { "blueprint:comm_radio" }, c8.OfferWhen); // synthesis §3.2: "comm radio researched"
        Assert.Equal(new[] { "chapter:tutorial:b4" }, c14.Requires);
        Assert.Equal(new[] { "any:[world:factory_near,blueprint:fabricator]" }, c14.OfferWhen);
        foreach (var c in new[] { c8, c14 })
        {
            Assert.Equal(("tut.course", "tut.done"), (c.OpenCutscene, c.CloseCutscene));
            Assert.All(c.Steps, s => Assert.StartsWith(c.Id + ".", s.Id));
        }

        Assert.Equal(new[] { "c3", "c4", "c1" }, Pack.Graduation.Suggest); // unchanged: the crystal net needs the comm radio first

        // C8.1–C8.6 are the six jobs, in order, as mission bridges — the chain itself stays in data/missions.json.
        for (int i = 0; i < Jobs.Length; i++)
        {
            var step = StepDef("c8." + (i + 1));
            Assert.True(step.Objective.IsMission, step.Id);
            Assert.Equal(Jobs[i], step.Objective.Target);
            var job = _content.Missions[Jobs[i]];
            Assert.Equal(("crystal_workshop", i + 1), (job.ChainId, job.Step));
            Assert.Equal((MissionChains.GiverVega, MissionChains.SurfaceRadio), (job.GiverRole, job.Surface)); // B55: VEGA's own jobs
            Assert.Equal(JobBlueprints[i], job.RequiresBlueprint);
            Assert.True(job.Rewards.Count > 0 && job.KnowledgeReward > 0, job.Id); // the jobs keep paying
            Assert.False(job.CountsForStory);
            if (i > 0)
            {
                // A job whose blueprint is still missing says so — the start line and the chip.
                var research = Assert.Single(step.Variants);
                Assert.Equal("not:blueprint:" + JobBlueprints[i], research.When);
                Assert.True(research.StartKey.Length > 0 && research.ChipKey.Length > 0, step.Id);
            }
        }

        Assert.Equal(new[] { "blueprint:crystal_conduit" }, StepDef("c8.0").AlreadyDoneIf);
        Assert.Contains(Pack.Chapters.SelectMany(c => c.Steps), s => s.Objective.IsMission); // only C8 bridges
        Assert.All(Pack.Chapters.Where(c => c.Id != "c8").SelectMany(c => c.Steps), s => Assert.False(s.Objective.IsMission, s.Id));

        // The first working circuit: a banner on the first job's hand-in (synthesis §6.4: T1, the camera untouched).
        Assert.Equal("tut.first_circuit", StepDef("c8.1").OnComplete.Cutscene);
        var banner = Card("tut.first_circuit");
        Assert.Equal(CutsceneTiers.Banner, banner.Tier);
        Assert.False(banner.TakesCamera);
        Assert.False(banner.EffectiveHoldWorld);
        Assert.False(banner.EffectiveLockInput);
        Assert.Contains("tut.first_circuit", StoryCutsceneIds.Tutorial);

        // One teaching voice: the steps whose lines say what a hint would.
        Assert.Equal(new[] { "crystal_researched", "crystal_researchable" }, StepDef("c8.0").HintRefs); // #2459: the radio line too
        Assert.Equal(new[] { "crystal_first" }, StepDef("c8.1").HintRefs);
        Assert.Equal(new[] { "crystal_door" }, StepDef("c8.4").HintRefs);
        Assert.Equal(new[] { "crystal_mechanics", "crystal_phase" }, StepDef("c8.6").HintRefs);
        Assert.Equal(new[] { "factory_near" }, StepDef("c14.1").HintRefs);

        // C14's hard prerequisites (synthesis §3.2): the machine lesson waits for a machine circuit (C8.5) and a forge's goods
        // (C1) and says why; claiming spends a rare code, so it is explained and only reacted to.
        Assert.Equal(new[] { "any:[step:tutorial:c8.5,world:machine_circuit]", "any:[chapter:tutorial:c1,world:forge_ready]" }, StepDef("c14.5").Requires);
        Assert.Equal((StoryStepKinds.Read, StoryStepKinds.React, StoryStepKinds.Read), (StepDef("c14.2").Kind, StepDef("c14.3").Kind, StepDef("c14.4").Kind));
        Assert.Equal(("craft:station", "factory"), (StepDef("c14.1").Objective.Event, StepDef("c14.1").Objective.Arg));
        Assert.Equal("c14.1a", StepDef("c14.1").Alternative);
        Assert.Empty(StoryPackValidator.Validate(Pack, StoryPackValidationTests.Context()).Where(i => i.Severity == StoryPackIssueSeverity.Error));
    }

    // ---------------- Offers ----------------

    [Fact]
    public void TheWorkshopCourse_IsOfferedWithTheCommRadio_OnceAndNeverToAPlayerWhoDidEveryJob()
    {
        var (server, transport) = World("offer_c8");

        // During the training the comm radio is no moment.
        var rookie = Join(server, 1, "Rookie");
        Unlock(rookie, "comm_radio");
        server.TickStoryPacksForTest(rookie, 3);
        Assert.Empty(transport.Offers(rookie));

        // Past the training: locked before the comm radio ("needs: Comm Radio"), offered with it — once.
        var tinker = LocalPastTraining(server, "Tinker");
        server.TickStoryPacksForTest(tinker);
        Assert.Empty(transport.Offers(tinker));
        var locked = ChapterState(server, tinker, "c8");
        Assert.Equal(StoryChapterStates.Locked, locked.State);
        Assert.Contains("blueprint.comm_radio.name", locked.RequiresKeys);
        Unlock(tinker, "comm_radio");
        server.TickStoryPacksForTest(tinker);
        Assert.Equal("Crystal workshop", Assert.Single(transport.Offers(tinker)).LineArg);
        server.TickStoryPacksForTest(tinker, 200);
        Assert.Single(transport.Offers(tinker));

        // A player who did all six jobs on the radio before: nothing left to teach — the course is never offered.
        var veteran = LocalPastTraining(server, "Veteran");
        Unlock(veteran, "comm_radio", "crystal_conduit");
        foreach (string job in Jobs)
        {
            veteran.State.Missions.Add(new MissionProgress { MissionId = job, ChainId = "crystal_workshop", Status = MissionStatus.TurnedIn, ObjectiveProgress = { 1 } });
        }

        server.TickStoryPacksForTest(veteran, 5);
        Assert.Empty(transport.Offers(veteran));
        Assert.Equal(StoryChapterStates.Available, ChapterState(server, veteran, "c8").State); // still startable from the Journal

        // One job left: the course is offered — it adopts the rest.
        veteran.State.Missions.RemoveAll(m => m.MissionId == Jobs[5]);
        server.TickStoryPacksForTest(veteran);
        Assert.Equal("Crystal workshop", Assert.Single(transport.Offers(veteran)).LineArg);
    }

    [Fact]
    public void TheFactoryCourse_IsOfferedNearAFactory_OrWithTheFabricatorBlueprint()
    {
        var (server, transport) = World("offer_c14");

        // VEGA's tip pass saw a factory's terminal close by: the course's moment.
        var visitor = LocalPastTraining(server, "Visitor");
        server.TickStoryPacksForTest(visitor);
        Assert.Empty(transport.Offers(visitor));
        visitor.VegaTipLive = new[] { "factory_near" };
        Assert.True(server.WorldPredicateForTest(visitor, StoryWorldPredicates.FactoryNear));
        server.TickStoryPacksForTest(visitor);
        Assert.Equal("Factory and automation", Assert.Single(transport.Offers(visitor)).LineArg);

        // The fabricator blueprint researched.
        var maker = LocalPastTraining(server, "Maker");
        Unlock(maker, "fabricator");
        server.TickStoryPacksForTest(maker);
        Assert.Equal("Factory and automation", Assert.Single(transport.Offers(maker)).LineArg);
    }

    // ---------------- C8 Crystal workshop ----------------

    [Fact]
    public void TheWorkshopCourse_WalksTheRealChain_KeepsTheJobsRewards_AndCelebratesTheFirstCircuitOnce()
    {
        var (server, transport) = World("c8_walk");
        var p = LocalPastTraining(server, "Tinker", cutscenes: true);
        string id = p.State.PlayerId;
        Unlock(p, "comm_radio");
        Start(server, p, "c8");
        Assert.Contains("story.tutorial.c8.net.start", transport.LineKeys(p));
        Assert.Equal("c8.0", server.ChipStepForTest(p, T));
        Assert.Contains("vega:hint:crystal_researched", p.State.Milestones); // the step's line says what the hint would

        // The Crystal Net researched in the Blueprints tab (the real research): VEGA's own hint about it stays quiet.
        p.State.KnowledgePoints = 100;
        foreach (var cost in _content.Blueprints["crystal_conduit"].UnlockCost)
        {
            Give(p, cost.Item, cost.Count);
        }

        server.UnlockBlueprint(id, "crystal_conduit");
        Assert.True(Done(p, "c8.0"));
        Assert.DoesNotContain("vega.hint.crystal_researched", transport.LineKeys(p));
        Assert.Equal("c8.1", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c8.light.start", transport.LineKeys(p));
        Assert.Equal("story.tutorial.c8.light.chip", server.ObjectiveKeyForTest(id));

        // Job 1 the way any player does it: taken in the Missions tab, the circuit really built and switched on, handed in.
        Assert.Contains(Jobs[0], server.VisibleMissionIdsForTest(id));
        server.AcceptMission(id, Jobs[0]);
        Assert.Empty(p.State.NpcMemory); // B55: VEGA hands it out — no coined quartermaster remembers the player
        int y = AirRow(server, p);
        Give(p, "crystal_switch", 1);
        Give(p, "crystal_conduit", 4);
        Give(p, "light_white", 1);
        server.PlaceBlock(id, 1, y, 0, "crystal_switch");
        server.PlaceBlock(id, 2, y, 0, "crystal_conduit");
        server.PlaceBlock(id, 3, y, 0, "light_white");
        Ticks(server, 0.8);
        Assert.Equal(new[] { 0 }, server.MissionProgressForTest(id, Jobs[0])); // placed is not enough
        server.SetCrystalDeviceForTest(p, new Vector3i(1, y, 0), action: 0); // flip the switch
        Ticks(server, 0.8);
        Assert.Equal(new[] { 1 }, server.MissionProgressForTest(id, Jobs[0])); // it works
        Assert.False(Done(p, "c8.1")); // the step waits for the hand-in
        Assert.DoesNotContain("vega.hint.crystal_first", transport.LineKeys(p)); // the step's line taught the conduit line

        int crystal = p.State.Inventory.CountOf("crystal"), glass = p.State.Inventory.CountOf("glass"), knowledge = p.State.KnowledgePoints;
        server.TurnInMission(id, Jobs[0]);
        Assert.True(Done(p, "c8.1"));
        Assert.Equal((crystal + 3, glass + 3, knowledge + 5), (p.State.Inventory.CountOf("crystal"), p.State.Inventory.CountOf("glass"), p.State.KnowledgePoints));
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.first_circuit")); // "First circuit · It works!"

        // B55: no quartermaster on the radio — and VEGA's own follow-up stays quiet: the course's next line is the follow-up.
        Assert.Equal(0, server.DialogRadioPendingForTest);
        Assert.Empty(transport.FollowUps(p));
        Assert.Contains("story.tutorial.c8.bell.start_research", transport.LineKeys(p)); // job 2 needs Crystal Sound first
        Assert.Equal("story.tutorial.c8.bell.chip_research", server.ObjectiveKeyForTest(id));
        Unlock(p, "crystal_sound");
        Assert.Equal("story.tutorial.c8.bell.chip", server.ObjectiveKeyForTest(id)); // the chip follows the research

        // Jobs 2–6 through the real turn-in: each completes its step, each pays its own reward.
        for (int i = 1; i < Jobs.Length; i++)
        {
            Unlock(p, JobBlueprints[i]);
            HandIn(server, p, Jobs[i]);
            Assert.True(Done(p, "c8." + (i + 1)), Jobs[i]);
        }

        Assert.Equal(knowledge + 5 + 5 + 8 + 8 + 10 + 12, p.State.KnowledgePoints);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c8")));
        Assert.Contains("tut.done@c8", transport.Plays(p));
        Assert.Empty(transport.FollowUps(p));
        Assert.Equal(0, server.DialogRadioPendingForTest);

        // A replay: the Crystal Net blueprint is known (#2459: a research step a replay cannot ask for again passes — it no
        // longer waits for "Skip step") and the jobs are handed in, so their bridges are through at once — the replay ends
        // there, and the banner never comes twice.
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.ReplayChapter, ChapterId = "c8" });
        Assert.False(Has(p, StoryPackKeys.ChapterReplay(T, "c8")));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c8")));
        Assert.Equal(1, transport.Plays(p).Count(c => c == "tut.first_circuit"));
    }

    [Fact]
    public void ARunningChain_IsAdopted_TheJobsDoneBeforeCountSilently_AndTheSameJobsCarryOn()
    {
        var (server, transport) = World("c8_adopt");
        var p = LocalPastTraining(server, "Wirer", cutscenes: true);
        string id = p.State.PlayerId;
        Unlock(p, "comm_radio", "crystal_conduit", "crystal_sound");
        HandIn(server, p, Jobs[0]); // before the course: VEGA's own follow-up
        Assert.Equal(SvGameServer.VegaChainNextLine, Assert.Single(transport.FollowUps(p)).LineKey);
        server.AcceptMission(id, Jobs[1]); // job 2 running
        int before = transport.LineKeys(p).Count;

        Start(server, p, "c8");
        Assert.True(Done(p, "c8.0")); // the blueprint …
        Assert.True(Done(p, "c8.1")); // … and the job handed in: done silently
        Assert.Equal("c8.2", server.ChipStepForTest(p, T));
        var spoken = transport.LineKeys(p).Skip(before).ToList();
        Assert.DoesNotContain("story.tutorial.c8.net.start", spoken);
        Assert.DoesNotContain("story.tutorial.c8.light.start", spoken);
        Assert.Contains("story.tutorial.c8.bell.start", spoken); // the running job, with its blueprint owned
        Assert.DoesNotContain(StoryPackKeys.CutsceneSent(T, "tut.first_circuit"), p.State.Milestones); // no banner for an old circuit
        Assert.Single(p.State.Missions, m => m.MissionId == Jobs[1]); // the same job row — nothing copied, nothing re-offered
        Assert.DoesNotContain(Jobs[1], server.VisibleMissionIdsForTest(id));

        // The running job handed in completes its step; the course speaks the next one — VEGA's own line stays quiet.
        p.State.Missions.Single(m => m.MissionId == Jobs[1]).ObjectiveProgress[0] = 1;
        server.TurnInMission(id, Jobs[1]);
        Assert.True(Done(p, "c8.2"));
        Assert.Equal("c8.3", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c8.night.start_research", transport.LineKeys(p));
        Assert.Single(transport.FollowUps(p)); // only the one from before the course
    }

    [Fact]
    public void ADeclinedCourse_LeavesTheChainAsItWas_AndVegaSpeaksItsFollowUp_WithoutARadio()
    {
        var (server, transport) = World("c8_declined");
        var p = LocalPastTraining(server, "Solo");
        string id = p.State.PlayerId;
        Unlock(p, "comm_radio", "crystal_conduit");
        server.TickStoryPacksForTest(p);
        Assert.Single(transport.Offers(p));
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.DeclineChapter, ChapterId = "c8" }); // "Later"
        Assert.Equal(StoryChapterStates.Declined, ChapterState(server, p, "c8").State);
        Assert.Equal(0, p.State.Inventory.CountOf("comm_radio")); // no radio worn or carried

        int crystal = p.State.Inventory.CountOf("crystal");
        HandIn(server, p, Jobs[0]);
        Assert.Equal(crystal + 3, p.State.Inventory.CountOf("crystal")); // the job pays as always
        Assert.False(Done(p, "c8.1")); // the course did not run
        var line = Assert.Single(transport.FollowUps(p));
        Assert.Equal(SvGameServer.VegaChainNextResearchLine, line.LineKey); // job 2 needs Crystal Sound first
        Assert.Equal("Crystal Workshop: Doorbell" + LineText.ArgSeparator + "Crystal Sound", line.LineArg);
        Assert.Equal(0, line.Kind); // a mission message: never muted with the hints
        Assert.Equal(string.Empty, line.Speaker); // VEGA herself
        Assert.Equal(0, server.DialogRadioPendingForTest); // no coined quartermaster calls later
        Assert.Empty(p.State.NpcMemory);

        Unlock(p, "crystal_sound", "crystal_sensors");
        HandIn(server, p, Jobs[1]);
        line = transport.FollowUps(p).Last();
        Assert.Equal((SvGameServer.VegaChainNextLine, "Crystal Workshop: Night Light"), (line.LineKey, line.LineArg));

        // An older client (no packs1) has no text for the new line: it arrives composed in its language.
        var old = Join(server, 2, "Oldie", current: false);
        Unlock(old, "crystal_conduit");
        HandIn(server, old, Jobs[0]);
        var composed = transport.To<ShipAiLine>(old).Last(l => l.Text.Contains("Crystal Workshop: Doorbell", StringComparison.Ordinal));
        Assert.Equal(string.Empty, composed.LineKey);
        Assert.Contains("Crystal Sound", composed.Text, StringComparison.Ordinal);
        Assert.Equal(0, server.DialogRadioPendingForTest);
    }

    // ---------------- C14 Factory and automation ----------------

    [Fact]
    public void TheFactoryCourse_WalksThroughTheRealHandlers_TerminalClaimAndAnAssemblyLine()
    {
        var (server, transport) = World("c14_walk");
        var p = LocalPastTraining(server, "Foreman", cutscenes: true);
        string id = p.State.PlayerId;
        int y = AirRow(server, p);
        var terminal = new Vector3f(0.5f, y + 3, 2.5f);
        int factory = server.AddFactoryForTest(terminal, "factory_iron_plate");
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.FactoryHere));

        Start(server, p, "c14");
        Assert.Contains("story.tutorial.c14.terminal.start", transport.LineKeys(p));
        Assert.Equal("c14.1", server.ChipStepForTest(p, T));
        Assert.True(server.HintHeldByTutorialForTest(p, "factory_near")); // the course explains the factory: the tip waits

        // Bulk crafting at the terminal — the real roster check: raw ore in, a plate out.
        int plates = p.State.Inventory.CountOf("iron_plate");
        Give(p, "iron_ore", 6);
        server.Craft(id, "factory_iron_plate");
        Assert.Equal(plates + 1, p.State.Inventory.CountOf("iron_plate"));
        Assert.True(Done(p, "c14.1"));

        // The SPS code explained (never asked for); the machine lesson waits for its prerequisites and says why.
        Assert.Contains("story.tutorial.c14.claim.start", transport.LineKeys(p));
        Assert.Contains("story.tutorial.c14.wait.start", transport.LineKeys(p));
        Assert.Null(server.ChipStepForTest(p, T));
        Assert.True(Has(p, StoryPackKeys.ChapterActive(T, "c14")));

        // A factory claimed anyway (the real claim, a code spent): VEGA says what changed.
        Give(p, "access_code", 1);
        p.State.Position = terminal;
        server.ClaimFactory(id, factory);
        Assert.Equal(id, server.FactoriesForTest.Single(f => f.Id == factory).OwnerId);
        Assert.Contains("story.tutorial.c14.claimed.start", transport.LineKeys(p));
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.FactoryOwned));

        // The Crystal workshop's machine job and a forge: the assembly line opens.
        p.State.Milestones.Add(StoryPackKeys.StepDone(T, "c8.5"));
        p.State.AchievementCounters[AchievementCounters.Craft("forge")] = 1;
        server.TickStoryPacksForTest(p);
        Assert.Equal("c14.5", server.ChipStepForTest(p, T));
        Assert.Contains("story.tutorial.c14.line.start", transport.LineKeys(p));

        // A fabricator beside its crate and a forge, started twice (the real machine code): two plates, two crafts.
        p.State.Position = new Vector3f(2.5f, y + 3, 0.5f);
        Give(p, "fabricator", 1);
        Give(p, "crate", 1);
        Give(p, "forge", 1);
        var fab = new Vector3i(1, y, 0);
        server.PlaceBlock(id, fab.X, fab.Y, fab.Z, "fabricator");
        server.PlaceBlock(id, 0, y, 0, "crate");
        server.SetCrystalDeviceForTest(p, fab, action: 2, config: "recipe=titanium_plate");
        var crate = server.Containers.Single(c => c.Position == new Vector3i(0, y, 0));
        crate.Items.Add(new ItemStack("titanium_ore", 4));
        server.PlaceBlock(id, 2, y, 0, "forge");
        server.SetCrystalDeviceForTest(p, fab, action: 1);
        Assert.False(Done(p, "c14.5"));
        server.SetCrystalDeviceForTest(p, fab, action: 1);
        Assert.Equal(2, crate.Items.Single(s => s.Item == "titanium_plate").Count);
        Assert.True(Done(p, "c14.5"));

        // What machines do while nobody is there; the course is through.
        Assert.Contains("story.tutorial.c14.away.start", transport.LineKeys(p));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c14")));
        Assert.Contains("tut.done@c14", transport.Plays(p));
    }

    [Fact]
    public void TheFactoryCourse_ExplainsWhatTheWorldCannotShow_AndCountsAMachineCircuitBuiltBefore()
    {
        var (server, transport) = World("c14_rules", c => c.Rules.MachineCatchUpMinutes = 0);
        var p = LocalPastTraining(server, "Remote");
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.FactoryHere));
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.MachineCatchUp));

        // A machine circuit built before the course (a signal-started job's counter) and a forge crafted long ago.
        Assert.False(server.WorldPredicateForTest(p, StoryWorldPredicates.MachineCircuit));
        p.State.AchievementCounters[AchievementCounters.Crystal(CircuitEvents.MachineJob)] = 1;
        p.State.AchievementCounters[AchievementCounters.Craft("forge")] = 1;
        Assert.True(server.WorldPredicateForTest(p, StoryWorldPredicates.MachineCircuit));

        Start(server, p, "c14");
        Assert.True(Skipped(p, "c14.1")); // no factory on this world …
        Assert.Contains("story.tutorial.c14.terminal.start_later", transport.LineKeys(p)); // … so the stand-in tells what one does
        Assert.Contains("story.tutorial.c14.claim.start", transport.LineKeys(p));
        Assert.DoesNotContain("story.tutorial.c14.wait.start", transport.LineKeys(p)); // nothing to wait for
        Assert.Equal("c14.5", server.ChipStepForTest(p, T));
        server.RaiseGameEventForTest(p, GameEventKeys.Crystal(CircuitEvents.Craft), string.Empty, 2);
        Assert.True(Done(p, "c14.5"));
        Assert.Contains("story.tutorial.c14.away.start_off", transport.LineKeys(p)); // machines rest on this world
        Assert.DoesNotContain("story.tutorial.c14.away.start", transport.LineKeys(p));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(T, "c14")));

        // A factory's owner is not told how to claim one.
        var owner = LocalPastTraining(server, "Owner");
        int y = AirRow(server, owner);
        var terminal = new Vector3f(0.5f, y + 3, 2.5f);
        int factory = server.AddFactoryForTest(terminal, "factory_iron_plate");
        Give(owner, "access_code", 1);
        owner.State.Position = terminal;
        server.ClaimFactory(owner.State.PlayerId, factory);
        Assert.True(server.WorldPredicateForTest(owner, StoryWorldPredicates.FactoryOwned));
        Start(server, owner, "c14");
        Give(owner, "iron_ore", 6);
        server.Craft(owner.State.PlayerId, "factory_iron_plate");
        Assert.True(Done(owner, "c14.1"));
        Assert.True(Skipped(owner, "c14.2"));
        Assert.DoesNotContain("story.tutorial.c14.claim.start", transport.LineKeys(owner));
        Assert.Contains("story.tutorial.c14.wait.start", transport.LineKeys(owner)); // no machine circuit yet: the course waits
    }

    // ---------------- The new verbs and the VEGA giver, raised by their real handlers ----------------

    [Fact]
    public void TheCraftStationAndClaimVerbs_AreReportedFromTheirHandlers_AndVegaNeedsTheRadioSurface()
    {
        var (server, _) = World("verb_crystal");
        var events = new RecordingGameEventListener();
        server.AddGameEventListener("*", events);
        var p = server.AddLocalPlayer("Crafter");
        string id = p.State.PlayerId;
        int y = AirRow(server, p);
        var terminal = new Vector3f(0.5f, y + 3, 2.5f);
        int factory = server.AddFactoryForTest(terminal, "factory_iron_plate");

        Give(p, "iron_ore", 6);
        server.Craft(id, "factory_iron_plate");
        Assert.Contains(events.Of(GameEventKeys.CraftStation), e => e.Arg == "factory");
        Assert.Contains(events.Of(GameEventKeys.CraftAny), e => e.Arg == "factory_iron_plate");

        server.ClaimFactory(id, factory); // no code: refused, nothing raised
        Assert.Empty(events.Of(GameEventKeys.FactoryClaimed));
        Give(p, "access_code", 1);
        p.State.Position = terminal;
        server.ClaimFactory(id, factory);
        Assert.Equal("Test Works", Assert.Single(events.Of(GameEventKeys.FactoryClaimed)).Arg);

        // The content rule: VEGA has no board or face — her jobs come over the radio surface only.
        Assert.True(MissionChains.IsValidGiverRole(MissionChains.GiverVega));
        Assert.All(_content.Missions.Values.Where(MissionChains.IsVegaGiver), m => Assert.Equal(MissionChains.SurfaceRadio, MissionChains.SurfaceOf(m)));
        Assert.Equal(Jobs, _content.Missions.Values.Where(MissionChains.IsVegaGiver).Select(m => m.Id).OrderBy(x => x, StringComparer.Ordinal));
    }
}
