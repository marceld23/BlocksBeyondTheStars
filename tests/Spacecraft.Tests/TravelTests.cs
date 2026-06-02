using System;
using System.IO;
using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Travelling to another planet switches the active world and keeps each planet's own edits.</summary>
public sealed class TravelTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TravelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_travel_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "travel"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "travel", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private CelestialBody OtherPlanet(SvGameServer server)
        => server.Galaxy.AllBodies().First(b =>
            b.Kind == CelestialKind.Planet
            && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is not null
            && b.Id != server.ActiveLocationId);

    [Fact]
    public void Travel_SwitchesActiveWorld_ToAnotherPlanet()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            var dest = OtherPlanet(server);

            server.Travel("Pilot", dest.Id);

            Assert.Equal(dest.Id, server.ActiveLocationId);
            Assert.Equal(dest.Id, server.World.LocationId);
        }
    }

    [Fact]
    public void Travel_RejectsUnknownOrSameLocation()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string origin = server.ActiveLocationId;

            server.Travel("Pilot", "does-not-exist");
            Assert.Equal(origin, server.ActiveLocationId); // unchanged

            server.Travel("Pilot", origin);
            Assert.Equal(origin, server.ActiveLocationId); // already here → unchanged
        }
    }

    [Fact]
    public void Travel_KeepsEachPlanetsEdits()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string origin = server.ActiveLocationId;
            var dest = OtherPlanet(server);

            var pos = new Vector3i(6, 100, 6); // high up: air on both planets unless edited
            var stone = _content.GetBlock("stone")!.NumericId;
            var iron = _content.GetBlock("iron_wall")!.NumericId;

            // Edit the origin world.
            server.World.SetBlock(pos, stone);
            Assert.Equal(stone.Value, server.World.GetBlock(pos).Value);

            // On the destination, the origin edit is absent.
            server.Travel("Pilot", dest.Id);
            Assert.NotEqual(stone.Value, server.World.GetBlock(pos).Value);

            // Edit the destination world differently.
            server.World.SetBlock(pos, iron);
            Assert.Equal(iron.Value, server.World.GetBlock(pos).Value);

            // Back home: the origin edit persisted; the destination's edit isn't here.
            server.Travel("Pilot", origin);
            Assert.Equal(stone.Value, server.World.GetBlock(pos).Value);

            // Destination again: its own edit persisted.
            server.Travel("Pilot", dest.Id);
            Assert.Equal(iron.Value, server.World.GetBlock(pos).Value);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
