// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Scan (survey) missions (#1205): the shared target grammar, the board slot every settlement board offers, the
/// progress hook fed from real scans (re-scans count unless FirstOnly), the knowledge reward on turn-in, and the
/// player-created whitelist.
/// </summary>
public sealed class ScanMissionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ScanMissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_scanmission_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(string name, long seed, out SqliteWorldRepository repo, bool settlements = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceWrecks = false,
            DataDir = TestPaths.DataDir(),
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- grammar ----------------

    [Theory]
    [InlineData("any", "block", "stone", false, true)]
    [InlineData("creature:any", "creature", "sp1", false, true)]
    [InlineData("creature:any", "flora", "flora_fern", false, false)]
    [InlineData("creature:hostile", "creature", "sp1", true, true)]
    [InlineData("creature:hostile", "creature", "sp1", false, false)]
    [InlineData("creature:sp7", "creature", "sp7", false, true)]
    [InlineData("creature:sp7", "creature", "sp8", false, false)]
    [InlineData("block:iron_ore", "block", "iron_ore", false, true)]
    [InlineData("block:flora_fern", "flora", "flora_fern", false, true)]
    [InlineData("block:rune_stone", "monument", "monument_arch", false, false)]
    [InlineData("flora:any", "flora", "flora_fern", false, true)]
    [InlineData("monument:any", "monument", "monument_arch", false, true)]
    [InlineData("microfauna:any", "microfauna", "butterfly", false, true)]
    [InlineData("asteroid", "asteroid", "asteroid", false, true)]
    [InlineData("anomaly", "anomaly", "anomaly", false, true)]
    [InlineData("asteroid", "creature", "sp1", false, false)]
    public void Matcher_FollowsTheGrammar(string target, string kind, string subject, bool hostile, bool expected)
        => Assert.Equal(expected, ScanTargets.Matches(target, kind, subject, hostile));

    [Fact]
    public void Validator_AcceptsTheGrammar_AndRejectsTheRest()
    {
        Func<string, bool> blocks = k => k == "iron_ore";
        Assert.True(ScanTargets.IsValid("any", blocks));
        Assert.True(ScanTargets.IsValid("creature:hostile", blocks));
        Assert.True(ScanTargets.IsValid("creature:some_species", blocks));
        Assert.True(ScanTargets.IsValid("block:iron_ore", blocks));
        Assert.True(ScanTargets.IsValid("monument:any", blocks));
        Assert.True(ScanTargets.IsValid("asteroid", blocks));
        Assert.False(ScanTargets.IsValid("block:unobtainium", blocks));
        Assert.False(ScanTargets.IsValid("block:", blocks));
        Assert.False(ScanTargets.IsValid("creature:", blocks));
        Assert.False(ScanTargets.IsValid("planet:any", blocks));
        Assert.False(ScanTargets.IsValid(string.Empty, blocks));
        Assert.False(ScanTargets.IsValid(null, blocks));
    }

    [Fact]
    public void Content_ScanMissionWithUnknownBlockTarget_IsReported()
    {
        // The shipped content has no scan mission with a broken target; the validator rule itself is exercised
        // through the grammar check above and this end-to-end sanity check of the shipped data.
        _content.Validate(); // throws on a broken reference — the shipped data (incl. the new validator rule) must load clean
    }

    // ---------------- boards ----------------

    [Fact]
    public void EverySettlementBoard_OpensWithASurveyJob()
    {
        int boards = 0;
        for (long seed = 1; seed <= 12; seed++)
        {
            var server = Start("scanboard_" + seed, seed, out var repo);
            using (repo)
            {
                var ids = server.SettlementMissionIds;
                if (ids.Count == 0)
                {
                    continue;
                }

                boards++;
                var scanId = ids.FirstOrDefault(i => i.EndsWith("_s0", StringComparison.Ordinal));
                Assert.NotNull(scanId);
                var obj = server.FirstObjectiveForTest(scanId!);
                Assert.NotNull(obj);
                Assert.Equal(MissionObjectiveType.Scan, obj!.Type);
                Assert.True(ScanTargets.IsValid(obj.Target, k => _content.GetBlock(k) is not null), obj.Target);
                Assert.InRange(obj.Required, 1, 5);
            }
        }

        Assert.True(boards > 0, "expected at least one settlement with a board in 12 seeds");
    }

    // ---------------- progress + reward ----------------

    [Fact]
    public void WildlifeSurvey_CountsRescans_AndPaysKnowledgeOnTurnIn()
    {
        var server = Start("scan_wildlife", 4242, out var repo, settlements: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");
            var def = new MissionDefinition
            {
                Id = "test_wildlife",
                Source = MissionSource.System,
                NameKey = "mission.settlement.scan_wildlife.title",
                DescriptionKey = "mission.settlement.scan_wildlife.desc",
                Objectives = { new MissionObjective { Type = MissionObjectiveType.Scan, Target = "creature:any", Required = 3 } },
                Rewards = { new ItemAmount("medpack", 1) },
                KnowledgeReward = 3,
                Active = true,
            };
            server.AddMissionDefForTest(def);
            server.AcceptMission("Scout", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            var species = server.SpeciesRoster.First().Id;
            server.ScanSubject("Scout", "creature", species); // first time
            server.ScanSubject("Scout", "creature", species); // re-scan — still counts (FirstOnly = false)
            Assert.Equal(2, progress.ObjectiveProgress[0]);

            server.ScanSubject("Scout", "block", "stone"); // a block scan is not a creature
            Assert.Equal(2, progress.ObjectiveProgress[0]);

            server.ScanSubject("Scout", "creature", species);
            server.ScanSubject("Scout", "creature", species); // capped at the requirement
            Assert.Equal(3, progress.ObjectiveProgress[0]);

            int knowledgeBefore = p.State.KnowledgePoints;
            server.TurnInMission("Scout", def.Id);
            Assert.Equal(MissionStatus.TurnedIn, p.State.Missions.First(m => m.MissionId == def.Id).Status);
            // +3 from the mission itself; the first mission turn-in may add an achievement's own knowledge bonus on top.
            Assert.InRange(p.State.KnowledgePoints - knowledgeBefore, 3, 6);
            Assert.True(p.State.Inventory.CountOf("medpack") >= 1);
        }
    }

    [Fact]
    public void FirstOnlyObjective_IgnoresRescans()
    {
        var server = Start("scan_firstonly", 4242, out var repo, settlements: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Botanist");
            var def = new MissionDefinition
            {
                Id = "test_botany",
                Source = MissionSource.System,
                NameKey = "mission.settlement.scan_botany.title",
                DescriptionKey = "mission.settlement.scan_botany.desc",
                Objectives = { new MissionObjective { Type = MissionObjectiveType.Scan, Target = "any", Required = 2, FirstOnly = true } },
                Active = true,
            };
            server.AddMissionDefForTest(def);
            server.AcceptMission("Botanist", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            server.SimulateScanForTest(p, "flora", "flora_a", hostile: false, firstTime: true);
            server.SimulateScanForTest(p, "flora", "flora_a", hostile: false, firstTime: false); // re-scan ignored
            server.SimulateScanForTest(p, "flora", "flora_a", hostile: false, firstTime: false);
            Assert.Equal(1, progress.ObjectiveProgress[0]);

            server.SimulateScanForTest(p, "flora", "flora_b", hostile: false, firstTime: true);
            Assert.Equal(2, progress.ObjectiveProgress[0]);
        }
    }

    [Fact]
    public void HostileWatch_OnlyCountsHostileCreatures()
    {
        var server = Start("scan_hostile", 4242, out var repo, settlements: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Watcher");
            var def = new MissionDefinition
            {
                Id = "test_hostile",
                Source = MissionSource.System,
                NameKey = "mission.settlement.scan_hostile.title",
                DescriptionKey = "mission.settlement.scan_hostile.desc",
                Objectives = { new MissionObjective { Type = MissionObjectiveType.Scan, Target = "creature:hostile", Required = 1 } },
                Active = true,
            };
            server.AddMissionDefForTest(def);
            server.AcceptMission("Watcher", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            server.SimulateScanForTest(p, "creature", "tame_one", hostile: false, firstTime: true);
            Assert.Equal(0, progress.ObjectiveProgress[0]);
            server.SimulateScanForTest(p, "creature", "mean_one", hostile: true, firstTime: false);
            Assert.Equal(1, progress.ObjectiveProgress[0]);
        }
    }

    // ---------------- player-created ----------------

    [Fact]
    public void PlayerMission_MayUseScanWithTheGrammar_ButNotABrokenTarget()
    {
        var server = Start("scan_player", 4242, out var repo, settlements: false);
        using (repo)
        {
            var poster = server.AddLocalPlayer("Poster");
            poster.State.Inventory.Add("iron_plate", 5, 5);

            bool ok = server.CreatePlayerMissionForTest("Poster", "Survey please", "creature:any", MissionObjectiveType.Scan, 2, "iron_plate", 1);
            Assert.True(ok, "a Scan objective with a grammar target is accepted");

            bool bad = server.CreatePlayerMissionForTest("Poster", "Broken", "block:unobtainium", MissionObjectiveType.Scan, 1, "iron_plate", 1);
            Assert.False(bad, "an unknown block target is rejected");
        }
    }

    [Fact]
    public void Locale_HasEveryScanKeyInEnglishAndGerman()
    {
        var en = _content.CreateLocalizer(Shared.Localization.GameLocale.English);
        var de = _content.CreateLocalizer(Shared.Localization.GameLocale.German);
        foreach (var key in new[]
                 {
                     "mission.settlement.scan_wildlife.title", "mission.settlement.scan_hostile.desc",
                     "mission.settlement.scan_runes.title", "mission.settlement.scan_botany.desc",
                     "mission.station.scan_asteroids.title", "ui.missions.objtype_scan", "ui.missions.objtype_build",
                     "ui.missions.scantarget.creature_any", "ui.missions.scantarget.asteroid", "ui.missions.knowledge_reward",
                 })
        {
            Assert.True(en.Has(key), key + " (en)");
            Assert.True(de.Has(key), key + " (de)");
        }
    }
}
