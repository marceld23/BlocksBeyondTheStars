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

/// <summary>Covers the chunk-streaming budget (A2) and the far-chunk eviction sweep (A4): the per-tick stream
/// budget is honoured (so a wider view fills proportionally faster), and chunks that drift outside every
/// player's keep-range are dropped from the cache while the player's own region stays resident.</summary>
public sealed class ChunkStreamingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ChunkStreamingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sweep_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private int ChunksLoadedAfterOneTick(string name, int budget)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 4,
            ChunkStreamPerTick = budget,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Streamer");
        int before = server.World.LoadedChunkCount;
        server.TickForTest(0.1); // exactly one streaming pass
        int delta = server.World.LoadedChunkCount - before;
        repo.Dispose();
        return delta;
    }

    [Fact]
    public void StreamBudget_ControlsHowFastTheViewFills()
    {
        // A bigger per-tick budget sends (and so caches) more new chunks in a single streaming pass — that is the
        // knob that keeps the wider default view from thawing in slowly at the horizon.
        int small = ChunksLoadedAfterOneTick("budget_small", 4);
        int large = ChunksLoadedAfterOneTick("budget_large", 20);

        Assert.True(small <= 4, $"one tick must not stream more than the budget (got {small} for budget 4)");
        Assert.True(large > small, $"a larger budget should fill faster (large={large}, small={small})");
    }

    [Fact]
    public void Sweep_EvictsFarChunks_ButKeepsThePlayersOwnRegion()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sweep"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "sweep",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 2,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        // Use the player's natural safe spawn (set by the join-time spawn guard) so the runtime void-rescue never
        // relocates them mid-test — that would move the anchor and invalidate the assertions.
        var p = server.AddLocalPlayer("Wanderer");
        var nearChunk = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        // Stream the player's own region in (12 chunks/tick).
        for (int i = 0; i < 20; i++)
        {
            server.TickForTest(0.1);
        }

        Assert.True(server.World.IsChunkLoaded(nearChunk), "the player's own chunk should be resident");

        // Force a chunk far away into the cache (e.g. a query from another subsystem) — it sits well outside the
        // player's keep-range and is exactly what the sweep is meant to reclaim. Offset from the actual anchor so
        // it's far regardless of where the player spawned.
        var farChunk = new ChunkCoord(nearChunk.X, nearChunk.Y, nearChunk.Z + 400); // ~6400 blocks away in Z
        server.World.GetOrLoadChunk(farChunk);
        Assert.True(server.World.IsChunkLoaded(farChunk), "the far chunk should be cached right after loading it");

        // Tick past the sweep interval (player stays put, so the near region is the anchor).
        for (int i = 0; i < 15; i++)
        {
            server.TickForTest(1.0);
        }

        Assert.False(server.World.IsChunkLoaded(farChunk), "the far chunk should have been swept out of the cache");
        Assert.True(server.World.IsChunkLoaded(nearChunk), "the player's own region must stay resident through the sweep");
    }

    [Fact]
    public void ClientViewDistance_ExtendsStreamingRadius_BeyondHostDefault()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vd"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "vd",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1, // small host default — the client asks for more
            ChunkStreamPerTick = 16,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        var p = server.AddLocalPlayer("FarSighted");
        p.ViewDistance = 3; // the client's slider, larger than the host's radius-1 default
        var center = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        for (int i = 0; i < 30; i++)
        {
            server.TickForTest(0.1); // fill the view; short dt so the 10 s sweep never trips
        }

        // A chunk 2 east is inside the client's radius-3 view but outside the host's radius-1 default — it is
        // resident only because the client's requested view distance drove the streaming radius.
        var withinClientView = new ChunkCoord(center.X + 2, center.Y, center.Z);
        // A chunk 5 east is beyond even the client's radius — it must never have been streamed.
        var beyondClientView = new ChunkCoord(center.X + 5, center.Y, center.Z);

        Assert.True(server.World.IsChunkLoaded(withinClientView), "client's wider view distance should stream terrain past the host default");
        Assert.False(server.World.IsChunkLoaded(beyondClientView), "nothing beyond the client's requested radius should stream");
    }

    [Fact]
    public void FarColumns_StreamOnlyTheSurfaceBand_WhileNearColumnsStreamTheFullVerticalSpan()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "lod"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "lod",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 5,   // radius 5 > the near-full-column radius (3), so there are "far" columns
            ChunkStreamPerTick = 64,  // drain the whole view quickly
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        var p = server.AddLocalPlayer("Surveyor");
        var center = WorldConstants.WorldToChunk(p.State.Position.ToBlock());

        for (int i = 0; i < 30; i++)
        {
            server.TickForTest(0.1); // short dt so the 10 s far-chunk sweep never trips
        }

        // The player's own column (dx=0) keeps the full 6-layer vertical span (-3..+2) for caves/digging.
        int nearLayers = 0;
        for (int cy = center.Y - 3; cy <= center.Y + 2; cy++)
        {
            if (server.World.IsChunkLoaded(new ChunkCoord(center.X, cy, center.Z))) nearLayers++;
        }

        // A far column (dx=5, Chebyshev 5 > 3) streams only the band around its surface — count over a wide
        // vertical window so we catch the band wherever the terrain there sits.
        int farLayers = 0;
        for (int cy = center.Y - 8; cy <= center.Y + 8; cy++)
        {
            if (server.World.IsChunkLoaded(new ChunkCoord(center.X + 5, cy, center.Z))) farLayers++;
        }

        Assert.Equal(6, nearLayers); // near column: full vertical span
        Assert.InRange(farLayers, 1, 3); // far column: just the surface band (below+surface+above)
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
