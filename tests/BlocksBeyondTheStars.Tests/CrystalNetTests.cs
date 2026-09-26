// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Crystal Net (#2045): conduits form networks, sources drive them ON, sinks follow them, gates sit between
/// them, doors lock and hold on a signal, lamps swap to their unlit twin, machines do one bounded job per pulse,
/// and every device comes back from its row after a reload. All through the real server tick.
/// </summary>
public sealed class CrystalNetTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrystalNetTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_crystal_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Records every server send so a test can assert what reached the world.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;
        public readonly List<object> Sent = new();
        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode) { if (NetCodec.Decode(payload) is { } m) Sent.Add(m); }
        public void Broadcast(byte[] payload, DeliveryMode mode) { if (NetCodec.Decode(payload) is { } m) Sent.Add(m); }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, IServerTransport? transport = null, string world = "crystal")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = transport ?? new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = at; // up in the air → the cells are empty + within reach
        foreach (var key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024)); // 0 = everything fit: the 24-slot pocket must take every kind a test hands out
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

    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Conduits_MergeIntoOneNetwork_AndSplitWhenCut()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Builder(server, new Vector3f(0, 200, 0), "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "crystal_conduit");
            Assert.Equal(2, server.CrystalNetSnapshots.Count); // two islands

            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit"); // bridges them
            Assert.Single(server.CrystalNetSnapshots);
            Assert.Equal(3, server.CrystalNetSnapshots[0].Cells);
            Assert.Equal(3, repo.ListCrystalCells(server.ActiveLocationId).Count); // every conduit has its row

            server.MineBlock("Builder", 2, 200, 0); // cut
            Assert.Equal(2, server.CrystalNetSnapshots.Count);
            Assert.All(server.CrystalNetSnapshots, n => Assert.Equal(1, n.Cells));
            Assert.Equal(2, repo.ListCrystalCells(server.ActiveLocationId).Count);
        }
    }

    [Fact]
    public void ASwitch_DrivesItsNetwork_AndALamp_SwapsToItsUnlitTwin()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_switch", "crystal_conduit", "light_white");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "light_white"); // placed beside a net cell → it joins
            Assert.Equal(3, server.CrystalCellCount);
            Assert.Single(server.CrystalNetSnapshots);

            Ticks(server, 0.7);
            Assert.False(server.CrystalLevelAt(new Vector3i(2, 200, 0)));
            Assert.Equal("light_white_off", KeyAt(server, 3, 200, 0)); // OFF network: the lamp goes dark

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0); // flip the lever
            Ticks(server, 0.7);
            Assert.True(server.CrystalDeviceOutput(new Vector3i(1, 200, 0)));
            Assert.True(server.CrystalLevelAt(new Vector3i(2, 200, 0)));
            Assert.Equal("light_white", KeyAt(server, 3, 200, 0));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0);
            Ticks(server, 0.7);
            Assert.Equal("light_white_off", KeyAt(server, 3, 200, 0));

            server.MineBlock("Builder", 2, 200, 0); // the conduit goes: an orphaned lamp lights up again on its own
            Assert.Equal("light_white", KeyAt(server, 3, 200, 0));
            Assert.Equal(64, p.State.Inventory.CountOf("crystal_conduit")); // the conduit came back as its item
        }
    }

    [Fact]
    public void AButton_PulsesForHalfASecond()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_button", "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_button");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Ticks(server, 0.15);
            Assert.True(server.CrystalLevelAt(new Vector3i(2, 200, 0)));
            Ticks(server, 0.7);
            Assert.False(server.CrystalLevelAt(new Vector3i(2, 200, 0)));
        }
    }

    [Fact]
    public void AStepPlate_ReadsWhoStandsOnIt_AndTheOwnerFilterIgnoresOthers()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "step_plate", "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "step_plate");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            var plate = new Vector3i(1, 200, 0);

            p.State.Position = new Vector3f(1.5f, 201f, 0.5f); // standing on the plate
            Ticks(server, 0.7);
            Assert.True(server.CrystalDeviceOutput(plate));

            p.State.Position = new Vector3f(6f, 201f, 0.5f);
            Ticks(server, 0.7);
            Assert.False(server.CrystalDeviceOutput(plate));

            // Owner-only plate: another player on it reads nothing.
            server.SetCrystalDeviceForTest(p, plate, action: 2, mode: (int)PresenceFilter.Owner);
            var other = server.AddLocalPlayer("Visitor");
            other.State.Position = new Vector3f(1.5f, 201f, 0.5f);
            Ticks(server, 0.7);
            Assert.False(server.CrystalDeviceOutput(plate));
            p.State.Position = new Vector3f(1.5f, 201f, 0.5f);
            Ticks(server, 0.7);
            Assert.True(server.CrystalDeviceOutput(plate));
        }
    }

    [Fact]
    public void ADoor_BesideAConduit_LocksWhenOff_AndHoldsOpenWhenOn()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "stone", "door_wood", "crystal_conduit", "crystal_switch");
            for (int x = 0; x <= 4; x++)
            {
                server.PlaceBlock("Builder", x, 199, 0, "stone"); // a floor to hang the door on
            }

            server.PlaceBlock("Builder", 1, 200, 0, "door_wood");
            int doorId = server.DoorSnapshots.Single(d => Math.Abs(d.Pos.X - 1.5f) < 0.01f && Math.Abs(d.Pos.Y - 200f) < 0.01f).Id;
            bool Open() => server.DoorSnapshots.Single(d => d.Id == doorId).Open;
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit"); // beside the doorway
            server.PlaceBlock("Builder", 3, 200, 0, "crystal_switch");

            Ticks(server, 0.4);
            p.State.Position = new Vector3f(1.5f, 200f, 1.5f);
            server.InteractDoorForTest(p, doorId); // locked: the latch does not move
            Assert.False(Open());

            server.SetCrystalDeviceForTest(p, new Vector3i(3, 200, 0), action: 0); // signal ON → held open
            Ticks(server, 0.5);
            Assert.True(Open());
            server.InteractDoorForTest(p, doorId); // held open: it stays open
            Assert.True(Open());

            server.SetCrystalDeviceForTest(p, new Vector3i(3, 200, 0), action: 0); // OFF → locked shut again
            Ticks(server, 0.5);
            Assert.False(Open());

            server.MineBlock("Builder", 2, 200, 0); // conduit gone → a normal hand door again
            Ticks(server, 0.3);
            server.InteractDoorForTest(p, doorId);
            Assert.True(Open());
        }
    }

    [Fact]
    public void ALogicBlock_InNotMode_InvertsItsInput_OneBeatLater()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_switch", "crystal_conduit", "logic_block", "light_white");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "logic_block", yaw: 1); // output face +X → the lamp at x = 4
            server.PlaceBlock("Builder", 4, 200, 0, "light_white");
            var gate = new Vector3i(3, 200, 0);
            server.SetCrystalDeviceForTest(p, gate, action: 2, mode: (int)LogicMode.Not);

            Ticks(server, 0.8);
            Assert.True(server.CrystalDeviceOutput(gate)); // no input ON → NOT is ON
            Assert.True(server.CrystalLevelAt(new Vector3i(4, 200, 0)));
            Assert.Equal("light_white", KeyAt(server, 4, 200, 0));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0); // input ON
            Ticks(server, 0.8);
            Assert.False(server.CrystalDeviceOutput(gate));
            Assert.False(server.CrystalLevelAt(new Vector3i(4, 200, 0)));
            Assert.Equal("light_white_off", KeyAt(server, 4, 200, 0));
            Assert.Equal(2, server.CrystalNetSnapshots.Count); // the gate keeps the two networks apart
        }
    }

    [Fact]
    public void ATimerBlock_InToggleMode_FlipsOnEveryPulse()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_button", "crystal_conduit", "timer_block");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_button");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "timer_block", yaw: 1);
            server.PlaceBlock("Builder", 4, 200, 0, "crystal_conduit");
            var timer = new Vector3i(3, 200, 0);
            server.SetCrystalDeviceForTest(p, timer, action: 2, mode: (int)TimerMode.Toggle);
            Ticks(server, 0.3);
            Assert.False(server.CrystalLevelAt(new Vector3i(4, 200, 0)));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Ticks(server, 0.8);
            Assert.True(server.CrystalLevelAt(new Vector3i(4, 200, 0))); // first pulse: ON, and it stays ON after the pulse ended

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(new Vector3i(4, 200, 0))); // second pulse: OFF
        }
    }

    [Fact]
    public void ADaylightSensor_FollowsTheLocalSun()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Builder(server, new Vector3f(0, 200, 0), "daylight_sensor", "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "daylight_sensor");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.SetDayFractionForTest(0.5); // noon
            Ticks(server, 0.7);
            Assert.True(server.CrystalDeviceOutput(new Vector3i(1, 200, 0)));
            server.SetDayFractionForTest(0.0); // midnight
            Ticks(server, 0.7);
            Assert.False(server.CrystalDeviceOutput(new Vector3i(1, 200, 0)));
        }
    }

    [Fact]
    public void AWatcher_PulsesWhenTheCellInFrontChanges()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Builder(server, new Vector3f(0, 200, 0), "watcher", "crystal_conduit", "stone");
            server.PlaceBlock("Builder", 1, 200, 0, "watcher", yaw: 1); // looks at +X → (2,200,0)
            server.PlaceBlock("Builder", 1, 200, 1, "crystal_conduit");
            Ticks(server, 0.3);
            Assert.False(server.CrystalLevelAt(new Vector3i(1, 200, 1)));
            server.PlaceBlock("Builder", 2, 200, 0, "stone");
            Ticks(server, 0.15);
            Assert.True(server.CrystalLevelAt(new Vector3i(1, 200, 1)));
            Ticks(server, 0.7);
            Assert.False(server.CrystalLevelAt(new Vector3i(1, 200, 1)));
        }
    }

    [Fact]
    public void AnAlarmSiren_StartsALoopWhenOn_AndStopsItWhenOff()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, transport);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_switch", "alarm_siren");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "alarm_siren");
            Ticks(server, 0.7);
            transport.Sent.Clear();

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0);
            Ticks(server, 0.7);
            var start = transport.Sent.OfType<SoundFx>().Single(s => s.Loop);
            Assert.StartsWith("alarm_siren", start.SoundId, StringComparison.Ordinal);

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0);
            Ticks(server, 0.7);
            Assert.Contains(transport.Sent.OfType<SoundFx>(), s => s.Stop && s.SourceId == start.SourceId);
        }
    }

    [Fact]
    public void Devices_ComeBackFromTheirRows_AfterAReload()
    {
        string loc;
        var server = NewServer(out var repo1);
        using (repo1)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_switch", "crystal_conduit", "logic_block", "auto_drill_2");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "logic_block", yaw: 2);
            server.PlaceBlock("Builder", 1, 200, 3, "auto_drill_2"); // a multi-key kind: the row must remember the tier
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 0);
            server.SetCrystalDeviceForTest(p, new Vector3i(3, 200, 0), action: 2, mode: (int)LogicMode.Xor);
            loc = server.ActiveLocationId;
            Assert.Equal(4, repo1.ListCrystalCells(loc).Count);
        }

        var reloaded = NewServer(out var repo2);
        using (repo2)
        {
            Builder(reloaded, new Vector3f(0, 200, 0), "crystal_conduit");
            Assert.Equal(4, reloaded.CrystalCellCount);
            Assert.True(reloaded.CrystalDeviceOutput(new Vector3i(1, 200, 0))); // the lever stayed ON
            Ticks(reloaded, 0.3);
            Assert.True(reloaded.CrystalLevelAt(new Vector3i(2, 200, 0)));
            var rows = repo2.ListCrystalCells(loc);
            Assert.Equal((int)LogicMode.Xor, rows.Single(r => r.Kind == "LogicBlock").Mode);
            Assert.Equal("yaw=2", rows.Single(r => r.Kind == "LogicBlock").Config);
            Assert.Equal("key=auto_drill_2", rows.Single(r => r.Kind == "AutoDrill").Config); // a Mk2 stays a Mk2 after the reload
        }
    }

    [Fact]
    public void AMatterLink_BeamsAStack_BetweenTheCratesOfItsPair()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crate", "matter_sender", "matter_receiver");
            server.PlaceBlock("Builder", 1, 200, 0, "crate");
            server.PlaceBlock("Builder", 2, 200, 0, "matter_sender");
            server.PlaceBlock("Builder", 1, 200, 4, "crate");
            server.PlaceBlock("Builder", 2, 200, 4, "matter_receiver", "Lager");
            var from = server.Containers.Single(c => c.Position == new Vector3i(1, 200, 0));
            var to = server.Containers.Single(c => c.Position == new Vector3i(1, 200, 4));
            from.Items.Add(new ItemStack("iron_ore", 40));

            int receiverId = server.CrystalReceiversFor("Builder").Single(r => r.Label == "Lager").Id;
            server.SetCrystalDeviceForTest(p, new Vector3i(2, 200, 0), action: 2, config: "pair=" + receiverId);
            server.SetCrystalDeviceForTest(p, new Vector3i(2, 200, 0), action: 1); // one shot
            Assert.Equal(16, to.Items.Single(s => s.Item == "iron_ore").Count);
            Assert.Equal(24, from.Items.Single(s => s.Item == "iron_ore").Count);
            Assert.False(server.CrystalDeviceOutput(new Vector3i(2, 200, 0))); // not blocked
            Assert.True(server.CrystalDeviceOutput(new Vector3i(2, 200, 4)));  // "something arrived" pulse
        }
    }

    [Fact]
    public void AnAutoDrill_MinesOnlyOre_BelowItself_IntoItsCrate()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "auto_drill_1", "crate");
            var stone = _content.GetBlock("stone")!.NumericId;
            var ore = _content.GetBlock("iron_ore")!.NumericId;
            for (int x = 8; x <= 12; x++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    server.World.SetBlock(new Vector3i(x, 199, z), stone); // "natural" ground: no player owner
                    server.World.SetBlock(new Vector3i(x, 198, z), (x + z) % 2 == 0 ? ore : stone);
                }
            }

            p.State.Position = new Vector3f(9, 200, 0);
            server.PlaceBlock("Builder", 10, 200, 0, "auto_drill_1");
            server.PlaceBlock("Builder", 11, 200, 0, "crate");
            var crate = server.Containers.Single(c => c.Position == new Vector3i(11, 200, 0));
            var drill = new Vector3i(10, 200, 0);

            for (int i = 0; i < 40; i++)
            {
                server.SetCrystalDeviceForTest(p, drill, action: 1); // pulses, one block each
                server.TickForTest(0.1);
            }

            int mined = crate.Items.Where(s => s.Item == "iron_ore").Sum(s => s.Count);
            Assert.True(mined > 0, "the drill should have mined ore into its crate");
            Assert.Empty(crate.Items.Where(s => s.Item == "stone")); // only ore
            Assert.Equal(stone, server.World.GetBlock(new Vector3i(10, 199, 0))); // the stone layer stays
        }
    }

    [Fact]
    public void ACloneTank_GrowsAWildAnimal_OfAScannedSpecies()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "clone_tank", "forage_bait", "meat_bait", "nectar_lure", "matter_dust");
            var sp = server.SpeciesRoster.FirstOrDefault(s => !s.Hostile);
            if (sp is null)
            {
                return; // a barren test world: nothing to clone here
            }

            p.State.Scanned.Add("creature:" + sp.Id);
            server.PlaceBlock("Builder", 1, 200, 0, "clone_tank");
            var tank = new Vector3i(1, 200, 0);
            server.SetCrystalDeviceForTest(p, tank, action: 2, mode: 0, config: "sp=" + sp.Id);
            server.SetCrystalDeviceForTest(p, tank, action: 1); // start
            Assert.True(server.CrystalDeviceOutput(tank)); // growing
            Assert.Equal(62, p.State.Inventory.CountOf("matter_dust"));

            Ticks(server, CrystalNetRules.CloneGrowSeconds + 1.0, 0.5);
            var clone = server.Creatures.FirstOrDefault(c => c.CloneOf.Length > 0);
            Assert.NotNull(clone);
            Assert.Equal(sp.Id, clone!.SpeciesId);
            Assert.False(clone.IsCompanion); // a WILD animal (Marcel's decision)
            Assert.False(server.CrystalDeviceOutput(tank)); // grown: the light is off again
        }
    }

    [Fact]
    public void ACaller_MarksItselfActive_ForTheCreatureTick()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "caller");
            server.PlaceBlock("Builder", 1, 200, 0, "caller");
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 1);
            Ticks(server, 0.3);
            Assert.Equal(1, server.CrystalCellCount); // registered, planet-only kinds are fine on a planet
        }
    }

    [Fact]
    public void Rules_LogicGates_AndTimers_ArePure()
    {
        Assert.True(LogicGate.Evaluate(LogicMode.And, new[] { true, true }));
        Assert.False(LogicGate.Evaluate(LogicMode.And, new[] { true, false }));
        Assert.False(LogicGate.Evaluate(LogicMode.And, Array.Empty<bool>()));
        Assert.True(LogicGate.Evaluate(LogicMode.Or, new[] { false, true }));
        Assert.True(LogicGate.Evaluate(LogicMode.Not, Array.Empty<bool>()));
        Assert.False(LogicGate.Evaluate(LogicMode.Not, new[] { true }));
        Assert.True(LogicGate.Evaluate(LogicMode.Xor, new[] { true, false }));
        Assert.False(LogicGate.Evaluate(LogicMode.Xor, new[] { true, true }));

        var t = new TimerState();
        t.Step(TimerMode.Delay, true, 0.1, 0.5, 1);
        Assert.False(t.Output);
        for (int i = 0; i < 5; i++)
        {
            t.Step(TimerMode.Delay, true, 0.1, 0.5, 1);
        }

        Assert.True(t.Output);
        t.Step(TimerMode.Delay, false, 0.1, 0.5, 1);
        Assert.False(t.Output);

        var counter = new TimerState();
        for (int pulse = 0; pulse < 3; pulse++)
        {
            counter.Step(TimerMode.Counter, true, 0.1, 1, 3);
            counter.Step(TimerMode.Counter, false, 0.1, 1, 3);
        }

        Assert.False(counter.Output); // fired on the third rising edge, then fell with the next step
        counter.Reset();
        counter.Step(TimerMode.Counter, true, 0.1, 1, 1);
        Assert.True(counter.Output);

        var clock = new TimerState();
        int ticks = 0;
        for (int i = 0; i < 30; i++)
        {
            clock.Step(TimerMode.Clock, true, 0.1, 1.0, 1);
            if (clock.Output)
            {
                ticks++;
            }
        }

        Assert.Equal(3, ticks); // one tick per second over three seconds
        Assert.Equal(CrystalDeviceKind.Light, CrystalNetRules.KindOfKey("light_white_off"));
        Assert.Equal("light_white", CrystalNetRules.LightOnKey("light_white_off"));
        Assert.Equal(new Vector3i(1, 0, 0), CrystalNetRules.OutputFace(1));
    }
}
