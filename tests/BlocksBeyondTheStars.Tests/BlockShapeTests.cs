// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Craftable non-cube building shapes (sphere/dome/pyramid/ramp/…): a per-voxel form modifier, analogous to
/// the dye tint. Covers the packed descriptor, the item-key encoding, chunk storage, persistence, the wire
/// messages, and the always-available Shape craft action.
/// </summary>
public sealed class BlockShapeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public BlockShapeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shape_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void ShapeCode_PacksAndUnpacks_ShapeAndOrientation()
    {
        int d = ShapeCode.Pack(BlockShape.Ramp, 3);
        Assert.Equal((int)BlockShape.Ramp, ShapeCode.ShapeOf(d));
        Assert.Equal(3, ShapeCode.OrientationOf(d));
        Assert.False(ShapeCode.IsCube(d));

        Assert.True(ShapeCode.IsCube(ShapeCode.Pack(BlockShape.Cube, 0)));
        Assert.True(ShapeCode.IsValidShape((int)BlockShape.Cylinder));
        Assert.False(ShapeCode.IsValidShape(0));   // cube is not a "real" shape to build
        Assert.False(ShapeCode.IsValidShape(99));  // out of range
    }

    [Fact]
    public void ItemKey_EncodesShape_AndComposesWithColour()
    {
        string sphere = ItemKey.Compose("stone", 0, 0, (int)BlockShape.Sphere);
        Assert.Equal("stone#s04", sphere);
        Assert.Equal("stone", ItemKey.Base(sphere));
        Assert.Equal((int)BlockShape.Sphere, ItemKey.Shape(sphere));
        Assert.Equal(0, ItemKey.Tint(sphere));

        // No modifiers at all → the bare base key (shape 0 adds nothing).
        Assert.Equal("stone", ItemKey.Compose("stone", 0, 0, 0));

        // Tint + glow + shape compose together, in the order t, g, s; each reads back independently.
        string combo = ItemKey.Compose("mud", 0xFF0000, 0x00FF00, (int)BlockShape.Ramp);
        Assert.Equal(0xFF0000, ItemKey.Tint(combo));
        Assert.Equal(0x00FF00, ItemKey.Glow(combo));
        Assert.Equal((int)BlockShape.Ramp, ItemKey.Shape(combo));
        var (baseKey, tint, glow) = ItemKey.Parse(combo);
        Assert.Equal("mud", baseKey);
        Assert.Equal(0xFF0000, tint);
        Assert.Equal(0x00FF00, glow);

        // Re-composing rebases (drops the old modifiers) and applies the new shape only.
        Assert.Equal("mud#s03", ItemKey.Compose(combo, 0, 0, (int)BlockShape.Dome));
    }

    [Fact]
    public void ChunkData_StoresShape_IndependentOfTint_AndClearsOnAir()
    {
        var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
        chunk.Set(1, 2, 3, new BlockId(5));
        chunk.SetModifier(1, 2, 3, 0x123456, 0);
        chunk.SetShape(1, 2, 3, ShapeCode.Pack(BlockShape.Dome, 2));

        Assert.Equal(ShapeCode.Pack(BlockShape.Dome, 2), chunk.GetShape(1, 2, 3));
        Assert.Equal((0x123456, 0), chunk.GetModifier(1, 2, 3)); // colour untouched by the shape
        Assert.True(chunk.HasShapes);

        // Clearing to air drops the shape (and the colour) so a re-placed plain block never inherits it.
        chunk.Set(1, 2, 3, BlockId.Air);
        Assert.Equal(0, chunk.GetShape(1, 2, 3));
        Assert.False(chunk.HasShapes);
    }

    [Fact]
    public void Persistence_RoundTripsShapeDescriptor()
    {
        int desc = ShapeCode.Pack(BlockShape.Pyramid, 1);
        using (var repo = NewRepo())
        {
            repo.SetBlock("rocky", new Vector3i(1, 2, 3), 5, tint: 0x112233, glow: 0, shape: desc);
        }

        using var reopened = NewRepo();
        var edits = reopened.LoadChunkEdits("rocky", new ChunkCoord(0, 0, 0));
        Assert.Single(edits);
        Assert.Equal(desc, edits[0].Shape);
        Assert.Equal(0x112233, edits[0].Tint);
    }

    [Fact]
    public void Networking_RoundTripsShapeMessages()
    {
        var intent = new ShapeCraftIntent { SourceItemKey = "stone", Shape = (int)BlockShape.Ramp, Count = 2 };
        var di = Assert.IsType<ShapeCraftIntent>(NetCodec.Decode(NetCodec.Encode(intent)));
        Assert.Equal("stone", di.SourceItemKey);
        Assert.Equal((int)BlockShape.Ramp, di.Shape);
        Assert.Equal(2, di.Count);

        int desc = ShapeCode.Pack(BlockShape.Dome, 2);
        var chunk = new ChunkDataMessage { Cx = 1, Blocks = new ushort[] { 0, 1 }, ShapeIndex = new[] { 7 }, ShapeData = new[] { desc } };
        var dc = Assert.IsType<ChunkDataMessage>(NetCodec.Decode(NetCodec.Encode(chunk)));
        Assert.Equal(new[] { 7 }, dc.ShapeIndex);
        Assert.Equal(new[] { desc }, dc.ShapeData);

        var bc = new BlockChanged { X = 1, Block = 5, Shape = ShapeCode.Pack(BlockShape.Cone, 1) };
        var dbc = Assert.IsType<BlockChanged>(NetCodec.Decode(NetCodec.Encode(bc)));
        Assert.Equal(ShapeCode.Pack(BlockShape.Cone, 1), dbc.Shape);
    }

    [Fact]
    public void ShapeCraft_FormsShapeableMaterial_IntoAShapedItem()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Inventory.Add("stone", 4, 99);

            server.ShapeCraft("Builder", "stone", (int)BlockShape.Sphere);
            Assert.Equal(3, p.State.Inventory.CountOf("stone"));      // one source consumed
            Assert.Equal(1, p.State.Inventory.CountOf("stone#s04"));  // one shaped sphere produced

            // The server stamps the packed (shape + orientation) descriptor on the placed cell + reads it back.
            int desc = ShapeCode.Pack(BlockShape.Ramp, 2);
            var pos = new Vector3i(2, 64, 2);
            server.World.SetBlock(pos, _content.GetBlock("stone")!.NumericId, 0, 0, desc);
            Assert.Equal(desc, server.World.GetShape(pos));
        }
    }

    [Fact]
    public void ShapeCraft_RefusesNonShapeableMaterial()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Inventory.Add("iron_ingot", 2, 99); // a material that doesn't place a shapeable block

            server.ShapeCraft("Builder", "iron_ingot", (int)BlockShape.Sphere);
            Assert.Equal(2, p.State.Inventory.CountOf("iron_ingot"));            // nothing consumed
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ingot#s04"));        // nothing produced
        }
    }

    private SqliteWorldRepository NewRepo(string world = "world_001")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        return repo;
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shape"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "shape", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    public void Dispose()
    {
        // NB: the repos this class opens are disposed via their `using` blocks; we deliberately do NOT call
        // SqliteConnection.ClearAllPools() here — that is global and would dispose connections held by other
        // test classes running in parallel (a flaky ObjectDisposedException). Just remove the temp folder.
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup races on Windows file locks
        }
    }
}
