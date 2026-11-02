// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The watcher reacts to more than block changes (#3092): a switch flipped in front of it, a wire changing its level, a
/// door opening — and its eye is no wire, so what it looks at is never pulled into its own network (a lamp stays a lamp,
/// a door is not locked). Plus #3091 for the heal tank: a wire nothing drives leaves it healing. The sentry's side of
/// both issues is in <see cref="SentryTests"/>. All through the real server tick.
/// </summary>
public sealed class CrystalWatcherTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrystalWatcherTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_watcher_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "watcher"));
        var config = new ServerConfig { WorldName = "watcher", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer("Builder");
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

    // ---------------------------------------------------------------------------------------------------
    // #3092: what the watcher sees
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AWatcher_PulsesWhenTheSwitchInFrontIsFlipped_BothWays()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(1, 202, 0), "watcher", "crystal_conduit", "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "watcher", yaw: 1);     // looks at +X → (2,200,0)
            server.PlaceBlock("Builder", 1, 200, 1, "crystal_conduit");     // its wire, on a side
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_switch");      // in front of its eye
            var wire = new Vector3i(1, 200, 1);
            var lever = new Vector3i(2, 200, 0);
            Ticks(server, 0.8);
            Assert.Equal(2, server.CrystalNetSnapshots.Count); // the eye is no wire: the switch is a network of its own
            Assert.False(server.CrystalLevelAt(wire));

            server.SetCrystalDeviceForTest(p, lever, action: 0); // ON
            Ticks(server, 0.3);
            Assert.True(server.CrystalLevelAt(wire));            // the watcher saw it
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(wire));           // one pulse, not a copy of the lever
            Assert.True(server.CrystalLevelAt(lever));

            server.SetCrystalDeviceForTest(p, lever, action: 0); // OFF — a change too
            Ticks(server, 0.3);
            Assert.True(server.CrystalLevelAt(wire));
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(wire));
        }
    }

    [Fact]
    public void AWatcher_PulsesWhenTheWireInFrontChangesItsLevel()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(1, 202, 0), "watcher", "crystal_conduit", "crystal_switch");
            server.PlaceBlock("Builder", 1, 200, 0, "watcher", yaw: 1);     // looks at (2,200,0)
            server.PlaceBlock("Builder", 1, 200, 1, "crystal_conduit");     // its own wire
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");     // the watched wire …
            server.PlaceBlock("Builder", 3, 200, 0, "crystal_switch");      // … and what drives it
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(new Vector3i(1, 200, 1)));

            server.SetCrystalDeviceForTest(p, new Vector3i(3, 200, 0), action: 0);
            Ticks(server, 0.3);
            Assert.True(server.CrystalLevelAt(new Vector3i(1, 200, 1)));
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(new Vector3i(1, 200, 1)));
        }
    }

    [Fact]
    public void AWatcher_LookingIntoADoorway_SeesTheDoorOpen_AndDoesNotLockIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "stone", "door_wood", "crystal_conduit", "watcher");
            for (int x = 0; x <= 4; x++)
            {
                server.PlaceBlock("Builder", x, 199, 0, "stone"); // a floor to hang the door on
            }

            server.PlaceBlock("Builder", 1, 200, 0, "door_wood");
            int doorId = server.DoorSnapshots.Single(d => Math.Abs(d.Pos.X - 1.5f) < 0.01f && Math.Abs(d.Pos.Y - 200f) < 0.01f).Id;
            bool Open() => server.DoorSnapshots.Single(d => d.Id == doorId).Open;
            server.PlaceBlock("Builder", 2, 200, 0, "watcher", yaw: 3);     // beside the doorway, looking into it (−X)
            server.PlaceBlock("Builder", 3, 200, 0, "crystal_conduit");     // its wire, behind it
            var wire = new Vector3i(3, 200, 0);
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(wire));

            p.State.Position = new Vector3f(1.5f, 200f, 1.5f);
            server.InteractDoorForTest(p, doorId); // not locked: the watcher's eye does not hold the door
            Assert.True(Open());
            Ticks(server, 0.3);
            Assert.True(server.CrystalLevelAt(wire)); // and it saw the door open
            Ticks(server, 0.8);
            Assert.False(server.CrystalLevelAt(wire));

            server.InteractDoorForTest(p, doorId);
            Assert.False(Open());
            Ticks(server, 0.3);
            Assert.True(server.CrystalLevelAt(wire)); // closing is a change too
        }
    }

    [Fact]
    public void AWatcher_DoesNotWireTheLampItLooksAt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Builder(server, new Vector3f(1, 202, 0), "watcher", "light_white");
            server.PlaceBlock("Builder", 1, 200, 0, "watcher", yaw: 1);
            server.PlaceBlock("Builder", 2, 200, 0, "light_white"); // in front of its eye
            Ticks(server, 0.8);
            Assert.Equal(1, server.CrystalCellCount);               // only the watcher: the lamp is no port
            Assert.Equal("light_white", KeyAt(server, 2, 200, 0));  // and it stays lit
        }
    }

    [Fact]
    public void TurningAWatcher_MovesItsEye_TheOldSideBecomesAWire()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, new Vector3f(1, 202, 0), "watcher", "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "watcher", yaw: 1);  // looks at (2,200,0)
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            Ticks(server, 0.3);
            Assert.Equal(2, server.CrystalNetSnapshots.Count);           // watched, not joined

            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 3); // Turn: yaw 1 → 2 (−Z)
            Assert.Equal(2, server.CrystalDeviceYaw(new Vector3i(1, 200, 0)));
            Assert.Single(server.CrystalNetSnapshots);                   // the old eye side is a wire now
            Assert.Equal(2, server.CrystalNetSnapshots[0].Cells);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // #3091: a wire nothing drives leaves a heal tank healing
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public void AHealTank_BesideABareConduit_KeepsHealing_UntilASwitchJoinsTheWire()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Builder(server, new Vector3f(3, 201, 2), "heal_tank", "crystal_conduit", "crystal_switch");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 2, 200, 1, "heal_tank");
            var tank = new Vector3i(2, 200, 1);
            Ticks(server, 0.8);
            Assert.NotNull(server.CrystalDeviceOutput(tank));    // wired …
            Assert.False(server.CrystalDeviceHeldForTest(tank)); // … but nothing drives the wire
            Assert.True(server.NearHealTankForTest("Builder"));

            server.PlaceBlock("Builder", 1, 200, 0, "crystal_switch"); // placed OFF: now the wire is a circuit
            Ticks(server, 0.8);
            Assert.True(server.CrystalDeviceHeldForTest(tank));
            Assert.False(server.NearHealTankForTest("Builder"));

            server.MineBlock("Builder", 1, 200, 0); // the switch is gone: the tank heals again
            Ticks(server, 0.8);
            Assert.False(server.CrystalDeviceHeldForTest(tank));
            Assert.True(server.NearHealTankForTest("Builder"));
        }
    }
}
