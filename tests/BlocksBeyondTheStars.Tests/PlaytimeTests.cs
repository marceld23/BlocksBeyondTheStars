// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Cumulative world playtime: it advances only while a player is joined (an idle server / a headless tick with
/// no players must not inflate it), accumulates whole seconds across ticks, and persists in the world metadata.
/// </summary>
public sealed class PlaytimeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlaytimeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_playtime_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo, string world)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = true,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Playtime_DoesNotAdvance_WithoutAnyJoinedPlayer()
    {
        var server = Start(out var repo, "idle");
        using (repo)
        {
            for (int i = 0; i < 20; i++)
            {
                server.TickForTest(1.0); // 20 seconds of idle ticking, no player joined
            }

            Assert.Equal(0, server.Metadata.CumulativePlaytimeSeconds);
        }
    }

    [Fact]
    public void Playtime_Accumulates_WhileAPlayerIsJoined()
    {
        var server = Start(out var repo, "active");
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            for (int i = 0; i < 12; i++)
            {
                server.TickForTest(1.0); // 12 seconds with a joined player
            }

            // Whole seconds are committed (sub-second carry may leave the last fraction pending).
            Assert.InRange(server.Metadata.CumulativePlaytimeSeconds, 11, 12);
        }
    }

    [Fact]
    public void Playtime_PersistsAcrossReload()
    {
        var server = Start(out var repo, "persist");
        long played;
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            for (int i = 0; i < 30; i++)
            {
                server.TickForTest(1.0);
            }

            played = server.Metadata.CumulativePlaytimeSeconds;
            Assert.True(played >= 29, $"expected ~30s accumulated, got {played}");
            server.Stop(); // saves metadata
        }

        // Reopen the same save: the accumulated total survives.
        var reloaded = new SqliteWorldRepository(new SaveGamePaths(_root, "persist"));
        using (reloaded)
        {
            reloaded.Initialize();
            var meta = reloaded.LoadMetadata();
            Assert.NotNull(meta);
            Assert.Equal(played, meta!.CumulativePlaytimeSeconds);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
