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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2476 — step 16b of the story packs (#2459): VEGA's story missions per chapter (synthesis §4.8 option O3, on top of the leads
/// of #2475). The data (one mission per chapter 1–6 and the resolution, none in the finale's chapter, alternatives), handing a
/// mission out once the chapter's scene went out (never during the basic training) with a briefing line, a Journal entry and the
/// chip in the lead's place; per player in multiplayer; every mission completed through real handlers or bus events (lead
/// bridges, the lore reader, scans, a mission bridge); infeasible missions and steps giving way to their stand-ins; the chapter
/// bonus never paid twice; no beat ever waiting (an unfinished mission is let go when the chapter moves on); the chip order
/// against the training and the finale; the Journal rows and the skip; older clients; the validators; and the cost.
/// </summary>
public sealed class VegaStoryMissionsTests : IDisposable
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const string Tutorial = "tutorial";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public VegaStoryMissionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vega_missions_" + Guid.NewGuid().ToString("N"));
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

    private (SvGameServer Server, RecordingTransport Transport) World(string world, Action<ServerConfig>? configure = null, GameContent? content = null)
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
        var server = new SvGameServer(config, content ?? _content, transport, Repo(world));
        server.Start();
        return (server, transport);
    }

    private static void TutorialOff(ServerConfig c) => c.Rules.TutorialPolicy = TutorialPolicy.Off;

    /// <summary>The real join path; <paramref name="packs"/> = a current client (<c>packs1</c> + <c>cutscenes1</c>), else an older one.</summary>
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

    /// <summary>Every queued story scene went out (a chapter's opening and its beat cards), then the 1 Hz mission pass ran.</summary>
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

    /// <summary>Adds the milestones the world still needs to reveal beat <paramref name="beat"/> (a world with machines).</summary>
    private void AdvanceTo(SvGameServer server, int beat)
    {
        int threshold = Vega.Beats[beat].Threshold;
        int target = (threshold + 1) / 2;
        int missing = target - server.StorySnapshot.Milestones;
        if (missing > 0)
        {
            server.AdminAdvanceStory(missing);
        }

        Assert.True(server.StorySnapshot.BeatsRevealed > beat, $"B{beat} was not revealed");
    }

    private static void EnsureFragments(SvGameServer server, int count = 1)
    {
        for (int i = 0; i < 128 && server.NetFragmentCount < count; i++)
        {
            server.PlaceStructureFragmentForTest("data_terminal", 100f + (i * 7f), 40f, -80f + (i * 5f));
        }

        Assert.True(server.NetFragmentCount >= count, "the data_terminal markers did not roll enough fragments");
    }

    private static bool Has(PlayerSession p, string milestone) => p.State.Milestones.Contains(milestone);

    private static string Done(string mission) => StoryPackKeys.ChapterDone(V, mission);

    private static string Skipped(string mission) => StoryPackKeys.ChapterSkipped(V, mission);

    private StoryDefinition Vega => _content.Stories[V];

    private static Dictionary<string, string> PackTable(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "locales", code + ".json")),
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip })!;

    // ---------------- The data ----------------

    [Fact]
    public void TheVegaPack_GivesEveryChapterButTheFinalesAMission_WithStandIns_AndTheFallbackCarriesNone()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["awakening"] = Array.Empty<string>(),
            ["signature"] = new[] { "m_signal" },
            ["service"] = new[] { "m_observatory", "m_archive" },
            ["guardian"] = new[] { "m_runes" },
            ["verdict"] = new[] { "m_long_quiet", "m_wrecks" },
            ["what_you_are"] = new[] { "m_radar" },
            ["sleeps"] = new[] { "m_jump" },
            ["core"] = Array.Empty<string>(), // the finale is chapter 7's mission (never muted, its own set pieces)
            ["resolution"] = new[] { "m_survey" },
        };
        foreach (var chapter in Vega.Chapters)
        {
            Assert.Equal(expected[chapter.Id], chapter.Missions.Select(m => m.Id));
        }

        var missions = Vega.Chapters.SelectMany(c => c.Missions).ToList();
        Assert.Equal("m_archive", missions.Single(m => m.Id == "m_observatory").Alternative);
        Assert.Equal("m_wrecks", missions.Single(m => m.Id == "m_long_quiet").Alternative);
        Assert.All(missions, m => Assert.InRange(m.Steps.Count(s => !m.Steps.Any(o => o.Alternative == s.Id)), 1, 4)); // stand-ins aside
        Assert.All(missions.SelectMany(m => m.Steps), s => Assert.False(string.IsNullOrEmpty(s.Explain.ChipKey), s.Id));

        // Every mission text in English and German, the chips within budget, no {0} in a mission's own chip text, every VEGA line
        // within one panel page.
        var en = PackTable("en");
        var de = PackTable("de");
        var keys = missions.SelectMany(m => new[] { m.TitleKey }.Concat(m.Steps.SelectMany(s => new[]
        {
            s.Explain.StartKey, s.Explain.HintKey, s.Explain.DoneKey, s.Explain.ChipKey,
        }))).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        foreach (string key in keys)
        {
            Assert.True(en.ContainsKey(key) && de.ContainsKey(key), $"{key} needs an English and a German text in the VEGA pack");
        }

        foreach (var step in missions.SelectMany(m => m.Steps))
        {
            string chip = step.Explain.ChipKey;
            if (en.TryGetValue(chip, out var chipEn))
            {
                Assert.True(StoryPackValidator.ShownLength(chipEn) <= StoryPackValidator.ChipMaxEn, chip);
                Assert.True(StoryPackValidator.ShownLength(de[chip]) <= StoryPackValidator.ChipMaxDe, chip);
                Assert.DoesNotContain("{0}", chipEn, StringComparison.Ordinal);
            }

            foreach (string line in new[] { step.Explain.StartKey, step.Explain.HintKey, step.Explain.DoneKey }.Where(k => !string.IsNullOrEmpty(k)))
            {
                Assert.True(StoryPackValidator.ShownLength(en[line]) <= StoryPackValidator.LineMaxEn, line);
                Assert.True(StoryPackValidator.ShownLength(de[line]) <= StoryPackValidator.LineMaxDe, line);
            }
        }

        // The built-in fallback (used only when the pack does not load — then its texts are missing too) has no missions.
        Assert.All(StoryRegistry.Default.Chapters, c => Assert.Empty(c.Missions));

        var (server, _) = World("m_data", TutorialOff);
        Assert.Equal(new[] { "m_signal", "m_observatory", "m_archive", "m_runes", "m_long_quiet", "m_wrecks", "m_radar", "m_jump", "m_survey" },
            server.StoryMissionIdsForTest);
    }

    // ---------------- Handing out ----------------

    [Fact]
    public void AMission_IsHandedOutAfterTheChaptersScene_WithABriefing_TheJournalAndTheChipInTheLeadsPlace()
    {
        var (server, t) = World("m_offer", TutorialOff);
        var p = Join(server, 1, "Pilot");
        EnsureFragments(server);
        Assert.True(server.StoryTrackStarted(p));

        server.TickStoryMissionsForTest(p);
        Assert.Empty(server.RunningMissionsForTest(p)); // chapter 1's scene still waits in the queue: VEGA briefs after it

        Settle(server, p);
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(p));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.m.signal.1.start"));
        Assert.Equal("m_signal.1", server.MissionChipStepForTest(p));

        // The lead bridge shows the lead's own placed text on the chip (the fragment's map signal comes with it), as VEGA's.
        string home = server.Galaxy.FindBody(p.CurrentLocationId)!.Name;
        Assert.Equal((StoryLeads.FragmentOnKey, home, ObjectiveSources.Story), server.StoryObjectiveForTest(p));
        Assert.Equal(V, server.ObjectivePackForTest("Pilot"));

        var row = Assert.Single(server.JournalMissionsForTest(p));
        Assert.Equal(("m_signal", "signature", "story.vega.m.signal.title", StoryChapterStates.Active, true), (row.Id, row.ChapterId, row.TitleKey, row.State, row.Skippable));
        var step = Assert.Single(row.Steps); // the stand-in nobody needs is left out
        Assert.Equal(("m_signal.1", StoryLeads.FragmentOnKey, home, StoryChapterStates.Active, true), (step.Id, step.Key, step.Arg, step.State, step.OnChip));

        // Handed out once: the next passes say nothing new.
        server.TickStoryMissionsForTest(p);
        server.TickStoryMissionsForTest(p);
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.m.signal.1.start"));
        Assert.Contains("mission=m_signal/m_signal.1", server.StoryMissionStatusForTest(p), StringComparison.Ordinal);

        // The help escalation of the step on the chip: after three minutes of play without progress, VEGA's hint — once.
        Assert.DoesNotContain("story.vega.m.hint.fragment", t.LineKeys(p));
        server.TickStoryPacksForTest(p, 181);
        server.TickStoryMissionsForTest(p);
        server.TickStoryMissionsForTest(p);
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.m.hint.fragment"));
        Assert.DoesNotContain(StoryPackRuntimeSkipOffer, t.LineKeys(p)); // a mission step never nags with the skip offer
    }

    private const string StoryPackRuntimeSkipOffer = "vega.step.offer_skip";

    [Fact]
    public void NoMission_IsHandedOutDuringTheBasicTraining_AndTheTrainingKeepsTheChip()
    {
        var (server, _) = World("m_training");
        var p = Join(server, 1, "Pilot");
        server.StartStoryTrackForTest(p); // "Start the story now" while the basic training runs
        Settle(server, p);
        Assert.Empty(server.RunningMissionsForTest(p));

        p.State.Milestones.Add(StoryPackKeys.OptOut(Tutorial)); // the training is switched off for this player
        Settle(server, p);
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(p));
        Assert.Equal(ObjectiveSources.Story, server.ObjectiveSourceForTest("Pilot"));

        p.State.Milestones.Remove(StoryPackKeys.OptOut(Tutorial)); // back on: rank 3 (basic training) beats VEGA's mission
        Assert.Equal(ObjectiveSources.Tutorial, server.ObjectiveSourceForTest("Pilot"));
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(p)); // it waits, it is not lost
    }

    [Fact]
    public void InMultiplayer_EveryPlayerDoesTheirOwnMission_AndTheChapterBonusIsPaidOnce()
    {
        var (server, _) = World("m_mp", TutorialOff);
        var ann = Join(server, 1, "Ann");
        var ben = Join(server, 2, "Ben");
        EnsureFragments(server, 2);
        Settle(server, ann);
        Settle(server, ben);
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(ann));
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(ben));

        int milestones = server.StorySnapshot.Milestones;
        var fragments = server.NetFragmentSnapshots.Select(f => f.Id).ToList();
        Assert.True(server.PickUpNetFragmentForTest(fragments[0], "Ann")); // the real pickup: the lead and the mission at once
        Assert.True(Has(ann, Done("m_signal")));
        Assert.True(Has(ann, StoryPackKeys.StepDone(V, "m_signal.1")));
        Assert.False(Has(ben, Done("m_signal")));
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(ben));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // the chapter bonus — once

        Assert.True(server.PickUpNetFragmentForTest(fragments[1], "Ben"));
        Assert.True(Has(ben, Done("m_signal")));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // never twice per save and chapter
    }

    // ---------------- Each mission, through real handlers and events ----------------

    [Fact]
    public void TheObservatory_IsALandingThenTheLogFromItsTerminal_AndTheLeadAlreadyPaidTheBonus()
    {
        var (server, t) = World("m_observatory", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 2); // chapter 2, "The Service"
        Settle(server, p);
        Assert.Equal(new[] { "m_observatory" }, server.RunningMissionsForTest(p));
        string site = server.UniqueSiteBodyForTest("observatory")!;
        Assert.Equal(("story.vega.obj.site.observatory", server.Galaxy.FindBody(site)!.Name), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveArgForTest("Pilot")));

        int milestones = server.StorySnapshot.Milestones;
        server.RaiseGameEventForTest(p, GameEventKeys.VisitBody, site); // the landing: the lead and the mission's first step
        Assert.True(Has(p, StoryPackKeys.Lead(V, "service", 1)));
        Assert.True(Has(p, StoryPackKeys.StepDone(V, "m_observatory.1")));
        Assert.Equal("m_observatory.2", server.MissionChipStepForTest(p));
        Assert.Contains("story.vega.m.observatory.2.start", t.LineKeys(p));
        Assert.Equal("story.vega.m.observatory.2.obj", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones);

        server.RevealLoreForTest("Pilot", "observatory"); // the reader opens the post's last log: lore:read
        Assert.True(Has(p, "lore:observatory_log"));
        Assert.True(Has(p, Done("m_observatory")));
        Assert.Contains("story.vega.m.observatory.2.done", t.LineKeys(p));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // the lead paid it already
        Assert.Empty(server.RunningMissionsForTest(p)); // the alternative never runs for a mission that was feasible
        Assert.False(Has(p, Skipped("m_archive")) || Has(p, Done("m_archive")));
        Assert.NotEqual("story.vega.m.observatory.2.obj", server.ObjectiveKeyForTest("Pilot")); // the lead is back on the chip
    }

    /// <summary>#2490: the landing step done by evidence while the player is elsewhere puts the log step on the chip off-site;
    /// its one marker (markerAfter 5) used to be spent right there, where the site provider finds nothing. Now it waits: no
    /// ping off-site, and on the observatory's world a private ping with VEGA's "marked" line.</summary>
    [Fact]
    public void TheObservatoryMarker_IsNotSpentOffSite_ItPingsOnTheSitesWorld()
    {
        var (server, t) = World("m_observatory_marker", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 2);
        Settle(server, p);
        Assert.Equal(new[] { "m_observatory" }, server.RunningMissionsForTest(p));
        string site = server.UniqueSiteBodyForTest("observatory")!;
        Assert.NotEqual(site, p.CurrentLocationId);

        server.RaiseGameEventForTest(p, GameEventKeys.VisitBody, site); // step 1 by evidence — the player is still at home
        Assert.Equal("m_observatory.2", server.MissionChipStepForTest(p));
        server.TickStoryPacksForTest(p, 6); // past markerAfter 5
        server.TickStoryMissionsForTest(p);
        Assert.DoesNotContain(server.VisibleMarkersForTest("Pilot"), m => m.Ping);
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, t.LineKeys(p));

        // On the observatory's world (a new location: probed at once) the marker goes up — for this player only.
        p.Ships[p.ActiveShipId].Modules.Add("jump_generator"); // the site may lie in another system
        server.Travel(p.State.PlayerId, site);
        Assert.Equal(site, p.CurrentLocationId);
        Assert.Equal("m_observatory.2", server.MissionChipStepForTest(p));
        server.TickStoryPacksForTest(p);
        server.TickStoryMissionsForTest(p);
        Assert.Contains(server.VisibleMarkersForTest("Pilot"), m => m.Ping && m.OwnerId == "Pilot");
        Assert.Single(t.LineKeys(p), k => k == SvGameServer.StepMarkedLine);
    }

    [Fact]
    public void AWorldWithoutTheObservatory_GivesTheDeepArchive_WhoseVaultReadingPaysTheBonusOnce()
    {
        var (server, t) = World("m_archive", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 2);
        server.RecordUniqueSiteSkipForTest(server.UniqueSiteBodyForTest("observatory")!, "observatory"); // stamped before #1129
        Settle(server, p);
        Assert.True(Has(p, Skipped("m_observatory")));
        Assert.Equal(new[] { "m_archive" }, server.RunningMissionsForTest(p));
        Assert.DoesNotContain("story.vega.m.observatory.1.start", t.LineKeys(p)); // skipped silently
        Assert.Contains("story.vega.m.archive.1.start", t.LineKeys(p));

        int milestones = server.StorySnapshot.Milestones;
        server.RevealLoreForTest("Pilot", "vault");
        Assert.True(Has(p, Done("m_archive")));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // no lead of the chapter had paid: the mission does

        EnsureFragments(server);
        Assert.True(server.PickUpNetFragmentForTest(server.NetFragmentSnapshots[0].Id, "Pilot")); // the chapter's fragment lead
        Assert.True(Has(p, StoryPackKeys.Lead(V, "service", 2)));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // never twice
    }

    [Fact]
    public void AVaultPlaqueReadBefore_IsTheLoreEvidence_AndTheMissionCompletesSilently()
    {
        var (server, t) = World("m_archive_known", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        p.State.Milestones.Add("lore:vault_plaque");
        Assert.True(server.EvaluateStoryConditionForTest("lore:vault_plaque", p));
        Assert.False(server.EvaluateStoryConditionForTest("lore:observatory_log", p));
        AdvanceTo(server, 2);
        server.RecordUniqueSiteSkipForTest(server.UniqueSiteBodyForTest("observatory")!, "observatory");
        Settle(server, p);
        Assert.True(Has(p, Done("m_archive")));
        Assert.DoesNotContain("story.vega.m.archive.1.start", t.LineKeys(p));
    }

    [Fact]
    public void TheRunes_AreAMonumentReadingThenASettlementJob_WithTheMonumentMarkedForThisPlayer()
    {
        var (server, t) = World("m_runes", TutorialOff);
        var ann = Join(server, 1, "Ann");
        var ben = Join(server, 2, "Ben");
        var at = ann.State.Position;
        server.SpawnMonumentForTest(new Vector3f(at.X + 12, at.Y, at.Z + 12), "obelisk");
        AdvanceTo(server, 5); // chapter 3, "The Guardian"
        Settle(server, ann);
        Assert.Equal(new[] { "m_runes" }, server.RunningMissionsForTest(ann));
        Assert.Equal("story.vega.obj.monument", server.ObjectiveKeyForTest("Ann")); // the lead's placed text: one is here

        server.TickStoryLeadsForTest(ann); // the marker of the mission's bridged lead: a ping for Ann only
        Assert.Contains(server.VisibleMarkersForTest("Ann"), m => m.Ping && m.OwnerId == "Ann");
        Assert.DoesNotContain(server.VisibleMarkersForTest("Ben"), m => m.Ping);

        server.RaiseGameEventForTest(ann, GameEventKeys.ScanAny, "monument");
        Assert.True(Has(ann, StoryPackKeys.StepDone(V, "m_runes.1")));
        Assert.Contains("story.vega.m.runes.2.start", t.LineKeys(ann));
        Assert.Equal("story.vega.obj.settlement_help", server.StoryObjectiveForTest(ann).Key);

        server.AddMissionDefForTest(new MissionDefinition { Id = "teach_test_1", ChainId = "teach_test", Step = 1, Surface = "radio", CountsForStory = false });
        server.AddMissionDefForTest(new MissionDefinition { Id = "help_test_1", ChainId = "help_test", Step = 1, Surface = "radio" });
        server.RaiseGameEventForTest(ann, GameEventKeys.MissionTurnedIn, "teach_test_1"); // a teaching job is no help
        Assert.False(Has(ann, Done("m_runes")));
        server.RaiseGameEventForTest(ann, GameEventKeys.MissionTurnedIn, "help_test_1");
        Assert.True(Has(ann, Done("m_runes")));
        Assert.Contains("story.vega.m.runes.2.done", t.LineKeys(ann));
    }

    [Fact]
    public void TheLongQuiet_IsBoardingTheDerelictThenItsManifest()
    {
        var (server, t) = World("m_long_quiet", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 6); // chapter 4, "The Verdict"
        Assert.True(server.EvaluateStoryConditionForTest("world:derelict_reachable", p));
        Settle(server, p);
        Assert.Equal(new[] { "m_long_quiet" }, server.RunningMissionsForTest(p));

        server.RaiseGameEventForTest(p, GameEventKeys.StationBoarded, SvGameServer.DerelictStationId);
        Assert.True(Has(p, StoryPackKeys.StepDone(V, "m_long_quiet.1")));
        Assert.Equal("story.vega.m.long_quiet.2.obj", server.ObjectiveKeyForTest("Pilot"));
        server.RevealLoreForTest("Pilot", "derelict");
        Assert.True(Has(p, Done("m_long_quiet")));
        Assert.Contains("story.vega.m.long_quiet.2.done", t.LineKeys(p));
    }

    [Fact]
    public void WithoutTheDerelict_ShipsThatRemember_AsksForAWreckScan()
    {
        // A pack copy whose derelict can never be reached here (a fresh content load; the shared one stays as is).
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.Stories[V].Chapters.Single(c => c.Id == "verdict").Missions.Single(m => m.Id == "m_long_quiet").FeasibleIf = new List<string> { "world:creative" };
        var (server, t) = World("m_wrecks", TutorialOff, content);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 6);
        Settle(server, p);
        Assert.True(Has(p, Skipped("m_long_quiet")));
        Assert.Equal(new[] { "m_wrecks" }, server.RunningMissionsForTest(p));
        Assert.Equal("story.vega.m.wrecks.1.obj", server.ObjectiveKeyForTest("Pilot"));

        server.RaiseGameEventForTest(p, GameEventKeys.ScanAny, "asteroid"); // not a wreck
        Assert.False(Has(p, Done("m_wrecks")));
        server.RaiseGameEventForTest(p, GameEventKeys.ScanAny, "wreck");
        Assert.True(Has(p, Done("m_wrecks")));
        Assert.Contains("story.vega.m.wrecks.1.done", t.LineKeys(p));
    }

    [Fact]
    public void TheRadarAndTheJumpGenerator_AreResearchThenTheModule_ByEvidenceOrTheEvent()
    {
        var (server, t) = World("m_research", TutorialOff);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 9); // chapter 5, "What You Are"
        Settle(server, p);
        Assert.Equal(new[] { "m_radar" }, server.RunningMissionsForTest(p));
        Assert.Equal(("story.vega.obj.research", "Radar Array"), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveArgForTest("Pilot")));

        int milestones = server.StorySnapshot.Milestones;
        p.State.UnlockedBlueprints.Add("radar_array"); // researched on the side: the evidence books the step at the next pass
        server.TickStoryMissionsForTest(p);
        Assert.True(Has(p, StoryPackKeys.StepDone(V, "m_radar.1")));
        if (!Has(p, Done("m_radar")))
        {
            Assert.Contains("story.vega.m.radar.2.start", t.LineKeys(p));
            server.RaiseGameEventForTest(p, GameEventKeys.ModuleBuilt, "radar_array");
        }

        Assert.True(Has(p, Done("m_radar")));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // the mission paid the chapter's bonus (no lead had)

        AdvanceTo(server, 11); // chapter 6, "It Still Sleeps"
        Settle(server, p);
        Assert.Equal(new[] { "m_jump" }, server.RunningMissionsForTest(p));
        milestones = server.StorySnapshot.Milestones;
        server.RaiseGameEventForTest(p, GameEventKeys.ResearchAny, "jump_generator"); // the lead pays; the mission moves on
        Assert.True(Has(p, StoryPackKeys.Lead(V, "sleeps", 1)));
        Assert.True(Has(p, StoryPackKeys.StepDone(V, "m_jump.1")));
        server.RaiseGameEventForTest(p, GameEventKeys.ModuleBuilt, "jump_generator");
        Assert.True(Has(p, Done("m_jump")));
        Assert.Contains("story.vega.m.jump.2.done", t.LineKeys(p));
        Assert.Equal(milestones + 3, server.StorySnapshot.Milestones); // once
    }

    [Fact]
    public void TheEpilogue_BridgesTheFirstSurveyOrder_OrTheRelaysOnAWorldWithoutStations_AndPaysNoBonus()
    {
        var (server, t) = World("m_survey", TutorialOff);
        var p = Join(server, 1, "Pilot");
        server.AdminRevealFinale();
        server.MarkGuardianDefeatedForTest();
        Assert.Equal("resolution", server.TrackChapterIdForTest(p));
        Settle(server, p);
        Assert.Equal(new[] { "m_survey" }, server.RunningMissionsForTest(p));
        Assert.Equal(("story.vega.m.survey.1.obj", ObjectiveSources.Story), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));

        int milestones = server.StorySnapshot.Milestones;
        server.RaiseGameEventForTest(p, GameEventKeys.MissionTurnedIn, "relay_survey_1"); // the mission bridge
        Assert.True(Has(p, Done("m_survey")));
        Assert.Contains("story.vega.m.survey.1.done", t.LineKeys(p));
        Assert.Equal(milestones, server.StorySnapshot.Milestones); // after the last beat no bonus is paid

        var (dark, dt) = World("m_survey_dark", c => { TutorialOff(c); c.World = new WorldDescription { SpaceStations = Frequency.Off }; });
        var q = Join(dark, 1, "Pilot");
        dark.AdminRevealFinale();
        dark.MarkGuardianDefeatedForTest();
        Settle(dark, q);
        Assert.Equal("m_survey.1a", dark.MissionChipStepForTest(q)); // no station boards here: the relays stand in
        Assert.Equal("story.vega.obj.relay", dark.ObjectiveKeyForTest("Pilot"));
        dark.RaiseGameEventForTest(q, GameEventKeys.RelayCommissioned, "relay_1");
        Assert.True(Has(q, Done("m_survey")));
        Assert.Contains("story.vega.m.survey.1a.done", dt.LineKeys(q));
    }

    // ---------------- Stand-ins, letting go, beats ----------------

    [Fact]
    public void AStepWhoseWorldFactWentAway_GivesWayToItsStandIn()
    {
        var (server, t) = World("m_fact", c => { TutorialOff(c); c.Rules.StoryDensity = StoryDensity.Sparse; });
        var p = Join(server, 1, "Pilot");
        EnsureFragments(server);
        Settle(server, p);
        Assert.Equal("m_signal.1", server.MissionChipStepForTest(p));

        foreach (var fragment in Vega.Fragments)
        {
            server.RecordStoryFragmentForTest(fragment.Key); // someone else found them all
        }

        Assert.Equal("signature", server.TrackChapterIdForTest(p)); // (the sparse pace keeps chapter 1)
        server.TickStoryMissionsForTest(p);
        Assert.True(Has(p, StoryPackKeys.StepSkipped(V, "m_signal.1")));
        Assert.Equal("m_signal.1a", server.MissionChipStepForTest(p));
        Assert.Contains("story.vega.m.signal.1a.start", t.LineKeys(p));
        Assert.Equal("story.vega.obj.settlement_help", server.StoryObjectiveForTest(p).Key);
        var steps = Assert.Single(server.JournalMissionsForTest(p)).Steps;
        Assert.Equal(new[] { "m_signal.1a" }, steps.Select(s => s.Id)); // the skipped step is left out, its stand-in shows
    }

    [Fact]
    public void NoBeatWaitsForAMission_AndAnUnfinishedOneIsLetGoWhenTheChapterMovesOn()
    {
        var (server, t) = World("m_moveon", c => { TutorialOff(c); c.Rules.InstantTravel = true; });
        var p = Join(server, 1, "Pilot");
        EnsureFragments(server);
        Settle(server, p);
        Assert.Equal(new[] { "m_signal" }, server.RunningMissionsForTest(p));

        AdvanceTo(server, 2); // the score reveals the next chapter while the mission is still open — no beat waits
        Assert.Equal("service", server.TrackChapterIdForTest(p));
        int linesBefore = t.LineKeys(p).Count;
        server.TickStoryMissionsForTest(p);
        Assert.True(Has(p, Skipped("m_signal")));
        Assert.True(Has(p, StoryPackKeys.StepSkipped(V, "m_signal.1")));
        Assert.Empty(server.RunningMissionsForTest(p)); // chapter 2's scene first
        Assert.DoesNotContain(t.LineKeys(p).Skip(linesBefore), k => k.StartsWith("story.vega.m.signal", StringComparison.Ordinal));

        Settle(server, p);
        Assert.Equal(new[] { "m_observatory" }, server.RunningMissionsForTest(p));
        Assert.Equal(new[] { "m_observatory" }, server.JournalMissionsForTest(p).Select(m => m.Id)); // only this chapter's
    }

    // ---------------- The chip, the Journal, the skip ----------------

    [Fact]
    public void TheOpenFinale_KeepsItsChip_AndLetsAnUnfinishedMissionGo()
    {
        var (server, _) = World("m_finale", TutorialOff);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 11);
        Settle(server, p);
        Assert.Equal(new[] { "m_jump" }, server.RunningMissionsForTest(p));

        server.AdminRevealFinale(); // B12: the finale is open — the finale lead, never a mission (never muted)
        Assert.Equal((StoryLeads.FinaleJumpKey, ObjectiveSources.Finale), (server.ObjectiveKeyForTest("Pilot"), server.ObjectiveSourceForTest("Pilot")));
        server.TickStoryMissionsForTest(p);
        Assert.True(Has(p, Skipped("m_jump")));
        Assert.Empty(server.RunningMissionsForTest(p));
        Assert.Empty(server.JournalMissionsForTest(p)); // chapter 7 has none
    }

    [Fact]
    public void ThePinnedStory_ShowsItsMission_AndSkippingItInTheJournal_GivesTheChipBackToTheLeads()
    {
        var (server, _) = World("m_skip", TutorialOff);
        var p = Join(server, 1, "Pilot");
        AdvanceTo(server, 9);
        Settle(server, p);
        Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.Track });
        Assert.Equal("story.vega.obj.research", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal("m_radar.1", server.MissionChipStepForTest(p));

        // The Journal: the mission with its steps — the running one on the chip, the next one ahead; the wire carries them.
        var vega = server.StoryPackStatesForTest(p).Packs.Single(x => x.Id == V);
        var mission = Assert.Single(vega.Missions);
        Assert.Equal(new[] { StoryChapterStates.Active, StoryChapterStates.Upcoming }, mission.Steps.Select(s => s.State));
        Assert.Equal(new[] { "story.vega.obj.research", "story.vega.m.radar.2.obj" }, mission.Steps.Select(s => s.Key));
        Assert.Equal("Radar Array", mission.Steps[0].Arg);
        var decoded = (StoryPackStates)NetCodec.Decode(NetCodec.Encode(server.StoryPackStatesForTest(p)))!;
        Assert.Equal("m_radar", decoded.Packs.Single(x => x.Id == V).Missions.Single().Id);

        Deliver(server, p, new StoryPackIntent { PackId = V, Action = StoryPackActions.SkipChapter, ChapterId = "m_radar" });
        Assert.True(Has(p, Skipped("m_radar")));
        Assert.Empty(server.RunningMissionsForTest(p));
        Assert.Empty(server.JournalMissionsForTest(p));
        Assert.Null(server.MissionChipStepForTest(p));
        Assert.Equal("story.vega.obj.research", server.StoryObjectiveForTest(p).Key); // the chapter's lead is the compass again
        server.TickStoryMissionsForTest(p);
        Assert.Empty(server.RunningMissionsForTest(p)); // a skipped mission is not handed out again
    }

    [Fact]
    public void AnOlderClient_GetsNoMission_NoLine_AndKeepsTheStoryChipItKnows()
    {
        var (server, t) = World("m_old", TutorialOff);
        var old = Join(server, 1, "Old", packs: false);
        EnsureFragments(server);
        Assert.True(server.StoryTrackStarted(old));
        Settle(server, old);
        server.TickStoryMissionsForTest(old);
        Assert.Empty(server.RunningMissionsForTest(old));
        Assert.DoesNotContain(t.LineKeys(old), k => k.StartsWith("story.vega.m.", StringComparison.Ordinal));
        Assert.DoesNotContain(t.To<ShipAiLine>(old), l => !string.IsNullOrEmpty(l.Text) && l.PackId == V);
        Assert.StartsWith("story.obj.", server.ObjectiveKeyForTest("Old"), StringComparison.Ordinal);
        Assert.Empty(server.StoryMissionStatusForTest(old));
    }

    // ---------------- Events, conditions, validators, cost ----------------

    [Fact]
    public void TheReader_RaisesLoreRead_WithTheSiteKind_OnlyForANewText()
    {
        var (server, _) = World("m_lore", TutorialOff);
        var p = Join(server, 1, "Pilot");
        var listener = new RecordingGameEventListener();
        server.AddGameEventListener(GameEventKeys.LoreRead, listener);

        server.RevealLoreForTest("Pilot", "observatory");
        Assert.Equal(new[] { ("Pilot", "observatory") }, listener.Of(GameEventKeys.LoreRead).Select(e => (e.Player, e.Arg)));
        server.RevealLoreForTest("Pilot", "observatory"); // nothing new to read there
        Assert.Single(listener.Of(GameEventKeys.LoreRead));
        Assert.Contains(GameEventKeys.LoreRead, GameEventKeys.All);
    }

    [Fact]
    public void TheWorldPredicates_OfTheMissions_ReadTheWorldsOptionsAndTheSites()
    {
        var (server, _) = World("m_predicates", c => { TutorialOff(c); c.World = new WorldDescription { Vaults = Frequency.Off, Wrecks = Frequency.Off }; });
        var p = Join(server, 1, "Pilot");
        Assert.True(server.EvaluateStoryConditionForTest("world:fragments_left", p));
        Assert.True(server.EvaluateStoryConditionForTest("world:settlements", p));
        Assert.False(server.EvaluateStoryConditionForTest("world:vaults", p));
        Assert.False(server.EvaluateStoryConditionForTest("world:space_wrecks", p));

        foreach (var fragment in Vega.Fragments)
        {
            server.RecordStoryFragmentForTest(fragment.Key);
        }

        Assert.False(server.EvaluateStoryConditionForTest("world:fragments_left", p));
        string site = server.UniqueSiteBodyForTest("observatory")!;
        server.RecordUniqueSiteSkipForTest(site, "observatory");
        Assert.False(server.EvaluateStoryConditionForTest("world:observatory_reachable", p));
    }

    [Fact]
    public void TheValidators_ReportBrokenMissions_AndTheShippedPackValidatesClean()
    {
        string raw = File.ReadAllText(Path.Combine(TestPaths.DataDir(), "stories", V, "story.json"));
        Assert.DoesNotContain(StoryPackValidator.Validate(Vega, StoryPackValidationTests.Context(), raw), i => i.Severity == StoryPackIssueSeverity.Error);

        var broken = JsonSerializer.Deserialize<StoryDefinition>(raw, ContentLoader.JsonOptions)!;
        StoryStep Step(string id, string chip = "story.vega.m.signal.1.obj")
            => new() { Id = id, Objective = new StoryStepObjective { Event = GameEventKeys.ScanAny }, Explain = new StoryStepExplain { ChipKey = chip } };
        var signature = broken.Chapters.Single(c => c.Id == "signature");
        signature.Missions.Add(new StoryMission { Id = "service", TitleKey = "story.vega.m.signal.title", Steps = { Step("x.1") } });
        signature.Missions.Add(new StoryMission { Id = "m_alt", TitleKey = "story.vega.m.signal.title", Alternative = "m_nowhere", Steps = { Step("x.2") } });
        signature.Missions.Add(new StoryMission { Id = "m_empty", TitleKey = "story.vega.m.signal.title" });
        signature.Missions.Add(new StoryMission
        {
            Id = "m_bad",
            TitleKey = "story.vega.m.signal.title",
            FeasibleIf = { "lore:no_such_text" },
            Steps =
            {
                new StoryStep { Id = "x.3", Objective = new StoryStepObjective { Event = GameEventKeys.ScanAny } },
                new StoryStep { Id = "x.4", Objective = new StoryStepObjective { Type = StoryObjectiveTypes.Lead, Target = "explore" }, Explain = new StoryStepExplain { ChipKey = "story.vega.m.signal.1.obj" } },
                new StoryStep { Id = "x.5", Objective = new StoryStepObjective { Type = StoryObjectiveTypes.Lead, Target = "research:no_such_blueprint" }, Explain = new StoryStepExplain { ChipKey = "story.vega.m.signal.1.obj" } },
                new StoryStep { Id = "x.6", Kind = StoryStepKinds.Read, Explain = new StoryStepExplain { ChipKey = "story.vega.m.signal.1.obj" } },
                Step("x.7", chip: StoryLeads.FragmentOnKey),
            },
        });
        signature.Steps.Add(new StoryStep { Id = "x.8", Objective = new StoryStepObjective { Type = StoryObjectiveTypes.Lead, Target = "fragment" } });

        var errors = StoryPackValidator.Validate(broken, StoryPackValidationTests.Context())
            .Where(i => i.Severity == StoryPackIssueSeverity.Error).Select(i => i.Path + ": " + i.Message).ToList();
        Assert.Contains(errors, e => e.Contains("missions[1]") && e.Contains("a chapter's id"));
        Assert.Contains(errors, e => e.Contains("missions[2].alternative") && e.Contains("not another mission"));
        Assert.Contains(errors, e => e.Contains("missions[3].steps") && e.Contains("at least one step"));
        Assert.Contains(errors, e => e.Contains("missions[4].feasibleIf") && e.Contains("lore text 'no_such_text'"));
        Assert.Contains(errors, e => e.Contains("missions[4].steps[0].explain.chipKey") && e.Contains("needs a chip text"));
        Assert.Contains(errors, e => e.Contains("missions[4].steps[1].objective.target") && e.Contains("cannot be a mission step"));
        Assert.Contains(errors, e => e.Contains("missions[4].steps[2].objective.target") && e.Contains("blueprint 'no_such_blueprint'"));
        Assert.Contains(errors, e => e.Contains("missions[4].steps[3].kind") && e.Contains("kind 'do'"));
        Assert.Contains(errors, e => e.Contains("missions[4].steps[4].explain.chipKey") && e.Contains("{0}"));
        Assert.Contains(errors, e => e.Contains("chapters[1].steps[0].objective.type") && e.Contains("missions only"));

        // The runtime leaves a broken mission list out (logged) — the story itself still runs.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.Stories[V].Chapters.Single(c => c.Id == "signature").Missions[0].Alternative = "m_runes"; // another chapter's
        var (server, _) = World("m_broken", TutorialOff, content);
        Assert.Empty(server.StoryMissionIdsForTest);
        var p = Join(server, 1, "Pilot");
        Assert.True(server.StoryTrackStarted(p));
    }

    [Fact]
    public void MissionLeads_AreResolvedOnlyWhenSomethingRelevantChanged_NotEveryTick()
    {
        var (server, _) = World("m_cost", TutorialOff);
        var p = Join(server, 1, "Pilot");
        EnsureFragments(server);
        Settle(server, p);
        _ = server.ObjectiveKeyForTest("Pilot");
        int resolutions = server.MissionLeadResolutionsForTest;

        for (int i = 0; i < 30; i++)
        {
            _ = server.ObjectiveKeyForTest("Pilot");
            server.TickStoryMissionsForTest(p);
            server.TickStoryLeadsForTest(p);
            _ = server.JournalMissionsForTest(p);
        }

        Assert.Equal(resolutions, server.MissionLeadResolutionsForTest);
        p.CurrentLocationId = "test:void"; // the player moved: resolved once more — the step's own text then (no fragment there)
        Assert.Equal("story.vega.m.signal.1.obj", server.ObjectiveKeyForTest("Pilot"));
        _ = server.ObjectiveKeyForTest("Pilot");
        Assert.Equal(resolutions + 1, server.MissionLeadResolutionsForTest);
    }
}
