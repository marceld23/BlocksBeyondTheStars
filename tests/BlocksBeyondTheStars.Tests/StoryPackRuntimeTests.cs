// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2466 — step 7 of the story packs (#2459): the per-player runtime of the PLAYER packs, driven by the test packs
/// (<see cref="StoryPackFixtures"/>). A pack starts per player (world tutorial policy, defaultEnabled), steps activate in
/// order and count game events since activation (argument alternatives), read steps never block, react steps are armed
/// from the chapter start, a CORE step blocks until its hint time and then counts silently, an infeasible step lets its
/// alternative stand in, <c>requires</c> holds a step back, variants swap whole lines by world rule, skip / replay /
/// opt-out / offers work, help escalates on play time (hint → a marker for this session only → a skip offer), the chip
/// follows the binding order, a client without <c>packs1</c> gets the legacy tutorial mode, the legacy keys are
/// double-written and migrated once, the running counters persist (and an older build dropping them costs only the
/// count), and the admin cases and the pause whitelist behave.
/// </summary>
public sealed class StoryPackRuntimeTests : IDisposable
{
    private const string R = StoryPackFixtures.Runtime;
    private const string T = StoryPackFixtures.Tutorial;
    /// <summary>The legacy onboarding stages the tutorial double-writes (the one list: <see cref="StoryLegacy.StageIds"/>).</summary>
    private static readonly IReadOnlyList<string> LegacyStages = StoryLegacy.StageIds;

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public StoryPackRuntimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_packs_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The content of the last <see cref="SideWorld"/> / <see cref="TutorialWorld"/>.</summary>
    private GameContent? _lastContent;

    /// <summary>A world without a world story (so VEGA's lead stays off the chip) and only the side test pack.</summary>
    private (SvGameServer Server, RecordingTransport Transport) SideWorld(string world, Action<ServerConfig>? configure = null)
    {
        var transport = new RecordingTransport();
        _lastContent = StoryPackFixtures.ContentWith(R);
        var server = Boot(world, transport, _lastContent, c =>
        {
            c.Rules.StoryId = StoryRegistry.NoneStoryId;
            configure?.Invoke(c);
        });
        return (server, transport);
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

    /// <summary>Writes the nine legacy onboarding keys after the join, the way an older build would have. Since #2469 they
    /// are migration evidence only: written after the migration ran they change nothing (the tests that call it assert
    /// exactly that the pack's own state decides).</summary>
    private static void FinishLegacyChain(PlayerSession p)
    {
        foreach (string stage in LegacyStages)
        {
            p.State.Milestones.Add("vega:stage:" + stage);
        }
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Event(SvGameServer server, PlayerSession p, string key, string arg = "", int amount = 1)
        => server.RaiseGameEventForTest(p, key, arg, amount);

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static int Progress(PlayerSession p, string pack, string step)
        => p.State.StepProgress.TryGetValue(StoryPackKeys.ProgressKey(pack, step), out int n) ? n : -1;

    /// <summary>A side-world player (no tutorial pack there, #2469: nothing outranks the side pack's chip).</summary>
    private static PlayerSession SidePlayer(SvGameServer server, int conn = 1, string name = "Pilot", bool packs = true)
        => Join(server, conn, name, packs);

    // ---------------- Start, activation, counting ----------------

    [Fact]
    public void APlayerPack_StartsAtTheJoin_AndSpeaksItsFirstStep()
    {
        var (server, transport) = SideWorld("start");
        var p = Join(server, 1, "Pilot");

        Assert.True(Has(p, StoryPackKeys.Started(R)));
        Assert.Equal(0, Progress(p, R, "a.1"));
        var line = Assert.Single(transport.To<ShipAiLine>(p), l => l.LineKey == "story.runtime_test.a1.start");
        Assert.Equal(R, line.PackId);
        Assert.Equal("vega", line.Speaker);
        Assert.Equal(string.Empty, line.Text);
    }

    [Fact]
    public void Events_CountSinceActivation_CompleteTheStep_AndMoveTheChip()
    {
        var (server, transport) = SideWorld("count");
        var p = SidePlayer(server);
        Assert.Equal("story.runtime_test.a1.chip", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(R, server.ObjectivePackForTest("Pilot"));
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest("Pilot"));

        Event(server, p, GameEventKeys.MineAny, "stone");
        Event(server, p, GameEventKeys.MineAny, "dirt");
        Assert.Equal(2, Progress(p, R, "a.1"));
        var chip = transport.To<ShipAiLine>(p).Last();
        Assert.Equal("story.runtime_test.a1.chip", chip.ObjectiveKey);
        Assert.Equal(2, chip.ObjectiveProgress);
        Assert.Equal(3, chip.ObjectiveTarget);
        Assert.Equal(R, chip.ObjectivePackId);

        transport.Sent.Clear();
        Event(server, p, GameEventKeys.MineAny, "stone");
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.1")));
        Assert.Equal(-1, Progress(p, R, "a.1"));
        var keys = transport.LineKeys(p);
        Assert.Equal(new[] { "story.runtime_test.a1.done", "story.runtime_test.a2.start", "story.runtime_test.a3.start" },
            keys.Where(k => k.StartsWith("story.runtime_test.", StringComparison.Ordinal)).ToArray());
        Assert.Equal("story.runtime_test.a3.chip", server.ObjectiveKeyForTest("Pilot"));
    }

    [Fact]
    public void OnlyTheNamedArguments_Count_AndTheDoStepClosesTheWaitingReadStep()
    {
        var (server, _) = SideWorld("args");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny, amount: 3);
        Assert.Equal(0, Progress(p, R, "a.2")); // the read step waits for "continue"

        Event(server, p, GameEventKeys.CraftAny, "iron_plate");
        Assert.Equal(0, Progress(p, R, "a.3"));
        Event(server, p, GameEventKeys.CraftAny, "campfire");
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.3")));
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.2"))); // closed with the next do step
    }

    [Fact]
    public void AReadStep_CompletesOnContinue()
    {
        var (server, _) = SideWorld("read");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny, amount: 3);
        Event(server, p, GameEventKeys.UiVegaContinue);
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.2")));
        Assert.Equal(0, Progress(p, R, "a.3")); // the do step after it was already active
    }

    [Fact]
    public void AReactStep_IsArmedFromTheChapterStart_NeverBlocks_AndSpeaksWhenItFires()
    {
        var (server, transport) = SideWorld("react");
        var p = SidePlayer(server);
        Assert.Equal(0, Progress(p, R, "a.5"));
        Assert.Equal("story.runtime_test.a1.chip", server.ObjectiveKeyForTest("Pilot"));

        transport.Sent.Clear();
        Event(server, p, GameEventKeys.DefeatAny, GameEventKeys.DefeatCreature);
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.5")));
        Assert.Contains("story.runtime_test.a5.start", transport.LineKeys(p));
        Assert.Equal("story.runtime_test.a1.chip", server.ObjectiveKeyForTest("Pilot"));
    }

    [Fact]
    public void ACoreStep_BlocksUntilItsHintTime_ThenCountsSilently_AndRequiresHoldsTheNextStepBack()
    {
        var (server, transport) = SideWorld("core");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny, amount: 3);
        Event(server, p, GameEventKeys.CraftAny, "torch");
        Assert.Equal("story.runtime_test.a4.chip", server.ObjectiveKeyForTest("Pilot"));
        Assert.False(Has(p, StoryPackKeys.ChapterDone(R, "a")));

        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 3); // the step's own hintAfter: 3 s
        Assert.Contains("story.runtime_test.a4.hint", transport.LineKeys(p));
        Assert.True(Has(p, StoryPackKeys.ChapterDone(R, "a")));
        Assert.Equal(0, Progress(p, R, "a.4")); // an open extra: still listening
        Assert.Equal("story.runtime_test.b1.chip", server.ObjectiveKeyForTest("Pilot"));

        Event(server, p, GameEventKeys.StationBoarded, "st1");
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "b.1")));
        Assert.Equal(-1, Progress(p, R, "b.3")); // waits for step:runtime_test:a.4
        Assert.Equal(string.Empty, server.ObjectiveKeyForTest("Pilot"));

        transport.Sent.Clear();
        Event(server, p, GameEventKeys.EatAny, "berry");
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.4")));
        Assert.DoesNotContain(transport.LineKeys(p), k => k.StartsWith("story.runtime_test.a4", StringComparison.Ordinal));
        Assert.Equal(0, Progress(p, R, "b.3"));
        Assert.Contains("story.runtime_test.b3.start", transport.LineKeys(p));
    }

    [Fact]
    public void AnInfeasibleStep_IsSkippedSilently_AndItsAlternativeStandsIn()
    {
        var (server, transport) = SideWorld("alt", c => c.Rules.HungerConsumption = HungerConsumption.Off);
        var p = SidePlayer(server);
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipChapter, ChapterId = "a" });

        Assert.True(Has(p, StoryPackKeys.StepSkipped(R, "b.1")));
        Assert.Equal(0, Progress(p, R, "b.2"));
        Assert.Equal("story.runtime_test.b2.chip", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(-1, Progress(p, R, "b.3")); // the stand-in holds the chapter where b.1 was

        Event(server, p, GameEventKeys.TradeBarter, "iron_plate");
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "b.2")));
        Event(server, p, GameEventKeys.TravelLanded, GameEventKeys.TravelAdmin);
        Assert.False(Has(p, StoryPackKeys.StepDone(R, "b.3"))); // an admin move is not a landing the step counts
        Event(server, p, GameEventKeys.TravelLanded, GameEventKeys.TravelQuick);
        Assert.True(Has(p, StoryPackKeys.ChapterDone(R, "b")));
        Assert.True(Has(p, StoryPackKeys.Done(R)));
        Assert.DoesNotContain("story.runtime_test.b1.start", transport.LineKeys(p));
    }

    [Fact]
    public void Variants_SwapTheWholeLine_ByWorldRule()
    {
        var (server, transport) = SideWorld("variant", c => c.Rules.GameMode = GameMode.Creative);
        var p = Join(server, 1, "Pilot");
        Assert.Contains("story.runtime_test.a1.start_creative", transport.LineKeys(p));
        Assert.DoesNotContain("story.runtime_test.a1.start", transport.LineKeys(p));
    }

    // ---------------- Skip, replay, opt-out, offers ----------------

    [Fact]
    public void SkippingAStep_AChapter_AndThePack_GrantsNothing_AndOpensWhatWaits()
    {
        var (server, _) = SideWorld("skip");
        var p = SidePlayer(server);
        int kp = p.State.KnowledgePoints;

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipStep, StepId = "a.1" });
        Assert.True(Has(p, StoryPackKeys.StepSkipped(R, "a.1")));
        Assert.Equal(0, Progress(p, R, "a.3"));

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipChapter, ChapterId = "a" });
        Assert.True(Has(p, StoryPackKeys.ChapterSkipped(R, "a")));
        Assert.True(Has(p, StoryPackKeys.StepSkipped(R, "a.4")));
        Assert.Equal(0, Progress(p, R, "b.1"));
        Assert.True(server.EvaluateStoryConditionForTest("chapter:runtime_test:a", p));

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipPack });
        Assert.True(Has(p, StoryPackKeys.ChapterSkipped(R, "b")));
        Assert.True(Has(p, StoryPackKeys.Done(R)));
        Assert.False(Has(p, StoryPackKeys.ChapterSkipped(R, "c"))); // the optional chapter stays offerable
        Assert.Equal(kp, p.State.KnowledgePoints);
        Assert.Equal(string.Empty, server.ObjectiveKeyForTest("Pilot"));
    }

    [Fact]
    public void AReplay_RunsTheChapterAgain_WithTheEvidenceOff_AndKeepsItsDoneKeys()
    {
        var (server, transport) = SideWorld("replay");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny, amount: 3);
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipChapter, ChapterId = "a" });
        Assert.True(Has(p, StoryPackKeys.ChapterSkipped(R, "a")));

        transport.Sent.Clear();
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.RestartChapter, ChapterId = "a" });
        Assert.True(Has(p, StoryPackKeys.ChapterReplay(R, "a")));
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.1"))); // conditions keep holding during the replay
        Assert.True(server.EvaluateStoryConditionForTest("step:runtime_test:a.1", p));
        Assert.Equal(0, Progress(p, R, "a.1"));
        Assert.Contains("story.runtime_test.a1.start", transport.LineKeys(p));

        Event(server, p, GameEventKeys.MineAny, amount: 3);
        Assert.True(Has(p, StoryPackKeys.StepReplay(R, "a.1")));
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipChapter, ChapterId = "a" });
        Assert.False(Has(p, StoryPackKeys.ChapterReplay(R, "a")));
        Assert.DoesNotContain(p.State.Milestones, m => m.StartsWith("story:runtime_test:replay:", StringComparison.Ordinal));
    }

    [Fact]
    public void OptingOut_StopsThePack_KeepsItsKeys_AndOptingIn_Resumes()
    {
        var (server, _) = SideWorld("optout");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny);

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.OptOut });
        Assert.True(Has(p, StoryPackKeys.OptOut(R)));
        Assert.Equal(string.Empty, server.ObjectiveKeyForTest("Pilot"));
        Event(server, p, GameEventKeys.MineAny, amount: 5);
        Assert.Equal(1, Progress(p, R, "a.1")); // switched off — nothing counts, nothing is lost
        Assert.False(server.EvaluateStoryConditionForTest("pack:runtime_test:enabled", p));

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.OptIn });
        Assert.Equal("story.runtime_test.a1.chip", server.ObjectiveKeyForTest("Pilot"));
        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.1")));
    }

    [Fact]
    public void AnOptionalChapter_IsOfferedOnce_OnlyAfterTheMandatoryChapters_AndRunsOnRequest()
    {
        var (server, transport) = SideWorld("offer");
        var p = SidePlayer(server);
        p.State.UnlockedBlueprints.Add("terrain_scanner");
        server.TickStoryPacksForTest(p, 2);
        Assert.False(Has(p, StoryPackKeys.ChapterOffered(R, "c"))); // no offers while the mandatory chapters run

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipPack });
        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 1);
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(R, "c")));
        var offer = Assert.Single(transport.To<ShipAiLine>(p), l => l.LineKey == "vega.chapter.offer");
        Assert.Equal(R, offer.PackId);
        Assert.False(string.IsNullOrEmpty(offer.LineArg)); // the chapter title, in the player's language
        server.TickStoryPacksForTest(p, 200);
        Assert.Single(transport.To<ShipAiLine>(p), l => l.LineKey == "vega.chapter.offer"); // once

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.DeclineChapter, ChapterId = "c" });
        Assert.True(Has(p, StoryPackKeys.ChapterDeclined(R, "c")));

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.StartChapter, ChapterId = "c" });
        Assert.True(Has(p, StoryPackKeys.ChapterActive(R, "c")));
        Assert.False(Has(p, StoryPackKeys.ChapterDeclined(R, "c")));
        Assert.Equal(0, Progress(p, R, "c.1"));
        Assert.Equal("story.runtime_test.c1.chip", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(ObjectiveSources.Mission, server.ObjectiveSourceForTest("Pilot"));

        Event(server, p, GameEventKeys.MissionTurnedIn, "supply_cables");
        Assert.Equal(0, Progress(p, R, "c.1"));
        Event(server, p, GameEventKeys.MissionTurnedIn, "first_iron");
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "c.1")));
        Event(server, p, GameEventKeys.ScanPerformed, "flora");
        Assert.True(Has(p, StoryPackKeys.ChapterDone(R, "c")));
        Assert.False(Has(p, StoryPackKeys.ChapterActive(R, "c")));
    }

    [Fact]
    public void AMissionBridge_CompletesAtActivation_WhenTheMissionIsAlreadyTurnedIn()
    {
        var (server, _) = SideWorld("bridge");
        var p = SidePlayer(server);
        p.State.Missions.Add(new MissionProgress { MissionId = "first_iron", Status = MissionStatus.TurnedIn });
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.StartChapter, ChapterId = "c" });
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "c.1")));
        Assert.Equal(0, Progress(p, R, "c.2"));
    }

    // ---------------- Help escalation ----------------

    [Fact]
    public void HelpEscalates_OnPlayTime_HintThenAMarkerForThisSessionOnly_ThenTheSkipOffer()
    {
        var (server, transport) = SideWorld("escalate");
        var p = SidePlayer(server, 1, "Pilot");
        var other = SidePlayer(server, 2, "Other");
        var pos = p.State.Position;
        var stone = new Vector3i((int)Math.Floor(pos.X) + 2, (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z));
        server.World.SetBlock(stone, _lastContent!.GetBlock("stone")!.NumericId);
        int revealed = server.Metadata.RevealedPois.Count;

        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 4);
        Assert.DoesNotContain("story.runtime_test.a1.hint", transport.LineKeys(p));
        server.TickStoryPacksForTest(p, 1); // hintAfter 5
        Assert.Contains("story.runtime_test.a1.hint", transport.LineKeys(p));

        server.TickStoryPacksForTest(p, 5); // markerAfter 10
        Assert.Contains(SvGameServer.StepMarkedLine, transport.LineKeys(p));
        Assert.Contains(server.VisibleMarkersForTest("Pilot"), m => m.Ping);
        Assert.DoesNotContain(server.VisibleMarkersForTest("Other"), m => m.Ping);
        Assert.Empty(transport.To<MarkerList>(other)); // nobody else's view was re-sent
        Assert.Equal(revealed, server.Metadata.RevealedPois.Count); // never RevealPoi

        server.TickStoryPacksForTest(p, 5); // offerSkipAfter 15
        Assert.Contains(SvGameServer.StepOfferSkipLine, transport.LineKeys(p));
        server.TickStoryPacksForTest(p, 60);
        Assert.Single(transport.To<ShipAiLine>(p), l => l.LineKey == SvGameServer.StepOfferSkipLine); // once per session
        Assert.Empty(transport.To<ShipAiLine>(other).Where(l => l.LineKey == "story.runtime_test.a1.hint"));
    }

    /// <summary>#2490: a marker probe that finds nothing is not spent — it used to be marked sent before the provider ran. It
    /// tries again after <see cref="SvGameServer.MarkerRetrySeconds"/> on the same spot (never more often), and pings once the
    /// ore is in reach.</summary>
    [Fact]
    public void AMarkerThatFindsNothing_IsNotSpent_AndPingsOnceTheOreIsInReach()
    {
        var (server, transport) = SideWorld("escalate_retry");
        var p = SidePlayer(server, 1, "Pilot");
        var pos = p.State.Position;
        p.State.Position = new Vector3f(pos.X, pos.Y + 60, pos.Z); // high in the air: no stone within the probe's reach

        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 10); // markerAfter 10: the probe finds nothing — no line, no ping
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, transport.LineKeys(p));
        Assert.DoesNotContain(server.VisibleMarkersForTest("Pilot"), m => m.Ping);

        var at = p.State.Position.ToBlock();
        server.World.SetBlock(new Vector3i(at.X + 3, at.Y, at.Z), _lastContent!.GetBlock("stone")!.NumericId);
        server.TickStoryPacksForTest(p, (int)SvGameServer.MarkerRetrySeconds - 2); // the same spot: not yet probed again
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, transport.LineKeys(p));

        server.TickStoryPacksForTest(p, 2); // the retry: the stone is in reach now
        Assert.Single(transport.LineKeys(p), k => k == SvGameServer.StepMarkedLine);
        Assert.Contains(server.VisibleMarkersForTest("Pilot"), m => m.Ping);

        server.TickStoryPacksForTest(p, 90);
        Assert.Single(transport.LineKeys(p), k => k == SvGameServer.StepMarkedLine); // spent once it went up
    }

    /// <summary>A stone block two blocks from the player — in reach of the <c>nearest_ore:stone</c> marker probe.</summary>
    private void PlaceStoneNear(SvGameServer server, PlayerSession p)
    {
        var pos = p.State.Position;
        var stone = new Vector3i((int)Math.Floor(pos.X) + 2, (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z));
        server.World.SetBlock(stone, _lastContent!.GetBlock("stone")!.NumericId);
    }

    /// <summary>#2570 (Bloody Mary's report): VEGA's help marker is no 30-second ping any more. It stays while its step is open
    /// — play time and server uptime both long past 30 s — labelled with the step's objective, and goes with the step: done ⇒
    /// gone from the view, and the 1 Hz story pass tells the client. The line says where to look, without the "pulse".</summary>
    [Fact]
    public void TheHelpMarker_StaysWhileTheStepIsOpen_AndGoesWhenItIsDone()
    {
        var (server, transport) = SideWorld("story_marker_done");
        var p = SidePlayer(server, 1, "Pilot");
        PlaceStoneNear(server, p);

        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 10); // markerAfter 10
        Assert.Equal("vega.step.marked_spot", SvGameServer.StepMarkedLine);
        Assert.Single(transport.LineKeys(p), k => k == SvGameServer.StepMarkedLine);
        Assert.DoesNotContain("vega.step.marked", transport.LineKeys(p)); // the old "Follow the pulse!" line is gone
        var marker = Assert.Single(server.StoryMarkersForTest("Pilot"));
        Assert.Equal("story.runtime_test.a1.chip", marker.LabelKey); // the chip's objective names it on the map
        Assert.Equal(R, marker.PackId);
        Assert.True(marker.Ping); // an older client draws it as a ping
        Assert.Contains(transport.To<MarkerList>(p).Last().Markers, m => m.Story); // pushed at once

        server.AdvanceUptimeForTest(120); // a ping would have expired four times over
        server.TickMarkerPingsForTest();
        server.TickStoryPacksForTest(p, 60);
        server.SyncStoryMarkersForTest(p);
        Assert.Single(server.StoryMarkersForTest("Pilot"));
        Assert.Contains(transport.To<MarkerList>(p).Last().Markers, m => m.Story);
        Assert.Single(transport.LineKeys(p), k => k == SvGameServer.StepMarkedLine); // said once

        Event(server, p, GameEventKeys.MineAny, "stone", amount: 3); // a.1 done
        Assert.True(Has(p, StoryPackKeys.StepDone(R, "a.1")));
        Assert.Empty(server.StoryMarkersForTest("Pilot"));
        transport.Sent.Clear();
        server.Tick(1.0); // the 1 Hz story pass notices and re-sends the view
        Assert.DoesNotContain(transport.To<MarkerList>(p).Last().Markers, m => m.Story);
    }

    /// <summary>#2570: skipping the step from the Journal takes its marker along at once (the answer to the click).</summary>
    [Fact]
    public void SkippingTheStep_TakesItsHelpMarkerAlong_AtOnce()
    {
        var (server, transport) = SideWorld("story_marker_skip");
        var p = SidePlayer(server, 1, "Pilot");
        PlaceStoneNear(server, p);
        server.TickStoryPacksForTest(p, 10);
        Assert.Single(server.StoryMarkersForTest("Pilot"));

        transport.Sent.Clear();
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.SkipStep, StepId = "a.1" });
        Assert.True(Has(p, StoryPackKeys.StepSkipped(R, "a.1")));
        Assert.Empty(server.StoryMarkersForTest("Pilot"));
        Assert.DoesNotContain(transport.To<MarkerList>(p).Last().Markers, m => m.Story);
    }

    /// <summary>#2570: the marker belongs to the body it was raised on — off it while the player is elsewhere, back when they
    /// return and the step is still open.</summary>
    [Fact]
    public void TheHelpMarker_StaysOnItsBody_AndIsBackWhenThePlayerReturns()
    {
        var (server, transport) = SideWorld("story_marker_travel", c => c.Rules.FreeSpaceFlight = true);
        var p = SidePlayer(server, 1, "Pilot");
        PlaceStoneNear(server, p);
        string home = p.CurrentLocationId;
        server.TickStoryPacksForTest(p, 10);
        Assert.Single(server.StoryMarkersForTest("Pilot"));

        string away = server.Galaxy.AllBodies().First(b =>
            b.Kind is BlocksBeyondTheStars.Shared.World.CelestialKind.Planet or BlocksBeyondTheStars.Shared.World.CelestialKind.Moon
            && b.SystemId == server.Galaxy.FindBody(home)!.SystemId
            && !string.IsNullOrEmpty(b.PlanetType) && _lastContent!.GetPlanet(b.PlanetType!) is not null
            && b.Id != home).Id;
        server.Travel("Pilot", away);
        Assert.Equal(away, p.CurrentLocationId);
        Assert.Empty(server.StoryMarkersForTest("Pilot"));
        Assert.DoesNotContain(transport.To<MarkerList>(p).Last().Markers, m => m.Story);

        server.Travel("Pilot", home);
        Assert.Equal(home, p.CurrentLocationId);
        Assert.Single(server.StoryMarkersForTest("Pilot")); // the step is still open
        Assert.Contains(transport.To<MarkerList>(p).Last().Markers, m => m.Story);
    }

    /// <summary>#2570: during the basic training only the step on the objective chip gets a help marker. An open CORE step
    /// becomes an open extra at its hint time; when its marker time is the same moment (both 5 s here), the chip has moved on
    /// already — so no marker and no "I've marked your goal" for a step the chip no longer shows, then or later.</summary>
    [Fact]
    public void ADeferredExtra_GetsNoHelpMarker_DuringTheBasicTraining()
    {
        var tutorial = StoryPackFixtures.Load(T);
        var extra = tutorial.Chapters[0].Steps.Single(st => st.Id == "t1.2");
        extra.Tier = StoryStepTiers.Core;
        extra.OnStart.Reveal = "nearest_ore:stone";
        extra.Escalate = new StoryEscalation { HintAfter = 5, MarkerAfter = 5 };
        var content = StoryPackFixtures.ContentWith();
        content.SetStories(content.Stories.Values.Concat(new[] { tutorial }).ToList());
        _lastContent = content;
        var transport = new RecordingTransport();
        var server = Boot("story_marker_deferred", transport, content, c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Kid");
        PlaceStoneNear(server, p);
        Event(server, p, GameEventKeys.MineAny, amount: 2); // t1.1 done: the CORE step t1.2 owns the chip
        Assert.Equal("t1.2", server.ChipStepForTest(p, T));

        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 5); // hint time = marker time: t1.2 is deferred, the chip moves on to t1.3
        Assert.Equal("t1.3", server.ChipStepForTest(p, T));
        Assert.Empty(server.StoryMarkersForTest("Kid"));
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, transport.LineKeys(p));

        server.TickStoryPacksForTest(p, 60);
        Assert.Empty(server.StoryMarkersForTest("Kid"));
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, transport.LineKeys(p));
    }

    /// <summary>#2742 (the berries step b2.3): a CORE step whose marker time comes before its hint time gets its help marker
    /// while it still owns the chip — and keeps it as an open extra once the hint moved the chip on.</summary>
    [Fact]
    public void ACoreStep_WhoseMarkerComesBeforeItsHint_GetsItsMarker_WhileItOwnsTheChip()
    {
        var tutorial = StoryPackFixtures.Load(T);
        var core = tutorial.Chapters[0].Steps.Single(st => st.Id == "t1.2");
        core.Tier = StoryStepTiers.Core;
        core.OnStart.Reveal = "nearest_ore:stone";
        core.Escalate = new StoryEscalation { HintAfter = 10, MarkerAfter = 5 };
        var content = StoryPackFixtures.ContentWith();
        content.SetStories(content.Stories.Values.Concat(new[] { tutorial }).ToList());
        _lastContent = content;
        var transport = new RecordingTransport();
        var server = Boot("story_marker_core_first", transport, content, c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Kid");
        PlaceStoneNear(server, p);
        Event(server, p, GameEventKeys.MineAny, amount: 2); // t1.1 done: the CORE step t1.2 owns the chip
        Assert.Equal("t1.2", server.ChipStepForTest(p, T));

        transport.Sent.Clear();
        server.TickStoryPacksForTest(p, 5); // marker time, the hint still ahead: the step owns the chip
        Assert.Equal("t1.2", server.ChipStepForTest(p, T));
        Assert.Single(server.StoryMarkersForTest("Kid"));
        Assert.Single(transport.LineKeys(p), k => k == SvGameServer.StepMarkedLine);

        server.TickStoryPacksForTest(p, 5); // hint time: deferred, the chip moves on — the marker stays with the open step
        Assert.Equal("t1.3", server.ChipStepForTest(p, T));
        Assert.Single(server.StoryMarkersForTest("Kid"));
    }

    /// <summary>#2570: while the basic training holds the chip, another story's step gets no help marker either (VEGA would point
    /// at something the chip does not show) — it comes once the training is through.</summary>
    [Fact]
    public void AnotherStorysStep_GetsItsHelpMarker_OnlyOnceTheBasicTrainingIsThrough()
    {
        var (server, transport) = TutorialWorld("story_marker_side", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = Join(server, 1, "Pilot");
        PlaceStoneNear(server, p);
        Assert.Equal(T, server.ObjectivePackForTest("Pilot")); // the training owns the chip

        server.TickStoryPacksForTest(p, 12); // the side story's a.1 is past its marker time (10)
        Assert.Empty(server.StoryMarkersForTest("Pilot"));
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, transport.LineKeys(p));

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        server.TickStoryPacksForTest(p);
        Assert.Contains(SvGameServer.StepMarkedLine, transport.LineKeys(p));
        Assert.Equal(R, Assert.Single(server.StoryMarkersForTest("Pilot")).PackId);
    }

    [Fact]
    public void TheHelpClock_StandsStill_WhileTheWorldIsHeld()
    {
        var (server, transport) = SideWorld("held_clock");
        var p = server.AddLocalPlayer("Solo");
        FinishLegacyChain(p);
        server.TickStoryPacksForTest(p, 1);
        double before = p.StoryPackPlaySeconds;

        server.PauseForTest(p, true);
        for (int i = 0; i < 20; i++)
        {
            server.Tick(1.0);
        }

        Assert.Equal(before, p.StoryPackPlaySeconds);
        Assert.DoesNotContain("story.runtime_test.a1.hint", transport.LineKeys(p));
    }

    [Fact]
    public void Progress_RestartsTheHelpClock()
    {
        var (server, transport) = SideWorld("clock");
        var p = SidePlayer(server);
        server.TickStoryPacksForTest(p, 4);
        Event(server, p, GameEventKeys.MineAny);
        server.TickStoryPacksForTest(p, 4);
        Assert.DoesNotContain("story.runtime_test.a1.hint", transport.LineKeys(p));
        server.TickStoryPacksForTest(p, 1);
        Assert.Contains("story.runtime_test.a1.hint", transport.LineKeys(p));
    }

    // ---------------- The event bus registration ----------------

    [Fact]
    public void TheRuntime_ListensOnlyForTheKeysOfActiveSteps()
    {
        var (server, _) = SideWorld("listen");
        var p = SidePlayer(server);
        Assert.Contains(GameEventKeys.MineAny, server.StepListenerKeysForTest);
        Assert.Contains(GameEventKeys.DefeatAny, server.StepListenerKeysForTest); // the armed react step
        Assert.DoesNotContain(GameEventKeys.CraftAny, server.StepListenerKeysForTest);

        Event(server, p, GameEventKeys.MineAny, amount: 3);
        Assert.DoesNotContain(GameEventKeys.MineAny, server.StepListenerKeysForTest);
        Assert.Contains(GameEventKeys.CraftAny, server.StepListenerKeysForTest);
        Assert.Contains(GameEventKeys.UiVegaContinue, server.StepListenerKeysForTest);
    }

    [Fact]
    public void AWorldWithoutPlayerPacks_RegistersNothing()
    {
        var transport = new RecordingTransport();
        var server = Boot("nopacks", transport, StoryPackFixtures.ContentWithoutPlayerPacks());
        var p = Join(server, 1, "Pilot");
        server.TickStoryPacksForTest(p, 3);
        Assert.Empty(server.StepListenerKeysForTest);
        Assert.Empty(p.State.StepProgress);
        Assert.False(server.HasGameEventListener(GameEventKeys.MineAny));
    }

    // ---------------- Rejoin ----------------

    [Fact]
    public void ARejoin_ReSpeaksTheStepOnTheChip()
    {
        var (server, transport) = SideWorld("rejoin");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny);
        transport.Sent.Clear();
        server.StoryPacksOnJoinForTest(p);
        Assert.Contains("story.runtime_test.a1.start", transport.LineKeys(p));
        Assert.Equal(1, Progress(p, R, "a.1")); // the count survives
    }

    // ---------------- The tutorial: policy, legacy keys, migration, gates ----------------

    private (SvGameServer Server, RecordingTransport Transport) TutorialWorld(string world, Action<ServerConfig>? configure = null)
    {
        var transport = new RecordingTransport();
        _lastContent = StoryPackFixtures.ContentWith(T, R);
        var server = Boot(world, transport, _lastContent, configure);
        return (server, transport);
    }

    [Fact]
    public void TutorialPolicy_Auto_StartsIt_OptIn_WaitsForTheRequest_Off_RefusesIt()
    {
        var (auto, _) = TutorialWorld("policy_auto");
        var a = auto.AddLocalPlayer("A");
        auto.TickStoryPacksForTest(a);
        Assert.True(Has(a, StoryPackKeys.Started(T)));
        Assert.True(auto.EvaluateStoryConditionForTest("pack:tutorial_test:enabled", a));

        var (optIn, _) = TutorialWorld("policy_optin", c => c.Rules.TutorialPolicy = TutorialPolicy.OptIn);
        var b = optIn.AddLocalPlayer("B");
        optIn.TickStoryPacksForTest(b);
        Assert.False(Has(b, StoryPackKeys.Started(T)));
        Assert.False(optIn.EvaluateStoryConditionForTest("pack:tutorial_test:enabled", b));
        Assert.True(Has(b, StoryPackKeys.Started(R))); // a side pack is not governed by the tutorial rule
        Deliver(optIn, b, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartPack });
        Assert.True(Has(b, StoryPackKeys.Started(T)));
        Assert.Equal(0, Progress(b, T, "t1.1"));

        var (off, _) = TutorialWorld("policy_off", c => c.Rules.TutorialPolicy = TutorialPolicy.Off);
        var o = off.AddLocalPlayer("O");
        off.TickStoryPacksForTest(o);
        Deliver(off, o, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartPack });
        Assert.False(Has(o, StoryPackKeys.Started(T)));
        Assert.Empty(o.State.StepProgress.Keys.Where(k => k.StartsWith(T + ":", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("auto", TutorialPolicy.Auto)]
    [InlineData("OptIn", TutorialPolicy.OptIn)]
    [InlineData("opt-in", TutorialPolicy.OptIn)]
    [InlineData("opt_in", TutorialPolicy.OptIn)]
    [InlineData("OFF", TutorialPolicy.Off)]
    public void TheTutorialOption_ParsesItsWords(string word, TutorialPolicy expected)
    {
        var config = new ServerConfig();
        Assert.Contains("tutorial", config.ApplyCommandLine(new[] { "--tutorial", word }));
        Assert.Equal(expected, config.Rules.TutorialPolicy);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("on")]
    public void TheTutorialOption_RefusesOtherWords(string word)
    {
        Assert.False(ServerConfig.TryParseTutorialPolicy(word, out _));
    }

    [Fact]
    public void TheTutorialPolicy_IsBakedIntoTheSave_AndTheAdminChangesItLive()
    {
        var (server, transport) = TutorialWorld("policy_bake", c => c.Rules.TutorialPolicy = TutorialPolicy.OptIn);
        Assert.Equal(TutorialPolicy.OptIn, server.Metadata.RulesOverride!.TutorialPolicy);

        var admin = server.AddLocalPlayer("Admin");
        admin.State.Role = PlayerRole.Admin;
        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "policy off" });
        Assert.Equal(TutorialPolicy.Off, server.TutorialPolicyForTest);
        Assert.Equal(TutorialPolicy.Off, server.Metadata.RulesOverride!.TutorialPolicy);
        Assert.Equal("Off", transport.To<ServerRules>(admin).Last().TutorialPolicy);

        Deliver(server, admin, new SetWorldRulesIntent { TutorialPolicy = "Auto" });
        Assert.Equal(TutorialPolicy.Auto, server.TutorialPolicyForTest);
        Assert.True(Has(admin, StoryPackKeys.Started(T))); // Auto starts it for whoever is online
    }

    [Fact]
    public void AnOlderSave_WithoutTheRule_ReadsAuto()
    {
        var rules = JsonSerializer.Deserialize<GameRules>("{\"StoryId\":\"vega_protocol\"}")!;
        Assert.Equal(TutorialPolicy.Auto, rules.TutorialPolicy);
    }

    [Fact]
    public void TheLegacyKey_IsDoubleWritten_OnCompletionSkipAndGraduation()
    {
        var (server, _) = TutorialWorld("legacy_write");
        var p = server.AddLocalPlayer("Kid");
        server.TickStoryPacksForTest(p);
        Assert.True(Has(p, StoryPackKeys.Migrated(T)));
        Assert.True(Has(p, "vega:intro")); // the tutorial started — older builds read it as "the intro was shown"

        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "t1.1")));
        Assert.True(Has(p, "vega:stage:mine"));

        Event(server, p, GameEventKeys.BuildAny, "stone");
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipStep, StepId = "t1.3" });
        Assert.True(Has(p, StoryPackKeys.StepSkipped(T, "t1.3")));
        Assert.True(Has(p, "vega:stage:craft"));
        Assert.False(Has(p, "vega:stage:eat"));

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.All(LegacyStages, s => Assert.True(Has(p, "vega:stage:" + s)));
    }

    [Fact]
    public void TheMigration_TurnsLegacyKeysIntoStepKeys_Once()
    {
        var (server, _) = TutorialWorld("migrate");
        var p = server.AddLocalPlayer("Old");
        p.State.Milestones.Add("vega:stage:mine");
        p.State.Milestones.Add("vega:stage:craft");
        server.TickStoryPacksForTest(p);
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "t1.1")));
        Assert.True(Has(p, StoryPackKeys.StepDone(T, "t1.3")));
        Assert.False(Has(p, StoryPackKeys.StepDone(T, "t1.2")));
        Assert.Equal(0, Progress(p, T, "t1.2"));

        p.State.Milestones.Remove(StoryPackKeys.StepDone(T, "t1.1"));
        server.TickStoryPacksForTest(p);
        Assert.False(Has(p, StoryPackKeys.StepDone(T, "t1.1"))); // the marker holds: legacy keys are evidence once
    }

    [Fact]
    public void TheTutorialGates_FollowThePack_AndOpenWhenTheTutorialIsOff()
    {
        var (server, _) = TutorialWorld("gates");
        var p = server.AddLocalPlayer("Kid");
        server.TickStoryPacksForTest(p);
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips));
        Assert.False(server.TutorialGateForTest(p, StoryGateNames.Banter));
        Assert.Null(server.TutorialGateForTest(p, StoryGateNames.StationNear)); // unset: the caller keeps today's rule

        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Event(server, p, GameEventKeys.BuildAny, "stone");
        Event(server, p, GameEventKeys.CraftAny, "torch");
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.OpportunityTips));

        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.OptOut });
        Assert.True(server.TutorialGateForTest(p, StoryGateNames.Banter)); // "tutorial off" opens every gate

        var (plain, _) = SideWorld("gates_none");
        var q = plain.AddLocalPlayer("Q");
        Assert.Null(plain.TutorialGateForTest(q, StoryGateNames.Banter)); // no tutorial pack: no say
    }

    /// <summary>#2469: finishing the mandatory chapters by playing graduates — the pack's closing line, its achievement
    /// counter, its suggested deep dives offered — and skipping never does.</summary>
    [Fact]
    public void PlayingThroughTheMandatoryChapters_Graduates_AndOffersTheSuggestedDeepDives()
    {
        var (server, transport) = TutorialWorld("graduate");
        var p = server.AddLocalPlayer("Kid");
        server.TickStoryPacksForTest(p);
        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Event(server, p, GameEventKeys.BuildAny, "stone");
        Event(server, p, GameEventKeys.CraftAny, "torch");
        Event(server, p, GameEventKeys.EatAny, "berries");

        Assert.True(Has(p, StoryPackKeys.Done(T)));
        Assert.Single(transport.LineKeys(p), k => k == "story.tutorial_test.graduated");
        Assert.Equal(1, p.State.AchievementCounters["tutorial_test:graduated"]);
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, "t3"))); // the suggestion — the Journal lists it

        var (skipServer, skipTransport) = TutorialWorld("graduate_skip");
        var q = skipServer.AddLocalPlayer("Pro");
        skipServer.TickStoryPacksForTest(q);
        Deliver(skipServer, q, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.DoesNotContain("story.tutorial_test.graduated", skipTransport.LineKeys(q));
        Assert.False(q.State.AchievementCounters.ContainsKey("tutorial_test:graduated"));
    }

    [Fact]
    public void TheDeepDive_IsOffered_AfterTheBasics_AndTheTutorialOnlyRunsWhatTheClientUnderstands()
    {
        var (server, transport) = TutorialWorld("deepdive");
        var p = server.AddLocalPlayer("Kid");
        server.TickStoryPacksForTest(p);
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        server.TickStoryPacksForTest(p);
        Assert.True(Has(p, StoryPackKeys.ChapterOffered(T, "t3")));
        Assert.Contains(transport.To<ShipAiLine>(p), l => l.LineKey == "vega.chapter.offer" && l.PackId == T);
    }

    // ---------------- The chip order ----------------

    [Fact]
    public void TheChip_FollowsTheBindingOrder()
    {
        var (server, _) = TutorialWorld("chip_order");
        var p = Join(server, 1, "Pilot");

        // 3: the tutorial pack's basic training owns the chip from the join (#2469: the old onboarding chain is gone).
        Assert.Equal("story.tutorial_test.t11.chip", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(ObjectiveSources.Tutorial, server.ObjectiveSourceForTest("Pilot"));
        Assert.Equal(T, server.ObjectivePackForTest("Pilot"));

        // 2: a pinned pack beats basic training.
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.Track });
        Assert.Equal(R, p.State.TrackedStoryId);
        Assert.Equal("story.runtime_test.a1.chip", server.ObjectiveKeyForTest("Pilot"));
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.Untrack });
        Assert.Equal(string.Empty, p.State.TrackedStoryId);

        // 5: with the basics through, the VEGA lead beats the other packs (7).
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.StartsWith(StoryLeads.ObjPrefix, server.ObjectiveKeyForTest("Pilot")); // #2475: the chapter lead
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest("Pilot"));
        Assert.Equal(StoryRegistry.DefaultStoryId, server.ObjectivePackForTest("Pilot")); // #2471: the chip reads "VEGA: …"

        // 6: a started deep dive comes after the VEGA lead; pinning the world story keeps the lead.
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.StartChapter, ChapterId = "t3" });
        Assert.StartsWith(StoryLeads.ObjPrefix, server.ObjectiveKeyForTest("Pilot"));
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.Track });
        Assert.Equal("story.tutorial_test.t31.chip", server.ObjectiveKeyForTest("Pilot"));
        Deliver(server, p, new StoryPackIntent { PackId = StoryRegistry.DefaultStoryId, Action = StoryPackActions.Track });
        Assert.StartsWith(StoryLeads.ObjPrefix, server.ObjectiveKeyForTest("Pilot"));
    }

    [Fact]
    public void TheFinale_BeatsAPin_InTheGuardianSystem_AndAPinBeatsItElsewhere()
    {
        var (server, _) = TutorialWorld("chip_finale");
        var veteran = server.AddLocalPlayer("Veteran");
        FinishLegacyChain(veteran);
        for (int i = 0; i < 400 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        Assert.True(server.IsGuardianSystemRevealedForTest);
        var p = Join(server, 1, "Pilot");
        FinishLegacyChain(p);
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.Equal(StoryLeads.FinaleJumpKey, server.ObjectiveKeyForTest("Pilot")); // 4 beats the other packs (#2475: no jump generator yet)

        Deliver(server, p, new StoryPackIntent { PackId = R, Action = StoryPackActions.Track });
        Assert.Equal("story.runtime_test.a1.chip", server.ObjectiveKeyForTest("Pilot")); // 2 beats 4

        server.AdminGotoCore(p);
        Assert.Equal(StoryLeads.FinaleKey, server.ObjectiveKeyForTest("Pilot")); // 1 beats a pin (#1832)
        Assert.Equal(ObjectiveSources.Finale, server.ObjectiveSourceForTest("Pilot"));
    }

    [Fact]
    public void TutorialActive_StaysTrue_WhileATutorialPackRuns()
    {
        var (server, transport) = TutorialWorld("tutorial_active");
        var p = Join(server, 1, "Pilot");
        FinishLegacyChain(p);
        server.TickStoryPacksForTest(p);
        Assert.True(transport.To<ShipAiLine>(p).Last().TutorialActive);
        Deliver(server, p, new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipPack });
        Assert.False(transport.To<ShipAiLine>(p).Last().TutorialActive);
    }

    // ---------------- Clients without packs1 ----------------

    [Fact]
    public void AClientWithoutPacks1_GetsTheOldKeysWithoutText_AndNoStepWithoutALegacyStage()
    {
        var (server, transport) = TutorialWorld("legacy_client", c => c.Rules.StoryId = StoryRegistry.NoneStoryId);
        var p = server.AddLocalPlayer("Old");
        p.ClientFeatures.Clear(); // an outdated client
        server.TickStoryPacksForTest(p);
        FinishLegacyChain(p); // after the migration ran: the pack's own steps decide

        var lines = transport.To<ShipAiLine>(p);
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1" && l.Text.Length == 0);
        var start = Assert.Single(lines, l => l.LineKey == "vega.s.mine.start");
        Assert.Equal(string.Empty, start.Text);
        Assert.DoesNotContain(lines, l => l.LineKey.StartsWith("story.tutorial_test.", StringComparison.Ordinal));
        Assert.Equal("vega.obj.mine", server.ObjectiveKeyForTest("Old"));

        transport.Sent.Clear();
        Event(server, p, GameEventKeys.MineAny, amount: 2);
        Assert.Contains("vega.s.mine.done", transport.LineKeys(p));
        Assert.Contains("vega.s.craft.start", transport.LineKeys(p)); // t1.2 has no legacy stage: it does not run here
        Assert.Equal(-1, Progress(p, T, "t1.2"));
        Assert.Equal("vega.obj.craft", server.ObjectiveKeyForTest("Old"));

        Event(server, p, GameEventKeys.CraftAny, "torch");
        Assert.Contains("vega.s.eat.start", transport.LineKeys(p)); // the old chain runs across the new chapters
        Event(server, p, GameEventKeys.EatAny, "berry");
        Assert.False(Has(p, StoryPackKeys.Done(T))); // a step only a current client can do is still open
        Assert.Equal(string.Empty, server.ObjectiveKeyForTest("Old")); // no pack chip it could not read
        Assert.False(Has(p, StoryPackKeys.ChapterOffered(T, "t3"))); // and no deep dive

        // A side pack's line reaches it as text composed on the server, with an empty key.
        var side = lines.Where(l => l.PackId == R).ToList();
        Assert.NotEmpty(side);
        Assert.All(side, l => Assert.Equal(string.Empty, l.LineKey));
        Assert.All(side, l => Assert.False(string.IsNullOrEmpty(l.Text)));
        Assert.All(side, l => Assert.DoesNotContain("{", l.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void TheJoin_ReadsTheClientsFeatures()
    {
        var (server, _) = SideWorld("features");
        var current = Join(server, 1, "New", packs: true);
        var old = Join(server, 2, "Old", packs: false);
        Assert.True(current.SupportsPacks);
        Assert.False(old.SupportsPacks);
        Assert.True(server.AddLocalPlayer("Local").SupportsPacks);
    }

    [Fact]
    public void ServerComposedText_UsesNeutralKeyWords_InThePlayersLanguage()
    {
        var (server, _) = SideWorld("compose");
        string en = server.ComposeLegacyTextForTest("en", "srv.station.cockpit");
        string de = server.ComposeLegacyTextForTest("de", "srv.station.cockpit");
        Assert.Contains("[Use]", en, StringComparison.Ordinal);
        Assert.Contains("[Benutzen]", de, StringComparison.Ordinal);
        Assert.DoesNotContain("{", en, StringComparison.Ordinal);
        Assert.Equal("New short course: Basics. Want to try it? Start it in your Journal whenever you like.",
            server.ComposeLegacyTextForTest("en", SvGameServer.ChapterOfferLine, "Basics"));
    }

    // ---------------- Persistence ----------------

    [Fact]
    public void RunningCounters_AndThePin_SurviveASave()
    {
        var state = new PlayerState { PlayerId = "p", Name = "P" };
        state.StepProgress["runtime_test:a.1"] = 2;
        state.TrackedStoryId = "runtime_test";
        string json = JsonSerializer.Serialize(StateMapper.ToSnapshot(state));
        var back = StateMapper.PlayerFromJson(json, "p", new JsonSerializerOptions());
        Assert.Equal(2, back.StepProgress["runtime_test:a.1"]);
        Assert.Equal("runtime_test", back.TrackedStoryId);

        var empty = StateMapper.ToSnapshot(new PlayerState { PlayerId = "q", Name = "Q" });
        Assert.Null(empty.StepProgress); // a save without running steps does not grow
    }

    [Fact]
    public void AnOlderBuild_DroppingTheCounters_CostsOnlyTheRunningCount()
    {
        var (server, _) = SideWorld("old_build");
        var p = SidePlayer(server);
        Event(server, p, GameEventKeys.MineAny, amount: 2);
        p.State.TrackedStoryId = R;

        // What an older build writes back: the same blob without the two new fields.
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(StateMapper.ToSnapshot(p.State)))!.AsObject();
        Assert.True(node.Remove("StepProgress"));
        Assert.True(node.Remove("TrackedStoryId"));
        var reloaded = StateMapper.PlayerFromJson(node.ToJsonString(), p.State.PlayerId, new JsonSerializerOptions());
        Assert.Empty(reloaded.StepProgress);
        Assert.Equal(string.Empty, reloaded.TrackedStoryId);
        Assert.Contains(StoryPackKeys.Started(R), reloaded.Milestones);

        var (server2, transport2) = SideWorld("old_build_back");
        var again = server2.AddLocalPlayer("Again");
        again.State.Milestones.UnionWith(reloaded.Milestones);
        server2.TickStoryPacksForTest(again);
        Assert.Equal(0, Progress(again, R, "a.1")); // the step simply runs again from zero
        Assert.Contains("story.runtime_test.a1.start", transport2.LineKeys(again));
    }

    [Fact]
    public void TheRepository_RoundTripsTheNewFields()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "repo"));
        _repos.Add(repo);
        repo.Initialize();
        var state = new PlayerState { PlayerId = "Pilot", Name = "Pilot" };
        state.StepProgress["tutorial_test:t1.1"] = 1;
        state.TrackedStoryId = "tutorial_test";
        repo.SavePlayer(state);
        var loaded = repo.LoadPlayer("Pilot")!;
        Assert.Equal(1, loaded.StepProgress["tutorial_test:t1.1"]);
        Assert.Equal("tutorial_test", loaded.TrackedStoryId);
    }

    // ---------------- Admin ----------------

    [Fact]
    public void TheAdmin_SkipsRestartsAndStartsTutorialChapters_ForAPlayer()
    {
        var (server, transport) = TutorialWorld("admin");
        var admin = server.AddLocalPlayer("Admin");
        admin.State.Role = PlayerRole.Admin;
        var kid = server.AddLocalPlayer("Kid");
        server.TickStoryPacksForTest(kid);

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "skip", TargetPlayer = "Kid" });
        Assert.True(Has(kid, StoryPackKeys.Done(T)));
        Assert.Contains(transport.To<ServerMessage>(admin), m => m.Text == "@srv.admin.tutorial_skipped:Kid");

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "restart", TargetPlayer = "Kid" });
        Assert.True(Has(kid, StoryPackKeys.ChapterReplay(T, "t1")));
        Assert.True(Has(kid, StoryPackKeys.ChapterReplay(T, "t2")));
        Assert.Equal(0, Progress(kid, T, "t1.1")); // the replays run in order: only part 1 for now
        Assert.Equal(-1, Progress(kid, T, "t2.1"));

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "chapter t3", TargetPlayer = "Kid" });
        Assert.True(Has(kid, StoryPackKeys.ChapterActive(T, "t3")));

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "chapter nope", TargetPlayer = "Kid" });
        Assert.Contains(transport.To<ActionRejected>(admin), r => r.Reason == "@srv.admin.tutorial_no_chapter:nope");

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "skip", TargetPlayer = "Nobody" });
        Assert.Contains(transport.To<ActionRejected>(admin), r => r.Reason == "@srv.admin.tutorial_no_player:Nobody");
        Assert.False(Has(admin, StoryPackKeys.Done(T)));

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "dance" });
        Assert.Contains(transport.To<ActionRejected>(admin), r => r.Reason == "@srv.admin.tutorial_usage");
    }

    [Fact]
    public void TheAdminChapterJump_SkipsTheMandatoryChaptersBefore()
    {
        var (server, transport) = TutorialWorld("admin_jump");
        var admin = server.AddLocalPlayer("Admin");
        admin.State.Role = PlayerRole.Admin;
        var kid = server.AddLocalPlayer("Kid");
        server.TickStoryPacksForTest(kid);

        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "chapter t2", TargetPlayer = "Kid" });
        Assert.True(Has(kid, StoryPackKeys.ChapterSkipped(T, "t1")));
        Assert.Equal(0, Progress(kid, T, "t2.1"));
        Assert.Contains(transport.To<ServerMessage>(admin), m => m.Text == "@srv.admin.tutorial_chapter:Kid · t2");

        Event(server, kid, GameEventKeys.EatAny, "berry");
        Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = "chapter t1", TargetPlayer = "Kid" });
        Assert.True(Has(kid, StoryPackKeys.ChapterReplay(T, "t1"))); // a chapter that is through is replayed
    }

    [Fact]
    public void WithoutATutorialPack_TheAdminTutorialCommand_IsRefused()
    {
        // #2469: the tutorial IS the pack — the old onboarding chain the command used to fall back to is gone.
        var transport = new RecordingTransport();
        var server = Boot("admin_legacy", transport, StoryPackFixtures.ContentWithoutPlayerPacks());
        var admin = server.AddLocalPlayer("Admin");
        admin.State.Role = PlayerRole.Admin;
        foreach (string verb in new[] { "skip", "restart", "chapter b1" })
        {
            transport.Sent.Clear();
            Deliver(server, admin, new AdminCommandIntent { Command = "tutorial", StringArg = verb });
            Assert.Contains(transport.To<ActionRejected>(admin), r => r.Reason == "@srv.admin.tutorial_none");
        }

        Assert.DoesNotContain(admin.State.Milestones, m => m.StartsWith("vega:stage:", StringComparison.Ordinal));
        Assert.DoesNotContain(admin.State.Milestones, m => m.StartsWith("story:", StringComparison.Ordinal) && m.Contains(":ch:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTutorialCommand_IsNoCheat_AndNeedsTheRole()
    {
        var (server, transport) = TutorialWorld("admin_role");
        var player = server.AddLocalPlayer("Player");
        player.State.Role = PlayerRole.Player; // not the world's owner
        server.TickStoryPacksForTest(player);
        Deliver(server, player, new AdminCommandIntent { Command = "tutorial", StringArg = "skip" });
        Assert.False(Has(player, StoryPackKeys.Done(T)));

        player.State.Role = PlayerRole.Admin; // cheats stay off in this world
        Deliver(server, player, new AdminCommandIntent { Command = "tutorial", StringArg = "skip" });
        Assert.True(Has(player, StoryPackKeys.Done(T)));
    }

    [Fact]
    public void TheStoryReport_ListsTheWorldStoryAndThePlayerPacks()
    {
        var (server, transport) = TutorialWorld("report");
        var admin = server.AddLocalPlayer("Admin");
        admin.State.Role = PlayerRole.Admin;
        server.TickStoryPacksForTest(admin);
        Deliver(server, admin, new AdminCommandIntent { Command = "story_status" });
        var texts = transport.To<ServerMessage>(admin).Select(m => m.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("Story 'vega_protocol'", StringComparison.Ordinal));
        string packs = Assert.Single(texts, t => t.StartsWith("tutorial policy=", StringComparison.Ordinal));
        Assert.Contains("tutorial_test (tutorial, started, chapters t1 step t1.1 0/2)", packs, StringComparison.Ordinal);
        Assert.Contains("runtime_test (side, started", packs, StringComparison.Ordinal);
    }

    // ---------------- The pause whitelist (dispatch level) ----------------

    private (SvGameServer Server, PlayerSession Player) HeldSideWorld(string world)
    {
        var (server, _) = SideWorld(world);
        var p = server.AddLocalPlayer("Solo");
        server.TickStoryPacksForTest(p);
        server.PauseForTest(p, true);
        Assert.True(server.IsPaused);
        return (server, p);
    }

    [Theory]
    [InlineData(StoryPackActions.Track)]
    [InlineData(StoryPackActions.OptOut)]
    [InlineData(StoryPackActions.SkipPack)]
    public void WhileHeld_ThePlayersOwnPackSwitches_AreServed(string action)
    {
        var (server, p) = HeldSideWorld("held_" + action);
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = action });
        bool served = action switch
        {
            StoryPackActions.Track => p.State.TrackedStoryId == R,
            StoryPackActions.OptOut => Has(p, StoryPackKeys.OptOut(R)),
            _ => Has(p, StoryPackKeys.Done(R)),
        };
        Assert.True(served);
        Assert.True(server.IsPaused);
    }

    /// <summary>#2471 (issue: "every action is pause-whitelisted"): the Journal can be opened from the pause menu, so the
    /// progress-moving actions get through a held world too (they were dropped until step 12). Every action's dispatch test:
    /// <c>StoryJournalTests.WhileHeld_EveryJournalAction_IsServed</c>.</summary>
    [Theory]
    [InlineData(StoryPackActions.SkipChapter)]
    [InlineData(StoryPackActions.SkipStep)]
    public void WhileHeld_ProgressMovingPackActions_AreServedSinceTheJournal(string action)
    {
        var (server, p) = HeldSideWorld("held_serve_" + action);
        Deliver(server, p, new StoryPackIntent { PackId = R, Action = action, ChapterId = "a", StepId = "a.1" });
        Assert.True(Has(p, StoryPackKeys.ChapterSkipped(R, "a")) || Has(p, StoryPackKeys.StepSkipped(R, "a.1")));
        Assert.True(server.IsPaused);
    }

    // ---------------- Wire ----------------

    [Fact]
    public void TheNewFieldsAndTheIntent_RoundTrip_AndDefaultToLegacy()
    {
        Assert.Empty(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint }.Features);
        var join = Assert.IsType<JoinRequest>(NetCodec.Decode(NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            Features = new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 },
        })));
        Assert.Equal(new[] { "packs1", "cutscenes1" }, join.Features);

        var line = Assert.IsType<ShipAiLine>(NetCodec.Decode(NetCodec.Encode(new ShipAiLine { PackId = R, Speaker = "vega", ObjectivePackId = T })));
        Assert.Equal(R, line.PackId);
        Assert.Equal("vega", line.Speaker);
        Assert.Equal(T, line.ObjectivePackId);
        Assert.Equal(string.Empty, new ShipAiLine().ObjectivePackId);

        var intent = Assert.IsType<StoryPackIntent>(NetCodec.Decode(NetCodec.Encode(new StoryPackIntent { PackId = T, Action = StoryPackActions.SkipStep, ChapterId = "t1", StepId = "t1.2" })));
        Assert.Equal(StoryPackActions.SkipStep, intent.Action);
        Assert.Equal("t1.2", intent.StepId);

        var rules = Assert.IsType<SetWorldRulesIntent>(NetCodec.Decode(NetCodec.Encode(new SetWorldRulesIntent { TutorialPolicy = "OptIn" })));
        Assert.Equal("OptIn", rules.TutorialPolicy);
        Assert.Equal(string.Empty, new ServerRules().TutorialPolicy);
    }
}
