using System;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Spawn safety: the world has no bedrock floor, so a player below the terrain with nothing under them
/// would fall forever — and their position is persisted + restored verbatim, so one fall can poison a save.
/// Two guards close that loop: a join-time check that snaps a void position back to safe ground, and a
/// runtime rescue that recovers anyone caught plummeting before the fall can be saved.
/// </summary>
public sealed class SpawnSafetyTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpawnSafetyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_spawn_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Finds a Y in the player's own column that the server considers "the void" (well below the
    /// terrain with nothing to stand on) — the shape of a position persisted mid-fall.</summary>
    private static Vector3f FindVoidBelow(SvGameServer server, Vector3f from)
    {
        for (int y = (int)from.Y - 20; y > -5000; y -= 24)
        {
            var probe = new Vector3f(from.X, y, from.Z);
            if (server.IsInVoidForTest(probe))
            {
                return probe;
            }
        }

        throw new Xunit.Sdk.XunitException("No void Y found below the surface — the world should have a bottom.");
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
            var voidPos = FindVoidBelow(server, spawn);
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
            var voidPos = FindVoidBelow(server, spawn);
            p.State.Position = voidPos;
            p.State.RespawnPoint = voidPos;

            // The join-time guard (run on every join) must snap them back to safe ground.
            server.EnsureSafeSpawnForTest(p);

            Assert.False(server.IsInVoidForTest(p.State.Position));
            Assert.False(server.IsInVoidForTest(p.State.RespawnPoint)); // a poisoned respawn point is fixed too
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
