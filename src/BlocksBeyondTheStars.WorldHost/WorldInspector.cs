// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>One player as the world save knows them (issue #489).</summary>
public sealed record InspectedPlayer(string Name, string Role, string Body, int X, int Y, int Z, string LastSeenUtc);

/// <summary>One named player structure: a base, beacon, beam pad or space station.</summary>
public sealed record InspectedBuild(string Kind, string Name, string Owner, string Body, int X, int Y, int Z);

/// <summary>A cluster of block edits — building activity that has no registry row of its own (an unnamed
/// house, a mine, a dug-out hillside). <see cref="LastEditor"/>/<see cref="LastEditUtc"/> are empty/null for
/// cells edited before attribution existed (issue #490 cannot be back-filled).</summary>
public sealed record InspectedHotspot(string Body, int X, int Z, int Edits, string LastEditor, DateTime? LastEditUtc);

/// <summary>Everything the world-detail page shows, plus how stale it might be.</summary>
public sealed record WorldInsight(
    IReadOnlyList<InspectedPlayer> Players,
    IReadOnlyList<InspectedBuild> Builds,
    IReadOnlyList<InspectedHotspot> Hotspots,
    DateTime? SaveModifiedUtc,
    string? Problem);

/// <summary>
/// Reads a world save directly for the operator's world-detail page.
///
/// <para>This works because world saves are bind-mounted from a host directory the WorldHost can reach
/// (<see cref="SavePaths.WorldDbPath"/>) — so no protocol message, no instance endpoint and no game-server
/// change are needed to answer "who plays here and what have they built".</para>
///
/// <para>Two deliberate constraints. First, the DB is opened <b>read-only</b>: a running instance owns it, and
/// the panel must never be able to corrupt a live world. Second, the data is therefore as fresh as the last
/// autosave (5 minutes by default), which the page states plainly rather than pretending to be live.</para>
///
/// <para>The player rows are JSON blobs written by the game server's <c>PlayerSnapshot</c>. This project
/// deliberately does not reference Persistence to read them — the operator panel wants four fields, not the
/// whole savegame model, and a local DTO keeps a UI concern from pinning the persistence layer's shape.</para>
/// </summary>
public static class WorldInspector
{
    /// <summary>Hotspot bucket size in blocks. 32 = two chunks across: big enough that one house is a single
    /// row rather than forty, small enough that two neighbouring bases don't merge into one blob.</summary>
    private const int HotspotBucket = 32;

    private const int MaxHotspots = 50;

    /// <summary>Minimum edits before a cluster is worth showing — below this it is someone digging a few
    /// blocks of dirt, and the list would be pages of noise.</summary>
    private const int MinHotspotEdits = 25;

    private sealed class PlayerBlob
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public string CurrentLocationId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string LastSeenUtc { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions BlobOptions = new() { PropertyNameCaseInsensitive = true };

    public static WorldInsight Read(WorldHostConfig config, string worldId)
    {
        string path = SavePaths.WorldDbPath(config, worldId);
        if (!File.Exists(path))
        {
            // A world that has never been started has no save yet — an empty state, not an error.
            return new WorldInsight(
                Array.Empty<InspectedPlayer>(), Array.Empty<InspectedBuild>(), Array.Empty<InspectedHotspot>(),
                null, "This world has no save file yet — it has never been started.");
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
            };

            using var con = new SqliteConnection(builder.ToString());
            con.Open();

            return new WorldInsight(
                ReadPlayers(con),
                ReadBuilds(con),
                ReadHotspots(con),
                File.GetLastWriteTimeUtc(path),
                null);
        }
        catch (Exception e)
        {
            // A world mid-write, a schema from a much older build, a locked file — the panel degrades to a
            // message instead of a 500. The operator's other tools (logs, restart) must stay reachable.
            return new WorldInsight(
                Array.Empty<InspectedPlayer>(), Array.Empty<InspectedBuild>(), Array.Empty<InspectedHotspot>(),
                null, "Could not read the world save: " + e.Message);
        }
    }

    private static List<InspectedPlayer> ReadPlayers(SqliteConnection con)
    {
        var result = new List<InspectedPlayer>();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT json FROM player;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            PlayerBlob? blob;
            try
            {
                blob = JsonSerializer.Deserialize<PlayerBlob>(reader.GetString(0), BlobOptions);
            }
            catch (JsonException)
            {
                continue; // one unreadable row must not lose the whole list
            }

            if (blob is null)
            {
                continue;
            }

            result.Add(new InspectedPlayer(
                string.IsNullOrEmpty(blob.Name) ? blob.PlayerId : blob.Name,
                string.IsNullOrEmpty(blob.Role) ? "Player" : blob.Role,
                blob.CurrentLocationId,
                (int)blob.X, (int)blob.Y, (int)blob.Z,
                blob.LastSeenUtc));
        }

        return result
            .OrderByDescending(p => p.LastSeenUtc, StringComparer.Ordinal)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<InspectedBuild> ReadBuilds(SqliteConnection con)
    {
        var result = new List<InspectedBuild>();

        Collect("base_claim", "base", "name");
        Collect("beacon", "beacon", "label");
        Collect("beam", "beam", "name");

        // Stations live in flight-space coordinates (REAL), not block cells.
        try
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT name, owner, location, px, py, pz FROM space_structure;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new InspectedBuild(
                    "station", reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    (int)reader.GetDouble(3), (int)reader.GetDouble(4), (int)reader.GetDouble(5)));
            }
        }
        catch (SqliteException)
        {
            // Older save without the table — skip that category rather than failing the page.
        }

        return result
            .OrderBy(b => b.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Kind, StringComparer.Ordinal)
            .ToList();

        void Collect(string table, string kind, string nameColumn)
        {
            try
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = $"SELECT {nameColumn}, owner, planet, x, y, z FROM {table};";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new InspectedBuild(
                        kind, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
                }
            }
            catch (SqliteException)
            {
            }
        }
    }

    /// <summary>Clusters block edits into buckets so unnamed building activity is findable. This is the only
    /// way to spot a house someone built without placing a base core — and the reason the whole page is worth
    /// having: <c>block_edit</c> is keyed by cell, so COUNT(*) per bucket is literally "how much was built or
    /// dug here".</summary>
    private static List<InspectedHotspot> ReadHotspots(SqliteConnection con)
    {
        var result = new List<InspectedHotspot>();
        bool attributed = HasColumn(con, "block_edit", "owner_id");

        using var cmd = con.CreateCommand();

        // Integer division in SQLite truncates toward zero, which would fold x=-31 and x=+31 onto bucket 0 and
        // put two places on opposite sides of the origin in one row. CAST(FLOOR(...)) keeps the buckets uniform.
        const string bucketX = "CAST(FLOOR(CAST(x AS REAL) / $bucket) AS INTEGER)";
        const string bucketZ = "CAST(FLOOR(CAST(z AS REAL) / $bucket) AS INTEGER)";

        cmd.CommandText = attributed
            ? $@"SELECT planet, {bucketX} AS bx, {bucketZ} AS bz, COUNT(*) AS n,
                        COALESCE((SELECT r.name FROM player_ref r WHERE r.id = MAX(e.owner_id)), ''),
                        MAX(e.edited_unix)
                 FROM block_edit e GROUP BY planet, bx, bz
                 HAVING n >= $min ORDER BY n DESC LIMIT $limit;"
            : $@"SELECT planet, {bucketX} AS bx, {bucketZ} AS bz, COUNT(*) AS n, '', 0
                 FROM block_edit GROUP BY planet, bx, bz
                 HAVING n >= $min ORDER BY n DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$bucket", HotspotBucket);
        cmd.Parameters.AddWithValue("$min", MinHotspotEdits);
        cmd.Parameters.AddWithValue("$limit", MaxHotspots);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long unix = reader.GetInt64(5);
            result.Add(new InspectedHotspot(
                reader.GetString(0),
                reader.GetInt32(1) * HotspotBucket + (HotspotBucket / 2), // bucket centre reads better as a jump target
                reader.GetInt32(2) * HotspotBucket + (HotspotBucket / 2),
                reader.GetInt32(3),
                reader.GetString(4),
                unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null));
        }

        return result;
    }

    /// <summary>Whether a table has a column — the attribution columns (issue #490) are absent in saves written
    /// by older builds, and the hotspot query has to degrade instead of throwing.</summary>
    private static bool HasColumn(SqliteConnection con, string table, string column)
    {
        try
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (SqliteException)
        {
        }

        return false;
    }
}
