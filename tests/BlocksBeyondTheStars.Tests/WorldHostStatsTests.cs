// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Server-stats observability (issues #244/#245): the /proc and docker-stats parsers behind the admin
/// "Server health" card, and the TTL/single-flight cache that makes the public /api/stats endpoint safe.
/// All pure logic — no docker, no /proc, no web host needed.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // the async cache hand-offs starve in the parallel suite
public sealed class WorldHostStatsTests
{
    // ---------------- /proc parsers ----------------

    [Fact]
    public void ParseMeminfo_ReadsTotalAndAvailable()
    {
        const string sample = "MemTotal:        8069984 kB\nMemFree:         6224140 kB\nMemAvailable:    7256064 kB\nBuffers:          123456 kB\n";
        var parsed = HostStats.ParseMeminfo(sample);
        Assert.NotNull(parsed);
        Assert.Equal(8069984, parsed!.Value.TotalKb);
        Assert.Equal(7256064, parsed.Value.AvailableKb);
    }

    [Fact]
    public void ParseMeminfo_NullWhenFieldsMissing()
    {
        Assert.Null(HostStats.ParseMeminfo("MemFree: 100 kB\n"));
        Assert.Null(HostStats.ParseMeminfo(string.Empty));
    }

    [Fact]
    public void ParseLoadavg_ReadsThreeAverages()
    {
        var parsed = HostStats.ParseLoadavg("0.42 0.30 0.19 1/123 4567\n");
        Assert.NotNull(parsed);
        Assert.Equal(0.42, parsed!.Value.Load1, 3);
        Assert.Equal(0.30, parsed.Value.Load5, 3);
        Assert.Equal(0.19, parsed.Value.Load15, 3);
        Assert.Null(HostStats.ParseLoadavg("garbage"));
    }

    [Fact]
    public void ParseCpuinfoCores_CountsProcessorEntries()
    {
        // x86 layout: one "processor : N" block per logical CPU, plus lines that merely start
        // with "processor"-unrelated keys ("model name" contains "processor" mid-line — ignored).
        const string sample =
            "processor\t: 0\nvendor_id\t: GenuineIntel\nmodel name\t: Some Xeon processor\n\n" +
            "processor\t: 1\nvendor_id\t: GenuineIntel\n\n" +
            "processor\t: 2\n\nprocessor\t: 3\n\nprocessor\t: 4\n\nprocessor\t: 5\n";
        Assert.Equal(6, HostStats.ParseCpuinfoCores(sample));
    }

    [Fact]
    public void ParseCpuinfoCores_NullWhenNoEntries()
    {
        Assert.Null(HostStats.ParseCpuinfoCores(string.Empty));
        Assert.Null(HostStats.ParseCpuinfoCores("model name : whatever\n"));
        // "processor" as a key prefix without the colon separator must not count.
        Assert.Null(HostStats.ParseCpuinfoCores("processors total 4\n"));
    }

    [Fact]
    public void DiskFor_CurrentDirectory_ReportsPositiveSizes()
    {
        var disk = HostStats.DiskFor(Environment.CurrentDirectory);
        Assert.NotNull(disk);
        Assert.True(disk!.Value.TotalBytes > 0);
        Assert.True(disk.Value.FreeBytes >= 0);
    }

    // ---------------- docker stats parsing ----------------

    [Theory]
    [InlineData("115.7MiB", 121_320_243L)]
    [InlineData("7.696GiB", 8_263_517_078L)]
    [InlineData("512kB", 512_000L)]
    [InlineData("1.5GB", 1_500_000_000L)]
    [InlineData("0B", 0L)]
    public void ParseSizeToBytes_HandlesDockerUnits(string text, long expected)
    {
        var bytes = HostStats.ParseSizeToBytes(text);
        Assert.NotNull(bytes);
        // double→long conversion may round the last bits; a per-mille tolerance is plenty here.
        Assert.InRange(bytes!.Value, expected - expected / 1000 - 1, expected + expected / 1000 + 1);
    }

    [Fact]
    public void ParseSizeToBytes_NullOnGarbage()
    {
        Assert.Null(HostStats.ParseSizeToBytes("many"));
        Assert.Null(HostStats.ParseSizeToBytes(""));
    }

    [Fact]
    public void ParseDockerStats_ParsesRowsAndSkipsGarbage()
    {
        const string output =
            "{\"Name\":\"bbs-caddy\",\"CPUPerc\":\"0.12%\",\"MemUsage\":\"45.2MiB / 7.696GiB\"}\n" +
            "not json at all\n" +
            "{\"Name\":\"bbs-worldhost\",\"CPUPerc\":\"1.50%\",\"MemUsage\":\"115.7MiB / 7.696GiB\"}\n";

        var rows = HostStats.ParseDockerStats(output);

        Assert.Equal(2, rows.Count);
        Assert.Equal("bbs-caddy", rows[0].Name);
        Assert.Equal(0.12, rows[0].CpuPercent, 3);
        Assert.Equal("bbs-worldhost", rows[1].Name);
        Assert.True(rows[1].MemUsedBytes > 100L * 1024 * 1024);
        Assert.True(rows[1].MemLimitBytes > 7L * 1024 * 1024 * 1024);
    }

    [Fact]
    public void ParseDockerStats_EmptyOutput_EmptyList()
    {
        Assert.Empty(HostStats.ParseDockerStats(string.Empty));
    }

    // ---------------- CachedJson (the public-endpoint guard) ----------------

    [Fact]
    public async Task CachedJson_ServesFromCacheWithinTtlAsync()
    {
        long now = 1000;
        int rebuilds = 0;
        var cache = new CachedJson(TimeSpan.FromSeconds(30), () =>
        {
            rebuilds++;
            return Task.FromResult($"snapshot-{rebuilds}");
        }, () => now);

        Assert.Equal("snapshot-1", await cache.GetAsync());
        now += 29;
        Assert.Equal("snapshot-1", await cache.GetAsync());
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public async Task CachedJson_RebuildsAfterTtlAsync()
    {
        long now = 1000;
        int rebuilds = 0;
        var cache = new CachedJson(TimeSpan.FromSeconds(30), () =>
        {
            rebuilds++;
            return Task.FromResult($"snapshot-{rebuilds}");
        }, () => now);

        Assert.Equal("snapshot-1", await cache.GetAsync());
        now += 31;
        Assert.Equal("snapshot-2", await cache.GetAsync());
        Assert.Equal(2, rebuilds);
    }

    [Fact]
    public async Task CachedJson_ConcurrentFirstRequests_RebuildOnceAsync()
    {
        int rebuilds = 0;
        using var release = new SemaphoreSlim(0);
        using var cache = new CachedJson(TimeSpan.FromSeconds(30), async () =>
        {
            Interlocked.Increment(ref rebuilds);
            await release.WaitAsync();
            return "snapshot";
        }, () => 1000);

        var calls = Enumerable.Range(0, 8).Select(_ => cache.GetAsync()).ToArray();
        release.Release();
        var results = await Task.WhenAll(calls);

        Assert.All(results, r => Assert.Equal("snapshot", r));
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public async Task CachedJson_StaleValueServedWhileRebuildInFlightAsync()
    {
        long now = 1000;
        int rebuilds = 0;
        using var release = new SemaphoreSlim(0);
        using var cache = new CachedJson(TimeSpan.FromSeconds(30), async () =>
        {
            int n = Interlocked.Increment(ref rebuilds);
            if (n > 1)
            {
                await release.WaitAsync(); // second rebuild hangs — stale readers must not wait on it
            }

            return $"snapshot-{n}";
        }, () => now);

        Assert.Equal("snapshot-1", await cache.GetAsync());
        now += 31;
        var slowRebuild = cache.GetAsync(); // takes the gate, hangs in the builder

        // While the rebuild is in flight, other callers get the stale snapshot immediately.
        Assert.Equal("snapshot-1", await cache.GetAsync());

        release.Release();
        Assert.Equal("snapshot-2", await slowRebuild);
    }
}
