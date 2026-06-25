// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Persistence;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>BugReportPaths sends /bump reports to the repo when the server runs inside the working tree,
/// and to the per-world fallback otherwise.</summary>
public sealed class BugReportPathsTests : IDisposable
{
    private readonly string _root;

    public BugReportPathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bugpath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void FindsRepositoryRoot_ByGitDirectory()
    {
        string repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string deep = Path.Combine(repo, "client", "Build", "Windows", "Data", "server");
        Directory.CreateDirectory(deep);

        Assert.Equal(Path.GetFullPath(repo), BugReportPaths.FindRepositoryRoot(deep));
    }

    [Fact]
    public void FindsRepositoryRoot_BySolutionFile()
    {
        string repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, "BlocksBeyondTheStars.sln"), "// marker");
        string deep = Path.Combine(repo, "a", "b", "c");
        Directory.CreateDirectory(deep);

        Assert.Equal(Path.GetFullPath(repo), BugReportPaths.FindRepositoryRoot(deep));
    }

    [Fact]
    public void Resolve_UsesRepoBugreports_WhenInsideRepo()
    {
        string repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        string start = Path.Combine(repo, "sub", "dir");
        Directory.CreateDirectory(start);

        string fallback = Path.Combine(_root, "world", "bumps");
        string resolved = BugReportPaths.Resolve(fallback, start);

        Assert.Equal(Path.Combine(repo, "bugreports", "server"), resolved);
    }

    [Fact]
    public void Resolve_UsesFallback_WhenNotInRepo()
    {
        // A bare temp directory with no .git / .sln in any ancestor (within the search bound).
        string start = Path.Combine(_root, "loose", "dir");
        Directory.CreateDirectory(start);

        string fallback = Path.Combine(_root, "world", "bumps");
        Assert.Equal(fallback, BugReportPaths.Resolve(fallback, start));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
