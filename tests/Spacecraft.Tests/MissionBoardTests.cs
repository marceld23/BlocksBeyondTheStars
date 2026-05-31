using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

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
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_board_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "board_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "board_" + seed, Seed = seed, StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
            PlaceSettlements = true, PlaceWrecks = false,
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
            foreach (var need in new[] { "iron_ore", "carbon", "silicate", "copper_ore", "crystal" })
            {
                p.State.Inventory.Add(need, 20, 99);
            }

            server.TurnInMission("Courier", id);

            var prog = p.State.Missions.First(m => m.MissionId == id);
            Assert.Equal(MissionStatus.Completed, prog.Status);
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
