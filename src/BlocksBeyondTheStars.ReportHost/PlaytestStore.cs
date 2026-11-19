// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text;
using BlocksBeyondTheStars.Shared.Playtests;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>One stored playtest task: the task as its author wrote it (validated, normalised JSON) and when it
/// last changed.</summary>
public sealed record PlaytestTaskRecord(string Id, int Rev, string Json, long UpdatedUnix)
{
    /// <summary>The task read as written — what the admin form and the sync tool work on.</summary>
    public PlaytestTask Task()
    {
        PlaytestCatalogJson.TryReadTask(Json, out var task, PlaytestReadMode.AsWritten);
        return task;
    }
}

/// <summary>What <see cref="PlaytestStore.Save"/> did.</summary>
public enum PlaytestSaveResult
{
    /// <summary>Stored.</summary>
    Saved,

    /// <summary>Refused: the stored task has a higher revision than the one offered (someone changed it here since
    /// the author last fetched it).</summary>
    StaleRev,

    /// <summary>Refused: the id is one of the route words (<see cref="PlaytestStore.ReservedIds"/>).</summary>
    ReservedId,

    /// <summary>Refused: the store is full (<see cref="PlaytestStore.MaxStoredTasks"/>).</summary>
    TooMany,
}

/// <summary>
/// The playtest catalogue's storage: the tasks the game clients fetch, in their own small SQLite file
/// (<c>playtests.db</c>) next to the reports. It is deliberately separate from <see cref="ReportStore"/> — tasks are
/// authored content, reports are player data with their own retention — and tiny: a few dozen rows, written by one
/// operator. The served catalogue is built once per change and cached with its ETag, because every client asks for
/// it and almost every answer is "not modified".
/// </summary>
public sealed class PlaytestStore : IDisposable
{
    /// <summary>How many tasks may be stored (switched-off ones included). The served catalogue is cut to
    /// <see cref="PlaytestLimits.MaxTasks"/>.</summary>
    public const int MaxStoredTasks = 200;

    /// <summary>Ids a task cannot have: they are words of the route tables under <c>/api/playtests/</c> and
    /// <c>/admin/playtests/</c>.</summary>
    public static readonly IReadOnlyList<string> ReservedIds = new[] { "export", "mine", "enabled", "new", "save", "switch" };

    private const string EnabledKey = "enabled";

    private readonly Lock _gate = new();
    private readonly SqliteConnection _db;
    private (string Json, string ETag)? _served;

    public PlaytestStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Unpooled, like ReportStore (#2768): one connection for the store's whole life.
        _db = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS playtest_task(
                id TEXT PRIMARY KEY,
                rev INTEGER NOT NULL DEFAULT 1,
                json TEXT NOT NULL DEFAULT '{}',
                updated_unix INTEGER NOT NULL DEFAULT 0);
            """);
        Exec("CREATE TABLE IF NOT EXISTS playtest_meta(key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '');");
    }

    /// <summary>The catalogue's kill switch: false makes every client show nothing. On by default.</summary>
    public bool CatalogEnabled
    {
        get
        {
            lock (_gate)
            {
                return ReadEnabledLocked();
            }
        }
    }

    /// <summary>Flips the kill switch.</summary>
    public void SetCatalogEnabled(bool enabled)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO playtest_meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
            cmd.Parameters.AddWithValue("$k", EnabledKey);
            cmd.Parameters.AddWithValue("$v", enabled ? "1" : "0");
            cmd.ExecuteNonQuery();
            _served = null;
        }
    }

    /// <summary>Every stored task (switched-off ones included), in display order.</summary>
    public IReadOnlyList<PlaytestTaskRecord> All()
    {
        lock (_gate)
        {
            return AllLocked();
        }
    }

    /// <summary>One stored task, or null.</summary>
    public PlaytestTaskRecord? Get(string id)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id, rev, json, updated_unix FROM playtest_task WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }
    }

    /// <summary>
    /// Stores <paramref name="task"/> under its id — a new task or the next state of an existing one. The caller has
    /// validated it (<see cref="PlaytestCatalogValidator"/>); the store only keeps the bookkeeping rules: a reserved
    /// id, a full store, and — unless <paramref name="force"/> — a revision lower than the stored one are refused.
    /// </summary>
    public PlaytestSaveResult Save(PlaytestTask task, long nowUnix, bool force = false)
    {
        if (ReservedIds.Contains(task.Id))
        {
            return PlaytestSaveResult.ReservedId;
        }

        lock (_gate)
        {
            int storedRev = 0;
            using (var find = _db.CreateCommand())
            {
                find.CommandText = "SELECT rev FROM playtest_task WHERE id = $id;";
                find.Parameters.AddWithValue("$id", task.Id);
                if (find.ExecuteScalar() is long rev)
                {
                    storedRev = (int)rev;
                }
            }

            if (storedRev == 0 && CountLocked() >= MaxStoredTasks)
            {
                return PlaytestSaveResult.TooMany;
            }

            if (!force && task.Rev < storedRev)
            {
                return PlaytestSaveResult.StaleRev;
            }

            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO playtest_task(id, rev, json, updated_unix) VALUES($id, $rev, $json, $now)
                ON CONFLICT(id) DO UPDATE SET rev = $rev, json = $json, updated_unix = $now;
                """;
            cmd.Parameters.AddWithValue("$id", task.Id);
            cmd.Parameters.AddWithValue("$rev", task.Rev);
            cmd.Parameters.AddWithValue("$json", PlaytestCatalogJson.WriteTask(task));
            cmd.Parameters.AddWithValue("$now", nowUnix);
            cmd.ExecuteNonQuery();
            _served = null;
            return PlaytestSaveResult.Saved;
        }
    }

    /// <summary>Removes a task. False when there was none.</summary>
    public bool Delete(string id)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM playtest_task WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            bool removed = cmd.ExecuteNonQuery() > 0;
            if (removed)
            {
                _served = null;
            }

            return removed;
        }
    }

    /// <summary>
    /// The catalogue as the game fetches it, with its ETag: the kill switch and the switched-on tasks, each read the
    /// SAFE way (cleaned and cut — what is stored was validated, but the bounds are applied once more on the way
    /// out), in display order, at most <see cref="PlaytestLimits.MaxTasks"/>.
    /// </summary>
    public (string Json, string ETag) Served()
    {
        lock (_gate)
        {
            if (_served is { } cached)
            {
                return cached;
            }

            var catalog = new PlaytestCatalog { Enabled = ReadEnabledLocked() };
            foreach (var record in AllLocked())
            {
                if (catalog.Tasks.Count >= PlaytestLimits.MaxTasks)
                {
                    break;
                }

                if (PlaytestCatalogJson.TryReadTask(record.Json, out var task) && task.Enabled && task.Id.Length > 0 && task.Steps.Count > 0)
                {
                    catalog.Tasks.Add(task);
                }
            }

            string json = PlaytestCatalogJson.WriteCatalog(catalog);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            var served = (json, "\"" + Convert.ToHexStringLower(hash.AsSpan(0, 8)) + "\"");
            _served = served;
            return served;
        }
    }

    public void Dispose() => _db.Dispose();

    private List<PlaytestTaskRecord> AllLocked()
    {
        var records = new List<PlaytestTaskRecord>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, rev, json, updated_unix FROM playtest_task;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                records.Add(Read(reader));
            }
        }

        // The order lives in the task (its "sort"), so it is read from there — a few dozen rows at most.
        return records
            .Select(r => (Record: r, r.Task().Sort))
            .OrderBy(x => x.Sort)
            .ThenBy(x => x.Record.Id, StringComparer.Ordinal)
            .Select(x => x.Record)
            .ToList();
    }

    private int CountLocked()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM playtest_task;";
        return (int)(long)cmd.ExecuteScalar()!;
    }

    private bool ReadEnabledLocked()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT value FROM playtest_meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", EnabledKey);
        return cmd.ExecuteScalar() is not string value || value != "0";
    }

    private static PlaytestTaskRecord Read(SqliteDataReader reader)
        => new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt64(3));

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
