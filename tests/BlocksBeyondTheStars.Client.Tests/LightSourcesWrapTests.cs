// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #428 (N10): placed-lamp light must not stop dead at the world wrap seams. Sources are stored canonically;
/// <see cref="ClientWorld.LightSourcesNear"/> has to measure the short way round the X (circumference) and Z
/// (latitude) seams and hand the mesher positions in the queried chunk's own un-wrapped frame.
/// </summary>
public sealed class LightSourcesWrapTests
{
    private const int Circ = 512; // small round world: X wraps at 512, Z period = 256 (±128)

    private static ClientWorld World(params ChunkCoord[] chunks)
    {
        var w = new ClientWorld();
        w.SetCircumference(Circ);
        foreach (var c in chunks)
        {
            w.StoreChunk(c, new ushort[WorldConstants.ChunkSize * WorldConstants.ChunkSize * WorldConstants.ChunkSize]);
        }

        return w;
    }

    [Fact]
    public void LampJustWestOfTheSeam_ReachesTheChunkAtXZero_InItsRawFrame()
    {
        // The last chunk column (X = 496..511) and the first (X = 0..15) touch across the seam.
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(Circ / 16 - 1, 0, 0));
        Assert.True(w.ApplyBlockChange(Circ - 2, 4, 3, block: 1, tint: 0, glow: 0xFF8800, out _));

        var near = w.LightSourcesNear(new ChunkCoord(0, 0, 0), 9);

        var hit = Assert.Single(near);
        Assert.Equal(new Vector3i(-2, 4, 3), hit.Pos); // re-expressed: 510 → −2 relative to origin X = 0
        Assert.Equal(0xFF8800, hit.Rgb);
    }

    [Fact]
    public void LampJustEastOfTheSeam_ReachesTheLastChunk_AsPastTheCircumference()
    {
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(Circ / 16 - 1, 0, 0));
        Assert.True(w.ApplyBlockChange(2, 4, 3, block: 1, tint: 0, glow: 0x00FF00, out _));

        var near = w.LightSourcesNear(new ChunkCoord(Circ / 16 - 1, 0, 0), 9);

        var hit = Assert.Single(near);
        Assert.Equal(new Vector3i(Circ + 2, 4, 3), hit.Pos); // origin 496: 2 → 514 (raw frame of that chunk)
    }

    [Fact]
    public void LampAcrossTheLatitudeSeam_IsFound()
    {
        // Latitude period 256 → canonical Z ∈ [−128, 128); Z = −128 (first row) and Z = 127 (last row) touch.
        int lastZChunk = 127 / 16;   // chunk Z 7 (112..127)
        int firstZChunk = -128 / 16; // chunk Z −8 (−128..−113)
        var w = World(new ChunkCoord(0, 0, firstZChunk), new ChunkCoord(0, 0, lastZChunk));
        Assert.True(w.ApplyBlockChange(5, 4, 126, block: 1, tint: 0, glow: 0x0000FF, out _));

        var near = w.LightSourcesNear(new ChunkCoord(0, 0, firstZChunk), 9);

        var hit = Assert.Single(near);
        Assert.Equal(new Vector3i(5, 4, -130), hit.Pos); // 126 → −130 (2 south of the −128 origin, the short way)
    }

    [Fact]
    public void LampFarAway_IsStillFiltered()
    {
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(10, 0, 0));
        Assert.True(w.ApplyBlockChange(165, 4, 3, block: 1, tint: 0, glow: 0xFFFFFF, out _));

        Assert.Empty(w.LightSourcesNear(new ChunkCoord(0, 0, 0), 9));
        Assert.Single(w.LightSourcesNear(new ChunkCoord(10, 0, 0), 9));
    }
}
