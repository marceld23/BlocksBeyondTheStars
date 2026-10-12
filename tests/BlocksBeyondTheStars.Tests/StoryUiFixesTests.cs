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
/// #2462 — step 3 of the story packs (#2459): the server side of the story UI fixes. The objective chip names its
/// source and whether the tutorial runs (B03), the finale outranks a running tutorial only in the Guardian system
/// (B11), the story snapshot lists the beats this player heard (B08), a mission result says what happened and names
/// the mission (B24), a muted hint waits instead of burning its once-flag (B31), the story/tutorial/preference intents
/// get through a held pause (B12) — plus the wire round-trips of every new field and the new locale keys (B24, B25, N10).
/// </summary>
public sealed class StoryUiFixesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public StoryUiFixesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_storyui_" + Guid.NewGuid().ToString("N"));
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
    }

    private SvGameServer Boot(string world, RecordingTransport transport, Action<ServerConfig>? configure = null)
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
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>The legacy onboarding stages the tutorial double-writes for older builds.</summary>
    private static readonly IReadOnlyList<string> Stages = StoryLegacy.StageIds;

    /// <summary>#2469: the official tutorial pack's first chip — what a fresh player's basic training shows.</summary>
    private const string FirstChip = "story.tutorial.b1.walk.chip";

    /// <summary>Past the basic training: "I know the game" (the tutorial pack skipped, #2469).</summary>
    private static void FinishTutorial(SvGameServer server, PlayerSession p) => server.SkipTutorialForTest(p);

    /// <summary>Leaves the basic training silently, behind the server's back (the opt-out key — no handler, no line).</summary>
    private static void LeaveTutorialSilently(PlayerSession p)
        => p.State.Milestones.Add(StoryPackKeys.OptOut(SvGameServer.OfficialTutorialPackId));

    private static void CompleteTheArc(SvGameServer server)
    {
        for (int i = 0; i < 400 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        Assert.True(server.IsGuardianSystemRevealedForTest);
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    /// <summary>Joins through the real receive path (the join a client makes — VEGA boots, the story catches up).
    /// <c>AddLocalPlayer</c> skips both.</summary>
    private static PlayerSession JoinOverWire(SvGameServer server, int conn, string name)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = new[] { ClientFeatures.Packs1 }, // a current client
        }));
        return server.Sessions[conn];
    }

    // ---------------- ObjectiveSource (B03 / B11) ----------------

    [Fact]
    public void EveryChipLine_NamesItsSource_AndWhetherTheTutorialRuns()
    {
        var transport = new RecordingTransport();
        var server = Boot("source", transport);
        var p = JoinOverWire(server, 1, "Pilot");

        var join = transport.To<ShipAiLine>(p).Last();
        Assert.Equal(FirstChip, join.ObjectiveKey);
        Assert.Equal(ObjectiveSources.Tutorial, join.ObjectiveSource);
        Assert.True(join.TutorialActive);

        transport.Sent.Clear();
        Deliver(server, p, new SkipOnboardingIntent()); // skip: the basic training is through, the story objective takes the chip
        var after = transport.To<ShipAiLine>(p).Last();
        Assert.StartsWith("story.obj.", after.ObjectiveKey);
        Assert.Equal(ObjectiveSources.Story, after.ObjectiveSource);
        Assert.False(after.TutorialActive);
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest("Pilot"));
    }

    [Fact]
    public void WithoutAStory_TheEmptyChipHasNoSource()
    {
        var transport = new RecordingTransport();
        var server = Boot("nosource", transport, c => c.Rules.StoryId = "none");
        var p = server.AddLocalPlayer("Pilot");
        FinishTutorial(server, p);

        Assert.Equal(string.Empty, server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(ObjectiveSources.None, server.ObjectiveSourceForTest("Pilot"));
    }

    /// <summary>B11: a newcomer on a world whose finale is open keeps the lesson on the chip at home; in the Guardian
    /// system the finale takes the chip — and the line still says the tutorial runs, so the Settings button offers
    /// "Skip", never "Restart" (B03).</summary>
    [Fact]
    public void AnOpenFinale_OutranksARunningTutorial_OnlyInTheGuardianSystem()
    {
        var transport = new RecordingTransport();
        var server = Boot("finale_chip", transport);
        var veteran = server.AddLocalPlayer("Veteran");
        FinishTutorial(server, veteran);
        CompleteTheArc(server);

        var newcomer = JoinOverWire(server, 2, "Newcomer");
        Assert.Equal(FirstChip, server.ObjectiveKeyForTest("Newcomer"));
        Assert.Equal(ObjectiveSources.Tutorial, server.ObjectiveSourceForTest("Newcomer"));
        var atHome = transport.To<ShipAiLine>(newcomer).Last();
        Assert.Equal(ObjectiveSources.Tutorial, atHome.ObjectiveSource);
        Assert.True(atHome.TutorialActive);

        // Whoever finished the lessons sees the finale everywhere — it is never muted.
        Assert.Equal("story.obj.finale", server.ObjectiveKeyForTest("Veteran"));
        Assert.Equal(ObjectiveSources.Finale, server.ObjectiveSourceForTest("Veteran"));

        transport.Sent.Clear();
        server.AdminGotoCore(newcomer);
        Assert.Equal(SvGameServer.GuardianCoreBodyId, newcomer.CurrentLocationId);
        Assert.Equal("story.obj.finale", server.ObjectiveKeyForTest("Newcomer"));
        var atCore = transport.To<ShipAiLine>(newcomer).Last();
        Assert.Equal("story.obj.finale", atCore.ObjectiveKey);
        Assert.Equal(ObjectiveSources.Finale, atCore.ObjectiveSource);
        Assert.True(atCore.TutorialActive); // the lessons are unfinished — Skip, not Restart
    }

    /// <summary>The 1 Hz refresh compares the whole chip: a tutorial finished while the finale sits on the chip changes
    /// neither key nor counter, only the tutorial flag — and the Settings button must still learn it.</summary>
    [Fact]
    public void TheOneHertzRefresh_ResendsTheChip_WhenOnlyTheTutorialFlagMoves()
    {
        var transport = new RecordingTransport();
        var server = Boot("refresh", transport);
        CompleteTheArc(server);
        var p = JoinOverWire(server, 1, "Pilot");
        server.AdminGotoCore(p);
        server.Tick(1.1); // settle the chip in the Guardian system
        Assert.True(transport.To<ShipAiLine>(p).Last().TutorialActive);

        transport.Sent.Clear();
        LeaveTutorialSilently(p); // silently — no lesson line goes out
        server.Tick(1.1);

        var chip = transport.To<ShipAiLine>(p).LastOrDefault();
        Assert.NotNull(chip);
        Assert.Equal("story.obj.finale", chip!.ObjectiveKey);
        Assert.Equal(ObjectiveSources.Finale, chip.ObjectiveSource);
        Assert.False(chip.TutorialActive);
    }

    // ---------------- HeardBeats (B08) ----------------

    [Fact]
    public void TheStorySnapshot_ListsTheBeatsThisPlayerHeard()
    {
        var transport = new RecordingTransport();
        var server = Boot("heard", transport);
        var p = JoinOverWire(server, 1, "Pilot");
        Assert.Equal(new[] { 0 }, server.StoryStateForTest("Pilot")!.HeardBeats); // B0 at the first join (the prologue's page)
        server.StartStoryTrackForTest(p); // #2470: the story reaches a player once their track started

        for (int i = 0; i < 3; i++)
        {
            server.RecordStoryMilestoneForTest(); // score 6: B1
        }

        Assert.Equal(new[] { 0, 1 }, server.StoryStateForTest("Pilot")!.HeardBeats);
        // The broadcast after the beat carries it too — the client rebuilds the list from it.
        Assert.Equal(new[] { 0, 1 }, transport.To<StoryStateMessage>(p).Last().HeardBeats);

        // Other per-player story keys of the pack are no beats.
        p.State.Milestones.Add("story:vega_protocol:finale:revealed");
        p.State.Milestones.Add("story:vega_protocol:beat:");
        Assert.Equal(new[] { 0, 1 }, server.StoryStateForTest("Pilot")!.HeardBeats);
    }

    [Fact]
    public void ALatecomer_HearsTheCaughtUpBeats_InTheirOwnSnapshot()
    {
        var transport = new RecordingTransport();
        var server = Boot("latecomer", transport);
        server.AddLocalPlayer("Host");
        for (int i = 0; i < 3; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        var guest = JoinOverWire(server, 2, "Guest");
        Assert.Equal(new[] { 0 }, server.StoryStateForTest("Guest")!.HeardBeats); // #2470: B1 is held during the training

        server.StartStoryTrackForTest(guest);
        Assert.Equal(new[] { 0, 1 }, server.StoryStateForTest("Guest")!.HeardBeats);
        // The track start speaks the chapter's beat as a narrator line too (the client keeps those live on top of the snapshot).
        Assert.Contains(transport.To<ShipAiLine>(guest), l => l.Kind == 2 && l.LineKey == _content.Stories["vega_protocol"].Beats[1].TextKey);
    }

    [Fact]
    public void WithoutAStory_NoBeatsAreListed()
    {
        var transport = new RecordingTransport();
        var server = Boot("heard_none", transport, c => c.Rules.StoryId = "none");
        server.AddLocalPlayer("Pilot");
        Assert.Empty(server.StoryStateForTest("Pilot")!.HeardBeats);
    }

    // ---------------- Mission results (B24) ----------------

    private static MissionDefinition CollectStone(string id) => new()
    {
        Id = id,
        Source = MissionSource.System,
        NameKey = "mission.settlement.scan_wildlife.title",
        DescriptionKey = "mission.settlement.scan_wildlife.desc",
        Objectives = { new MissionObjective { Type = MissionObjectiveType.Collect, Target = "stone", Required = 1 } },
        Rewards = { new ItemAmount("medpack", 1) },
        Active = true,
    };

    [Fact]
    public void AMissionResult_SaysAcceptedOnAccept_AndCompleteOnlyOnTheTurnIn()
    {
        var transport = new RecordingTransport();
        var server = Boot("mission_toast", transport);
        var p = server.AddLocalPlayer("Pilot");
        var def = CollectStone("test_toast");
        server.AddMissionDefForTest(def);

        transport.Sent.Clear();
        server.AcceptMission("Pilot", def.Id);
        var accepted = Assert.Single(transport.To<MissionResult>(p));
        Assert.True(accepted.Success);
        Assert.Equal(MissionOutcomes.Accepted, accepted.Outcome);
        Assert.Equal(def.NameKey, accepted.Title);
        Assert.False(accepted.FreeText);

        p.State.Inventory.Add("stone", 5, 64);
        transport.Sent.Clear();
        server.TurnInMission("Pilot", def.Id);
        var done = Assert.Single(transport.To<MissionResult>(p));
        Assert.True(done.Success);
        Assert.Equal(MissionOutcomes.Completed, done.Outcome);
        Assert.Equal(def.NameKey, done.Title);
    }

    [Fact]
    public void APostedPlayerMission_IsNamedByItsFreeTextTitle()
    {
        var transport = new RecordingTransport();
        var server = Boot("mission_posted", transport);
        var p = server.AddLocalPlayer("Poster");
        p.State.Inventory.Add("stone", 10, 64);

        transport.Sent.Clear();
        Assert.True(server.CreatePlayerMissionForTest("Poster", "Bring me stones", "stone", MissionObjectiveType.Collect, 3, "stone", 2));
        var posted = Assert.Single(transport.To<MissionResult>(p));
        Assert.Equal(MissionOutcomes.Posted, posted.Outcome);
        Assert.Equal("Bring me stones", posted.Title);
        Assert.True(posted.FreeText);
    }

    [Fact]
    public void AFailedMission_StillCarriesItsReason()
    {
        var transport = new RecordingTransport();
        var server = Boot("mission_fail", transport);
        var p = server.AddLocalPlayer("Pilot");

        server.AcceptMission("Pilot", "no_such_mission");
        var failed = Assert.Single(transport.To<MissionResult>(p));
        Assert.False(failed.Success);
        Assert.Equal("@srv.mission.unknown", failed.Reason);
        Assert.Equal(string.Empty, failed.Outcome);
    }

    // ---------------- Muted hints wait (B31) ----------------

    private SvGameServer BootJungle(string world, RecordingTransport transport)
        => Boot(world, transport, c =>
        {
            c.StartPlanet = "jungle";
            c.Seed = 11;
            c.World.TerrainGeneration = 0;
        });

    /// <summary>The first float above the atmosphere line is a once-hint (Kind 1). Muted, it must not burn: it comes on
    /// the next float once the hints are on again.</summary>
    [Fact]
    public void AMutedHint_IsHeldBack_AndComesOnceTheHintsAreOnAgain()
    {
        var transport = new RecordingTransport();
        var server = BootJungle("muted_hint", transport);
        var p = server.AddLocalPlayer("Climber");
        p.State.AboardShip = false;
        p.State.GodMode = true;
        float line = (float)server.AtmosphereHeight;

        Deliver(server, p, new SetNpcCallsIntent { Mode = 0, VegaHintsMuted = true });
        Assert.True(p.VegaHintsMuted);

        int Hints() => transport.To<ShipAiLine>(p).Count(l => l.LineKey == "vega.hint.zero_g");
        void Float(float y)
        {
            p.State.Position = new Vector3f(0, y, 0);
            server.Tick(0.2);
        }

        Float(line + 10f);
        Assert.True(p.State.AboveAtmosphere);
        Assert.Equal(0, Hints());
        Assert.DoesNotContain("vega:hint:zero_g", server.MilestonesForTest("Climber")); // not spent unseen

        Float(line - 20f); // back down …
        Deliver(server, p, new SetNpcCallsIntent { Mode = 0, VegaHintsMuted = false });
        Float(line + 10f); // … and up again with the hints on: now VEGA explains it
        Assert.Equal(1, Hints());
        Assert.Contains("vega:hint:zero_g", server.MilestonesForTest("Climber"));
    }

    [Fact]
    public void AMutedPlayer_GetsNoContextTip_AndKeepsTheTipCounts()
    {
        var transport = new RecordingTransport();
        var server = Boot("muted_tips", transport, c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;     // nothing wanders in while the test pins the vitals
            c.Rules.CreatureAbundance = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
        });
        var p = server.AddLocalPlayer("Pilot");
        p.State.AboardShip = false; // no god mode: it keeps the oxygen full, and the tip reads the real bar
        Deliver(server, p, new SetNpcCallsIntent { Mode = 0, VegaHintsMuted = true });

        // Low oxygen is a safety tip that ignores the cadence — muted, nothing goes out and nothing is counted.
        for (int i = 0; i < 90; i++)
        {
            p.State.Oxygen = 10f;
            p.State.Health = 100f;
            p.State.Hunger = 100f;
            server.Tick(1.0);
        }

        Assert.DoesNotContain(transport.To<ShipAiLine>(p), l => l.LineKey == "vega.hint.o2");
        Assert.DoesNotContain(server.MilestonesForTest("Pilot"), m => m.StartsWith("vega:hint:o2", StringComparison.Ordinal));

        // Hints on again, the air still thin: the tip comes — as its FIRST telling.
        Deliver(server, p, new SetNpcCallsIntent { Mode = 0, VegaHintsMuted = false });
        for (int i = 0; i < 30 && !transport.To<ShipAiLine>(p).Any(l => l.LineKey == "vega.hint.o2"); i++)
        {
            p.State.Oxygen = 10f;
            p.State.Health = 100f;
            p.State.Hunger = 100f;
            server.Tick(1.0);
        }

        Assert.Contains(transport.To<ShipAiLine>(p), l => l.LineKey == "vega.hint.o2" && l.Kind == 1);
        Assert.Contains("vega:hint:o2", server.MilestonesForTest("Pilot"));
    }

    [Fact]
    public void AnOldClientWithoutTheField_ReadsAsHintsOn()
    {
        // An older client sends SetNpcCallsIntent without the new field: the hints stay on, as before.
        Assert.False(new SetNpcCallsIntent().VegaHintsMuted);
        var decoded = Assert.IsType<SetNpcCallsIntent>(NetCodec.Decode(NetCodec.Encode(new SetNpcCallsIntent { Mode = 2 })));
        Assert.Equal(2, decoded.Mode);
        Assert.False(decoded.VegaHintsMuted);
    }

    // ---------------- The pause whitelist (B12) ----------------

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Player) HeldWorld(string world)
    {
        var transport = new RecordingTransport();
        var server = Boot(world, transport);
        var p = server.AddLocalPlayer("Solo");
        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);
        transport.Sent.Clear();
        return (server, transport, p);
    }

    [Fact]
    public void WhileHeld_TheSettingsPreferencesAreServed()
    {
        var (server, _, p) = HeldWorld("held_prefs");
        Deliver(server, p, new SetNpcCallsIntent { Mode = (int)NpcCallsMode.Off, VegaHintsMuted = true });
        Assert.Equal(NpcCallsMode.Off, p.State.NpcCallsMode);
        Assert.True(p.VegaHintsMuted);
        Assert.True(server.IsPaused);
    }

    [Fact]
    public void WhileHeld_SkippingTheTutorialIsServed()
    {
        var (server, transport, p) = HeldWorld("held_skip");
        Deliver(server, p, new SkipOnboardingIntent());
        Assert.All(Stages, s => Assert.Contains("vega:stage:" + s, server.MilestonesForTest("Solo")));
        Assert.Contains(transport.To<ShipAiLine>(p), l => l.LineKey == "vega.skip");
        Assert.True(server.IsPaused);
    }

    [Fact]
    public void WhileHeld_RestartingTheTutorialIsServed()
    {
        var (server, transport, p) = HeldWorld("held_restart");
        FinishTutorial(server, p);
        Deliver(server, p, new SkipOnboardingIntent { Restart = true });
        Assert.Contains(StoryPackKeys.ChapterReplay(SvGameServer.OfficialTutorialPackId, "b1"), server.MilestonesForTest("Solo"));
        Assert.Equal(FirstChip, transport.To<ShipAiLine>(p).Last().ObjectiveKey); // #2469: the tutorial pack replays from part 1
        Assert.True(server.IsPaused);
    }

    [Fact]
    public void WhileHeld_TheEndingCanBeWatchedAgain()
    {
        var (server, transport, p) = HeldWorld("held_ending");
        server.MarkGuardianDefeatedForTest();
        transport.Sent.Clear();
        Deliver(server, p, new RequestStoryResolutionIntent());
        Assert.Single(transport.To<StoryResolved>(p));
        Assert.True(server.IsPaused);
    }

    [Fact]
    public void WhileHeld_AnAdminCanSwitchTheStory()
    {
        var (server, _, p) = HeldWorld("held_story");
        p.State.Role = PlayerRole.Admin;
        Deliver(server, p, new StorySelectIntent { StoryId = "none" });
        Assert.Equal("none", server.StorySnapshot.StoryId);
    }

    // ---------------- Wire: additive fields (no protocol bump) ----------------

    [Fact]
    public void ShipAiLine_SourceAndTutorialFlag_RoundTrip_AndDefaultToLegacy()
    {
        var legacy = new ShipAiLine();
        Assert.Equal(ObjectiveSources.None, legacy.ObjectiveSource);
        Assert.False(legacy.TutorialActive);

        var line = new ShipAiLine { ObjectiveKey = "story.obj.finale", ObjectiveSource = ObjectiveSources.Finale, TutorialActive = true, Kind = 0 };
        var decoded = Assert.IsType<ShipAiLine>(NetCodec.Decode(NetCodec.Encode(line)));
        Assert.Equal("story.obj.finale", decoded.ObjectiveKey);
        Assert.Equal(ObjectiveSources.Finale, decoded.ObjectiveSource);
        Assert.True(decoded.TutorialActive);
    }

    [Fact]
    public void StoryState_HeardBeats_RoundTrip_AndDefaultToEmpty()
    {
        Assert.Empty(new StoryStateMessage().HeardBeats);
        var decoded = Assert.IsType<StoryStateMessage>(NetCodec.Decode(NetCodec.Encode(new StoryStateMessage
        {
            StoryId = "vega_protocol",
            Active = true,
            HeardBeats = new[] { 0, 1, 4 },
        })));
        Assert.Equal(new[] { 0, 1, 4 }, decoded.HeardBeats);
    }

    [Fact]
    public void MissionResult_OutcomeAndTitle_RoundTrip_AndDefaultToLegacy()
    {
        var legacy = new MissionResult { Success = true, MissionId = "m1" };
        Assert.Equal(string.Empty, legacy.Outcome);
        Assert.Equal(string.Empty, legacy.Title);

        var decoded = Assert.IsType<MissionResult>(NetCodec.Decode(NetCodec.Encode(new MissionResult
        {
            Success = true,
            MissionId = "pm_1",
            Outcome = MissionOutcomes.Posted,
            Title = "Bring me stones",
            FreeText = true,
        })));
        Assert.Equal(MissionOutcomes.Posted, decoded.Outcome);
        Assert.Equal("Bring me stones", decoded.Title);
        Assert.True(decoded.FreeText);
    }

    [Fact]
    public void SetNpcCalls_HintsMute_RoundTrips()
    {
        var decoded = Assert.IsType<SetNpcCallsIntent>(NetCodec.Decode(NetCodec.Encode(new SetNpcCallsIntent { Mode = 1, VegaHintsMuted = true })));
        Assert.Equal(1, decoded.Mode);
        Assert.True(decoded.VegaHintsMuted);
    }

    // ---------------- Texts (B24, B25, N10) ----------------

    [Fact]
    public void EveryCircuitObjective_HasAReadableLabel_InEnglishAndGerman()
    {
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        var keys = CircuitEvents.Objectives.Select(e => "ui.missions.circuittarget." + e)
            .Concat(new[] { "ui.missions.circuittarget.any", "ui.missions.objtype_circuit" });
        foreach (var key in keys)
        {
            Assert.True(en.Has(key), $"missing '{key}' in en");
            Assert.True(de.Has(key), $"missing '{key}' in de");
        }

        // Every circuit target the missions actually use is one of the labelled events.
        foreach (var m in _content.Missions.Values.Where(m => m.Objectives.Any(o => o.Type == MissionObjectiveType.Circuit)))
        {
            Assert.All(m.Objectives.Where(o => o.Type == MissionObjectiveType.Circuit), o => Assert.True(CircuitEvents.IsObjective(o.Target), o.Target));
        }
    }

    [Fact]
    public void TheMissionToastAndTheStoryFallback_AreLocalized()
    {
        var en = _content.CreateLocalizer(GameLocale.English);
        var de = _content.CreateLocalizer(GameLocale.German);
        foreach (var key in new[] { "ui.missions.toast.accepted", "ui.missions.toast.completed", "ui.missions.toast.posted", "ui.missions.toast.updated" })
        {
            Assert.Contains("{0}", en.Get(key));
            Assert.Contains("{0}", de.Get(key));
        }

        foreach (var key in new[] { "ui.missions.toast.unnamed", "ui.story.entry_missing" })
        {
            Assert.True(en.Has(key), $"missing '{key}' in en");
            Assert.True(de.Has(key), $"missing '{key}' in de");
        }
    }
}
