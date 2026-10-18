// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2768: <c>SqliteConnection.ClearAllPools()</c> clears the connection pools of the WHOLE process, and a pooled connection
/// that another thread is just opening can lose its native handle to it — the flaky
/// <c>ObjectDisposedException: 'SQLitePCL.sqlite3'</c> on the first statement after <c>Open()</c> (CI saw it in
/// <c>SqliteWorldRepository.Initialize</c> while ~370 test cleanups clear every pool). Measured with Microsoft.Data.Sqlite
/// 10.0.9: 126 failures in 43 342 pooled opens beside a clearing thread, none in 11 291 unpooled ones. An unpooled connection
/// never enters a pool, so no clear anywhere can reach it — hence the two rules pinned here. Every connection in our code
/// lives long anyway (the repository, the WorldHost registry and the report store keep one open) or is a one-shot read.
/// </summary>
public sealed class SqlitePoolingGuardTests
{
    /// <summary>Lines above a <c>new SqliteConnection(</c> in which its connection-string builder may say <c>Pooling = false</c>.</summary>
    private const int BuilderLookBack = 12;

    private static readonly Regex NewConnection = new(@"new\s+SqliteConnection\s*\(", RegexOptions.None, TimeSpan.FromSeconds(5));
    private static readonly Regex PoolingOff = new(@"Pooling\s*=\s*false", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
    private static readonly Regex ClearAll = new(@"(?:Sqlite|Npgsql)Connection\s*\.\s*ClearAllPools\s*\(\s*\)\s*;", RegexOptions.None, TimeSpan.FromSeconds(5));

    [Fact]
    public void EverySqliteConnection_InTheServerAndItsTests_IsOpenedUnpooled()
    {
        var pooled = new List<string>();
        int seen = 0;
        foreach (string file in SourceFiles("src").Concat(SourceFiles(Path.Combine("tests", "BlocksBeyondTheStars.Tests"))))
        {
            if (Path.GetFileName(file) == nameof(SqlitePoolingGuardTests) + ".cs")
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!NewConnection.IsMatch(lines[i]))
                {
                    continue;
                }

                seen++;
                int from = Math.Max(0, i - BuilderLookBack);
                string context = string.Join('\n', lines[from..(i + 1)]);
                if (!PoolingOff.IsMatch(context))
                {
                    pooled.Add($"{Path.GetRelativePath(TestPaths.RepoRoot(), file)}:{i + 1}");
                }
            }
        }

        Assert.True(seen > 10, $"only {seen} SqliteConnection constructions found — did the scan miss the sources?");
        Assert.True(pooled.Count == 0,
            "Open these SQLite connections with Pooling=False (#2768) — a pooled handle can be disposed by a " +
            "ClearAllPools() anywhere in the process:\n" + string.Join('\n', pooled));
    }

    [Fact]
    public void ProductionCode_NeverClearsEveryConnectionPool()
    {
        var offenders = SourceFiles("src")
            .Where(file => ClearAll.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(TestPaths.RepoRoot(), file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "ClearAllPools() is process-wide (SQLite #2768, PostgreSQL #2786) — it reaches every other repository's " +
            "connections; dispose your own connection instead:\n" + string.Join('\n', offenders));
    }

    private static IEnumerable<string> SourceFiles(string relativeDir)
    {
        string root = Path.Combine(TestPaths.RepoRoot(), relativeDir);
        char sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}artifacts{sep}", StringComparison.Ordinal));
    }
}
