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
/// Settlement mission boards: an inhabited settlement offers local gather missions that can only be
/// accepted and turned in while standing at its mission-board marker.
/// </summary>
public sealed class MissionBoardTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MissionBoardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_board_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "board_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "board_" + seed,
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

    /// <summary>Finds a world whose settlement has a mission board (offers settlement missions).</summary>
    private SvGameServer StartedWithBoard(out SqliteWorldRepository repo)
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var server = Start(seed, out repo);
            if (server.SettlementMissionIds.Count > 0)
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No settlement with a mission board found across 60 seeds.");
    }

    private static Vector3f BoardPos(SvGameServer server)
        => server.SettlementMarkers.First(m => m.Type == "mission_board").Pos;

    [Fact]
    public void Settlement_OffersLocalMissions_AtItsBoard()
    {
        var server = StartedWithBoard(out var repo);
        using (repo)
        {
            var id = server.SettlementMissionIds.First();
            Assert.True(server.IsSettlementMission(id));
            Assert.Contains(server.SettlementMarkers, m => m.Type == "mission_board");
        }
    }

    [Fact]
    public void Mission_Rejected_AwayFromBoard_Accepted_AtBoard()
    {
        var server = StartedWithBoard(out var repo);
        using (repo)
        {
            var id = server.SettlementMissionIds.First();
            var p = server.AddLocalPlayer("Courier");

            // Away from the board (spawn is offset from the settlement): accept is rejected.
            p.State.Position = new Vector3f(1000, 64, 1000);
            server.AcceptMission("Courier", id);
            Assert.DoesNotContain(p.State.Missions, m => m.MissionId == id);

            // At the board: accept succeeds.
            p.State.Position = BoardPos(server);
            server.AcceptMission("Courier", id);
            Assert.Contains(p.State.Missions, m => m.MissionId == id && m.Status == MissionStatus.Active);
        }
    }

    [Fact]
    public void Mission_TurnIn_AtBoard_CompletesIt()
    {
        var server = StartedWithBoard(out var repo);
        using (repo)
        {
            var id = server.SettlementMissionIds.First();
            var p = server.AddLocalPlayer("Courier");
            p.State.Position = BoardPos(server);

            server.AcceptMission("Courier", id);

            // Supply plenty of every possible required resource so whatever the mission needs is covered.
            foreach (var need in new[] { "iron_ore", "carbon", "silicate", "copper_ore", "crystal", "titanium_ore", "data_fragment" })
            {
                p.State.Inventory.Add(need, 30, 99);
            }

            server.TurnInMission("Courier", id);

            var prog = p.State.Missions.First(m => m.MissionId == id);
            Assert.Equal(MissionStatus.TurnedIn, prog.Status);
        }
    }

    // ---------------- Mission-giver never runs dry (item 13) ----------------

    [Fact]
    public void Board_NeverRunsDry_KeepsMissionsAvailable_AsYouKeepAccepting()
    {
        var server = StartedWithBoard(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Courier");
            p.State.Position = BoardPos(server);

            // Take a dozen jobs in a row; the giver always still has fresh ones on offer.
            for (int round = 0; round < 12; round++)
            {
                var available = server.AvailableBoardMissions("Courier");
                Assert.True(available.Count >= 3, $"the giver should always offer ≥3 (round {round}, had {available.Count}).");
                server.AcceptMission("Courier", available[0]);
            }

            Assert.True(server.AvailableBoardMissions("Courier").Count >= 3); // still stocked at the end
        }
    }

    [Fact]
    public void BoardMissions_CarryAGiverName()
    {
        var server = StartedWithBoard(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Courier");
            p.State.Position = BoardPos(server);

            var id = server.AvailableBoardMissions("Courier").First();
            Assert.False(string.IsNullOrEmpty(server.MissionGiverName(id)), "a giver mission names who offers it.");
        }
    }

    // ---------------- NPC memory of interactions (item 14) ----------------

    [Fact]
    public void Quartermaster_RemembersMissionAccepts_RelationshipRises_LogCapsAtTen()
    {
        var server = StartedWithBoard(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Courier");
            p.State.Position = BoardPos(server);

            for (int i = 0; i < 12; i++)
            {
                var avail = server.AvailableBoardMissions("Courier");
                server.AcceptMission("Courier", avail[0]);
            }

            var rel = p.State.NpcMemory.Values.First(r => r.Role == "quartermaster");
            Assert.Equal(10, rel.Log.Count);              // only the last 10 are kept
            Assert.True(rel.Value > 0);                    // taking jobs builds the relationship
            Assert.All(rel.Log, e => Assert.Equal(BlocksBeyondTheStars.Shared.State.NpcInteractionKind.MissionAccepted, e.Kind));
            Assert.False(string.IsNullOrEmpty(rel.Name));  // remembers the giver's name
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
