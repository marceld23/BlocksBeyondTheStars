// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
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
    /// lie on), and the builder standing beside the shaft with stops, stone and snow in the pack. #3204: on the given planet,
    /// with the motor where the test wants it.</summary>
    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player) Lift(string planet = "varied", Vector3i? at = null)
    {
        var motor = at ?? Motor;
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, "liftshaft"));
        var config = new ServerConfig { WorldName = "liftshaft", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, StartPlanet = planet };
        var server = new SvGameServer(config, _content, _transport, _repo);
        server.Start();
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(motor.X + 4, motor.Y + 3, motor.Z); // up in the air → the cells are empty + within reach
        foreach (var key in new[] { "lift_motor", "lift_stop", "stone", "snow" })
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024));
        }

        p.State.SuitEnergy = 100f;
        server.PlaceBlock("Builder", motor.X, motor.Y, motor.Z, "lift_motor");
        server.PlaceBlock("Builder", motor.X + 1, motor.Y, motor.Z, "stone"); // the pit floor beside the motor
        Assert.Equal(motor.Y + 1f, server.LiftPlatformYForTest(motor)); // resting on the motor
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
        Assert.Contains("@srv.lift.stop_no_shaft:" + CrystalNetRules.LiftMaxHeight, Said(p)); // #3204: the height from the rule

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

    // ---- #3204: up to 256 cells — a speed curve, the shaft read on the way, the space elevator -----------------------

    private const string SpaceElevatorLine = "@srv.lift.space_elevator";

    /// <summary>Ticks for up to <paramref name="seconds"/> (until the platform has arrived), the rider's feet carried on the
    /// platform's top the way the client carries them. Returns the fastest speed of the ride and how long it took.</summary>
    private static (float Fastest, double Seconds) RideAlong(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession? rider, Vector3i motor, double seconds)
    {
        float fastest = 0f;
        double t = 0;
        for (; t < seconds; t += 0.1)
        {
            if (rider is not null && server.LiftPlatformYForTest(motor) is float y)
            {
                rider.State.Position = new Vector3f(motor.X + 0.5f, y + 1f, motor.Z + 0.5f);
            }

            server.TickForTest(0.1);
            float speed = server.LiftSpeedForTest(motor) ?? 0f;
            fastest = Math.Max(fastest, speed);
            if (speed <= 0f && t > 0.15)
            {
                break; // arrived (or stopped short)
            }
        }

        return (fastest, t);
    }

    private void Set(SvGameServer server, Vector3i cell, string key)
        => server.World.SetBlock(cell, key == "air" ? BlocksBeyondTheStars.Shared.Primitives.BlockId.Air : _content.GetBlock(key)!.NumericId);

    [Fact]
    public void AStopAtTheTopOfThe256CellShaft_IsAFloor_TheLongRideIsFast_AndTellsTheRiderOnceThatTheAirIsBelow()
    {
        var (server, p) = Lift();
        var top = new Vector3i(4, Motor.Y + CrystalNetRules.LiftMaxHeight, 2);      // the highest stop the shaft takes
        var beyond = new Vector3i(0, Motor.Y + CrystalNetRules.LiftMaxHeight + 1, 2); // one higher: no lift's stop
        var middle = new Vector3i(4, 351, 2);                                          // a floor above the air as well
        double line = server.AtmosphereHeight;
        Assert.True(line > Motor.Y + 2 && line < middle.Y - 1, $"the shaft must cross this world's atmosphere line ({line})");
        foreach (var stop in new[] { middle, top, beyond })
        {
            p.State.Position = new Vector3f(2, stop.Y - 2, 4.5f);
            server.PlaceBlock("Builder", stop.X, stop.Y, stop.Z, "lift_stop");
        }

        Press(server, p, beyond);
        Assert.Contains("@srv.lift.stop_no_shaft:" + CrystalNetRules.LiftMaxHeight, Said(p));
        Assert.Equal(new[] { 201, middle.Y - 1, top.Y - 1 }, server.LiftLevelsForTest(Motor));

        p.State.Position = new Vector3f(Motor.X + 0.5f, Motor.Y + 2, Motor.Z + 0.5f); // on the platform at the bottom
        server.SetCrystalDeviceForTest(p, Motor, action: 5, mode: top.Y - 1);         // the floor picker: to the top
        var ride = RideAlong(server, p, Motor, 60.0);
        Assert.Equal(top.Y - 1f, server.LiftPlatformYForTest(Motor));
        Assert.True(server.CrystalDeviceOutput(top));
        Assert.InRange(ride.Fastest, CrystalNetRules.LiftSpeed * 3, LiftMotion.MaxSpeed); // it really sped up …
        Assert.True(ride.Seconds < 35.0, $"255 cells took {ride.Seconds:0.0} s");          // … (85 s at the old pace)
        Assert.Single(Said(p), t => t == SpaceElevatorLine); // out of the air: said once

        server.SetCrystalDeviceForTest(p, Motor, action: 5, mode: middle.Y - 1); // between two floors above the air
        RideAlong(server, p, Motor, 30.0);
        Assert.Equal(middle.Y - 1f, server.LiftPlatformYForTest(Motor));
        Assert.Single(Said(p), t => t == SpaceElevatorLine); // nothing new to say
    }

    [Theory]
    [InlineData("varied", true)]   // breathable air below the line
    [InlineData("crystal", false)] // an airless body: there is no air to leave
    public void ARideOutOfTheAir_TellsItsRiders_OnlyWhereThereIsAir(string planet, bool told)
    {
        var low = new Vector3i(2, 140, 2); // below both worlds' atmosphere lines (crystal 150, varied 240)
        var (server, p) = Lift(planet, low);
        var high = new Vector3i(4, 261, 2);
        Assert.InRange(server.AtmosphereHeight, low.Y + 3, high.Y - 2);
        p.State.Position = new Vector3f(6, high.Y - 2, 2);
        server.PlaceBlock("Builder", high.X, high.Y, high.Z, "lift_stop");

        p.State.Position = new Vector3f(low.X + 0.5f, low.Y + 2, low.Z + 0.5f);
        server.SetCrystalDeviceForTest(p, low, action: 5, mode: high.Y - 1);
        RideAlong(server, p, low, 40.0);
        Assert.Equal(high.Y - 1f, server.LiftPlatformYForTest(low));
        Assert.Equal(told ? 1 : 0, Said(p).Count(t => t == SpaceElevatorLine));
    }

    /// <summary>A rider's feet stand on the platform's top (one above its cell) and the head reaches this far above the feet.</summary>
    private const float RiderHeight = 1.8f;

    [Fact]
    public void ABlockAHundredUpATwoHundredCellRide_DoesNotHoldTheStart_AndTheRideStopsWithTheRidersHeadBelowIt()
    {
        var (server, p) = Lift();
        var top = new Vector3i(4, 401, 2);                                  // two hundred up: the platform rests at 400 there
        var block = new Vector3i(Motor.X - 1, Motor.Y + 100, Motor.Z + 1); // in the shaft, far past the layers read at the press
        p.State.Position = new Vector3f(6, top.Y - 2, 2);
        server.PlaceBlock("Builder", top.X, top.Y, top.Z, "lift_stop");
        Set(server, block, "stone");

        p.State.Position = new Vector3f(Motor.X + 0.5f, Motor.Y + 2, Motor.Z + 0.5f); // on the platform at the bottom
        server.SetCrystalDeviceForTest(p, Motor, action: 5, mode: top.Y - 1);         // up at full speed
        Assert.False(server.CrystalDeviceOutput(Motor)); // no mass read at the press: the start goes ahead
        RideAlong(server, p, Motor, 40.0);               // the rider carried on the platform's top, as the client does

        float platform = server.LiftPlatformYForTest(Motor)!.Value;
        Assert.Equal(block.Y - 3f, platform);                   // feet two under the block …
        Assert.True(platform + 1f + RiderHeight < block.Y);     // … and the head below it (the review's case)
        Assert.True(server.CrystalDeviceOutput(Motor));         // blocked: the amber light
        Assert.False(server.CrystalDeviceOutput(top));          // not arrived
        Assert.Contains("@srv.lift.blocked:99", Said(p));       // counted from the bottom floor
        var lists = _transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<LiftList>().SelectMany(l => l.Lifts);
        Assert.Contains(lists, l => l.Moving && l.TargetY == block.Y - 3f); // the clients learned the short end before it got there
    }

    [Fact]
    public void ABlockPutIntoTheShaftDuringTheRide_StopsThePlatformWhereItsRiderStaysClearOfIt()
    {
        var (server, p) = Lift();
        var stop = new Vector3i(4, 241, 2);
        p.State.Position = new Vector3f(6, 239, 2);
        server.PlaceBlock("Builder", stop.X, stop.Y, stop.Z, "lift_stop");
        server.SetCrystalDeviceForTest(p, stop, action: 1);
        for (int i = 0; i < 20; i++)
        {
            server.TickForTest(0.1);
        }

        float platform = server.LiftPlatformYForTest(Motor)!.Value;
        Assert.InRange(platform, 203f, 212f); // on its way
        var ahead = new Vector3i(Motor.X + 1, 218, Motor.Z - 1);
        Set(server, ahead, "stone"); // someone builds into the shaft ahead of it
        RideAlong(server, null, Motor, 30.0);
        Assert.Equal(215f, server.LiftPlatformYForTest(Motor)); // a rider's head stays below 218
        Assert.True(server.CrystalDeviceOutput(Motor));
        Assert.Contains("@srv.lift.blocked:17", Said(p));

        Set(server, ahead, "air");
        server.SetCrystalDeviceForTest(p, stop, action: 1); // on again — and this time a block lands right over the rider's head
        for (int i = 0; i < 10; i++)
        {
            server.TickForTest(0.1);
        }

        platform = server.LiftPlatformYForTest(Motor)!.Value;
        int overHead = (int)Math.Ceiling(platform) + 3; // the next layer a rider's head enters: already past the read-ahead
        Set(server, new Vector3i(Motor.X, overHead, Motor.Z), "stone");
        RideAlong(server, null, Motor, 30.0);
        Assert.Equal(overHead - 3f, server.LiftPlatformYForTest(Motor)); // stopped at once, the head still below it
        Assert.Contains("@srv.lift.blocked:" + (overHead - (Motor.Y + 1)), Said(p));
    }

    [Fact]
    public void AFastPlatformComingDown_StillWaitsForABodyInTheShaft_AndTheClientsHoldIt()
    {
        var (server, p) = Lift();
        var high = new Vector3i(4, 401, 2); // two hundred up: the way down cruises at full speed
        p.State.Position = new Vector3f(6, 399, 2);
        server.PlaceBlock("Builder", high.X, high.Y, high.Z, "lift_stop");
        server.SetCrystalDeviceForTest(p, high, action: 1);
        RideAlong(server, null, Motor, 40.0);
        Assert.Equal(400f, server.LiftPlatformYForTest(Motor));

        var kid = server.AddLocalPlayer("Kid");
        kid.State.Position = new Vector3f(Motor.X + 0.5f, 300, Motor.Z + 0.5f); // in the shaft: feet at 300, head at 301
        server.SetCrystalDeviceForTest(p, Motor, action: 5, mode: 201);        // called down from the top floor
        RideAlong(server, null, Motor, 20.0);
        Assert.True(server.LiftPlatformYForTest(Motor) >= 302f, $"the platform came down to {server.LiftPlatformYForTest(Motor)}");
        Assert.Equal(0f, server.LiftWireSpeedForTest(Motor)); // the clients hold it while it waits

        kid.State.Position = new Vector3f(6, 300, 2); // stepped out of the shaft
        RideAlong(server, null, Motor, 30.0);
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
    }

    // ---- #3300–#3302: the build advice — the platform rests on top of the motor -------------------------------------

    /// <summary>Places a block with the builder standing close by (the cell within reach, not in the builder's own body).</summary>
    private static void PlaceNear(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, Vector3i cell, string key)
    {
        p.State.Position = new Vector3f(cell.X + 4, cell.Y + 3, cell.Z);
        server.PlaceBlock("Builder", cell.X, cell.Y, cell.Z, key);
    }

    private static int Count(string[] said, string text) => said.Count(t => t == text);

    [Fact]
    public void AStopSetTooLowBesideTheShaft_SaysSo_WhenPlacedAndWhenPressed()
    {
        var (server, p) = Lift();
        var low = new Vector3i(4, Motor.Y + 1, 2); // in the ring, one below the bottom floor's stop (on the ground beside a motor set into it)
        server.PlaceBlock("Builder", low.X, low.Y, low.Z, "lift_stop");
        Assert.Equal(1, Count(Said(p), "@srv.lift.stop_too_low")); // said at once, when placed

        Press(server, p, low);
        Assert.Equal(2, Count(Said(p), "@srv.lift.stop_too_low")); // and again on the press — not "two blocks from the middle"
        Assert.DoesNotContain(Said(p), t => t.StartsWith("@srv.lift.stop_no_shaft", StringComparison.Ordinal));
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));

        var level = new Vector3i(4, Motor.Y, 4); // a corner of the ring, level with the motor (beside a motor set on the ground)
        server.PlaceBlock("Builder", level.X, level.Y, level.Z, "lift_stop");
        Assert.Equal(3, Count(Said(p), "@srv.lift.stop_too_low"));

        server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop"); // where it belongs: nothing to say
        Assert.Equal(3, Count(Said(p), "@srv.lift.stop_too_low"));

        var deep = new Vector3i(0, Motor.Y - 2, 2); // in the ring but far below: no lift's stop at all
        PlaceNear(server, p, deep, "lift_stop");
        Press(server, p, deep);
        Assert.Equal(3, Count(Said(p), "@srv.lift.stop_too_low"));
        Assert.Contains("@srv.lift.stop_no_shaft:" + CrystalNetRules.LiftMaxHeight, Said(p));
    }

    [Fact]
    public void APressOnTheMotor_WithOnlyTheBottomStop_AsksForAStopHigherUp()
    {
        var (server, p) = Lift();
        server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop");

        Press(server, p, Motor);
        Assert.Contains("@srv.lift.only_bottom_floor", Said(p)); // the lift has a stop — it has no other floor yet
        Assert.DoesNotContain("@srv.lift.no_stops", Said(p));
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
    }

    [Fact]
    public void AMotorPlacedAboveTheGround_SaysHowHighThePlatformWillRest_AMotorInAPitSaysNothing()
    {
        var (server, p) = Lift(); // the harness's motor hangs in the air: no ground near, nothing to say
        Assert.DoesNotContain(Said(p), t => t.StartsWith("@srv.lift.motor_", StringComparison.Ordinal));

        // On the ground: the floor beside the shaft lies one below the motor — the platform's top two above it, out of
        // reach. A wall beside the shaft is no floor.
        var onGround = new Vector3i(20, 200, 2);
        Set(server, new Vector3i(onGround.X + 2, onGround.Y - 1, onGround.Z), "stone");
        for (int dy = 1; dy <= 3; dy++)
        {
            Set(server, new Vector3i(onGround.X - 2, onGround.Y + dy, onGround.Z), "stone");
        }

        PlaceNear(server, p, onGround, "lift_motor");
        Assert.Equal(1, Count(Said(p), "@srv.lift.motor_high"));

        // Set into the ground: the floor level with the motor — a one-block step.
        var inGround = new Vector3i(30, 200, 2);
        Set(server, new Vector3i(inGround.X - 2, inGround.Y, inGround.Z + 1), "stone");
        PlaceNear(server, p, inGround, "lift_motor");
        Assert.Equal(1, Count(Said(p), "@srv.lift.motor_step"));

        // In the floor of a 3×3 pit: the ground beside the shaft is level with the platform's top — the right build.
        var inPit = new Vector3i(40, 200, 2);
        Set(server, new Vector3i(inPit.X + 2, inPit.Y + 1, inPit.Z - 2), "stone");
        Set(server, new Vector3i(inPit.X - 2, inPit.Y - 1, inPit.Z), "stone"); // lower ground elsewhere does not matter
        PlaceNear(server, p, inPit, "lift_motor");
        Assert.Equal(1, Count(Said(p), "@srv.lift.motor_high"));
        Assert.Equal(1, Count(Said(p), "@srv.lift.motor_step"));
        Assert.Equal(201f, server.LiftPlatformYForTest(Motor));
    }
}
