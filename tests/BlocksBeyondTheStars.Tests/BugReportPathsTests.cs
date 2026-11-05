// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Persistence;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>BugReportPaths sends /bump and crash reports to the repo when the server runs inside the game's own source
/// checkout (marked by <c>BlocksBeyondTheStars.sln</c>), and to the per-world fallback otherwise — also below somebody
/// else's git working tree (#3223).</summary>
public sealed class BugReportPathsTests : IDisposable
{
    private readonly string _root;

    public BugReportPathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bugpath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>A folder with a <c>.git</c> entry (a clone's directory or a worktree's file) and, when asked, the solution.</summary>
    private string Repo(bool gitFile = false, bool solution = true)
    {
        string repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(repo);
        if (gitFile)
        {
            File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: elsewhere");
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
        }

        if (solution)
        {
            File.WriteAllText(Path.Combine(repo, BugReportPaths.RepositoryMarker), "// marker");
        }

        return repo;
    }

    [Theory]
    [InlineData(false)] // a clone: .git is a directory
    [InlineData(true)]  // a worktree: .git is a file
    public void FindsRepositoryRoot_ByTheSolutionFile(bool gitFile)
    {
        string repo = Repo(gitFile);
        string deep = Path.Combine(repo, "client", "Build", "Windows", "Data", "server");
        Directory.CreateDirectory(deep);

        Assert.Equal(Path.GetFullPath(repo), BugReportPaths.FindRepositoryRoot(deep));
    }

    /// <summary>#3223: somebody else's git working tree — a dotfiles home, a project folder the portable zip was unpacked
    /// in — is no checkout of the game.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGitFolder_WithoutTheSolution_IsNoRepositoryRoot(bool gitFile)
    {
        string foreign = Repo(gitFile, solution: false);
        string install = Path.Combine(foreign, "games", "BlocksBeyondTheStars", "server");
        Directory.CreateDirectory(install);

        Assert.Null(BugReportPaths.FindRepositoryRoot(install));
        string bumps = Path.Combine(_root, "world", "bumps");
        string crashes = Path.Combine(_root, "world", "crashes");
        Assert.Equal(bumps, BugReportPaths.Resolve(bumps, install));          // the reports stay with the world
        Assert.Equal(crashes, BugReportPaths.ResolveCrashes(crashes, install));
    }

    /// <summary>The developer's own checkout — a clone or a worktree, both carry the solution at their root — still resolves.</summary>
    [Fact]
    public void TheCheckoutTheTestsRunIn_IsTheRepositoryRoot()
        => Assert.Equal(TestPaths.RepoRoot(), BugReportPaths.FindRepositoryRoot(AppContext.BaseDirectory));

    [Fact]
    public void Resolve_UsesRepoBugreports_WhenInsideRepo()
    {
        string repo = Repo();
        string start = Path.Combine(repo, "sub", "dir");
        Directory.CreateDirectory(start);

        string fallback = Path.Combine(_root, "world", "bumps");

        Assert.Equal(Path.Combine(repo, "bugreports", "server"), BugReportPaths.Resolve(fallback, start));
        Assert.Equal(Path.Combine(repo, "bugreports", "crashes"), BugReportPaths.ResolveCrashes(fallback, start));
    }

    [Fact]
    public void Resolve_UsesFallback_WhenNotInRepo()
    {
        // A bare temp directory with no .sln in any ancestor (within the search bound).
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
