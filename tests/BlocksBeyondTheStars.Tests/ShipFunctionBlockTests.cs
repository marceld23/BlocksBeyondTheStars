// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A block that only works in a world's block grid is decoration in a ship (#2219). The bio lab is found by scanning
/// the chunk grid around the player and a Crystal Net device registers its cell on a world place; built into the own
/// ship — a structure object — either one stands there and does nothing. Players furnish their ships with exactly
/// these blocks, so every path that turns a placed item into a ship cell takes them like any other block, and VEGA
/// tells the builder once — once per player, not once per block — that the block does its job outside. The ground, a
/// base and a station's deck take them as working blocks. A station built from OUTSIDE, on a spacewalk, turns away
/// what the world place handler has to register (a conduit, a device, a beacon …): stamped into the deck it stood
/// there and did nothing, while the same block placed aboard works.
/// </summary>
public sealed class ShipFunctionBlockTests : IDisposable
{
    /// <summary>VEGA's line "aboard a ship this block is only decoration", and the once-flag it leaves on the player.</summary>
    private const string DecorNotice = "vega.hint.ship_decor";
    private const string DecorMilestone = "vega:hint:ship_decor";
    private const string StationRefusal = "@srv.station.block_needs_deck";

    /// <summary>The Crystal Net devices: every block that is a device by itself.</summary>
    private static readonly string[] CrystalDevices =
    {
        "clone_tank", "caller", "crystal_switch", "crystal_button", "step_plate", "proximity_sensor",
        "daylight_sensor", "storage_sensor", "watcher", "logic_block", "timer_block", "alarm_siren", "chime", "horn",
        "melody_block", "announcer", "fabricator", "auto_drill_1", "auto_drill_2", "auto_drill_3", "matter_sender",
        "matter_receiver", "drill_laser", "rail_stop", "device_eye",
    };

    /// <summary>The older blocks that gained a Crystal Net port. Each has a function of its own that needs a world: a
    /// waypoint, a teleporter pad, a guard, a call for a giant, a waterfall, a pen's door, a crop bed.</summary>
    private static readonly string[] PortBlocks =
    {
        "radio_beacon", "beam_block", "sentry_post", "thumper", "water_spout", "energy_gate", "hydro_tray",
    };

    /// <summary>What is only decoration in a ship, and what VEGA says so about: the bio lab and everything the Crystal
    /// Net keeps a device row for — not a conduit, not a lamp.</summary>
    private static readonly string[] WorldOnly = new[] { "bio_lab" }.Concat(CrystalDevices).Concat(PortBlocks).ToArray();

    /// <summary>What a station refuses on a spacewalk: the blocks the world place handler has to register — a conduit,
    /// the devices, and the port blocks with a named entry or a start of their own.</summary>
    private static readonly string[] DeckOnly = new[] { "crystal_conduit", "radio_beacon", "beam_block", "thumper", "water_spout" }
        .Concat(CrystalDevices).ToArray();

    /// <summary>What a station still takes on a spacewalk besides ordinary blocks: the bio lab and the Crystal Net
    /// blocks that are read from the block grid wherever they stand.</summary>
    private static readonly string[] SpacewalkParts = { "light_white", "sentry_post", "energy_gate", "hydro_tray" };

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
        => Edit(server, t, who, new StructureEditIntent { StructureId = structureId, X = cell.X, Y = cell.Y, Z = cell.Z, ItemKey = item });

    /// <summary>Takes the block at a cell out of a structure again; what came back is the refusal, or nothing.</summary>
    private static string? TakeOut(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who, string structureId,
        Vector3i cell)
        => Edit(server, t, who, new StructureEditIntent { StructureId = structureId, X = cell.X, Y = cell.Y, Z = cell.Z, Mine = true });

    private static string? Edit(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who, StructureEditIntent intent)
    {
        int before = t.Sent.Count;
        server.HandleStructureEditForTest(who.State.PlayerId, intent);
        return t.Sent.Skip(before).Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg)
            .OfType<ActionRejected>().LastOrDefault()?.Reason;
    }

    /// <summary>How often VEGA has told this player that a block is only decoration aboard a ship — every time as a
    /// system line (<see cref="ShipAiLine.Kind"/> 3). An advisor hint (kind 1) is muted for a player who switched VEGA's
    /// hints off, and the notice's once-flag would be used up without the player ever reading it.</summary>
    private static int DecorNotices(NpcLifeWorld.RecordingTransport t, PlayerSession who)
    {
        var notices = t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>()
            .Where(line => line.LineKey == DecorNotice).ToList();
        Assert.All(notices, line => Assert.Equal(3, line.Kind));
        return notices.Count;
    }

    /// <summary>An ordinary block at one cell of a ship, for a player who has not heard the notice yet: it is built and
    /// paid for, and VEGA has nothing to say about it. It is taken out again, so the cell is free afterwards.</summary>
    private void AssertAnOrdinaryBlockIsBuiltWithoutANotice(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who,
        string structureId, Vector3i cell, Func<Vector3i, BlockId> blockAt)
    {
        who.State.InstantBuild = false; // survival rules: an accepted block is taken out of the pack
        Give(who, "iron_wall");
        Assert.Null(Build(server, t, who, structureId, cell, "iron_wall"));
        Assert.Equal(Block("iron_wall"), blockAt(cell));
        Assert.Equal(0, who.State.Inventory.CountOf("iron_wall"));

        Assert.Equal(0, DecorNotices(t, who));
        Assert.DoesNotContain(DecorMilestone, who.State.Milestones);

        Assert.Null(TakeOut(server, t, who, structureId, cell));
        Assert.True(blockAt(cell).IsAir);
        EmptyPack(who);
    }

    /// <summary>Builds every world-only block at one cell of a ship, in a survival pack that holds exactly that one
    /// item: each is accepted, stands in the cell and is used up — and comes out again like any block the owner built.
    /// VEGA's notice is there once after the first of them (or was there before, from another ship path), and no later
    /// block of any kind adds a second one.</summary>
    private void AssertAllBuiltAsDecor(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who,
        string structureId, Vector3i cell, Func<Vector3i, BlockId> blockAt)
    {
        who.State.InstantBuild = false; // survival rules: an accepted block is taken out of the pack
        foreach (string item in WorldOnly)
        {
            Give(who, item);
            Assert.Null(Build(server, t, who, structureId, cell, item));
            Assert.Equal(Block(item), blockAt(cell));
            Assert.Equal(0, who.State.Inventory.CountOf(item));

            Assert.Equal(1, DecorNotices(t, who));
            Assert.Contains(DecorMilestone, who.State.Milestones);

            Assert.Null(TakeOut(server, t, who, structureId, cell));
            Assert.True(blockAt(cell).IsAir);
            EmptyPack(who); // the block's drop
        }
    }

    /// <summary>The block a self-built ship's persisted design ("x:y:z:blockId;…") holds at a cell, or air.</summary>
    private static BlockId DesignCell(string builtCells, Vector3i cell)
    {
        string prefix = $"{cell.X}:{cell.Y}:{cell.Z}:";
        string? entry = builtCells.Split(';').FirstOrDefault(e => e.StartsWith(prefix, StringComparison.Ordinal));
        return entry is null ? BlockId.Air : new BlockId(ushort.Parse(entry[prefix.Length..], CultureInfo.InvariantCulture));
    }

    private static void AssertRefusedAndKept(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who,
        string structureId, Vector3i cell, string[] items, string refusal)
    {
        who.State.InstantBuild = false; // survival rules: an accepted block would be taken out of the pack
        foreach (string item in items)
        {
            Give(who, item);
            Assert.Equal(refusal, Build(server, t, who, structureId, cell, item));
            Assert.Equal(1, who.State.Inventory.CountOf(item));
            who.State.Inventory.Remove(item, 1);
        }
    }

    [Fact]
    public void TheLists_CoverEveryCrystalNetBlockOfTheContent()
    {
        // A block added to the Crystal Net later must not slip past these tests: every block the net knows is either
        // a conduit, a lamp or one of the keys the lists above name — so it is classified as "decoration in a ship,
        // with VEGA's notice" or not, and as "built aboard a station only" or not.
        var netBlocks = _content.Blocks.Values
            .Where(b => CrystalNetRules.KindOf(b) is not (CrystalDeviceKind.None or CrystalDeviceKind.Light or CrystalDeviceKind.Conduit))
            .Select(b => b.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        Assert.Equal(CrystalDevices.Concat(PortBlocks).OrderBy(k => k, StringComparer.Ordinal).ToArray(), netBlocks);
        Assert.All(DeckOnly.Concat(SpacewalkParts), key => Assert.NotEqual(CrystalDeviceKind.None, CrystalNetRules.KindOf(_content.GetBlock(key))));
    }

    /// <summary>A starter ship's pilot standing just inside the rear hatch — parked on the planet, or in the walkable
    /// interior out in space — and a free cabin cell beside them.</summary>
    private static (PlayerSession Pilot, Vector3i Cell) InTheCabin(SvGameServer server, bool inSpace, string name = "Pilot")
    {
        var pilot = server.AddLocalPlayer(name);
        EmptyPack(pilot);
        if (inSpace)
        {
            server.EnterSpace(name);
            server.EnterShipInterior(name);
            Assert.True(server.InShipInterior(name));
        }

        var hatch = server.BuildShipStructureForTest(name).DoorCells.Single(c => c.Z == 0); // the rear-wall hatch
        var (origin, _) = server.LandedShipBoundsForTest(name);
        pilot.State.Position = new Vector3f(origin.X + hatch.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);
        return (pilot, new Vector3i(hatch.X, hatch.Y, hatch.Z + 2));
    }

    // ---------------- The ship takes them as decoration, and VEGA says so once ----------------

    /// <summary>The landed-ship edit on the planet and the same edit in the ship interior out in space.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InTheCabin_ALabOrACrystalDevice_IsBuiltAndPaidFor_AndVegaSaysOnceItIsDecoration(bool inSpace)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer(inSpace ? "shipfn_cabin_space" : "shipfn_cabin", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace);
            BlockId InTheShip(Vector3i c) => server.BuildShipStructureForTest("Pilot").Get(c); // the design plus the stored edits

            AssertAnOrdinaryBlockIsBuiltWithoutANotice(server, t, pilot, "ship:Pilot", cell, InTheShip);
            AssertAllBuiltAsDecor(server, t, pilot, "ship:Pilot", cell, InTheShip);

            // The once-flag is saved with the player: a restart does not bring the notice back.
            Assert.Contains(DecorMilestone, repo.LoadPlayer("Pilot")!.Milestones);
        }
    }

    /// <summary>A conduit and a lamp are furnishing — a conduit does nothing by itself anywhere, a lamp shines in a cabin
    /// too — so a ship takes them without a word.</summary>
    [Theory]
    [InlineData("crystal_conduit")]
    [InlineData("light_white")]
    public void InTheCabin_AConduitAndALamp_AreBuilt_WithoutANotice(string item)
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
            Assert.Equal(0, DecorNotices(t, pilot));
            Assert.DoesNotContain(DecorMilestone, pilot.State.Milestones);
        }
    }

    [Fact]
    public void OnASpacewalk_TheOwnShipsHull_TakesThemToo()
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
            BlockId OnTheHull(Vector3i c) => new(server.StructureBlockForTest("Pilot", c.X, c.Y, c.Z));

            AssertAnOrdinaryBlockIsBuiltWithoutANotice(server, t, pilot, "ship:Pilot", cell, OnTheHull);
            AssertAllBuiltAsDecor(server, t, pilot, "ship:Pilot", cell, OnTheHull);
        }
    }

    /// <summary>The notice is once per PLAYER: a second pilot hears it for their own first such block, whatever the
    /// first pilot has built — and neither hears it for the other's.</summary>
    [Fact]
    public void TheNotice_IsToldToEachPlayerOnce()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_two_pilots", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace: false);
            var (mate, mateCell) = InTheCabin(server, inSpace: false, name: "Mate");
            pilot.State.InstantBuild = false;
            mate.State.InstantBuild = false;

            Give(pilot, "bio_lab");
            Assert.Null(Build(server, t, pilot, "ship:Pilot", cell, "bio_lab"));
            Assert.Equal(1, DecorNotices(t, pilot));
            Assert.Equal(0, DecorNotices(t, mate));
            Assert.DoesNotContain(DecorMilestone, mate.State.Milestones);

            Give(mate, "crystal_switch");
            Assert.Null(Build(server, t, mate, "ship:Mate", mateCell, "crystal_switch"));
            Assert.Equal(Block("crystal_switch"), server.BuildShipStructureForTest("Mate").Get(mateCell));
            Assert.Equal(1, DecorNotices(t, mate));
            Assert.Equal(1, DecorNotices(t, pilot));

            // A second block of another kind, for either of them: nothing more.
            Give(pilot, "radio_beacon");
            Assert.Null(Build(server, t, pilot, "ship:Pilot", new Vector3i(cell.X, cell.Y + 1, cell.Z), "radio_beacon"));
            Give(mate, "bio_lab");
            Assert.Null(Build(server, t, mate, "ship:Mate", new Vector3i(mateCell.X, mateCell.Y + 1, mateCell.Z), "bio_lab"));
            Assert.Equal(0, pilot.State.Inventory.CountOf("radio_beacon") + mate.State.Inventory.CountOf("bio_lab"));
            Assert.Equal(1, DecorNotices(t, pilot));
            Assert.Equal(1, DecorNotices(t, mate));
        }
    }

    /// <summary>The keel's construction site, and the same hull once it is commissioned and parked.</summary>
    [Fact]
    public void ASelfBuiltShip_TakesThem_OnTheSiteAndOnceCommissioned_AndTheNoticeIsNotRepeated()
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
            BlockId InTheDesign(Vector3i c) => DesignCell(ship.BuiltCells, c); // the persisted hull, site and ship alike

            // The construction site: each one joins the hull beside the keel and leaves the pack — and VEGA says once
            // that it is decoration there.
            var beside = new Vector3i(1, 0, 0);
            AssertAnOrdinaryBlockIsBuiltWithoutANotice(server, t, pilot, Yard, beside, InTheDesign);
            AssertAllBuiltAsDecor(server, t, pilot, Yard, beside, InTheDesign);
            Assert.Equal(keelOnly, ship.BuiltCells); // every one of them came out again

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

            // The commissioned ship: a free cabin cell on the floor takes each of them as well, as a change of the
            // design — and the builder, who heard the notice on the site, does not hear it again.
            EmptyPack(pilot);
            var cabin = new Vector3i(3, 1, 3);
            Assert.True(InTheDesign(cabin).IsAir);
            AssertAllBuiltAsDecor(server, t, pilot, "ship:Pilot", cabin, InTheDesign);

            // One of them stays aboard: the ship keeps it, and is still the commissioned ship it was.
            Give(pilot, "bio_lab");
            Assert.Null(Build(server, t, pilot, "ship:Pilot", cabin, "bio_lab"));
            Assert.Equal(Block("bio_lab"), InTheDesign(cabin));
            Assert.Equal(0, pilot.State.Inventory.CountOf("bio_lab"));
            Assert.True(ship.Commissioned);
            Assert.Equal(1, DecorNotices(t, pilot));
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

    /// <summary>The owner on a spacewalk at their own commissioned station: a core, a small hull along +X and an
    /// airlock door.</summary>
    private static (PlayerSession Owner, string StationId) OnASpacewalkAtTheOwnStation(SvGameServer server, NpcLifeWorld.RecordingTransport t)
    {
        var owner = server.AddLocalPlayer("Owner");
        EmptyPack(owner);
        server.EnterSpace("Owner");
        owner.State.InEva = true;
        owner.State.InstantBuild = true;
        server.DeployStationCoreForTest("Owner");
        string id = server.OwnedStationIdForTest("Owner")!;
        for (int i = 1; i <= 11; i++)
        {
            Build(server, t, owner, id, new Vector3i(i, 0, 0), "iron_wall");
        }

        Build(server, t, owner, id, new Vector3i(0, 1, 0), "door_slide");
        Assert.True(server.StationIsBoardableForTest(id));
        owner.State.InstantBuild = false; // from here on a block is paid for like any other
        return (owner, id);
    }

    /// <summary>Docks at the station and steps aboard: its build is stamped into the deck's world grid.</summary>
    private static void Board(SvGameServer server, string stationId)
    {
        var contact = server.SpaceEntitiesFor("Owner").First(e => e.Id == stationId);
        server.ShipMove("Owner", contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation("Owner", stationId);
        Assert.True(server.InStation("Owner"));
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
            var (owner, id) = OnASpacewalkAtTheOwnStation(server, t);

            // The lab goes onto the station on the spacewalk — accepted, and paid for like any block.
            Give(owner, "bio_lab");
            Assert.Null(Build(server, t, owner, id, new Vector3i(1, 1, 0), "bio_lab"));
            Assert.Equal(Block("bio_lab").Value, server.StructureCellForTest(id, 1, 1, 0));
            Assert.Equal(0, owner.State.Inventory.CountOf("bio_lab"));

            // Aboard, it stands in the deck's world grid and works.
            Board(server, id);
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

    /// <summary>The spacewalk build is stamped into the deck without the world place handler, so what that handler has
    /// to register would stand there dead — a conduit that does not conduct, a switch without a menu, a beacon that is
    /// no waypoint. The station turns those away before the item is used and says to build them aboard.</summary>
    [Fact]
    public void OnASpacewalk_TheOwnStation_RefusesWhatOnlyTheDeckCanRegister_AndKeepsTheItem()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_station_eva", t, out var repo);
        using (repo)
        {
            var (owner, id) = OnASpacewalkAtTheOwnStation(server, t);
            var cell = new Vector3i(1, 1, 0);

            AssertRefusedAndKept(server, t, owner, id, cell, DeckOnly, StationRefusal);
            Assert.Equal((ushort)0, server.StructureCellForTest(id, cell.X, cell.Y, cell.Z));

            // The same cell takes an ordinary block: the refusal was about the block, not about the place.
            Give(owner, "iron_wall");
            Assert.Null(Build(server, t, owner, id, cell, "iron_wall"));
            Assert.Equal(Block("iron_wall").Value, server.StructureCellForTest(id, cell.X, cell.Y, cell.Z));
            Assert.Equal(0, owner.State.Inventory.CountOf("iron_wall"));
        }
    }

    /// <summary>A lamp, a sentry post, an energy gate and a hydro tray are read from the block grid wherever they stand:
    /// the station takes them on a spacewalk as before, and aboard a conduit laid on each one finds it as a port — and
    /// joins a conduit laid beside it, which a conduit stamped from the spacewalk never did.</summary>
    [Fact]
    public void OnASpacewalk_TheOwnStation_StillTakesTheBlocksTheGridIsReadFor_AndTheDeckWiresThem()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_station_parts", t, out var repo);
        using (repo)
        {
            var (owner, id) = OnASpacewalkAtTheOwnStation(server, t);
            for (int i = 0; i < SpacewalkParts.Length; i++)
            {
                Give(owner, SpacewalkParts[i]);
                Assert.Null(Build(server, t, owner, id, new Vector3i(2 + (2 * i), 1, 0), SpacewalkParts[i]));
                Assert.Equal(Block(SpacewalkParts[i]).Value, server.StructureCellForTest(id, 2 + (2 * i), 1, 0));
                Assert.Equal(0, owner.State.Inventory.CountOf(SpacewalkParts[i]));
            }

            Board(server, id);
            Assert.Equal(0, server.CrystalCellCount); // stamped blocks are no net cells yet

            foreach (string part in SpacewalkParts)
            {
                var block = FindBlock(server, owner.State.Position, Block(part));
                var above = new Vector3i(block.X, block.Y + 1, block.Z);
                Assert.True(server.World.GetBlock(above).IsAir);
                owner.State.Position = new Vector3f(block.X + 0.5f, block.Y + 1f, block.Z + 1.5f);
                int before = server.CrystalCellCount;
                Give(owner, "crystal_conduit");
                server.PlaceBlock("Owner", above.X, above.Y, above.Z, "crystal_conduit");

                Assert.Equal(Block("crystal_conduit"), server.World.GetBlock(above));
                Assert.Equal(before + 2, server.CrystalCellCount); // the conduit, and the block beneath it as its port
                Assert.NotNull(server.CrystalLevelAt(block));
            }
        }
    }

    /// <summary>A station is no ship: what it takes on a spacewalk works on its deck, so VEGA's "only decoration aboard
    /// a ship" would be wrong there and is not sent — not for the lab, not for a block the Crystal Net has a row for.</summary>
    [Fact]
    public void OnASpacewalk_TheOwnStation_IsNoShip_AndGetsNoDecorationNotice()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipfn_station_notice", t, out var repo);
        using (repo)
        {
            var (owner, id) = OnASpacewalkAtTheOwnStation(server, t);
            string[] built = new[] { "bio_lab" }.Concat(SpacewalkParts).ToArray();
            for (int i = 0; i < built.Length; i++)
            {
                Give(owner, built[i]);
                Assert.Null(Build(server, t, owner, id, new Vector3i(1 + (2 * i), 1, 0), built[i]));
                Assert.Equal(Block(built[i]).Value, server.StructureCellForTest(id, 1 + (2 * i), 1, 0));
            }

            Assert.Equal(0, DecorNotices(t, owner));
            Assert.DoesNotContain(DecorMilestone, owner.State.Milestones);
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
