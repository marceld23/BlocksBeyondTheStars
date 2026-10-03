// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A block that only works in a world's block grid cannot be built into a ship (#2219). The bio lab is found by
/// scanning the chunk grid around the player and a Crystal Net device registers its cell on a world place; built into
/// the own ship — a structure object — either one was accepted, used up its item and then did nothing, without a word.
/// Every path that turns a placed item into a ship cell refuses them now and keeps the item; the ground, a base and a
/// station take them as before.
/// </summary>
public sealed class ShipFunctionBlockTests : IDisposable
{
    private const string Refusal = "@srv.ship.block_needs_ground";

    /// <summary>The bio lab and every block that is a Crystal Net device by itself (not a conduit, not a lamp and not
    /// one of the older blocks that merely gained a port).</summary>
    private static readonly string[] WorldOnly =
    {
        "bio_lab", "clone_tank", "caller", "crystal_switch", "crystal_button", "step_plate", "proximity_sensor",
        "daylight_sensor", "storage_sensor", "watcher", "logic_block", "timer_block", "alarm_siren", "chime", "horn",
        "melody_block", "announcer", "fabricator", "auto_drill_1", "auto_drill_2", "auto_drill_3", "matter_sender",
        "matter_receiver", "drill_laser", "rail_stop", "device_eye",
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_shipfn_" + Guid.NewGuid().ToString("N"));
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
        bool starterShip = false, Action<ServerConfig>? configure = null)
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
        configure?.Invoke(config);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private BlockId Block(string key) => _content.GetBlock(key)!.NumericId;

    /// <summary>An empty backpack: a test counts exactly what it hands out.</summary>
    private static void EmptyPack(PlayerSession p)
    {
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }
    }

    private static void Give(PlayerSession p, string item)
        => Assert.Equal(0, p.State.Inventory.Add(item, 1, 1));

    /// <summary>One build into a structure, and what came back to the builder: the refusal, or nothing.</summary>
    private static string? Build(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who, string structureId,
        Vector3i cell, string item)
    {
        int before = t.Sent.Count;
        server.HandleStructureEditForTest(who.State.PlayerId,
            new StructureEditIntent { StructureId = structureId, X = cell.X, Y = cell.Y, Z = cell.Z, ItemKey = item });
        return t.Sent.Skip(before).Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg)
            .OfType<ActionRejected>().LastOrDefault()?.Reason;
    }

    /// <summary>Tries every world-only block at one cell of a structure, in a survival pack that holds exactly that one
    /// item: each is refused with the line that says where it works, and each is still in the pack afterwards.</summary>
    private static void AssertAllRefusedAndKept(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who,
        string structureId, Vector3i cell)
    {
        who.State.InstantBuild = false; // survival rules: an accepted block would be taken out of the pack
        foreach (string item in WorldOnly)
        {
            Give(who, item);
            Assert.Equal(Refusal, Build(server, t, who, structureId, cell, item));
            Assert.Equal(1, who.State.Inventory.CountOf(item));
            who.State.Inventory.Remove(item, 1);
        }
    }

    /// <summary>The starter ship's pilot standing just inside the rear hatch — parked on the planet, or in the walkable
    /// interior out in space — and a free cabin cell beside them.</summary>
    private static (PlayerSession Pilot, Vector3i Cell) InTheCabin(SvGameServer server, bool inSpace)
    {
        var pilot = server.AddLocalPlayer("Pilot");
        EmptyPack(pilot);
        if (inSpace)
        {
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));
        }

        var hatch = server.BuildShipStructureForTest("Pilot").DoorCells.Single(c => c.Z == 0); // the rear-wall hatch
        var (origin, _) = server.LandedShipBoundsForTest("Pilot");
        pilot.State.Position = new Vector3f(origin.X + hatch.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);
        return (pilot, new Vector3i(hatch.X, hatch.Y, hatch.Z + 2));
    }

    // ---------------- The ship refuses them ----------------

    /// <summary>The landed-ship edit on the planet and the same edit in the ship interior out in space.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InTheCabin_ALabOrACrystalDevice_IsRefused_AndStaysInThePack(bool inSpace)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer(inSpace ? "shipfn_cabin_space" : "shipfn_cabin", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace);

            AssertAllRefusedAndKept(server, t, pilot, "ship:Pilot", cell);
            Assert.True(server.BuildShipStructureForTest("Pilot").Get(cell).IsAir);

            // The same cell takes an ordinary block: the refusal was about the block, not about the place.
            Give(pilot, "iron_wall");
            Assert.Null(Build(server, t, pilot, "ship:Pilot", cell, "iron_wall"));
            Assert.Equal(Block("iron_wall"), server.BuildShipStructureForTest("Pilot").Get(cell));
            Assert.Equal(0, pilot.State.Inventory.CountOf("iron_wall"));
        }
    }

    /// <summary>A conduit and a lamp are furnishing, and so are the older blocks that only gained a port (here the hydro
    /// tray): they are ordinary blocks until a conduit meets them, and a ship takes them as it always did.</summary>
    [Theory]
    [InlineData("crystal_conduit")]
    [InlineData("light_white")]
    [InlineData("hydro_tray")]
    public void InTheCabin_AConduitALampAndAPortBlock_AreStillBuilt(string item)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_furnish_" + item, t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace: false);
            Give(pilot, item);

            Assert.Null(Build(server, t, pilot, "ship:Pilot", cell, item));

            Assert.Equal(Block(item), server.BuildShipStructureForTest("Pilot").Get(cell));
            Assert.Equal(0, pilot.State.Inventory.CountOf(item));
        }
    }

    [Fact]
    public void OnASpacewalk_TheOwnShipsHull_RefusesThemToo()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_eva", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, _) = InTheCabin(server, inSpace: true);
            var (origin, _) = server.LandedShipBoundsForTest("Pilot");
            int hatchX = server.BuildShipStructureForTest("Pilot").DoorCells.Single(c => c.Z == 0).X;

            // Out through the hatch onto a spacewalk; the cell hangs on the hull behind it.
            pilot.State.Position = new Vector3f(pilot.State.Position.X, pilot.State.Position.Y, origin.Z - 1000f);
            server.Tick(0.1);
            Assert.True(pilot.State.InEva);
            var cell = new Vector3i(hatchX, 1, -2);

            AssertAllRefusedAndKept(server, t, pilot, "ship:Pilot", cell);
            Assert.Equal((ushort)0, server.StructureBlockForTest("Pilot", cell.X, cell.Y, cell.Z));

            Give(pilot, "iron_wall");
            Assert.Null(Build(server, t, pilot, "ship:Pilot", cell, "iron_wall"));
            Assert.Equal(Block("iron_wall").Value, server.StructureBlockForTest("Pilot", cell.X, cell.Y, cell.Z));
        }
    }

    /// <summary>The keel's construction site, and the same hull once it is commissioned and parked.</summary>
    [Fact]
    public void ASelfBuiltShip_RefusesThem_OnTheSiteAndOnceCommissioned()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        // The keel goes down at a fixed column, which the newer relief may flood or bury — the classic generation
        // keeps it dry, like the other self-built-ship tests.
        var server = NewServer("shipfn_keel", t, out var repo, configure: c => c.World.TerrainGeneration = 0);
        using (repo)
        {
            server.AddLocalPlayer("Host");
            var pilot = server.AddLocalPlayer("Pilot");
            EmptyPack(pilot);
            const string Yard = "shipyard:Pilot";

            int groundY = 220;
            while (groundY > 1 && server.World.GetBlock(new Vector3i(40, groundY, 40)).IsAir)
            {
                groundY--;
            }

            var keel = new Vector3i(40, groundY + 1, 40);
            pilot.State.InstantBuild = true;
            pilot.State.Position = new Vector3f(keel.X + 2.5f, keel.Y + 1.5f, keel.Z + 2.5f);
            server.PlaceShipCoreForTest("Pilot", keel.X, keel.Y, keel.Z);
            var ship = pilot.Ships.Values.Single(s => s.IsCustom);
            string keelOnly = ship.BuiltCells;

            // The construction site: nothing joins the hull, nothing leaves the pack.
            AssertAllRefusedAndKept(server, t, pilot, Yard, new Vector3i(1, 0, 0));
            Assert.Equal(keelOnly, ship.BuiltCells);

            // A valid 5 × 4 × 5 hull around the keel: floor, wall ring with a door, roof, a helm and an engine.
            pilot.State.InstantBuild = true;
            void Hull(int x, int y, int z, string item) => Build(server, t, pilot, Yard, new Vector3i(x, y, z), item);
            for (int x = 0; x < 5; x++)
            {
                for (int z = 0; z < 5; z++)
                {
                    if (x != 0 || z != 0)
                    {
                        Hull(x, 0, z, "iron_wall");
                    }
                }
            }

            for (int y = 1; y <= 2; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    for (int z = 0; z < 5; z++)
                    {
                        bool ring = x == 0 || x == 4 || z == 0 || z == 4;
                        if (ring && !(x == 1 && y == 1 && z == 0))
                        {
                            Hull(x, y, z, "iron_wall");
                        }
                    }
                }
            }

            Hull(1, 1, 0, "door_slide");
            for (int x = 0; x < 5; x++)
            {
                for (int z = 0; z < 5; z++)
                {
                    Hull(x, 3, z, "iron_wall");
                }
            }

            Hull(2, 1, 2, "ship_helm");
            Hull(1, 1, 1, "ship_engine");
            pilot.State.Position = new Vector3f(keel.X + 2.5f, keel.Y + 1f, keel.Z + 2.5f); // at the helm
            server.CommissionShipForTest("Pilot");
            Assert.True(ship.Commissioned);

            // The commissioned ship: a free cabin cell on the floor takes none of them either.
            string commissioned = ship.BuiltCells;
            var cabin = new Vector3i(3, 1, 3);
            AssertAllRefusedAndKept(server, t, pilot, "ship:Pilot", cabin);
            Assert.Equal(commissioned, ship.BuiltCells);

            Give(pilot, "iron_wall");
            Assert.Null(Build(server, t, pilot, "ship:Pilot", cabin, "iron_wall"));
            Assert.NotEqual(commissioned, ship.BuiltCells);
            Assert.Equal(0, pilot.State.Inventory.CountOf("iron_wall"));
        }
    }

    // ---------------- Everywhere else they are built, and they work ----------------

    /// <summary>On open ground and inside the own base the same blocks go into the world grid: the item is used, the
    /// device joins the Crystal Net and the lab analyses.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnTheGround_AndInTheOwnBase_TheyAreBuilt_AndWork(bool inBase)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer(inBase ? "shipfn_base" : "shipfn_ground", t, out var repo);
        using (repo)
        {
            // Mid-air on rocky, below its atmosphere line: every cell around is free and in reach.
            var p = server.AddLocalPlayer("Builder");
            EmptyPack(p);
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 120, 0);
            if (inBase)
            {
                Give(p, "base_core");
                server.PlaceBlock("Builder", -1, 120, 0, "base_core");
                Assert.Single(server.BaseSnapshots);
            }

            var lab = new Vector3i(1, 120, 0);
            var tank = new Vector3i(1, 120, 2);
            var lever = new Vector3i(1, 120, -2);
            Give(p, "bio_lab");
            Give(p, "clone_tank");
            Give(p, "crystal_switch");
            server.PlaceBlock("Builder", lab.X, lab.Y, lab.Z, "bio_lab");
            server.PlaceBlock("Builder", tank.X, tank.Y, tank.Z, "clone_tank");
            server.PlaceBlock("Builder", lever.X, lever.Y, lever.Z, "crystal_switch");

            Assert.Equal(Block("bio_lab"), server.World.GetBlock(lab));
            Assert.Equal(Block("clone_tank"), server.World.GetBlock(tank));
            Assert.Equal(Block("crystal_switch"), server.World.GetBlock(lever));
            Assert.Equal(0, p.State.Inventory.CountOf("bio_lab") + p.State.Inventory.CountOf("clone_tank")
                + p.State.Inventory.CountOf("crystal_switch"));

            // Both devices are Crystal Net cells of their own, and the lab answers.
            Assert.Equal(2, server.CrystalCellCount);
            Assert.NotNull(server.CrystalDeviceOutput(lever));
            AssertTheLabAnalyses(server, p);
        }
    }

    /// <summary>A station's build is stamped into a world grid when it is boarded, so a lab built onto the own station
    /// on a spacewalk is a working lab on its deck — and the deck itself takes a lab and a device like any ground.</summary>
    [Fact]
    public void OnTheOwnStation_ALabBuiltOnASpacewalk_Works_AndTheDeckTakesThem()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_station", t, out var repo);
        using (repo)
        {
            var owner = server.AddLocalPlayer("Owner");
            EmptyPack(owner);
            server.EnterSpace("Owner");
            owner.State.InEva = true;
            owner.State.InstantBuild = true;
            server.DeployStationCoreForTest("Owner");
            string id = server.OwnedStationIdForTest("Owner")!;

            // A small hull along +X and an airlock door, so the station commissions.
            for (int i = 1; i <= 11; i++)
            {
                Build(server, t, owner, id, new Vector3i(i, 0, 0), "iron_wall");
            }

            Build(server, t, owner, id, new Vector3i(0, 1, 0), "door_slide");
            Assert.True(server.StationIsBoardableForTest(id));

            // The lab goes onto the station on the spacewalk — accepted, and paid for like any block.
            owner.State.InstantBuild = false;
            Give(owner, "bio_lab");
            Assert.Null(Build(server, t, owner, id, new Vector3i(1, 1, 0), "bio_lab"));
            Assert.Equal(Block("bio_lab").Value, server.StructureCellForTest(id, 1, 1, 0));
            Assert.Equal(0, owner.State.Inventory.CountOf("bio_lab"));

            // Aboard, it stands in the deck's world grid and works.
            var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == id);
            server.ShipMove("Owner", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
            server.BoardStation("Owner", id);
            Assert.True(server.InStation("Owner"));
            var lab = FindBlock(server, owner.State.Position, Block("bio_lab"));
            owner.State.Position = new Vector3f(lab.X + 0.5f, lab.Y, lab.Z + 1.5f);
            AssertTheLabAnalyses(server, owner);

            // Built on the deck itself, a device joins the Crystal Net there.
            var lever = FreeCellBeside(server, lab);
            Give(owner, "crystal_switch");
            server.PlaceBlock("Owner", lever.X, lever.Y, lever.Z, "crystal_switch");
            Assert.Equal(Block("crystal_switch"), server.World.GetBlock(lever));
            Assert.NotNull(server.CrystalDeviceOutput(lever));
        }
    }

    /// <summary>The lab in reach really is a lab: a first analysis of a fresh sample goes through.</summary>
    private static void AssertTheLabAnalyses(SvGameServer server, PlayerSession p)
    {
        uint seed = server.GiveFloraSampleForTest(p, "flora_bush", 1);
        server.BioLabForTest(p, new BioLabIntent { Action = BioLabIntent.Analyse, Sample = seed });
        Assert.True(server.BioAnalysedForTest(p.State.PlayerId, seed));
    }

    /// <summary>The first cell holding <paramref name="block"/> in the box around a position.</summary>
    private static Vector3i FindBlock(SvGameServer server, Vector3f around, BlockId block)
    {
        int cx = (int)Math.Floor(around.X), cy = (int)Math.Floor(around.Y), cz = (int)Math.Floor(around.Z);
        for (int x = cx - 24; x <= cx + 24; x++)
        {
            for (int y = cy - 8; y <= cy + 8; y++)
            {
                for (int z = cz - 24; z <= cz + 24; z++)
                {
                    var cell = new Vector3i(x, y, z);
                    if (server.World.GetBlock(cell) == block)
                    {
                        return cell;
                    }
                }
            }
        }

        throw new InvalidOperationException("the block is not in the world grid around the player");
    }

    /// <summary>A free cell within reach of a player standing beside <paramref name="cell"/>.</summary>
    private static Vector3i FreeCellBeside(SvGameServer server, Vector3i cell)
    {
        foreach (var (dx, dy, dz) in new[] { (0, 1, 0), (0, 0, 2), (1, 0, 1), (-1, 0, 1), (0, 1, 2), (0, 2, 0) })
        {
            var free = new Vector3i(cell.X + dx, cell.Y + dy, cell.Z + dz);
            if (server.World.GetBlock(free).IsAir)
            {
                return free;
            }
        }

        throw new InvalidOperationException("no free cell beside the lab");
    }
}
