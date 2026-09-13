// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1821: the far view asks the server for the persisted builds of distant tiles.</summary>
public sealed class FarTerrainTileTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_fartiles_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EditColumnTops_ReturnTheHighestNonAirEditPerColumn_InsideTheBox_OnBothRepositories()
    {
        using var sqlite = new SqliteWorldRepository(new SaveGamePaths(_root, "tops_sqlite"));
        sqlite.Initialize();
        using var memory = new MemoryWorldRepository(new SaveGamePaths(_root, "tops_memory"));
        memory.Initialize();

        foreach (IWorldRepository repo in new IWorldRepository[] { sqlite, memory })
        {
            repo.SetBlock("body:a", new Vector3i(10, 70, 5), 7);
            repo.SetBlock("body:a", new Vector3i(10, 95, 5), 9, tint: 0x802040); // higher in the same column
            repo.SetBlock("body:a", new Vector3i(10, 120, 5), 0);                // air above it never counts
            repo.SetBlock("body:a", new Vector3i(-3, -40, 60), 4);              // negative coords, low
            repo.SetBlock("body:a", new Vector3i(200, 80, 5), 11);              // outside the box
            repo.SetBlock("body:b", new Vector3i(11, 80, 5), 12);               // another body

            var tops = repo.LoadEditColumnTops("body:a", -64, 0, 63, 63).OrderBy(t => t.X).ToArray();
            Assert.Equal(2, tops.Length);
            Assert.Equal((-3, -40, 60, (ushort)4), (tops[0].X, tops[0].Y, tops[0].Z, tops[0].Block));
            Assert.Equal((10, 95, 5, (ushort)9, 0x802040), (tops[1].X, tops[1].Y, tops[1].Z, tops[1].Block, tops[1].Tint));
        }
    }

    [Fact]
    public void ATileSummarisesItsBuilds_IsRefusedOutOfRange_AndIsResentWhenAnEditChangesIt()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tiles"));
        var config = new ServerConfig { WorldName = "tiles", Seed = 5, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Watcher");
        var feet = p.State.Position.ToBlock();
        var stone = Content.GetBlock("stone")!.NumericId;

        // A tower ~300 blocks east of the player: a 3×3 column of stone up to y = 200.
        int bx = feet.X + 300, bz = feet.Z;
        int circ = server.World.Circumference;
        for (int y = 150; y <= 200; y++)
        {
            server.World.SetBlock(new Vector3i(bx, y, bz), stone);
        }

        var canon = Shared.World.WorldConstants.CanonicalBlock(new Vector3i(bx, 200, bz), circ);
        int tx = canon.X >> 6, tz = canon.Z >> 6;
        Assert.True(circ >= 4000, "the test needs a world wide enough for an out-of-range tile");
        var far = Shared.World.WorldConstants.CanonicalBlock(new Vector3i(feet.X + 1800, 200, bz), circ);
        int farTx = far.X >> 6;
        server.FarTerrainTileRequestForTest(p, new FarTerrainTileRequest { Tiles = new[] { tx, tz, farTx, tz } });

        // In range: answered; ~1.8 km away (beyond every far view): refused, nothing scanned or sent.
        Assert.True(p.FarTilesSent.ContainsKey((tx, tz)), "the in-range tile should have been answered");
        Assert.False(p.FarTilesSent.ContainsKey((farTx, tz)), "a tile beyond every far view must be refused");
        int firstVersion = p.FarTilesSent[(tx, tz)];

        // Raising the tower dirties the tile; the refresh re-sends a newer version.
        server.World.SetBlock(new Vector3i(bx, 201, bz), stone);
        for (int i = 0; i < 25; i++)
        {
            server.TickForTest(0.1);
        }

        Assert.True(p.FarTilesSent[(tx, tz)] > firstVersion, "an edit inside a held tile must re-send it");
        server.Stop();
    }

    [Fact]
    public void TheWorldInfo_CarriesTheGeneratorSettings_AndGoesOutOnTheFirstPass()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "info"));
        var config = new ServerConfig { WorldName = "info", Seed = 9, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Surveyor");
        Assert.True(p.FarInfoDue);
        server.TickForTest(0.1);
        Assert.False(p.FarInfoDue, "the first streaming pass sends the world info");

        var info = server.BuildFarTerrainWorldInfo();
        Assert.Equal(server.World.Circumference, info.Circumference);
        Assert.Equal(server.World.LocationId, info.LocationId);
        Assert.Equal(server.World.LandingPadFlats.Count * FarTerrainWorldInfo.PadStride, info.Pads.Length);
        Assert.False(info.Void);
        server.Stop();
    }

    [Fact]
    public void TheTileMessages_AreRegistered_AndRoundTrip()
    {
        var tile = new FarTerrainTile
        {
            WorldId = 3,
            TileX = -2,
            TileZ = 7,
            Version = 4,
            Cells = new byte[] { 0, 17, 255 },
            TopY = new short[] { 70, -40, 300 },
            Blocks = new ushort[] { 1, 2, 3 },
            Tints = new[] { 0, 0x123456, 0 },
        };
        var back = Assert.IsType<FarTerrainTile>(NetCodec.Decode(NetCodec.Encode(tile)));
        Assert.Equal(tile.Cells, back.Cells);
        Assert.Equal(tile.TopY, back.TopY);
        Assert.Equal(tile.Tints, back.Tints);
        Assert.Equal(-2, back.TileX);

        var request = new FarTerrainTileRequest { WorldId = 3, Tiles = new[] { 1, 2, 3, 4 } };
        Assert.Equal(request.Tiles, Assert.IsType<FarTerrainTileRequest>(NetCodec.Decode(NetCodec.Encode(request))).Tiles);
    }

    // ---------------- #1871: the far-tile query must never scan the planet, and builds are paced per tick ----------------

    [Fact]
    public void ColumnTopsQueryPlan_LooksUpTheOuterRowsByTheirFullKey_NeverScansThePlanet()
    {
        // On a city save (808k edits) the old plain JOIN put block_edit on the outer side with only planet=? to
        // search on — a scan of every edit on the planet per tile (50–90 ms, empty tiles included). The CROSS
        // JOIN pins the order; this test pins the plan so a future SQLite or query edit cannot bring it back.
        using var sqlite = new SqliteWorldRepository(new SaveGamePaths(_root, "plan_sqlite"));
        sqlite.Initialize();
        var plan = sqlite.ExplainEditColumnTopsForTest();
        Assert.NotEmpty(plan);
        Assert.Contains(plan, step => step.Contains("x=?", StringComparison.Ordinal) && step.Contains("y=?", StringComparison.Ordinal) && step.Contains("z=?", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, step => step.Contains("AUTOMATIC", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan, step => step.Contains("BLOOM", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<FarTerrainTile> Tiles = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is FarTerrainTile t) Tiles.Add(t);
        }

        public void Broadcast(byte[] payload, DeliveryMode mode) { }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    [Fact]
    public void TileBuilds_ArePacedByThePerTickBudget_AndCachedTilesAnswerAtOnce()
    {
        // A request burst on a built-up world used to build every tile inside the handler and freeze the tick for
        // a minute (doors opened seconds late). Now a request queues the builds; ServeFarTiles drains the queue
        // under a wall-clock budget — pinned here to "one build per tick" so the pacing is observable.
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "paced"));
        var transport = new RecordingTransport();
        var config = new ServerConfig { WorldName = "paced", Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        var p = server.AddLocalPlayer("Watcher");
        server.TickForTest(0.1); // the world-info pass
        server.FarTileBudgetMsForTest = 0;

        var feet = p.State.Position.ToBlock();
        int circ = server.World.Circumference;
        var origin = Shared.World.WorldConstants.CanonicalBlock(feet, circ);
        int tx0 = origin.X >> 6, tz0 = origin.Z >> 6;
        var pairs = new List<int>();
        for (int i = 0; i < 24; i++)
        {
            pairs.Add(tx0 + i % 6 - 3);
            pairs.Add(tz0 + i / 6 - 2);
        }

        server.FarTerrainTileRequestForTest(p, new FarTerrainTileRequest { Tiles = pairs.ToArray() }, queueOnly: true);
        Assert.Empty(transport.Tiles); // nothing is built in the handler any more
        Assert.Equal(24, p.FarTileQueue.Count);

        server.TickForTest(0.1);
        Assert.Single(transport.Tiles); // one build per tick at budget 0
        for (int i = 0; i < 23; i++)
        {
            server.TickForTest(0.1);
        }

        Assert.Equal(24, transport.Tiles.Count);
        Assert.Equal(24, transport.Tiles.Select(t => (t.TileX, t.TileZ)).Distinct().Count()); // every tile once
        Assert.Empty(p.FarTileQueue);

        // Asking again for tiles the world has cached is answered inside the handler — free, no queue.
        server.FarTerrainTileRequestForTest(p, new FarTerrainTileRequest { Tiles = pairs.Take(8).ToArray() }, queueOnly: true);
        Assert.Equal(28, transport.Tiles.Count);
        Assert.Empty(p.FarTileQueue);

        // A re-ask of a tile that is still queued is a no-op: the queue holds each tile once.
        transport.Tiles.Clear();
        var fresh = new[] { tx0 + 9, tz0, tx0 + 9, tz0, tx0 + 10, tz0 };
        server.FarTerrainTileRequestForTest(p, new FarTerrainTileRequest { Tiles = fresh }, queueOnly: true);
        Assert.Equal(2, p.FarTileQueue.Count);
        server.FarTileBudgetMsForTest = -1; // unlimited: the queue drains in one tick
        server.TickForTest(0.1);
        Assert.Equal(2, transport.Tiles.Count);
        server.Stop();
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
        catch (IOException)
        {
            // lingering SQLite handle on Windows
        }
    }
}
