// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Threading;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3068: a new world's first boot spent 15–19 s finding its landing pads. The speed-up must not move a single pad, so
/// every piece is checked against the serial answer: the batch runner, the river grid sampled on several threads, the
/// whole river field built with build workers, and the pad search's column memo (across the longitude seam too).
/// </summary>
public sealed class PadSearchSpeedTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ParallelWork_RunsEveryIndexExactlyOnce(int workers)
    {
        var hits = new int[200];
        int states = 0;

        ParallelWork.For(hits.Length, workers, -1, () => Interlocked.Increment(ref states),
            (i, _) => Interlocked.Increment(ref hits[i]));

        Assert.All(hits, h => Assert.Equal(1, h));
        Assert.InRange(states, 0, workers);
    }

    [Fact]
    public void ParallelWork_RethrowsAFailureOnTheCaller()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ParallelWork.For(50, 3, 0, () => 0, (i, _) =>
            {
                if (i == 17)
                {
                    throw new InvalidOperationException("item 17");
                }
            }));

        Assert.Equal("item 17", error.Message);
    }

    [Fact]
    public void RiverNetwork_FromAGridSampledOnSeveralThreads_IsTheSerialNetwork()
    {
        var content = Content();
        var planet = content.GetPlanet("jungle")!;
        const long seed = 992211;
        int circ = WorldConstants.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        var generator = new WorldGenerator(seed, content);
        int Height(int x, int z) => generator.SurfaceHeight(planet, x, z);

        var serial = RiverNetwork.Build(seed, circ, period, 40, Height, cellSize: 16);
        var (gridW, gridH) = RiverNetwork.GridSize(circ, period, 16);
        var sampled = new int[gridW * gridH];
        ParallelWork.For(gridH, 3, generator, generator.CreateSibling,
            (gz, g) => RiverNetwork.SampleRow(gz, circ, period, 16, (x, z) => g.SurfaceHeight(planet, x, z), sampled));
        var parallel = RiverNetwork.Build(seed, circ, period, 40, Height, cellSize: 16, sampledHeights: sampled);

        Assert.Equal(serial.Height, parallel.Height);
        Assert.Equal(serial.FilledLevel, parallel.FilledLevel);
        Assert.Equal(serial.FlowDir, parallel.FlowDir);
        Assert.Equal(serial.FlowAccum, parallel.FlowAccum);
        Assert.Equal(serial.ChannelCells, parallel.ChannelCells);
    }

    [Fact]
    public void RiverField_BuiltWithBuildWorkers_IsTheSerialField()
    {
        var content = Content();
        var planet = content.GetPlanet("river_lowlands")!;
        const long seed = 4242;
        var serial = new WorldGenerator(seed, content);
        var parallel = new WorldGenerator(seed, content);
        parallel.SetBuildWorkers(3);

        var a = serial.BuildRiverFieldForTest(planet);
        var b = parallel.BuildRiverFieldForTest(planet);

        Assert.True(a.ColumnCount > 0, "the test world should route rivers");
        Assert.Equal(a.ColumnCount, b.ColumnCount);
        Assert.Equal(a.WaterfallColumnCount, b.WaterfallColumnCount);
        Assert.Equal(a.LakeShoreColumnCount, b.LakeShoreColumnCount);
        foreach (var (cell, col) in a.ColumnsByPosition)
        {
            Assert.True(b.TryGet(cell.X, cell.Z, out var other), $"column {cell} missing");
            Assert.Equal((col.WaterSurfaceY, col.BedY, col.WaterfallDrop, col.FlowAxis, col.Underground, col.RoofY, col.Mouth),
                (other.WaterSurfaceY, other.BedY, other.WaterfallDrop, other.FlowAxis, other.Underground, other.RoofY, other.Mouth));
        }
    }

    [Fact]
    public void PadColumnMemo_AsksEachColumnOnce_AcrossTheLongitudeSeam()
    {
        const int circ = 1024;
        int wetAsks = 0, heightAsks = 0;
        var memo = new SvGameServer.PadColumnMemo(circ - 2, 0, 20, circ,
            (x, z) => { wetAsks++; return WorldConstants.WrapX(x, circ) % 2 == 0; },
            (x, z) => false,
            (x, z) => { heightAsks++; return WorldConstants.WrapX(x, circ) + z; });

        // Column 1 east of the seam, asked as the wrapped and as the unwrapped X.
        bool first = memo.IsWet(1, 5);
        bool again = memo.IsWet(circ + 1, 5);
        int h1 = memo.Height(1, 5);
        int h2 = memo.Height(circ + 1, 5);

        Assert.False(first);
        Assert.Equal(first, again);
        Assert.Equal(1, wetAsks);
        Assert.Equal(6, h1);
        Assert.Equal(h1, h2);
        Assert.Equal(1, heightAsks);

        // Outside the window: answered, never remembered.
        memo.IsWet(circ / 2, 0);
        memo.IsWet(circ / 2, 0);
        Assert.Equal(3, wetAsks);
    }
}
