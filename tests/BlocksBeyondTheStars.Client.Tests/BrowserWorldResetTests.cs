// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// "New world" in the browser (#1181): the reset must delete exactly the world blob, leave the
/// cloud-version meta alone, and arm the marker that keeps the save migration and the cloud fetch from
/// restoring the old world — until the host clears it once the fresh world is persisted.
/// </summary>
public sealed class BrowserWorldResetTests : IDisposable
{
    private const string Blob = "world.blob";

    private readonly string _saveDir = Path.Combine(Path.GetTempPath(), "bbs-reset-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_saveDir, recursive: true);
        }
        catch (IOException)
        {
            // temp cleanup is best effort
        }
    }

    [Fact]
    public void Reset_DeletesTheBlobKeepsTheMetaAndArmsTheMarker()
    {
        Directory.CreateDirectory(_saveDir);
        File.WriteAllText(Path.Combine(_saveDir, Blob), "world");
        File.WriteAllText(Path.Combine(_saveDir, Blob + ".tmp"), "half-written");
        File.WriteAllText(Path.Combine(_saveDir, "cloud.meta.json"), "{\"version\":7}");
        Assert.False(BrowserWorldReset.IsPending(_saveDir));

        bool deleted = BrowserWorldReset.Reset(_saveDir, Blob);

        Assert.True(deleted);
        Assert.False(File.Exists(Path.Combine(_saveDir, Blob)));
        Assert.False(File.Exists(Path.Combine(_saveDir, Blob + ".tmp")));
        Assert.Equal("{\"version\":7}", File.ReadAllText(Path.Combine(_saveDir, "cloud.meta.json")));
        Assert.True(BrowserWorldReset.IsPending(_saveDir));
    }

    [Fact]
    public void Reset_WithoutAWorld_StillArmsTheMarkerAndReportsNothingDeleted()
    {
        // No local blob (fresh browser, or a cloud-only world): the marker is what keeps the cloud copy
        // from coming back at boot, so it must be armed regardless.
        bool deleted = BrowserWorldReset.Reset(_saveDir, Blob);

        Assert.False(deleted);
        Assert.True(BrowserWorldReset.IsPending(_saveDir));
    }

    [Fact]
    public void ClearPending_DisarmsTheMarkerAndIsIdempotent()
    {
        BrowserWorldReset.Reset(_saveDir, Blob);
        Assert.True(BrowserWorldReset.IsPending(_saveDir));

        BrowserWorldReset.ClearPending(_saveDir);
        Assert.False(BrowserWorldReset.IsPending(_saveDir));

        BrowserWorldReset.ClearPending(_saveDir); // nothing to clear — must not throw
        Assert.False(BrowserWorldReset.IsPending(_saveDir));
    }

    [Fact]
    public void IsPending_OnAMissingFolder_IsFalse()
    {
        Assert.False(BrowserWorldReset.IsPending(Path.Combine(_saveDir, "does-not-exist")));
    }
}
