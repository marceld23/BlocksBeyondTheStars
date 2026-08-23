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
/// "SPS Survey Orders" (#1213): the post-finale chain on station boards. Covers the story gate that hides the
/// whole chain until the Guardian is down, the two new objective mechanics (Contribute, fed by relay
/// contributions; Travel to an unlinked system), the machine-Defeat hook that only counts remnants, and the
/// repeatable chain that restarts itself so the station boards keep something to offer after the ending.
/// </summary>
public sealed class SurveyOrderTests : IDisposable
{
    private const string ChainId = "relay_survey";

    private readonly string _root;
    private readonly GameContent _content;

    public SurveyOrderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_survey_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(string name, long seed, out SqliteWorldRepository repo)
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
            PlaceSettlements = false,
            PlaceWrecks = false,
            DataDir = TestPaths.DataDir(),
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>An ad-hoc stand-alone mission (no ChainId → AcceptMission does not run the chain rule), used to
    /// exercise ONE objective mechanic in isolation.</summary>
    private static MissionDefinition Single(string id, MissionObjectiveType type, string target, int required) => new()
    {
        Id = id,
        Source = MissionSource.System,
        NameKey = "mission.chain.relay_survey_1.title",
        DescriptionKey = "mission.chain.relay_survey_1.desc",
        Objectives = { new MissionObjective { Type = type, Target = target, Required = required } },
        Rewards = { new ItemAmount("data_fragment", 1) },
        Active = true,
    };

    // ---------------- content ----------------

    [Fact]
    public void SurveyChain_LoadsValidatesAndIsShapedAsFourStepsOnStationBoards()
    {
        _content.Validate(); // requiresStory, prerequisites, nextMissionId and the objective targets all resolve

        var steps = _content.Missions.Values.Where(m => m.ChainId == ChainId).OrderBy(m => m.Step).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, steps.Select(s => s.Step).ToArray());
        Assert.All(steps, s => Assert.Equal(MissionChains.OfferAtStation, s.OfferAt));
        Assert.All(steps, s => Assert.Equal(MissionChains.StoryGuardianDefeated, s.RequiresStory));

        Assert.Equal(MissionObjectiveType.Scan, steps[0].Objectives[0].Type);
        Assert.Equal("anomaly", steps[0].Objectives[0].Target);
        Assert.Equal(MissionChains.TravelUnlinkedSystem, steps[1].Objectives[0].Target);
        Assert.Equal(MissionObjectiveType.Contribute, steps[2].Objectives[0].Type);
        Assert.Equal(MissionObjectiveType.Defeat, steps[3].Objectives[0].Type);

        // The Contribute step must ask for something that is actually on the relay's bill of materials.
        Assert.NotNull(_content.Relay);
        Assert.Contains(_content.Relay!.Costs, c => c.Item == steps[2].Objectives[0].Target);

        // Only the LAST step is repeatable — that is what restarts the whole chain.
        Assert.Equal(new[] { false, false, false, true }, steps.Select(s => s.Repeatable).ToArray());
        Assert.Empty(steps[3].NextMissionId);
    }

    [Fact]
    public void RequiresStory_AcceptsOnlyTheKnownGate()
    {
        Assert.True(MissionChains.IsValidRequiresStory(""));
        Assert.True(MissionChains.IsValidRequiresStory(null));
        Assert.True(MissionChains.IsValidRequiresStory(MissionChains.StoryGuardianDefeated));
        Assert.False(MissionChains.IsValidRequiresStory("guardian_awake"));
    }

    // ---------------- the story gate ----------------

    [Fact]
    public void SurveyChain_IsInvisibleUntilTheGuardianIsDefeated()
    {
        var server = Start("survey_gate", 90210, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Surveyor");

            Assert.DoesNotContain("relay_survey_1", server.VisibleMissionIdsForTest("Surveyor"));
            var before = server.ChainStepAvailableForTest("Surveyor", "relay_survey_1");
            Assert.False(before.Ok);

            server.MarkGuardianDefeatedForTest();

            // The story gate no longer speaks. The step is still not takeable here — the player is standing in
            // the wild, not at a station board — but the REASON has moved on, which is what the gate controls.
            var after = server.ChainStepAvailableForTest("Surveyor", "relay_survey_1");
            Assert.False(after.Ok);
            Assert.Equal("@srv.mission.chain_board", after.Reason);
            Assert.NotEqual(before.Reason, after.Reason);
        }
    }

    // ---------------- Contribute ----------------

    [Fact]
    public void RelayContribution_AdvancesAContributeObjective_AndStopsAtTheRequirement()
    {
        var server = Start("survey_contribute", 4711, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Engineer");
            var def = Single("test_contribute", MissionObjectiveType.Contribute, "circuit_board", 10);
            server.AddMissionDefForTest(def);
            server.AcceptMission("Engineer", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            server.SimulateRelayContributionForTest(p, "circuit_board", 4);
            Assert.Equal(4, progress.ObjectiveProgress[0]);

            server.SimulateRelayContributionForTest(p, "titanium_plate", 50); // a different BOM line does not count
            Assert.Equal(4, progress.ObjectiveProgress[0]);

            server.SimulateRelayContributionForTest(p, "circuit_board", 99); // clamps at the requirement
            Assert.Equal(10, progress.ObjectiveProgress[0]);
        }
    }

    // ---------------- Travel to an unlinked system ----------------

    [Fact]
    public void UnlinkedSystemTravel_CountsAnArrivalOutsideTheRelayNetwork()
    {
        var server = Start("survey_travel", 20260823, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var def = Single("test_unlinked", MissionObjectiveType.Travel, MissionChains.TravelUnlinkedSystem, 1);
            server.AddMissionDefForTest(def);
            server.AcceptMission("Pilot", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            // No relay has been commissioned in this world, so every system is still unlinked.
            var body = server.Galaxy.AllBodies().First();
            server.SimulateTravelForTest(p, body.Id, body.Name);
            Assert.Equal(1, progress.ObjectiveProgress[0]);
        }
    }

    [Fact]
    public void UnlinkedSystemTravel_IgnoresAnArrivalInTheSystemTheJobWasTakenIn()
    {
        var server = Start("survey_travel_home", 20260824, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            var def = Single("test_unlinked_home", MissionObjectiveType.Travel, MissionChains.TravelUnlinkedSystem, 1);
            server.AddMissionDefForTest(def);
            server.AcceptMission("Pilot", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            // Pretend the order was taken at a station in this very system: arriving there proves nothing.
            var home = server.Galaxy.Systems.First();
            var body = home.Bodies.First();
            progress.AcceptedBodyId = body.Id;

            server.SimulateTravelForTest(p, body.Id, body.Name);
            Assert.Equal(0, progress.ObjectiveProgress[0]);

            var elsewhere = server.Galaxy.AllBodies().First(b => b.SystemId != home.Id);
            server.SimulateTravelForTest(p, elsewhere.Id, elsewhere.Name);
            Assert.Equal(1, progress.ObjectiveProgress[0]);
        }
    }

    // ---------------- machine Defeat is post-win only ----------------

    [Fact]
    public void MachineDefeat_CountsOnlyAfterTheGuardianIsDown()
    {
        var server = Start("survey_defeat", 1357, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            var def = Single("test_machines", MissionObjectiveType.Defeat, "machine", 3);
            server.AddMissionDefForTest(def);
            server.AcceptMission("Ranger", def.Id);
            var progress = p.State.Missions.First(m => m.MissionId == def.Id);

            // Pre-win those very kills drive the story's own pacing — an order must not double-dip on them.
            server.SimulateMachineDefeatForTest(p);
            server.SimulateMachineDefeatForTest(p);
            Assert.Equal(0, progress.ObjectiveProgress[0]);

            server.MarkGuardianDefeatedForTest();

            server.SimulateMachineDefeatForTest(p);
            server.SimulateMachineDefeatForTest(p);
            Assert.Equal(2, progress.ObjectiveProgress[0]);
        }
    }

    // ---------------- the chain restarts ----------------

    [Fact]
    public void TurningInTheLastStep_RestartsTheWholeChain()
    {
        var server = Start("survey_restart", 24680, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Surveyor");
            server.MarkGuardianDefeatedForTest();

            // Walk the chain's bookkeeping by hand: every step turned in, as if the orders had been run.
            foreach (var step in _content.Missions.Values.Where(m => m.ChainId == ChainId).OrderBy(m => m.Step))
            {
                p.State.Missions.Add(new MissionProgress
                {
                    MissionId = step.Id,
                    ChainId = ChainId,
                    Status = MissionStatus.TurnedIn,
                    ObjectiveProgress = { step.Objectives[0].Required },
                });
            }

            var last = _content.Missions.Values.First(m => m.ChainId == ChainId && m.Step == 4);
            Assert.True(last.Repeatable);

            server.RestartChainIfRepeatableForTest(p, last);

            // Every row of the chain is gone, so step 1 is a fresh offer again — and no OTHER chain was touched.
            Assert.DoesNotContain(p.State.Missions, m => m.ChainId == ChainId);
            Assert.True(server.ChainStepAvailableForTest("Surveyor", "relay_survey_1").Reason
                        != "@srv.mission.accepted");
        }
    }

    [Fact]
    public void RestartingIgnoresANonRepeatableChain()
    {
        var server = Start("survey_restart_no", 13579, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Helper");
            var step1 = _content.Missions.Values.First(m => m.ChainId == "settlement_needs" && m.Step == 1);
            p.State.Missions.Add(new MissionProgress
            {
                MissionId = step1.Id,
                ChainId = "settlement_needs",
                Status = MissionStatus.TurnedIn,
            });

            server.RestartChainIfRepeatableForTest(p, step1); // repeatable: false → nothing happens
            Assert.Contains(p.State.Missions, m => m.MissionId == step1.Id);
        }
    }
}
