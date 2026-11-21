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
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3892 — a world-story mission marked <c>keep</c> stays open after its chapter. The chapter follows the world's shared
/// score, so a mission without the switch is handed out only while the world is in exactly its chapter and is let go when
/// the chapter moves on. These tests run the real server with the VEGA arc and a mission list built here (the shipped
/// pack's own missions are taken out, so the tests do not depend on them): a kept mission that runs survives the chapter
/// change; one that was never handed out comes in a later chapter after that chapter's own missions; an infeasible one is
/// skipped silently and its alternative stands in and is kept too; the bonus of a mission finished late is paid under its
/// own chapter's key, once, never after the last beat; the open finale keeps its chip; the Journal lists the kept ones;
/// a mission without the switch behaves as before; older clients get none; and the validator's rules.
/// </summary>
public sealed class KeptStoryMissionsTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;

    private readonly string _root;
    private readonly List<SqliteWorldRepository> _repos = new();

    public KeptStoryMissionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_kept_missions_" + Guid.NewGuid().ToString("N"));
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

    // ---------------- The pack built here ----------------

    /// <summary>A one-step mission: scanning something called like the mission finishes it (<c>scan:any</c>, no lead, no
    /// place — so nothing but the test finishes it). Its briefing line is <c>test.&lt;id&gt;.start</c>, its chip text
    /// <c>test.&lt;id&gt;.obj</c>.</summary>
    private static StoryMission Mission(string id, bool keep = false, string? feasibleIf = null, string? alternative = null)
    {
        var mission = new StoryMission
        {
            Id = id,
            TitleKey = "test." + id + ".title",
            Keep = keep,
            Alternative = alternative ?? string.Empty,
            Steps =
            {
                new StoryStep
                {
                    Id = id + ".1",
                    Objective = new StoryStepObjective { Event = GameEventKeys.ScanAny, Arg = id },
                    Explain = new StoryStepExplain { StartKey = Start(id), ChipKey = Chip(id) },
                },
            },
        };
        if (feasibleIf is not null)
        {
            mission.FeasibleIf.Add(feasibleIf);
        }

        return mission;
    }

    private static string Start(string mission) => "test." + mission + ".start";

    private static string Chip(string mission) => "test." + mission + ".obj";

    /// <summary>A condition no test world here fulfils (they are survival worlds).</summary>
    private const string Never = "world:creative";

    /// <summary>The shipped content with VEGA's missions replaced by the given ones (chapter id → its missions in order).</summary>
    private static GameContent Content(params (string Chapter, StoryMission Mission)[] missions)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var vega = content.Stories[V];
        foreach (var chapter in vega.Chapters)
        {
            chapter.Missions.Clear();
        }

        foreach (var (chapterId, mission) in missions)
        {
            vega.Chapters.Single(c => c.Id == chapterId).Missions.Add(mission);
        }

        return content;
    }

    // ---------------- The world ----------------

    private (SvGameServer Server, RecordingTransport Transport) World(string world, GameContent content)
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
        config.Rules.TutorialPolicy = TutorialPolicy.Off;
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, content, transport, repo);
        server.Start();
        return (server, transport);
    }

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

    /// <summary>Every queued story scene went out, then the 1 Hz mission pass ran.</summary>
    private static void Settle(SvGameServer server, PlayerSession p)
    {
        for (int i = 0; i < 40 && (i < 4 || p.Story.CutsceneQueue.Count > 0); i++)
        {
            server.AdvanceUptimeForTest(SvGameServer.CutsceneCooldownSeconds);
            server.TickCutscenesForTest(p);
        }

        Assert.Empty(p.Story.CutsceneQueue);
        server.TickStoryMissionsForTest(p);
    }

    /// <summary>Adds the milestones the world still needs to reveal beat <paramref name="beat"/>.</summary>
    private static void AdvanceTo(SvGameServer server, GameContent content, int beat)
    {
        int threshold = content.Stories[V].Beats[beat].Threshold;
        int missing = ((threshold + 1) / 2) - server.StorySnapshot.Milestones;
        if (missing > 0)
        {
            server.AdminAdvanceStory(missing);
        }

        Assert.True(server.StorySnapshot.BeatsRevealed > beat, $"B{beat} was not revealed");
    }

    /// <summary>The player does what the mission asks (its one step's event), then the 1 Hz pass runs.</summary>
    private static void Finish(SvGameServer server, PlayerSession p, string mission)
    {
        server.RaiseGameEventForTest(p, GameEventKeys.ScanAny, mission);
        server.TickStoryMissionsForTest(p);
    }

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static string Done(string mission) => StoryPackKeys.ChapterDone(V, mission);

    private static string Skipped(string mission) => StoryPackKeys.ChapterSkipped(V, mission);

    // ---------------- Walking: a running kept mission survives the chapter change ----------------

    [Fact]
    public void AKeptMissionThatRuns_SurvivesTheChapterChange_AndTheChaptersOwnMissionTakesTheChip()
    {
        var content = Content(("signature", Mission("k_tower", keep: true)), ("service", Mission("s_own")));
        var (server, t) = World("keep_walk", content);
        var ann = Join(server, 1, "Ann");
        var ben = Join(server, 2, "Ben");
        Settle(server, ann);
        Settle(server, ben);
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(ann));
        Assert.Equal("k_tower.1", server.MissionChipStepForTest(ann));

        AdvanceTo(server, content, 2); // the world moves on to chapter 2 while the mission is still open
        Assert.Equal("service", server.TrackChapterIdForTest(ann));
        server.TickStoryMissionsForTest(ann);
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(ann)); // not let go
        Assert.False(Has(ann, Skipped("k_tower")) || Has(ann, StoryPackKeys.StepSkipped(V, "k_tower.1")));
        Assert.Equal("k_tower.1", server.MissionChipStepForTest(ann)); // chapter 2's scene first: it keeps the chip meanwhile

        // The chapter's own mission starts exactly as before and ranks first on the chip; the kept one walks on beside it.
        Settle(server, ann);
        Assert.Equal(new[] { "k_tower", "s_own" }, server.RunningMissionsForTest(ann));
        Assert.Equal("s_own.1", server.MissionChipStepForTest(ann));
        Assert.Equal((Chip("s_own"), ObjectiveSources.Story), (server.ObjectiveKeyForTest("Ann"), server.ObjectiveSourceForTest("Ann")));
        Assert.Contains("mission=s_own/s_own.1", server.StoryMissionStatusForTest(ann), StringComparison.Ordinal);
        Assert.Equal(1, t.LineKeys(ann).Count(k => k == Start("k_tower"))); // briefed once, never again

        // The Journal: the chapter's own first, then the kept one of the earlier chapter — with its own chapter's id.
        var rows = server.JournalMissionsForTest(ann);
        Assert.Equal(new[] { ("s_own", "service"), ("k_tower", "signature") }, rows.Select(r => (r.Id, r.ChapterId)));
        Assert.All(rows, r => Assert.Equal((StoryChapterStates.Active, true), (r.State, r.Skippable)));
        Assert.Equal(new[] { true, false }, rows.Select(r => r.Steps.Single().OnChip));
        var wire = (StoryPackStates)NetCodec.Decode(NetCodec.Encode(server.StoryPackStatesForTest(ann)))!;
        Assert.Equal(new[] { "s_own", "k_tower" }, wire.Packs.Single(x => x.Id == V).Missions.Select(m => m.Id));

        // Finished late, it pays its OWN chapter's bonus — once per save and chapter.
        Assert.False(server.LeadBonusPaidForTest("signature") || server.LeadBonusPaidForTest("service"));
        int milestones = server.StorySnapshot.Milestones;
        Finish(server, ann, "k_tower");
        Assert.True(Has(ann, Done("k_tower")));
        Assert.True(server.LeadBonusPaidForTest("signature"));
        Assert.False(server.LeadBonusPaidForTest("service"));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);
        Assert.Equal(new[] { "s_own" }, server.JournalMissionsForTest(ann).Select(r => r.Id)); // through: no row of an earlier chapter

        server.TickStoryMissionsForTest(ben);
        Finish(server, ben, "k_tower"); // everyone does their own — the bonus is not paid a second time
        Assert.True(Has(ben, Done("k_tower")));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);

        Finish(server, ann, "s_own"); // the current chapter's bonus was not used up by the kept mission
        Assert.True(Has(ann, Done("s_own")));
        Assert.True(server.LeadBonusPaidForTest("service"));
        Assert.Equal(milestones + 6, server.StorySnapshot.Milestones);
    }

    // ---------------- Handing out: later, after the chapter's own ----------------

    [Fact]
    public void AKeptMissionNeverHandedOut_ComesInALaterChapter_AfterThatChaptersOwnMissions_OneAtATime()
    {
        var content = Content(
            ("signature", Mission("k_tower", keep: true)),
            ("signature", Mission("p_plain")),
            ("service", Mission("s_own")),
            ("service", Mission("s_keep", keep: true)));
        var (server, t) = World("keep_later", content);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, content, 2); // the world is in chapter 2 before VEGA ever briefed this player
        Settle(server, p);
        Assert.Equal(new[] { "s_own" }, server.RunningMissionsForTest(p)); // the chapter's own first
        for (int i = 0; i < 5; i++)
        {
            server.TickStoryMissionsForTest(p);
        }

        Assert.Equal(new[] { "s_own" }, server.RunningMissionsForTest(p)); // a kept mission never starts beside a running one
        Assert.DoesNotContain(Start("k_tower"), t.LineKeys(p));

        Finish(server, p, "s_own");
        Assert.Equal(new[] { "s_keep" }, server.RunningMissionsForTest(p)); // still the chapter's own (data order)
        Finish(server, p, "s_keep");

        // None of the chapter's own is left: the kept mission of chapter 1 gets its turn — briefing, chip, Journal.
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == Start("k_tower")));
        Assert.Equal("k_tower.1", server.MissionChipStepForTest(p));
        Assert.Equal((Chip("k_tower"), ObjectiveSources.Story), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));
        Assert.Equal(
            new[] { ("s_own", StoryChapterStates.Done), ("s_keep", StoryChapterStates.Done), ("k_tower", StoryChapterStates.Active) },
            server.JournalMissionsForTest(p).Select(r => (r.Id, r.State)));

        // The mission without the switch of that earlier chapter is not handed out — as before.
        Assert.False(Has(p, Done("p_plain")) || Has(p, Skipped("p_plain")));
        Assert.DoesNotContain(Start("p_plain"), t.LineKeys(p));

        int milestones = server.StorySnapshot.Milestones;
        Finish(server, p, "k_tower");
        Assert.True(Has(p, Done("k_tower")));
        Assert.True(server.LeadBonusPaidForTest("signature"));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);

        AdvanceTo(server, content, 5); // chapter 3 has no mission and nothing kept is open: nothing more comes
        Settle(server, p);
        Assert.Empty(server.RunningMissionsForTest(p));
        Assert.Empty(server.JournalMissionsForTest(p));
        Assert.DoesNotContain(Start("p_plain"), t.LineKeys(p));
    }

    [Fact]
    public void AKeptMissionOfTheChapter_WaitsWhileAnEarlierKeptOneRuns_AndTheChaptersNextGoesFirst()
    {
        var content = Content(
            ("signature", Mission("k_tower", keep: true)),
            ("service", Mission("s_keep", keep: true)),
            ("service", Mission("s_own")));
        var (server, _) = World("keep_wait", content);
        var p = Join(server, 1, "Pilot");
        Settle(server, p);
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));

        AdvanceTo(server, content, 2);
        Settle(server, p);
        Assert.Equal(new[] { "k_tower", "s_own" }, server.RunningMissionsForTest(p)); // never two kept ones at a time
        Finish(server, p, "s_own");
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));
        Finish(server, p, "k_tower");
        Settle(server, p); // (its bonus revealed a beat: that card first)
        Assert.Equal(new[] { "s_keep" }, server.RunningMissionsForTest(p)); // it was not lost: its turn comes
    }

    // ---------------- Feasibility and the alternative ----------------

    [Fact]
    public void AnInfeasibleKeptMission_IsSkippedSilently_AndItsAlternativeStandsIn_AndIsKeptToo()
    {
        var content = Content(
            ("signature", Mission("k_tower", keep: true, feasibleIf: Never, alternative: "k_stand_in")),
            ("signature", Mission("k_stand_in")));
        var (server, t) = World("keep_alt", content);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, content, 2); // chapter 2 has no mission of its own
        Settle(server, p);
        Assert.True(Has(p, Skipped("k_tower")));
        Assert.DoesNotContain(Start("k_tower"), t.LineKeys(p)); // silently
        Assert.Equal(new[] { "k_stand_in" }, server.RunningMissionsForTest(p));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == Start("k_stand_in")));

        AdvanceTo(server, content, 5); // the stand-in of a kept mission stays open with it
        Settle(server, p);
        Assert.Equal(new[] { "k_stand_in" }, server.RunningMissionsForTest(p));
        Assert.False(Has(p, Skipped("k_stand_in")));
        Assert.Equal(new[] { "k_stand_in" }, server.JournalMissionsForTest(p).Select(r => r.Id));

        Finish(server, p, "k_stand_in");
        Assert.True(Has(p, Done("k_stand_in")));
        Assert.Empty(server.RunningMissionsForTest(p)); // the skipped one is not handed out again

        // Neither can be done here: both are closed, nothing runs, no line.
        var none = Content(
            ("signature", Mission("k_tower", keep: true, feasibleIf: Never, alternative: "k_stand_in")),
            ("signature", Mission("k_stand_in", feasibleIf: Never)));
        var (dark, dt) = World("keep_alt_none", none);
        var q = Join(dark, 1, "Pilot");
        AdvanceTo(dark, none, 2);
        Settle(dark, q);
        Assert.True(Has(q, Skipped("k_tower")) && Has(q, Skipped("k_stand_in")));
        Assert.Empty(dark.RunningMissionsForTest(q));
        Assert.DoesNotContain(dt.LineKeys(q), k => k.StartsWith("test.", StringComparison.Ordinal));
    }

    // ---------------- A mission without the switch: exactly as before ----------------

    [Fact]
    public void AMissionWithoutKeep_IsLetGoWhenTheChapterMovesOn_AndNeverComesBack_EvenInAPackWithKeptOnes()
    {
        var content = Content(
            ("signature", Mission("p_plain")),
            ("signature", Mission("k_tower", keep: true)),
            ("service", Mission("s_own")));
        var (server, t) = World("keep_regression", content);
        var p = Join(server, 1, "Pilot");
        Settle(server, p);
        Assert.Equal(new[] { "p_plain" }, server.RunningMissionsForTest(p));

        AdvanceTo(server, content, 2);
        int linesBefore = t.LineKeys(p).Count;
        server.TickStoryMissionsForTest(p);
        Assert.True(Has(p, Skipped("p_plain"))); // let go, silently — as before
        Assert.True(Has(p, StoryPackKeys.StepSkipped(V, "p_plain.1")));
        Assert.Empty(server.RunningMissionsForTest(p)); // chapter 2's scene first — also for the kept one
        Assert.Equal(linesBefore, t.LineKeys(p).Count);

        Settle(server, p);
        Assert.Equal(new[] { "s_own" }, server.RunningMissionsForTest(p));
        Assert.Equal(new[] { "s_own" }, server.JournalMissionsForTest(p).Select(r => r.Id)); // only this chapter's
        Finish(server, p, "s_own");
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));
        Finish(server, p, "k_tower");
        Assert.Empty(server.RunningMissionsForTest(p));
        Assert.True(Has(p, Skipped("p_plain")) && !Has(p, Done("p_plain")));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == Start("p_plain")));

        // The switch is off unless the pack says so, and reads as "keep".
        Assert.False(JsonSerializer.Deserialize<StoryMission>("{ \"id\": \"m\" }", ContentLoader.JsonOptions)!.Keep);
        Assert.True(JsonSerializer.Deserialize<StoryMission>("{ \"id\": \"m\", \"keep\": true }", ContentLoader.JsonOptions)!.Keep);
    }

    // ---------------- The finale, the last beat, older clients ----------------

    [Fact]
    public void TheOpenFinale_KeepsItsChipOverAKeptMission_AndAfterTheLastBeatNoBonusIsPaid()
    {
        var content = Content(("sleeps", Mission("k_tower", keep: true)));
        var (server, _) = World("keep_finale", content);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, content, 11);
        Settle(server, p);
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));
        Assert.Equal(Chip("k_tower"), server.ObjectiveKeyForTest("Pilot"));

        server.AdminRevealFinale(); // B12: the finale lead, never a mission (never muted) — the kept one walks on behind it
        server.TickStoryMissionsForTest(p);
        Assert.Equal((StoryLeads.FinaleJumpKey, ObjectiveSources.Finale), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));
        Assert.False(Has(p, Skipped("k_tower")));
        Assert.Equal(new[] { "k_tower" }, server.JournalMissionsForTest(p).Select(r => r.Id));

        server.MarkGuardianDefeatedForTest(); // the last chapter: the mission is still open and has the chip again
        Assert.Equal("resolution", server.TrackChapterIdForTest(p));
        Settle(server, p);
        Assert.Equal(new[] { "k_tower" }, server.RunningMissionsForTest(p));
        Assert.Equal((Chip("k_tower"), ObjectiveSources.Story), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));

        int milestones = server.StorySnapshot.Milestones;
        Finish(server, p, "k_tower");
        Assert.True(Has(p, Done("k_tower")));
        Assert.False(server.LeadBonusPaidForTest("sleeps")); // never after the arc's last beat
        Assert.Equal(milestones, server.StorySnapshot.Milestones);
    }

    [Fact]
    public void AnOlderClient_GetsNoKeptMissionEither()
    {
        var content = Content(("signature", Mission("k_tower", keep: true)));
        var (server, t) = World("keep_old", content);
        var old = Join(server, 1, "Old", packs: false);
        Settle(server, old);
        AdvanceTo(server, content, 2);
        Settle(server, old);
        server.TickStoryMissionsForTest(old);
        Assert.Empty(server.RunningMissionsForTest(old));
        Assert.Empty(server.JournalMissionsForTest(old));
        Assert.DoesNotContain(t.LineKeys(old), k => k.StartsWith("test.", StringComparison.Ordinal));
        Assert.Null(server.MissionChipStepForTest(old));
    }

    // ---------------- The validator ----------------

    [Fact]
    public void TheValidator_AllowsKeepOnAWorldStorysMissions_AndWarnsWhereItChangesNothing()
    {
        string raw = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "story.json"));
        StoryMission Valid(string id, bool keep, string? alternative = null)
        {
            var m = Mission(id, keep, alternative: alternative);
            m.TitleKey = "story.vega.m.signal.title"; // texts that exist, so only the keep rules speak
            m.Steps[0].Explain = new StoryStepExplain { ChipKey = "story.vega.m.signal.1.obj" };
            return m;
        }

        List<StoryPackIssue> KeepIssues(Action<StoryDefinition> change)
        {
            var def = JsonSerializer.Deserialize<StoryDefinition>(raw, ContentLoader.JsonOptions)!;
            change(def);
            return StoryPackValidator.Validate(def, StoryPackValidationTests.Context()).Where(i => i.Path.EndsWith(".keep", StringComparison.Ordinal)).ToList();
        }

        // A kept mission in a chapter that others follow: nothing to say (its alternative needs no switch of its own).
        Assert.Empty(KeepIssues(def =>
        {
            var signature = def.Chapters.Single(c => c.Id == "signature");
            signature.Missions.Add(Valid("k_tower", keep: true, alternative: "k_stand_in"));
            signature.Missions.Add(Valid("k_stand_in", keep: false));
        }));

        // The last chapter: nothing follows it.
        var last = Assert.Single(KeepIssues(def => def.Chapters[^1].Missions.Add(Valid("k_last", keep: true))));
        Assert.Equal(StoryPackIssueSeverity.Warning, last.Severity);
        Assert.Contains("is the last one", last.Message, StringComparison.Ordinal);

        // Only the stand-in is kept: it is never handed out later — the author meant the mission it stands in for.
        var standIn = Assert.Single(KeepIssues(def =>
        {
            var signature = def.Chapters.Single(c => c.Id == "signature");
            signature.Missions.Add(Valid("k_tower", keep: false, alternative: "k_stand_in"));
            signature.Missions.Add(Valid("k_stand_in", keep: true));
        }));
        Assert.Equal(StoryPackIssueSeverity.Warning, standIn.Severity);
        Assert.Contains("only stands in", standIn.Message, StringComparison.Ordinal);

        // A player pack has no chapters the world moves through: keep is an error there.
        var side = StoryPackFixtures.Load(StoryPackFixtures.Runtime);
        side.Chapters[0].Missions.Add(Valid("k_side", keep: true));
        var packs = new Dictionary<string, StoryDefinition>(StoryPackValidationTests.Context().Packs!) { [side.Id] = side };
        var issue = Assert.Single(
            StoryPackValidator.Validate(side, StoryPackValidationTests.Context(packs, StoryPackFixtures.Dir(StoryPackFixtures.Runtime))),
            i => i.Path.EndsWith(".keep", StringComparison.Ordinal));
        Assert.Equal(StoryPackIssueSeverity.Error, issue.Severity);
        Assert.Contains("world story's missions only", issue.Message, StringComparison.Ordinal);
    }
}
