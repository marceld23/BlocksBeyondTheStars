// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

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
        _root = Path.Combine(Path.GetTempPath(), "bbts_shipint_" + Guid.NewGuid().ToString("N"));
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
    public void LaunchToSpaceFromShipInterior_TakesTheSkipPath_NotAFreshPlanetLaunch()
    {
        // B40: the ship interior is only ever entered from space, so launching to space from there (whatever
        // fires the EnterSpace intent) must return to flight via the skip-take-off path — never replay the
        // planet take-off. The intent handler now routes a player in the interior through ExitShipToFlight.
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));

            server.HandleEnterSpaceForTest("Pilot"); // the menu "Launch into space" intent

            Assert.True(server.InSpace("Pilot"));         // back in the flight view
            Assert.False(server.InShipInterior("Pilot")); // exited cleanly via the skip path, not a fresh launch
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
    public void ShipHatchDoor_StaysSealedAtTheSpawn_ButOpensWhenYouWalkUpToIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot"); // standing inside the ship at the heal-tank

            var hatch = server.DoorSnapshots.First(d => d.Kind == "energy"); // the ship's own hatch (energy door, item 35)
            Assert.False(hatch.Open);                                       // starts closed

            // At the heal-tank spawn (a few blocks from the hatch) the tighter-range hatch stays sealed.
            server.TickForTest(0.5);
            Assert.False(server.DoorSnapshots.First(d => d.Id == hatch.Id).Open);

            // Walk right up to the hatch → it slides open.
            session.State.Position = hatch.Pos;
            server.TickForTest(0.2);
            Assert.True(server.DoorSnapshots.First(d => d.Id == hatch.Id).Open);
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
    public void DeathOnAnEva_RecoversToTheShip_NotStuckInSpace()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            var p = session.State;
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            // Walk out the hatch into the void → an EVA spacewalk.
            p.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(p.Position.X, p.Position.Y, p.Position.Z - 1000f);
            server.Tick(0.1);
            Assert.True(p.InEva);
            Assert.True(server.InSpace("Pilot"));

            p.Health = 0f;
            server.Tick(0.1); // death → recovery

            Assert.Equal(100f, p.Health);
            Assert.False(server.InSpace("Pilot"));        // dropped out of the flight view
            Assert.False(server.InShipInterior("Pilot"));
            Assert.False(p.InEva);
            Assert.True(p.AboardShip);                    // recovered into the ship with life support
        }
    }

    [Fact]
    public void TwoPilotsInTheSameSystem_SeeEachOtherInSpace()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Ann");
            server.AddLocalPlayer("Bob");
            server.EnterSpace("Ann");
            server.EnterSpace("Bob"); // same start world → same space instance

            server.ShipMove("Ann", 10f, 0f, 5f, 1.2f);
            server.ShipMove("Bob", -8f, 0f, 3f, 0.4f);

            var annSees = server.OtherSpacePlayers("Ann");
            var bobSees = server.OtherSpacePlayers("Bob");

            Assert.Single(annSees);
            Assert.Equal("Bob", annSees[0].Name);
            Assert.False(annSees[0].Eva); // piloting, not floating

            Assert.Single(bobSees);
            Assert.Equal("Ann", bobSees[0].Name);
        }
    }

    [Fact]
    public void WalkingOutTheHatchInSpace_StartsAnEva()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Pilot");
            var p = session.State;
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));

            // Step well outside the hull — out the hatch into the surrounding void.
            p.Position = new BlocksBeyondTheStars.Shared.Geometry.Vector3f(p.Position.X, p.Position.Y, p.Position.Z - 1000f);
            server.Tick(0.1);

            Assert.False(server.InShipInterior("Pilot")); // turned into an EVA, not a fall
            Assert.True(server.InSpace("Pilot"));
            Assert.True(p.InEva);
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
