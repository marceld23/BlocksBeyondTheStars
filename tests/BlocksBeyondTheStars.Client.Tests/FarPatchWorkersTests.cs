// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.FarTerrain;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#4048: the far terrain samples on one to three threads, by the machine.</summary>
public sealed class FarPatchWorkersTests
{
    private const int PlentyOfMemory = 32 * 1024;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(11, 1)]
    [InlineData(12, 2)]
    [InlineData(15, 2)]
    [InlineData(16, 3)]
    [InlineData(32, 3)]
    [InlineData(128, 3)]
    public void For_FollowsTheLogicalProcessors_BetweenOneAndThree(int logicalProcessors, int expected)
        => Assert.Equal(expected, FarPatchWorkers.For(logicalProcessors, PlentyOfMemory));

    [Theory]
    [InlineData(0)]                                // unknown
    [InlineData(-1)]
    [InlineData(8 * 1024)]
    [InlineData(FarPatchWorkers.MinMemoryMb - 1)]
    public void For_WithLittleOrUnknownMemory_KeepsOneWorker(int systemMemoryMb)
        => Assert.Equal(FarPatchWorkers.Minimum, FarPatchWorkers.For(64, systemMemoryMb));

    [Fact]
    public void For_AtTheMemoryFloor_ScalesWithTheMachine()
        => Assert.Equal(FarPatchWorkers.Maximum, FarPatchWorkers.For(64, FarPatchWorkers.MinMemoryMb));

    [Fact]
    public void For_NeverLeavesItsBounds()
    {
        for (int cores = -8; cores <= 512; cores++)
        {
            Assert.InRange(FarPatchWorkers.For(cores, PlentyOfMemory), FarPatchWorkers.Minimum, FarPatchWorkers.Maximum);
        }
    }
}
