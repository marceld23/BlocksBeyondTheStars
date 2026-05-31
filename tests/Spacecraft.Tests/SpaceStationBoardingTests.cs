using Spacecraft.GameServer;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Boardable space stations: neutral contacts in space that can be docked with, stamping a
/// procedural voxel station interior with vendor/mission-board markers.
/// </summary>
public sealed class SpaceStationBoardingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpaceStationBoardingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_stations_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "stations"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "stations",
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent },
        };
        config.Rules.FreeSpaceFlight = true;

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static CombatEntity BoardFirstStation(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.BoardStation(playerId, station.Id);
        return station;
    }

    [Fact]
    public void EnterSpace_IncludesNeutralStationContact()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            var station = server.SpaceEntitiesFor("Pilot").FirstOrDefault(e => e.Kind == CombatEntityKind.SpaceStation);

            Assert.NotNull(station);
            Assert.False(station!.Hostile);
            Assert.False(string.IsNullOrWhiteSpace(station.Name));
        }
    }

    [Fact]
    public void BoardStation_StampsInterior_AndMovesPlayerInside()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var station = BoardFirstStation(server, "Pilot");

            Assert.False(server.InSpace("Pilot"));
            Assert.True(server.InStation("Pilot"));
            Assert.False(pilot.State.AboardShip);
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "vendor");
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "mission_board");

            bool solidNearby = false;
            var pos = pilot.State.Position.ToBlock();
            for (int dx = -6; dx <= 6 && !solidNearby; dx++)
            for (int dy = -2; dy <= 6 && !solidNearby; dy++)
            for (int dz = -6; dz <= 6 && !solidNearby; dz++)
            {
                if (!server.World.GetBlock(new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz)).IsAir)
                {
                    solidNearby = true;
                }
            }

            Assert.True(solidNearby, $"Boarding {station.Name} should stamp real station blocks.");
        }
    }

    [Fact]
    public void StationVendor_EnablesMarketBarter()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Trader");
            BoardFirstStation(server, "Trader");
            var vendor = server.SpaceStationMarkers.First(m => m.Type == "vendor");
            pilot.State.Position = vendor.Pos;
            pilot.State.Inventory.Add("iron_ore", 5, 99);

            server.Craft("Trader", "market_iron_to_titanium");

            Assert.Equal(0, pilot.State.Inventory.CountOf("iron_ore"));
            Assert.Equal(1, pilot.State.Inventory.CountOf("titanium_ore"));
        }
    }

    [Fact]
    public void StationMissions_AreBoardGated()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Runner");
            BoardFirstStation(server, "Runner");
            string missionId = server.StationMissionIds.First();

            pilot.State.Position = new Vector3f(0, 0, 0);
            server.AcceptMission("Runner", missionId);
            Assert.DoesNotContain(pilot.State.Missions, m => m.MissionId == missionId);

            pilot.State.Position = server.SpaceStationMarkers.First(m => m.Type == "mission_board").Pos;
            server.AcceptMission("Runner", missionId);
            Assert.Contains(pilot.State.Missions, m => m.MissionId == missionId);
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
