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
            WorldName = "spawn", Seed = 12345, StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
            PlaceSettlements = false, PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Carves a deep air shaft in the player's column and returns a position in it the server counts as
    /// "the void" (well below the terrain with nothing within reach below) — the shape of a position persisted
    /// mid-fall. The world has a bedrock floor (B46), so the void has to be made, not found.</summary>
    private static Vector3f MakeVoidBelow(SvGameServer server, Vector3f from)
    {
        int bx = (int)System.Math.Floor(from.X), bz = (int)System.Math.Floor(from.Z);
        int top = (int)from.Y;
        for (int y = top; y > top - 160; y--)
        {
            server.World.SetBlock(new Vector3i(bx, y, bz), BlockId.Air); // a deep, empty shaft (clears terrain + floor)
        }

        var voidPos = new Vector3f(from.X, top - 50, from.Z); // mid-shaft: far below the surface, no ground within reach
        if (!server.IsInVoidForTest(voidPos))
        {
            throw new Xunit.Sdk.XunitException("The carved shaft should read as the void.");
        }

        return voidPos;
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
