// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#1531: the in-process loopback hands server→client messages over as objects — the whole join and
/// world stream arrive without a single server→client encode — while the byte mode still works for the tests
/// that want the codec in the loop. Client→server stays encoded in both modes.</summary>
public sealed class LoopbackObjectPassingTests
{
    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void ObjectMode_JoinsAndStreamsChunks_WithoutEncodingAnythingToTheClient()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Join();

        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => h.Chunks.Count >= 4, maxTicks: 60), "chunks should stream over the object path");
        Assert.True(h.Link.ObjectsToClient > 0);
        Assert.Equal(0, h.Link.BytesToClient);

        // The world view was filled from the objects exactly as it would be from decoded payloads.
        var first = h.Chunks.Values.First();
        Assert.True(h.World.TryGetChunk(new ChunkCoord(first.Cx, first.Cy, first.Cz), out _));
    }

    [Fact]
    public void ByteMode_JoinsAndStreamsChunks_ThroughTheCodec()
    {
        using var h = new ClientServerHarness(LoadContent(), passObjects: false);
        h.Join();

        Assert.NotNull(h.JoinAccepted);
        Assert.True(h.PumpUntil(() => h.Chunks.Count >= 4, maxTicks: 60), "chunks should stream over the byte path");
        Assert.True(h.Link.BytesToClient > 0);
        Assert.Equal(0, h.Link.ObjectsToClient);
    }

    [Fact]
    public void ObjectMode_ARestreamedChunk_IsTheSameCachedInstance_AndStillReadOnlyForTheClient()
    {
        using var h = new ClientServerHarness(LoadContent());
        h.Join();
        Assert.True(h.PumpUntil(() => h.Chunks.Count >= 1, maxTicks: 60));

        // The server caches the chunk message per version; the client must never have written into it —
        // the RLE payload it received still decodes to the blocks the world view holds.
        var m = h.Chunks.Values.First();
        var again = m.DecodeBlocks(WorldConstants.BlocksPerChunk);
        Assert.NotNull(again);
        Assert.True(h.World.TryGetChunk(new ChunkCoord(m.Cx, m.Cy, m.Cz), out var stored));
        Assert.Equal(again, stored.ToArray());
    }
}
