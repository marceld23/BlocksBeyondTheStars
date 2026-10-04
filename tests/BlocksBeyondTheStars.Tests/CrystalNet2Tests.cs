// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Crystal Net 2 (#2251) through the real server tick: pairs that survive a reload (#2252), doors only their owner's
/// alliance can lock (#2253), "only you" filters that count the alliance (#2254), the owner + alliance rule for operating
/// (#2256), ports that switch fields, fires and heal tanks (#2261), the display, the dice block, the signal pair and the
/// remote control (#2263), the phase block and the trapdoor (#2264), the bridge motor and the piston (#2265), six
/// directions, turning and fair caps (#2267).
/// </summary>
public sealed class CrystalNet2Tests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrystalNet2Tests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_crystal2_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string world = "crystal2", int catchUpMinutes = CrystalNetRules.CatchUpDefaultMinutes)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.MachineCatchUpMinutes = catchUpMinutes;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Player(SvGameServer server, string name, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer(name);
        p.State.Position = at; // up in the air → the cells are empty + within reach
        foreach (var key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024));
        }

        p.State.SuitEnergy = 100f;
        return p;
    }

    private static void Ticks(SvGameServer server, double seconds, double step = 0.1)
    {
        for (double t = 0; t < seconds; t += step)
        {
            server.TickForTest(step);
        }
    }

    private string KeyAt(SvGameServer server, int x, int y, int z) => _content.BlockById(server.World.GetBlock(new Vector3i(x, y, z)))?.Key ?? "air";

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAllianceForTest(a, b);
        server.RespondAllianceForTest(b, a, accept: true);
        Assert.True(server.AreAllied(a, b));
    }

    // ---------------------------------------------------------------------------------------------------
    // #2252 pairs, #2253 doors, #2254 filters, #2256 operating
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AMatterLink_StillWorksAfterAReload_WhenTheRowsComeBackInAnotherOrder()
    {
        var server = NewServer(out var repo1);
        using (repo1)
        {
            var p = Player(server, "Builder", new Vector3f(3, 200, 0), "crate", "matter_sender", "matter_receiver");
            server.PlaceBlock("Builder", 6, 200, 0, "crate");
            server.PlaceBlock("Builder", 5, 200, 0, "matter_receiver", "Lager"); // placed FIRST: the smaller device id …
            server.PlaceBlock("Builder", 0, 200, 0, "crate");
            server.PlaceBlock("Builder", 1, 200, 0, "matter_sender");            // … but the smaller coordinate is the sender's
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, config: "pair=5,200,0");
        }

        var reloaded = NewServer(out var repo2);
        using (repo2)
        {
            var p = Player(reloaded, "Builder", new Vector3f(3, 200, 0));
            var from = reloaded.Containers.Single(c => c.Position == new Vector3i(0, 200, 0));
            var to = reloaded.Containers.Single(c => c.Position == new Vector3i(6, 200, 0));
            from.Items.Add(new ItemStack("iron_ore", 20));
            reloaded.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Assert.Equal(16, to.Items.Single(s => s.Item == "iron_ore").Count); // the pair still names the receiver
            Assert.False(reloaded.CrystalDeviceOutput(new Vector3i(1, 200, 0)));
        }
    }

    private static int DoorAt(SvGameServer server, float x) => server.DoorSnapshots.Single(d => Math.Abs(d.Pos.X - x) < 0.01f).Id;

    [Fact]
    public void AStrangersConduit_CannotLockSomeonesDoor_ButTheOwnersAllyCan()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner", new Vector3f(0, 200, 0), "stone", "door_wood");
            var stranger = Player(server, "Stranger", new Vector3f(0, 200, 0), "crystal_conduit", "crystal_switch");
            var ally = Player(server, "Friend", new Vector3f(0, 200, 0), "crystal_conduit", "crystal_switch");
            for (int x = 0; x <= 6; x++)
            {
                server.PlaceBlock("Owner", x, 199, 0, "stone");
            }

            server.PlaceBlock("Owner", 1, 200, 0, "door_wood");
            int door = DoorAt(server, 1.5f);
            bool Open() => server.DoorSnapshots.Single(d => d.Id == door).Open;

            server.PlaceBlock("Stranger", 2, 200, 0, "crystal_conduit"); // an OFF wire beside someone else's door
            Ticks(server, 0.5);
            owner.State.Position = new Vector3f(1.5f, 200f, 1.5f);
            server.InteractDoorForTest(owner, door);
            Assert.True(Open()); // not locked: the stranger's wire is ignored
            server.InteractDoorForTest(owner, door);
            Assert.False(Open());

            server.MineBlock("Stranger", 2, 200, 0);
            Ally(server, "Owner", "Friend");
            server.PlaceBlock("Friend", 2, 200, 0, "crystal_conduit"); // the owner's ally may wire it
            Ticks(server, 0.5);
            server.InteractDoorForTest(owner, door);
            Assert.False(Open()); // locked by an OFF network
            _ = stranger;
            _ = ally;
        }
    }

    [Fact]
    public void AnOnlyYouPlate_CountsTheOwnersAlliance_ButNotAStranger()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var owner = Player(server, "Owner", new Vector3f(0, 200, 0), "step_plate", "crystal_conduit");
            server.PlaceBlock("Owner", 1, 200, 0, "step_plate");
            server.PlaceBlock("Owner", 2, 200, 0, "crystal_conduit");
            var plate = new Vector3i(1, 200, 0);
            server.SetCrystalDeviceForTest(owner, plate, action: 2, mode: (int)PresenceFilter.Owner);
            owner.State.Position = new Vector3f(8f, 201f, 0.5f);

            var stranger = Player(server, "Stranger", new Vector3f(1.5f, 201f, 0.5f));
            Ticks(server, 0.7);
            Assert.False(server.CrystalDeviceOutput(plate));

            stranger.State.Position = new Vector3f(8f, 201f, 4f);
            var friend = Player(server, "Friend", new Vector3f(1.5f, 201f, 0.5f));
            Ally(server, "Owner", "Friend");
            Ticks(server, 0.7);
            Assert.True(server.CrystalDeviceOutput(plate)); // #2254: "only you" = you and your alliance
            _ = friend;
        }
    }

    [Fact]
    public void AStranger_CannotFlipSomeonesSwitch_ButTheOwnersAllyCan()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Player(server, "Owner", new Vector3f(0, 200, 0), "crystal_switch", "crystal_conduit");
            server.PlaceBlock("Owner", 1, 200, 0, "crystal_switch");
            var sw = new Vector3i(1, 200, 0);
            var stranger = Player(server, "Stranger", new Vector3f(0, 200, 0));
            server.SetCrystalDeviceForTest(stranger, sw, action: 0);
            Assert.False(server.CrystalDeviceOutput(sw)); // #2256: refused

            var friend = Player(server, "Friend", new Vector3f(0, 200, 0));
            Ally(server, "Owner", "Friend");
            server.SetCrystalDeviceForTest(friend, sw, action: 0);
            Assert.True(server.CrystalDeviceOutput(sw));
        }
    }

    [Fact]
    public void TheFirstConduitLine_ComesOncePerPlayer_NotOncePerWorld()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var a = Player(server, "A", new Vector3f(0, 200, 0), "crystal_conduit");
            var b = Player(server, "B", new Vector3f(0, 200, 0), "crystal_conduit");
            server.PlaceBlock("A", 1, 200, 0, "crystal_conduit");
            server.PlaceBlock("B", 4, 200, 0, "crystal_conduit");
            Assert.Contains("vega:hint:crystal_first", a.State.Milestones);
            Assert.Contains("vega:hint:crystal_first", b.State.Milestones); // #2254: the second player hears it too
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2260 world circuits
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AWorldCircuitSwitch_IsOperatedByAnyone_ButOnlyAnAdminReconfiguresIt_AndItCostsNoPlayerCap()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            // The stamp writes the blocks; the registration makes them a world circuit (as a settlement's markers do).
            var builder = Player(server, "Builder", new Vector3f(0, 203, 0), "crystal_switch", "crystal_conduit", "light_white", "timer_block");
            server.World.SetBlock(new Vector3i(1, 200, 0), _content.GetBlock("crystal_switch")!.NumericId);
            server.World.SetBlock(new Vector3i(2, 200, 0), _content.GetBlock("crystal_conduit")!.NumericId);
            server.World.SetBlock(new Vector3i(3, 200, 0), _content.GetBlock("light_white")!.NumericId);
            server.RegisterWorldCircuitForTest(new Vector3i(1, 200, 0));
            server.RegisterWorldCircuitForTest(new Vector3i(2, 200, 0));
            Assert.Equal(3, server.CrystalCellCount); // the lamp beside the wire joined as a world port

            var visitor = Player(server, "Visitor", new Vector3f(0, 203, 0));
            server.SetCrystalDeviceForTest(visitor, new Vector3i(1, 200, 0), action: 0);
            Ticks(server, 0.8);
            Assert.True(server.CrystalDeviceOutput(new Vector3i(1, 200, 0))); // a puzzle must be solvable by anyone
            Assert.Equal("light_white", KeyAt(server, 3, 200, 0));

            server.World.SetBlock(new Vector3i(1, 200, 4), _content.GetBlock("timer_block")!.NumericId);
            server.RegisterWorldCircuitForTest(new Vector3i(1, 200, 4), mode: (int)TimerMode.Clock, config: "period=2");
            server.SetCrystalDeviceForTest(visitor, new Vector3i(1, 200, 4), action: 2, mode: (int)TimerMode.Toggle);
            Assert.Equal((int)TimerMode.Clock, server.CrystalDeviceModeForTest(new Vector3i(1, 200, 4))); // not re-wired by a visitor

            // The player's own share of networks is untouched by the world's circuits.
            for (int i = 0; i < CrystalNetRules.MaxNetsPerPlayer; i++)
            {
                builder.State.Position = new Vector3f(i * 2, 206, 8);
                server.PlaceBlock("Builder", i * 2, 203, 8, "crystal_conduit");
            }

            Assert.Equal(CrystalNetRules.MaxNetsPerPlayer + 1, server.CrystalNetSnapshots.Count); // 32 own + the world's one
        }
    }

    [Fact]
    public void ATemplateWithACircuit_CarriesItsDevicesAsMarkers_AndAQuarterTurnKeepsTheirSettings()
    {
        var t = new StructureTemplate { Key = "test_circuit", Width = 4, Height = 2, Length = 2 };
        t.Cells.Add(new TemplateCell { X = 0, Y = 0, Z = 0, Kind = "block", Id = "daylight_sensor", Mode = 1 });
        t.Cells.Add(new TemplateCell { X = 1, Y = 0, Z = 0, Kind = "block", Id = "crystal_conduit" });
        t.Cells.Add(new TemplateCell { X = 2, Y = 0, Z = 0, Kind = "block", Id = "light_white" }); // a plain port: no marker
        t.Cells.Add(new TemplateCell { X = 3, Y = 0, Z = 0, Kind = "block", Id = "timer_block", Config = "period=3", Label = "Uhr" });
        var s = BlocksBeyondTheStars.WorldGeneration.SettlementGenerator.FromTemplate(t, _content);
        var devices = s.Markers.Where(m => m.Type == TemplateDevices.Marker).ToList();
        Assert.Equal(3, devices.Count);
        var sensor = devices.Single(m => m.LocalPos == new Vector3i(0, 0, 0));
        Assert.Equal(1, TemplateDevices.Decode(sensor.Data).Mode);
        var timer = TemplateDevices.Decode(devices.Single(m => m.LocalPos == new Vector3i(3, 0, 0)).Data);
        Assert.Equal(("period=3", "Uhr"), (timer.Config, timer.Label));

        var turned = BlocksBeyondTheStars.WorldGeneration.TemplateTransform.RotateY(t, 1);
        var turnedTimer = turned.Cells.Single(c => c.Id == "timer_block");
        Assert.Equal(("period=3", "Uhr"), (turnedTimer.Config, turnedTimer.Label));
    }

    // ---------------------------------------------------------------------------------------------------
    // #2261 ports
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AWiredForceFieldWall_SwitchesAsOne_AndGoesBackOnWhenUnwired()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(3, 203, 0), "force_field", "crystal_conduit", "crystal_switch");
            for (int x = 3; x <= 5; x++)
            {
                server.PlaceBlock("Builder", x, 200, 0, "force_field"); // an unwired field wall: plain blocks
            }

            Assert.Equal(0, server.CrystalCellCount);
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit"); // touches only the first field cell …
            Assert.Equal(5, server.CrystalCellCount);                   // … and the whole wall joins

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0); // ON = the field is off
            Ticks(server, 0.8);
            Assert.All(new[] { 3, 4, 5 }, x => Assert.Equal("force_field_off", KeyAt(server, x, 200, 0)));

            server.MineBlock("Builder", 2, 200, 0); // unwired: the whole wall dissolves and switches back on
            Assert.Equal(1, server.CrystalCellCount);
            Assert.All(new[] { 3, 4, 5 }, x => Assert.Equal("force_field", KeyAt(server, x, 200, 0)));
        }
    }

    [Fact]
    public void AWiredForge_GoesOutWhileItsNetworkIsOff_AndAWiredHealTankStopsHealing()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(3, 201, 2), "forge", "heal_tank", "crystal_conduit", "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "forge");
            server.PlaceBlock("Builder", 2, 200, 1, "heal_tank");
            Ticks(server, 0.8);
            Assert.Equal("forge_off", KeyAt(server, 3, 200, 0));        // OFF network: the fire is out
            Assert.False(server.NearHealTankForTest("Builder"));        // and the tank does not heal

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0);
            Ticks(server, 0.8);
            Assert.Equal("forge", KeyAt(server, 3, 200, 0));
            Assert.True(server.NearHealTankForTest("Builder"));
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2262 the fabricator runs the recipes of the station beside it; machines use every crate beside them
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AFabricatorBesideAForge_Smelts_AndWithoutTheForge_ItIsStuck()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(2, 203, 0), "fabricator", "crate", "forge");
            server.PlaceBlock("Builder", 1, 200, 0, "fabricator");
            server.PlaceBlock("Builder", 0, 200, 0, "crate");
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, config: "recipe=titanium_plate");
            var crate = server.Containers.Single(c => c.Position == new Vector3i(0, 200, 0));
            crate.Items.Add(new ItemStack("titanium_ore", 4));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1); // no forge beside it: stuck
            Assert.True(server.CrystalDeviceOutput(new Vector3i(1, 200, 0)));
            Assert.Equal(4, crate.Items.Single(s => s.Item == "titanium_ore").Count);

            server.PlaceBlock("Builder", 2, 200, 0, "forge");
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Assert.False(server.CrystalDeviceOutput(new Vector3i(1, 200, 0)));
            Assert.Equal(1, crate.Items.Single(s => s.Item == "titanium_plate").Count);
            Assert.Equal(2, crate.Items.Single(s => s.Item == "titanium_ore").Count);
        }
    }

    [Fact]
    public void AMatterSender_TakesFromAnyCrateBesideIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(3, 203, 0), "crate", "matter_sender", "matter_receiver");
            server.PlaceBlock("Builder", 1, 200, 0, "matter_sender");
            server.PlaceBlock("Builder", 0, 200, 0, "crate");   // empty
            server.PlaceBlock("Builder", 1, 200, 1, "crate");   // the second crate holds the goods
            server.PlaceBlock("Builder", 6, 200, 0, "matter_receiver", "Lager");
            server.PlaceBlock("Builder", 7, 200, 0, "crate");
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, config: "pair=6,200,0");
            server.Containers.Single(c => c.Position == new Vector3i(1, 200, 1)).Items.Add(new ItemStack("iron_ore", 10));
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Assert.Equal(10, server.Containers.Single(c => c.Position == new Vector3i(7, 200, 0)).Items.Single(s => s.Item == "iron_ore").Count);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2264 phase block, trapdoor
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void APhaseBlock_OpensOnTheSignal_KeepsItsDye_AndWaitsForAPlayerBeforeClosing()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "phase_block", "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "phase_block");
            server.PlaceBlock("Builder", 2, 201, 0, "phase_block"); // a two-high secret door, one network with the switch
            var cell = new Vector3i(2, 200, 0);
            var id = server.World.GetBlock(cell);
            server.World.SetBlock(cell, id, 0x3366AA, 0, 0); // dyed to match a wall
            Ticks(server, 0.8);
            Assert.Equal("phase_block", KeyAt(server, 2, 200, 0));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0); // ON = open
            Ticks(server, 0.8);
            Assert.Equal("phase_block_open", KeyAt(server, 2, 200, 0));
            Assert.Equal("phase_block_open", KeyAt(server, 2, 201, 0));
            Assert.Equal(0x3366AA, server.World.GetModifier(cell).Tint); // the dye rode along

            p.State.Position = new Vector3f(2.5f, 200f, 0.5f); // standing in the doorway
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0); // OFF
            Ticks(server, 1.2);
            Assert.Equal("phase_block_open", KeyAt(server, 2, 200, 0)); // kid rule: it never closes onto someone

            p.State.Position = new Vector3f(6f, 203f, 0.5f);
            Ticks(server, 1.2);
            Assert.Equal("phase_block", KeyAt(server, 2, 200, 0));
        }
    }

    [Fact]
    public void ATrapdoor_IsAPlateAtTheTop_FoldsOpenOnTheSignal_AndAFallThroughItDoesNotHurt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "trapdoor", "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "trapdoor");
            var cell = new Vector3i(2, 200, 0);
            int closed = server.World.GetShape(cell);
            Assert.Equal((int)BlockShape.Panel, ShapeCode.ShapeOf(closed));
            Assert.Equal(1, ShapeCode.UpFaceOf(closed)); // flipped to the top of its cell: flush with the floor

            p.State.Position = new Vector3f(2.5f, 201f, 0.5f); // standing on the hatch
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0);
            Ticks(server, 0.8);
            Assert.Equal("trapdoor_open", KeyAt(server, 2, 200, 0));
            Assert.InRange(ShapeCode.UpFaceOf(server.World.GetShape(cell)), 2, 5); // folded against a side

            float health = p.State.Health;
            server.FallDamageForTest("Builder", 40f);
            Assert.Equal(health, p.State.Health); // a moving block never hurts
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2265 bridge motor, piston
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void ABridgeMotor_ExtendsIntoAir_StopsAtAnObstacle_AndPullsTheDeckBackIn()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "bridge_motor", "crystal_switch", "stone");
            server.PlaceBlock("Builder", 0, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "bridge_motor", deviceDir: 1); // points +X
            server.PlaceBlock("Builder", 5, 200, 0, "stone");                    // in the way after three deck blocks
            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0);
            Ticks(server, 2.5);
            Assert.All(new[] { 2, 3, 4 }, x => Assert.Equal("bridge_deck", KeyAt(server, x, 200, 0)));
            Assert.Equal("stone", KeyAt(server, 5, 200, 0));

            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0); // OFF
            Ticks(server, 2.0);
            Assert.All(new[] { 2, 3, 4 }, x => Assert.Equal("air", KeyAt(server, x, 200, 0)));
        }
    }

    [Fact]
    public void APiston_PushesALineOfDyedBlocks_AndASticky_PullsTheFirstOneBack()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "piston", "crystal_switch", "stone");
            server.PlaceBlock("Builder", 0, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "piston", deviceDir: 1);
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, mode: (int)PistonMode.Sticky);
            for (int x = 2; x <= 4; x++)
            {
                server.PlaceBlock("Builder", x, 200, 0, "stone");
            }

            var id = server.World.GetBlock(new Vector3i(2, 200, 0));
            server.World.SetBlock(new Vector3i(2, 200, 0), id, 0xAA3300, 0, 0);
            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0);
            Ticks(server, 0.8);
            Assert.Equal("piston_head", KeyAt(server, 2, 200, 0));
            Assert.All(new[] { 3, 4, 5 }, x => Assert.Equal("stone", KeyAt(server, x, 200, 0)));
            Assert.Equal(0xAA3300, server.World.GetModifier(new Vector3i(3, 200, 0)).Tint); // the dye moved with its block

            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0); // OFF: sticky pulls one back
            Ticks(server, 0.8);
            Assert.Equal("stone", KeyAt(server, 2, 200, 0));
            Assert.Equal("air", KeyAt(server, 3, 200, 0));
            Assert.Equal("stone", KeyAt(server, 4, 200, 0));
        }
    }

    [Fact]
    public void APiston_NeverMovesACrate()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "piston", "crystal_switch", "crate");
            server.PlaceBlock("Builder", 0, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "piston", deviceDir: 1);
            server.PlaceBlock("Builder", 2, 200, 0, "crate");
            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0);
            Ticks(server, 0.8);
            Assert.Equal("crate", KeyAt(server, 2, 200, 0));
            Assert.True(server.CrystalDeviceOutput(new Vector3i(1, 200, 0))); // blocked: the amber light says so
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2269 catch-up
    // ---------------------------------------------------------------------------------------------------

    /// <summary>A Mk1 drill in "everything" mode standing on the natural ground at (0, ?, 0), a crate beside it and a
    /// switch beside that; returns the crate's cell and the switch's.</summary>
    private (Vector3i Crate, Vector3i Switch, BlocksBeyondTheStars.GameServer.PlayerSession Builder) DrillOnTheGround(SvGameServer server)
    {
        int y = 200;
        while (y > 1 && server.World.GetBlock(new Vector3i(0, y - 1, 0)).IsAir)
        {
            y--;
        }

        var p = Player(server, "Builder", new Vector3f(0.5f, y + 3, 0.5f), "auto_drill_1", "crate", "crystal_switch");
        server.PlaceBlock("Builder", 0, y, 0, "auto_drill_1");
        server.PlaceBlock("Builder", 1, y, 0, "crate");
        server.PlaceBlock("Builder", 0, y, 1, "crystal_switch");
        server.SetCrystalDeviceForTest(p, new Vector3i(0, y, 0), action: 2, mode: (int)AutoDrillMode.Everything);
        return (new Vector3i(1, y, 0), new Vector3i(0, y, 1), p);
    }

    private static int CrateItems(SvGameServer server, Vector3i crate)
        => server.Containers.Single(c => c.Position == crate).Items.Sum(s => s.Count);

    [Theory]
    [InlineData(true, 60, true)]   // running, rule on: the drill catches up
    [InlineData(false, 60, false)] // switched off when the world stopped: nothing
    [InlineData(true, 0, false)]   // the world rule is off: nothing
    public void ARunningDrill_CatchesUpAfterAGap_ButNotWhenItWasOff_OrTheRuleIsOff(bool running, int minutes, bool expectCredit)
    {
        var server = NewServer(out var repo, catchUpMinutes: minutes);
        using (repo)
        {
            var (crate, sw, p) = DrillOnTheGround(server);
            if (running)
            {
                server.SetCrystalDeviceForTest(p, sw, action: 0);
            }

            Ticks(server, 1.0);
            int before = CrateItems(server, crate);
            server.ShiftCrystalClockForTest(600); // ten minutes nobody was on this world
            Ticks(server, 2.0);
            int gained = CrateItems(server, crate) - before;
            if (expectCredit)
            {
                Assert.True(gained >= 100, $"caught up {gained} blocks"); // 600 s / 2 s per block, capped at 256 — far more than live
            }
            else
            {
                Assert.True(gained <= 2, $"no catch-up expected, got {gained}"); // at most what the live drill did meanwhile
            }

            Assert.False(server.CrystalCatchUpPendingForTest);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2266 lift
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void ALift_RidesToTheStopThatCalledIt_ShowsItIsThere_AndTheMotorSendsItBackDown()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(6, 203, 2), "lift_motor", "lift_stop", "stone");
            server.PlaceBlock("Builder", 2, 200, 2, "lift_motor");
            server.PlaceBlock("Builder", 4, 205, 2, "lift_stop"); // a landing two cells from the shaft's middle, five up
            var motor = new Vector3i(2, 200, 2);
            Assert.Equal(201f, server.LiftPlatformYForTest(motor)); // resting on the motor

            server.SetCrystalDeviceForTest(p, new Vector3i(4, 205, 2), action: 1); // call it up
            Ticks(server, 2.0);
            Assert.Equal(204f, server.LiftPlatformYForTest(motor)); // its top is level with the stop's cell
            Assert.True(server.CrystalDeviceOutput(new Vector3i(4, 205, 2))); // "the platform is here"

            server.SetCrystalDeviceForTest(p, motor, action: 1); // the motor sends it on: from the top back to the bottom
            Ticks(server, 2.0);
            Assert.Equal(201f, server.LiftPlatformYForTest(motor));
            Assert.False(server.CrystalDeviceOutput(new Vector3i(4, 205, 2)));

            server.PlaceBlock("Builder", 3, 203, 2, "stone"); // a block in the shaft: the lift refuses to start
            server.SetCrystalDeviceForTest(p, new Vector3i(4, 205, 2), action: 1);
            Ticks(server, 2.0);
            Assert.Equal(201f, server.LiftPlatformYForTest(motor));
            Assert.True(server.CrystalDeviceOutput(motor)); // blocked: the amber light says so
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2263 display, dice, signals, remote
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void ACounterDisplay_CountsRisingEdges_AndKeepsTheCountAcrossAReload()
    {
        var server = NewServer(out var repo1);
        using (repo1)
        {
            var p = Player(server, "Builder", new Vector3f(0, 200, 0), "crystal_button", "signal_display");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_button");
            server.PlaceBlock("Builder", 2, 200, 0, "signal_display");
            server.SetCrystalDeviceForTest(p, new Vector3i(2, 200, 0), action: 2, mode: (int)DisplayMode.Counter);
            for (int i = 0; i < 3; i++)
            {
                server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
                Ticks(server, 0.9);
            }

            Assert.Contains("n=3", server.CrystalDeviceConfig(new Vector3i(2, 200, 0)));
        }

        var reloaded = NewServer(out var repo2);
        using (repo2)
        {
            Player(reloaded, "Builder", new Vector3f(0, 200, 0));
            Assert.Contains("n=3", reloaded.CrystalDeviceConfig(new Vector3i(2, 200, 0)));
        }
    }

    [Fact]
    public void ADiceBlock_AnswersSomeRisingEdges_ButNotAll()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 200, 0), "timer_block", "dice_block", "crystal_conduit", "chime");
            server.PlaceBlock("Builder", 0, 200, 0, "timer_block", deviceDir: 1); // a free clock driving +X
            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 2, mode: (int)TimerMode.Clock, config: "period=0.5");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 2, 200, 0, "dice_block", deviceDir: 1); // 1 in 2, sends +X
            server.PlaceBlock("Builder", 3, 200, 0, "crystal_conduit");
            int ons = 0;
            bool last = false;
            for (int i = 0; i < 200; i++)
            {
                server.TickForTest(0.1);
                bool now = server.CrystalLevelAt(new Vector3i(3, 200, 0)) == true;
                if (now && !last)
                {
                    ons++;
                }

                last = now;
            }

            Assert.InRange(ons, 2, 38); // ~40 clock edges in 20 s: some answered, never every one
        }
    }

    [Fact]
    public void ASignalReceiver_RepeatsItsSendersLevel_FarAway_AndARemoteFlipsIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "crystal_switch", "signal_sender", "signal_receiver", "crystal_conduit", "remote_control");
            server.PlaceBlock("Builder", 0, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "signal_sender", "Tor");
            p.State.Position = new Vector3f(30, 203, 0);
            server.PlaceBlock("Builder", 30, 200, 0, "signal_receiver", "Lampe");
            server.PlaceBlock("Builder", 31, 200, 0, "crystal_conduit");
            server.SetCrystalDeviceForTest(p, new Vector3i(30, 200, 0), action: 2, config: "pair=1,200,0");

            p.State.Position = new Vector3f(0, 203, 0);
            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0);
            Ticks(server, 0.5);
            Assert.True(server.CrystalLevelAt(new Vector3i(31, 200, 0))); // no wire across 30 blocks, still ON

            server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0);
            Ticks(server, 0.5);
            Assert.False(server.CrystalLevelAt(new Vector3i(31, 200, 0)));

            // The remote: aimed at the receiver it pairs, aimed anywhere else it flips it.
            p.State.Position = new Vector3f(28, 201, 0.5f);
            server.UseGadgetForTest("Builder", "remote_control", new Vector3f(30.5f, 200.5f, 0.5f));
            Assert.Contains("|30,200,0", p.State.RemoteReceiver);
            Ticks(server, 0.4); // the remote's short cooldown
            p.State.Position = new Vector3f(0, 203, 0);
            server.UseGadgetForTest("Builder", "remote_control", new Vector3f(0, 150, 0));
            Ticks(server, 0.5);
            Assert.True(server.CrystalLevelAt(new Vector3i(31, 200, 0)));
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #2267 directions, turning, caps
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void ALogicBlockPointingDown_DrivesTheNetworkBelowIt_AndTurnCyclesItsDirection()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Player(server, "Builder", new Vector3f(0, 203, 0), "logic_block", "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "logic_block", deviceDir: CrystalNetRules.YawDown); // NOT, no input → ON
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, mode: (int)LogicMode.Not);
            server.PlaceBlock("Builder", 1, 199, 0, "crystal_conduit");
            Ticks(server, 0.4);
            Assert.Equal(CrystalNetRules.YawDown, server.CrystalDeviceYaw(new Vector3i(1, 200, 0)));
            Assert.True(server.CrystalLevelAt(new Vector3i(1, 199, 0)));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 3); // Turn: down → +Z
            Assert.Equal(0, server.CrystalDeviceYaw(new Vector3i(1, 200, 0)));
            Ticks(server, 0.4);
            Assert.False(server.CrystalLevelAt(new Vector3i(1, 199, 0)));
        }
    }

    [Fact]
    public void OnePlayer_MayUseAtMostHalfOfTheWorldsNetworks()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var hog = Player(server, "Hog", new Vector3f(0, 203, 0), "crystal_conduit");
            for (int i = 0; i < CrystalNetRules.MaxNetsPerPlayer + 1; i++)
            {
                hog.State.Position = new Vector3f(i * 2, 203, 0);
                server.PlaceBlock("Hog", i * 2, 200, 0, "crystal_conduit"); // islands: a network each
            }

            Assert.Equal(CrystalNetRules.MaxNetsPerPlayer, server.CrystalNetSnapshots.Count); // the last one is inert

            Player(server, "Other", new Vector3f(0, 203, 6), "crystal_conduit");
            server.PlaceBlock("Other", 0, 200, 6, "crystal_conduit");
            Assert.Equal(CrystalNetRules.MaxNetsPerPlayer + 1, server.CrystalNetSnapshots.Count); // someone else still builds
        }
    }
}
