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
/// #3174 (Lyxette 2026-11-04: "a barrier so nobody falls into the shaft when the platform isn't there"): the lift gate at a
/// floor's landing opens while the platform rests there and closes when it leaves, waits for a body before it closes,
/// belongs to its own floor, puts a stale state right, and leaves every gate outside the shaft's ring alone. Through the
/// real server tick.
/// </summary>
public sealed class LiftGateTests : IDisposable
{
    private static readonly Vector3i Motor = new(2, 200, 2);
    private static readonly Vector3i BottomStop = new(4, 202, 2); // the bottom landing (platform at 201)
    private static readonly Vector3i UpperStop = new(4, 205, 2);  // the upper landing (platform at 204)
    private static readonly Vector3i BottomGate = new(4, 202, 1); // in the ring beside the bottom stop
    private static readonly Vector3i UpperGate = new(4, 205, 3);  // in the ring beside the upper stop

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_liftgate_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

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

    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player) Lift(SqliteWorldRepository repo, bool build = true)
    {
        var config = new ServerConfig { WorldName = "liftgate", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(6, 203, 2); // up in the air beside the shaft → the cells are empty + within reach
        foreach (var key in new[] { "lift_motor", "lift_stop", "lift_gate" })
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024));
        }

        p.State.SuitEnergy = 100f;
        if (build)
        {
            server.PlaceBlock("Builder", Motor.X, Motor.Y, Motor.Z, "lift_motor");
            server.PlaceBlock("Builder", BottomStop.X, BottomStop.Y, BottomStop.Z, "lift_stop");
            server.PlaceBlock("Builder", UpperStop.X, UpperStop.Y, UpperStop.Z, "lift_stop");
        }

        return (server, p);
    }

    private SqliteWorldRepository Repo() => new(new SaveGamePaths(_root, "liftgate"));

    private static void Ticks(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    private static void Press(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, Vector3i stop)
    {
        server.SetCrystalDeviceForTest(p, stop, action: 1);
        Ticks(server, 2.0);
    }

    private string KeyAt(SvGameServer server, Vector3i cell) => _content.BlockById(server.World.GetBlock(cell))?.Key ?? "air";

    private void Set(SvGameServer server, Vector3i cell, string key) => server.World.SetBlock(cell, _content.GetBlock(key)!.NumericId);

    [Fact]
    public void AGate_OpensWhenThePlatformArrives_AndClosesWhenItLeaves()
    {
        using var repo = Repo();
        var (server, p) = Lift(repo);
        server.PlaceBlock("Builder", BottomGate.X, BottomGate.Y, BottomGate.Z, "lift_gate");
        server.PlaceBlock("Builder", UpperGate.X, UpperGate.Y, UpperGate.Z, "lift_gate");
        Ticks(server, 1.0);
        Assert.Equal("lift_gate_open", KeyAt(server, BottomGate)); // placed where the platform waits: it opens
        Assert.Equal("lift_gate", KeyAt(server, UpperGate));

        server.SetCrystalDeviceForTest(p, UpperStop, action: 1);
        Assert.Equal("lift_gate", KeyAt(server, BottomGate)); // closed the moment the platform leaves
        Ticks(server, 2.0);
        Assert.Equal(204f, server.LiftPlatformYForTest(Motor));
        Assert.Equal("lift_gate_open", KeyAt(server, UpperGate));
        Assert.Equal("lift_gate", KeyAt(server, BottomGate));

        Press(server, p, BottomStop);
        Assert.Equal("lift_gate_open", KeyAt(server, BottomGate));
        Assert.Equal("lift_gate", KeyAt(server, UpperGate));
    }

    [Fact]
    public void AGate_NeverClosesOntoABody_ItWaitsUntilTheCellIsFree()
    {
        using var repo = Repo();
        var (server, p) = Lift(repo);
        server.PlaceBlock("Builder", BottomGate.X, BottomGate.Y, BottomGate.Z, "lift_gate");
        Ticks(server, 1.0);
        Assert.Equal("lift_gate_open", KeyAt(server, BottomGate));

        p.State.Position = new Vector3f(BottomGate.X + 0.5f, BottomGate.Y, BottomGate.Z + 0.5f); // standing in the open gate
        Press(server, p, UpperStop);
        Assert.Equal(204f, server.LiftPlatformYForTest(Motor));
        Assert.Equal("lift_gate_open", KeyAt(server, BottomGate)); // kid rule: not onto the player

        p.State.Position = new Vector3f(6, 203, 2); // stepped out
        Ticks(server, 0.2);
        Assert.Equal("lift_gate", KeyAt(server, BottomGate));
    }

    [Fact]
    public void AGate_BelongsToItsFloor_AStackOpensTogether_AndAGateOutsideTheRingIsLeftAlone()
    {
        using var repo = Repo();
        var (server, p) = Lift(repo);
        var stacked = UpperGate + new Vector3i(0, 1, 0);      // a second gate on top of the upper one
        var noFloor = new Vector3i(0, 208, 2);                // in the ring, but at a height without a floor
        var outside = new Vector3i(7, 205, 5);                // not beside the shaft
        var outsideOpen = new Vector3i(7, 205, 6);
        foreach (var cell in new[] { UpperGate, stacked, noFloor, outside })
        {
            server.PlaceBlock("Builder", cell.X, cell.Y, cell.Z, "lift_gate");
        }

        Set(server, outsideOpen, "lift_gate_open");
        Press(server, p, UpperStop);
        Assert.Equal("lift_gate_open", KeyAt(server, UpperGate));
        Assert.Equal("lift_gate_open", KeyAt(server, stacked)); // the stack belongs to the floor of its lowest gate
        Assert.Equal("lift_gate", KeyAt(server, noFloor));      // no floor there: it stays closed
        Assert.Equal("lift_gate", KeyAt(server, outside));      // not the lift's: never touched
        Assert.Equal("lift_gate_open", KeyAt(server, outsideOpen));

        Press(server, p, BottomStop);
        Assert.Equal("lift_gate", KeyAt(server, UpperGate));
        Assert.Equal("lift_gate", KeyAt(server, stacked));
        Assert.Equal("lift_gate_open", KeyAt(server, outsideOpen));
    }

    [Fact]
    public void AStaleGate_IsPutRight_AfterAReload_AndAMinedMotorClosesItsGates()
    {
        using (var repo = Repo())
        {
            var (server, p) = Lift(repo);
            server.PlaceBlock("Builder", BottomGate.X, BottomGate.Y, BottomGate.Z, "lift_gate");
            server.PlaceBlock("Builder", UpperGate.X, UpperGate.Y, UpperGate.Z, "lift_gate");
            Press(server, p, UpperStop);
            Assert.Equal("lift_gate_open", KeyAt(server, UpperGate));

            // The motor's row and the chunk deltas are written at different times: a save may disagree with the platform.
            Set(server, UpperGate, "lift_gate");
            Set(server, BottomGate, "lift_gate_open");
        }

        using (var repo = Repo())
        {
            var (server, p) = Lift(repo, build: false);
            _ = KeyAt(server, UpperGate) + KeyAt(server, BottomGate); // the player's chunks are streamed in (a test player streams none)
            Ticks(server, 1.0);
            Assert.Equal(204f, server.LiftPlatformYForTest(Motor));
            Assert.Equal("lift_gate_open", KeyAt(server, UpperGate)); // the platform waits here: open
            Assert.Equal("lift_gate", KeyAt(server, BottomGate));     // no platform there: closed

            p.State.Position = new Vector3f(3, 199, 5); // beside the motor
            server.MineBlock("Builder", Motor.X, Motor.Y, Motor.Z);
            Assert.Equal("air", KeyAt(server, Motor));
            Assert.Equal("lift_gate", KeyAt(server, UpperGate)); // no platform any more: the shaft is closed
        }
    }

    [Fact]
    public void TheOpenGate_IsWalkedThrough_AndMinedItGivesTheGateBack()
    {
        var open = _content.GetBlock("lift_gate_open")!;
        Assert.False(open.Solid);
        Assert.True(_content.GetBlock("lift_gate")!.Solid);
        Assert.True(Shared.Definitions.WalkThroughBlocks.IsProp(open.Key));
        Assert.Equal("lift_gate", open.Drops.Single().Item);
        Assert.Equal(Shared.Textures.TextureAlphaMode.Cutout, Shared.Textures.TextureTiles.AlphaModeOf("lift_gate"));
        Assert.Equal(Shared.Textures.TextureAlphaMode.Cutout, Shared.Textures.TextureTiles.AlphaModeOf("lift_gate_open"));
    }
}
