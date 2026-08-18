// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Build missions (#1116): settlement boards offer a builder's assignment beside the delivery jobs; the
/// objective advances on block placement (filtered by target group), never regresses when blocks are mined
/// back out, and turns in at the board like every other mission.
/// </summary>
public sealed class BuildMissionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BuildMissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_buildmission_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "build_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "build_" + seed,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds a world with a mission-board settlement whose build job does NOT target "base"
    /// (the base variants need a founded base — the matcher itself is unit-tested below).</summary>
    private SvGameServer StartedWithPlaceableBuildJob(out SqliteWorldRepository repo, out string missionId, out MissionObjective objective)
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var server = Start(seed, out repo);
            if (server.SettlementMissionIds.Count > 0)
            {
                var id = server.SettlementMissionIds.FirstOrDefault(i => i.Contains("_b0", StringComparison.Ordinal));
                var obj = id is null ? null : server.FirstObjectiveForTest(id);
                if (obj is { Type: MissionObjectiveType.Build } && obj.Target != "base")
                {
                    missionId = id!;
                    objective = obj;
                    return server;
                }
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No settlement with a placeable build job found across 60 seeds.");
    }

    private static Vector3f BoardPos(SvGameServer server)
        => server.SettlementMarkers.First(m => m.Type == "mission_board").Pos;

    /// <summary>A block key that satisfies the objective's target group.</summary>
    private static string MatchingBlock(MissionObjective obj) => obj.Target switch
    {
        "any" => "concrete",
        "light" => "torch",
        _ => obj.Target,
    };

    [Fact]
    public void SettlementBoard_OffersABuildJob()
    {
        var server = StartedWithPlaceableBuildJob(out var repo, out var id, out var obj);
        using (repo)
        {
            Assert.True(server.IsSettlementMission(id));
            Assert.Equal(MissionObjectiveType.Build, obj.Type);
            Assert.InRange(obj.Required, 1, 40);
        }
    }

    [Fact]
    public void BuildObjective_AdvancesOnPlace_NeverRegressesOnMine_AndTurnsIn()
    {
        var server = StartedWithPlaceableBuildJob(out var repo, out var id, out var obj);
        using (repo)
        {
            var p = server.AddLocalPlayer("Mason");
            p.State.Position = BoardPos(server);
            server.AcceptMission("Mason", id);
            var progress = p.State.Missions.First(m => m.MissionId == id);

            string block = MatchingBlock(obj);
            var pos = new Vector3i(10, 64, 10);

            // A non-matching placement never advances a filtered objective.
            if (obj.Target != "any")
            {
                server.SimulatePlaceForTest(p, "concrete", pos);
                Assert.Equal(0, progress.ObjectiveProgress[0]);
            }

            // Matching placements advance up to the requirement…
            for (int i = 0; i < obj.Required; i++)
            {
                server.SimulatePlaceForTest(p, block, pos);
            }

            Assert.Equal(obj.Required, progress.ObjectiveProgress[0]);

            // …and never past it, and mining the blocks back out never regresses the count.
            server.SimulatePlaceForTest(p, block, pos);
            server.SimulateMineForTest(p, block);
            server.SimulateMineForTest(p, "concrete");
            Assert.Equal(obj.Required, progress.ObjectiveProgress[0]);

            // Turn-in at the board completes it and pays the reward.
            server.TurnInMission("Mason", id);
            Assert.Equal(MissionStatus.TurnedIn, p.State.Missions.First(m => m.MissionId == id).Status);
        }
    }

    [Fact]
    public void BuildObjectiveMatcher_FiltersTargets()
    {
        var torch = _content.GetBlock("torch")!;
        var concrete = _content.GetBlock("concrete")!;

        Assert.True(SvGameServer.BuildObjectiveMatches("any", concrete, inOwnBase: false));
        Assert.True(SvGameServer.BuildObjectiveMatches("light", torch, inOwnBase: false));
        Assert.False(SvGameServer.BuildObjectiveMatches("light", concrete, inOwnBase: false));
        Assert.True(SvGameServer.BuildObjectiveMatches("base", concrete, inOwnBase: true));
        Assert.False(SvGameServer.BuildObjectiveMatches("base", concrete, inOwnBase: false));
        Assert.True(SvGameServer.BuildObjectiveMatches("concrete", concrete, inOwnBase: false));
        Assert.False(SvGameServer.BuildObjectiveMatches("torch", concrete, inOwnBase: false));
    }
}
