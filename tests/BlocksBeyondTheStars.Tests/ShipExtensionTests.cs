// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Growing an authored ship (#2119 – #2121). Two player reports: "all doors are just blocks" (a door built onto the
/// ship on a spacewalk stayed a cube) and "I extended my ship at the back, but as soon as I go through the door my
/// area doesn't count as ship" (the hatch threw him out into a spacewalk at the design box's edge).
/// </summary>
public sealed class ShipExtensionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipExtensionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shipext_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

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

    private SvGameServer Started(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Into the ship interior in space, standing just inside the rear hatch, building for free.</summary>
    private static (PlayerSession Pilot, Vector3i Origin, int Width, int Top, int HatchX) Inside(SvGameServer server)
    {
        var pilot = server.AddLocalPlayer("Pilot");
        pilot.State.InstantBuild = true;
        server.EnterSpace("Pilot");
        server.EnterShipInterior("Pilot");
        Assert.True(server.InShipInterior("Pilot"));

        var (origin, size) = server.LandedShipBoundsForTest("Pilot");
        var hatch = server.DoorFits.Single(d => (int)Math.Floor(d.Pos.Z) == origin.Z); // the rear-wall hatch
        int hatchX = (int)Math.Floor(hatch.Pos.X) - origin.X;
        pilot.State.Position = new Vector3f(origin.X + hatchX + 0.5f, origin.Y + 1f, origin.Z + 1.5f);
        return (pilot, origin, size.X, size.Y - 1, hatchX);
    }

    private static void Build(SvGameServer server, int x, int y, int z, string item)
        => server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = "ship:Pilot", X = x, Y = y, Z = z, ItemKey = item });

    private static void Mine(SvGameServer server, int x, int y, int z)
        => server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = "ship:Pilot", X = x, Y = y, Z = z, Mine = true });

    /// <summary>A room behind the rear hatch, as wide and as tall as the hull: floor, two side walls, a back wall and a
    /// roof of iron walls, built in an order where every block touches one already there. The hatch (an energy door)
    /// closes its front. Returns the back wall's centre cell.</summary>
    private static Vector3i BuildRoomBehindTheHatch(SvGameServer server, int width, int top, int hatchX, bool roof = true)
    {
        const int Depth = 4; // interior z = -1 .. -4, back wall at -5
        for (int z = -1; z >= -Depth - 1; z--)
        {
            for (int x = 0; x < width; x++) { Build(server, x, 0, z, "iron_wall"); }
            Build(server, -1, 0, z, "iron_wall");
            Build(server, width, 0, z, "iron_wall");
        }

        for (int y = 1; y <= top; y++)
        {
            for (int z = -1; z >= -Depth - 1; z--)
            {
                Build(server, -1, y, z, "iron_wall");
                Build(server, width, y, z, "iron_wall");
            }

            for (int x = 0; x < width; x++) { Build(server, x, y, -Depth - 1, "iron_wall"); }
        }

        if (roof)
        {
            for (int z = -1; z >= -Depth; z--)
            {
                for (int x = 0; x < width; x++) { Build(server, x, top, z, "iron_wall"); }
            }
        }

        return new Vector3i(hatchX, 2, -Depth - 1);
    }

    [Fact]
    public void ASealedRoomBuiltBehindTheHatch_IsShip_YouWalkInAndBreathe()
    {
        var server = Started("ext_sealed", out var repo);
        using (repo)
        {
            var (pilot, origin, width, top, hatchX) = Inside(server);
            BuildRoomBehindTheHatch(server, width, top, hatchX);
            Assert.True(server.ShipCellSealedForTest("Pilot", hatchX, 1, -3));

            // Walk through the hatch into the room: still inside the ship, aboard, no spacewalk, no drain.
            pilot.State.Position = new Vector3f(origin.X + hatchX + 0.5f, origin.Y + 1f, origin.Z - 2.5f);
            pilot.State.Oxygen = 50f;
            server.Tick(0.5);

            Assert.True(server.InShipInterior("Pilot"));
            Assert.False(pilot.State.InEva);
            Assert.True(pilot.State.AboardShip);
            Assert.True(pilot.State.Oxygen > 50f, "the sealed room breathes");
        }
    }

    [Fact]
    public void AnOpenExtension_IsNoShipAir_ButNoSpacewalkEither()
    {
        var server = Started("ext_open", out var repo);
        using (repo)
        {
            var (pilot, origin, width, top, hatchX) = Inside(server);
            BuildRoomBehindTheHatch(server, width, top, hatchX, roof: false); // no roof: open to space
            Assert.False(server.ShipCellSealedForTest("Pilot", hatchX, 1, -3));

            pilot.State.Position = new Vector3f(origin.X + hatchX + 0.5f, origin.Y + 1f, origin.Z - 2.5f);
            pilot.State.Oxygen = 50f;
            server.Tick(0.5);

            Assert.True(server.InShipInterior("Pilot"), "inside the ship's cells — no spacewalk");
            Assert.False(pilot.State.InEva);
            Assert.False(pilot.State.AboardShip);
            Assert.True(pilot.State.Oxygen < 50f, "helmet on: out here there is no air");
        }
    }

    [Fact]
    public void AHoleInTheExtension_LetsTheAirOut()
    {
        var server = Started("ext_hole", out var repo);
        using (repo)
        {
            var (_, _, width, top, hatchX) = Inside(server);
            var back = BuildRoomBehindTheHatch(server, width, top, hatchX);
            Assert.True(server.ShipCellSealedForTest("Pilot", hatchX, 1, -3));

            Mine(server, back.X, back.Y, back.Z); // an owner-built block comes out again on foot
            Assert.False(server.ShipCellSealedForTest("Pilot", hatchX, 1, -3));
        }
    }

    [Fact]
    public void WalkingFarOutOfTheShip_StillStartsASpacewalk()
    {
        var server = Started("ext_eject", out var repo);
        using (repo)
        {
            var (pilot, origin, width, top, hatchX) = Inside(server);
            BuildRoomBehindTheHatch(server, width, top, hatchX);

            pilot.State.Position = new Vector3f(origin.X + hatchX + 0.5f, origin.Y + 1f, origin.Z - 40f);
            server.Tick(0.1);

            Assert.False(server.InShipInterior("Pilot"));
            Assert.True(pilot.State.InEva);
        }
    }

    [Fact]
    public void TheShipGrowsNoBiggerThan15()
    {
        var server = Started("ext_cap", out var repo);
        using (repo)
        {
            var (_, _, _, _, hatchX) = Inside(server);

            // A long floor strip out of the hatch: each block touches the last, until the ship would pass 15 long.
            var (_, size) = server.LandedShipBoundsForTest("Pilot");
            for (int z = -1; z >= -20; z--)
            {
                Build(server, hatchX, 0, z, "iron_wall");
            }

            var s = server.BuildShipStructureForTest("Pilot");
            int minZ = s.Cells.Keys.Min(c => c.Z);
            int maxZ = s.Cells.Keys.Max(c => c.Z);
            Assert.True(maxZ - minZ + 1 <= Math.Max(15, size.Z), $"the ship is {maxZ - minZ + 1} long");
            Assert.True(minZ < -1, "it did grow");
        }
    }

    [Fact]
    public void ADoorBuiltOnASpacewalk_IsARealDoor_AndStaysOne()
    {
        var server = Started("ext_evadoor", out var repo);
        using (repo)
        {
            var (pilot, origin, _, _, hatchX) = Inside(server);

            // Out through the hatch onto a spacewalk, then hang a door on the hull behind it.
            pilot.State.Position = new Vector3f(pilot.State.Position.X, pilot.State.Position.Y, origin.Z - 1000f);
            server.Tick(0.1);
            Assert.True(pilot.State.InEva);

            var cell = new Vector3i(hatchX, 1, -2);
            Build(server, cell.X, cell.Y, cell.Z, "door_energy");
            Assert.Equal((ushort)0, server.StructureBlockForTest("Pilot", cell.X, cell.Y, cell.Z)); // a doorway, not a cube

            // Persisted as a door: the rebuilt ship carries it as a doorway with its kind.
            var rebuilt = server.BuildShipStructureForTest("Pilot");
            Assert.True(rebuilt.PlacedDoorAxes.ContainsKey(cell));
            Assert.Equal("energy", rebuilt.DoorKinds[cell]);
            Assert.True(rebuilt.Get(cell).IsAir);

            // Back inside: the door hangs in the world — one cell wide.
            server.EnterShipInterior("Pilot");
            Assert.Contains(server.PlacedShipDoorsForTest("Pilot"), d => d.Cell.Equals(cell) && d.Kind == "energy");
            var (o, _) = server.LandedShipBoundsForTest("Pilot");
            Assert.Contains(server.DoorFits, d => d.Kind == "energy" && d.Width == 1f
                && (int)Math.Floor(d.Pos.X) == o.X + cell.X && (int)Math.Floor(d.Pos.Z) == o.Z + cell.Z);
        }
    }

    [Fact]
    public void ADoorBuiltInTheCabin_IsADoor_AndComesOutAgain()
    {
        var server = Started("ext_cabindoor", out var repo);
        using (repo)
        {
            var (pilot, origin, _, _, hatchX) = Inside(server);
            pilot.State.InstantBuild = false;
            pilot.State.Inventory.Add("door_slide", 1, 99);

            var cell = new Vector3i(hatchX, 1, 2);
            Build(server, cell.X, cell.Y, cell.Z, "door_slide");

            Assert.Equal(0, pilot.State.Inventory.CountOf("door_slide"));
            Assert.Contains(server.PlacedShipDoorsForTest("Pilot"), d => d.Cell.Equals(cell) && d.Kind == "slide");
            Assert.Contains(server.DoorFits, d => d.Kind == "slide"
                && (int)Math.Floor(d.Pos.X) == origin.X + cell.X && (int)Math.Floor(d.Pos.Z) == origin.Z + cell.Z);

            Mine(server, cell.X, cell.Y + 1, cell.Z); // aiming at the door's upper half picks it up too
            Assert.Empty(server.PlacedShipDoorsForTest("Pilot"));
            Assert.Equal(1, pilot.State.Inventory.CountOf("door_slide"));
            Assert.DoesNotContain(server.DoorFits, d => d.Kind == "slide");
        }
    }

    [Fact]
    public void AHullCellTheOwnerTookOut_IsNoRepairDamage_AHitIs()
    {
        var server = Started("ext_repair", out var repo);
        using (repo)
        {
            var (pilot, origin, width, _, _) = Inside(server);
            pilot.State.Position = new Vector3f(pilot.State.Position.X, pilot.State.Position.Y, origin.Z - 1000f);
            server.Tick(0.1);
            Assert.True(pilot.State.InEva);
            Assert.Equal(0, server.ShipRepairMissingCellsForTest("Pilot"));

            // The owner cuts a hull cell out of the rear wall on a spacewalk: that's design, not damage.
            Mine(server, 0, 2, 0);
            Assert.Equal((ushort)0, server.StructureBlockForTest("Pilot", 0, 2, 0));
            Assert.Equal(0, server.ShipRepairMissingCellsForTest("Pilot"));

            // A hit still is damage.
            server.CarveFirstShipCellForTest("Pilot");
            Assert.Equal(1, server.ShipRepairMissingCellsForTest("Pilot"));
            _ = width;
        }
    }
}
