// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#3211 sparse chunk anchoring: while a player is on a body, the chunks holding Crystal Net cells and the chunks
/// with player builds in a base's reach stay resident wherever they are, so a machine park or a base a few hundred blocks
/// away keeps working; the host cap bounds it (whole networks smallest first, base builds nearest the core first), a body
/// nobody is on loads nothing, and a removed net releases its chunks.</summary>
public sealed class ChunkAnchorTests : IDisposable
{
    private static readonly Vector3f FarAway = new(0.5f, 260f, 400.5f); // 25 chunks from the nets, above any terrain

    private readonly string _root;
    private readonly GameContent _content;

    public ChunkAnchorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_anchor_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer NewServer(string world, out SqliteWorldRepository repo, out ServerConfig config, IGameLogger? log = null,
        long seed = 1, string startPlanet = "varied")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = startPlanet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = 1, // a small view (keep radius 5 chunks) keeps the streaming cheap; the nets sit 25 chunks off
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo, log);
        server.Start();
        return server;
    }

    private static PlayerSession Builder(SvGameServer server, Vector3f at, params string[] items)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = at;
        foreach (var key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 64, 1024));
        }

        p.State.SuitEnergy = 100f;
        return p;
    }

    /// <summary>Runs the server past one far-chunk sweep (every 10 s) in whole-second ticks.</summary>
    private static void PastASweep(SvGameServer server)
    {
        for (int i = 0; i < 11; i++)
        {
            server.TickForTest(1.0);
        }
    }

    private static ChunkCoord ChunkOf(SvGameServer server, int x, int y, int z)
        => WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(new Vector3i(x, y, z)), server.World.Circumference);

    /// <summary>The lamp's block as the server holds it — read without loading, so the read can never mask an eviction.</summary>
    private string LampKey(SvGameServer server, Vector3i lamp)
        => _content.BlockById(server.World.GetBlockIfLoaded(lamp))?.Key ?? "air";

    [Fact]
    public void ACrystalNet_FarFromTheOnlyPlayer_StaysResident_AndKeepsSwitchingItsLamp()
    {
        var server = NewServer("far_net", out var repo, out _);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "timer_block", "crystal_conduit", "light_white");
            server.PlaceBlock("Builder", 1, 200, 0, "timer_block", yaw: 1); // drives +X
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 3, 200, 0, "light_white");            // beside a net cell → it joins
            server.SetCrystalDeviceForTest(p, new Vector3i(1, 200, 0), action: 2, mode: (int)TimerMode.Clock, config: "period=1");
            var lamp = new Vector3i(3, 200, 0);
            var netChunk = ChunkOf(server, 3, 200, 0);

            // The player walks 400 blocks away and stays there past the 10 s sweep.
            p.State.Position = FarAway;
            PastASweep(server);

            Assert.True(server.IsChunkAnchoredForTest(netChunk), "the net's chunk must be anchored");
            Assert.True(server.World.IsChunkLoaded(netChunk), "the sweep must keep the anchored net chunk resident");

            // And the net keeps working out there: the clock still swaps the lamp on and off.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.1);
                seen.Add(LampKey(server, lamp));
            }

            Assert.Contains("light_white", seen);
            Assert.Contains("light_white_off", seen);
        }
    }

    [Fact]
    public void TheCap_KeepsWholeNetsSmallestFirst_NeverHalfANet_AndLogsTheCut()
    {
        var log = new CapturingLogger();
        var server = NewServer("cap", out var repo, out var config, log);
        using (repo)
        {
            var p = Builder(server, new Vector3f(15.5f, 202, 0.5f), "crystal_conduit"); // hovering above the row
            for (int x = 14; x <= 17; x++)
            {
                server.PlaceBlock("Builder", x, 200, 0, "crystal_conduit"); // net A straddles chunks x 0 and 1
            }

            p.State.Position = new Vector3f(40, 200, 0);
            server.PlaceBlock("Builder", 41, 200, 0, "crystal_conduit");     // net B: chunk x 2
            p.State.Position = new Vector3f(80, 200, 0);
            server.PlaceBlock("Builder", 81, 200, 0, "crystal_conduit");     // net C: chunk x 5
            Assert.Equal(3, server.CrystalNetSnapshots.Count);

            config.MaxAnchoredChunks = 3; // room for B and C, but not for both of A's chunks
            server.RefreshChunkAnchorsForTest();

            Assert.Equal(4, server.AnchorChunksWantedForTest);
            Assert.Equal(2, server.AnchoredChunkCountForTest);
            Assert.True(server.IsChunkAnchoredForTest(ChunkOf(server, 41, 200, 0)));
            Assert.True(server.IsChunkAnchoredForTest(ChunkOf(server, 81, 200, 0)));
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 14, 200, 0)));
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 17, 200, 0)));
            Assert.Single(log.Infos, line => line.Contains("Chunk anchors on", StringComparison.Ordinal));

            server.RefreshChunkAnchorsForTest(); // the same cut again: no second line
            Assert.Single(log.Infos, line => line.Contains("Chunk anchors on", StringComparison.Ordinal));

            // Past the sweep, far away: what the cap kept is resident, the cut net is gone.
            p.State.Position = FarAway;
            PastASweep(server);
            Assert.True(server.World.IsChunkLoaded(ChunkOf(server, 41, 200, 0)));
            Assert.True(server.World.IsChunkLoaded(ChunkOf(server, 81, 200, 0)));
            Assert.False(server.World.IsChunkLoaded(ChunkOf(server, 14, 200, 0)));
            Assert.False(server.World.IsChunkLoaded(ChunkOf(server, 17, 200, 0)));

            config.MaxAnchoredChunks = 0; // off: only players keep chunks again
            server.RefreshChunkAnchorsForTest();
            Assert.Equal(0, server.AnchoredChunkCountForTest);
        }
    }

    [Fact]
    public void AnEmptyBody_LoadsNoAnchoredChunk()
    {
        var server = NewServer("empty", out var repo, out _);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_conduit");
            server.PlaceBlock("Builder", 2, 200, 0, "crystal_conduit");
            var netChunk = ChunkOf(server, 1, 200, 0);
            server.DisconnectLocalPlayerForTest(p.State.PlayerId); // the start world stays resident and ticks headless
            Assert.True(server.CrystalCellCount >= 2, "precondition: the net is still registered on the resident world");

            server.World.UnloadFarChunks(new[] { new ChunkCoord(netChunk.X + 100, netChunk.Y, netChunk.Z + 100) }, 0);
            Assert.False(server.World.IsChunkLoaded(netChunk));
            int loadsBefore = server.AnchorChunkLoadsForTest;

            PastASweep(server); // past the sweep with nobody on the body

            Assert.Equal(0, server.AnchoredChunkCountForTest);
            Assert.Equal(loadsBefore, server.AnchorChunkLoadsForTest);
            Assert.False(server.World.IsChunkLoaded(netChunk), "nobody is on the body: the net's chunk must stay unloaded");
        }
    }

    [Fact]
    public void ARemovedNet_ReleasesItsAnchor_AndTheSweepEvictsTheChunk()
    {
        var server = NewServer("release", out var repo, out _);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_conduit");
            server.PlaceBlock("Builder", 1, 200, 0, "crystal_conduit");
            var netChunk = ChunkOf(server, 1, 200, 0);
            server.RefreshChunkAnchorsForTest();
            Assert.True(server.IsChunkAnchoredForTest(netChunk));

            server.MineBlock("Builder", 1, 200, 0); // the last cell goes (no base here, so the dug cell anchors nothing)
            Assert.Equal(0, server.CrystalCellCount);
            server.RefreshChunkAnchorsForTest();
            Assert.False(server.IsChunkAnchoredForTest(netChunk));

            p.State.Position = FarAway;
            PastASweep(server);
            Assert.False(server.World.IsChunkLoaded(netChunk), "an unanchored chunk far from every player is swept as before");
        }
    }

    [Fact]
    public void ABase_AnchorsOnlyItsBuiltChunks_NearestTheCoreFirst()
    {
        var server = NewServer("base", out var repo, out var config, seed: 7, startPlanet: "rocky");
        using (repo)
        {
            const int y = 120;
            var p = Builder(server, new Vector3f(0, y, 0), "base_core");
            server.PlaceBlock(p.State.PlayerId, 1, y, 0, "base_core");
            Assert.NotEmpty(server.BaseSnapshots);
            var stone = _content.GetBlock("stone")!.NumericId;

            server.World.SetBlock(new Vector3i(40, y, 0), stone, owner: p.State.PlayerId);  // a built wall, chunk x 2
            server.World.SetBlock(new Vector3i(100, y, 0), stone, owner: p.State.PlayerId); // a far post, chunk x 6
            server.World.SetBlock(new Vector3i(-10, y, 0), stone, owner: p.State.PlayerId); // across the seam: chunk x -1, the nearest
            server.World.SetBlock(new Vector3i(60, y, 0), stone);                          // a server write (no owner), chunk x 3
            server.RefreshChunkAnchorsForTest();

            Assert.Equal(4, server.AnchoredChunkCountForTest); // the core's chunk and the three built ones — nothing else
            var core = ChunkOf(server, 1, y, 0);
            var seam = ChunkOf(server, -10, y, 0);
            Assert.True(server.IsChunkAnchoredForTest(core));
            Assert.True(server.IsChunkAnchoredForTest(seam));
            Assert.True(server.IsChunkAnchoredForTest(ChunkOf(server, 40, y, 0)));
            Assert.True(server.IsChunkAnchoredForTest(ChunkOf(server, 100, y, 0)));
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 60, y, 0)), "a server write is no player build");
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 80, y, 0)), "the box between the builds is not anchored");

            // The cap keeps the nearest by the wrapped distance to the core — the seam chunk is the HIGHEST canonical x, so
            // the store's index order would have put it last.
            config.MaxAnchoredChunks = 2;
            server.RefreshChunkAnchorsForTest();
            Assert.Equal(2, server.AnchoredChunkCountForTest);
            Assert.True(server.IsChunkAnchoredForTest(core));
            Assert.True(server.IsChunkAnchoredForTest(seam));
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 40, y, 0)));
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 100, y, 0)));
        }
    }

    [Fact]
    public void ABaseBuild_JoinsTheAnchors_WithoutAskingTheStoreAgainOnEverySweep()
    {
        var server = NewServer("base_cache", out var repo, out _, seed: 7, startPlanet: "rocky");
        using (repo)
        {
            const int y = 120;
            var p = Builder(server, new Vector3f(0, y, 0), "base_core");
            server.PlaceBlock(p.State.PlayerId, 1, y, 0, "base_core");
            Assert.NotEmpty(server.BaseSnapshots);
            var stone = _content.GetBlock("stone")!.NumericId;

            server.RefreshChunkAnchorsForTest();
            int queries = server.BaseAnchorQueriesForTest;
            Assert.True(queries > 0, "the first refresh reads the base's builds from the store");
            server.RefreshChunkAnchorsForTest();
            Assert.Equal(queries, server.BaseAnchorQueriesForTest); // the next sweep reuses them

            var newWall = ChunkOf(server, 140, y, 0);
            Assert.False(server.IsChunkAnchoredForTest(newWall));
            server.World.SetBlock(new Vector3i(140, y, 0), stone, owner: p.State.PlayerId); // a player builds far out
            server.World.SetBlock(new Vector3i(150, y, 0), stone);                          // a server write does not count
            server.RefreshChunkAnchorsForTest();

            Assert.True(server.IsChunkAnchoredForTest(newWall), "a new build inside the base's reach joins the anchors");
            Assert.False(server.IsChunkAnchoredForTest(ChunkOf(server, 150, y, 0))); // chunk x 9: only the server write is there
            Assert.Equal(queries, server.BaseAnchorQueriesForTest); // ...without a store query
        }
    }

    [Fact]
    public void AnchoredChunks_AreLoadedAFewPerTick_NotAllAtOnce()
    {
        var server = NewServer("amortised", out var repo, out _);
        using (repo)
        {
            var p = Builder(server, new Vector3f(0, 200, 0), "crystal_conduit");
            var cells = new List<ChunkCoord>();
            for (int i = 0; i < 6; i++)
            {
                int x = 8 + i * 16; // one single-cell net per chunk, x chunks 0..5
                p.State.Position = new Vector3f(x - 1, 200, 0.5f);
                server.PlaceBlock("Builder", x, 200, 0, "crystal_conduit");
                cells.Add(ChunkOf(server, x, 200, 0));
            }

            p.State.Position = FarAway;
            server.World.UnloadFarChunks(new[] { WorldConstants.WorldToChunk(FarAway.ToBlock()) }, 0); // as if swept
            server.RefreshChunkAnchorsForTest();
            Assert.Equal(6, server.AnchoredChunkCountForTest);
            int before = server.AnchorChunkLoadsForTest;

            server.TickForTest(0.01);
            int afterOne = server.AnchorChunkLoadsForTest - before;
            Assert.InRange(afterOne, 1, 3); // one inline, or one per generation worker plus the tick thread

            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.01);
            }

            Assert.All(cells, c => Assert.True(server.World.IsChunkLoaded(c), $"anchored chunk {c} should be loaded by now"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListPlayerEditedChunks_ReturnsTheDistinctFloorChunksOfPlayerEdits(bool memory)
    {
        var paths = new SaveGamePaths(_root, memory ? "edits_mem" : "edits_sql");
        using IWorldRepository repo = memory ? new MemoryWorldRepository(paths) : new SqliteWorldRepository(paths);
        repo.Initialize();
        const string body = "sys0-p1";
        repo.SetBlock(body, new Vector3i(-1, 5, -17), 1, owner: "alice");  // chunk (-1, 0, -2)
        repo.SetBlock(body, new Vector3i(-16, 5, 0), 1, owner: "alice");   // chunk (-1, 0, 0)
        repo.SetBlock(body, new Vector3i(-16, 6, 1), 0, owner: "bob");     // same chunk, a dug cell
        repo.SetBlock(body, new Vector3i(15, 5, 15), 1, owner: "alice");   // chunk (0, 0, 0)
        repo.SetBlock(body, new Vector3i(17, 5, 15), 1, owner: "alice");   // chunk (1, 0, 0)
        repo.SetBlock(body, new Vector3i(3, 3, 3), 1);                     // server write: no owner
        repo.SetBlock(body, new Vector3i(40, 5, 0), 1, owner: "alice");    // outside the box
        repo.SetBlock("other", new Vector3i(1, 5, 1), 1, owner: "alice");  // another body

        var box = (Min: new Vector3i(-32, -32, -32), Max: new Vector3i(31, 31, 31));
        var chunks = repo.ListPlayerEditedChunks(body, box.Min, box.Max, 100);

        var expected = new[] { new ChunkCoord(-1, 0, -2), new ChunkCoord(-1, 0, 0), new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 0) };
        Assert.Equal(expected.OrderBy(c => (c.X, c.Y, c.Z)), chunks.OrderBy(c => (c.X, c.Y, c.Z)));
        Assert.Equal(2, repo.ListPlayerEditedChunks(body, box.Min, box.Max, 2).Count);
        Assert.Empty(repo.ListPlayerEditedChunks(body, box.Min, box.Max, 0));
    }
}
