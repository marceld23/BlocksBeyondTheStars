// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1525: the browser host asks <see cref="MemoryWorldRepository.ExportSnapshotBlob"/> for the save blob
/// every two minutes and on every tab hide. A world nobody touched since the last export (or since the
/// load) must hand back the previous blob instead of re-serializing and re-gzipping tens of thousands of
/// rows on the render thread; any mutation invalidates it.
/// </summary>
public sealed class MemorySnapshotBlobTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbs-blob-" + Guid.NewGuid().ToString("N"));

    private MemoryWorldRepository NewRepo()
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "world_001"));
        repo.Initialize();
        return repo;
    }

    [Fact]
    public void Export_ReusesTheBlob_WhileNothingChanged()
    {
        using var repo = NewRepo();
        repo.SetBlock("terra", new Vector3i(1, 64, 2), 7);

        byte[] first = repo.ExportSnapshotBlob();
        byte[] second = repo.ExportSnapshotBlob();

        Assert.Same(first, second);
    }

    [Fact]
    public void Export_RebuildsAfterAMutation_AndTheNewBlobRoundTrips()
    {
        using var repo = NewRepo();
        repo.SetBlock("terra", new Vector3i(1, 64, 2), 7);
        byte[] before = repo.ExportSnapshotBlob();

        repo.SetBlock("terra", new Vector3i(3, 64, 4), 9);
        byte[] after = repo.ExportSnapshotBlob();

        Assert.NotSame(before, after);
        Assert.NotEqual(before, after);

        using var fresh = NewRepo();
        fresh.ImportSnapshotBlob(after);
        Assert.Equal(after, fresh.ExportSnapshotBlob()); // same content, and see the next test: same instance
    }

    [Fact]
    public void Import_LeavesTheRepositoryClean_SoTheFirstExportIsTheImportedBlob()
    {
        using var source = NewRepo();
        source.SetBlock("terra", new Vector3i(1, 64, 2), 7);
        byte[] blob = source.ExportSnapshotBlob();

        using var loaded = NewRepo();
        loaded.ImportSnapshotBlob(blob);

        Assert.Same(blob, loaded.ExportSnapshotBlob());

        loaded.DeleteBlockEdits("terra", new Vector3i(0, 0, 0), new Vector3i(8, 128, 8));
        Assert.NotSame(blob, loaded.ExportSnapshotBlob());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
