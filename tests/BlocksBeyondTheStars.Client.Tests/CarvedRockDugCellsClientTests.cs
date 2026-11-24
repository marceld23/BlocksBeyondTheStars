// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4011: the client side of the dug cells — the chunk restores the server's dug runs, a live mine of natural rock marks the
/// cell itself (the server's rule, <see cref="DugCells.DigsLive"/>), and the terrain forms read the marks, so walls a player
/// carved out of a mountain stay flat.
/// </summary>
public sealed class CarvedRockDugCellsClientTests
{
    private const ushort Stone = 3;
    private const ushort Planks = 9;

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    private static ClientWorld World(ushort[]? dugRuns = null)
    {
        var w = new ClientWorld();
        w.SetCircumference(512);
        w.SetDugTerrainBlocks(new[] { Stone });
        var blocks = new ushort[WorldConstants.BlocksPerChunk];
        Array.Fill(blocks, Stone);
        var built = WorldConstants.LocalIndex(4, 5, 6);
        w.StoreChunk(new ChunkCoord(0, 0, 0), blocks, editRuns: new ushort[] { (ushort)built, 1 }, dugRuns: dugRuns);
        return w;
    }

    [Fact]
    public void TheDugRuns_AreRestored_WithTheChunk()
    {
        int idx = WorldConstants.LocalIndex(1, 1, 1);
        var world = World(new ushort[] { (ushort)idx, 2 });
        Assert.True(world.TryGetChunk(new ChunkCoord(0, 0, 0), out var chunk));
        Assert.True(chunk.IsDugLocal(idx));
        Assert.True(chunk.IsDugLocal(idx + 1));
        Assert.False(chunk.IsDugLocal(idx + 2));
    }

    [Fact]
    public void ALiveMine_OfNaturalRock_DigsTheCell_APlacedBlockDoesNot()
    {
        var world = World();
        Assert.True(world.TryGetChunk(new ChunkCoord(0, 0, 0), out var chunk));

        Assert.True(world.ApplyBlockChange(9, 9, 9, BlockId.AirValue, out _)); // natural stone mined
        Assert.True(chunk.IsDugLocal(WorldConstants.LocalIndex(9, 9, 9)));

        Assert.True(world.ApplyBlockChange(4, 5, 6, BlockId.AirValue, out _)); // the placed (edited) block mined
        Assert.False(chunk.IsDugLocal(WorldConstants.LocalIndex(4, 5, 6)));

        Assert.True(world.ApplyBlockChange(9, 9, 9, Planks, out _)); // a plank put into the tunnel keeps the mark
        Assert.True(chunk.IsDugLocal(WorldConstants.LocalIndex(9, 9, 9)));

        var plain = new ClientWorld(); // no table registered: a live change digs nothing
        plain.StoreChunk(new ChunkCoord(0, 0, 0), new ushort[WorldConstants.BlocksPerChunk]);
        Assert.True(plain.TryGetChunk(new ChunkCoord(0, 0, 0), out var plainChunk));
        plain.ApplyBlockChange(2, 2, 2, Stone, out _);
        plain.ApplyBlockChange(2, 2, 2, BlockId.AirValue, out _);
        Assert.False(plainChunk.IsDugLocal(WorldConstants.LocalIndex(2, 2, 2)));
    }

    [Fact]
    public void TheDugMarks_TravelFromTheChunkMessage_ToTheTerrainForms()
    {
        string streaming = Script("GameBootstrap.ChunkStreaming.cs");
        Assert.Contains("m.ShapeIndex, m.ShapeData, m.EditRuns, m.DugRuns);", streaming);
        Assert.Contains("if (src.HasEdits || src.HasDug)", streaming); // the worker's chunk copy carries them
        Assert.Contains("worldDug: Hood.DugFn", streaming);
        Assert.Contains("World.SetDugTerrainBlocks(DugCells.CountingIds(Content.Blocks.Values));", Script("GameBootstrap.cs"));

        string forms = Script("ChunkMesher.TerrainForms.cs");
        Assert.Contains("if (DugAt(wx + d.X, wy + d.Y, wz + d.Z))", forms);
        Assert.Contains("return Chunk.IsDugLocal(WorldConstants.LocalIndex(lx, ly, lz));", forms);
        Assert.Contains("c.WorldDug = worldDug;", forms);
    }
}
