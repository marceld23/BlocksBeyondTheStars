// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Mission chains (#1212): the authored "settlement needs" chain at a settlement board (hidden → visible after the
/// prerequisite, server-side rejection, bound to its board, the camp/travel alternative), the vendor friendship chain
/// handed out in conversation, the procedural big order after three turn-ins, the radio nudge, and the persistence
/// of the chain fields across a snapshot and a restart.
/// </summary>
public sealed class MissionChainTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MissionChainTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_chains_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ServerConfig Config(string name, long seed, bool settlements = true, bool banditCamps = true) => new()
    {
        WorldName = name,
        Seed = seed,
        StartPlanet = "jungle",
        AutoSaveIntervalMinutes = 9999,
        PlaceStarterShip = false,
        PlaceSettlements = settlements,
        PlaceWrecks = false,
        PlaceBanditCamps = banditCamps,
        AiLevel = AiLevel.Off,
        DataDir = TestPaths.DataDir(),
    };

    private SvGameServer Start(string name, long seed, out SqliteWorldRepository repo, out LoopbackLink link, bool settlements = true, bool banditCamps = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        link = new LoopbackLink();
        var server = new SvGameServer(Config(name, seed, settlements, banditCamps), _content, new LoopbackServerTransport(link), repo);
        server.Start();
        return server;
    }

    /// <summary>A world whose (inhabited) settlement carries a mission board, plus that board's position.</summary>
    private SvGameServer StartedWithBoard(string tag, out SqliteWorldRepository repo, out LoopbackLink link, out long seed)
    {
        for (seed = 1; seed <= 60; seed++)
        {
            var server = Start($"{tag}_{seed}", seed, out repo, out link);
            if (server.SettlementMissionIds.Count > 0 && !server.SettlementRuined
                && server.SettlementMarkers.Any(m => m.Type == "mission_board"))
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No settlement with a mission board found across 60 seeds.");
    }

    private static Vector3f BoardPos(SvGameServer server) => server.SettlementMarkers.First(m => m.Type == "mission_board").Pos;

    private static MissionProgress Progress(BlocksBeyondTheStars.GameServer.PlayerSession p, string id)
        => p.State.Missions.First(m => m.MissionId == id);

    // ---------------- content ----------------

    [Fact]
    public void AuthoredChains_LoadAndValidate()
    {
        _content.Validate(); // chain ids, prerequisites, giver roles, dialogue mission consequences all resolve

        var needs = _content.Missions.Values.Where(m => m.ChainId == "settlement_needs").OrderBy(m => m.Step).ThenBy(m => m.Id).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 4 }, needs.Select(m => m.Step).ToArray()); // step 4 has two alternatives
        Assert.Equal("chain_needs_1", needs[1].Prerequisites.Single());
        Assert.Equal(MissionObjectiveType.Defeat, needs[3].Objectives[0].Type);
        Assert.Equal(MissionChains.TravelOtherBody, needs[4].Objectives[0].Target);

        var favours = _content.Missions.Values.Where(m => m.ChainId == "vendor_favours").OrderBy(m => m.Step).ToList();
        Assert.Equal(3, favours.Count);
        Assert.All(favours, f => Assert.Equal(MissionChains.SurfaceDialog, f.Surface));
        Assert.Equal(MissionChains.StageTrusted, favours[2].MinStage);

        // Every favour dialogue hands out exactly its step.
        var dialogs = _content.Dialogs.Where(d => d.Key.StartsWith("vendor_favour_", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, dialogs.Count);
        Assert.Contains(dialogs[0].Nodes[0].Choices, c => c.Consequence == "mission:chain_vendor_1");
    }

    [Fact]
    public void Validator_RejectsDanglingChainReferences()
    {
        Assert.True(MissionChains.IsValidGiverRole(""));
        Assert.True(MissionChains.IsValidGiverRole("vendor"));
        Assert.True(MissionChains.IsValidGiverRole("character:elder_maren"));
        Assert.False(MissionChains.IsValidGiverRole("mayor"));
        Assert.False(MissionChains.IsValidGiverRole("character:"));
        Assert.True(MissionChains.IsValidStage("trusted"));
        Assert.False(MissionChains.IsValidStage("bestie"));
        Assert.True(MissionChains.IsValidSurface("dialog"));
        Assert.False(MissionChains.IsValidSurface("billboard"));
        Assert.True(MissionChains.IsValidOfferAt("station"));
        Assert.False(MissionChains.IsValidOfferAt("moon"));
        Assert.Equal(2, MissionChains.StageRank("trusted"));
        Assert.Equal(0, MissionChains.StageRank(null));
    }

    // ---------------- settlement needs chain at the board ----------------

    [Fact]
    public void SettlementChain_StepTwoIsHiddenUntilStepOneIsTurnedIn_AndRejectedServerSide()
    {
        var server = StartedWithBoard("needs", out var repo, out _, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hero");
            p.State.Position = BoardPos(server);

            var visible = server.VisibleMissionIdsForTest("Hero");
            Assert.Contains("chain_needs_1", visible);
            Assert.DoesNotContain("chain_needs_2", visible);

            // Skipping ahead is refused by the server, not just hidden by the list.
            server.AcceptMission("Hero", "chain_needs_2");
            Assert.DoesNotContain(p.State.Missions, m => m.MissionId == "chain_needs_2");
            Assert.Equal((false, "@srv.mission.chain_locked"), server.ChainStepAvailableForTest("Hero", "chain_needs_2"));

            // Step 1: take it at the board, deliver the iron, turn in.
            server.AcceptMission("Hero", "chain_needs_1");
            var pr = Progress(p, "chain_needs_1");
            Assert.Equal("settlement_needs", pr.ChainId);
            Assert.Equal(server.CurrentPlaceKeyForTest("Hero"), pr.AcceptedFrom);
            Assert.StartsWith("settle_", pr.AcceptedFrom);
            Assert.False(string.IsNullOrEmpty(pr.AcceptedBodyId));

            p.State.Inventory.Add("iron_ore", 8, 99);
            server.TurnInMission("Hero", "chain_needs_1");
            Assert.Equal(MissionStatus.TurnedIn, Progress(p, "chain_needs_1").Status);

            visible = server.VisibleMissionIdsForTest("Hero");
            Assert.DoesNotContain("chain_needs_1", visible);
            Assert.Contains("chain_needs_2", visible);
            Assert.DoesNotContain("chain_needs_3", visible);
            Assert.True(server.ChainStepAvailableForTest("Hero", "chain_needs_2").Ok);
        }
    }

    [Fact]
    public void SettlementChain_IsBoundToTheBoardItStartedAt()
    {
        var server = StartedWithBoard("bound", out var repo, out _, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hero");
            p.State.Position = BoardPos(server);
            server.AcceptMission("Hero", "chain_needs_1");
            p.State.Inventory.Add("iron_ore", 8, 99);
            server.TurnInMission("Hero", "chain_needs_1");

            // Away from any board the next step is neither listed nor acceptable; back at the board it is.
            p.State.Position = new Vector3f(p.State.Position.X, p.State.Position.Y + 500f, p.State.Position.Z);
            Assert.DoesNotContain("chain_needs_2", server.VisibleMissionIdsForTest("Hero"));
            Assert.Equal("@srv.mission.chain_board", server.ChainStepAvailableForTest("Hero", "chain_needs_2").Reason);
            server.AcceptMission("Hero", "chain_needs_2");
            Assert.DoesNotContain(p.State.Missions, m => m.MissionId == "chain_needs_2");

            p.State.Position = BoardPos(server);
            server.AcceptMission("Hero", "chain_needs_2");
            Assert.Equal(MissionStatus.Active, Progress(p, "chain_needs_2").Status);

            // Turning the step in needs the board of the chain's place too.
            server.SimulatePlaceForTest(p, "light_white", new Vector3i(5, 70, 5));
            server.SimulatePlaceForTest(p, "light_white", new Vector3i(6, 70, 5));
            server.SimulatePlaceForTest(p, "light_white", new Vector3i(7, 70, 5));
            server.SimulatePlaceForTest(p, "light_white", new Vector3i(8, 70, 5));
            Assert.Equal(4, Progress(p, "chain_needs_2").ObjectiveProgress[0]);

            p.State.Position = new Vector3f(p.State.Position.X, p.State.Position.Y + 500f, p.State.Position.Z);
            server.TurnInMission("Hero", "chain_needs_2");
            Assert.Equal(MissionStatus.Active, Progress(p, "chain_needs_2").Status);
            p.State.Position = BoardPos(server);
            server.TurnInMission("Hero", "chain_needs_2");
            Assert.Equal(MissionStatus.TurnedIn, Progress(p, "chain_needs_2").Status);
        }
    }

    [Fact]
    public void SettlementChain_ProgressIsPerPlayer()
    {
        var server = StartedWithBoard("perplayer", out var repo, out _, out _);
        using (repo)
        {
            var a = server.AddLocalPlayer("Ada");
            var b = server.AddLocalPlayer("Ben");
            a.State.Position = BoardPos(server);
            b.State.Position = BoardPos(server);

            server.AcceptMission("Ada", "chain_needs_1");
            a.State.Inventory.Add("iron_ore", 8, 99);
            server.TurnInMission("Ada", "chain_needs_1");

            Assert.Contains("chain_needs_2", server.VisibleMissionIdsForTest("Ada"));
            Assert.Contains("chain_needs_1", server.VisibleMissionIdsForTest("Ben"));
            Assert.DoesNotContain("chain_needs_2", server.VisibleMissionIdsForTest("Ben"));
            Assert.Empty(b.State.Missions);
        }
    }

    [Fact]
    public void SettlementChain_StepFour_IsTheCampOnBanditWorlds_AndTheTravelElsewhere()
    {
        var server = StartedWithBoard("alt", out var repo, out _, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hero");
            p.State.Position = BoardPos(server);
            server.AcceptMission("Hero", "chain_needs_1");
            p.State.Inventory.Add("iron_ore", 8, 99);
            server.TurnInMission("Hero", "chain_needs_1");
            server.AcceptMission("Hero", "chain_needs_2");
            for (int i = 0; i < 4; i++)
            {
                server.SimulatePlaceForTest(p, "light_white", new Vector3i(10 + i, 70, 5));
            }

            server.TurnInMission("Hero", "chain_needs_2");
            server.AcceptMission("Hero", "chain_needs_3");
            var species = server.SpeciesRoster.First().Id;
            server.SimulateScanForTest(p, "creature", species, hostile: false, firstTime: true);
            server.SimulateScanForTest(p, "creature", species, hostile: false, firstTime: false);
            server.TurnInMission("Hero", "chain_needs_3");
            Assert.Equal(MissionStatus.TurnedIn, Progress(p, "chain_needs_3").Status);

            // Exactly ONE alternative of step 4 is on offer: the camp on a world with an uncleared camp, the
            // travel step elsewhere (worldgen may or may not have stamped a camp on this seed).
            var visible = server.VisibleMissionIdsForTest("Hero");
            bool campOffered = visible.Contains("chain_needs_4a_camp");
            Assert.True(campOffered ^ visible.Contains("chain_needs_4b_travel"), "exactly one step-4 alternative is offered");
            if (!campOffered)
            {
                // A camp appears → the camp alternative wins (lowest id among the feasible ones) — unless the
                // world's rules keep bandits off entirely, in which case the travel step rightly stays.
                server.SpawnBanditCampForTest(new Vector3f(p.State.Position.X + 60f, p.State.Position.Y, p.State.Position.Z), guards: 2);
                visible = server.VisibleMissionIdsForTest("Hero");
                campOffered = visible.Contains("chain_needs_4a_camp");
                Assert.True(campOffered ^ visible.Contains("chain_needs_4b_travel"));
            }

            if (campOffered)
            {
                server.AcceptMission("Hero", "chain_needs_4a_camp");
                Assert.Equal(MissionStatus.Active, Progress(p, "chain_needs_4a_camp").Status);
                Assert.Equal("@srv.mission.chain_locked", server.ChainStepAvailableForTest("Hero", "chain_needs_4b_travel").Reason); // sibling held

                // Clearing ANY camp completes the chain's camp step (co-op: every online holder gets it).
                foreach (var guard in server.Bandits.ToList())
                {
                    if (Progress(p, "chain_needs_4a_camp").ObjectiveProgress[0] > 0)
                    {
                        break;
                    }

                    p.State.Position = guard.Position;
                    for (int i = 0; i < 10 && server.Bandits.Contains(guard); i++)
                    {
                        server.AttackEntity("Hero", guard.Id);
                    }
                }

                Assert.Equal(1, Progress(p, "chain_needs_4a_camp").ObjectiveProgress[0]);
            }
            else
            {
                server.AcceptMission("Hero", "chain_needs_4b_travel");
                Assert.Equal(MissionStatus.Active, Progress(p, "chain_needs_4b_travel").Status);
            }
        }
    }

    [Fact]
    public void ChainCampStep_TakenElsewhere_IsNotCreditedByLandingOnAPacifiedWorld()
    {
        // #1303: the "all camps are down" sweep completed the Defeat objectives of ANY active chain row. A
        // step taken at another settlement — on another world — was therefore handed out for a fight this
        // player never had, simply by landing somewhere whose camps had long since fallen.
        var server = Start("campstep", 4242, out var repo, out _, settlements: false, banditCamps: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");

            // Clear this world's only camp BEFORE any order is in the log, so nothing is credited on the kill.
            string campKey = server.SpawnBanditCampForTest(new Vector3f(40, 70, 40), guards: 1);
            foreach (var guard in server.Bandits.ToList())
            {
                p.State.Position = guard.Position;
                for (int i = 0; i < 40 && server.Bandits.Contains(guard); i++)
                {
                    server.AttackEntity("Ranger", guard.Id);
                }
            }

            Assert.True(server.BanditCampClearedForTest(campKey));

            var def = new MissionDefinition
            {
                Id = "test_chain_camp",
                Source = MissionSource.System,
                NameKey = "mission.chain.needs_4a.title",
                DescriptionKey = "mission.chain.needs_4a.desc",
                ChainId = "test_camp_chain",
                Step = 1,
                Surface = MissionChains.SurfaceRadio, // returnable anywhere — keeps this test off a board
                Objectives = { new MissionObjective { Type = MissionObjectiveType.Defeat, Target = "bandit_camp", Required = 1 } },
                Active = true,
            };
            server.AddMissionDefForTest(def);

            // The step as another settlement's board would have written it: bound to a DIFFERENT world.
            p.State.Missions.Add(new MissionProgress
            {
                MissionId = def.Id,
                ChainId = def.ChainId,
                Status = MissionStatus.Active,
                ObjectiveProgress = { 0 },
                AcceptedBodyId = "another-world",
            });
            var pr = Progress(p, def.Id);

            // The turn-in path runs the sweep first; it must leave this row alone, so the turn-in fails.
            server.TurnInMission("Ranger", def.Id);
            Assert.Equal(0, pr.ObjectiveProgress[0]);
            Assert.Equal(MissionStatus.Active, pr.Status);

            // The same row taken HERE still gets the credit a clear during the player's absence earns.
            pr.AcceptedBodyId = p.State.CurrentLocationId;
            server.TurnInMission("Ranger", def.Id);
            Assert.Equal(MissionStatus.TurnedIn, Progress(p, def.Id).Status);
        }
    }

    [Fact]
    public void TravelOtherBody_CompletesOnAnyBodyButTheOneTheStepWasTakenOn()
    {
        var server = Start("travel", 77, out var repo, out _, settlements: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scout");
            var def = new MissionDefinition
            {
                Id = "test_travel_chain",
                Source = MissionSource.System,
                NameKey = "mission.chain.needs_4b.title",
                DescriptionKey = "mission.chain.needs_4b.desc",
                ChainId = "test_chain",
                Step = 1,
                Surface = MissionChains.SurfaceRadio, // offered anywhere — keeps this test off the board
                Objectives = { new MissionObjective { Type = MissionObjectiveType.Travel, Target = MissionChains.TravelOtherBody, Required = 1 } },
                Active = true,
            };
            server.AddMissionDefForTest(def);
            server.AcceptMission("Scout", def.Id);
            var pr = Progress(p, def.Id);
            Assert.Equal(p.State.CurrentLocationId, pr.AcceptedBodyId);

            server.SimulateTravelForTest(p, pr.AcceptedBodyId, "home"); // landing where you started does not count
            Assert.Equal(0, pr.ObjectiveProgress[0]);
            server.SimulateTravelForTest(p, "some-other-body", "Neighbour");
            Assert.Equal(1, pr.ObjectiveProgress[0]);
        }
    }

    // ---------------- persistence ----------------

    [Fact]
    public void SnapshotAndRestart_KeepTheChainFields()
    {
        var pr = new MissionProgress { MissionId = "chain_needs_1", ChainId = "settlement_needs", AcceptedFrom = "settle_42", AcceptedBodyId = "b1", ObjectiveProgress = { 3 } };
        var state = new PlayerState { PlayerId = "pid", Name = "Hero", Missions = { pr } };
        var back = StateMapper.FromSnapshot(StateMapper.ToSnapshot(state));
        var clone = Assert.Single(back.Missions);
        Assert.Equal("settlement_needs", clone.ChainId);
        Assert.Equal("settle_42", clone.AcceptedFrom);
        Assert.Equal("b1", clone.AcceptedBodyId);
        Assert.Equal(3, clone.ObjectiveProgress[0]);

        var server = StartedWithBoard("restart", out var repo, out _, out long seed);
        string place;
        using (repo)
        {
            var p = server.AddLocalPlayer("Hero");
            p.State.Position = BoardPos(server);
            server.AcceptMission("Hero", "chain_needs_1");
            p.State.Inventory.Add("iron_ore", 8, 99);
            server.TurnInMission("Hero", "chain_needs_1"); // persists the player
            place = Progress(p, "chain_needs_1").AcceptedFrom;
            server.AcceptMission("Hero", "chain_needs_2");
            server.SimulatePlaceForTest(p, "light_white", new Vector3i(5, 70, 5));
            server.SaveAllForTest();
            server.Stop();
        }

        var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, $"restart_{seed}"));
        using (repo2)
        {
            var server2 = new SvGameServer(Config($"restart_{seed}", seed), _content, new LoopbackServerTransport(new LoopbackLink()), repo2);
            server2.Start();
            var p = server2.AddLocalPlayer("Hero");
            Assert.Equal(MissionStatus.TurnedIn, Progress(p, "chain_needs_1").Status);
            Assert.Equal(place, Progress(p, "chain_needs_1").AcceptedFrom);
            var step2 = Progress(p, "chain_needs_2");
            Assert.Equal(MissionStatus.Active, step2.Status);
            Assert.Equal("settlement_needs", step2.ChainId);
            Assert.Equal(1, step2.ObjectiveProgress[0]);
            p.State.Position = BoardPos(server2);
            Assert.DoesNotContain("chain_needs_1", server2.VisibleMissionIdsForTest("Hero"));
            Assert.DoesNotContain("chain_needs_3", server2.VisibleMissionIdsForTest("Hero"));
            server2.Stop();
        }
    }

    // ---------------- vendor friendship chain (dialogue) ----------------

    private static (int Id, string Name, string Role, string CharacterId)? FirstVendor(SvGameServer s)
        => s.NpcRosterForTest().Where(n => n.Role == "vendor" && n.CharacterId.Length == 0).Cast<(int, string, string, string)?>().FirstOrDefault();

    [Fact]
    public void VendorFavour_IsHandedOutInConversation_AndOnlyWhileTakeable()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var server = Start($"favour_{seed}", seed, out var repo, out _);
            if (!server.HasSettlement || server.SettlementRuined || FirstVendor(server) is not { } vendor)
            {
                repo.Dispose();
                continue;
            }

            using (repo)
            {
                var p = server.AddLocalPlayer("Regular");
                p.State.Position = server.NpcSnapshots.First(n => n.Id == vendor.Id).Home;

                // A stranger gets no dialogue at all.
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Null(server.ActiveDialogForTest("Regular"));

                // Known → the favour comes first; declining sets it aside and the smalltalk follows this session.
                string key = server.NpcKeyForTest("Regular", "vendor")!;
                p.State.NpcMemory[key] = new NpcRelationship { Name = vendor.Name, Role = "vendor", Value = 20 };
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_favour_1", 0), server.ActiveDialogForTest("Regular"));
                server.ChooseDialogForTest("Regular", 1); // "not today"
                Assert.Empty(p.State.Missions);
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_smalltalk", 0), server.ActiveDialogForTest("Regular"));
                server.ChooseDialogForTest("Regular", 0);

                // A new session (cleared decline) → asked again; saying yes puts the job in the log.
                p.DeclinedMissionDialogs.Clear();
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_favour_1", 0), server.ActiveDialogForTest("Regular"));
                server.ChooseDialogForTest("Regular", 0);
                var pr = Progress(p, "chain_vendor_1");
                Assert.Equal(MissionStatus.Active, pr.Status);
                Assert.Equal("vendor_favours", pr.ChainId);
                Assert.StartsWith("settle_", pr.AcceptedFrom);

                // While the job is held the favour dialogue steps aside; step 2 is not offered before step 1 is in.
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_smalltalk", 0), server.ActiveDialogForTest("Regular"));
                server.ChooseDialogForTest("Regular", 0);

                // Turn-in: away from the vendor refused, at the vendor it pays; then favour 2 opens.
                p.State.Inventory.Add("crystal", 3, 99);
                p.State.Position = new Vector3f(p.State.Position.X, p.State.Position.Y + 500f, p.State.Position.Z);
                server.TurnInMission("Regular", "chain_vendor_1");
                Assert.Equal(MissionStatus.Active, Progress(p, "chain_vendor_1").Status);
                p.State.Position = server.NpcSnapshots.First(n => n.Id == vendor.Id).Home;
                server.TurnInMission("Regular", "chain_vendor_1");
                Assert.Equal(MissionStatus.TurnedIn, Progress(p, "chain_vendor_1").Status);
                Assert.True(p.State.Inventory.CountOf("glass") >= 4);

                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_favour_2", 0), server.ActiveDialogForTest("Regular"));
                server.ChooseDialogForTest("Regular", 0);
                Assert.Equal(MissionStatus.Active, Progress(p, "chain_vendor_2").Status);

                // The third favour waits for a trusted friend.
                server.SimulateScanForTest(p, "flora", "flora_fern", hostile: false, firstTime: true);
                server.SimulateScanForTest(p, "flora", "flora_moss", hostile: false, firstTime: true);
                server.TurnInMission("Regular", "chain_vendor_2");
                Assert.Equal(MissionStatus.TurnedIn, Progress(p, "chain_vendor_2").Status);
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_smalltalk", 0), server.ActiveDialogForTest("Regular")); // known, not trusted
                server.ChooseDialogForTest("Regular", 0);
                p.State.NpcMemory[key].Value = 60;
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_favour_3", 0), server.ActiveDialogForTest("Regular"));
                server.Stop();
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("no inhabited settlement with a vendor found across 200 seeds");
    }

    [Fact]
    public void DialogueGrant_IsRefusedForAStepWhosePrerequisiteIsMissing()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var server = Start($"grant_{seed}", seed, out var repo, out _);
            if (!server.HasSettlement || server.SettlementRuined || FirstVendor(server) is not { } vendor)
            {
                repo.Dispose();
                continue;
            }

            using (repo)
            {
                var p = server.AddLocalPlayer("Regular");
                p.State.Position = server.NpcSnapshots.First(n => n.Id == vendor.Id).Home;
                string key = server.NpcKeyForTest("Regular", "vendor")!;
                p.State.NpcMemory[key] = new NpcRelationship { Name = vendor.Name, Role = "vendor", Value = 60 };

                // Even a trusted friend only gets favour 1 first — favours 2/3 are gated by their prerequisites.
                server.TalkToNpcForTest("Regular", vendor.Id);
                Assert.Equal(("vendor_favour_1", 0), server.ActiveDialogForTest("Regular"));
                server.ChooseDialogForTest("Regular", 1);

                // And the board route cannot take a dialogue step at all.
                p.State.Position = BoardPos(server);
                server.AcceptMission("Regular", "chain_vendor_1");
                Assert.Empty(p.State.Missions);
                Assert.Equal("@srv.mission.chain_giver", server.ChainStepAvailableForTest("Regular", "chain_vendor_1").Reason);
                Assert.DoesNotContain("chain_vendor_1", server.VisibleMissionIdsForTest("Regular"));
                server.Stop();
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("no inhabited settlement with a vendor found across 200 seeds");
    }

    // ---------------- procedural big orders ----------------

    [Fact]
    public void BigOrder_AppearsAfterThreeTurnIns_ForAKnownPlayer_AndStepTwoFollowsStepOne()
    {
        var server = StartedWithBoard("bigorder", out var repo, out _, out _);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hauler");
            p.State.Position = BoardPos(server);
            Assert.DoesNotContain(server.VisibleMissionIdsForTest("Hauler"), id => id.Contains("_c", StringComparison.Ordinal) && id.StartsWith("settle_", StringComparison.Ordinal));

            // Three gather turn-ins at this board.
            for (int i = 0; i < 3; i++)
            {
                string id = server.AvailableBoardMissions("Hauler").First(x => IsGatherId(x));
                server.AcceptMission("Hauler", id);
                var obj = server.FirstObjectiveForTest(id)!;
                p.State.Inventory.Add(obj.Target, obj.Required, 99);
                server.TurnInMission("Hauler", id);
                Assert.Equal(MissionStatus.TurnedIn, Progress(p, id).Status);
            }

            // Three accepts = standing 9 → still a stranger → no big order yet; the rule says known.
            string prefix = server.CurrentPlaceKeyForTest("Hauler") + "_c";
            Assert.DoesNotContain(server.VisibleMissionIdsForTest("Hauler"), id => id.StartsWith(prefix, StringComparison.Ordinal));
            string qKey = server.CurrentPlaceKeyForTest("Hauler") + ":quartermaster";
            Assert.Equal("@srv.mission.chain_stage", server.ChainStepAvailableForTest("Hauler", prefix + "0").Reason);

            p.State.NpcMemory[qKey].Value = 20; // known
            var visible = server.VisibleMissionIdsForTest("Hauler");
            Assert.Contains(prefix + "0", visible);
            Assert.DoesNotContain(prefix + "1", visible);

            var stepA = server.MissionDefForTest(prefix + "0")!;
            var stepB = server.MissionDefForTest(prefix + "1")!;
            Assert.Equal(stepA.ChainId, stepB.ChainId);
            Assert.Equal((1, 2), (stepA.Step, stepB.Step));
            Assert.Equal(prefix + "0", stepB.Prerequisites.Single());
            Assert.Equal(prefix + "1", stepA.NextMissionId);
            Assert.Equal(MissionObjectiveType.Deliver, stepA.Objectives[0].Type);
            Assert.True(stepA.Objectives[0].Required >= 10, "a big order asks for a lot");
            Assert.True(stepA.Rewards[0].Count >= 3, "…and pays accordingly");

            server.AcceptMission("Hauler", prefix + "0");
            Assert.Equal(MissionStatus.Active, Progress(p, prefix + "0").Status);
            p.State.Inventory.Add(stepA.Objectives[0].Target, stepA.Objectives[0].Required, 99);
            server.TurnInMission("Hauler", prefix + "0");
            Assert.Equal(MissionStatus.TurnedIn, Progress(p, prefix + "0").Status);

            visible = server.VisibleMissionIdsForTest("Hauler");
            Assert.Contains(prefix + "1", visible);
            Assert.DoesNotContain(prefix + "2", visible); // the next order waits for this one to finish

            // The order's definitions survive a restart: persisted on accept (progress may happen far away).
            Assert.Contains(repo.ListMissions(), m => m.Id == prefix + "0");
        }

        static bool IsGatherId(string id)
        {
            int us = id.LastIndexOf('_');
            return us > 0 && int.TryParse(id[(us + 1)..], out _);
        }
    }

    // ---------------- radio nudge ----------------

    [Fact]
    public void TurningInAChainStep_QueuesTheGiversRadioNudge()
    {
        var server = StartedWithBoard("nudge", out var repo, out var link, out _);
        using (repo)
        {
            using var client = new LoopbackClientTransport(link);
            var lines = new List<ChatMessage>();
            client.PayloadReceived += payload =>
            {
                if (NetCodec.Decode(payload) is ChatMessage m)
                {
                    lines.Add(m);
                }
            };
            client.Connect("loopback", 0);
            client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Hero" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            client.Poll();
            var p = server.Sessions[1];
            p.State.Inventory.Add("comm_radio", 1, 99);
            p.State.Position = BoardPos(server);

            int pendingBefore = server.DialogRadioPendingForTest;
            server.AcceptMission("Hero", "chain_needs_1");
            p.State.Inventory.Add("iron_ore", 8, 99);
            server.TurnInMission("Hero", "chain_needs_1");
            Assert.Equal(pendingBefore + 1, server.DialogRadioPendingForTest); // "there's a follow-up" is queued

            // It fires through the radio gates once due — 90 s later, as a MISSION call. (Other calls the world
            // makes meanwhile re-arm the per-player call gap, so the gap is skipped before every drain.)
            for (int i = 0; i < 25 && server.DialogRadioPendingForTest > pendingBefore; i++)
            {
                client.Send(NetCodec.Encode(new RequestMissions()), DeliveryMode.ReliableOrdered); // heartbeat: a wire session silent for 90 s is swept (#964)
                server.Tick(5.0);
                server.SkipNpcCallCooldownsForTest("Hero");
                server.TickDialogRadioForTest();
            }

            server.Tick(0.1);
            client.Poll();
            string expected = _content.CreateLocalizer(Shared.Localization.GameLocale.English).Get("npc.call.chain_next");
            Assert.True(lines.Any(l => l.IsNpcCall && l.Text == expected),
                $"pending={server.DialogRadioPendingForTest} (before {pendingBefore}); loc={p.CurrentLocationId}; lines: " + string.Join(" | ", lines.Select(l => l.Sender + ": " + l.Text)));
        }
    }
}
