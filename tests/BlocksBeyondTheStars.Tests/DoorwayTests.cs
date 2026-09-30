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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A school-club report, "the door won't open" (#2149): a door hung in a self-built house on a flower world. Three
/// ways a player-built door ended up not opening — a block or a second door built into its doorway (#2145), a flower
/// taken for a jamb so the door hung crosswise (#2146), and the starter ship's hatch walled up from inside (#2148).
/// </summary>
public sealed class DoorwayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_doorway_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string name, NpcLifeWorld.RecordingTransport transport, out SqliteWorldRepository repo,
        bool starterShip = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = starterShip,
            PlaceSettlements = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A builder up in the air, so every target cell around (1, 200, 0) is empty.</summary>
    private static PlayerSession Builder(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(0, 200, 0);
        p.State.Inventory.Add("door_hinge", 3, 99);
        p.State.Inventory.Add("iron_wall", 10, 99);
        return p;
    }

    private static string? LastRejection(NpcLifeWorld.RecordingTransport t, PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;

    [Fact]
    public void ABlock_IsRefusedAnywhereInADoorway_AndCostsNothing()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("doorway_block", t, out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "door_hinge");
            Assert.Equal(1, server.DoorCount);

            // The door's opening is three cells high: none of them takes a block, and the refused block stays in the pack.
            for (int y = 200; y <= 202; y++)
            {
                server.PlaceBlock("Builder", 1, y, 0, "iron_wall");
                Assert.True(server.World.GetBlock(new Vector3i(1, y, 0)).IsAir);
                Assert.Equal("@srv.place.door_here", LastRejection(t, p));
            }

            Assert.Equal(10, p.State.Inventory.CountOf("iron_wall"));

            // Beside the doorway and above it, building is as free as ever.
            server.PlaceBlock("Builder", 2, 200, 0, "iron_wall");
            server.PlaceBlock("Builder", 1, 203, 0, "iron_wall");
            Assert.False(server.World.GetBlock(new Vector3i(2, 200, 0)).IsAir);
            Assert.False(server.World.GetBlock(new Vector3i(1, 203, 0)).IsAir);
        }
    }

    [Fact]
    public void ASecondDoor_IsRefusedInTheSameDoorway()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("doorway_stack", t, out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock("Builder", 1, 200, 0, "door_hinge");
            server.PlaceBlock("Builder", 1, 200, 0, "door_hinge"); // the right-click at the door, the Minecraft way
            server.PlaceBlock("Builder", 1, 201, 0, "door_hinge"); // …or a little higher up its leaf

            Assert.Equal(1, server.DoorCount); // one door — E swings it, nothing shut is left behind it
            Assert.Equal("@srv.place.door_here", LastRejection(t, p));
            Assert.Equal(2, p.State.Inventory.CountOf("door_hinge"));
            Assert.Single(repo.ListDoors(server.ActiveLocationId));
        }
    }

    [Fact]
    public void AFlowerInFrontOfTheDoorway_IsNoJamb()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("doorway_flower", t, out var repo);
        using (repo)
        {
            var p = Builder(server);
            var stone = _content.GetBlock("stone")!.NumericId;
            var flower = _content.GetBlock("flora_flower")!.NumericId;

            // A 1-wide gap in a wall along X, with a flower growing right in front of it.
            server.World.SetBlock(new Vector3i(0, 200, 0), stone);
            server.World.SetBlock(new Vector3i(2, 200, 0), stone);
            server.World.SetBlock(new Vector3i(1, 200, -1), flower);

            // Looking along +X: had the flower counted, jambs on both axes would have turned the door to face this way.
            p.State.Position = new Vector3f(1.5f, 200, 3.5f);
            p.State.Yaw = 90f;
            server.PlaceBlock("Builder", 1, 200, 0, "door_hinge");

            var door = Assert.Single(server.DoorFits);
            Assert.True(door.AxisX); // hangs along the wall, closing the gap
        }
    }

    [Fact]
    public void TheJambRule_CountsWallsNotFlora()
    {
        bool Jamb(string key) => DoorProbe.IsJamb(_content.GetBlock(key));

        Assert.True(Jamb("stone"));
        Assert.True(Jamb("iron_wall"));
        Assert.True(Jamb("glass"));
        Assert.True(Jamb("tree_leaves")); // a hedge is a wall you bump into

        Assert.False(Jamb("flora_flower"));
        Assert.False(Jamb("flora_bush"));
        Assert.False(Jamb("torch"));
        Assert.False(Jamb("water"));
        Assert.False(Jamb("lava"));
        Assert.False(DoorProbe.IsJamb(null));
    }

    /// <summary>Landed on the planet (the landed-ship edit) and in the ship interior out in space (the structure edit):
    /// the hatch opening takes no block either way, the cabin beside it still does.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheStarterShipsHatch_CannotBeWalledUp(bool inSpace)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer(inSpace ? "doorway_hatch_space" : "doorway_hatch", t, out var repo, starterShip: true);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true;
            if (inSpace)
            {
                server.EnterSpace("Pilot");
                server.EnterShipInterior("Pilot");
                Assert.True(server.InShipInterior("Pilot"));
            }

            var hatch = server.BuildShipStructureForTest("Pilot").DoorCells.Single(c => c.Z == 0); // the rear-wall hatch
            var (origin, _) = server.LandedShipBoundsForTest("Pilot");
            pilot.State.Position = new Vector3f(origin.X + hatch.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);

            // What one build sends back to the pilot: its refusal, or the changed cell.
            (string? Refusal, bool Changed) Build(int x, int y, int z)
            {
                int before = t.Sent.Count;
                server.HandleStructureEditForTest("Pilot", new StructureEditIntent { StructureId = "ship:Pilot", X = x, Y = y, Z = z, ItemKey = "iron_wall" });
                var fresh = t.Sent.Skip(before).Select(s => s.Msg).ToList();
                return (fresh.OfType<ActionRejected>().LastOrDefault()?.Reason, fresh.OfType<StructureBlockChanged>().Any());
            }

            // The three-wide, three-high opening stays open, however it is aimed at.
            foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (1, 0), (0, 1), (0, 2) })
            {
                var (refusal, changed) = Build(hatch.X + dx, hatch.Y + dy, hatch.Z);
                Assert.Equal("@srv.place.door_here", refusal);
                Assert.False(changed);
            }

            // Furnishing the cabin beside it still works.
            var cabin = Build(hatch.X, hatch.Y, hatch.Z + 2);
            Assert.Null(cabin.Refusal);
            Assert.True(cabin.Changed);
        }
    }
}
