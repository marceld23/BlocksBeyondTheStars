// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using Npgsql;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3508: the world's report list (<c>player_report</c>) round-trips in every repository — SQLite (a reopened file), the
/// in-memory one of the browser build (through its snapshot blob) and PostgreSQL (opt-in, like
/// <see cref="PostgreSqlRepositoryTests"/>: set <c>BBS_POSTGRES_TEST_CONNECTION_STRING</c>).
/// </summary>
public sealed class PlayerReportPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_playerreport_" + Guid.NewGuid().ToString("N"));

    private static StoredPlayerReport Sample(string kind, string note) => new()
    {
        Kind = kind,
        ReporterId = "Justus",
        ReporterName = "Justus",
        TargetId = "Jonah",
        TargetName = "Jonah",
        Note = note,
        Planet = "sys0-p1",
        HasPosition = true,
        X = 10.5f,
        Y = 64f,
        Z = -3.25f,
        Evidence = "first line\nsecond line",
        CreatedUnix = 1_790_000_000,
    };

    /// <summary>Adds two rows, closes one done and reads them back through <paramref name="reopen"/>.</summary>
    private static void AssertRoundTrip(IWorldRepository repo, Func<IWorldRepository> reopen)
    {
        long first = repo.AddPlayerReport(Sample("player", "he broke my house"));
        long second = repo.AddPlayerReport(Sample("help", "I am stuck"));
        Assert.True(second > first);
        repo.SetPlayerReportState(first, "done", "Owner", 1_790_000_100);
        repo.SetPlayerReportState(999_999, "done", "Owner", 1); // unknown ids are ignored

        var rows = reopen().ListPlayerReports();

        Assert.Equal(2, rows.Count);
        var a = rows[0];
        Assert.Equal(first, a.Id);
        Assert.Equal("player", a.Kind);
        Assert.Equal("he broke my house", a.Note);
        Assert.Equal("Jonah", a.TargetName);
        Assert.True(a.HasPosition);
        Assert.Equal(10.5f, a.X);
        Assert.Equal(-3.25f, a.Z);
        Assert.Equal("first line\nsecond line", a.Evidence);
        Assert.Equal("done", a.State);
        Assert.Equal("Owner", a.HandledBy);
        Assert.Equal(1_790_000_100, a.HandledUnix);
        Assert.Equal("open", rows[1].State);
        Assert.Equal("I am stuck", rows[1].Note);
    }

    [Fact]
    public void Sqlite_RoundTripsTheReportList_AcrossAReopen()
    {
        var paths = new SaveGamePaths(_root, "sqlite");
        var repo = new SqliteWorldRepository(paths);
        repo.Initialize();
        SqliteWorldRepository? reopened = null;
        try
        {
            AssertRoundTrip(repo, () =>
            {
                repo.Dispose();
                reopened = new SqliteWorldRepository(paths);
                reopened.Initialize();
                return reopened;
            });
        }
        finally
        {
            repo.Dispose();
            reopened?.Dispose();
        }
    }

    [Fact]
    public void Memory_RoundTripsTheReportList_ThroughTheSnapshotBlob()
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "mem"));
        repo.Initialize();

        AssertRoundTrip(repo, () =>
        {
            var restored = new MemoryWorldRepository(new SaveGamePaths(_root, "mem2"));
            restored.Initialize();
            restored.ImportSnapshotBlob(repo.ExportSnapshotBlob());
            return restored;
        });
    }

    [Fact]
    public void PostgreSql_RoundTripsTheReportList()
    {
        string? connectionString = Environment.GetEnvironmentVariable("BBS_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // opt-in, like PostgreSqlRepositoryTests
        }

        string world = "pg_" + Guid.NewGuid().ToString("N");
        var paths = new SaveGamePaths(_root, world);
        using var repo = new PostgreSqlWorldRepository(paths, connectionString);
        try
        {
            repo.Initialize();
            AssertRoundTrip(repo, () => repo);
        }
        finally
        {
            try
            {
                // The repository's schema for a lower-case alphanumeric world name (see PostgreSqlRepositoryTests.SchemaNameFor).
                using var connection = new NpgsqlConnection(connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DROP SCHEMA IF EXISTS \"bbs_" + world + "\" CASCADE;";
                cmd.ExecuteNonQuery();
            }
            catch (NpgsqlException)
            {
                // Best-effort cleanup; the test world schema is unique and harmless if left behind.
            }
        }
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
        catch (IOException)
        {
            // A test host still holding the file is not a test failure.
        }
    }
}
