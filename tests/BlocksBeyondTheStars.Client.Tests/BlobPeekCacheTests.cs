// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The saved-name peek cache (#1368): the browser menu's look into the save blob runs once per blob version,
/// not once per menu build — and never caches a blob that is not there yet.
/// </summary>
public sealed class BlobPeekCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bbts_peekcache_" + Guid.NewGuid().ToString("N"));

    public BlobPeekCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void UnchangedBlob_IsComputedOnce()
    {
        string blob = Path.Combine(_dir, "world.blob");
        File.WriteAllBytes(blob, new byte[] { 1, 2, 3 });
        var cache = new BlobPeekCache();
        int computed = 0;
        string? Compute() { computed++; return "Justus"; }

        Assert.Equal("Justus", cache.Get(blob, Compute));
        Assert.Equal("Justus", cache.Get(blob, Compute));
        Assert.Equal("Justus", cache.Get(blob, Compute));

        Assert.Equal(1, computed);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public void RewrittenBlob_IsComputedAgain()
    {
        string blob = Path.Combine(_dir, "world.blob");
        File.WriteAllBytes(blob, new byte[] { 1, 2, 3 });
        var cache = new BlobPeekCache();
        Assert.Equal("first", cache.Get(blob, () => "first"));

        // A save rewrites the blob: different length AND a later write time — either alone invalidates.
        File.WriteAllBytes(blob, new byte[] { 1, 2, 3, 4 });
        File.SetLastWriteTimeUtc(blob, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal("second", cache.Get(blob, () => "second"));
        Assert.Equal("second", cache.Get(blob, () => "third")); // …and the new answer sticks
        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public void SameLength_NewerWriteTime_IsComputedAgain()
    {
        string blob = Path.Combine(_dir, "world.blob");
        File.WriteAllBytes(blob, new byte[] { 9, 9 });
        var cache = new BlobPeekCache();
        Assert.Equal("a", cache.Get(blob, () => "a"));

        File.SetLastWriteTimeUtc(blob, File.GetLastWriteTimeUtc(blob).AddMinutes(1));
        Assert.Equal("b", cache.Get(blob, () => "b"));
    }

    [Fact]
    public void MissingBlob_IsNeverCached()
    {
        string blob = Path.Combine(_dir, "absent.blob");
        var cache = new BlobPeekCache();
        int computed = 0;
        string? Compute() { computed++; return null; }

        Assert.Null(cache.Get(blob, Compute));
        Assert.Null(cache.Get(blob, Compute));
        Assert.Equal(2, computed); // no stamp → ask again (the loader may adopt one from an older deployment)

        // Once the blob exists the answer is cached from then on.
        File.WriteAllBytes(blob, new byte[] { 7 });
        Assert.Equal("Marcel", cache.Get(blob, () => "Marcel"));
        Assert.Equal("Marcel", cache.Get(blob, () => "nope"));
    }

    [Fact]
    public void Invalidate_ForcesTheNextGetToCompute()
    {
        string blob = Path.Combine(_dir, "world.blob");
        File.WriteAllBytes(blob, new byte[] { 1 });
        var cache = new BlobPeekCache();
        Assert.Equal("x", cache.Get(blob, () => "x"));
        cache.Invalidate();
        Assert.Equal("y", cache.Get(blob, () => "y"));
        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public void NullOrEmptyPath_Computes_WithoutCaching()
    {
        var cache = new BlobPeekCache();
        int computed = 0;
        Assert.Null(cache.Get(string.Empty, () => { computed++; return null; }));
        Assert.Null(cache.Get(string.Empty, () => { computed++; return null; }));
        Assert.Equal(2, computed);
    }
}
