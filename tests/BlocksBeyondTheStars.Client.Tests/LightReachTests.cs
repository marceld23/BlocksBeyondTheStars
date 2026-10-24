// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2951: a lamp placed six cells before a chunk border lit only its own chunk — the chunk next door kept its old mesh,
/// and the light stopped dead along the border plane. A block change must re-mesh every chunk the light can reach:
/// <see cref="ClientWorld.LightReachThrough"/> says how far light travels on from the edited cell, and
/// <see cref="LightReach.ChunksTouched"/> turns that into the chunks to rebuild.
/// </summary>
public sealed class LightReachTests
{
    private const int Circ = 512; // small round world: X wraps at 512
    private const int Lamp = 0xFFFFFF; // a white glow — a fixture with the full reach (9)

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
    public void ChunksTouched_CoversExactlyTheChunksWithinTheStepsLeftPlusTheSamplingCell()
    {
        foreach (var (wx, wy, wz, steps) in new[] { (10, 4, 3, 8), (0, 0, 0, 8), (15, 15, 15, 2), (8, 8, 8, 0), (-20, 33, 100, 5) })
        {
            var got = new HashSet<ChunkCoord>();
            LightReach.ChunksTouched(wx, wy, wz, steps, Circ, got);

            var expected = new HashSet<ChunkCoord>();
            int half = steps + 1;
            for (int x = wx - half; x <= wx + half; x++)
                for (int y = wy - half; y <= wy + half; y++)
                    for (int z = wz - half; z <= wz + half; z++)
                    {
                        var c = new ChunkCoord(WorldConstants.WorldToChunk(x), WorldConstants.WorldToChunk(y), WorldConstants.WorldToChunk(z));
                        expected.Add(WorldConstants.CanonicalChunk(c, Circ));
                    }

            Assert.Equal(expected.OrderBy(c => (c.X, c.Y, c.Z)), got.OrderBy(c => (c.X, c.Y, c.Z)));
        }
    }

    [Fact]
    public void ChunksTouched_NoLightReachingTheCell_TouchesNothing()
    {
        var got = new List<ChunkCoord>();
        LightReach.ChunksTouched(10, 4, 3, -1, Circ, got);
        Assert.Empty(got);
    }

    [Fact]
    public void LampSixCellsBeforeABorder_ReachesTheChunkNextDoor()
    {
        // The report: a lamp at local x 10 of chunk 0, the border at x 16. Its own cell keeps reach − 1 = 8 steps.
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 0));
        Assert.True(w.ApplyBlockChange(10, 4, 3, block: 1, tint: 0, glow: Lamp, out _));

        int steps = w.LightReachThrough(10, 4, 3);
        Assert.Equal(8, steps);

        var touched = new HashSet<ChunkCoord>();
        LightReach.ChunksTouched(10, 4, 3, steps, Circ, touched);
        Assert.Contains(new ChunkCoord(1, 0, 0), touched);
    }

    [Fact]
    public void LightReachThrough_FallsByOnePerManhattanStep_AndEndsPastTheReach()
    {
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 0));
        Assert.True(w.ApplyBlockChange(10, 4, 3, block: 1, tint: 0, glow: Lamp, out _));

        Assert.Equal(7, w.LightReachThrough(11, 4, 3));  // one step
        Assert.Equal(5, w.LightReachThrough(11, 5, 4));  // three steps (one on each axis)
        Assert.Equal(0, w.LightReachThrough(18, 4, 3));  // eight steps: the last lit cell
        Assert.Equal(-1, w.LightReachThrough(19, 4, 3)); // nine steps: dark
    }

    [Fact]
    public void LightReachThrough_WithoutAnyLight_IsMinusOne()
    {
        var w = World(new ChunkCoord(0, 0, 0));
        Assert.Equal(-1, w.LightReachThrough(5, 5, 5));
    }

    [Fact]
    public void MiningTheLamp_IsSeenBeforeTheChange_AndGoneAfterIt()
    {
        // The client reads the cell's source and reach BEFORE applying the change, so a mined lamp still re-meshes
        // its whole reach (the neighbour chunk otherwise keeps a ghost pool of light).
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 0));
        Assert.True(w.ApplyBlockChange(10, 4, 3, block: 1, tint: 0, glow: Lamp, out _));
        int before = w.LightSourceAt(10, 4, 3);
        int reachBefore = w.LightReachThrough(10, 4, 3);

        Assert.True(w.ApplyBlockChange(10, 4, 3, block: 0, tint: 0, glow: 0, out _));

        Assert.Equal(Lamp, before);
        Assert.Equal(8, reachBefore);
        Assert.Equal(0, w.LightSourceAt(10, 4, 3));
        Assert.Equal(-1, w.LightReachThrough(10, 4, 3));
    }

    [Fact]
    public void LightReachThrough_MeasuresTheShortWayRoundTheSeam()
    {
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(Circ / 16 - 1, 0, 0));
        Assert.True(w.ApplyBlockChange(Circ - 2, 4, 3, block: 1, tint: 0, glow: Lamp, out _));

        Assert.Equal(4, w.LightReachThrough(2, 4, 3)); // 510 → 2 is four steps across the seam, not 508
    }

    [Fact]
    public void ChunksLitFrom_ALampNearACorner_ReachesTheDiagonalChunk()
    {
        // A freshly stored chunk used to refresh only its six face neighbours; a lamp near a chunk corner also lights
        // the chunk diagonally across it.
        var w = World(new ChunkCoord(0, 0, 0), new ChunkCoord(1, 0, 1));
        Assert.True(w.ApplyBlockChange(14, 4, 14, block: 1, tint: 0, glow: Lamp, out _));

        var lit = new HashSet<ChunkCoord>();
        w.ChunksLitFrom(new ChunkCoord(0, 0, 0), lit);

        Assert.Contains(new ChunkCoord(1, 0, 1), lit);
        Assert.Contains(new ChunkCoord(0, 0, 0), lit);
    }

    [Fact]
    public void ChunksLitFrom_ADarkChunk_AddsNothing()
    {
        var w = World(new ChunkCoord(0, 0, 0));
        var lit = new List<ChunkCoord>();
        w.ChunksLitFrom(new ChunkCoord(0, 0, 0), lit);
        Assert.Empty(lit);
    }
}
