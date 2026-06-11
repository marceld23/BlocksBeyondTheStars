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
        // Stations now park well above the orbital plane (clear of every planet) — fly up to one
        // before docking, exactly like a player would (the board range check is real).
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
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
    public void BoardStation_PutsPlayerInOwnVoidWorld_OnSolidGround_WithLifeSupport()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            BoardFirstStation(server, "Pilot");

            // The station is now its OWN location (a void world), not the planet.
            Assert.StartsWith("station:", pilot.CurrentLocationId);
            Assert.StartsWith("station:", server.World.LocationId);

            // Solid floor directly under the spawn (the floor pad) — the player can't fall through.
            var feet = pilot.State.Position.ToBlock();
            Assert.False(server.World.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)).IsAir,
                "Expected solid ground directly below the station spawn.");

            // Life support: oxygen does not drain inside the station.
            pilot.State.Oxygen = 80f;
            server.TickForTest(2.0);
            Assert.True(pilot.State.Oxygen >= 80f, "Oxygen should not drain inside a station (life support).");
        }
    }

    [Fact]
    public void BoardStation_PopulatesItWithCrewNpcs()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.Equal(0, server.NpcCount); // no settlements in this config → no NPCs yet

            BoardFirstStation(server, "Pilot");

            // The station is now inhabited: at least a vendor + a quartermaster at their markers.
            Assert.True(server.NpcCount >= 2, $"Expected station crew, got {server.NpcCount}.");
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
            Assert.Contains(server.NpcSnapshots, n => n.Role == "quartermaster");

            // The vendor crew stands at the station's vendor marker (its interior, not the surface).
            var vendorMarker = server.SpaceStationMarkers.First(m => m.Type == "vendor");
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor" && n.Home.DistanceSquared(vendorMarker.Pos) < 1f);

            // Beyond the marker crew, the station is populated with extra wandering civilians.
            Assert.True(server.NpcCount >= 4, $"Expected extra civilians, got {server.NpcCount}.");
            Assert.Contains(server.NpcSnapshots, n => n.Role == "settler");
        }
    }

    [Fact]
    public void DockingAStationOnAnEva_KeepsTheShipFloating_AndUndockReturnsToEva()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var p = pilot.State;
            server.EnterSpace("Pilot");
            p.InEva = true; // floating on a spacewalk next to the parked ship

            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.SpaceStation);
            server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f); // float over
            server.BoardStation("Pilot", station.Id);
            Assert.True(server.InStation("Pilot"));
            Assert.False(p.InEva); // inside the station now — the spacewalk ended

            server.LeaveStation("Pilot");
            Assert.True(server.InSpace("Pilot")); // back in the flight view
            Assert.True(p.InEva);                 // floating again next to the ship that waited in space
        }
    }

    [Fact]
    public void Station_NeverSpawnsHostilesOrWildlife_OnlyPeacefulNpcs()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // Hostile aliens fully enabled — they would normally spawn near a player on a surface.
            var pilot = server.AddLocalPlayer("Pilot");
            BoardFirstStation(server, "Pilot");
            Assert.True(server.InStation("Pilot"));

            // Tick well past any spawn interval while standing on the station (a void world).
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(60.0);
            }

            Assert.Empty(server.PlanetEnemies); // no hostile aliens aboard the station
            Assert.Empty(server.Creatures);     // no wandering wildlife either
            Assert.True(server.NpcCount > 0);   // only the peaceful station crew lives here
        }
    }

    [Fact]
    public void LeaveStation_UndocksBackIntoSpaceFlight()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string planetLoc = pilot.CurrentLocationId;
            BoardFirstStation(server, "Pilot");
            Assert.True(server.InStation("Pilot"));

            server.LeaveStation("Pilot");

            Assert.False(server.InStation("Pilot"));
            Assert.True(server.InSpace("Pilot"));               // back in the ship's space view, not on foot
            Assert.Equal(planetLoc, pilot.CurrentLocationId);   // the world underneath is the orbited planet
            Assert.True(pilot.State.AboardShip);
        }
    }

    [Fact]
    public void StationCrew_StandsOnTheDeck_NotFloating()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            BoardFirstStation(server, "Pilot");

            // Every crew member's feet sit exactly on the floor grid (integer Y, no +0.5 hover) with solid
            // deck directly below — i.e. standing on the deck, never floating above it.
            foreach (var npc in server.NpcSnapshots)
            {
                Assert.True(npc.Home.Y == (float)System.Math.Floor(npc.Home.Y),
                    $"NPC {npc.Role} Y {npc.Home.Y} should sit on the floor grid, not the centred marker.");
                var feet = npc.Home.ToBlock();
                Assert.False(server.World.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)).IsAir,
                    $"NPC {npc.Role} should have solid deck under its feet.");
            }
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
