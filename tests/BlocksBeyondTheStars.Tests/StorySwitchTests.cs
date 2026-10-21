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
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2506 — a world admin switches the main story in-game (VEGA ↔ "none") without losing progress: switching back restores
/// the story's saved row (fragments, milestones, revealed beats, the Guardian flags) instead of starting it over; every
/// player's own keys of it stay untouched; while "none" runs nothing of the VEGA world story does (lines, leads, fragments,
/// queued scenes, the Guardian system); re-selecting the running story changes nothing; the active row wins at a restart;
/// the admins' rule set names the main story (the capability a client needs before it offers the switch).
/// </summary>
public sealed class StorySwitchTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string None = StoryRegistry.NoneStoryId;

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public StorySwitchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_story_switch_" + Guid.NewGuid().ToString("N"));
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

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null,
        SqliteWorldRepository? repo = null)
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
        var server = new SvGameServer(config, _content, transport, repo ?? Repo(world));
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path; <paramref name="admin"/> makes the player a world admin.</summary>
    private static PlayerSession Join(SvGameServer server, int conn, string name, bool admin = false, bool cutscenes = false)
    {
        server.HandlePayloadForTest(conn, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = name,
            Features = cutscenes ? new[] { ClientFeatures.Packs1, ClientFeatures.Cutscenes1 } : new[] { ClientFeatures.Packs1 },
        }));
        var session = server.Sessions[conn];
        session.State.Role = admin ? PlayerRole.Admin : PlayerRole.Player;
        return session;
    }

    private static void Deliver(SvGameServer server, PlayerSession who, object message)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));

    private static void Switch(SvGameServer server, PlayerSession admin, string storyId)
        => Deliver(server, admin, new StorySelectIntent { StoryId = storyId });

    /// <summary>The player's own keys of the VEGA story (the per-player track, beats, scenes, chapters).</summary>
    private static List<string> VegaKeys(PlayerSession p)
        => p.State.Milestones.Where(k => k.StartsWith(StoryPackKeys.Prefix(V), StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>A VEGA world some way into the arc, the player's track started.</summary>
    private static void PlayAWhile(SvGameServer server, params PlayerSession[] players)
    {
        foreach (var p in players)
        {
            server.SkipTutorialForTest(p); // the start rule: the basic training is through
        }

        for (int i = 0; i < 4; i++)
        {
            server.RecordStoryFragmentForTest("frag_switch_" + i);
        }

        server.RecordStoryMachineKillForTest();
        server.RecordStoryMilestoneForTest("first_base");
        server.AdminAdvanceStory(10);
    }

    private static bool OnStarMap(RecordingTransport transport, PlayerSession p)
        => transport.To<StarMapData>(p).Last().Systems.Any(s => s.Id == SvGameServer.GuardianFinaleSystemId);

    // ---------------- Switching back restores ----------------

    [Fact]
    public void VegaToNoneToVega_KeepsTheWorldArc_AndThePlayersTrack()
    {
        var (server, transport) = World("back_again");
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        var before = server.StorySnapshot;
        var fragmentsBefore = server.StoryStateForTest("Admin")!.FoundFragmentKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var keysBefore = VegaKeys(admin);
        Assert.True(before.BeatsRevealed > 2);
        Assert.Equal(4, before.Fragments);
        Assert.Contains(StoryPackKeys.Started(V), keysBefore);

        Switch(server, admin, None);
        Assert.Equal(None, server.StorySnapshot.StoryId);
        Assert.Equal(0, server.StorySnapshot.Fragments);
        Assert.False(server.StoryStateForTest("Admin")!.Active);
        Assert.Equal(keysBefore, VegaKeys(admin)); // the player's own track is never touched

        int lines = transport.To<ShipAiLine>(admin).Count;
        Switch(server, admin, V);

        Assert.Equal(before, server.StorySnapshot); // fragments, kills, milestones, beats, the Guardian flag — back where it stopped
        var state = server.StoryStateForTest("Admin")!;
        Assert.True(state.Active);
        Assert.True(state.Started);
        Assert.Equal(fragmentsBefore, state.FoundFragmentKeys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        Assert.Equal(keysBefore, VegaKeys(admin));
        var after = transport.To<ShipAiLine>(admin).Skip(lines).ToList();
        Assert.DoesNotContain(after, l => l.Kind == 4); // no prologue again
        Assert.DoesNotContain(after, l => l.LineKey.StartsWith("story.vega.beat", StringComparison.Ordinal)); // no beat spoken twice
    }

    [Fact]
    public void TheGuardianFlags_ComeBack_AndTheSystemIsOffTheMapWhileNoStoryRuns()
    {
        var (server, transport) = World("guardian_back", c => c.Rules.FreeSpaceFlight = true);
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        server.AdminRevealFinale();
        Assert.True(server.IsGuardianSystemRevealedForTest);
        Deliver(server, admin, new RequestStarMap());
        Assert.True(OnStarMap(transport, admin));

        Switch(server, admin, None);
        Assert.False(server.IsGuardianSystemRevealedForTest);
        Assert.True(server.GalaxyHasGuardianSystemForTest); // still in the galaxy: whoever is there is not stranded …
        Assert.False(OnStarMap(transport, admin));           // … but off the map, and no jump leads there
        server.HyperjumpToSystem(admin.State.PlayerId, SvGameServer.GuardianFinaleSystemId);
        Assert.Contains(transport.To<ActionRejected>(admin), r => r.Reason == "@srv.space.unknown_system");

        Switch(server, admin, V);
        Assert.True(server.IsGuardianSystemRevealedForTest);
        Assert.True(server.StoryStateForTest("Admin")!.GuardianSystemRevealed);
        Assert.True(OnStarMap(transport, admin));
    }

    [Fact]
    public void AWonWorld_IsStillWon_WhenTheStoryComesBack()
    {
        var (server, _) = World("won_back");
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();
        Assert.True(server.StorySnapshot.Defeated);

        Switch(server, admin, None);
        Assert.False(server.StorySnapshot.Defeated);
        Switch(server, admin, V);
        Assert.True(server.StorySnapshot.Defeated);
        Assert.Equal(4, server.WorldKnowledgeLevel());
    }

    [Fact]
    public void ReselectingTheRunningStory_ChangesNothing()
    {
        var (server, transport) = World("reselect");
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        var before = server.StorySnapshot;
        int messages = transport.To<ServerMessage>(admin).Count;

        Assert.False(server.SetActiveStory(V));
        Switch(server, admin, V);
        Assert.Equal(before, server.StorySnapshot); // it used to start the story over in place
        Assert.Equal(messages, transport.To<ServerMessage>(admin).Count); // and nothing is announced

        Switch(server, admin, None);
        Assert.False(server.SetActiveStory("NONE"));
        Assert.Equal(None, server.StorySnapshot.StoryId);
    }

    [Fact]
    public void AnUnknownOrPlayerPack_ChangesNothing()
    {
        var (server, _) = World("unknown");
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        var before = server.StorySnapshot;

        Assert.False(server.SetActiveStory("no_such_pack"));
        Assert.False(server.SetActiveStory(SvGameServer.OfficialTutorialPackId)); // a player pack is never the world story
        Assert.Equal(before, server.StorySnapshot);
    }

    [Fact]
    public void ANonAdmin_IsRefused()
    {
        var (server, transport) = World("non_admin");
        var player = Join(server, 1, "Player");
        PlayAWhile(server, player);
        var before = server.StorySnapshot;

        Switch(server, player, None);

        Assert.Equal(before, server.StorySnapshot);
        Assert.Contains(transport.To<ActionRejected>(player), r => r.Reason == "@srv.admin.story_admin_only");
        Assert.DoesNotContain(transport.To<ServerMessage>(player), m => m.Text.Contains("main story", StringComparison.Ordinal));
    }

    // ---------------- Nothing of VEGA runs while "none" ----------------

    [Fact]
    public void WhileNone_NothingOfTheVegaStoryRuns_AndThePlayersKeysStayAsTheyAre()
    {
        var (server, transport) = World("quiet");
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        Assert.True(server.LeadListenerActiveForTest);
        var keys = VegaKeys(admin);

        Switch(server, admin, None);
        int lines = transport.To<ShipAiLine>(admin).Count;
        server.RecordStoryFragmentForTest("frag_while_none");
        server.RecordStoryMachineKillForTest();
        server.RecordStoryMilestoneForTest();
        server.RecordStoryMilestoneForTest("first_station");
        Assert.Equal(0, server.AdminAdvanceStory(20));
        Assert.False(server.RevealCompanionWardInsightForTest(admin.State.PlayerId));
        Assert.False(server.RevealShapeAnomalyMemoryForTest(admin.State.PlayerId));
        server.SpeakRelayInsightForTest("relay");
        server.TickStoryLeadsForTest(admin);

        var snap = server.StorySnapshot;
        Assert.Equal((None, 0, 0, 0, 0, false), snap);
        Assert.False(server.LeadListenerActiveForTest);                   // no chapter leads listen
        Assert.Empty(server.LeadsForTest(admin));
        Assert.Equal(0, server.WorldKnowledgeLevel());                    // NPCs speak no story flavour
        Assert.Empty(transport.To<NetFragmentList>(admin).Last().Fragments); // the fragments are hidden, not gone
        Assert.DoesNotContain(transport.To<ShipAiLine>(admin).Skip(lines),
            l => l.LineKey.StartsWith("story.", StringComparison.Ordinal) || l.LineKey.StartsWith("vega.relay.", StringComparison.Ordinal));
        Assert.Equal(keys, VegaKeys(admin));

        // Back on: the lead listener listens again, and the finds made while it was off did not count.
        Switch(server, admin, V);
        Assert.True(server.LeadListenerActiveForTest);
        Assert.DoesNotContain("frag_while_none", server.StoryStateForTest("Admin")!.FoundFragmentKeys);
    }

    [Fact]
    public void AQueuedVegaScene_WaitsWhileTheStoryIsOff_AndPlaysWhenItIsBack()
    {
        var (server, transport) = World("queued_scene");
        var admin = Join(server, 1, "Admin", admin: true, cutscenes: true);
        server.SkipTutorialForTest(admin);
        Assert.True(server.PlayCutscene(admin, V, "b03")); // queued: the client is not in the world yet

        Switch(server, admin, None);
        for (int i = 0; i < 4; i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(admin);
        }

        Assert.DoesNotContain(transport.To<CutscenePlay>(admin), c => c.PackId == V); // nothing of the story plays while it is off

        Switch(server, admin, V);
        for (int i = 0; i < 4; i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(admin);
        }

        Assert.Contains("b03", transport.Plays(admin)); // the promise was kept
    }

    // ---------------- Several players ----------------

    [Fact]
    public void ASwitchWithTwoPlayersOnline_TellsEveryone_AndBothCarryOn()
    {
        var (server, transport) = World("two_players");
        var admin = Join(server, 1, "Admin", admin: true);
        var friend = Join(server, 2, "Friend");
        PlayAWhile(server, admin, friend);
        var friendKeys = VegaKeys(friend);
        var before = server.StorySnapshot;

        Switch(server, admin, None);
        foreach (var p in new[] { admin, friend })
        {
            Assert.Contains(transport.To<ServerMessage>(p), m => m.Text.Contains("Admin switched the main story off", StringComparison.Ordinal));
            Assert.False(transport.To<StoryStateMessage>(p).Last().Active);
        }

        // A newcomer arrives while the world has no story: nothing of VEGA meets them.
        var newcomer = Join(server, 3, "Newcomer");
        Assert.Empty(VegaKeys(newcomer));
        Assert.DoesNotContain(transport.To<ShipAiLine>(newcomer), l => l.Kind == 4);

        Switch(server, admin, V);
        Assert.Equal(before, server.StorySnapshot);
        foreach (var p in new[] { admin, friend, newcomer })
        {
            Assert.Contains(transport.To<ServerMessage>(p), m => m.Text.Contains("Admin switched the main story on: VEGA", StringComparison.Ordinal));
            Assert.True(transport.To<StoryStateMessage>(p).Last().Active);
        }

        Assert.Equal(friendKeys, VegaKeys(friend));                         // the friend's track carries on, untouched
        Assert.Contains(StoryPackKeys.Started(V), VegaKeys(friend));
        Assert.Contains(transport.To<ShipAiLine>(newcomer), l => l.Kind == 4); // the newcomer meets the story: its prologue
        Assert.Contains(StoryPackKeys.Beat(V, 0), VegaKeys(newcomer));
    }

    [Fact]
    public void APlayerWhoMissedBeats_CatchesUp_WhenTheStoryComesBack()
    {
        var (server, transport) = World("missed");
        var admin = Join(server, 1, "Admin", admin: true);
        var friend = Join(server, 2, "Friend");
        PlayAWhile(server, admin, friend);
        int heard = server.StoryStateForTest("Friend")!.HeardBeats.Length;

        // The friend "was away" for the last beat: their key of it is gone, as if they had been offline when it was revealed.
        int last = server.StorySnapshot.BeatsRevealed - 1;
        friend.State.Milestones.Remove(StoryPackKeys.Beat(V, last));

        Switch(server, admin, None);
        Switch(server, admin, V);

        Assert.Contains(StoryPackKeys.Beat(V, last), VegaKeys(friend));
        Assert.Equal(heard, server.StoryStateForTest("Friend")!.HeardBeats.Length);
        Assert.Contains(transport.LineKeys(friend), k => k.StartsWith("story.vega.beat", StringComparison.Ordinal));
    }

    // ---------------- The capability and the rules ----------------

    [Fact]
    public void TheRuleSet_NamesTheMainStory_ToAdminsOnly_AndFollowsTheSwitch()
    {
        var (server, transport) = World("capability");
        var admin = Join(server, 1, "Admin", admin: true);
        var player = Join(server, 2, "Player");
        Assert.Equal(V, transport.To<ServerRules>(admin).Last().MainStory); // the second join re-sent the admins' roster

        Switch(server, admin, None);
        Assert.Equal(None, transport.To<ServerRules>(admin).Last().MainStory);
        Assert.All(transport.To<ServerRules>(player), r => Assert.Equal(string.Empty, r.MainStory));

        Switch(server, admin, V);
        Assert.Equal(V, transport.To<ServerRules>(admin).Last().MainStory);
    }

    [Fact]
    public void ServerRules_MainStory_RoundTrips_AndDefaultsToEmpty()
    {
        Assert.Equal(string.Empty, new ServerRules().MainStory);
        var decoded = Assert.IsType<ServerRules>(NetCodec.Decode(NetCodec.Encode(new ServerRules { MainStory = None })));
        Assert.Equal(None, decoded.MainStory);
    }

    // ---------------- A restart ----------------

    [Fact]
    public void TheRestoredStory_SurvivesARestart_AndTheRulesAgree()
    {
        var repo = Repo("restart");
        var (server, _) = World("restart", repo: repo);
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        var before = server.StorySnapshot;
        Switch(server, admin, None);
        server.Stop();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        (server, _) = World("restart", repo: repo);
        Assert.Equal(None, server.StorySnapshot.StoryId);              // the active row wins
        Assert.Equal(None, repo.LoadMetadata()!.RulesOverride!.StoryId); // and the save's rules say the same
        admin = Join(server, 1, "Admin", admin: true);
        Switch(server, admin, V);
        Assert.Equal(before, server.StorySnapshot);
        server.Stop();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        (server, _) = World("restart", repo: repo);
        Assert.Equal(before, server.StorySnapshot); // VEGA, where it stopped
        Assert.Equal(V, repo.LoadMetadata()!.RulesOverride!.StoryId);
        Assert.Single(repo.ListStoryStates(), r => r.Active);
        server.Stop();
    }

    /// <summary>A save from before world options (no rules of its own — it reads the launch config) is not given a whole
    /// baked rule set by a story switch, so later config edits keep applying; the active row still decides at a restart.</summary>
    [Fact]
    public void ASaveWithoutItsOwnRules_KeepsReadingTheConfig_AfterASwitch()
    {
        var repo = Repo("legacy_rules");
        var (server, _) = World("legacy_rules", repo: repo);
        server.Stop();
        var meta = repo.LoadMetadata()!;
        meta.RulesOverride = null;
        repo.SaveMetadata(meta);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        (server, _) = World("legacy_rules", repo: repo);
        var admin = Join(server, 1, "Admin", admin: true);
        Switch(server, admin, None);
        Assert.Equal(None, server.StorySnapshot.StoryId);
        server.Stop();
        Assert.Null(repo.LoadMetadata()!.RulesOverride);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        (server, _) = World("legacy_rules", repo: repo);
        Assert.Equal(None, server.StorySnapshot.StoryId); // the active row decides
        server.Stop();
    }

    /// <summary>A pilot flying in the Guardian system when the story is switched off can still land on the body they are at
    /// — the system is off the map and no jump leads there, but nobody is stranded.</summary>
    [Fact]
    public void APilotInTheGuardianSystem_CanStillLand_WhenTheStoryIsSwitchedOff()
    {
        var (server, _) = World("guardian_land", c =>
        {
            c.Rules.FreeSpaceFlight = true;
            c.PlaceStarterShip = true;
        });
        var admin = Join(server, 1, "Admin", admin: true);
        PlayAWhile(server, admin);
        server.AdminRevealFinale();
        server.Ship.Modules.Add("jump_generator");
        server.HyperjumpToSystem(admin.State.PlayerId, SvGameServer.GuardianFinaleSystemId);
        Assert.True(server.InSpace(admin.State.PlayerId));
        var anchor = server.Galaxy.FindBody(admin.CurrentLocationId)!;
        Assert.Equal(SvGameServer.GuardianFinaleSystemId, anchor.SystemId);

        Switch(server, admin, None);
        server.LandOnBody(admin.State.PlayerId, anchor.Id);
        Assert.False(server.InSpace(admin.State.PlayerId));
        Assert.Equal(anchor.Id, admin.CurrentLocationId);
    }

    [Fact]
    public void ANoneWorld_SwitchedToVegaForTheFirstTime_StartsItFresh()
    {
        var (server, _) = World("fresh", c => c.Rules.StoryId = None);
        var admin = Join(server, 1, "Admin", admin: true);
        Assert.Equal(None, server.StorySnapshot.StoryId);

        Switch(server, admin, V);
        Assert.Equal(V, server.StorySnapshot.StoryId);
        Assert.Equal(0, server.StorySnapshot.Fragments);
        Assert.Equal(1, server.StorySnapshot.BeatsRevealed); // B0, the fresh start
    }
}
