// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Spawn safety: a player below the terrain with nothing under them (a deep cave/shaft) would fall, and their
/// position is persisted + restored verbatim, so one fall can poison a save. Two guards close that loop: a
/// join-time check that snaps a void position back to safe ground, and a runtime rescue that recovers anyone
/// caught plummeting. (Since B46 the world also has a bedrock floor, so these tests carve an artificial shaft
/// to reproduce a fall-into-void.)
/// </summary>
public sealed class SpawnSafetyTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpawnSafetyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_spawn_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "spawn"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "spawn",
            Seed = 12345,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Carves a hole through the bedrock floor under the player's column and returns a position below that
    /// floor with nothing within reach beneath it — the only shape the server still counts as "the void" (#1788:
    /// above the floor every fall ends on something, so a deep dig is no longer rescued). The world has a bedrock
    /// floor (B46), so the void has to be made, not found.</summary>
    private static Vector3f MakeVoidBelow(SvGameServer server, Vector3f from)
    {
        int bx = (int)System.Math.Floor(from.X), bz = (int)System.Math.Floor(from.Z);
        int floorY = (int)from.Y - server.FloorDepthForTest; // the spawn sits a couple of blocks over the surface
        for (int y = floorY + 8; y > floorY - 70; y--)
        {
            server.World.SetBlock(new Vector3i(bx, y, bz), BlockId.Air); // through the lava band and the bedrock, into nothing
        }

        var voidPos = new Vector3f(from.X, floorY - 35, from.Z); // under the floor, no ground within reach
        if (!server.IsInVoidForTest(voidPos))
        {
            throw new Xunit.Sdk.XunitException("A position below the carved-open bedrock floor should read as the void.");
        }

        return voidPos;
    }

    [Fact]
    public void RuntimeRescue_LeavesAPlayerFallingDownTheirOwnShaftAlone()
    {
        // #1788: a player dug a 1-wide shaft 50+ blocks deep and fell into it; the 24-block ground probe read the
        // fall as "the void" and teleported them to the ship's heal tank once a second ("Ich werde im End Level
        // immer wieder zum Schiff tp"). Above the bedrock floor there is always ground to land on — leave them be.
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Digger");
            var spawn = p.State.Position;
            int bx = (int)System.Math.Floor(spawn.X), bz = (int)System.Math.Floor(spawn.Z);
            int top = (int)spawn.Y;
            for (int y = top; y > top - 160; y--)
            {
                server.World.SetBlock(new Vector3i(bx, y, bz), BlockId.Air); // a deep shaft, far past the old 24-block probe
            }

            var falling = new Vector3f(bx + 0.5f, top - 50, bz + 0.5f);
            p.State.Position = falling;
            Assert.False(server.IsInVoidForTest(falling), "a shaft above the bedrock floor is not the void");

            server.RunVoidRescueForTest();

            Assert.Equal(falling.X, p.State.Position.X);
            Assert.Equal(falling.Y, p.State.Position.Y);
            Assert.Equal(falling.Z, p.State.Position.Z);
        }
    }

    [Fact]
    public void RuntimeRescue_PullsAPlummetingPlayer_BackToSafeGround()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Faller");
            var spawn = p.State.Position;
            Assert.False(server.IsInVoidForTest(spawn)); // a fresh spawn is safe

            // Drop them into the void, as a runaway fall would.
            var voidPos = MakeVoidBelow(server, spawn);
            p.State.Position = voidPos;
            Assert.True(server.IsInVoidForTest(p.State.Position));

            server.RunVoidRescueForTest();

            // Recovered onto solid ground, lifted back up out of the void.
            Assert.False(server.IsInVoidForTest(p.State.Position));
            Assert.True(p.State.Position.Y > voidPos.Y);
        }
    }

    [Fact]
    public void JoinGuard_HealsAPositionPersistedMidFall()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ghost");
            var spawn = p.State.Position;

            // Simulate a poisoned save: their stored position is deep in the void.
            var voidPos = MakeVoidBelow(server, spawn);
            p.State.Position = voidPos;
            p.State.RespawnPoint = voidPos;

            // The join-time guard (run on every join) must snap them back to safe ground.
            server.EnsureSafeSpawnForTest(p);

            Assert.False(server.IsInVoidForTest(p.State.Position));
            Assert.False(server.IsInVoidForTest(p.State.RespawnPoint)); // a poisoned respawn point is fixed too
        }
    }

    [Fact]
    public void JoinGuard_HealsAPositionPersistedHighAboveTheSurface()
    {
        // A save written during a space / EVA session can hold a position far above the planet surface;
        // restoring it dropped the player out of the sky onto an empty world. The join guard snaps it down.
        var server = Start(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Astronaut");
            var spawn = p.State.Position;
            var highPos = new Vector3f(spawn.X, spawn.Y + 4000f, spawn.Z);
            p.State.Position = highPos;

            server.EnsureSafeSpawnForTest(p);

            Assert.True(p.State.Position.Y < highPos.Y - 1000f, "a wildly high spawn must be pulled back down");
            Assert.False(server.IsInVoidForTest(p.State.Position));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
