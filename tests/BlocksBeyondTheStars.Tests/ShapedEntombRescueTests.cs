// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3081 (Justus as Screelit, 2026-11-01, "Ich werde teleportet"): walking sideways along a wall with a fence in it, he was
/// lifted twice onto the roof of the column — "Du stecktest im Gestein — ausgegraben." The client collides with a shaped
/// block's real geometry, so pressed against a fence the capsule centre sits a few centimetres inside the fence cell; the
/// server's entombed test read only the block key, and a fence two high read as solid rock. Since then the test asks the
/// cell's form too (<see cref="BlockShapeFacts.CanEnclosePlayer"/>, the client's own rule since #2438) — a genuine tomb is
/// still dug out, and the dig-out still treats a fence as a floor and never as standing room.
/// </summary>
public sealed class ShapedEntombRescueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_shapedtomb_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best effort */ } catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private SvGameServer Started(string world, out SqliteWorldRepository repo, bool withShip = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = withShip ? 4242 : 9,
            StartPlanet = withShip ? "jungle" : "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = withShip,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A test column three blocks beside the player's spawn: stone underfoot, the two body cells in the given forms
    /// (<paramref name="feet"/> / <paramref name="head"/> as packed descriptors, -1 = plain air) and clear air above them.</summary>
    private Vector3i Column(SvGameServer server, Vector3f spawn, int feet, int head)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int x = (int)Math.Floor(spawn.X) + 3, y = (int)Math.Floor(spawn.Y), z = (int)Math.Floor(spawn.Z);
        server.World.SetBlock(new Vector3i(x, y - 1, z), stone);
        Set(new Vector3i(x, y, z), feet);
        Set(new Vector3i(x, y + 1, z), head);
        for (int dy = 2; dy <= 4; dy++)
        {
            server.World.SetBlock(new Vector3i(x, y + dy, z), BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
        }

        return new Vector3i(x, y, z);

        void Set(Vector3i cell, int descriptor)
            => server.World.SetBlock(cell, descriptor < 0 ? BlocksBeyondTheStars.Shared.Primitives.BlockId.Air : stone, shape: Math.Max(0, descriptor));
    }

    private static readonly int Fence = ShapeCode.Pack(BlockShape.Fence, 0);
    private static readonly int EdgePanel = ShapeCode.Pack(BlockShape.Panel, 0, 2); // a plate stood on its edge (up-face +X): a thin wall
    private const int Cube = 0;

    [Fact]
    public void APlayerPressedAgainstATwoHighFence_IsLeftAlone()
    {
        var server = Started("fence", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Screelit");
            p.State.AboardShip = false;
            var cell = Column(server, p.State.Position, Fence, Fence);
            var pressed = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.38f); // the rails sit at z 0.44–0.56; the capsule is just short of them
            p.State.Position = pressed;

            Assert.False(server.IsEntombedForTest(pressed), "a fence is no rock to be sealed in");
            server.RunVoidRescueForTest();
            Assert.Equal(pressed, p.State.Position);
        }
    }

    [Fact]
    public void APlayerPressedAgainstAnUprightPanel_OrAFenceUnderABlock_IsLeftAlone()
    {
        var server = Started("panel", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Screelit");
            p.State.AboardShip = false;

            var panel = Column(server, p.State.Position, EdgePanel, EdgePanel);
            var leaning = new Vector3f(panel.X + 0.2f, panel.Y, panel.Z + 0.5f);
            Assert.False(server.IsEntombedForTest(leaning));

            // A fence with a plain block on top: the feet cell is still only a fence.
            var capped = Column(server, new Vector3f(p.State.Position.X + 3f, p.State.Position.Y, p.State.Position.Z), Fence, Cube);
            var beside = new Vector3f(capped.X + 0.5f, capped.Y, capped.Z + 0.38f);
            Assert.False(server.IsEntombedForTest(beside));

            p.State.Position = beside;
            server.RunVoidRescueForTest();
            Assert.Equal(beside, p.State.Position);
        }
    }

    [Fact]
    public void ARealCubeTomb_IsStillDugOut()
    {
        var server = Started("tomb", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Screelit");
            p.State.AboardShip = false;
            var cell = Column(server, p.State.Position, Cube, Cube);
            var buried = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.State.Position = buried;

            Assert.True(server.IsEntombedForTest(buried));
            server.RunVoidRescueForTest();
            Assert.NotEqual(buried, p.State.Position);
            Assert.False(server.IsEntombedForTest(p.State.Position));
        }
    }

    /// <summary>The dig-out's other half: a fence above a tomb is something to stand ON — the rescue sets the player on top of
    /// it, never into the fence cell (a destination's body cells hold no colliding block of any form).</summary>
    [Fact]
    public void TheDigOut_StandsThePlayerOnAFence_NotInsideIt()
    {
        var server = Started("fencetop", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Screelit");
            p.State.AboardShip = false;
            var cell = Column(server, p.State.Position, Cube, Cube);
            server.World.SetBlock(new Vector3i(cell.X, cell.Y + 2, cell.Z), _content.GetBlock("stone")!.NumericId, shape: Fence);
            var buried = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            p.State.Position = buried;

            server.RunVoidRescueForTest();
            Assert.Equal(new Vector3f(cell.X + 0.5f, cell.Y + 3, cell.Z + 0.5f), p.State.Position);
        }
    }

    /// <summary>The same blind spot inside a parked hull (#1709's sealed-in-hull rescue): a self-built ship's railing or an
    /// upright partition is no wall a body is sealed in.</summary>
    [Fact]
    public void InsideAParkedHull_AFenceShapedCellIsNoTrap()
    {
        var server = Started("hullfence", out var repo, withShip: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Screelit");
            var spot = server.SealedHullSpotForTest(p.State.PlayerId);
            Assert.NotNull(spot);
            Assert.True(server.SealedInHullForTest(spot!.Value));

            server.ShapeLandedHullCellForTest(p.State.PlayerId, spot.Value, Fence);
            server.ShapeLandedHullCellForTest(p.State.PlayerId, new Vector3f(spot.Value.X, spot.Value.Y + 1f, spot.Value.Z), Fence);
            Assert.False(server.SealedInHullForTest(spot.Value));
        }
    }
}
