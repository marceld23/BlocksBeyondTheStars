// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The browser save migration (#1177): a new glitch.fun deployment starts with an empty storage folder
/// while the previous deployment's world still sits in a sibling folder under the same IDBFS mount.
/// The rules under test: the newest sibling copy wins, the current folder is never a candidate, an
/// existing local file is never overwritten, companions ride along, the source is left untouched.
/// </summary>
public sealed class PreviousDeploymentStorageTests : IDisposable
{
    private const string Blob = "browser-singleplayer/world.blob";
    private const string Meta = "browser-singleplayer/cloud.meta.json";

    private readonly string _mount = Path.Combine(Path.GetTempPath(), "bbs-prevdeploy-" + Guid.NewGuid().ToString("N"));
    private readonly string _current;

    public PreviousDeploymentStorageTests()
    {
        _current = Path.Combine(_mount, "current-hash");
        Directory.CreateDirectory(_current);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_mount, recursive: true);
        }
        catch (IOException)
        {
            // temp cleanup is best effort
        }
    }

    private string Sibling(string name, string relativePath, string content, DateTime writtenUtc)
    {
        string root = Path.Combine(_mount, name);
        string file = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        File.SetLastWriteTimeUtc(file, writtenUtc);
        return root;
    }

    [Fact]
    public void FindNewestSiblingRoot_NoSiblings_ReturnsNull()
    {
        Assert.Null(PreviousDeploymentStorage.FindNewestSiblingRoot(_current, Blob));
    }

    [Fact]
    public void FindNewestSiblingRoot_PicksTheNewestCopyNotTheFirstEnumerated()
    {
        var older = Sibling("a-old-deploy", Blob, "old", new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
        var newest = Sibling("m-newest-deploy", Blob, "new", new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        var middle = Sibling("z-middle-deploy", Blob, "mid", new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc));

        string? picked = PreviousDeploymentStorage.FindNewestSiblingRoot(_current, Blob);

        Assert.NotNull(picked);
        Assert.Equal(Path.GetFullPath(newest), Path.GetFullPath(picked));
        Assert.NotEqual(Path.GetFullPath(older), Path.GetFullPath(picked));
        Assert.NotEqual(Path.GetFullPath(middle), Path.GetFullPath(picked));
    }

    [Fact]
    public void FindNewestSiblingRoot_IgnoresTheCurrentRootAndFoldersWithoutTheFile()
    {
        // The current deployment's own (newer) copy must never be "migrated" onto itself, and a sibling
        // without the file (another app's folder, a half-initialised deployment) is not a candidate.
        string own = Path.Combine(_current, Blob);
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "mine");
        File.SetLastWriteTimeUtc(own, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        Directory.CreateDirectory(Path.Combine(_mount, "empty-deploy"));
        var only = Sibling("real-deploy", Blob, "theirs", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        string? picked = PreviousDeploymentStorage.FindNewestSiblingRoot(_current, Blob);

        Assert.NotNull(picked);
        Assert.Equal(Path.GetFullPath(only), Path.GetFullPath(picked));
    }

    [Fact]
    public void TryAdopt_CopiesTheNewestBlobAndItsCompanionAndLeavesTheSourceIntact()
    {
        Sibling("old-deploy", Blob, "old", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = Sibling("new-deploy", Blob, "new-world", new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(newest, Meta), "{\"version\":7}");

        string? source = PreviousDeploymentStorage.TryAdopt(_current, Blob, Meta);

        Assert.NotNull(source);
        Assert.Equal(Path.GetFullPath(newest), Path.GetFullPath(source));
        Assert.Equal("new-world", File.ReadAllText(Path.Combine(_current, Blob)));
        Assert.Equal("{\"version\":7}", File.ReadAllText(Path.Combine(_current, Meta)));
        Assert.True(File.Exists(Path.Combine(newest, Blob)), "the source must stay where it is (never delete the old deployment's copy)");
    }

    [Fact]
    public void TryAdopt_NeverOverwritesAnExistingLocalFile()
    {
        Sibling("new-deploy", Blob, "theirs", new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        string own = Path.Combine(_current, Blob);
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "mine");

        Assert.Null(PreviousDeploymentStorage.TryAdopt(_current, Blob, Meta));
        Assert.Equal("mine", File.ReadAllText(own));
    }

    [Fact]
    public void TryAdopt_MissingCompanionIsNotAnError()
    {
        Sibling("new-deploy", Blob, "world", new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));

        string? source = PreviousDeploymentStorage.TryAdopt(_current, Blob, Meta);

        Assert.NotNull(source);
        Assert.Equal("world", File.ReadAllText(Path.Combine(_current, Blob)));
        Assert.False(File.Exists(Path.Combine(_current, Meta)));
    }

    [Fact]
    public void TryAdopt_NothingToAdopt_ReturnsNullAndCreatesNothing()
    {
        Assert.Null(PreviousDeploymentStorage.TryAdopt(_current, Blob, Meta));
        Assert.False(File.Exists(Path.Combine(_current, Blob)));
    }
}
