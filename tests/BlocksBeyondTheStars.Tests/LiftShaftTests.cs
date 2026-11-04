// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Lyxette's lift (2026-11-03) through the real server tick: a stop on the bottom floor calls the platform back down
/// (#3161), weather snow neither settles in a shaft nor blocks the ride (#3162), and a press that cannot be answered says
/// why (#3163). The lift itself (#2266) is pinned in <see cref="CrystalNet2Tests"/>.
/// </summary>
public sealed class LiftShaftTests : IDisposable
{
    private static readonly Vector3i Motor = new(2, 200, 2);
    private static readonly Vector3i UpperStop = new(4, 205, 2);  // five up: the platform's top comes level with y 205
    private static readonly Vector3i BottomStop = new(4, 202, 2); // the bottom landing: level with the resting platform's top

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_liftshaft_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly RecordingTransport _transport = new();
    private SqliteWorldRepository? _repo;

    public void Dispose()
    {
        _transport.Dispose();
        _repo?.Dispose();
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

    /// <summary>A server with a lift high in the air: the motor, a stone pit floor beside it (so a drift has something to
    /// lie on), and the builder standing beside the shaft with stops, stone and snow in the pack.</summary>
    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player) Lift()
    {
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, "liftshaft"));
        var config = new ServerConfig { WorldName = "liftshaft", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, _transport, _repo);
        server.Start();
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(6, 203, 2); // up in the air → the cells are empty + within reach
        foreach (var key in new[] { "lift_motor", "lift_stop", "stone", "snow" })
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024));
        }

        p.State.SuitEnergy = 100f;
        server.PlaceBlock("Builder", Motor.X, Motor.Y, Motor.Z, "lift_motor");
        server.PlaceBlock("Builder", Motor.X + 1, Motor.Y, Motor.Z, "stone"); // the pit floor beside the motor
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor)); // resting on the motor
        return (server, p);
    }

    private static void Press(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, Vector3i cell, double seconds = 2.0)
    {
        server.SetCrystalDeviceForTest(p, cell, action: 1);
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    private string[] Said(BlocksBeyondTheStars.GameServer.PlayerSession p)
        => _transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text).ToArray();

    [Fact]
    public void AStopOnTheBottomFloor_CallsThePlatformBackDown_AndShowsItIsThere()
    {
        var (server, p) = Lift();
        server.PlaceBlock("Builder", UpperStop.X, UpperStop.Y, UpperStop.Z, "lift_stop");
        server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop");

        Press(server, p, UpperStop);
        Assert.Equal(204f, server.LiftPlatformYForTest(Motor));
        Assert.False(server.CrystalDeviceOutput(BottomStop));

        Press(server, p, BottomStop); // Lyxette's switch: it used to belong to no lift
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
        Assert.True(server.CrystalDeviceOutput(BottomStop));   // "the platform is here"
        Assert.False(server.CrystalDeviceOutput(UpperStop));
        Assert.DoesNotContain(Said(p), t => t.StartsWith("@srv.lift.", StringComparison.Ordinal));
    }

    [Fact]
    public void WeatherSnow_NeverSettlesInAShaft_ButStillBesideIt()
    {
        var (server, _) = Lift();
        server.PlaceBlock("Builder", Motor.X + 4, Motor.Y, Motor.Z, "stone"); // a ledge outside the shaft

        Assert.False(server.SnowSurfaceForTest(Motor.X + 1, Motor.Y + 2, Motor.Z, out var inShaft), $"a drift in the shaft at {inShaft}");
        Assert.False(server.SnowSurfaceForTest(Motor.X, Motor.Y + 2, Motor.Z, out var onMotor), $"a drift on the motor at {onMotor}");
        Assert.True(server.SnowSurfaceForTest(Motor.X + 4, Motor.Y + 2, Motor.Z, out var outside), "snow still settles beside the shaft");
        Assert.Equal(new Vector3i(Motor.X + 4, Motor.Y + 1, Motor.Z), outside);
    }

    [Fact]
    public void ADriftThatLayInTheShaft_IsClearedForTheRide_ButAPlayersSnowStillBlocks()
    {
        var (server, p) = Lift();
        server.PlaceBlock("Builder", UpperStop.X, UpperStop.Y, UpperStop.Z, "lift_stop");
        server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop");
        var drift = new Vector3i(Motor.X + 1, Motor.Y + 1, Motor.Z);
        server.DepositWeatherSnowForTest(drift.X, drift.Y, drift.Z); // a drift from before the fix, in the platform's layer
        Assert.Equal("snow", KeyAt(server, drift));

        Press(server, p, UpperStop);
        Assert.Equal(204f, server.LiftPlatformYForTest(Motor)); // the drift no longer blocks the ride …
        Assert.Equal("air", KeyAt(server, drift));                // … it is gone
        Assert.False(server.CrystalDeviceOutput(Motor));

        server.PlaceBlock("Builder", drift.X, drift.Y, drift.Z, "snow"); // the player's own snow is a block like any other
        Assert.Equal("snow", KeyAt(server, drift));
        Press(server, p, BottomStop);
        Assert.Equal(204f, server.LiftPlatformYForTest(Motor));
        Assert.True(server.CrystalDeviceOutput(Motor)); // blocked: the amber light
        Assert.Equal("snow", KeyAt(server, drift));
        Assert.Contains("@srv.lift.blocked_bottom", Said(p));
    }

    [Fact]
    public void APressThatCannotBeAnswered_SaysWhy()
    {
        var (server, p) = Lift();

        Press(server, p, Motor); // a motor without stops
        Assert.Contains("@srv.lift.no_stops", Said(p));

        var stray = new Vector3i(7, 205, 5); // not beside the shaft
        server.PlaceBlock("Builder", stray.X, stray.Y, stray.Z, "lift_stop");
        Press(server, p, stray);
        Assert.Contains("@srv.lift.stop_no_shaft", Said(p));

        server.PlaceBlock("Builder", UpperStop.X, UpperStop.Y, UpperStop.Z, "lift_stop");
        server.PlaceBlock("Builder", Motor.X + 1, Motor.Y + 3, Motor.Z, "stone"); // a block in the shaft, two above the bottom floor
        Press(server, p, UpperStop);
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
        Assert.Contains("@srv.lift.blocked:2", Said(p));
    }

    // ---- #3171: choosing the floor -------------------------------------------------------------------------------------

    private static readonly Vector3i TopStop = new(4, 211, 2); // ten up: the platform rests at 210 there

    /// <summary>A three-floor lift: the bottom, the middle (<see cref="UpperStop"/>) and the top.</summary>
    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player) ThreeFloors()
    {
        var (server, p) = Lift();
        server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop");
        server.PlaceBlock("Builder", UpperStop.X, UpperStop.Y, UpperStop.Z, "lift_stop");
        p.State.Position = new Vector3f(6, 209, 2); // up by the top landing
        server.PlaceBlock("Builder", TopStop.X, TopStop.Y, TopStop.Z, "lift_stop");
        p.State.Position = new Vector3f(6, 203, 2);
        return (server, p);
    }

    /// <summary>The floor picker's "go": action 5 at the motor with the floor's platform height.</summary>
    private static void GoTo(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, int level, double seconds = 4.0)
    {
        server.SetCrystalDeviceForTest(p, Motor, action: 5, mode: level);
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    private string[] Refused(BlocksBeyondTheStars.GameServer.PlayerSession p)
        => _transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason).ToArray();

    private NetLift LastLift(BlocksBeyondTheStars.GameServer.PlayerSession p)
        => _transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<LiftList>().Last().Lifts.Single();

    [Fact]
    public void TheFloorPicker_RidesFromTheTopDown_AndFromTheMiddleUp()
    {
        var (server, p) = ThreeFloors();
        p.State.Position = new Vector3f(6, 209, 2);
        Press(server, p, TopStop, seconds: 4.0);
        Assert.Equal(210f, server.LiftPlatformYForTest(Motor));

        p.State.Position = new Vector3f(Motor.X + 0.5f, 211, Motor.Z + 0.5f); // on the platform, the motor 10 blocks below
        GoTo(server, p, 201);
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor)); // Lyxette: "from up here I want to ride down"

        p.State.Position = new Vector3f(5.5f, 205, 2.5f); // on the middle landing, beside its stop
        Press(server, p, UpperStop);
        Assert.Equal(204f, server.LiftPlatformYForTest(Motor));
        GoTo(server, p, 210);
        Assert.Equal(210f, server.LiftPlatformYForTest(Motor));
        Assert.True(server.CrystalDeviceOutput(TopStop));
        Assert.DoesNotContain("@srv.lift.too_far", Refused(p));
    }

    [Fact]
    public void TheFloorPicker_IsRefused_OutOfReach_OnSomeoneElsesLift_AndForAFloorThatIsNone()
    {
        var (server, p) = ThreeFloors();
        p.State.Position = new Vector3f(30, 205, 30); // far from the platform and every stop
        GoTo(server, p, 210);
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
        Assert.Contains("@srv.lift.too_far", Refused(p));

        var stranger = server.AddLocalPlayer("Stranger");
        stranger.State.Position = new Vector3f(Motor.X + 0.5f, 202, Motor.Z + 0.5f); // on the platform, but not his lift
        GoTo(server, stranger, 210);
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
        Assert.Contains("@srv.crystal.owner_only", Refused(stranger));

        p.State.Position = new Vector3f(Motor.X + 0.5f, 202, Motor.Z + 0.5f);
        GoTo(server, p, 207); // no stop at that height
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
        Assert.Contains("@srv.lift.no_floor", Said(p));

        GoTo(server, p, 201); // already there: said, not silent
        Assert.Contains("@srv.lift.already_here", Said(p));
    }

    [Fact]
    public void ThePressOnTheStopWhereThePlatformWaits_SaysItIsHere()
    {
        var (server, p) = Lift();
        server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop");
        Press(server, p, BottomStop);
        Assert.Contains("@srv.lift.already_here", Said(p));
    }

    [Fact]
    public void TheFloorsAndTheirNames_RideOnTheLiftList_AndAFloorIsNamedFromThePicker()
    {
        var (server, p) = ThreeFloors();
        Press(server, p, UpperStop, seconds: 2.0); // ticks: the floors are read and go out
        var lift = LastLift(p);
        Assert.Equal(new[] { 201, 204, 210 }, lift.Levels);
        Assert.Equal(new[] { true, true, true }, lift.LevelHasStop);
        Assert.All(lift.LevelNames, n => Assert.Equal(string.Empty, n));

        p.State.Position = new Vector3f(Motor.X + 0.5f, 205, Motor.Z + 0.5f); // a rider names the top floor from the middle one
        server.SetCrystalDeviceForTest(p, Motor, action: 6, mode: 210, label: "Roof garden");
        Press(server, p, UpperStop, seconds: 1.0);
        Assert.Equal(new[] { string.Empty, string.Empty, "Roof garden" }, LastLift(p).LevelNames);
        Assert.Equal("Roof garden", server.CrystalDeviceLabelForTest(TopStop));

        server.SetCrystalDeviceForTest(p, Motor, action: 6, mode: 210, label: ""); // an empty name clears it
        Press(server, p, UpperStop, seconds: 1.0);
        Assert.Equal(string.Empty, LastLift(p).LevelNames[2]);

        p.State.Position = new Vector3f(6, 203, 2);
        server.MineBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z); // the bottom floor stays — without a stop
        Press(server, p, UpperStop, seconds: 1.0);
        Assert.Equal(new[] { false, true, true }, LastLift(p).LevelHasStop);
        server.SetCrystalDeviceForTest(p, Motor, action: 6, mode: 201, label: "Cellar");
        Assert.Contains("@srv.lift.floor_no_stop", Said(p));
    }
}
