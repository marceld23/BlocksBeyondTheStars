// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Threading.Tasks;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#4044: the timing samples PerfProbe reads for the chunk work.</summary>
public sealed class ChunkWorkStatsTests
{
    [Fact]
    public void Snapshot_OfNothing_IsAllZero()
    {
        var sample = new WorkSampler().Snapshot(reset: false);

        Assert.Equal(0, sample.Count);
        Assert.Equal(0d, sample.TotalMs);
        Assert.Equal(0f, sample.P50Ms);
        Assert.Equal(0f, sample.P90Ms);
        Assert.Equal(0f, sample.MaxMs);
    }

    [Fact]
    public void Snapshot_ReportsCountSumPercentilesAndMax()
    {
        var sampler = new WorkSampler();
        for (int ms = 1; ms <= 11; ms++)
        {
            sampler.Add(ms);
        }

        var sample = sampler.Snapshot(reset: false);

        Assert.Equal(11, sample.Count);
        Assert.Equal(66d, sample.TotalMs, 3);
        Assert.Equal(6f, sample.P50Ms);
        Assert.Equal(10f, sample.P90Ms);
        Assert.Equal(11f, sample.MaxMs);
    }

    [Fact]
    public void Snapshot_WithReset_StartsANewWindow()
    {
        var sampler = new WorkSampler();
        sampler.Add(40);

        Assert.Equal(1, sampler.Snapshot(reset: true).Count);

        sampler.Add(2);
        var sample = sampler.Snapshot(reset: false);
        Assert.Equal(1, sample.Count);
        Assert.Equal(2f, sample.MaxMs);
        Assert.Equal(2f, sample.P50Ms);
    }

    [Fact]
    public void Add_PastTheCapacity_KeepsCountingAndTakesPercentilesOverTheRecentOnes()
    {
        var sampler = new WorkSampler();
        for (int i = 0; i < WorkSampler.Capacity; i++)
        {
            sampler.Add(100);
        }

        for (int i = 0; i < WorkSampler.Capacity; i++)
        {
            sampler.Add(1); // overwrites every older sample
        }

        var sample = sampler.Snapshot(reset: false);

        Assert.Equal(2L * WorkSampler.Capacity, sample.Count);
        Assert.Equal(101d * WorkSampler.Capacity, sample.TotalMs, 0);
        Assert.Equal(1f, sample.P90Ms);
        Assert.Equal(100f, sample.MaxMs); // the longest since the reset, not only of the recent ones
    }

    [Fact]
    public void Add_NegativeTime_CountsAsZero()
    {
        var sampler = new WorkSampler();
        sampler.Add(-5);

        var sample = sampler.Snapshot(reset: false);
        Assert.Equal(1, sample.Count);
        Assert.Equal(0d, sample.TotalMs);
    }

    [Fact]
    public void Add_FromManyThreads_LosesNothing()
    {
        var sampler = new WorkSampler();
        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 1000; i++)
            {
                sampler.Add(1);
            }
        });

        var sample = sampler.Snapshot(reset: false);
        Assert.Equal(8000, sample.Count);
        Assert.Equal(8000d, sample.TotalMs, 3);
    }
}
