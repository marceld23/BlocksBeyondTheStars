// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The custom spawn point (issue #461): E on a placed heal tank stores a body-qualified home spawn on
/// PlayerState — separate from RespawnPoint (the ship heal-tank cache, rewritten on every transit).
/// Phase 2 only STORES the point; the death flow consuming it is issue #462.
/// </summary>
public sealed class SpawnPointTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpawnPointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_spawnpt_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "spawnpt"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "spawnpt", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void SetSpawnPoint_StoresBodyQualifiedPoint_AtTheTank()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Homer");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            server.World.SetBlock(new Vector3i(2, 64, 0), _content.GetBlock("heal_tank")!.NumericId);

            server.SetSpawnPoint(p.State.PlayerId, 2, 64, 0);

            Assert.Equal(p.CurrentLocationId, p.State.CustomSpawnBodyId);
            Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId));
            Assert.Equal(p.State.Position, p.State.CustomSpawnPoint); // the standing spot, not the tank cell
        }
    }

    [Fact]
    public void SetSpawnPoint_Rejected_OutOfReach_OrWrongBlock()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Doubter");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);

            // A tank far outside interact reach → nothing stored.
            server.World.SetBlock(new Vector3i(30, 64, 0), _content.GetBlock("heal_tank")!.NumericId);
            server.SetSpawnPoint(p.State.PlayerId, 30, 64, 0);
            Assert.Equal(string.Empty, p.State.CustomSpawnBodyId);

            // A nearby cell that is NOT a heal tank → nothing stored either.
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("stone")!.NumericId);
            server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
            Assert.Equal(string.Empty, p.State.CustomSpawnBodyId);
        }
    }

    [Fact]
    public void CustomSpawn_SurvivesSnapshotRoundTrip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sleeper");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0.5f, 64, 0.5f);
            server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("heal_tank")!.NumericId);
            server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);

            var restored = StateMapper.FromSnapshot(StateMapper.ToSnapshot(p.State));

            Assert.Equal(p.State.CustomSpawnBodyId, restored.CustomSpawnBodyId);
            Assert.Equal(p.State.CustomSpawnPoint, restored.CustomSpawnPoint);
            Assert.Equal(p.State.CustomSpawnLabel, restored.CustomSpawnLabel);

            // Pre-feature snapshots (no custom-spawn fields) load as "none set".
            var legacy = StateMapper.FromSnapshot(new PlayerSnapshot { Id = "x", Name = "Legacy" });
            Assert.Equal(string.Empty, legacy.CustomSpawnBodyId);
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
