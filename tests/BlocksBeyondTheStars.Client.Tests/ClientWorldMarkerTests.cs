// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3275: the marker-block index the geyser view finds its vents through — filled when a chunk is stored, kept in
/// step by block changes, dropped with the chunk, and measured the short way round the wrap seams.</summary>
public sealed class ClientWorldMarkerTests
{
    private const int Circ = 512; // X wraps at 512, Z period 256 (±128)
    private const ushort Vent = 7;
    private const ushort Stone = 2;
    private static readonly int Size = WorldConstants.ChunkSize;

    private static ushort[] ChunkWith(params (int X, int Y, int Z, ushort Block)[] cells)
    {
        var blocks = new ushort[Size * Size * Size];
        foreach (var c in cells)
        {
            blocks[WorldConstants.LocalIndex(c.X, c.Y, c.Z)] = c.Block;
        }

        return blocks;
    }

    private static ClientWorld World()
    {
        var w = new ClientWorld();
        w.SetCircumference(Circ);
        return w;
    }

    private static List<(Vector3i Pos, ushort Block)> Near(ClientWorld w, int x, int z, int r)
    {
        var list = new List<(Vector3i, ushort)>();
        w.MarkersNear(x, z, r, list);
        return list;
    }

    [Fact]
    public void AStoredChunk_IndexesItsVents_OnlyTheRegisteredBlock()
    {
        var w = World();
        w.SetMarkerBlocks(new[] { Vent });
        w.StoreChunk(new ChunkCoord(2, 4, 1), ChunkWith((3, 5, 7, Vent), (4, 5, 7, Stone)));

        var hit = Assert.Single(Near(w, 32, 16, 40));
        Assert.Equal(new Vector3i(35, 69, 23), hit.Pos);
        Assert.Equal(Vent, hit.Block);
        Assert.Equal(1, w.MarkerCount);
    }

    [Fact]
    public void RegisteringLate_IndexesTheChunksAlreadyStored()
    {
        var w = World();
        w.StoreChunk(new ChunkCoord(0, 0, 0), ChunkWith((1, 1, 1, Vent)));
        Assert.Empty(Near(w, 0, 0, 50));

        int before = w.MarkerVersion;
        w.SetMarkerBlocks(new[] { Vent });
        Assert.Single(Near(w, 0, 0, 50));
        Assert.NotEqual(before, w.MarkerVersion);

        int same = w.MarkerVersion;
        w.SetMarkerBlocks(new[] { Vent }); // the same ids again: nothing to do
        Assert.Equal(same, w.MarkerVersion);
    }

    [Fact]
    public void BlockChanges_AddAndRemoveVents()
    {
        var w = World();
        w.SetMarkerBlocks(new[] { Vent });
        w.StoreChunk(new ChunkCoord(0, 0, 0), ChunkWith((1, 1, 1, Vent)));

        Assert.True(w.ApplyBlockChange(1, 1, 1, 0, out _)); // mined away
        Assert.Empty(Near(w, 0, 0, 50));

        Assert.True(w.ApplyBlockChange(5, 2, 9, Vent, out _)); // placed
        Assert.Equal(new Vector3i(5, 2, 9), Assert.Single(Near(w, 0, 0, 50)).Pos);
    }

    [Fact]
    public void Unloading_AndClearing_DropTheVents()
    {
        var w = World();
        w.SetMarkerBlocks(new[] { Vent });
        w.StoreChunk(new ChunkCoord(0, 0, 0), ChunkWith((1, 1, 1, Vent)));
        w.StoreChunk(new ChunkCoord(3, 0, 0), ChunkWith((1, 1, 1, Vent)));

        Assert.True(w.RemoveChunk(new ChunkCoord(0, 0, 0)));
        Assert.Equal(1, w.MarkerCount);

        w.Clear();
        Assert.Equal(0, w.MarkerCount);
    }

    [Fact]
    public void Restoring_AChunk_ReplacesItsVents()
    {
        var w = World();
        w.SetMarkerBlocks(new[] { Vent });
        w.StoreChunk(new ChunkCoord(0, 0, 0), ChunkWith((1, 1, 1, Vent)));
        w.StoreChunk(new ChunkCoord(0, 0, 0), ChunkWith((2, 2, 2, Vent)));

        Assert.Equal(new Vector3i(2, 2, 2), Assert.Single(Near(w, 0, 0, 50)).Pos);
    }

    [Fact]
    public void TheRadius_IsHorizontal_AndMeasuredAcrossTheSeams()
    {
        var w = World();
        w.SetMarkerBlocks(new[] { Vent });
        // A vent just west of the X seam (x = 510) and one just north of the Z seam (z = 126 ≡ −130).
        w.StoreChunk(new ChunkCoord(Circ / Size - 1, 0, 0), ChunkWith((14, 3, 4, Vent)));
        w.StoreChunk(new ChunkCoord(0, 0, 127 / Size), ChunkWith((5, 3, 126 - (127 / Size) * Size, Vent)));
        w.StoreChunk(new ChunkCoord(10, 9, 0), ChunkWith((0, 0, 0, Vent))); // far away, high up

        var near = Near(w, 2, 0, 20);
        Assert.Contains(near, m => m.Pos == new Vector3i(-2, 3, 4));      // 510 → −2 in the query's frame

        var south = Near(w, 5, -126, 10);
        Assert.Contains(south, m => m.Pos == new Vector3i(5, 3, -130));  // 126 → −130, the short way

        Assert.DoesNotContain(near, m => m.Pos.X == 160);
        Assert.Single(Near(w, 160, 0, 3)); // height does not count, only the column
    }
}
