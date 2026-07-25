// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// The deferred respawn choice (issue #462): with a home spawn set (issue #461), dying does NOT relocate
/// immediately — the player lies at the death spot at 0 HP until they pick ship vs home (or the ~30 s
/// timeout picks the ship for them). Without a home spawn the classic instant respawn is unchanged.
/// </summary>
public sealed class RespawnChoiceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public RespawnChoiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_respchoice_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "respchoice"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "respchoice", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Places a tank, sets the home spawn there and returns the home position.</summary>
    private Vector3f SetHome(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p)
    {
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 64, 0.5f);
        server.World.SetBlock(new Vector3i(1, 64, 0), _content.GetBlock("heal_tank")!.NumericId);
        server.SetSpawnPoint(p.State.PlayerId, 1, 64, 0);
        Assert.False(string.IsNullOrEmpty(p.State.CustomSpawnBodyId)); // sanity: the home is armed
        return p.State.CustomSpawnPoint;
    }

    [Fact]
    public void Death_WithHomeSpawn_DefersTheRespawn()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Chooser");
            SetHome(server, p);

            var deathSpot = new Vector3f(30, 64, 30);
            p.State.Position = deathSpot;
            p.State.Health = 0f;
            server.TickForTest(0.1);

            // Not relocated, not revived — the choice is pending; further ticks change nothing.
            Assert.Equal(0f, p.State.Health);
            Assert.Equal(deathSpot, p.State.Position);
            server.TickForTest(0.1);
            Assert.Equal(deathSpot, p.State.Position);
        }
    }

    [Fact]
    public void RespawnChoice_Home_WakesAtTheHomeSpawn()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Homer");
            var home = SetHome(server, p);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: true);

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(home, p.State.Position);
            Assert.False(p.State.AboardShip); // waking at the base, on foot — not in the ship's life support
        }
    }

    [Fact]
    public void RespawnChoice_Ship_WakesAtTheClassicRespawnPoint()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sailor");
            SetHome(server, p);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: false);

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
            Assert.True(p.State.AboardShip);
        }
    }

    [Fact]
    public void RespawnChoice_TimesOut_ToTheShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Sleeper");
            SetHome(server, p);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            Assert.Equal(0f, p.State.Health); // pending

            // Uptime advances at the tick tail, so the deadline check sees it on the FOLLOWING tick.
            server.TickForTest(31.0); // > RespawnChoiceTimeout
            server.TickForTest(0.1);  // → the safe default kicks in

            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
        }
    }

    [Fact]
    public void RespawnChoice_HomeGone_FallsBackToTheShip()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Refugee");
            SetHome(server, p);

            // The home tank is mined/destroyed after the spawn was set.
            server.World.SetBlock(new Vector3i(1, 64, 0), BlockId.Air);

            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);
            server.ChooseRespawn(p.State.PlayerId, useCustomSpawn: true);

            // Home refused (no tank) → ship fallback, never a dead end.
            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
            Assert.True(p.State.AboardShip);
        }
    }

    [Fact]
    public void Death_WithoutHomeSpawn_StaysInstant()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Classic");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(30, 64, 30);
            p.State.Health = 0f;
            server.TickForTest(0.1);

            // No home spawn set → the classic immediate respawn (no pending state).
            Assert.Equal(100f, p.State.Health);
            Assert.Equal(p.State.RespawnPoint, p.State.Position);
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
