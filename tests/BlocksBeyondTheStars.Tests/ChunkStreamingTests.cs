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
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Covers the chunk-streaming budget (A2), the far-chunk eviction sweep (A4) and the distance-based
/// vertical LOD: the per-tick stream budget is honoured (so a wider view fills proportionally faster), chunks
/// that drift outside every player's keep-range are dropped from the cache while the player's own region stays
/// resident, and a far column streams the band around its VISIBLE top — the waterline where it is flooded, so a
/// deep ocean is no longer cut off mid-water (#987).</summary>
public sealed class ChunkStreamingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ChunkStreamingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_sweep_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private int ChunksLoadedAfterOneTick(string name, int budget, double timeBudgetMs = 0)
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
            ChunkStreamBudgetMs = timeBudgetMs,
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
    public void StreamTimeBudget_CutsAStreamingPassShort_ButAlwaysMakesProgress()
    {
        // The wall-clock budget exists for hosts whose tick shares the render thread (in-browser
        // singleplayer): a burst of synchronous first-visit generations must not stall the frame. A
        // near-zero budget is spent after the first send, so exactly one guaranteed chunk goes out —
        // while the same count budget without a time budget streams the full per-tick allowance.
        int unbudgeted = ChunksLoadedAfterOneTick("timebudget_off", 16);
        int budgeted = ChunksLoadedAfterOneTick("timebudget_on", 16, timeBudgetMs: 0.000001);

        Assert.True(budgeted >= 1, "a spent time budget must still stream at least one chunk (no starvation)");
        Assert.True(budgeted < unbudgeted, $"the time budget should cut the pass short (budgeted={budgeted}, unbudgeted={unbudgeted})");
    }

    [Fact]
    [Trait("Category", "Slow")]
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
        // player's keep-range and is exactly what the sweep is meant to reclaim. Canonicalize it the way the
        // streamer/cache do (the world is a torus on BOTH axes), so the coord matches the key the sweep evicts —
        // SentChunks only ever holds canonical coords in production.
        var farChunk = WorldConstants.CanonicalChunk(
            new ChunkCoord(nearChunk.X, nearChunk.Y, nearChunk.Z + 40), server.World.Circumference);
        server.World.GetOrLoadChunk(farChunk);
        p.SentChunks.Add(farChunk); // pretend it was streamed to the player, so we can assert it gets forgotten
        Assert.True(server.World.IsChunkLoaded(farChunk), "the far chunk should be cached right after loading it");

        // Tick past the sweep interval (player stays put, so the near region is the anchor).
        for (int i = 0; i < 15; i++)
        {
            server.TickForTest(1.0);
        }

        Assert.False(server.World.IsChunkLoaded(farChunk), "the far chunk should have been swept out of the cache");
        Assert.DoesNotContain(farChunk, p.SentChunks); // forgotten too → it re-streams fresh if the player returns
        Assert.True(server.World.IsChunkLoaded(nearChunk), "the player's own region must stay resident through the sweep");
    }

    /// <summary>The multiplayer half of the sweep (#1030): the cache eviction only forgets chunks that are far
    /// from EVERY player, so chunks a camping partner kept alive stayed in a departed player's sent-set even
    /// though that player's client had long unloaded them (RepositionChunks drops everything past ~384 blocks).
    /// On return — /tpp, a beam, or simply walking back — StreamChunks skipped them as "already sent" and the
    /// returner stood in void terrain the server actually had ("I only see space"). The sweep must therefore
    /// also prune every session's sent-set by that session's OWN distance.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Sweep_ForgetsADepartedPlayersSentChunks_EvenWhenAPartnerKeepsThemCached()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sweep_mp"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "sweep_mp",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 2,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        var camper = server.AddLocalPlayer("Camper");
        var traveler = server.AddLocalPlayer("Traveler");
        traveler.State.Position = camper.State.Position; // side by side, sharing one home region
        var home = WorldConstants.WorldToChunk(camper.State.Position.ToBlock());

        for (int i = 0; i < 20; i++)
        {
            server.TickForTest(0.1); // stream the shared home region to BOTH sessions
        }

        Assert.Contains(home, traveler.SentChunks); // sanity: the home chunk reached the traveler

        // The traveler leaves — far past the streaming radius AND the client's ~24-chunk unload distance,
        // placed high above the terrain so neither the entombed- nor the void-rescue relocates the anchor.
        var homePos = camper.State.Position;
        traveler.State.Position = new Vector3f(homePos.X, homePos.Y + 120f, homePos.Z + 640f);

        for (int i = 0; i < 15; i++)
        {
            server.TickForTest(1.0); // run past the 10 s sweep interval
        }

        // The camper keeps the region alive, so the cache must keep it — which is exactly why the old
        // "forget only what was evicted" bookkeeping never forgot it for the traveler.
        Assert.True(server.World.IsChunkLoaded(home), "the camper's region must stay cached through the sweep");
        Assert.Contains(home, camper.SentChunks); // the camper still sees it — no over-pruning
        Assert.DoesNotContain(home, traveler.SentChunks); // the traveler's client unloaded it — forget it here too

        // And the return trip re-streams it through the normal path (the visible half of the bug).
        traveler.State.Position = homePos;
        for (int i = 0; i < 20; i++)
        {
            server.TickForTest(0.1);
        }

        Assert.Contains(home, traveler.SentChunks);
    }

    [Fact]
    [Trait("Category", "Slow")]
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
        // A chunk 5 east is beyond the client's radius-3 view AND its one-ring load-ahead margin (radius+1 = 4),
        // so it must never have been streamed. (#388's load-ahead reaches +4, not +5.)
        var beyondClientView = new ChunkCoord(center.X + 5, center.Y, center.Z);

        Assert.True(server.World.IsChunkLoaded(withinClientView), "client's wider view distance should stream terrain past the host default");
        Assert.False(server.World.IsChunkLoaded(beyondClientView), "nothing beyond the client's requested radius (plus the one-ring load-ahead) should stream");
    }

    [Fact]
    [Trait("Category", "Slow")]
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

        // A SUBMERGED far column stretches its band up to the waterline (#987), so its ceiling is the cap, not
        // the three-chunk surface band. Probe for that only after counting — GetBlock caches the chunks it reads.
        bool submerged = false;
        int probeX = (center.X + 5) * WorldConstants.ChunkSize + WorldConstants.ChunkSize / 2;
        int probeZ = center.Z * WorldConstants.ChunkSize + WorldConstants.ChunkSize / 2;
        for (int y = (center.Y - 8) * WorldConstants.ChunkSize; y <= (center.Y + 8) * WorldConstants.ChunkSize; y++)
        {
            var key = server.World.Definition(server.World.GetBlock(new Vector3i(probeX, y, probeZ)))?.Key;
            if (key is "water" or "lava") { submerged = true; break; }
        }

        Assert.Equal(6, nearLayers); // near column: full vertical span
        Assert.InRange(farLayers, 1, submerged ? 6 : 3); // far column: the surface band, stretched to the waterline when flooded
    }

    [Theory]
    // A dry world (no sea at all) and a column standing above the sea keep the original three-chunk band.
    [InlineData(100, int.MinValue, 5, 7)]
    [InlineData(100, 64, 5, 7)]
    // Submerged: the seabed sits at y=100 (chunk 6) but the water reaches y=150 (chunk 9), so the band must
    // stretch up to chunk 10 instead of stopping at chunk 7 with the ocean cut off mid-water (#987).
    [InlineData(100, 150, 5, 10)]
    // A shallow sea whose waterline shares the seabed's chunk changes nothing.
    [InlineData(100, 104, 5, 7)]
    public void FarColumnBand_ReachesTheWaterline_OnSubmergedColumns(int surfaceY, int seaLevel, int expectedLo, int expectedHi)
    {
        var (lo, hi) = SvGameServer.FarColumnBand(surfaceY, seaLevel);

        Assert.Equal(expectedLo, lo);
        Assert.Equal(expectedHi, hi);
    }

    [Fact]
    public void FarColumnBand_CapsAVeryDeepFloodedColumn_ByTrimmingTheSeabed_NotTheWaterline()
    {
        // A flooded rift: hundreds of blocks of water over the seabed. The waterline must still be in the band
        // (that is the whole point), but the band may not grow without bound — it is trimmed from the bottom.
        var (lo, hi) = SvGameServer.FarColumnBand(surfaceY: 0, seaLevel: 500);

        Assert.Equal(WorldConstants.WorldToChunk(500) + 1, hi);
        Assert.Equal(6, hi - lo + 1); // capped total height
        Assert.True(lo <= WorldConstants.WorldToChunk(500), "the waterline's own chunk must stay inside the band");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
