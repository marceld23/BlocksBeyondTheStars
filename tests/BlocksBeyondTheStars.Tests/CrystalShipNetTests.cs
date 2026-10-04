// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2268: the Crystal Net aboard the own ship. A parked ship (landed, or the walkable interior out in space) carries a net
/// of its own in ship-local cells: a switch lights a cabin lamp, a ship door follows its ship's wiring, the ship sensor
/// reads the ship — and only the owner (and their alliance) operates it. In flight the net rests; it comes back from its
/// rows whenever the ship is parked again.
/// </summary>
public sealed class CrystalShipNetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_shipnet_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    private SvGameServer NewServer(string name, NpcLifeWorld.RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
            PlaceSettlements = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>The pilot inside the parked starter ship, two cells in from the rear hatch, and the hatch cell.</summary>
    private static (PlayerSession Pilot, Vector3i Hatch) Aboard(SvGameServer server, string name = "Pilot")
    {
        var pilot = server.AddLocalPlayer(name);
        pilot.State.InstantBuild = true;
        var hatch = server.BuildShipStructureForTest(name).DoorCells.Single(c => c.Z == 0);
        var (origin, _) = server.LandedShipBoundsForTest(name);
        pilot.State.Position = new Vector3f(origin.X + hatch.X + 0.5f, origin.Y + 1f, origin.Z + 3.5f);
        return (pilot, hatch);
    }

    private static void Build(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who, Vector3i cell, string item, int deviceDir = -1)
    {
        int before = t.Sent.Count;
        server.HandleStructureEditForTest(who.State.PlayerId, new StructureEditIntent
        {
            StructureId = "ship:" + who.State.PlayerId,
            X = cell.X,
            Y = cell.Y,
            Z = cell.Z,
            ItemKey = item,
            DeviceDir = deviceDir,
        });
        string? refusal = t.Sent.Skip(before).Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;
        Assert.Null(refusal);
    }

    private static void Run(SvGameServer server, double seconds)
    {
        for (double s = 0; s < seconds; s += 0.1)
        {
            server.Tick(0.1);
        }
    }

    [Fact]
    public void ASwitchAboard_LightsTheCabinLamp_AndTheParkedHullShowsIt()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipnet_lamp", t, out var repo);
        using (repo)
        {
            var (pilot, hatch) = Aboard(server);
            var sw = new Vector3i(hatch.X, hatch.Y, hatch.Z + 2);
            var lamp = new Vector3i(sw.X, sw.Y + 1, sw.Z);
            Build(server, t, pilot, sw, "crystal_switch");
            Build(server, t, pilot, lamp, "light_white");

            Assert.Equal("crystal_switch", server.CrystalShipDeviceKeyForTest("Pilot", sw));
            Assert.NotNull(server.CrystalShipDeviceKeyForTest("Pilot", lamp)); // the lamp joined: a net cell touches it

            Run(server, 1.0);
            Assert.False(server.CrystalShipLevelAtForTest("Pilot", sw));
            Assert.Equal("light_white_off", server.ParkedShipBlockKeyForTest("Pilot", lamp)); // an OFF net: the lamp is dark

            server.SetCrystalShipDeviceForTest(pilot, "Pilot", sw, action: 0);
            Run(server, 1.0);
            Assert.True(server.CrystalShipLevelAtForTest("Pilot", sw));
            Assert.Equal("light_white", server.ParkedShipBlockKeyForTest("Pilot", lamp));

            // The swap reached the clients as a cell of the ship's structure, and the lists name the ship as their frame.
            Assert.Contains(t.Sent.Select(s => s.Msg).OfType<StructureBlockChanged>(),
                m => m.StructureId == "ship:Pilot" && m.X == lamp.X && m.Y == lamp.Y && m.Z == lamp.Z);
            Assert.Contains(t.Sent.Select(s => s.Msg).OfType<CrystalNetList>(), m => m.Frame == "ship:Pilot" && m.Nets.Any(n => n.On));
        }
    }

    [Fact]
    public void InFlight_TheNetRests_AndStepsBackInWithTheShip()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipnet_flight", t, out var repo);
        using (repo)
        {
            var (pilot, hatch) = Aboard(server);
            var sw = new Vector3i(hatch.X, hatch.Y, hatch.Z + 2);
            Build(server, t, pilot, sw, "crystal_switch");
            server.SetCrystalShipDeviceForTest(pilot, "Pilot", sw, action: 0);
            Run(server, 0.5);
            Assert.True(server.CrystalShipLevelAtForTest("Pilot", sw));

            server.EnterSpace("Pilot");
            Assert.Null(server.CrystalShipLevelAtForTest("Pilot", sw)); // in flight: no net

            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));
            Run(server, 0.5);
            Assert.True(server.CrystalShipLevelAtForTest("Pilot", sw)); // the lever stayed where it was
        }
    }

    [Fact]
    public void AStranger_CannotFlipTheOwnersSwitchAboard()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipnet_stranger", t, out var repo);
        using (repo)
        {
            var (pilot, hatch) = Aboard(server);
            var sw = new Vector3i(hatch.X, hatch.Y, hatch.Z + 2);
            Build(server, t, pilot, sw, "crystal_switch");
            var stranger = server.AddLocalPlayer("Stranger");
            stranger.State.Position = pilot.State.Position;

            int before = t.Sent.Count;
            server.SetCrystalShipDeviceForTest(stranger, "Pilot", sw, action: 0);
            Assert.Contains(t.Sent.Skip(before).Where(s => s.Conn == stranger.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>(),
                r => r.Reason == "@srv.crystal.owner_only");
            Run(server, 0.5);
            Assert.False(server.CrystalShipDeviceOutputForTest("Pilot", sw));
        }
    }

    [Fact]
    public void TheShipSensor_ReadsItsShip()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipnet_sensor", t, out var repo);
        using (repo)
        {
            var (pilot, hatch) = Aboard(server);
            var sensor = new Vector3i(hatch.X, hatch.Y, hatch.Z + 2);
            Build(server, t, pilot, sensor, "ship_sensor");
            var ship = pilot.Ships[pilot.ActiveShipId];

            // "Hull damaged" (mode 0): a whole hull reads OFF, a scratched one ON.
            Run(server, 1.0);
            Assert.False(server.CrystalShipDeviceOutputForTest("Pilot", sensor));
            ship.Hull = 1f;
            Run(server, 1.0);
            Assert.True(server.CrystalShipDeviceOutputForTest("Pilot", sensor));

            // "Landed" (mode 3): parked on a planet.
            server.SetCrystalShipDeviceForTest(pilot, "Pilot", sensor, action: 2, mode: (int)ShipSensorMode.Landed);
            Run(server, 1.0);
            Assert.True(server.CrystalShipDeviceOutputForTest("Pilot", sensor));
        }
    }

    [Fact]
    public void TheShipsOwnHatch_FollowsTheShipsWiring()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipnet_hatch", t, out var repo);
        using (repo)
        {
            var (pilot, hatch) = Aboard(server);
            Assert.Equal(DoorMode.Normal, server.ShipDoorModeForTest("Pilot"));

            // A conduit along the inside of the rear wall, in front of the hatch, and a switch on it.
            for (int dx = -1; dx <= 1; dx++)
            {
                Build(server, t, pilot, new Vector3i(hatch.X + dx, hatch.Y, hatch.Z + 1), "crystal_conduit");
            }

            var sw = new Vector3i(hatch.X, hatch.Y, hatch.Z + 2);
            Build(server, t, pilot, sw, "crystal_switch");

            Run(server, 0.5);
            Assert.Equal(DoorMode.Locked, server.ShipDoorModeForTest("Pilot")); // wired and OFF: locked

            server.SetCrystalShipDeviceForTest(pilot, "Pilot", sw, action: 0);
            Run(server, 0.5);
            Assert.Equal(DoorMode.HeldOpen, server.ShipDoorModeForTest("Pilot"));
        }
    }

    [Fact]
    public void AMinedDeviceAboard_LeavesTheShipsNet_AndItsRowIsGone()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipnet_mine", t, out var repo);
        using (repo)
        {
            var (pilot, hatch) = Aboard(server);
            var sw = new Vector3i(hatch.X, hatch.Y, hatch.Z + 2);
            Build(server, t, pilot, sw, "crystal_switch");
            Assert.Contains(repo.ListCrystalCells("ship:Pilot"), r => r.X == sw.X && r.Y == sw.Y && r.Z == sw.Z);

            server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = "ship:Pilot", X = sw.X, Y = sw.Y, Z = sw.Z, Mine = true });
            Assert.Null(server.CrystalShipDeviceKeyForTest("Pilot", sw));
            Assert.DoesNotContain(repo.ListCrystalCells("ship:Pilot"), r => r.X == sw.X && r.Y == sw.Y && r.Z == sw.Z);
        }
    }
}
