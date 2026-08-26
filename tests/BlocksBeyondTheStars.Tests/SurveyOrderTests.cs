// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
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

    private SvGameServer Start(string name, long seed, out SqliteWorldRepository repo, Action<ServerConfig>? tune = null)
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
        tune?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Puts a chain step straight into the log as already turned in — the chain bookkeeping a run of
    /// the orders would have left behind, without walking every objective.</summary>
    private static void TurnedIn(PlayerSession p, string missionId, string acceptedFrom = "")
        => p.State.Missions.Add(new MissionProgress
        {
            MissionId = missionId,
            ChainId = ChainId,
            Status = MissionStatus.TurnedIn,
            AcceptedFrom = acceptedFrom,
            ObjectiveProgress = { int.MaxValue },
        });

    /// <summary>Deploys + commissions a player station at the pilot's current location (core + hull + airlock,
    /// the minimum commissioning shape — mirrors RelayNetworkTests). Returns its id.</summary>
    private static string BuildCommissionedStation(SvGameServer server, PlayerSession pilot)
    {
        string playerId = pilot.State.PlayerId;
        if (!server.InSpace(playerId))
        {
            server.EnterSpace(playerId);
        }

        pilot.State.InEva = true;
        bool instant = pilot.State.InstantBuild;
        pilot.State.InstantBuild = true;

        server.DeployStationCoreForTest(playerId);
        string id = server.OwnedStationIdForTest(playerId)!;
        for (int i = 1; i <= 11; i++)
        {
            server.HandleStructureEditForTest(playerId,
                new StructureEditIntent { StructureId = id, X = i, Y = 0, Z = 0, Mine = false, ItemKey = "iron_wall" });
        }

        server.HandleStructureEditForTest(playerId,
            new StructureEditIntent { StructureId = id, X = 0, Y = 1, Z = 0, Mine = false, ItemKey = "door_slide" });

        pilot.State.InstantBuild = instant;
        return id;
    }

    /// <summary>Flies out and docks the first neutral station in the pilot's system (mirrors
    /// SpaceStationBoardingTests), leaving the player inside its interior.</summary>
    private static void BoardFirstStation(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(playerId, station.Id);
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

    [Fact]
    public void UnlinkedSystemTravel_IgnoresTheSystemOfTheStationTheOrderWasTakenAt()
    {
        // #1291: the orders are only takeable at a STATION board, where the player's location id is
        // "station:<id>" — a body lookup returns nothing for it, so the "not where you took it" exclusion
        // never fired and the very system the board sits in completed the step.
        var server = Start("survey_station_home", 1, out var repo, c =>
        {
            c.StartPlanet = "varied";
            c.Rules.FreeSpaceFlight = true;
        });
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            string stationId = BuildCommissionedStation(server, p);
            string stationLoc = "station:" + stationId;
            string stationSystem = server.SystemOfLocationForTest(stationLoc);
            Assert.NotEqual(string.Empty, stationSystem);
            Assert.Null(server.Galaxy.FindBody(stationLoc)); // …which is exactly why the body route could not work

            p.State.CurrentLocationId = stationLoc; // standing on the station board
            var def = Single("test_unlinked_station", MissionObjectiveType.Travel, MissionChains.TravelUnlinkedSystem, 1);
            def.ChainId = "test_survey";
            def.Step = 1;
            def.Surface = MissionChains.SurfaceRadio; // takeable anywhere — this is about the accept-time system
            server.AddMissionDefForTest(def);
            server.AcceptMission("Pilot", def.Id);

            var progress = p.State.Missions.First(m => m.MissionId == def.Id);
            Assert.Equal(stationLoc, progress.AcceptedBodyId);
            Assert.Equal(stationSystem, progress.AcceptedSystemId);

            var home = server.Galaxy.Systems.First(s => s.Id == stationSystem);
            var homeBody = home.Bodies.First(b => !string.IsNullOrEmpty(b.PlanetType));
            server.SimulateTravelForTest(p, homeBody.Id, homeBody.Name);
            Assert.Equal(0, progress.ObjectiveProgress[0]); // the board's own system proves nothing

            var elsewhere = server.Galaxy.AllBodies()
                .First(b => b.SystemId != stationSystem && !string.IsNullOrEmpty(b.PlanetType));
            server.SimulateTravelForTest(p, elsewhere.Id, elsewhere.Name);
            Assert.Equal(1, progress.ObjectiveProgress[0]);
        }
    }

    [Fact]
    public void AcceptedSystemId_SurvivesASnapshotRoundTrip()
    {
        var pr = new MissionProgress
        {
            MissionId = "relay_survey_2",
            ChainId = ChainId,
            AcceptedFrom = "station_4242",
            AcceptedBodyId = "station:st1",
            AcceptedSystemId = "sys7",
        };
        var back = StateMapper.FromSnapshot(StateMapper.ToSnapshot(
            new PlayerState { PlayerId = "pid", Name = "Surveyor", Missions = { pr } }));

        Assert.Equal("sys7", Assert.Single(back.Missions).AcceptedSystemId);
    }

    // ---------------- infeasible steps are skipped, never dead ends ----------------

    [Fact]
    public void SurveyChain_SkipsTheContributeStepWithNoRelayLeftToBuild_AndStillReachesTheLastStep()
    {
        var server = Start("survey_skip", 31415, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Surveyor");
            server.MarkGuardianDefeatedForTest();

            // Every relay in the galaxy is finished (a built-out network, or simply no boardable station at
            // all), so there is nothing left to pour into: step 3 could never finish here. Step 4 can — the
            // galaxy still has remnants to hunt.
            server.CompleteEveryRelayForTest();
            Assert.False(server.AnyRelayOpenForTest());
            Assert.False(server.ChainStepFeasibleForTest("relay_survey_3"));
            Assert.True(server.ChainStepFeasibleForTest("relay_survey_4"));

            TurnedIn(p, "relay_survey_1");
            TurnedIn(p, "relay_survey_2");

            // Step 3 stays hidden. Step 4's prerequisites now count as met — the only thing still missing is
            // the station board, which is what its reason says (before #1291 it read chain_locked forever).
            Assert.Equal("@srv.mission.chain_locked", server.ChainStepAvailableForTest("Surveyor", "relay_survey_3").Reason);
            Assert.Equal("@srv.mission.chain_board", server.ChainStepAvailableForTest("Surveyor", "relay_survey_4").Reason);

            // And the giver's follow-up call is about the step that CAN be handed out.
            Assert.Equal(new[] { "relay_survey_4" }, server.FeasibleNextStepsForTest("relay_survey_2").ToArray());
            Assert.Equal(new[] { "relay_survey_4" }, server.FeasibleNextStepsForTest("relay_survey_3").ToArray());
        }
    }

    [Fact]
    public void SurveyChain_WithHostilesOff_EndsAfterTheContributeStep_AndStillRestarts()
    {
        var server = Start("survey_peaceful", 27182, out var repo, c => c.Rules.PlanetEnemies = AlienActivity.Off);
        using (repo)
        {
            var p = server.AddLocalPlayer("Surveyor");
            server.MarkGuardianDefeatedForTest();
            Assert.False(server.ChainStepFeasibleForTest("relay_survey_4")); // nothing hostile to hunt here

            foreach (var id in new[] { "relay_survey_1", "relay_survey_2", "relay_survey_3" })
            {
                TurnedIn(p, id);
            }

            // Nothing feasible follows step 3, so its turn-in ends the run — and because the step this world
            // skips is the repeatable one, the chain starts over instead of leaving the boards quiet.
            Assert.Empty(server.FeasibleNextStepsForTest("relay_survey_3"));
            var step3 = server.MissionDefForTest("relay_survey_3")!;
            server.ChainStepTurnedInForTest(p, step3, p.State.Missions.First(m => m.MissionId == step3.Id));
            Assert.DoesNotContain(p.State.Missions, m => m.ChainId == ChainId);
        }
    }

    [Fact]
    public void SettlementChain_KeepsItsStepFourAlternatives()
    {
        // The skip rule is generic, so it must not disturb the authored 4a/4b pair: they are ALTERNATIVES of
        // one step (nothing depends on them), and exactly one of them is offered — never both, never neither.
        var server = Start("survey_alt", 999, out var repo);
        using (repo)
        {
            Assert.Empty(server.FeasibleNextStepsForTest("chain_needs_4a_camp"));
            Assert.Empty(server.FeasibleNextStepsForTest("chain_needs_4b_travel"));

            var next = server.FeasibleNextStepsForTest("chain_needs_3");
            Assert.NotEmpty(next);
            Assert.All(next, id => Assert.StartsWith("chain_needs_4", id));
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
    public void AfterTheWin_AStationBoardOffersTheOrders_AndTheLastTurnInRestartsTheChain()
    {
        // The whole promise of #1213 through the REAL paths: dock a station, see the first order on its
        // board, and hand in the last step through HandleTurnInMission so the chain starts over.
        var server = Start("survey_board", 42, out var repo, c =>
        {
            c.StartPlanet = "varied";
            c.World = new WorldDescription { SpaceStations = Frequency.Frequent };
            c.Rules.FreeSpaceFlight = true;
        });
        using (repo)
        {
            var p = server.AddLocalPlayer("Surveyor");
            server.MarkGuardianDefeatedForTest();
            BoardFirstStation(server, "Surveyor");
            p.State.Position = server.SpaceStationMarkers.First(m => m.Type == "mission_board").Pos;

            string place = server.CurrentPlaceKeyForTest("Surveyor");
            Assert.StartsWith("station_", place);
            Assert.Contains("relay_survey_1", server.VisibleMissionIdsForTest("Surveyor"));

            // Walk the bookkeeping to the last step, then run it for real.
            foreach (var id in new[] { "relay_survey_1", "relay_survey_2", "relay_survey_3" })
            {
                TurnedIn(p, id, place);
            }

            server.AcceptMission("Surveyor", "relay_survey_4");
            Assert.Equal(MissionStatus.Active, p.State.Missions.First(m => m.MissionId == "relay_survey_4").Status);

            var last = server.MissionDefForTest("relay_survey_4")!;
            for (int i = 0; i < last.Objectives[0].Required; i++)
            {
                server.SimulateMachineDefeatForTest(p);
            }

            server.TurnInMission("Surveyor", "relay_survey_4");

            Assert.DoesNotContain(p.State.Missions, m => m.ChainId == ChainId);            // the chain restarted…
            Assert.Contains("relay_survey_1", server.VisibleMissionIdsForTest("Surveyor")); // …and the board offers it again
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
