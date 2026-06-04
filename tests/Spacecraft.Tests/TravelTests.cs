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

    private SvGameServer Started(out SqliteWorldRepository repo, bool jumpDrive = true)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "travel"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "travel", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        // Most travel tests jump between systems; fit a jump generator so that is allowed.
        if (jumpDrive && !server.Ship.HasModule("jump_generator"))
        {
            server.Ship.Modules.Add("jump_generator");
        }

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
            server.Ship.Modules.Add("jump_generator"); // fit on the player's own ship (per-player ships)
            var dest = OtherPlanet(server);

            server.Travel("Pilot", dest.Id);

            Assert.Equal(dest.Id, server.ActiveLocationId);
            Assert.Equal(dest.Id, server.World.LocationId);
        }
    }

    [Fact]
    public void TwoPlayers_OnDifferentPlanets_HaveIsolatedWorlds()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Alice");
            server.AddLocalPlayer("Bob");

            var alice = server.Sessions.Values.First(s => s.State.PlayerId == "Alice");
            var bob = server.Sessions.Values.First(s => s.State.PlayerId == "Bob");
            string origin = alice.CurrentLocationId;
            Assert.Equal(origin, bob.CurrentLocationId); // both start together
            alice.Ships[alice.ActiveShipId].Modules.Add("jump_generator"); // Alice's own ship can jump

            var dest = OtherPlanet(server);
            server.Travel("Alice", dest.Id);

            // Only Alice moved; Bob stays on the origin world.
            Assert.Equal(dest.Id, alice.CurrentLocationId);
            Assert.Equal(origin, bob.CurrentLocationId);

            // Both worlds are resident at once.
            Assert.True(server.ResidentWorldCount >= 2);
            Assert.NotNull(server.WorldAt(dest.Id));
            Assert.NotNull(server.WorldAt(origin));

            // Edits on one world do not bleed into the other.
            var pos = new Vector3i(6, 100, 6); // air on both unless edited
            var stone = _content.GetBlock("stone")!.NumericId;
            server.WorldAt(dest.Id)!.SetBlock(pos, stone);
            Assert.Equal(stone.Value, server.WorldAt(dest.Id)!.GetBlock(pos).Value);
            Assert.NotEqual(stone.Value, server.WorldAt(origin)!.GetBlock(pos).Value);

            // Ticking simulates both occupied worlds without error.
            server.Tick(0.1);
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
    public void Travel_SeedsFaunaImmediately_OnALivingWorld()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("jump_generator");
            string origin = server.ActiveLocationId;

            // Travel until we reach a world that actually has fauna; it must be populated on arrival
            // (no ticking) so a freshly-entered planet feels alive instead of empty.
            foreach (var b in server.Galaxy.AllBodies().Where(b =>
                         b.Kind == CelestialKind.Planet
                         && !string.IsNullOrEmpty(b.PlanetType)
                         && _content.GetPlanet(b.PlanetType!) is not null
                         && b.Id != origin))
            {
                server.Travel("Pilot", b.Id);
                if (server.SpeciesRoster.Count > 0)
                {
                    Assert.NotEmpty(server.Creatures);
                    return;
                }
            }
        }
    }

    [Fact]
    public void Travel_KeepsEachPlanetsEdits()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.Ship.Modules.Add("jump_generator");
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

    [Fact]
    public void Hyperjump_ToAnotherSystem_RequiresJumpGenerator()
    {
        var server = Started(out var repo, jumpDrive: false);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string origin = server.ActiveLocationId;
            string originSystem = server.Galaxy.FindBody(origin)!.SystemId;

            var crossSystem = server.Galaxy.AllBodies().First(b =>
                b.Kind == CelestialKind.Planet
                && !string.IsNullOrEmpty(b.PlanetType)
                && _content.GetPlanet(b.PlanetType!) is not null
                && b.SystemId != originSystem);

            // Without a jump generator a cross-system jump is rejected.
            server.Travel("Pilot", crossSystem.Id);
            Assert.Equal(origin, server.ActiveLocationId);

            // Fit one and the same jump now succeeds.
            server.Ship.Modules.Add("jump_generator");
            server.Travel("Pilot", crossSystem.Id);
            Assert.Equal(crossSystem.Id, server.ActiveLocationId);
        }
    }

    [Fact]
    public void InSystemTravel_NeedsNoJumpGenerator()
    {
        var server = Started(out var repo, jumpDrive: false);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string origin = server.ActiveLocationId;
            string originSystem = server.Galaxy.FindBody(origin)!.SystemId;

            var sameSystem = server.Galaxy.AllBodies().FirstOrDefault(b =>
                b.Kind == CelestialKind.Planet
                && !string.IsNullOrEmpty(b.PlanetType)
                && _content.GetPlanet(b.PlanetType!) is not null
                && b.SystemId == originSystem
                && b.Id != origin);

            if (sameSystem is null)
            {
                return; // this seed's origin system has a single planet — nothing to assert
            }

            // An in-system hop is normal flight: allowed without a jump generator.
            server.Travel("Pilot", sameSystem.Id);
            Assert.Equal(sameSystem.Id, server.ActiveLocationId);
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
