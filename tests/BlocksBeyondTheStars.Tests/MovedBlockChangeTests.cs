// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3173 (Lyxette, v2026.11.4): a retracting drawbridge crumbled like mined rock. Every block a machine or the weather moves
/// now goes out as <see cref="BlockChanged.Moved"/> — the bridge deck out and in, a piston's push and sticky pull, a thawing
/// snow drift and one the lift clears from its shaft — so the client skips the mining crumble and sound; a block a player
/// mines by hand (a bridge deck too) still goes out as an ordinary edit. The broadcasts travel through the real codec
/// (<see cref="RecordingTransport"/> decodes every send), and the flag survives both wire paths.
/// </summary>
public sealed class MovedBlockChangeTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_moved_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A server on the recording transport and a builder up in the air (the cells around y 200 are empty and in reach).</summary>
    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player) Builder(Vector3f at, params string[] items)
    {
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, "moved"));
        var config = new ServerConfig { WorldName = "moved", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, _transport, _repo);
        server.Start();
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = at;
        foreach (var key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024));
        }

        p.State.SuitEnergy = 100f;
        return (server, p);
    }

    private static void Ticks(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    /// <summary>The block changes the builder was sent for one cell, in order.</summary>
    private List<BlockChanged> ChangesAt(BlocksBeyondTheStars.GameServer.PlayerSession p, int x, int y, int z)
        => _transport.To<BlockChanged>(p).Where(m => m.X == x && m.Y == y && m.Z == z).ToList();

    private ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;

    [Fact]
    public void ABridgeDeck_GoesOutAndIn_AsMovedBlocks_ButADeckMinedByHandIsAnOrdinaryEdit()
    {
        var (server, p) = Builder(new Vector3f(0, 203, 0), "bridge_motor", "crystal_switch", "stone");
        server.PlaceBlock("Builder", 0, 200, 0, "crystal_switch");
        server.PlaceBlock("Builder", 1, 200, 0, "bridge_motor", deviceDir: 1); // points +X
        server.PlaceBlock("Builder", 5, 200, 0, "stone");                    // three deck blocks fit before it
        Assert.All(new[] { 0, 1, 5 }, x => Assert.False(Assert.Single(ChangesAt(p, x, 200, 0)).Moved)); // placed by hand

        server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0); // ON: the deck goes out
        Ticks(server, 2.5);
        foreach (int x in new[] { 2, 3, 4 })
        {
            var deck = Assert.Single(ChangesAt(p, x, 200, 0));
            Assert.Equal(Id("bridge_deck"), deck.Block);
            Assert.True(deck.Moved, $"the deck block at x {x} went out as a mined/placed edit");
        }

        server.MineBlock("Builder", 4, 200, 0); // the player breaks the outermost deck block by hand: that still crumbles
        var mined = ChangesAt(p, 4, 200, 0).Last();
        Assert.Equal(BlockId.AirValue, mined.Block);
        Assert.False(mined.Moved);

        _transport.Sent.Clear();
        server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0); // OFF: the motor pulls the rest back in
        Ticks(server, 2.0);
        foreach (int x in new[] { 2, 3 })
        {
            var gone = Assert.Single(ChangesAt(p, x, 200, 0));
            Assert.Equal(BlockId.AirValue, gone.Block);
            Assert.True(gone.Moved, $"the deck block at x {x} went in as a mined edit");
        }

        Assert.Empty(ChangesAt(p, 4, 200, 0)); // already gone: nothing to pull in there
    }

    [Fact]
    public void APistonsPush_AndAStickyPull_AreMovedBlocks()
    {
        var (server, p) = Builder(new Vector3f(0, 203, 0), "piston", "crystal_switch", "stone");
        server.PlaceBlock("Builder", 0, 200, 0, "crystal_switch");
        server.PlaceBlock("Builder", 1, 200, 0, "piston", deviceDir: 1);
        server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, mode: (int)PistonMode.Sticky);
        server.PlaceBlock("Builder", 2, 200, 0, "stone");

        _transport.Sent.Clear();
        server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0); // ON: push
        Ticks(server, 0.8);
        Assert.Equal(Id("piston_head"), Assert.Single(ChangesAt(p, 2, 200, 0)).Block);
        Assert.Equal(Id("stone"), Assert.Single(ChangesAt(p, 3, 200, 0)).Block);
        Assert.All(_transport.To<BlockChanged>(p), m => Assert.True(m.Moved, $"({m.X},{m.Y},{m.Z}) went out as a hand edit"));

        _transport.Sent.Clear();
        server.SetCrystalDeviceForTest(p, new Vector3i(0, 200, 0), action: 0); // OFF: the sticky head pulls the stone back
        Ticks(server, 0.8);
        Assert.Equal(Id("stone"), Assert.Single(ChangesAt(p, 2, 200, 0)).Block);
        Assert.Equal(BlockId.AirValue, Assert.Single(ChangesAt(p, 3, 200, 0)).Block);
        Assert.All(_transport.To<BlockChanged>(p), m => Assert.True(m.Moved, $"({m.X},{m.Y},{m.Z}) went out as a hand edit"));
    }

    [Fact]
    public void AThawingDrift_IsAMovedBlock()
    {
        var (server, p) = Builder(new Vector3f(0, 203, 0), "stone");
        server.PlaceBlock("Builder", 3, 200, 0, "stone"); // something for the drift to lie on
        server.DepositWeatherSnowForTest(3, 201, 0);
        Assert.Equal(Id("snow"), ChangesAt(p, 3, 201, 0).Last().Block);

        _transport.Sent.Clear();
        server.MeltWeatherSnowForTest();
        var thaw = Assert.Single(ChangesAt(p, 3, 201, 0));
        Assert.Equal(BlockId.AirValue, thaw.Block);
        Assert.True(thaw.Moved);
    }

    [Fact]
    public void ADriftTheLiftClearsFromItsShaft_IsAMovedBlock()
    {
        // The lift of LiftShaftTests: a motor, a pit floor beside it, a stop up and one on the bottom floor.
        var motor = new Vector3i(2, 200, 2);
        var (server, p) = Builder(new Vector3f(6, 203, 2), "lift_motor", "lift_stop", "stone");
        server.PlaceBlock("Builder", motor.X, motor.Y, motor.Z, "lift_motor");
        server.PlaceBlock("Builder", motor.X + 1, motor.Y, motor.Z, "stone");
        server.PlaceBlock("Builder", 4, 205, 2, "lift_stop");
        server.PlaceBlock("Builder", 4, 202, 2, "lift_stop");
        var drift = new Vector3i(motor.X + 1, motor.Y + 1, motor.Z);
        server.DepositWeatherSnowForTest(drift.X, drift.Y, drift.Z); // a drift in the platform's layer

        _transport.Sent.Clear();
        server.SetCrystalDeviceForTest(p, new Vector3i(4, 205, 2), action: 1); // call the platform up: it clears the drift
        Ticks(server, 2.0);
        var cleared = Assert.Single(ChangesAt(p, drift.X, drift.Y, drift.Z));
        Assert.Equal(BlockId.AirValue, cleared.Block);
        Assert.True(cleared.Moved);
    }

    [Fact]
    public void TheMovedFlag_SurvivesBothWirePaths_AndAnOlderPeersChangeReadsAsAHandEdit()
    {
        var moved = new BlockChanged { X = 4, Y = 200, Z = -3, Block = 7, Moved = true };
        bool before = NetCodec.UseJsonEncoding;
        byte[] native;
        NetCodec.UseJsonEncoding = false;
        try
        {
            native = NetCodec.Encode(moved);
        }
        finally
        {
            NetCodec.UseJsonEncoding = before;
        }

        foreach (var payload in new[] { native, NetCodec.EncodeJson(moved) })
        {
            var back = Assert.IsType<BlockChanged>(NetCodec.Decode(payload));
            Assert.True(back.Moved);
            Assert.Equal((4, 200, -3, (ushort)7), (back.X, back.Y, back.Z, back.Block));
        }

        Assert.False(new BlockChanged().Moved);

        // An older server's change carries no "Moved" at all: it reads as an ordinary edit.
        var old = Assert.IsType<BlockChanged>(NetCodec.Decode(JsonFrame("{\"tag\":53,\"body\":{\"X\":1,\"Y\":2,\"Z\":3,\"Block\":3}}")));
        Assert.Equal((1, 2, 3, (ushort)3), (old.X, old.Y, old.Z, old.Block));
        Assert.False(old.Moved);
    }

    private static byte[] JsonFrame(string json)
    {
        var body = System.Text.Encoding.UTF8.GetBytes(json);
        var payload = new byte[body.Length + 1];
        payload[0] = 255; // the JSON envelope tag
        Buffer.BlockCopy(body, 0, payload, 1, body.Length);
        return payload;
    }
}
