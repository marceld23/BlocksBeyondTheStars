// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
            WorldId = 3, TileX = -2, TileZ = 7, Version = 4,
            Cells = new byte[] { 0, 17, 255 }, TopY = new short[] { 70, -40, 300 },
            Blocks = new ushort[] { 1, 2, 3 }, Tints = new[] { 0, 0x123456, 0 },
        };
        var back = Assert.IsType<FarTerrainTile>(NetCodec.Decode(NetCodec.Encode(tile)));
        Assert.Equal(tile.Cells, back.Cells);
        Assert.Equal(tile.TopY, back.TopY);
        Assert.Equal(tile.Tints, back.Tints);
        Assert.Equal(-2, back.TileX);

        var request = new FarTerrainTileRequest { WorldId = 3, Tiles = new[] { 1, 2, 3, 4 } };
        Assert.Equal(request.Tiles, Assert.IsType<FarTerrainTileRequest>(NetCodec.Decode(NetCodec.Encode(request))).Tiles);
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
