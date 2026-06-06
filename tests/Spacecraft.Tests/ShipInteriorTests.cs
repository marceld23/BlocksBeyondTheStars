using System;
using Spacecraft.GameServer;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Walking inside your own ship while it floats in space: from the flight view you step into the ship's
/// walkable interior (reusing the on-planet StampShip layout in a void world), and the helm takes you back
/// to the flight view with the ship parked where it was.
/// </summary>
public sealed class ShipInteriorTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipInteriorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_shipint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shipint"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "shipint",
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true, // we need the ship stamped so EnterShipInterior has a hull + heal-tank
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void EnterShipInterior_ThenHelm_RoundTripsThroughTheFlightView()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            var p = session.State;

            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));          // piloting in the flight view

            server.EnterShipInterior("Pilot");
            Assert.False(server.InSpace("Pilot"));         // left the flight view
            Assert.True(server.InShipInterior("Pilot"));   // walking inside the ship
            Assert.True(p.AboardShip);                     // inside the hull → life support
            Assert.False(p.InEva);
            Assert.StartsWith("shipint:", session.CurrentLocationId);

            server.ExitShipToFlight("Pilot");
            Assert.True(server.InSpace("Pilot"));          // back at the helm, in space
            Assert.False(server.InShipInterior("Pilot"));
        }
    }

    [Fact]
    public void UsingTheCockpitInsideTheShip_TakesTheHelm()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));

            // The cockpit is the helm while floating in space — using it flies again.
            server.UseStation("Pilot", "cockpit");
            Assert.True(server.InSpace("Pilot"));
            Assert.False(server.InShipInterior("Pilot"));
        }
    }

    [Fact]
    public void AirlockInsideTheShip_StepsOutIntoAnEva()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));

            // The airlock cycles out into the flight view as a floating EVA suit.
            server.UseStation("Pilot", "airlock");
            Assert.True(server.InSpace("Pilot"));
            Assert.False(server.InShipInterior("Pilot"));
            Assert.True(session.State.InEva);
        }
    }

    [Fact]
    public void Eva_CannotLandOnAPlanet_OnlyAnAsteroid()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");

            // The body you launched from ("rocky") is a planet — never an EVA landing target. From a
            // spacewalk you must board the ship to reach a planet/moon; only asteroids are landable on foot.
            Assert.False(server.EvaLandingAllowed(session.CurrentLocationId));
        }
    }

    [Fact]
    public void EnterShipInterior_OnlyWorksFromSpace()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");

            // On the surface (never launched) — stepping inside is rejected, no in-ship state.
            server.EnterShipInterior("Pilot");
            Assert.False(server.InShipInterior("Pilot"));
        }
    }
}
