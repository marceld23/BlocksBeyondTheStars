// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "Im Einzelspieler sollte das Spiel pausiert werden, wenn man in das Menü geht." The Esc dialog was already
/// titled "Pause" with a "Resume" button while the world kept simulating behind it. The hold is server-side
/// (singleplayer runs the bundled server in its own process) and must never let one player freeze a world for
/// anyone else.
/// </summary>
public sealed class SingleplayerPauseTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SingleplayerPauseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pause_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pause"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "pause",
            Seed = 3,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void ALonePlayer_CanHoldAndReleaseTheWorld()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");

            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            server.PauseForTest(p, false);
            Assert.False(server.IsPaused);
        }
    }

    [Fact]
    public void WhileHeld_TheClockDoesNotAdvance()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            server.TickForTest(0.1);
            long before = server.Metadata.CumulativePlaytimeSeconds;

            server.PauseForTest(p, true);
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.5); // 20 seconds of wall clock
            }

            // Playtime is accumulated by a simulation system that must not run while the world is held.
            Assert.Equal(before, server.Metadata.CumulativePlaytimeSeconds);
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void AfterResuming_TheWorldSimulatesAgain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            server.PauseForTest(p, true);
            server.TickForTest(1.0);
            server.PauseForTest(p, false);

            long before = server.Metadata.CumulativePlaytimeSeconds;
            for (int i = 0; i < 6; i++)
            {
                server.TickForTest(0.5);
            }

            Assert.True(server.Metadata.CumulativePlaytimeSeconds > before);
        }
    }

    [Fact]
    public void ASecondPlayer_IsNeverFrozenByTheFirstOnesMenu()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            server.AddLocalPlayer("Severin");

            server.PauseForTest(a, true);

            // Two players joined — the request must be declined outright.
            Assert.False(server.IsPaused);
        }
    }

    [Fact]
    public void AWatchingAdmin_DoesNotCostThePlayerTheirPause()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var admin = server.AddLocalPlayer("Marcel");
            admin.Spectating = true; // observer mode (#487): invisible, no footprint, ignored by creatures

            // Nobody's game is interrupted by holding the world for its only actual player (#908). Before this,
            // an admin quietly watching a world denied that player their pause — and lifted one already running.
            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            server.TickForTest(0.1);
            Assert.True(server.IsPaused);
        }
    }

    [Fact]
    public void APauseIsLifted_WhenSomeoneElseJoins()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            server.PauseForTest(a, true);
            Assert.True(server.IsPaused);

            server.AddLocalPlayer("Severin");
            server.TickForTest(0.1);

            Assert.False(server.IsPaused); // the newcomer wins over the other player's menu
        }
    }

    [Fact]
    public void APauseIsLifted_WhenTheHolderLeaves()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Justus");
            server.PauseForTest(a, true);
            Assert.True(server.IsPaused);

            server.DisconnectLocalPlayerForTest("Justus");
            server.TickForTest(0.1);

            // Nobody left to hold it for — a dedicated world must not sit frozen because someone quit
            // with the menu open.
            Assert.False(server.IsPaused);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked save file must never fail the test run.
        }
    }
}
