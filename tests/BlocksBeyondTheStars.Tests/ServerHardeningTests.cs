// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Covers the server-hardening fixes: the block-edit height band (K3), the persistence
/// transaction batching (K4) and the explicit-save cooldown (#3049). The malformed-packet guard (K1) is covered in
/// <see cref="NetworkingTests"/>.</summary>
public sealed class ServerHardeningTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ServerHardeningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_harden_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "harden"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "harden", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Mining_FarOutsideBuildHeight_LoadsNoChunk()
    {
        // A spoofed-position mine spam at ever-rising Y would otherwise generate + cache a chunk per cell
        // (unbounded RAM/disk DoS). The height guard drops it before any world access — no chunk is loaded.
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Miner");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);

            int before = server.World.LoadedChunkCount;
            server.MineBlockOnce("Miner", 0, 200_000, 0);   // far above the build ceiling
            server.MineBlockOnce("Miner", 0, -50_000, 0);   // far below the build floor

            Assert.Equal(before, server.World.LoadedChunkCount);
        }
    }

    [Fact]
    public void Placing_FarOutsideBuildHeight_IsRejected_AndLoadsNoChunk()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.Position = new Vector3f(0.5f, 66f, 0.5f);
            p.State.Inventory.Add("iron_wall", 4, 99);

            int before = server.World.LoadedChunkCount;
            server.PlaceBlock("Builder", 0, 300_000, 0, "iron_wall"); // way above the ceiling

            Assert.Equal(before, server.World.LoadedChunkCount);
        }
    }

    [Fact]
    public void RunInTransaction_Commits_AllWrites()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tx_commit"));
        repo.Initialize();

        repo.RunInTransaction(() =>
        {
            repo.SetBlock("planetX", new Vector3i(1, 2, 3), 7);
            repo.SetBlock("planetX", new Vector3i(2, 2, 3), 9);
        });

        var edits = repo.LoadChunkEdits("planetX", WorldConstants.WorldToChunk(new Vector3i(1, 2, 3)));
        Assert.Equal(2, edits.Count);
    }

    [Fact]
    public void RunInTransaction_RollsBack_WhenBodyThrows()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tx_rollback"));
        repo.Initialize();

        Assert.Throws<InvalidOperationException>(() =>
            repo.RunInTransaction(() =>
            {
                repo.SetBlock("planetY", new Vector3i(1, 2, 3), 7);
                throw new InvalidOperationException("boom");
            }));

        var edits = repo.LoadChunkEdits("planetY", WorldConstants.WorldToChunk(new Vector3i(1, 2, 3)));
        Assert.Empty(edits); // the partial write was rolled back
    }

    [Fact]
    public void ExplicitSave_Spam_SavesOncePerCooldown()
    {
        // #3049: "Save game" runs a full SaveAll on the tick thread and any client may send it — a spamming client
        // used to stall the server once per message. A save a moment ago already holds everyone's state.
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "save_spam"));
        var log = new InfoLog();
        var config = new ServerConfig { WorldName = "save_spam", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new RecordingTransport(), repo, log);
        server.Start();
        var spammer = server.AddLocalPlayer("Spammer");

        for (int i = 0; i < 50; i++)
        {
            server.HandlePayloadForTest(spammer.ConnectionId, NetCodec.Encode(new SaveGameIntent()));
        }

        Assert.Equal(1, log.ExplicitSaves); // the first press still saves at once

        server.AdvanceUptimeForTest(5.5);
        server.HandlePayloadForTest(spammer.ConnectionId, NetCodec.Encode(new SaveGameIntent()));
        Assert.Equal(2, log.ExplicitSaves); // and the next one after the cooldown
    }

    /// <summary>Counts the server's "explicit save" log lines.</summary>
    private sealed class InfoLog : IGameLogger
    {
        public int ExplicitSaves { get; private set; }

        public void Info(string message)
        {
            if (message.StartsWith("Explicit save requested", StringComparison.Ordinal))
            {
                ExplicitSaves++;
            }
        }

        public void Warn(string message) { }

        public void Error(string message) { }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
