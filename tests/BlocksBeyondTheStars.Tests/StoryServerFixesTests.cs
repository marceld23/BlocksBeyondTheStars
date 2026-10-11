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
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2460 — step 1 of the story packs (#2459): the server-side story fixes players hit today. Newcomers on a
/// progressed multiplayer world are newcomers (B01), "restart tutorial" restarts (B02), the Creative check reads the
/// save (B04), the active story row survives pack switches and unknown packs (B05/B07), the Guardian system survives
/// a restart (B06), latecomers hear the Guardian reveal (B10), the mission-thread fragment opens the reader (B45),
/// only real jobs tick the trade lesson (B58), invisible observers leave no story trace (N1/N2), a station is its
/// owner's (N3), the spawn system pays no story points and the ending goes only to those who heard the arc out (N4),
/// the Mk3 flag is no memory (N13), peaceful worlds get a peaceful chip (N14), the Guardian-core hint is never muted,
/// and the onboarding chain never waits on a lesson the world cannot offer (B29).
/// </summary>
public sealed class StoryServerFixesTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public StoryServerFixesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_storyfix_" + Guid.NewGuid().ToString("N"));
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
    }

    private ServerConfig Config(string world, Action<ServerConfig>? configure = null)
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
        return config;
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary>A started server on a loopback link, with the single network client the link models.</summary>
    private SvGameServer Boot(string world, out LoopbackClientTransport client, Action<ServerConfig>? configure = null,
        SqliteWorldRepository? repo = null)
    {
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        client = new LoopbackClientTransport(link);
        var server = new SvGameServer(Config(world, configure), _content, st, repo ?? Repo(world));
        server.Start();
        return server;
    }

    /// <summary>A started server on a recording transport (local players only).</summary>
    private SvGameServer BootRecording(string world, RecordingTransport transport, Action<ServerConfig>? configure = null)
    {
        var server = new SvGameServer(Config(world, configure), _content, transport, Repo(world));
        server.Start();
        return server;
    }

    /// <summary>Joins over the wire (the real join path) and collects every VEGA line the client receives.</summary>
    private static List<ShipAiLine> JoinOverNetwork(SvGameServer server, LoopbackClientTransport client, string name)
    {
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        return lines;
    }

    private static void Send(SvGameServer server, LoopbackClientTransport client, object message)
    {
        client.Send(NetCodec.Encode(message), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    private static void CompleteTheArc(SvGameServer server)
    {
        for (int i = 0; i < 400 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        Assert.True(server.IsGuardianSystemRevealedForTest);
    }

    private static readonly string[] Stages = { "mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land" };

    // ---------------- B01: the newcomer decision is a snapshot at the top of the join ----------------

    /// <summary>A world whose shared arc is past B1 (score ≥ 6) and one just under it (4–5, where the newcomer's own
    /// join used to cross the threshold): the fresh second player gets the prologue and VEGA's lessons, not the
    /// one-line veteran skip — although the beat catch-up still pays them the beats' knowledge.</summary>
    [Theory]
    [InlineData(3)] // milestones × weight 2 = score 6: B1 "A familiar signature" is revealed
    [InlineData(2)] // score 4: the guest's join itself must not cross into B1 any more
    public void ANewcomerOnAProgressedWorld_GetsThePrologueAndTheTutorial(int milestones)
    {
        var server = Boot("newcomer" + milestones, out var client);
        server.AddLocalPlayer("Host");
        for (int i = 0; i < milestones; i++)
        {
            server.RecordStoryMilestoneForTest();
        }

        int beatsBefore = server.StorySnapshot.BeatsRevealed;
        Assert.Equal(milestones >= 3 ? 2 : 1, beatsBefore);

        var lines = JoinOverNetwork(server, client, "Guest");

        Assert.DoesNotContain(lines, l => l.LineKey == "vega.veteran");
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.1" && l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        Assert.Contains(lines, l => l.LineKey == "vega.s.mine.start");
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey);
        Assert.DoesNotContain("vega:stage:mine", server.MilestonesForTest("Guest"));

        // The shared arc is untouched by the arrival (N4) — and the catch-up still credits what the world heard.
        Assert.Equal(milestones, server.StorySnapshot.Milestones);
        Assert.Equal(beatsBefore, server.StorySnapshot.BeatsRevealed);
        var guest = server.Sessions[1];
        Assert.True(guest.Join.Newcomer);
        Assert.Equal(beatsBefore - 1, guest.State.KnowledgePoints); // B0 pays nothing, B1 pays one
    }

    // ---------------- B02: "restart tutorial" restarts ----------------

    [Fact]
    public void RestartingTheTutorial_WithProgress_RunsTheLessonsAgain_WithoutThePrologue()
    {
        var repo = Repo("restart");
        repo.Initialize();
        repo.SavePlayer(new PlayerState { PlayerId = "Vet", Name = "Vet", KnowledgePoints = 12 }); // a played save
        var server = Boot("restart", out var client, repo: repo);
        var lines = JoinOverNetwork(server, client, "Vet");
        Assert.Contains(lines, l => l.LineKey == "vega.veteran"); // the veteran skip itself still stands

        lines.Clear();
        Send(server, client, new SkipOnboardingIntent { Restart = true });

        var milestones = server.MilestonesForTest("Vet");
        Assert.DoesNotContain("vega:stage:mine", milestones);
        Assert.DoesNotContain("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.veteran");
        Assert.DoesNotContain(lines, l => l.Kind == 4); // a restart is for the lessons, not the cold open
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey);
    }

    // ---------------- B04: Creative reads the save, not the launch flag ----------------

    [Fact]
    public void ARelaunchedHeadStartWorld_StillTeachesANewcomer()
    {
        var first = Repo("headstart");
        var creator = new SvGameServer(Config("headstart", c => c.CreativeUnlockAllBlueprints = true), _content,
            new LoopbackServerTransport(new LoopbackLink()), first);
        creator.Start();
        creator.AddLocalPlayer("Creator");
        creator.Stop();
        first.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // Relaunched like every existing save: without the creation-only flag.
        var server = Boot("headstart", out var client);
        var lines = JoinOverNetwork(server, client, "Friend");

        Assert.NotEmpty(server.Sessions[1].State.UnlockedBlueprints); // the saved option still hands them out
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.veteran");
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        var milestones = server.MilestonesForTest("Friend");
        Assert.Contains("vega:stage:unlock", milestones);     // moot with every blueprint — granted silently
        Assert.DoesNotContain("vega:stage:mine", milestones);
    }

    // ---------------- B05/B07: the active story row ----------------

    [Fact]
    public void APackSwitch_SurvivesARestart_BothWays()
    {
        var repo = Repo("switch");
        SvGameServer Boot2() => new SvGameServer(Config("switch"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);

        var server = Boot2();
        server.Start();
        server.RecordStoryFragmentForTest("frag_vega_signature");
        server.SetActiveStoryForTest("none");
        server.Stop();

        server = Boot2();
        server.Start();
        Assert.Equal("none", server.StorySnapshot.StoryId); // used to come back as VEGA with its old progress
        server.SetActiveStoryForTest("vega_protocol");
        server.Stop();

        server = Boot2();
        server.Start();
        Assert.Equal("vega_protocol", server.StorySnapshot.StoryId);
        Assert.Equal(0, server.StorySnapshot.Fragments); // a switch resets — the fresh row is the active one
        Assert.Equal(2, repo.ListStoryStates().Count);
        Assert.Single(repo.ListStoryStates(), r => r.Active);
        server.Stop();
    }

    [Fact]
    public void AnUnknownPackRow_NeverOverwritesRealVegaProgress()
    {
        var repo = Repo("ghost");
        repo.Initialize();
        repo.SaveStoryState(new StoredStoryState { StoryId = "ghost_pack", FragmentsFound = 1, Active = true, UpdatedUtc = "2099-01-01T00:00:00.0000000Z" });
        repo.SaveStoryState(new StoredStoryState { StoryId = "vega_protocol", FragmentsFound = 4, FoundFragmentKeys = new() { "a", "b", "c", "d" } });

        for (int run = 0; run < 2; run++)
        {
            var server = new SvGameServer(Config("ghost"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            Assert.Equal("vega_protocol", server.StorySnapshot.StoryId);
            Assert.Equal(4, server.StorySnapshot.Fragments); // the real progress, every restart
            server.Stop();
        }

        Assert.Equal(1, repo.ListStoryStates().Single(r => r.StoryId == "ghost_pack").FragmentsFound); // left untouched
    }

    [Fact]
    public void TheActiveRow_IsChosenDeterministically()
    {
        static StoredStoryState Row(string id, bool active = false, string stamp = "") => new() { StoryId = id, Active = active, UpdatedUtc = stamp };
        static bool Known(string id) => id is "vega_protocol" or "none";
        StoredStoryState? Pick(string rules, params StoredStoryState[] rows)
            => SvGameServer.SelectActiveStoryRow(rows, rules, "vega_protocol", Known);

        Assert.Null(Pick(""));
        Assert.Equal("ghost", Pick("", Row("ghost"))!.StoryId); // nothing usable → the caller starts the default

        // The active marker beats a newer stamp; a stamp beats an older one; usable beats unknown.
        Assert.Equal("none", Pick("", Row("vega_protocol", stamp: "2026-10-12"), Row("none", active: true, stamp: "2026-10-01"))!.StoryId);
        Assert.Equal("vega_protocol", Pick("", Row("none", stamp: "2026-10-01"), Row("vega_protocol", stamp: "2026-10-12"))!.StoryId);
        Assert.Equal("vega_protocol", Pick("", Row("ghost", active: true, stamp: "2099"), Row("vega_protocol"))!.StoryId);

        // Older saves without either: the fresh-save choice, else "none" (only an admin ever writes it), else the default.
        Assert.Equal("none", Pick("", Row("vega_protocol"), Row("none"))!.StoryId);
        Assert.Equal("vega_protocol", Pick("vega_protocol", Row("none"), Row("vega_protocol"))!.StoryId);
        Assert.Equal("vega_protocol", Pick("", Row("vega_protocol"))!.StoryId);
    }

    // ---------------- B06: the Guardian system survives a restart ----------------

    [Fact]
    public void ARevealedButUnwonSave_KeepsItsGuardianSystemAcrossARestart()
    {
        var repo = Repo("finale_restart");
        var server = new SvGameServer(Config("finale_restart"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        CompleteTheArc(server);
        Assert.True(server.GalaxyHasGuardianSystemForTest);
        server.Stop();

        var relaunched = new SvGameServer(Config("finale_restart"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        relaunched.Start();
        Assert.True(relaunched.IsGuardianSystemRevealedForTest);
        Assert.False(relaunched.StorySnapshot.Defeated);
        Assert.True(relaunched.GalaxyHasGuardianSystemForTest); // was gone from the star map after every restart
        Assert.True(relaunched.GuardianCoreIsLandableForTest);
        relaunched.Stop();
    }

    // ---------------- B10: latecomers hear the Guardian reveal ----------------

    [Fact]
    public void ALatecomer_HearsTheGuardianReveal_Once()
    {
        var repo = Repo("latecomer");
        var server = new SvGameServer(Config("latecomer"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        server.AddLocalPlayer("Host");
        CompleteTheArc(server);
        Assert.True(server.HasStoryMilestoneForTest("Host", "story:vega_protocol:finale:revealed")); // heard it live
        server.Stop();

        int JoinAndCount()
        {
            var s = Boot("latecomer", out var client, repo: repo);
            var lines = JoinOverNetwork(s, client, "Late");
            s.Stop();
            return lines.Count(l => l.LineKey == "story.vega.guardian_revealed" && l.Kind == 2);
        }

        Assert.Equal(1, JoinAndCount()); // the only line that names the jump generator
        Assert.Equal(0, JoinAndCount()); // heard — never again
    }

    // ---------------- B44: knowledge level 4 is the win ----------------

    [Fact]
    public void KnowledgeLevelFour_WaitsForTheWin()
    {
        var server = BootRecording("knowledge", new RecordingTransport());
        server.AdminRevealFinale();
        Assert.Equal(3, server.WorldKnowledgeLevel()); // the arc is told, the machines still hunt
        server.MarkGuardianDefeatedForTest();
        Assert.Equal(4, server.WorldKnowledgeLevel());
    }

    // ---------------- B45: the mission-thread fragment opens the reader ----------------

    [Fact]
    public void AThreadedMission_OpensTheFragmentReader_ForANewFind()
    {
        var transport = new RecordingTransport();
        var server = BootRecording("thread", transport);
        var helper = server.AddLocalPlayer("Helper");

        transport.Sent.Clear();
        server.TryThreadMissionForTest("Helper", "mission.settlement.gather.title");
        Assert.Contains(transport.Sent, x => x.Conn == helper.ConnectionId
            && x.Msg is NetFragmentRevealed f && f.TextKey == "lore.frag.frag_settler_legend");

        transport.Sent.Clear();
        server.TryThreadMissionForTest("Helper", "mission.settlement.gather.title"); // already found — no re-read
        Assert.DoesNotContain(transport.Sent, x => x.Msg is NetFragmentRevealed);
    }

    // ---------------- B58: only real jobs tick the trade lesson ----------------

    [Fact]
    public void AcceptingAStarterJob_DoesNotTickTheTradeLesson()
    {
        var server = Boot("starterjob", out var client);
        JoinOverNetwork(server, client, "Rookie");
        Send(server, client, new AcceptMissionIntent { MissionId = "first_iron" });

        Assert.Contains(server.Sessions[1].State.Missions, m => m.MissionId == "first_iron");
        Assert.DoesNotContain("vega:stage:trade", server.MilestonesForTest("Rookie"));
    }

    [Fact]
    public void OnlyBoardJobsAndChainSteps_CountAsTrade()
    {
        Assert.True(SvGameServer.MissionAcceptCountsAsTrade(new MissionDefinition { Id = "settle_123_gather" }));
        Assert.True(SvGameServer.MissionAcceptCountsAsTrade(new MissionDefinition { Id = "station_9_haul" }));
        Assert.True(SvGameServer.MissionAcceptCountsAsTrade(new MissionDefinition { Id = "chain_needs_1", ChainId = "settlement_needs" }));
        Assert.False(SvGameServer.MissionAcceptCountsAsTrade(new MissionDefinition { Id = "first_iron" }));
        Assert.False(SvGameServer.MissionAcceptCountsAsTrade(new MissionDefinition { Id = "yara_errand" })); // a story character's job
    }

    // ---------------- N1/N2: an invisible observer leaves no story trace ----------------

    private static void Admin(SvGameServer server, PlayerSession session, string command, string arg)
        => server.HandleForTest(session, new AdminCommandIntent { Command = command, StringArg = arg });

    [Fact]
    public void AnObserver_HearsNoBeats_UntilObserverModeEnds()
    {
        var server = BootRecording("observer_beats", new RecordingTransport(), c => c.FleetAdminPlayers = new() { "Operator" });
        server.AddLocalPlayer("Justus");
        var op = server.AddLocalPlayer("Operator");
        Admin(server, op, "spectate", "on");

        for (int i = 0; i < 3; i++)
        {
            server.RecordStoryMilestoneForTest(); // score 6 → B1
        }

        Assert.True(server.HasStoryMilestoneForTest("Justus", "story:vega_protocol:beat:1"));
        Assert.False(server.HasStoryMilestoneForTest("Operator", "story:vega_protocol:beat:1"));

        Admin(server, op, "spectate", "off");
        Assert.True(server.HasStoryMilestoneForTest("Operator", "story:vega_protocol:beat:1")); // caught up
    }

    [Fact]
    public void AnObserversGoto_MapsNothingForTheStory_WhileAPlayersFirstNewSystemStillCounts()
    {
        var server = BootRecording("observer_goto", new RecordingTransport(), c => c.FleetAdminPlayers = new() { "Operator" });
        var op = server.AddLocalPlayer("Operator");
        Assert.Equal(0, server.StorySnapshot.Milestones); // N4: the spawn system is no milestone

        string home = server.Galaxy.FindBody(op.CurrentLocationId)!.SystemId;
        var targets = server.Galaxy.Systems
            .Where(s => s.Id != home)
            .Select(s => s.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.Planet && !string.IsNullOrEmpty(b.PlanetType)))
            .Where(b => b is not null)
            .Take(2)
            .ToList();
        Assert.Equal(2, targets.Count);

        // A visible fleet admin is a player: a new system mapped counts like anyone's.
        server.MarkArrivedOnBodyForTest(op, targets[0]!.Id);
        int milestones = server.StorySnapshot.Milestones;
        Assert.Equal(1, milestones);

        // Observing, a jump to another star leaves the shared world as it was.
        Admin(server, op, "spectate", "on");
        int systems = server.Galaxy.Systems.Count;
        int knowledge = op.State.KnowledgePoints;
        Admin(server, op, "goto", targets[1]!.Id + " 0 120 0");
        Assert.Equal(targets[1]!.Id, op.CurrentLocationId);
        Assert.Equal(milestones, server.StorySnapshot.Milestones);
        Assert.Equal(systems, server.Galaxy.Systems.Count);
        Assert.Equal(knowledge, op.State.KnowledgePoints);
        Assert.Contains(targets[1]!.SystemId, op.State.KnownSystems); // booked, so a later rejoin there counts nothing
    }

    [Fact]
    public void PlayerNear_NeverNamesAnObserver()
    {
        var server = BootRecording("observer_near", new RecordingTransport(), c => c.FleetAdminPlayers = new() { "Operator" });
        var justus = server.AddLocalPlayer("Justus");
        var op = server.AddLocalPlayer("Operator");
        justus.State.AboardShip = false;
        op.State.AboardShip = false;
        op.State.Position = new Vector3f(justus.State.Position.X + 5f, justus.State.Position.Y, justus.State.Position.Z);

        Assert.Contains("player_near", server.VegaTipCandidatesForTest("Justus").Candidates); // visible: a pilot nearby

        Admin(server, op, "spectate", "on");
        Assert.DoesNotContain("player_near", server.VegaTipCandidatesForTest("Justus").Candidates);
    }

    // ---------------- N3: a commissioned station is its owner's ----------------

    [Fact]
    public void AStationCommissionedByAnAlly_CreditsTheOwner()
    {
        var transport = new RecordingTransport();
        var server = BootRecording("station_owner", transport, c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.PlaceSettlements = false;
            c.PlaceWrecks = false;
        });
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        server.RequestAlliance("Alice", "Bob");
        server.RespondAlliance("Bob", "Alice", accept: true);
        server.EnterSpace("Alice");
        server.EnterSpace("Bob");
        alice.State.InEva = true;
        alice.State.InstantBuild = true;
        bob.State.InEva = true;
        bob.State.InstantBuild = true;

        server.DeployStationCoreForTest("Alice");
        string id = server.OwnedStationIdForTest("Alice")!;
        for (int i = 1; i <= 11; i++)
        {
            server.HandleStructureEditForTest("Alice",
                new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
        }

        transport.Sent.Clear();
        server.HandleStructureEditForTest("Bob", // the ally sets the airlock — the block that commissions it
            new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });
        Assert.True(server.StationIsBoardableForTest(id));

        Assert.Contains(transport.Sent, x => x.Conn == alice.ConnectionId
            && x.Msg is ServerMessage m && m.Text == "@srv.station.commissioned:Alice's Station");
        Assert.DoesNotContain(transport.Sent, x => x.Conn == bob.ConnectionId
            && x.Msg is ServerMessage m && m.Text.StartsWith("@srv.station.commissioned", StringComparison.Ordinal));
        Assert.True(alice.State.AchievementCounters.TryGetValue(AchievementCounters.StationCommissioned, out int a) && a == 1);
        Assert.False(bob.State.AchievementCounters.ContainsKey(AchievementCounters.StationCommissioned));
    }

    // ---------------- N4: the spawn system and the ending ----------------

    [Fact]
    public void EveryNewIdentity_JoinsWithoutMovingTheSharedArc()
    {
        var server = BootRecording("arcade", new RecordingTransport());
        for (int guest = 0; guest < 4; guest++)
        {
            server.AddLocalPlayer("Guest" + guest);
        }

        Assert.Equal(0, server.StorySnapshot.Milestones); // each used to add +2 — the third guest revealed B1
        Assert.Equal(1, server.StorySnapshot.BeatsRevealed);
    }

    /// <summary>Wins the finale with the named player on the core (the authored rebuttals: 0, 1, 2, 0).</summary>
    private static void WinTheFinale(SvGameServer server, PlayerSession pilot)
    {
        CompleteTheArc(server);
        pilot.CurrentLocationId = SvGameServer.GuardianCoreBodyId;
        for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
        {
            server.CoreHackTickForTest(pilot.State.PlayerId);
        }

        foreach (int choice in new[] { 0, 1, 2, 0 })
        {
            server.CoreDialogueChoiceForTest(pilot.State.PlayerId, choice);
        }

        Assert.True(server.StorySnapshot.Defeated);
    }

    [Fact]
    public void TheEndingAtTheDoor_GoesOnlyToThoseWhoHeardTheArcOut()
    {
        var repo = Repo("ending");
        var server = new SvGameServer(Config("ending"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var winner = server.AddLocalPlayer("Winner");
        server.AddLocalPlayer("Friend");
        CompleteTheArc(server);                       // Friend hears B12 …
        server.DisconnectLocalPlayerForTest("Friend"); // … and is offline for the win
        WinTheFinale(server, winner);
        server.Stop();

        (int Resolved, bool FinaleLine, bool Achievement) Join(string name)
        {
            var s = Boot("ending", out var client, repo: repo);
            int resolved = 0;
            client.PayloadReceived += p => { if (NetCodec.Decode(p) is StoryResolved) { resolved++; } };
            var lines = JoinOverNetwork(s, client, name);
            var state = s.Sessions[1].State;
            bool achieved = state.AchievementCounters.TryGetValue(AchievementCounters.StoryFinale, out int n) && n > 0;
            s.Stop();
            return (resolved, lines.Any(l => l.LineKey == "story.vega.finale_resolved"), achieved);
        }

        // Friend heard the arc out: the ending, the line and the achievement the others earned at the win (E23).
        Assert.Equal((1, true, true), Join("Friend"));
        Assert.Equal(0, Join("Friend").Resolved);    // once

        // A stranger arriving after the win is not played the ending at the door — ever.
        Assert.Equal((0, false, false), Join("Stranger"));
        Assert.Equal(0, Join("Stranger").Resolved);
    }

    // ---------------- N13: the Mk3 flag is no memory ----------------

    [Fact]
    public void TheMk3PartsFlag_IsNotCountedAsAMemory()
    {
        var server = Boot("mk3", out var client);
        var lines = JoinOverNetwork(server, client, "Historian");
        var p = server.Sessions[1].State;
        for (int beat = 1; beat <= 10; beat++)
        {
            p.Milestones.Add("vega:mem:" + beat);
        }

        p.Milestones.Add("vega:mem:mk3parts"); // the arc's end handed over the parts
        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 1));
        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();
        var milestones = server.MilestonesForTest("Historian");
        Assert.Contains("vega:mem:11", milestones);       // the eleventh memory, not "12"
        Assert.DoesNotContain("vega:mem:12", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.mem.more");
    }

    // ---------------- N14: a peaceful world's chip ----------------

    [Theory]
    [InlineData(true, "story.obj.help")]
    [InlineData(false, "story.obj.help_peaceful")]
    public void TheLateStoryChip_OnlyAsksToDriveBackMachines_WhereThereAreAny(bool machines, string expected)
    {
        var server = BootRecording("chip" + machines, new RecordingTransport(), c =>
        {
            if (!machines)
            {
                c.Rules.PlanetEnemies = AlienActivity.Off; // a peaceful/family world
            }
        });
        var p = server.AddLocalPlayer("Pilot");
        foreach (var stage in Stages)
        {
            p.State.Milestones.Add("vega:stage:" + stage);
        }

        foreach (var f in server.NetFragmentSnapshots.ToList())
        {
            server.PickUpNetFragmentForTest(f.Id); // nothing left to follow on this world
        }

        foreach (var frag in _content.Stories["vega_protocol"].Fragments)
        {
            server.RecordStoryFragmentForTest(frag.Key); // every fragment found
        }

        Assert.Equal(expected, server.ObjectiveKeyForTest("Pilot"));
    }

    // ---------------- guardian_core: never muted ----------------

    [Fact]
    public void TheGuardianCoreHint_RidesTheNeverMutedChannel()
    {
        var transport = new RecordingTransport();
        var server = BootRecording("core_hint", transport);
        var pilot = server.AddLocalPlayer("Pilot");

        transport.Sent.Clear();
        server.AdminGotoCore(pilot);
        Assert.Equal(SvGameServer.GuardianCoreBodyId, pilot.CurrentLocationId);

        var hint = Assert.Single(transport.Sent, x => x.Conn == pilot.ConnectionId
            && x.Msg is ShipAiLine l && l.LineKey == "vega.hint.guardian_core");
        Assert.Equal(3, ((ShipAiLine)hint.Msg).Kind); // system — the hints mute (Kind 1) never swallows it
    }

    // ---------------- B29: no lesson the world cannot offer ----------------

    [Fact]
    public void WithoutAStationInReach_TheDockLessonIsSkipped_NotAwaited()
    {
        var server = Boot("no_station", out var client, c =>
        {
            c.World.SpaceStations = Frequency.Off;
            c.Rules.FreeSpaceFlight = true;
        });
        var lines = JoinOverNetwork(server, client, "Pilot");
        var p = server.Sessions[1].State;
        foreach (var stage in new[] { "mine", "craft", "eat", "scan", "unlock" })
        {
            p.Milestones.Add("vega:stage:" + stage);
        }

        lines.Clear();
        server.EnterSpace("Pilot"); // completes the launch lesson
        client.Poll();

        Assert.Contains("vega:stage:dock", server.MilestonesForTest("Pilot"));
        var keys = lines.Where(l => l.LineKey.Length > 0).Select(l => l.LineKey).ToList();
        Assert.Equal(new[] { "vega.s.launch.done", "vega.s.dock.start_far", "vega.s.trade.start" },
            keys.Where(k => k.StartsWith("vega.s.", StringComparison.Ordinal)).ToArray());
        Assert.Equal("vega.obj.trade", lines.Last().ObjectiveKey);
    }

    [Fact]
    public void WithSpaceFlightOff_TheFlightLessonsAreSkipped_OnRejoin()
    {
        var repo = Repo("no_flight");
        repo.Initialize();
        var stored = new PlayerState { PlayerId = "Pilot", Name = "Pilot" };
        stored.Milestones.Add("vega:intro");
        foreach (var stage in new[] { "mine", "craft", "eat", "scan", "unlock" })
        {
            stored.Milestones.Add("vega:stage:" + stage);
        }

        repo.SavePlayer(stored);
        var server = Boot("no_flight", out var client, c => c.Rules.FreeSpaceFlight = false, repo);
        var lines = JoinOverNetwork(server, client, "Pilot");

        var milestones = server.MilestonesForTest("Pilot");
        Assert.Contains("vega:stage:launch", milestones);
        Assert.Contains("vega:stage:dock", milestones);
        Assert.DoesNotContain("vega:stage:trade", milestones);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.resume"); // not "carry on" with an impossible lesson
        Assert.Contains(lines, l => l.LineKey == "vega.s.trade.start");
        Assert.Equal("vega.obj.trade", lines.Last().ObjectiveKey);
    }
}
